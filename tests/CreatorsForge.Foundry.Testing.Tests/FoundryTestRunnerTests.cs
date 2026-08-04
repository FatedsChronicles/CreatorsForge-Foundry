using System.Text.Json;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Testing;

namespace CreatorsForge.Foundry.Testing.Tests;

public sealed class FoundryTestRunnerTests
{
    [Fact]
    public async Task RunnerSimulatesArgumentsEventsLogsAndCphCalls()
    {
        using var fixture = new Fixture();
        fixture.WriteDefinition("""
            {
              "schemaVersion": 1,
              "provider": "streamerbot",
              "cases": [
                {
                  "id": "command-event",
                  "name": "Command event",
                  "event": {
                    "kind": "command",
                    "name": "!test",
                    "arguments": { "message": "prefix hello suffix", "count": 4 }
                  },
                  "assertions": [
                    { "kind": "returnEquals", "expected": true },
                    { "kind": "logContains", "expected": "hello" },
                    { "kind": "argumentEquals", "key": "count", "expected": 4 },
                    { "kind": "cphCallCount", "key": "CPH.LogInfo", "expected": 1 }
                  ]
                }
              ]
            }
            """);

        var result = await FoundryTestRunner.RunAsync(
            fixture.Manifest,
            fixture.ProjectPath,
            typeof(MockEntryPoint).Assembly.Location);

        Assert.True(result.IsSuccess, Format(result.Diagnostics));
        var testCase = Assert.Single(result.Cases);
        Assert.Equal(FoundryTestOutcome.Passed, testCase.Outcome);
        Assert.Equal("command", testCase.Event.Kind);
        Assert.Equal("!test", testCase.Event.Name);
        Assert.Equal("prefix hello suffix", Assert.Single(testCase.Logs));
        Assert.Equal("CPH.LogInfo", Assert.Single(testCase.CphCalls).Method);
        Assert.All(testCase.Assertions, item => Assert.Equal(FoundryTestOutcome.Passed, item.Outcome));
        Assert.True(File.Exists(result.ResultPath));
    }

    [Fact]
    public async Task FailedAssertionProducesStructuredFailure()
    {
        using var fixture = new Fixture();
        fixture.WriteDefinition("""
            {
              "schemaVersion": 1,
              "provider": "streamerbot",
              "cases": [{
                "id": "failure",
                "name": "Expected failure",
                "event": { "kind": "test", "arguments": { "message": "actual" } },
                "assertions": [{ "kind": "logContains", "expected": "different" }]
              }]
            }
            """);

        var result = await FoundryTestRunner.RunAsync(
            fixture.Manifest,
            fixture.ProjectPath,
            typeof(MockEntryPoint).Assembly.Location);

        Assert.False(result.IsSuccess);
        Assert.Equal(FoundryTestOutcome.Failed, result.Outcome);
        Assert.Equal(FoundryTestOutcome.Failed, Assert.Single(result.Cases).Outcome);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result.ResultPath!));
        Assert.Equal("failed", document.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task LoaderRejectsDuplicateCaseIdsAndUnsupportedAssertions()
    {
        using var fixture = new Fixture();
        fixture.WriteDefinition("""
            {
              "schemaVersion": 1,
              "provider": "streamerbot",
              "cases": [
                {
                  "id": "duplicate",
                  "name": "One",
                  "event": { "kind": "test", "arguments": {} },
                  "assertions": [{ "kind": "unknown", "expected": true }]
                },
                {
                  "id": "duplicate",
                  "name": "Two",
                  "event": { "kind": "test", "arguments": {} },
                  "assertions": [{ "kind": "returnEquals", "expected": true }]
                }
              ]
            }
            """);

        var result = await FoundryTestDefinitionLoader.LoadAsync(
            fixture.DefinitionPath,
            FoundryTestProviders.StreamerBot);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFT1006");
        Assert.Contains(result.Diagnostics, item => item.Code == "CFT1009");
    }

    [Fact]
    public async Task LoaderRejectsProviderSpecificAssertionInWrongAdapter()
    {
        using var fixture = new Fixture();
        fixture.WriteDefinition("""
            {
              "schemaVersion": 1,
              "provider": "streamerbot",
              "cases": [{
                "id": "wrong-provider",
                "name": "Wrong provider assertion",
                "event": { "kind": "command", "arguments": {} },
                "assertions": [{ "kind": "sourceCreated", "expected": true }]
              }]
            }
            """);

        var result = await FoundryTestDefinitionLoader.LoadAsync(
            fixture.DefinitionPath,
            FoundryTestProviders.StreamerBot);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFT1012");
    }

    [Theory]
    [InlineData("foundry-test-definition-v1.schema.json")]
    [InlineData("foundry-test-result-v1.schema.json")]
    [InlineData("foundry-compatibility-matrix-v1.schema.json")]
    [InlineData("obs-native-host-request-v1.schema.json")]
    [InlineData("obs-native-host-result-v1.schema.json")]
    public void TestSchemasUsePublishedJsonSchema(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Schemas",
            name)));
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            document.RootElement.GetProperty("$schema").GetString());
    }

    private static string Format(IEnumerable<object> values) =>
        string.Join(Environment.NewLine, values);

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "CreatorsForge.Foundry.Testing.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "tests"));
            ProjectPath = Path.Combine(Root, "Test.foundryproj");
            DefinitionPath = Path.Combine(Root, "tests", "foundry-tests.json");
            File.WriteAllText(ProjectPath, "{}");
        }

        public string Root { get; }
        public string ProjectPath { get; }
        public string DefinitionPath { get; }
        public FoundryProjectManifest Manifest { get; } = new()
        {
            Name = "Test",
            Id = "com.creatorsforge.tests.mock",
            Version = "1.0.0",
            Target = new() { Provider = "streamerbot", Profile = "1.0.4-stable" },
            Features = new() { MockRuntime = true },
            ManagedBuild = new() { AssemblyName = "Mock", Sources = ["src/Mock.cs"] },
            CphInlineBridge = new()
            {
                Contract = FoundryCphInlineBridge.SupportedContract,
                EntryType = "CreatorsForge.Foundry.Testing.Tests.MockEntryPoint",
                EntryMethod = "Execute",
            },
            TestDefinition = "tests/foundry-tests.json",
            Outputs = [FoundryOutputKinds.ManagedLibrary, FoundryOutputKinds.CphInlineBridge],
        };

        public void WriteDefinition(string json) => File.WriteAllText(DefinitionPath, json);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}

public static class MockEntryPoint
{
    public static bool Execute(
        IDictionary<string, object> arguments,
        Action<string> logInformation)
    {
        logInformation(arguments.TryGetValue("message", out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            : "default");
        return true;
    }
}
