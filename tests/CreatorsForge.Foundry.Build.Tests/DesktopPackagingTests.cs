namespace CreatorsForge.Foundry.Build.Tests;

public sealed class DesktopPackagingTests
{
    [Fact]
    public void NativeInstallerUsesStableIdentityProgramFilesAndSafeUninstall()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Desktop");
        var installer = File.ReadAllText(Path.Combine(root, "FoundrySetup.iss"));
        Assert.Contains("AppId={{D9786586-E859-4A81-B8AB-906A99E00510}", installer, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={code:GetDefaultInstallDir}", installer, StringComparison.Ordinal);
        Assert.Contains("Result := ExpandConstant('{autopf}\\Creators Forge\\Foundry')", installer, StringComparison.Ordinal);
        Assert.Contains("{localappdata}\\Programs\\Creators Forge Foundry", installer, StringComparison.Ordinal);
        Assert.Contains("FileExists(AddBackslash(LegacyInstallDir) + 'install-receipt.json')", installer, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=admin", installer, StringComparison.Ordinal);
        Assert.Contains("UsePreviousAppDir=yes", installer, StringComparison.Ordinal);
        Assert.Contains("CloseApplications=yes", installer, StringComparison.Ordinal);
        Assert.Contains("CheckForMutexes('CreatorsForge.Foundry')", installer, StringComparison.Ordinal);
        Assert.Contains("install-receipt.json", installer, StringComparison.Ordinal);
        Assert.Contains("InitializeUninstall", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("{localappdata}\\Creators Forge\\Foundry", installer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPackagerProducesSelfContainedArchiveAndUpdateManifest()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Desktop", "package-desktop.ps1"));
        Assert.Contains("--self-contained true", script, StringComparison.Ordinal);
        Assert.Contains("SHA256", script, StringComparison.Ordinal);
        Assert.Contains("foundry-update.json", script, StringComparison.Ordinal);
        Assert.Contains("FoundrySetup.iss", script, StringComparison.Ordinal);
        Assert.Contains("-Setup.exe", script, StringComparison.Ordinal);
        Assert.Contains("-Update.exe", script, StringComparison.Ordinal);
        Assert.Contains("JRSoftware.InnoSetup", script, StringComparison.Ordinal);
        Assert.Contains("SignToolCommand", script, StringComparison.Ordinal);
        Assert.Contains("/Sfoundry=", script, StringComparison.Ordinal);
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
