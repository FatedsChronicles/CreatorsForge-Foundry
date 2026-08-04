namespace CreatorsForge.Foundry.Build.Tests;

public sealed class DesktopPackagingTests
{
    [Fact]
    public void InstallerAndUninstallerDeclareOwnershipAndRunningProcessGuards()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Desktop");
        var installer = File.ReadAllText(Path.Combine(root, "install-foundry.ps1"));
        var uninstaller = File.ReadAllText(Path.Combine(root, "uninstall-foundry.ps1"));
        Assert.Contains("install-receipt.json", installer, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'CreatorsForge.Foundry'", installer, StringComparison.Ordinal);
        Assert.Contains(".previous", installer, StringComparison.Ordinal);
        Assert.Contains("install-receipt.json", uninstaller, StringComparison.Ordinal);
        Assert.Contains("RemoveUserData", uninstaller, StringComparison.Ordinal);
        Assert.Contains("preserved", uninstaller, StringComparison.Ordinal);
        Assert.Contains("GetTempPath", uninstaller, StringComparison.Ordinal);
        Assert.Contains("Set-Location", uninstaller, StringComparison.Ordinal);
        Assert.Contains("TrimEnd", uninstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("TrimEndingDirectorySeparator", uninstaller, StringComparison.Ordinal);
        Assert.Contains("installed file is still in use", uninstaller, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPackagerProducesSelfContainedArchiveAndUpdateManifest()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Desktop", "package-desktop.ps1"));
        Assert.Contains("--self-contained true", script, StringComparison.Ordinal);
        Assert.Contains("SHA256", script, StringComparison.Ordinal);
        Assert.Contains("foundry-update.json", script, StringComparison.Ordinal);
        Assert.Contains("privacy-and-offline.md", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopPackagerUsesDeterministicArchiveAndManifestInputs()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Desktop", "package-desktop.ps1"));

        Assert.Contains("Sort-Object", script, StringComparison.Ordinal);
        Assert.Contains("LastWriteTime", script, StringComparison.Ordinal);
        Assert.Contains("1980", script, StringComparison.Ordinal);
        Assert.Contains("UTF8Encoding", script, StringComparison.Ordinal);
        Assert.Contains("PublishedAtUtc", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFromDirectory", script, StringComparison.Ordinal);
    }
}
