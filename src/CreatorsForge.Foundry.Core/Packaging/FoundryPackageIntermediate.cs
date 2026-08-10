namespace CreatorsForge.Foundry.Core.Packaging;

/// <summary>
/// A platform-neutral inventory of validated build artifacts. Target-specific
/// package adapters consume this model in later phases.
/// </summary>
public sealed record FoundryPackageIntermediate
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required FoundryPackageProject Project { get; init; }

    public required FoundryPackageTarget Target { get; init; }

    public IReadOnlyList<FoundryPackageArtifact> Artifacts { get; init; } = [];
}

public sealed record FoundryPackageProject(
    string Id,
    string Name,
    string Version);

public sealed record FoundryPackageTarget(
    string Provider,
    string Profile,
    string Framework,
    string? CphCatalogueRevision = null,
    string? ObsApiVersion = null,
    string? ObsSdkVersion = null,
    string? ObsTemplateRevision = null,
    string? ObsComponentId = null);

public sealed record FoundryPackageArtifact(
    string Kind,
    string Path,
    long Size,
    string Sha256);

public static class FoundryPackageArtifactKinds
{
    public const string ManagedAssembly = "managedAssembly";
    public const string CphInlineBridge = "cphInlineBridge";
    public const string StreamerBotPackage = "streamerBotPackage";
    public const string StreamerBotPackageReport = "streamerBotPackageReport";
    public const string StreamerBotImportReport = "streamerBotImportReport";
    public const string NativeObsPlugin = "nativeObsPlugin";
    public const string ObsPluginPackage = "obsPluginPackage";
}
