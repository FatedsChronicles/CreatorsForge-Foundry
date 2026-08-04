using System.Text.Json;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class PrivateAlphaReadinessTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "PrivateAlpha");

    [Fact]
    public void PackagerCreatesInvitationOnlyVerifiedTesterBundle()
    {
        var package = File.ReadAllText(Path.Combine(FixtureRoot, "package-private-alpha.ps1"));
        var verify = File.ReadAllText(Path.Combine(FixtureRoot, "verify-private-alpha.ps1"));
        Assert.Contains("private-alpha-manifest.json", package, StringComparison.Ordinal);
        Assert.Contains("ExpectedManifestSha256", verify, StringComparison.Ordinal);
        Assert.Contains("Modified asset", verify, StringComparison.Ordinal);
        Assert.Contains("foundry-update.json", verify, StringComparison.Ordinal);
        Assert.Contains("TESTER-ONBOARDING.md", package, StringComparison.Ordinal);
        Assert.Contains("PrivateAlphaSamples.foundryworkspace", package, StringComparison.Ordinal);
        Assert.Contains("Copy-SampleSource", package, StringComparison.Ordinal);
        Assert.Contains("@('build', 'bin', 'obj')", package, StringComparison.Ordinal);
    }

    [Fact]
    public void PrivateAlphaDocumentationCoversEveryExitGate()
    {
        var docs = Directory.EnumerateFiles(Path.Combine(FixtureRoot, "Docs"), "*.md", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("README", docs.Keys);
        Assert.Contains("tester-onboarding", docs.Keys);
        Assert.Contains("update-strategy", docs.Keys);
        Assert.Contains("issue-reporting", docs.Keys);
        Assert.Contains("crash-recovery", docs.Keys);
        Assert.Contains("acceptance-checklist", docs.Keys);
        var combined = string.Join("\n", docs.Values);
        Assert.Contains("without a developer", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never uploaded automatically", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repair", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", combined, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("uninstall", combined, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublishedMatrixNamesEveryVerifiedRuntime()
    {
        var path = Path.Combine(FixtureRoot, "Compatibility", "private-alpha-matrix.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var targets = document.RootElement.GetProperty("targets").EnumerateArray().ToArray();
        Assert.Contains(targets, item => item.GetProperty("runtimeVersion").GetString() == "1.0.4");
        Assert.Contains(targets, item => item.GetProperty("runtimeVersion").GetString() == "1.0.5-alpha.34");
        Assert.Contains(targets, item => item.GetProperty("runtimeVersion").GetString() == "1.0.5-beta.1");
        Assert.Contains(targets, item => item.GetProperty("runtimeVersion").GetString() == "32.1.2");
        Assert.All(targets, item => Assert.Equal("verified", item.GetProperty("status").GetString()));
    }

    [Fact]
    public void RepresentativeSamplesAreMultiWorkflowTestedAndPublishable()
    {
        var sampleRoot = Path.Combine(FixtureRoot, "Samples");
        using var workspace = JsonDocument.Parse(File.ReadAllText(Path.Combine(sampleRoot, "PrivateAlphaSamples.foundryworkspace")));
        Assert.Equal(2, workspace.RootElement.GetProperty("projects").GetArrayLength());
        foreach (var name in new[] { "StreamerBotCreatorToolkit", "ObsConfigurableFilter" })
        {
            using var project = JsonDocument.Parse(File.ReadAllText(Path.Combine(sampleRoot, name + ".foundryproj")));
            Assert.Equal("1.0.1", project.RootElement.GetProperty("version").GetString());
            Assert.True(project.RootElement.TryGetProperty("publishing", out var publishing));
            Assert.False(publishing.GetProperty("signing").GetProperty("enabled").GetBoolean());
            using var tests = JsonDocument.Parse(File.ReadAllText(Path.Combine(sampleRoot, name + ".tests.json")));
            Assert.NotEmpty(tests.RootElement.GetProperty("cases").EnumerateArray());
        }
    }

    [Fact]
    public void PrivateAlphaSchemaAndPublishedJsonAreValidJson()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Schemas", "foundry-private-alpha-manifest-v1.schema.json")));
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        using var matrix = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureRoot, "Compatibility", "private-alpha-matrix.json")));
        Assert.Equal(1, matrix.RootElement.GetProperty("schemaVersion").GetInt32());
    }
}
