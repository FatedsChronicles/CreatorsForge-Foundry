using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Core.Compatibility;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class StreamerBotOperationCatalogueTests
{
    [Fact]
    public void EmbeddedCatalogueContainsOnlyReviewedVerifiedFoundationOperations()
    {
        var service = StreamerBotOperationCatalogueService.LoadEmbedded();

        Assert.Equal("streamerbot-operations-verified-v1", service.Catalogue.Revision);
        Assert.Equal(3, service.Catalogue.Operations.Count);
        Assert.Equal(401, service.Get("streamerbot.trigger.command").NativeType);
        Assert.Equal(702, service.Get("streamerbot.trigger.test").NativeType);
        Assert.Equal(123, service.Get("streamerbot.subaction.set-argument").NativeType);
        Assert.All(service.Catalogue.Operations, operation =>
            Assert.Equal(FoundryStreamerBotProfiles.Ordered.OrderBy(value => value),
                operation.Profiles.OrderBy(value => value)));
    }

    [Fact]
    public void SearchFiltersByKindProfileAndText()
    {
        var service = StreamerBotOperationCatalogueService.LoadEmbedded();

        var triggers = service.Search("trigger", FoundryStreamerBotProfiles.Stable107, "command");
        var subActions = service.Search("subAction", FoundryStreamerBotProfiles.Stable104, "argument");

        Assert.Equal("streamerbot.trigger.command", Assert.Single(triggers).Id);
        Assert.Equal("streamerbot.subaction.set-argument", Assert.Single(subActions).Id);
        Assert.Empty(service.Search("subAction", "unknown-profile"));
    }

    [Fact]
    public void LoaderRejectsUnknownFieldsProfilesAndDuplicateIds()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "revision": "broken",
              "operations": [
                { "id": "same", "entityKind": "trigger", "modelKind": "test", "category": "Core", "name": "One", "description": "One", "nativeType": 702, "outputMode": "native", "profiles": ["unknown"], "fields": [], "argumentsConsumed": [], "argumentsProduced": [] },
                { "id": "same", "entityKind": "trigger", "modelKind": "test", "category": "Core", "name": "Two", "description": "Two", "nativeType": 702, "outputMode": "native", "profiles": ["1.0.7-stable"], "fields": [{ "id": "x", "label": "X", "type": "secret", "required": false }], "argumentsConsumed": [], "argumentsProduced": [] }
              ]
            }
            """;

        var result = StreamerBotOperationCatalogueService.Load(json);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Contains("duplicated", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("profile", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("invalid field", StringComparison.Ordinal));
    }

    [Fact]
    public void DefinitionDiagnosticsRejectTamperedNativeMapping()
    {
        var definition = StreamerBotStableV23AdapterTests.CreateDefinition();
        definition = definition with
        {
            Actions = [definition.Actions[0] with
            {
                SubActions = [definition.Actions[0].SubActions[0] with { SourceType = 999 }],
            }],
        };

        var diagnostics = StreamerBotDefinitionDiagnostics.Analyze(
            definition, FoundryStreamerBotProfiles.Stable107);

        Assert.Contains(diagnostics, item => item.Code == "SBD1011");
    }
}
