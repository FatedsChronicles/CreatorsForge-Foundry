using System.Text;
using System.Text.Json.Nodes;
using System.IO.Compression;
using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class StreamerBotImportServiceTests
{
    [Theory]
    [InlineData("export.sb")]
    [InlineData("export.fc")]
    [InlineData("export.txt")]
    public async Task ImportFileReaderAcceptsAnyExtensionByContent(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "CreatorsForge.ImportFileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, fileName);
            var expected = StreamerBotEnvelopeCodec.Encode(CreatePayload());
            await File.WriteAllTextAsync(path, expected, new UTF8Encoding(false));

            Assert.Equal(expected, await StreamerBotImportFileReader.ReadAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ImportFileReaderRejectsShortcutsAndInvalidUtf8()
    {
        var root = Path.Combine(Path.GetTempPath(), "CreatorsForge.ImportFileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var shortcut = Path.Combine(root, "export.lnk");
            var invalid = Path.Combine(root, "export.sb");
            await File.WriteAllTextAsync(shortcut, "not-a-shortcut-but-still-rejected");
            await File.WriteAllBytesAsync(invalid, [0xff, 0xfe, 0xfd]);

            await Assert.ThrowsAsync<InvalidDataException>(() => StreamerBotImportFileReader.ReadAsync(shortcut));
            await Assert.ThrowsAsync<DecoderFallbackException>(() => StreamerBotImportFileReader.ReadAsync(invalid));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void NamingSuggestionsTrackProjectName()
    {
        var suggestion = StreamerBotImportNamingService.Suggest("Bot Eliminator", @"D:\Documents\Creators Forge Foundry");

        Assert.Equal("com.example.bot-eliminator", suggestion.PackageId);
        Assert.Equal(@"D:\Documents\Creators Forge Foundry\BotEliminator", suggestion.DestinationFolder);
    }

    [Fact]
    public void ImportedCSharpPathsReceiveFriendlyDisplayOnlyLabels()
    {
        var definition = StreamerBotStableV23AdapterTests.CreateDefinition();
        definition = definition with
        {
            Actions = [definition.Actions[0] with
            {
                Name = "Welcome viewers",
                SubActions =
                [
                    definition.Actions[0].SubActions[0],
                    new("code-a", "executeCSharp", true, null, null, false,
                        "streamerbot/code/action-wire-id/sub-wire-id.cs"),
                ],
            }],
        };

        var labels = StreamerBotProjectTreeLabelService.Create(definition);

        Assert.Equal("Welcome viewers", labels["streamerbot/code/action-wire-id"]);
        Assert.Equal("02 - Execute C# Code.cs", labels["streamerbot/code/action-wire-id/sub-wire-id.cs"]);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(24)]
    public async Task VerifiedPayloadCreatesPackageOnlyProjectAndReexportsWithoutExecutingCode(int version)
    {
        var payload = CreatePayload();
        payload["version"] = version;
        payload["exportedFrom"] = version == 23 ? "1.0.4" : "1.0.7";
        payload["data"]!["commands"]![0]!["group"] = "Creator Commands";
        payload["data"]!["actions"]![0]!["unknownActionField"] = "preserve-me";
        payload["data"]!["actions"]![0]!["subActions"]![1]!["references"] =
            new JsonArray("C:\\Developer\\Private\\Host.dll", ".\\Portable.Dependency.dll");
        payload["data"]!["actions"]![0]!["subActions"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "opaque-wire-id", ["type"] = 456789, ["enabled"] = true, ["unknown"] = 42,
        });
        var analysis = StreamerBotImportService.Analyze(StreamerBotEnvelopeCodec.Encode(payload));

        Assert.True(analysis.CanCreateProject);
        Assert.Equal(version, analysis.Summary!.PayloadVersion);
        Assert.Single(analysis.CSharpSources);
        Assert.Equal("Creator Commands", analysis.Definition!.Commands[0].Group);
        var importedResource = Assert.Single(analysis.Definition!.Resources);
        Assert.Equal("localFile", importedResource.Type);
        Assert.Equal(StreamerBotResourcePortability.ManualConfiguration, importedResource.Portability);
        Assert.Equal("subAction", Assert.Single(importedResource.Bindings!).EntityType);
        Assert.Contains(analysis.Definition!.Actions[0].SubActions, item => item.Kind == "opaque" && item.ReadOnly);

        var root = Path.Combine(Path.GetTempPath(), "CreatorsForge.ImportTests", Guid.NewGuid().ToString("N"));
        try
        {
            var created = await StreamerBotImportProjectService.CreateAsync(new(
                root, "Imported Test", "com.creatorsforge.import-test", "1.1.0-beta.1", "Original Developer",
                "1.0.7-stable", "Third-party fixture", analysis));
            Assert.True(created.IsSuccess);
            Assert.False(File.Exists(Path.Combine(root, "LICENSE.txt")));
            var loaded = await FoundryProjectLoader.LoadAsync(created.ProjectPath!);
            Assert.True(loaded.IsSuccess);
            Assert.Equal([FoundryOutputKinds.StreamerBotPackage], loaded.Manifest!.Outputs);
            Assert.Null(loaded.Manifest.ManagedBuild);

            var source = Assert.Single(analysis.CSharpSources);
            var sourcePath = Path.Combine(root, source.Key.Replace('/', Path.DirectorySeparatorChar));
            await File.AppendAllTextAsync(sourcePath, "// edited safely\n");
            var definitionPath = Path.Combine(root, "streamerbot", "streamerbot.json");
            var importedDefinition = StreamerBotDefinitionLoader.Load(await File.ReadAllTextAsync(definitionPath)).Definition!;
            importedDefinition = importedDefinition with
            {
                Actions = importedDefinition.Actions.Select(action => action with
                {
                    SubActions = action.SubActions.Select(subAction => subAction.Kind == "executeCSharp"
                        ? subAction with { References = [".\\Portable.Dependency.dll"] }
                        : subAction).ToArray(),
                }).ToArray(),
            };
            await File.WriteAllTextAsync(definitionPath, StreamerBotDefinitionLoader.Serialize(importedDefinition));
            var runner = new RejectingRunner();
            var build = await new FoundryBuildOrchestrator(runner).BuildAsync(loaded.Manifest, created.ProjectPath!);
            Assert.True(build.IsSuccess, string.Join(Environment.NewLine, build.Diagnostics.Select(item => item.Message)));
            Assert.Equal(0, runner.InvocationCount);
            Assert.DoesNotContain(build.PackageIntermediate!.Artifacts, item => item.Kind == FoundryPackageArtifactKinds.ManagedAssembly);
            Assert.Contains(build.PackageIntermediate.Artifacts, item => item.Kind == FoundryPackageArtifactKinds.StreamerBotImportReport);
            var portability = build.PackageIntermediate.Artifacts.Single(item =>
                item.Kind == FoundryPackageArtifactKinds.StreamerBotPortabilityReport);
            Assert.Contains("\"total\": 1", await File.ReadAllTextAsync(
                Path.Combine(root, "build", portability.Path)), StringComparison.Ordinal);
            var package = build.PackageIntermediate.Artifacts.Single(item => item.Kind == FoundryPackageArtifactKinds.StreamerBotPackage);
            var reexported = StreamerBotEnvelopeCodec.Decode(await File.ReadAllTextAsync(Path.Combine(root, "build", package.Path)));
            Assert.Equal("preserve-me", reexported["data"]!["actions"]![0]!["unknownActionField"]!.GetValue<string>());
            Assert.Equal("Creator Commands", reexported["data"]!["commands"]![0]!["group"]!.GetValue<string>());
            Assert.Equal(42, reexported["data"]!["actions"]![0]!["subActions"]![2]!["unknown"]!.GetValue<int>());
            var editedCode = Encoding.UTF8.GetString(Convert.FromBase64String(
                reexported["data"]!["actions"]![0]!["subActions"]![1]!["byteCode"]!.GetValue<string>()));
            Assert.Contains("edited safely", editedCode, StringComparison.Ordinal);
            Assert.Equal([".\\Portable.Dependency.dll"], reexported["data"]!["actions"]![0]!["subActions"]![1]!["references"]!
                .AsArray().Select(item => item!.GetValue<string>()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UnknownVersionRemainsAnalysisOnly()
    {
        var payload = CreatePayload();
        payload["version"] = 25;
        var result = StreamerBotImportService.Analyze(StreamerBotEnvelopeCodec.Encode(payload));
        Assert.False(result.CanCreateProject);
        Assert.NotNull(result.Summary);
        Assert.Contains(result.Findings, item => item.Code == "CFI1005");
    }

    [Fact]
    public void CredentialLocationBlocksImportWithoutLeakingValue()
    {
        var payload = CreatePayload();
        payload["meta"]!["api_token"] = "never-print-this-secret";
        var result = StreamerBotImportService.Analyze(StreamerBotEnvelopeCodec.Encode(payload));
        Assert.False(result.CanCreateProject);
        var finding = Assert.Single(result.Findings, item => item.Code == "CFI1006");
        Assert.Equal("$.meta.api_token", finding.Path);
        Assert.DoesNotContain("never-print-this-secret", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcessiveNestedEntityCountIsRejected()
    {
        var payload = CreatePayload();
        var subActions = payload["data"]!["actions"]![0]!["subActions"]!.AsArray();
        for (var index = subActions.Count; index < 10_001; index++)
            subActions.Add(new JsonObject { ["id"] = $"opaque-{index}", ["type"] = 8888, ["enabled"] = true });
        var result = StreamerBotImportService.Analyze(StreamerBotEnvelopeCodec.Encode(payload));
        Assert.False(result.CanCreateProject);
        Assert.Contains(result.Findings, item => item.Code == "CFI1004");
    }

    [Theory]
    [InlineData("not base64")]
    [InlineData("U0JBRQ==")]
    public void InvalidEnvelopesAreRejected(string text)
    {
        var result = StreamerBotImportService.Analyze(text);
        Assert.False(result.CanCreateProject);
        Assert.Contains(result.Findings, item => item.Code == "CFI1001");
    }

    [Fact]
    public void EnvelopeAcceptsWhitespaceButRejectsSignatureGzipUtf8AndExcessiveDepth()
    {
        var valid = StreamerBotEnvelopeCodec.Encode(CreatePayload());
        Assert.Equal(23, StreamerBotEnvelopeCodec.Decode($"\n {valid[..20]} \r\n{valid[20..]} \t")["version"]!.GetValue<int>());
        Assert.Throws<InvalidDataException>(() => StreamerBotEnvelopeCodec.Decode(Convert.ToBase64String("NOPEinvalid"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => StreamerBotEnvelopeCodec.Decode(Convert.ToBase64String("SBAEnot-gzip"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => StreamerBotEnvelopeCodec.Decode(CreateEnvelope([0xff, 0xfe, 0xfd])));
        var deepJson = Encoding.UTF8.GetBytes(new string('[', 130) + "0" + new string(']', 130));
        Assert.Throws<InvalidDataException>(() => StreamerBotEnvelopeCodec.Decode(CreateEnvelope(deepJson)));
    }

    [Fact]
    public void DefinitionV1MigratesDeterministicallyToV6()
    {
        const string json = """{"schemaVersion":1,"metadata":{"author":"A","description":"B"},"queues":[],"commands":[],"actions":[]}""";
        var first = StreamerBotDefinitionLoader.Load(json);
        var second = StreamerBotDefinitionLoader.Load(json);
        Assert.True(first.IsSuccess);
        Assert.Equal(StreamerBotDefinition.CurrentSchemaVersion, first.Definition!.SchemaVersion);
        Assert.Equal(StreamerBotDefinitionLoader.Serialize(first.Definition), StreamerBotDefinitionLoader.Serialize(second.Definition!));
    }

    [Fact]
    public async Task PreservedRoundTripRetainsTogglesWeightsAndEditedOrder()
    {
        var payload = CreatePayload();
        var analysis = StreamerBotImportService.Analyze(StreamerBotEnvelopeCodec.Encode(payload));
        var definition = analysis.Definition!;
        var action = definition.Actions[0];
        definition = definition with
        {
            Commands = [definition.Commands[0] with { Group = "Imported commands" }],
            Actions =
            [
                action with
                {
                    Group = "Imported group",
                    RandomAction = true,
                    ExcludeFromPending = true,
                    ExcludeFromHistory = true,
                    SubActions = action.SubActions.Reverse().Select((item, index) =>
                        item with { Weight = index + 1 }).ToArray(),
                },
            ],
        };
        var root = Path.Combine(Path.GetTempPath(), "CreatorsForge.ImportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "streamerbot"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "streamerbot", "import-preservation.json"),
                new JsonObject { ["payload"] = payload.DeepClone() }.ToJsonString());
            foreach (var source in analysis.CSharpSources)
            {
                var path = Path.Combine(root, source.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, source.Value);
            }
            var artifact = await StreamerBotPreservedPayloadAdapter.EncodeAsync(
                definition, root, "com.creatorsforge.fixture", "Imported Fixture", "1.1.0-beta.1");
            var wire = StreamerBotEnvelopeCodec.Decode(artifact.ImportCode)["data"]!["actions"]![0]!.AsObject();
            Assert.True(wire["randomAction"]!.GetValue<bool>());
            Assert.True(wire["excludeFromPending"]!.GetValue<bool>());
            Assert.True(wire["excludeFromHistory"]!.GetValue<bool>());
            Assert.Equal("Imported group", wire["group"]!.GetValue<string>());
            Assert.Equal("Imported commands", StreamerBotEnvelopeCodec.Decode(artifact.ImportCode)
                ["data"]!["commands"]![0]!["group"]!.GetValue<string>());
            Assert.Equal([1d, 2d], wire["subActions"]!.AsArray().Select(item => item!["weight"]!.GetValue<double>()));
            Assert.Equal(definition.Actions[0].SubActions.Select(item => item.SourceId),
                wire["subActions"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PreservedRoundTripConvertsVerifiedSetArgumentToEditableCSharp()
    {
        var payload = CreatePayload();
        var analysis = StreamerBotImportService.Analyze(StreamerBotEnvelopeCodec.Encode(payload));
        var definition = analysis.Definition!;
        var action = definition.Actions[0];
        var native = action.SubActions[0] with { AutoType = false, Value = "quote\" and slash\\" };
        var preview = StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(native, action.Id);
        definition = definition with
        {
            Actions = [action with
            {
                SubActions = [preview.ConvertedSubAction, .. action.SubActions.Skip(1)],
            }],
        };
        var root = Path.Combine(Path.GetTempPath(), "CreatorsForge.ImportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "streamerbot"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "streamerbot", "import-preservation.json"),
                new JsonObject { ["payload"] = payload.DeepClone() }.ToJsonString());
            foreach (var source in analysis.CSharpSources)
            {
                var path = Path.Combine(root, source.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, source.Value);
            }
            StreamerBotCSharpAuthoringService.WriteNewSource(root, preview.RelativePath, preview.Source);

            var artifact = await StreamerBotPreservedPayloadAdapter.EncodeAsync(
                definition, root, "com.creatorsforge.fixture", "Imported Fixture", "1.1.0-beta.1");
            var wire = StreamerBotEnvelopeCodec.Decode(artifact.ImportCode)
                ["data"]!["actions"]![0]!["subActions"]![0]!.AsObject();

            Assert.Equal(99999, wire["type"]!.GetValue<int>());
            Assert.False(wire.ContainsKey("variableName"));
            Assert.False(wire.ContainsKey("autoType"));
            Assert.Equal(preview.Source, Encoding.UTF8.GetString(
                Convert.FromBase64String(wire["byteCode"]!.GetValue<string>())));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CentralDiagnosticsReportUnsafeAndAmbiguousWorkflows()
    {
        var definition = StreamerBotStableV23AdapterTests.CreateDefinition();
        var command = definition.Commands[0];
        var action = definition.Actions[0];
        definition = definition with
        {
            Commands = [command, command with { Id = "other", Name = "Other" }],
            Actions =
            [
                action with
                {
                    Concurrent = true,
                    RandomAction = true,
                    SubActions =
                    [
                        action.SubActions[0] with { Weight = 0 },
                        action.SubActions[0] with { Id = "consumer", Value = "%message%", Weight = 1 },
                    ],
                },
            ],
        };

        var diagnostics = StreamerBotDefinitionDiagnostics.Analyze(definition, "1.0.7-stable");

        Assert.Contains(diagnostics, item => item.Code == "SBD1003");
        Assert.Contains(diagnostics, item => item.Code == "SBD1007");
        Assert.Contains(diagnostics, item => item.Code == "SBD2003");
        Assert.Contains(diagnostics, item => item.Code == "SBD2004");
    }

    private static JsonObject CreatePayload()
    {
        var definition = new StreamerBotDefinition
        {
            Metadata = new() { Author = "Original Developer", Description = "Imported fixture" },
            Queues = [new("queue", "Default", false)],
            Commands = [new("command", "Hello", ["!hello"], true, false, 0, 0)],
            Actions = [new("action", "Hello", true, "queue", false, false,
                [new("trigger", "command", true, "command")],
                [new("argument", "setArgument", true, "message", "Hello", true),
                 new("code", "executeBridge", true, null, null, false)])],
        };
        return StreamerBotStableV23Adapter.Decode(StreamerBotStableV23Adapter.Encode(
            definition, "com.creatorsforge.fixture", "Imported Fixture", "1.0.0",
            "public class CPHInline { public bool Execute() => true; }\n").ImportCode);
    }

    private static string CreateEnvelope(byte[] decoded)
    {
        using var envelope = new MemoryStream();
        envelope.Write("SBAE"u8);
        using (var gzip = new GZipStream(envelope, CompressionLevel.SmallestSize, true)) gzip.Write(decoded);
        return Convert.ToBase64String(envelope.ToArray());
    }

    private sealed class RejectingRunner : IBuildProcessRunner
    {
        public int InvocationCount { get; private set; }
        public Task<BuildProcessResult> RunAsync(BuildProcessRequest request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            throw new InvalidOperationException("Package-only imports must not start a compiler.");
        }
    }
}
