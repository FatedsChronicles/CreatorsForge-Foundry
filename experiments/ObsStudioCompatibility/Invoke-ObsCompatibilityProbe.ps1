[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PluginPath,

    [Parameter(Mandatory)]
    [string] $ObsRoot,

    [string] $ReportPath,

    [string] $ExpectedSourceId
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedPlugin = (Resolve-Path -LiteralPath $PluginPath).Path
$resolvedObsRoot = (Resolve-Path -LiteralPath $ObsRoot).Path
$obsExecutable = Join-Path $resolvedObsRoot "bin\64bit\obs64.exe"
if (-not (Test-Path -LiteralPath $obsExecutable -PathType Leaf)) {
    throw "The selected directory does not contain bin\64bit\obs64.exe."
}

$obsVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($obsExecutable).FileVersion
if ([version]$obsVersion -lt [version]"32.0.0" -or [version]$obsVersion -ge [version]"33.0.0") {
    throw "OBS $obsVersion is outside the verified 32.x-windows-x64 profile."
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class FoundryObsNativeProbe
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string path);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate UInt32 ModuleVersion();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ModuleLoad();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ObsOpenModule(out IntPtr module, IntPtr path, IntPtr dataPath);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ObsInitModule(IntPtr module);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ObsStartup(IntPtr locale, IntPtr moduleConfigPath, IntPtr profilerStore);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsShutdown();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool ObsEnumSourceTypes(UIntPtr index, out IntPtr id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ObsSourceCreate(IntPtr id, IntPtr name, IntPtr settings, IntPtr hotkeyData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsSourceRelease(IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsTask(IntPtr parameter);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ObsQueueTask(
        int type,
        ObsTask task,
        IntPtr parameter,
        [MarshalAs(UnmanagedType.I1)] bool wait);

    public static IDictionary<string, object> Inspect(string path, string dependencyDirectory, bool invokeLoadCallback)
    {
        if (!String.IsNullOrEmpty(dependencyDirectory) && !SetDllDirectory(dependencyDirectory))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var handle = LoadLibrary(path);
        if (handle == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            string[] required = { "obs_module_ver", "obs_module_set_pointer", "obs_module_load" };
            foreach (var name in required)
                if (GetProcAddress(handle, name) == IntPtr.Zero)
                    throw new EntryPointNotFoundException(name);

            var versionFunction = (ModuleVersion)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(handle, "obs_module_ver"), typeof(ModuleVersion));
            var version = versionFunction();
            var result = new Dictionary<string, object>();
            result["moduleApiEncoded"] = version;
            result["moduleApiVersion"] = String.Format("{0}.{1}.{2}", version >> 24, (version >> 16) & 255, version & 65535);
            if (invokeLoadCallback)
            {
                var loadFunction = (ModuleLoad)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(handle, "obs_module_load"), typeof(ModuleLoad));
                result["loadCallbackSucceeded"] = loadFunction();
            }
            else
            {
                result["loadCallbackSucceeded"] = null;
            }
            result["requiredExports"] = required;
            return result;
        }
        finally
        {
            FreeLibrary(handle);
            SetDllDirectory(null);
        }
    }

    public static IDictionary<string, object> InspectWithObs(
        string obsBinaryDirectory,
        string pluginPath,
        string expectedSourceId)
    {
        if (!SetDllDirectory(obsBinaryDirectory))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var obs = LoadLibrary(System.IO.Path.Combine(obsBinaryDirectory, "obs.dll"));
        if (obs == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        IntPtr utf8Path = IntPtr.Zero;
        IntPtr utf8Locale = IntPtr.Zero;
        IntPtr utf8SourceId = IntPtr.Zero;
        IntPtr utf8SourceName = IntPtr.Zero;
        IntPtr module = IntPtr.Zero;
        bool started = false;
        try
        {
            var startup = (ObsStartup)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_startup"), typeof(ObsStartup));
            var shutdown = (ObsShutdown)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_shutdown"), typeof(ObsShutdown));
            var open = (ObsOpenModule)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_open_module"), typeof(ObsOpenModule));
            var initialize = (ObsInitModule)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_init_module"), typeof(ObsInitModule));
            var enumerate = (ObsEnumSourceTypes)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_enum_source_types"), typeof(ObsEnumSourceTypes));
            var createSource = (ObsSourceCreate)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_source_create"), typeof(ObsSourceCreate));
            var releaseSource = (ObsSourceRelease)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_source_release"), typeof(ObsSourceRelease));
            var queueTask = (ObsQueueTask)Marshal.GetDelegateForFunctionPointer(
                GetProcAddress(obs, "obs_queue_task"), typeof(ObsQueueTask));
            byte[] pathBytes = System.Text.Encoding.UTF8.GetBytes(pluginPath + "\0");
            utf8Path = Marshal.AllocHGlobal(pathBytes.Length);
            Marshal.Copy(pathBytes, 0, utf8Path, pathBytes.Length);
            byte[] localeBytes = System.Text.Encoding.UTF8.GetBytes("en-US\0");
            utf8Locale = Marshal.AllocHGlobal(localeBytes.Length);
            Marshal.Copy(localeBytes, 0, utf8Locale, localeBytes.Length);
            started = startup(utf8Locale, IntPtr.Zero, IntPtr.Zero);
            if (!started)
                throw new InvalidOperationException("obs_startup returned false.");
            int openResult = open(out module, utf8Path, IntPtr.Zero);
            bool initialized = openResult == 0 && initialize(module);
            var sourceIds = new List<string>();
            if (initialized)
            {
                UInt64 index = 0;
                IntPtr id;
                while (enumerate(new UIntPtr(index), out id))
                {
                    sourceIds.Add(Marshal.PtrToStringAnsi(id));
                    index++;
                }
            }
            bool sourceLifecycleAttempted = initialized && !String.IsNullOrWhiteSpace(expectedSourceId);
            bool sourceCreated = false;
            bool sourceDestroyCompleted = false;
            if (sourceLifecycleAttempted)
            {
                utf8SourceId = AllocUtf8(expectedSourceId);
                utf8SourceName = AllocUtf8("Creators Forge lifecycle probe");
                IntPtr source = createSource(utf8SourceId, utf8SourceName, IntPtr.Zero, IntPtr.Zero);
                if (source == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "obs_source_create returned null for '" + expectedSourceId + "'.");
                sourceCreated = true;
                releaseSource(source);

                // obs_source_release defers destruction. Queueing a synchronous no-op behind it
                // proves that the plugin's destroy callback ran before this probe continues.
                ObsTask barrier = delegate(IntPtr parameter) { };
                queueTask(3, barrier, IntPtr.Zero, true); // OBS_TASK_DESTROY
                GC.KeepAlive(barrier);
                sourceDestroyCompleted = true;
            }
            var result = new Dictionary<string, object>();
            result["obsOpenResult"] = openResult;
            result["obsInitSucceeded"] = initialized;
            result["registeredSourceIds"] = sourceIds.ToArray();
            result["sourceLifecycleAttempted"] = sourceLifecycleAttempted;
            result["sourceCreated"] = sourceCreated;
            result["sourceDestroyCompleted"] = sourceDestroyCompleted;
            return result;
        }
        finally
        {
            if (utf8Path != IntPtr.Zero)
                Marshal.FreeHGlobal(utf8Path);
            if (utf8Locale != IntPtr.Zero)
                Marshal.FreeHGlobal(utf8Locale);
            if (utf8SourceId != IntPtr.Zero)
                Marshal.FreeHGlobal(utf8SourceId);
            if (utf8SourceName != IntPtr.Zero)
                Marshal.FreeHGlobal(utf8SourceName);
            if (started)
            {
                var shutdown = (ObsShutdown)Marshal.GetDelegateForFunctionPointer(
                    GetProcAddress(obs, "obs_shutdown"), typeof(ObsShutdown));
                shutdown();
            }
            FreeLibrary(obs);
            SetDllDirectory(null);
        }
    }

    private static IntPtr AllocUtf8(string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");
        IntPtr pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }
}
'@

$obsBinaryDirectory = Join-Path $resolvedObsRoot "bin\64bit"
$native = [FoundryObsNativeProbe]::Inspect(
    $resolvedPlugin,
    $obsBinaryDirectory,
    [string]::IsNullOrWhiteSpace($ExpectedSourceId))
$libobs = [FoundryObsNativeProbe]::InspectWithObs(
    $obsBinaryDirectory,
    $resolvedPlugin,
    $ExpectedSourceId)
$result = [ordered]@{
    schemaVersion = 1
    profile = "32.x-windows-x64"
    obsRoot = $resolvedObsRoot
    obsVersion = $obsVersion
    pluginPath = $resolvedPlugin
    moduleApiVersion = $native["moduleApiVersion"]
    moduleApiEncoded = $native["moduleApiEncoded"]
    requiredExports = $native["requiredExports"]
    loadCallbackSucceeded = $native["loadCallbackSucceeded"]
    obsOpenResult = $libobs["obsOpenResult"]
    obsInitSucceeded = $libobs["obsInitSucceeded"]
    registeredSourceIds = $libobs["registeredSourceIds"]
    expectedSourceId = if ($ExpectedSourceId) { $ExpectedSourceId } else { $null }
    expectedSourceRegistered = if ($ExpectedSourceId) {
        $libobs["registeredSourceIds"] -contains $ExpectedSourceId
    } else {
        $null
    }
    sourceLifecycleAttempted = $libobs["sourceLifecycleAttempted"]
    sourceCreated = $libobs["sourceCreated"]
    sourceDestroyCompleted = $libobs["sourceDestroyCompleted"]
    inObsRuntimeVerificationRequired = $true
}

if ($ReportPath) {
    $reportDirectory = Split-Path -Parent $ReportPath
    if ($reportDirectory) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::GetFullPath($ReportPath),
        (($result | ConvertTo-Json -Depth 4) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($false))
}

[pscustomobject]$result
