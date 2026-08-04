using CreatorsForge.Foundry.Core.Packaging;

namespace CreatorsForge.Foundry.Build;

public sealed record FoundryPublishingChecklistReport
{
    public int SchemaVersion { get; init; } = 1;
    public required string PackageName { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<FoundryPublishingChecklistItem> Checklist { get; init; }
    public required IReadOnlyList<FoundryReleaseDependency> Dependencies { get; init; }
}

public sealed record FoundryReproducibilityReport
{
    public int SchemaVersion { get; init; } = 1;
    public required string ProjectId { get; init; }
    public required string Version { get; init; }
    public required string Archive { get; init; }
    public required long ArchiveSize { get; init; }
    public required string ArchiveSha256 { get; init; }
    public required string BuildManifestSha256 { get; init; }
    public required string ReproductionCommand { get; init; }
}
