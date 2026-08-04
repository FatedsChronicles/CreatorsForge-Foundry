namespace CreatorsForge.Foundry.Core.Packaging;

public sealed record FoundryReleaseManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string FoundryVersion { get; init; }

    public required DateTimeOffset BuildTimestampUtc { get; init; }

    public required string Configuration { get; init; }

    public required FoundryPackageProject Project { get; init; }

    public required FoundryPackageTarget Target { get; init; }

    public IReadOnlyList<FoundryReleaseDependency> Dependencies { get; init; } = [];

    public IReadOnlyList<FoundryReleaseWarning> Warnings { get; init; } = [];

    public FoundryReleaseSigning Signing { get; init; } = new(false, false, null, null, []);

    public required FoundryReleaseValidation Validation { get; init; }

    public IReadOnlyList<FoundryReleaseFile> Files { get; init; } = [];
}

public sealed record FoundryReleaseDependency(
    string Name,
    string Version,
    string Kind = "runtime",
    string? License = null,
    string? Source = null);

public sealed record FoundryReleaseWarning(string Code, string Message);

public sealed record FoundryReleaseSigning(
    bool Requested,
    bool Applied,
    string? Tool,
    string? CertificateThumbprint,
    IReadOnlyList<string> SignedFiles);

public sealed record FoundryReleaseValidation(
    bool ProjectValidated,
    bool BuildSucceeded,
    bool ArtifactsVerified,
    bool ArchiveVerified,
    bool SigningVerified = true);

public sealed record FoundryReleaseFile(
    string Kind,
    string Path,
    long Size,
    string Sha256);
