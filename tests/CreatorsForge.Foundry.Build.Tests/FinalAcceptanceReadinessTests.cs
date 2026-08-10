using System.Text.Json;
using System.Text.RegularExpressions;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class FinalAcceptanceReadinessTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "FinalAcceptance");

    private static string SampleRoot =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "PrivateAlpha", "Samples");

    [Fact]
    public void StableV1ReleaseScriptsCoverPackagingVerificationAndAcceptance()
    {
        var package = ReadFixture("package-v1.ps1");
        var verify = ReadFixture("verify-v1-release.ps1");
        var acceptance = ReadFixture("invoke-final-acceptance.ps1");

        Assert.Contains("1.0.0", package, StringComparison.Ordinal);
        Assert.Contains("package-desktop.ps1", package, StringComparison.Ordinal);
        Assert.Contains("v1-release-manifest.json", package, StringComparison.Ordinal);
        Assert.Contains("SHA256", package, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LICENSE", package, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHANGELOG", package, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v1-matrix.json", package, StringComparison.Ordinal);

        Assert.Contains("ExpectedManifestSha256", verify, StringComparison.Ordinal);
        Assert.Contains("v1-release-manifest.json", verify, StringComparison.Ordinal);
        Assert.Contains("Modified asset", verify, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("final-acceptance-report.json", acceptance, StringComparison.Ordinal);
        Assert.Contains("streamerbot", acceptance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("obsstudio", acceptance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build", acceptance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test", acceptance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("package", acceptance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FinalAcceptanceSchemasAreStrictVersionOneJsonContracts()
    {
        foreach (var name in new[]
        {
            "foundry-v1-release-manifest-v1.schema.json",
            "foundry-final-acceptance-report-v1.schema.json",
        })
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "Schemas", name)));
            var root = schema.RootElement;
            Assert.Equal("object", root.GetProperty("type").GetString());
            Assert.False(root.GetProperty("additionalProperties").GetBoolean());
            Assert.Contains(root.GetProperty("required").EnumerateArray(),
                item => item.GetString() == "schemaVersion");
            Assert.Equal(1, root.GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetInt32());
        }
    }

    [Fact]
    public void FinalAcceptanceDocumentationCoversEveryV1ExitGate()
    {
        var docs = Directory.EnumerateFiles(
                Path.Combine(FixtureRoot, "Docs"),
                "*.md",
                SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

        Assert.Contains("README.md", docs.Keys);
        Assert.Contains("acceptance-checklist.md", docs.Keys);
        var combined = string.Join('\n', docs.Values);
        foreach (var required in new[]
        {
            "clean machine",
            "Streamer.bot",
            "OBS Studio",
            "build",
            "package",
            "deploy",
            "verify",
            "update",
            "repair",
            "uninstall",
            "user-owned",
            "identical",
            "offline",
        })
        {
            Assert.Contains(required, combined, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void V1CompatibilityMatrixContainsExactlyTheAcceptedRuntimes()
    {
        using var matrix = JsonDocument.Parse(ReadFixture("v1-matrix.json"));
        var root = matrix.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("v1", root.GetProperty("productChannel").GetString());
        Assert.True(root.GetProperty("supportPolicy").GetProperty("exactVersionsOnly").GetBoolean());

        var targets = root.GetProperty("targets").EnumerateArray().ToArray();
        var actual = targets
            .Select(item => (
                Provider: item.GetProperty("provider").GetString(),
                Profile: item.GetProperty("profile").GetString(),
                Runtime: item.GetProperty("runtimeVersion").GetString()))
            .ToHashSet();
        var expected = new HashSet<(string? Provider, string? Profile, string? Runtime)>
        {
            ("streamerbot", "1.0.4-stable", "1.0.4"),
            ("streamerbot", "1.0.5-alpha.34", "1.0.5-alpha.34"),
            ("streamerbot", "1.0.5-beta.1", "1.0.5-beta.1"),
            ("streamerbot", "1.0.5-beta.6", "1.0.5-beta.6"),
            ("streamerbot", "1.0.7-stable", "1.0.7"),
            ("obsstudio", "32.x-windows-x64", "32.1.2"),
            ("obsstudio", "32.x-windows-x64", "32.2.1"),
        };

        Assert.True(expected.SetEquals(actual));
        Assert.All(targets, target =>
        {
            Assert.Equal("verified", target.GetProperty("status").GetString());
            Assert.NotEmpty(target.GetProperty("automatedGates").EnumerateArray());
            Assert.NotEmpty(target.GetProperty("realHostGates").EnumerateArray());
        });
        Assert.All(
            targets.Where(target => target.GetProperty("provider").GetString() == "obsstudio"),
            target => Assert.True(target.GetProperty("exactVersionOnly").GetBoolean()));
    }

    [Fact]
    public void RepresentativeSampleVersionsAgreeWithWorkspaceAndChangelogs()
    {
        using var workspace = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(SampleRoot, "PrivateAlphaSamples.foundryworkspace")));
        var projectPaths = workspace.RootElement.GetProperty("projects")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.Equal(
            [
                "StreamerBotCreatorToolkit/StreamerBotCreatorToolkit.foundryproj",
                "ObsConfigurableFilter/ObsConfigurableFilter.foundryproj",
            ],
            projectPaths);

        foreach (var name in new[] { "StreamerBotCreatorToolkit", "ObsConfigurableFilter" })
        {
            using var project = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(SampleRoot, name + ".foundryproj")));
            var version = project.RootElement.GetProperty("version").GetString();
            Assert.Equal("1.0.1", version);

            var changelog = File.ReadAllText(Path.Combine(SampleRoot, name + ".CHANGELOG.md"));
            Assert.Matches(
                new Regex($"^##\\s+{Regex.Escape(version!)}\\s*$", RegexOptions.Multiline),
                changelog);
        }
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(FixtureRoot, name));
}
