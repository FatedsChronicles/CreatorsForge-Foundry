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
}
