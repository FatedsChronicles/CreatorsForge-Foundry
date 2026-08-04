using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;

namespace CreatorsForge.Foundry.Build;

public sealed record FoundryReleaseResult(
    string? ReleaseDirectory,
    string? ArchivePath,
    string? ManifestPath,
    FoundryReleaseManifest? Manifest,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public string? ReproducibilityReportPath { get; init; }

    public bool IsSuccess =>
        ReleaseDirectory is not null &&
        ArchivePath is not null &&
        Manifest is not null &&
        Diagnostics.All(item => !item.IsError);
}
