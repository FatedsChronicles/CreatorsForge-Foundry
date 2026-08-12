using CreatorsForge.Foundry.Build.StreamerBot;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class StreamerBotPortabilityServiceTests
{
    [Fact]
    public void SchemaThreeMigratesDeterministicallyToSchemaFour()
    {
        const string source = """
            {
              "schemaVersion": 3,
              "metadata": { "author": "Test", "description": "Legacy" },
              "queues": [],
              "commands": [],
              "actions": []
            }
            """;

        var first = StreamerBotDefinitionLoader.Load(source);
        var second = StreamerBotDefinitionLoader.Load(source);

        Assert.True(first.IsSuccess);
        Assert.Equal(5, first.Definition!.SchemaVersion);
        Assert.Empty(first.Definition.Resources);
        Assert.Equal(StreamerBotDefinitionLoader.Serialize(first.Definition),
            StreamerBotDefinitionLoader.Serialize(second.Definition!));
    }

    [Fact]
    public void ReportIsDeterministicAndOmitsSuggestedValues()
    {
        var definition = DefinitionWithResources(
            new("z-file", "Overlay file", "localFile", true,
                StreamerBotResourcePortability.ConfirmAfterImport,
                SuggestedValue: @"C:\Overlays\index.html",
                Bindings: [new("action", "hello", "overlayPath")]),
            new("a-scene", "Live scene", "obsScene", false,
                StreamerBotResourcePortability.ReconnectByName,
                SuggestedValue: "Starting Soon",
                Bindings: [new("trigger", "test", "sceneName")]));

        var first = StreamerBotPortabilityService.Serialize(
            StreamerBotPortabilityService.CreateReport(definition));
        var second = StreamerBotPortabilityService.Serialize(
            StreamerBotPortabilityService.CreateReport(definition));

        Assert.Equal(first, second);
        Assert.DoesNotContain(@"C:\Overlays\index.html", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Starting Soon", first, StringComparison.Ordinal);
        Assert.True(first.IndexOf("a-scene", StringComparison.Ordinal) <
                    first.IndexOf("z-file", StringComparison.Ordinal));
    }

    [Fact]
    public void ResourcesAndBindingsSurviveSaveAndReopen()
    {
        var original = DefinitionWithResources(
            new StreamerBotResourceDefinition("scene", "Starting scene", "obsScene", true,
                StreamerBotResourcePortability.ReconnectByName,
                "Scene selected after import", "Starting Soon", "^.{1,128}$",
                [new("action", "hello", "sceneName")]));

        var reopened = StreamerBotDefinitionLoader.Load(
            StreamerBotDefinitionLoader.Serialize(original));

        Assert.True(reopened.IsSuccess);
        var resource = Assert.Single(reopened.Definition!.Resources);
        Assert.Equal("scene", resource.Id);
        Assert.Equal("Starting scene", resource.Name);
        Assert.Equal("obsScene", resource.Type);
        Assert.True(resource.Required);
        Assert.Equal(StreamerBotResourcePortability.ReconnectByName, resource.Portability);
        Assert.Equal("Starting Soon", resource.SuggestedValue);
        Assert.Equal(new StreamerBotResourceBinding("action", "hello", "sceneName"),
            Assert.Single(resource.Bindings!));
    }

    [Fact]
    public void DiagnosticsRejectSecretsWithoutLeakingTheirValues()
    {
        const string secret = "ghp_abcdefghijklmnopqrstuvwxyz123456";
        var definition = DefinitionWithResources(
            new StreamerBotResourceDefinition("token", "API token", "integrationConnection", true,
                StreamerBotResourcePortability.ManualConfiguration,
                SuggestedValue: secret,
                Bindings: [new("action", "hello", "connection")]));

        var diagnostics = StreamerBotDefinitionDiagnostics.Analyze(definition);

        var diagnostic = Assert.Single(diagnostics, item => item.Code == "SBD1015");
        Assert.DoesNotContain(secret, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(StreamerBotDefinitionLoader.Validate(definition),
            item => item.Contains("SBD1015", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnosticsIdentifyInvalidBindingsAndPortability()
    {
        var definition = DefinitionWithResources(
            new StreamerBotResourceDefinition("file", "Machine file", "localFile", true,
                StreamerBotResourcePortability.Portable,
                SuggestedValue: @"D:\Private\file.txt",
                Bindings: [new("action", "missing", "path")]));

        var diagnostics = StreamerBotDefinitionDiagnostics.Analyze(definition);

        Assert.Contains(diagnostics, item => item.Code == "SBD1013");
        Assert.Contains(diagnostics, item => item.Code == "SBD1016");
    }

    [Fact]
    public void PortabilityWarningsDoNotBlockValidationOrExport()
    {
        var definition = DefinitionWithResources(
            new StreamerBotResourceDefinition("file", "Machine file", "localFile", true,
                StreamerBotResourcePortability.ConfirmAfterImport,
                SuggestedValue: @"D:\Disposable\file.txt",
                Bindings: [new("action", "hello", "path")]));

        var diagnostics = StreamerBotDefinitionDiagnostics.Analyze(definition);

        Assert.Contains(diagnostics, item => item.Code == "SBD2007" &&
                                             item.Severity == StreamerBotDefinitionDiagnosticSeverity.Warning);
        Assert.Contains(diagnostics, item => item.Code == "SBD2008" &&
                                             item.Severity == StreamerBotDefinitionDiagnosticSeverity.Warning);
        Assert.DoesNotContain(diagnostics,
            item => item.Severity == StreamerBotDefinitionDiagnosticSeverity.Error);
        Assert.Empty(StreamerBotDefinitionLoader.Validate(definition));
        Assert.NotEmpty(StreamerBotStableV23Adapter.Encode(definition,
            "com.creatorsforge.warning-test", "Warning test", "1.1.0-beta.1",
            "public class CPHInline { public bool Execute() => true; }\n").ImportCode);
    }

    private static StreamerBotDefinition DefinitionWithResources(
        params StreamerBotResourceDefinition[] resources)
    {
        var definition = StreamerBotStableV23AdapterTests.CreateDefinition();
        return definition with { Resources = resources };
    }
}
