using CreatorsForge.Foundry.Build.ObsStudio;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class ObsTemplateNativeCompilationTests
{
    public const string VerificationEnvironmentVariable =
        "CREATORS_FORGE_VERIFY_OBS_TEMPLATES";

    [Fact]
    public async Task EveryTemplateCompilesAgainstPinnedSdkWhenVerificationIsEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(VerificationEnvironmentVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        Assert.True(ObsSdkManager.Inspect().IsReady);
        foreach (var template in ObsPluginTemplateService.Templates)
        {
            using var project = TemporaryTemplateProject.Create(template.Id);
            var result = await new FoundryBuildOrchestrator().BuildAsync(
                project.Manifest,
                project.ManifestPath);

            Assert.True(
                result.IsSuccess,
                $"{template.Id}: {string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}{Environment.NewLine}{item.Details}"))}");
            Assert.True(File.Exists(Path.Combine(
                project.Root,
                "build",
                result.PackageIntermediate!.Artifacts
                    .Single(item => item.Kind == "nativeObsPlugin").Path
                    .Replace('/', Path.DirectorySeparatorChar))));
        }
    }

    private sealed class TemporaryTemplateProject : IDisposable
    {
        private TemporaryTemplateProject(
            string root,
            string manifestPath,
            FoundryProjectManifest manifest)
        {
            Root = root;
            ManifestPath = manifestPath;
            Manifest = manifest;
        }

        public string Root { get; }
        public string ManifestPath { get; }
        public FoundryProjectManifest Manifest { get; }

        public static TemporaryTemplateProject Create(string template)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.ObsTemplateTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            var design = new FoundryObsDesign
            {
                Template = template,
                Source = "src/plugin.c",
                ComponentId = $"dev.creatorsforge.{template.Replace("-v1", string.Empty, StringComparison.Ordinal)}",
                ComponentName = $"Foundry {template}",
            };
            var plugin = new FoundryObsPlugin
            {
                Contract = FoundryObsPlugin.SdkContract,
                ModuleName = $"foundry-{template}",
                EntrySymbol = "foundry_obs_plugin_load",
                DisplayName = $"Foundry {template}",
                Author = "Creators Forge",
                Description = "Native template compilation fixture.",
                ApiVersion = FoundryObsPlugin.SupportedSdkVersion,
                SdkVersion = FoundryObsPlugin.SupportedSdkVersion,
                Design = design,
            };
            var generated = ObsPluginTemplateService.Generate(plugin, design);
            Assert.True(generated.IsSuccess);
            File.WriteAllText(Path.Combine(root, "src", "plugin.c"), generated.Source);
            var manifestPath = Path.Combine(root, "Template.foundryproj");
            File.WriteAllText(manifestPath, "{}");
            var manifest = new FoundryProjectManifest
            {
                Name = $"Foundry {template}",
                Id = $"dev.creatorsforge.{template.Replace("-v1", string.Empty, StringComparison.Ordinal)}",
                Version = "0.1.0",
                Target = new FoundryTarget
                {
                    Provider = "obsstudio",
                    Profile = "32.x-windows-x64",
                },
                NativeBuild = new FoundryNativeBuild { Sources = ["src/plugin.c"] },
                ObsPlugin = plugin,
                Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
            };
            return new(root, manifestPath, manifest);
        }

        public void Dispose()
        {
            if (string.Equals(
                    Environment.GetEnvironmentVariable("CREATORS_FORGE_RETAIN_OBS_TEMPLATE_TESTS"),
                    "1",
                    StringComparison.Ordinal))
            {
                return;
            }

            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
