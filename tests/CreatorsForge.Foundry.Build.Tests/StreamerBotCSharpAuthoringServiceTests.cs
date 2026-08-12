using System.Text;
using CreatorsForge.Foundry.Build.StreamerBot;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class StreamerBotCSharpAuthoringServiceTests
{
    [Fact]
    public void SetArgumentConversionIsDeterministicEscapedAndProvenanced()
    {
        var source = new StreamerBotSubAction(
            "set-message", "setArgument", true, "quote\"name", "line1\nC:\\temp", false,
            SourceType: 123, Weight: 2);

        var first = StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(source, "hello");
        var second = StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(source, "hello");

        Assert.Equal(first, second);
        Assert.Equal("executeCSharp", first.ConvertedSubAction.Kind);
        Assert.Equal("streamerbot/code/hello/set-message.cs", first.RelativePath);
        Assert.StartsWith("using System;\n\n", first.Source);
        Assert.Contains("CPH.SetArgument(\"quote\\\"name\", \"line1\\nC:\\\\temp\");", first.Source);
        Assert.Equal(StreamerBotCSharpAuthoringService.SetArgumentRevision,
            first.ConvertedSubAction.Generation!.Revision);
        Assert.Equal(StreamerBotCSharpAuthoringService.Sha256(first.Source),
            first.ConvertedSubAction.Generation.SourceSha256);
    }

    [Fact]
    public void AutoTypeAndReadOnlyConversionsAreBlocked()
    {
        var autoType = new StreamerBotSubAction(
            "argument", "setArgument", true, "value", "1", true);
        var readOnly = autoType with { AutoType = false, ReadOnly = true };

        Assert.Contains("Auto Type", Assert.Throws<InvalidOperationException>(() =>
            StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(autoType, "action")).Message);
        Assert.Contains("Read-only", Assert.Throws<InvalidOperationException>(() =>
            StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(readOnly, "action")).Message);
    }

    [Fact]
    public void GeneratedSourceBecomesDetachedWithoutBeingOverwritten()
    {
        var original = new StreamerBotSubAction(
            "argument", "setArgument", true, "value", "hello", false);
        var preview = StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(original, "action");

        Assert.Equal(StreamerBotGeneratedSourceState.Generated,
            StreamerBotCSharpAuthoringService.GetState(preview.ConvertedSubAction, preview.Source));
        Assert.Equal(StreamerBotGeneratedSourceState.Detached,
            StreamerBotCSharpAuthoringService.GetState(preview.ConvertedSubAction, preview.Source + "// edit\n"));
        Assert.Equal(StreamerBotGeneratedSourceState.Missing,
            StreamerBotCSharpAuthoringService.GetState(preview.ConvertedSubAction, null));
    }

    [Fact]
    public void ManualSourceIsWrittenOnceToAConfinedPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "foundry-csharp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var created = StreamerBotCSharpAuthoringService.CreateManual("code", "action");
            StreamerBotCSharpAuthoringService.WriteNewSource(root, created.SubAction.SourcePath!, created.Source);
            var path = Path.Combine(root, "streamerbot", "code", "action", "code.cs");
            Assert.Equal(created.Source, File.ReadAllText(path));
            Assert.StartsWith("using System;\n\npublic class CPHInline", created.Source);
            Assert.Contains("\t\t// your main code goes here", created.Source);
            Assert.False(StreamerBotCSharpAuthoringService.WriteNewSourceOrVerify(
                root, created.SubAction.SourcePath!, created.Source));
            Assert.Throws<IOException>(() => StreamerBotCSharpAuthoringService.WriteNewSource(
                root, created.SubAction.SourcePath!, "replacement"));
            Assert.Throws<IOException>(() => StreamerBotCSharpAuthoringService.WriteNewSourceOrVerify(
                root, created.SubAction.SourcePath!, "replacement"));
            Assert.Throws<InvalidDataException>(() =>
                StreamerBotCSharpAuthoringService.ResolveConfinedSourcePath(root, "../outside.cs"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SchemaFourMigratesDeterministicallyToFive()
    {
        const string schemaFour = """
            {"schemaVersion":4,"metadata":{"author":"A","description":"B","minimumVersion":"1.0.4"},"queues":[],"commands":[],"actions":[],"resources":[]}
            """;

        var first = StreamerBotDefinitionLoader.Load(schemaFour);
        var second = StreamerBotDefinitionLoader.Load(schemaFour);

        Assert.True(first.IsSuccess);
        Assert.Equal(StreamerBotDefinition.CurrentSchemaVersion, first.Definition!.SchemaVersion);
        Assert.Equal(StreamerBotDefinitionLoader.Serialize(first.Definition),
            StreamerBotDefinitionLoader.Serialize(second.Definition!));
    }

    [Fact]
    public void StableV23EmbedsManualCSharpDeterministically()
    {
        var root = Path.Combine(Path.GetTempPath(), "foundry-encode-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var created = StreamerBotCSharpAuthoringService.CreateManual("code", "hello");
            StreamerBotCSharpAuthoringService.WriteNewSource(root, created.SubAction.SourcePath!, created.Source);
            var definition = StreamerBotStableV23AdapterTests.CreateDefinition();
            definition = definition with
            {
                Actions = [definition.Actions[0] with
                {
                    SubActions = [created.SubAction],
                }],
            };

            var first = StreamerBotStableV23Adapter.Encode(definition, "com.example.code", "Code", "1.0.0",
                "public class CPHInline {}\n", root);
            var second = StreamerBotStableV23Adapter.Encode(definition, "com.example.code", "Code", "1.0.0",
                "public class CPHInline {}\n", root);
            var payload = StreamerBotStableV23Adapter.Decode(first.ImportCode);
            var wire = payload["data"]!["actions"]![0]!["subActions"]![0]!;

            Assert.Equal(first.ImportCode, second.ImportCode);
            Assert.Equal(99999, wire["type"]!.GetValue<int>());
            Assert.Equal(created.Source, Encoding.UTF8.GetString(
                Convert.FromBase64String(wire["byteCode"]!.GetValue<string>())));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void AbsoluteCompilerReferenceIsAProjectValidationError()
    {
        var created = StreamerBotCSharpAuthoringService.CreateManual("code", "hello").SubAction with
        {
            References = [@"C:\developer\private.dll"],
        };
        var definition = StreamerBotStableV23AdapterTests.CreateDefinition();
        definition = definition with
        {
            Actions = [definition.Actions[0] with { SubActions = [created] }],
        };

        var diagnostics = StreamerBotDefinitionDiagnostics.Analyze(definition);

        Assert.Contains(diagnostics, item => item.Code == "SBD1019" &&
            item.Severity == StreamerBotDefinitionDiagnosticSeverity.Error);
        Assert.Contains(StreamerBotDefinitionLoader.Validate(definition),
            error => error.Contains("SBD1019", StringComparison.Ordinal));
    }
}
