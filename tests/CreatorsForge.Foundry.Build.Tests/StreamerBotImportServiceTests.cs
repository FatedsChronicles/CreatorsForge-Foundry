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
    [InlineData(23)]
    [InlineData(24)]
    public async Task VerifiedPayloadCreatesPackageOnlyProjectAndReexportsWithoutExecutingCode(int version)
    {
        var payload = CreatePayload();
        payload["version"] = version;
        payload["exportedFrom"] = version == 23 ? "1.0.4" : "1.0.7";
        payload["data"]!["actions"]![0]!["unknownActionField"] = "preserve-me";
        payload["data"]!["actions"]![0]!["subActions"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "opaque-wire-id", ["type"] = 456789, ["enabled"] = true, ["unknown"] = 42,
        });
        var analysis = StreamerBotImportService.Analyze(StreamerBotEnvelopeCodec.Encode(payload));

        Assert.True(analysis.CanCreateProject);
        Assert.Equal(version, analysis.Summary!.PayloadVersion);
        Assert.Single(analysis.CSharpSources);
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
            var runner = new RejectingRunner();
            var build = await new FoundryBuildOrchestrator(runner).BuildAsync(loaded.Manifest, created.ProjectPath!);
            Assert.True(build.IsSuccess, string.Join(Environment.NewLine, build.Diagnostics.Select(item => item.Message)));
            Assert.Equal(0, runner.InvocationCount);
            Assert.DoesNotContain(build.PackageIntermediate!.Artifacts, item => item.Kind == FoundryPackageArtifactKinds.ManagedAssembly);
            Assert.Contains(build.PackageIntermediate.Artifacts, item => item.Kind == FoundryPackageArtifactKinds.StreamerBotImportReport);
            var package = build.PackageIntermediate.Artifacts.Single(item => item.Kind == FoundryPackageArtifactKinds.StreamerBotPackage);
            var reexported = StreamerBotEnvelopeCodec.Decode(await File.ReadAllTextAsync(Path.Combine(root, "build", package.Path)));
            Assert.Equal("preserve-me", reexported["data"]!["actions"]![0]!["unknownActionField"]!.GetValue<string>());
            Assert.Equal(42, reexported["data"]!["actions"]![0]!["subActions"]![2]!["unknown"]!.GetValue<int>());
            var editedCode = Encoding.UTF8.GetString(Convert.FromBase64String(
                reexported["data"]!["actions"]![0]!["subActions"]![1]!["byteCode"]!.GetValue<string>()));
            Assert.Contains("edited safely", editedCode, StringComparison.Ordinal);
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
    public void DefinitionV1MigratesDeterministicallyToV2()
    {
        const string json = """{"schemaVersion":1,"metadata":{"author":"A","description":"B"},"queues":[],"commands":[],"actions":[]}""";
        var first = StreamerBotDefinitionLoader.Load(json);
        var second = StreamerBotDefinitionLoader.Load(json);
        Assert.True(first.IsSuccess);
        Assert.Equal(2, first.Definition!.SchemaVersion);
        Assert.Equal(StreamerBotDefinitionLoader.Serialize(first.Definition), StreamerBotDefinitionLoader.Serialize(second.Definition!));
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
