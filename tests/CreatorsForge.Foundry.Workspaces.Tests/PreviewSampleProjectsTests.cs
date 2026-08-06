namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class PreviewSampleProjectsTests
{
    [Theory]
    [InlineData("VisualWebOverlay/VisualWebOverlay.foundryproj", PreviewAdapterIds.StaticWeb)]
    [InlineData("VisualWinFormsPanel/VisualWinFormsPanel.foundryproj", PreviewAdapterIds.WinForms)]
    [InlineData("ObsConfigurableFilter/ObsConfigurableFilter.foundryproj", PreviewAdapterIds.ObsComponent)]
    public async Task RepresentativeSampleProducesExpectedAdapter(
        string projectRelativePath,
        string expectedAdapter)
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "samples",
            projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var opened = await FoundryWorkspaceService.OpenAsync(projectPath, CancellationToken.None);
        Assert.True(opened.IsSuccess, string.Join(Environment.NewLine, opened.Diagnostics));
        var workspace = opened.Value!;

        var analyzed = await PreviewDesignService.AnalyzeAsync(
            workspace,
            workspace.Manifest.Preview!,
            CancellationToken.None);

        Assert.True(analyzed.IsSuccess, string.Join(Environment.NewLine, analyzed.Diagnostics));
        Assert.Equal(expectedAdapter, analyzed.Value!.Adapter!.Id);
        Assert.NotEmpty(analyzed.Value.Elements);
    }

    [Fact]
    public async Task VisualPreviewWorkspaceLoadsAllThreeProviderSamples()
    {
        var workspacePath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "VisualPreviewSamples.foundryworkspace");

        var result = await FoundryWorkspaceSetService.LoadAsync(
            workspacePath,
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(3, result.Value!.Projects.Count);
        Assert.All(result.Value.Projects, project => Assert.NotNull(project.Manifest.Preview));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CreatorsForge.Foundry.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("The Foundry repository root could not be located.");
    }
}
