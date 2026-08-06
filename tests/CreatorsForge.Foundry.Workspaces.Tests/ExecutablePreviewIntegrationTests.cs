using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.PreviewHost;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class ExecutablePreviewIntegrationTests
{
    [Fact]
    public async Task BuiltWinFormsSampleExecutesAndReturnsLivePng()
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "VisualWinFormsPanel",
            "VisualWinFormsPanel.foundryproj");
        var opened = await FoundryWorkspaceService.OpenAsync(projectPath, CancellationToken.None);
        Assert.True(opened.IsSuccess, string.Join(Environment.NewLine, opened.Diagnostics));
        var workspace = opened.Value!;
        var build = await new FoundryBuildOrchestrator().BuildAsync(
            workspace.Manifest,
            workspace.ProjectPath,
            CancellationToken.None);
        Assert.True(build.IsSuccess, string.Join(Environment.NewLine, build.Diagnostics));
        var artifact = build.PackageIntermediate!.Artifacts.Single(item =>
            item.Kind == FoundryPackageArtifactKinds.ManagedAssembly);
        var artifactPath = Path.Combine(
            workspace.ProjectRoot,
            "build",
            artifact.Path.Replace('/', Path.DirectorySeparatorChar));
        var analyzed = await PreviewDesignService.AnalyzeAsync(
            workspace,
            workspace.Manifest.Preview!,
            CancellationToken.None);
        Assert.True(analyzed.IsSuccess, string.Join(Environment.NewLine, analyzed.Diagnostics));
        using var stateRoot = new TemporaryDirectory();
        await using var session = new PreviewRuntimeSession(
            typeof(PreviewHostMarker).Assembly.Location,
            stateRoot.Path,
            TimeSpan.FromSeconds(15));

        var result = await session.RefreshExecutableAsync(
            analyzed.Value!,
            new(
                PreviewRuntimeExecutionKinds.WinForms,
                workspace.ProjectRoot,
                workspace.Manifest.Preview!.Source,
                artifactPath));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("winforms-live-v1", result.Frame!.AdapterId);
        Assert.Equal("executable", result.Frame.ExecutionMode);
        Assert.NotEmpty(Convert.FromBase64String(result.Frame.ImagePngBase64!));
        Assert.Contains(result.Logs, item => item.Contains("ControlPanel", StringComparison.Ordinal));
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FoundryExecutablePreviewTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
