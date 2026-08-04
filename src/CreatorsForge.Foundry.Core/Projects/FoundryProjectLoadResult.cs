using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Core.Projects;

public sealed record FoundryProjectLoadResult(
    string? ProjectPath,
    FoundryProjectManifest? Manifest,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess =>
        Manifest is not null && Diagnostics.All(diagnostic => !diagnostic.IsError);
}
