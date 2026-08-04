using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class TemplateInterchangeBuildTests
{
    [Fact]
    public async Task ImportedManagedTemplateBuildsThroughNormalPipeline()
    {
        var root = Path.Combine(Path.GetTempPath(), "FoundryTemplateBuild", Guid.NewGuid().ToString("N"));
        try
        {
            var original = await FoundryWorkspaceService.CreateAsync(new(
                Path.Combine(root, "original"),
                "Template Original",
                "com.example.template-original",
                "1.0.4-stable"));
            var templatePath = Path.Combine(root, "managed.foundrytemplate");
            var exportDiagnostics = await FoundryTemplateInterchangeService.ExportAsync(original.Value!, templatePath);
            var imported = await FoundryTemplateInterchangeService.ImportAsync(new(
                templatePath,
                Path.Combine(root, "imported"),
                "Template Consumer",
                "com.example.template-consumer",
                "1.0.5-alpha.34"));

            var build = await new FoundryBuildOrchestrator().BuildAsync(
                imported.Value!.Manifest,
                imported.Value.ProjectPath);

            Assert.Empty(exportDiagnostics);
            Assert.True(imported.IsSuccess);
            Assert.True(build.IsSuccess, string.Join(Environment.NewLine, build.Diagnostics.Select(item => $"{item.Code}: {item.Message}{Environment.NewLine}{item.Details}")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
