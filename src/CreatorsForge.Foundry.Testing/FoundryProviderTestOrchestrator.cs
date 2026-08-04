using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Testing;

public sealed record FoundryProviderTestRequest(
    FoundryProjectManifest Manifest,
    string ProjectPath,
    string ArtifactPath,
    string? ObsRoot = null,
    string? NativeHostAssembly = null,
    string? ResultRelativePath = null,
    TimeSpan? Timeout = null);

public static class FoundryProviderTestOrchestrator
{
    public static Task<FoundryTestRunResult> RunAsync(
        FoundryProviderTestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Manifest.Target?.Provider switch
        {
            FoundryTestProviders.StreamerBot when request.ObsRoot is null =>
                FoundryTestRunner.RunAsync(
                    request.Manifest,
                    request.ProjectPath,
                    request.ArtifactPath,
                    request.ResultRelativePath,
                    cancellationToken),
            FoundryTestProviders.ObsStudio when
                !string.IsNullOrWhiteSpace(request.ObsRoot) &&
                !string.IsNullOrWhiteSpace(request.NativeHostAssembly) =>
                ObsNativeTestRunner.RunAsync(
                    request.Manifest,
                    request.ProjectPath,
                    request.ArtifactPath,
                    request.ObsRoot,
                    request.NativeHostAssembly,
                    request.Timeout,
                    request.ResultRelativePath,
                    cancellationToken),
            _ => throw new InvalidOperationException(
                "The provider test request does not contain the runtime inputs required by its target."),
        };
    }
}
