using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Testing;

public sealed class StreamerBotMockRuntime
{
    private readonly List<string> logs = [];
    private readonly List<FoundryMockCphCall> calls = [];

    public IReadOnlyList<string> Logs => logs;
    public IReadOnlyList<FoundryMockCphCall> Calls => calls;

    public void LogInfo(string message)
    {
        var normalized = message ?? string.Empty;
        logs.Add(normalized);
        calls.Add(new(
            "CPH.LogInfo",
            [JsonSerializer.SerializeToElement(normalized)]));
    }
}

public sealed record StreamerBotMockInvocationResult(
    bool ReturnValue,
    IReadOnlyDictionary<string, object?> Arguments,
    IReadOnlyList<string> Logs,
    IReadOnlyList<FoundryMockCphCall> Calls);

public static class StreamerBotMockInvoker
{
    public static StreamerBotMockInvocationResult Invoke(
        string assemblyPath,
        FoundryCphInlineBridge bridge,
        FoundrySimulatedEvent simulatedEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(simulatedEvent);

        var fullPath = Path.GetFullPath(assemblyPath);
        var context = new TestAssemblyLoadContext();
        try
        {
            using var assemblyStream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            var assembly = context.LoadFromStream(assemblyStream);
            var type = assembly.GetType(bridge.EntryType, throwOnError: false, ignoreCase: false) ??
                throw new InvalidOperationException($"Entry type '{bridge.EntryType}' was not found in the managed assembly.");
            var method = type.GetMethod(
                bridge.EntryMethod,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(IDictionary<string, object>), typeof(Action<string>)],
                modifiers: null) ?? throw new InvalidOperationException(
                    $"Entry method '{bridge.EntryType}.{bridge.EntryMethod}' does not match args-log-v1.");
            if (method.ReturnType != typeof(bool))
            {
                throw new InvalidOperationException("The args-log-v1 entry method must return bool.");
            }

            var arguments = simulatedEvent.Arguments.ToDictionary(
                item => item.Key,
                item => ConvertJson(item.Value),
                StringComparer.Ordinal);
            var runtime = new StreamerBotMockRuntime();
            object? returnValue;
            try
            {
                returnValue = method.Invoke(null, [arguments, new Action<string>(runtime.LogInfo)]);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                throw new InvalidOperationException(
                    $"The extension threw {exception.InnerException.GetType().Name}: {exception.InnerException.Message}",
                    exception.InnerException);
            }

            return new(
                (bool)returnValue!,
                arguments,
                runtime.Logs.ToArray(),
                runtime.Calls.ToArray());
        }
        finally
        {
            context.Unload();
        }
    }

    public static object? ConvertJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt32(out var integer) => integer,
        JsonValueKind.Number when value.TryGetInt64(out var longInteger) => longInteger,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.Array => value.EnumerateArray().Select(ConvertJson).ToArray(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(
            item => item.Name,
            item => ConvertJson(item.Value),
            StringComparer.Ordinal),
        _ => throw new InvalidDataException($"JSON value kind '{value.ValueKind}' cannot be converted to a mock argument."),
    };

    private sealed class TestAssemblyLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
