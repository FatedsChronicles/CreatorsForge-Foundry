using System.Text.Json;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class PreviewDesignServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public async Task StaticWebAnalysisProducesStructureWithoutIncludingScripts()
    {
        using var project = await TemporaryPreviewProject.CreateAsync();
        await project.WriteAsync(
            "ui/index.html",
            "<header>Creator Dashboard</header><main><button>Start</button><script>throw new Error('never run')</script></main>");
        var workspace = await project.OpenAsync();

        var result = await PreviewDesignService.AnalyzeAsync(
            workspace,
            new FoundryPreview
            {
                Kind = FoundryPreview.StaticWebKind,
                Source = "ui/index.html",
                Width = 1280,
                Height = 720,
            });

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!.Elements, item => item.Kind == "header" && item.Label == "Creator Dashboard");
        Assert.Contains(result.Value.Elements, item => item.Kind == "button" && item.Label == "Start");
        Assert.DoesNotContain(result.Value.Elements, item => item.Kind == "script");
        Assert.Equal(64, result.Value.SourceSha256.Length);
        Assert.Contains("did not execute", result.Value.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WinFormsAnalysisUsesDeclaredControlBoundsAndText()
    {
        using var project = await TemporaryPreviewProject.CreateAsync(winForms: true);
        await project.WriteAsync(
            "src/EntryPoint.cs",
            "buttonStart = new System.Windows.Forms.Button(); buttonStart.Location = new System.Drawing.Point(25, 40); buttonStart.Size = new System.Drawing.Size(160, 45); buttonStart.Text = \"Start stream\";");
        var workspace = await project.OpenAsync();

        var result = await PreviewDesignService.AnalyzeAsync(
            workspace,
            new FoundryPreview
            {
                Kind = FoundryPreview.WinFormsKind,
                Source = "src/EntryPoint.cs",
                Width = 800,
                Height = 600,
            });

        Assert.True(result.IsSuccess);
        var button = Assert.Single(result.Value!.Elements);
        Assert.Equal("Button: Start stream", button.Label);
        Assert.Equal(25, button.X);
        Assert.Equal(40, button.Y);
        Assert.Equal(160, button.Width);
        Assert.Equal(45, button.Height);
    }

    [Fact]
    public async Task AnalysisRejectsDisabledMissingAndOversizedSources()
    {
        using var project = await TemporaryPreviewProject.CreateAsync();
        var workspace = await project.OpenAsync();
        var disabled = await PreviewDesignService.AnalyzeAsync(
            workspace,
            new FoundryPreview
            {
                Enabled = false,
                Kind = FoundryPreview.StaticWebKind,
                Source = "ui/missing.html",
            });
        var missing = await PreviewDesignService.AnalyzeAsync(
            workspace,
            new FoundryPreview
            {
                Kind = FoundryPreview.StaticWebKind,
                Source = "ui/missing.html",
            });
        await project.WriteAsync("ui/large.html", new string('x', 1024 * 1024 + 1));
        workspace = await project.OpenAsync();
        var oversized = await PreviewDesignService.AnalyzeAsync(
            workspace,
            new FoundryPreview
            {
                Kind = FoundryPreview.StaticWebKind,
                Source = "ui/large.html",
            });

        Assert.Contains(disabled.Diagnostics, item => item.Code == "CFW2301");
        Assert.Contains(missing.Diagnostics, item => item.Code == "CFW2303");
        Assert.Contains(oversized.Diagnostics, item => item.Code == "CFW2304");
    }

    [Fact]
    public async Task SavingPreviewPreservesUnknownManifestProperties()
    {
        using var project = await TemporaryPreviewProject.CreateAsync();
        await project.WriteAsync("ui/index.html", "<main>Preview</main>");
        var json = await File.ReadAllTextAsync(project.ProjectPath);
        json = json.TrimEnd().TrimEnd('}') + ",\n  \"futurePreviewSetting\": { \"keep\": true }\n}\n";
        await File.WriteAllTextAsync(project.ProjectPath, json);
        var workspace = await project.OpenAsync();

        var result = await FoundryWorkspaceService.SavePreviewAsync(
            workspace,
            new FoundryPreview
            {
                Kind = FoundryPreview.StaticWebKind,
                Source = "ui/index.html",
                Width = 1280,
                Height = 720,
            });

        Assert.True(result.IsSuccess);
        Assert.Equal(FoundryPreview.StaticWebKind, result.Value!.Manifest.Preview!.Kind);
        Assert.True(result.Value.Manifest.AdditionalProperties!.ContainsKey("futurePreviewSetting"));
    }

    [Fact]
    public async Task DisablingPreviewRemovesOnlyThePreviewDeclaration()
    {
        using var project = await TemporaryPreviewProject.CreateAsync();
        await project.WriteAsync("ui/index.html", "<main>Preview</main>");
        var workspace = await project.OpenAsync();
        var enabled = await FoundryWorkspaceService.SavePreviewAsync(
            workspace,
            new FoundryPreview
            {
                Kind = FoundryPreview.StaticWebKind,
                Source = "ui/index.html",
            });

        var disabled = await FoundryWorkspaceService.SavePreviewAsync(enabled.Value!, null);

        Assert.True(disabled.IsSuccess);
        Assert.Null(disabled.Value!.Manifest.Preview);
        Assert.Equal(workspace.Manifest.Id, disabled.Value.Manifest.Id);
    }

    private sealed class TemporaryPreviewProject : IDisposable
    {
        private TemporaryPreviewProject(string root, string projectPath)
        {
            Root = root;
            ProjectPath = projectPath;
        }

        public string Root { get; }
        public string ProjectPath { get; }

        public static async Task<TemporaryPreviewProject> CreateAsync(bool winForms = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "FoundryPreviewTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            var projectPath = Path.Combine(root, "Preview.foundryproj");
            var manifest = new FoundryProjectManifest
            {
                Name = "Preview",
                Id = "com.example.preview",
                Version = "0.1.0",
                Target = new FoundryTarget { Provider = "streamerbot", Profile = "1.0.4-stable" },
                Features = new FoundryFeatures { WinForms = winForms },
                ManagedBuild = new FoundryManagedBuild
                {
                    AssemblyName = "Example.Preview",
                    Sources = ["src/EntryPoint.cs"],
                },
                Outputs = [FoundryOutputKinds.ManagedLibrary],
            };
            await File.WriteAllTextAsync(
                projectPath,
                JsonSerializer.Serialize(manifest, JsonOptions) + "\n");
            await File.WriteAllTextAsync(Path.Combine(root, "src", "EntryPoint.cs"), "public static class EntryPoint { }");
            return new(root, projectPath);
        }

        public async Task WriteAsync(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }

        public async Task<FoundryWorkspace> OpenAsync()
        {
            var result = await FoundryWorkspaceService.OpenAsync(ProjectPath, CancellationToken.None);
            Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
            return result.Value!;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
