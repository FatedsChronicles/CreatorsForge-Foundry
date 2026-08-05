namespace CreatorsForge.Foundry.Build.Tests;

public sealed class NativeToolchainReadinessServiceTests
{
    [Fact]
    public void InstalledCMakeIsResolvedAndMeetsMinimumVersion()
    {
        var path = NativeToolchainReadinessService.ResolveCMakeExecutable();

        Assert.NotNull(path);
        var status = NativeToolchainReadinessService.InspectCMake(path);
        Assert.True(status.IsReady);
        Assert.Equal(Path.GetFullPath(path), status.ExecutablePath);
    }

    [Fact]
    public void SelectedCMakeMustExistAndBeNamedCmakeExe()
    {
        var status = NativeToolchainReadinessService.InspectCMake(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "not-cmake.exe"));

        Assert.False(status.IsReady);
        Assert.Null(status.ExecutablePath);
    }

    [Fact]
    public void WindowsSdkRequiresCompleteMatchingX64SurfaceAndChoosesNewestVersion()
    {
        using var sdk = TemporaryWindowsSdk.Create("10.0.19041.0", "10.0.26100.0");

        var status = NativeToolchainReadinessService.InspectWindowsSdk(sdk.Root);

        Assert.True(status.IsReady);
        Assert.Equal("10.0.26100.0", status.Version);
        Assert.EndsWith(Path.Combine("10.0.26100.0", "x64", "rc.exe"), status.ResourceCompilerPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsSdkExplainsIncompleteInstallation()
    {
        var root = Path.Combine(Path.GetTempPath(), "FoundryWindowsSdkTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Include"));
            Directory.CreateDirectory(Path.Combine(root, "Lib"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));

            var status = NativeToolchainReadinessService.InspectWindowsSdk(root);

            Assert.False(status.IsReady);
            Assert.Contains("No complete Windows SDK", status.Details, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TemporaryWindowsSdk : IDisposable
    {
        private TemporaryWindowsSdk(string root) => Root = root;
        public string Root { get; }

        public static TemporaryWindowsSdk Create(params string[] versions)
        {
            var root = Path.Combine(Path.GetTempPath(), "FoundryWindowsSdkTests", Guid.NewGuid().ToString("N"));
            foreach (var version in versions)
            {
                foreach (var relative in new[]
                         {
                             Path.Combine("Include", version, "um", "Windows.h"),
                             Path.Combine("Include", version, "shared", "sdkddkver.h"),
                             Path.Combine("Lib", version, "um", "x64", "kernel32.lib"),
                             Path.Combine("bin", version, "x64", "rc.exe"),
                             Path.Combine("bin", version, "x64", "mt.exe"),
                         })
                {
                    var path = Path.Combine(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, Path.GetFileName(path));
                }
            }
            return new(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
