using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Testing;

public static class FoundryTestProviders
{
    public const string StreamerBot = "streamerbot";
    public const string ObsStudio = "obsstudio";
}

public static class FoundryTestProfiles
{
    public static IReadOnlySet<string> StreamerBot { get; } =
        FoundryStreamerBotProfiles.Supported;

    public static IReadOnlySet<string> ObsStudio { get; } = new HashSet<string>(
        ["32.x-windows-x64"],
        StringComparer.Ordinal);
}

public static class FoundryTestAssertionKinds
{
    public const string ReturnEquals = "returnEquals";
    public const string LogContains = "logContains";
    public const string LogEquals = "logEquals";
    public const string ArgumentEquals = "argumentEquals";
    public const string CphCallCount = "cphCallCount";
    public const string AbiExport = "abiExport";
    public const string ModuleLoadSucceeded = "moduleLoadSucceeded";
    public const string SourceRegistered = "sourceRegistered";
    public const string SourceCreated = "sourceCreated";
    public const string SourceDestroyed = "sourceDestroyed";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [
            ReturnEquals, LogContains, LogEquals, ArgumentEquals, CphCallCount,
            AbiExport, ModuleLoadSucceeded, SourceRegistered, SourceCreated,
            SourceDestroyed,
        ],
        StringComparer.Ordinal);
}

public sealed record FoundryTestDefinition
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Provider { get; init; } = string.Empty;
    public IReadOnlyList<string> Profiles { get; init; } = [];
    public IReadOnlyList<FoundryTestCaseDefinition> Cases { get; init; } = [];
}

public sealed record FoundryTestCaseDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public FoundrySimulatedEvent Event { get; init; } = new();
    public IReadOnlyList<FoundryTestAssertionDefinition> Assertions { get; init; } = [];
}

public sealed record FoundrySimulatedEvent
{
    public string Kind { get; init; } = string.Empty;
    public string? Name { get; init; }
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

public sealed record FoundryTestAssertionDefinition
{
    public string Kind { get; init; } = string.Empty;
    public string? Key { get; init; }
    public JsonElement Expected { get; init; }
}

public enum FoundryTestOutcome
{
    Passed,
    Failed,
    Error,
    Skipped,
}

public sealed record FoundryMockCphCall(
    string Method,
    IReadOnlyList<JsonElement> Arguments);

public sealed record FoundryTestAssertionResult(
    string Kind,
    string? Key,
    FoundryTestOutcome Outcome,
    JsonElement Expected,
    JsonElement Actual,
    string Message);

public sealed record FoundryTestCaseResult(
    string Id,
    string Name,
    FoundrySimulatedEvent Event,
    FoundryTestOutcome Outcome,
    long DurationMilliseconds,
    bool? ReturnValue,
    IReadOnlyList<string> Logs,
    IReadOnlyList<FoundryMockCphCall> CphCalls,
    IReadOnlyList<FoundryTestAssertionResult> Assertions,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public sealed record FoundryTestRunResult
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string ProjectId { get; init; }
    public required string ProjectVersion { get; init; }
    public required string Provider { get; init; }
    public required string Profile { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public required FoundryTestOutcome Outcome { get; init; }
    public IReadOnlyList<FoundryTestCaseResult> Cases { get; init; } = [];
    public IReadOnlyList<FoundryDiagnostic> Diagnostics { get; init; } = [];
    public string? ResultPath { get; init; }

    [JsonIgnore]
    public bool IsSuccess =>
        Outcome == FoundryTestOutcome.Passed &&
        Diagnostics.All(item => !item.IsError);
}

public sealed record FoundryTestDefinitionLoadResult(
    FoundryTestDefinition? Definition,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess =>
        Definition is not null && Diagnostics.All(item => !item.IsError);
}
