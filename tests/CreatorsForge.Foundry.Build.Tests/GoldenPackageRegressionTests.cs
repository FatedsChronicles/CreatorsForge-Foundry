using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CreatorsForge.Foundry.Build.ObsStudio;
using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class GoldenPackageRegressionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const string Bridge = "public class CPHInline { public bool Execute() => true; }\n";

    [Fact]
    public void StreamerBotPackageMatchesReviewedSemanticGolden()
    {
        var export = StreamerBotStableV23Adapter.Encode(
            StreamerBotStableV23AdapterTests.CreateDefinition(),
            "com.creatorsforge.golden.streamerbot",
            "Streamer.bot Golden",
            "1.2.3",
            Bridge);
        var payload = StreamerBotStableV23Adapter.Decode(export.ImportCode);
        var definition = StreamerBotStableV23Adapter.DecodeDefinition(export.ImportCode);
        var data = payload["data"]!.AsObject();
        var action = data["actions"]![0]!.AsObject();
        var command = data["commands"]![0]!.AsObject();
        var queue = data["queues"]![0]!.AsObject();
        var bridge = action["subActions"]![1]!.AsObject();
        var embeddedBridge = Convert.FromBase64String(bridge["byteCode"]!.GetValue<string>());

        var snapshot = new JsonObject
        {
            ["format"] = export.Report.Adapter,
            ["payloadVersion"] = export.Report.PayloadVersion,
            ["exportedFrom"] = export.Report.ExportedFrom,
            ["projectId"] = export.Report.ProjectId,
            ["projectVersion"] = export.Report.ProjectVersion,
            ["counts"] = new JsonObject
            {
                ["actions"] = export.Report.ActionCount,
                ["commands"] = export.Report.CommandCount,
                ["queues"] = export.Report.QueueCount,
            },
            ["roundTripVerified"] = export.Report.RoundTripVerified,
            ["definition"] = JsonNode.Parse(StreamerBotDefinitionLoader.Serialize(definition)),
            ["wire"] = new JsonObject
            {
                ["actionId"] = action["id"]!.GetValue<string>(),
                ["queueId"] = queue["id"]!.GetValue<string>(),
                ["commandId"] = command["id"]!.GetValue<string>(),
                ["triggerIds"] = new JsonArray(
                    action["triggers"]!.AsArray().Select(item =>
                        JsonValue.Create(item!["id"]!.GetValue<string>())).ToArray()),
                ["subActionIds"] = new JsonArray(
                    action["subActions"]!.AsArray().Select(item =>
                        JsonValue.Create(item!["id"]!.GetValue<string>())).ToArray()),
                ["queueLinkValid"] = action["queue"]!.GetValue<string>() == queue["id"]!.GetValue<string>(),
                ["commandLinkValid"] = action["triggers"]![0]!["commandId"]!.GetValue<string>() == command["id"]!.GetValue<string>(),
                ["bridgeSha256"] = Convert.ToHexStringLower(SHA256.HashData(embeddedBridge)),
            },
        };

        AssertGolden("streamerbot-stable-v23-package.json", snapshot);
    }

    [Fact]
    public async Task ObsPackageMatchesReviewedSemanticGolden()
    {
        using var project = TemporaryObsGoldenProject.Create();
        var result = await new FoundryBuildOrchestrator(
            new SuccessfulObsRunner(project.Root)).BuildAsync(
                project.Manifest,
                project.ManifestPath);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var package = result.PackageIntermediate!.Artifacts.Single(item =>
            item.Kind == "obsPluginPackage");
        var packagePath = Path.Combine(project.Root, "build", package.Path);
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = archive.GetEntry("foundry-package.json")!;
        using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
        var packageManifest = JsonNode.Parse(await reader.ReadToEndAsync());
        var snapshot = new JsonObject
        {
            ["format"] = "obs-plugin-package-v1",
            ["project"] = new JsonObject
            {
                ["id"] = result.PackageIntermediate.Project.Id,
                ["name"] = result.PackageIntermediate.Project.Name,
                ["version"] = result.PackageIntermediate.Project.Version,
            },
            ["target"] = new JsonObject
            {
                ["provider"] = result.PackageIntermediate.Target.Provider,
                ["profile"] = result.PackageIntermediate.Target.Profile,
                ["framework"] = result.PackageIntermediate.Target.Framework,
                ["apiVersion"] = result.PackageIntermediate.Target.ObsApiVersion,
            },
            ["artifacts"] = new JsonArray(result.PackageIntermediate.Artifacts.Select(item =>
                (JsonNode)new JsonObject
                {
                    ["kind"] = item.Kind,
                    ["path"] = item.Path,
                }).ToArray()),
            ["archiveEntries"] = new JsonArray(archive.Entries.Select(item =>
                JsonValue.Create(item.FullName)).ToArray()),
            ["fixedTimestamps"] = archive.Entries.All(item => item.LastWriteTime ==
                new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            ["packageManifest"] = packageManifest,
        };

        AssertGolden("obs-module-load-v1-package.json", snapshot);
    }

    private static void AssertGolden(string name, JsonNode actual)
    {
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", name))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var serialized = (actual.ToJsonString(JsonOptions) + "\n")
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, serialized);
    }

    private sealed class SuccessfulObsRunner(string projectRoot) : IBuildProcessRunner
    {
        public async Task<BuildProcessResult> RunAsync(
            BuildProcessRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Arguments.Contains("--build", StringComparer.Ordinal))
            {
                var output = Path.Combine(projectRoot, "build", "obs", "bin");
                Directory.CreateDirectory(output);
                await File.WriteAllBytesAsync(
                    Path.Combine(output, "golden-obs-module.dll"),
                    "golden-native-module"u8.ToArray(),
                    cancellationToken);
            }

            return new(0, "CMake succeeded.", string.Empty);
        }
    }

    private sealed class TemporaryObsGoldenProject : IDisposable
    {
        private TemporaryObsGoldenProject(
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

        public static TemporaryObsGoldenProject Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.Golden.Obs",
                Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(root, "src", "plugin.c");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, "#include <stdbool.h>\nbool foundry_obs_plugin_load(void) { return true; }\n");
            var manifestPath = Path.Combine(root, "GoldenObs.foundryproj");
            File.WriteAllText(manifestPath, "{}\n");
            var manifest = new FoundryProjectManifest
            {
                Name = "OBS Golden Module",
                Id = "dev.creatorsforge.golden.obs-module",
                Version = "2.3.4",
                Target = new() { Provider = "obsstudio", Profile = "32.x-windows-x64" },
                NativeBuild = new() { Sources = ["src/plugin.c"] },
                ObsPlugin = new()
                {
                    Contract = FoundryObsPlugin.MinimalContract,
                    ModuleName = "golden-obs-module",
                    DisplayName = "OBS Golden Module",
                    Author = "Creators Forge",
                    Description = "Reviewed OBS package fixture",
                    ApiVersion = FoundryObsPlugin.MinimalApiVersion,
                },
                Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
            };
            return new(root, manifestPath, manifest);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
