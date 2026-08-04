namespace CreatorsForge.Foundry.Testing;

public sealed record ObsNativeHostRequest
{
    public int SchemaVersion { get; init; } = 1;
    public required string PluginPath { get; init; }
    public required string ObsRoot { get; init; }
    public string? ExpectedSourceId { get; init; }
    public string Mode { get; init; } = "obs-lifecycle";
}

public sealed record ObsNativeHostResult
{
    public int SchemaVersion { get; init; } = 1;
    public bool ModuleOpened { get; init; }
    public bool ModuleLoadSucceeded { get; init; }
    public IReadOnlyList<string> RegisteredSourceIds { get; init; } = [];
    public bool SourceLifecycleAttempted { get; init; }
    public bool SourceCreated { get; init; }
    public bool SourceDestroyed { get; init; }
    public string? Error { get; init; }
}

public sealed record ObsAbiInspection(
    bool IsPortableExecutable,
    bool IsX64,
    bool IsDll,
    IReadOnlyList<string> Exports,
    IReadOnlyList<string> MissingRequiredExports);

public sealed record ObsNativeProcessResult(
    bool Completed,
    bool TimedOut,
    int? ExitCode,
    ObsNativeHostResult? HostResult,
    string StandardOutput,
    string StandardError,
    string? Failure);
