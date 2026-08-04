using System.Text.Json;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Testing;

namespace CreatorsForge.Foundry.Testing.Tests;

public sealed class FoundryCompatibilityMatrixTests
{
    [Fact]
    public async Task StreamerBotMatrixRunsEveryDeclaredProfileThroughProviderOrchestrator()
    {
        using var fixture = new Fixture();
        fixture.WriteDefinition("""
            {
              "schemaVersion": 1,
              "provider": "streamerbot",
              "profiles": ["1.0.4-stable", "1.0.5-alpha.34", "1.0.5-beta.1", "1.0.5-beta.6"],
              "cases": [{
                "id": "matrix-command",
                "name": "Matrix command",
                "event": { "kind": "command", "arguments": { "message": "matrix" } },
                "assertions": [
                  { "kind": "returnEquals", "expected": true },
                  { "kind": "logContains", "expected": "matrix" }
                ]
              }]
            }
            """);

        var result = await FoundryCompatibilityMatrixRunner.RunAsync(new(
            fixture.Manifest,
            fixture.ProjectPath,
            typeof(MockEntryPoint).Assembly.Location,
            []));

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(4, result.Cells.Count);
        Assert.All(result.Cells, cell => Assert.Equal(FoundryTestOutcome.Passed, cell.Outcome));
        Assert.Equal(4, result.Cells.Select(cell => cell.Result.ResultPath).Distinct().Count());
        Assert.All(result.Cells, cell => Assert.True(File.Exists(cell.Result.ResultPath)));
        Assert.True(File.Exists(result.ResultPath));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result.ResultPath!));
        Assert.Equal(4, document.RootElement.GetProperty("cells").GetArrayLength());
    }

    [Fact]
    public async Task MatrixWithoutTestDefinitionReturnsStructuredErrorAndWritesAggregate()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Manifest with { TestDefinition = null };

        var result = await FoundryCompatibilityMatrixRunner.RunAsync(new(
            manifest,
            fixture.ProjectPath,
            typeof(MockEntryPoint).Assembly.Location,
            []));

        Assert.Equal(FoundryTestOutcome.Error, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFT3003");
        Assert.Empty(result.Cells);
        Assert.True(File.Exists(result.ResultPath));
    }

    [Fact]
    public async Task LoaderRejectsUnsupportedCompatibilityProfile()
    {
        using var fixture = new Fixture();
        fixture.WriteDefinition("""
            {
              "schemaVersion": 1,
              "provider": "streamerbot",
              "profiles": ["future-version"],
              "cases": [{
                "id": "profile-check",
                "name": "Profile check",
                "event": { "kind": "command", "arguments": {} },
                "assertions": [{ "kind": "returnEquals", "expected": true }]
              }]
            }
            """);

        var result = await FoundryTestDefinitionLoader.LoadAsync(
            fixture.DefinitionPath,
            FoundryTestProviders.StreamerBot);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFT1014");
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "CreatorsForge.Foundry.Matrix.Tests", Guid.NewGuid().ToString("N"));
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
            Name = "Matrix Test",
            Id = "com.creatorsforge.tests.matrix",
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
