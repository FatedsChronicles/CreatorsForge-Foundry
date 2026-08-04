using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;

namespace CreatorsForge.Foundry.Build;

public sealed record FoundryBuildResult(
    string? OutputDirectory,
    string? PackageIntermediatePath,
    FoundryPackageIntermediate? PackageIntermediate,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess =>
        PackageIntermediate is not null &&
        Diagnostics.All(diagnostic => !diagnostic.IsError);
}
