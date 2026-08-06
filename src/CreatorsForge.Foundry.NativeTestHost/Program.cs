using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Testing;

namespace CreatorsForge.Foundry.NativeTestHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            return 2;
        }

        var resultPath = Path.GetFullPath(args[1]);
        try
        {
            var request = JsonSerializer.Deserialize<ObsNativeHostRequest>(
                await File.ReadAllTextAsync(Path.GetFullPath(args[0])).ConfigureAwait(false),
                JsonOptions) ?? throw new InvalidDataException("Native test request is empty.");
            if (request.Mode == "self-crash")
            {
                Environment.Exit(unchecked((int)0xC0000005));
            }

            if (request.Mode == "self-hang")
            {
                await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            }

            var result = request.Mode == "self-success"
                ? new ObsNativeHostResult
                {
                    ModuleOpened = true,
                    ModuleLoadSucceeded = true,
                }
                : InspectWithObs(request);
            await WriteAsync(resultPath, result).ConfigureAwait(false);
            return result.Error is null ? 0 : 1;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or InvalidOperationException or Win32Exception or
                EntryPointNotFoundException)
        {
            try
            {
                await WriteAsync(resultPath, new ObsNativeHostResult { Error = exception.Message })
                    .ConfigureAwait(false);
            }
            catch (Exception writeException) when (writeException is IOException or UnauthorizedAccessException)
            {
            }

            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static ObsNativeHostResult InspectWithObs(ObsNativeHostRequest request)
    {
        var binaryDirectory = Path.Combine(Path.GetFullPath(request.ObsRoot), "bin", "64bit");
        var obsPath = Path.Combine(binaryDirectory, "obs.dll");
        if (!File.Exists(obsPath) || !File.Exists(request.PluginPath))
        {
            throw new FileNotFoundException("The OBS runtime or plugin DLL does not exist.");
        }

        if (!SetDllDirectory(binaryDirectory))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var obs = LoadLibrary(obsPath);
        if (obs == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        IntPtr locale = IntPtr.Zero;
        IntPtr pluginPath = IntPtr.Zero;
        IntPtr sourceId = IntPtr.Zero;
        IntPtr sourceName = IntPtr.Zero;
        var started = false;
        try
        {
            var startup = GetDelegate<ObsStartup>(obs, "obs_startup");
            var shutdown = GetDelegate<ObsShutdown>(obs, "obs_shutdown");
            var open = GetDelegate<ObsOpenModule>(obs, "obs_open_module");
            var initialize = GetDelegate<ObsInitModule>(obs, "obs_init_module");
            var enumerate = GetDelegate<ObsEnumSourceTypes>(obs, "obs_enum_source_types");
            var create = GetDelegate<ObsSourceCreate>(obs, "obs_source_create");
            var release = GetDelegate<ObsSourceRelease>(obs, "obs_source_release");
            var getProperties = GetDelegate<ObsSourceProperties>(obs, "obs_source_properties");
            var firstProperty = GetDelegate<ObsPropertiesFirst>(obs, "obs_properties_first");
            var nextProperty = GetDelegate<ObsPropertyNext>(obs, "obs_property_next");
            var propertyName = GetDelegate<ObsPropertyName>(obs, "obs_property_name");
            var propertyDescription = GetDelegate<ObsPropertyDescription>(obs, "obs_property_description");
            var propertyType = GetDelegate<ObsPropertyType>(obs, "obs_property_get_type");
            var destroyProperties = GetDelegate<ObsPropertiesDestroy>(obs, "obs_properties_destroy");
            var queue = GetDelegate<ObsQueueTask>(obs, "obs_queue_task");
            locale = AllocateUtf8("en-US");
            pluginPath = AllocateUtf8(Path.GetFullPath(request.PluginPath));
            started = startup(locale, IntPtr.Zero, IntPtr.Zero);
            if (!started)
            {
                throw new InvalidOperationException("obs_startup returned false.");
            }

            var openResult = open(out var module, pluginPath, IntPtr.Zero);
            var initialized = openResult == 0 && initialize(module);
            var sourceIds = new List<string>();
            if (initialized)
            {
                ulong index = 0;
                while (enumerate(new UIntPtr(index), out var id))
                {
                    sourceIds.Add(Marshal.PtrToStringUTF8(id) ?? string.Empty);
                    index++;
                }
            }

            var lifecycle = initialized && !string.IsNullOrWhiteSpace(request.ExpectedSourceId);
            var created = false;
            var destroyed = false;
            var properties = new List<ObsNativeProperty>();
            if (lifecycle)
            {
                sourceId = AllocateUtf8(request.ExpectedSourceId!);
                sourceName = AllocateUtf8("Creators Forge isolated lifecycle test");
                var source = create(sourceId, sourceName, IntPtr.Zero, IntPtr.Zero);
                if (source == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"obs_source_create returned null for '{request.ExpectedSourceId}'.");
                }

                created = true;
                var propertySet = getProperties(source);
                if (propertySet != IntPtr.Zero)
                {
                    try
                    {
                        var property = firstProperty(propertySet);
                        while (property != IntPtr.Zero && properties.Count < 32)
                        {
                            properties.Add(new(
                                Marshal.PtrToStringUTF8(propertyName(property)) ?? string.Empty,
                                Marshal.PtrToStringUTF8(propertyDescription(property)) ?? string.Empty,
                                propertyType(property)));
                            if (!nextProperty(ref property)) break;
                        }
                    }
                    finally
                    {
                        destroyProperties(propertySet);
                    }
                }
                release(source);
                ObsTask barrier = _ => { };
                queue(3, barrier, IntPtr.Zero, true);
                GC.KeepAlive(barrier);
                destroyed = true;
            }

            shutdown();
            started = false;
            return new()
            {
                ModuleOpened = openResult == 0,
                ModuleLoadSucceeded = initialized,
                RegisteredSourceIds = sourceIds,
                SourceLifecycleAttempted = lifecycle,
                SourceCreated = created,
                SourceDestroyed = destroyed,
                Properties = properties,
            };
        }
        finally
        {
            Free(locale);
            Free(pluginPath);
            Free(sourceId);
            Free(sourceName);
            if (started)
            {
                GetDelegate<ObsShutdown>(obs, "obs_shutdown")();
            }

            _ = FreeLibrary(obs);
            _ = SetDllDirectory(null);
        }
    }

    private static T GetDelegate<T>(IntPtr module, string name) where T : Delegate
    {
        var pointer = GetProcAddress(module, name);
        return pointer == IntPtr.Zero
            ? throw new EntryPointNotFoundException(name)
            : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private static IntPtr AllocateUtf8(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static void Free(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static async Task WriteAsync(string path, ObsNativeHostResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(result, JsonOptions) + "\n",
            new UTF8Encoding(false)).ConfigureAwait(false);
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string? path);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    [SuppressMessage("Security", "CA2101:Specify marshaling for P/Invoke string arguments", Justification = "GetProcAddress requires an ANSI export name by Windows API contract.")]
    private static extern IntPtr GetProcAddress(
        IntPtr module,
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ObsStartup(IntPtr locale, IntPtr moduleConfigPath, IntPtr profilerStore);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsShutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ObsOpenModule(out IntPtr module, IntPtr path, IntPtr dataPath);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ObsInitModule(IntPtr module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ObsEnumSourceTypes(UIntPtr index, out IntPtr id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ObsSourceCreate(IntPtr id, IntPtr name, IntPtr settings, IntPtr hotkeyData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsSourceRelease(IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ObsSourceProperties(IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ObsPropertiesFirst(IntPtr properties);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ObsPropertyNext(ref IntPtr property);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ObsPropertyName(IntPtr property);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ObsPropertyDescription(IntPtr property);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ObsPropertyType(IntPtr property);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsPropertiesDestroy(IntPtr properties);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsTask(IntPtr parameter);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsQueueTask(
        int type,
        ObsTask task,
        IntPtr parameter,
        [MarshalAs(UnmanagedType.I1)] bool wait);
}
