namespace CreatorsForge.Foundry.Build.Tests;

public sealed class FoundryProductHealthServiceTests
{
    [Fact]
    public void ProductHealthReportsRequiredAndOptionalToolchainChecks()
    {
        var root = Path.Combine(Path.GetTempPath(), "FoundryProductHealth", Guid.NewGuid().ToString("N"));
        try
        {
            var health = FoundryProductHealthService.Inspect(root);
            Assert.Contains(health.Checks, item => item.Id == "windows" && item.Required);
            Assert.Contains(health.Checks, item => item.Id == "dotnet" && item.Required);
            Assert.Contains(health.Checks, item => item.Id == "storage" && item.Required);
            Assert.Contains(health.Checks, item => item.Id == "cmake" && !item.Required);
            Assert.Contains(health.Checks, item => item.Id == "msvc" && !item.Required);
            Assert.Contains(health.Checks, item => item.Id == "obs-sdk" && !item.Required);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ProductHealthUsesTheSelectedVisualStudioInstallation()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), "FoundryProductHealth", Guid.NewGuid().ToString("N"));
        using var installation = VisualStudioToolchainServiceTests.TemporaryVisualStudioInstallation.Create("14.42.34433");
        try
        {
            var health = FoundryProductHealthService.Inspect(stateRoot, installation.Root);

            var msvc = Assert.Single(health.Checks, item => item.Id == "msvc");
            Assert.True(msvc.IsReady);
            Assert.Contains("14.42.34433", msvc.Details, StringComparison.Ordinal);
            Assert.Contains(installation.Root, msvc.Details, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(stateRoot)) Directory.Delete(stateRoot, recursive: true);
        }
    }
}
