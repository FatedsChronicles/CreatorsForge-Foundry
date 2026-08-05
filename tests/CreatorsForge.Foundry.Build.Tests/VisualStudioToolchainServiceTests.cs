namespace CreatorsForge.Foundry.Build.Tests;

public sealed class VisualStudioToolchainServiceTests
{
    [Fact]
    public void ManualInstallationRootSelectsNewestCompleteX64Toolset()
    {
        using var installation = TemporaryVisualStudioInstallation.Create(
            "14.38.33130",
            "14.42.34433");

        var result = VisualStudioToolchainService.InspectInstallation(
            installation.Root,
            "Visual Studio Community",
            "17.12.0");

        Assert.True(result.IsReady);
        Assert.Equal("14.42.34433", result.MsvcVersion);
        Assert.Equal("Visual Studio Community", result.DisplayName);
        Assert.EndsWith(
            Path.Combine("14.42.34433", "bin", "Hostx64", "x64", "cl.exe"),
            result.CompilerPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.All(
            [result.CompilerPath, result.LinkerPath, result.LibrarianPath, result.DumpbinPath, result.DeveloperCommandPath],
            path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void ManualInstallationRootExplainsMissingCppWorkload()
    {
        var root = Path.Combine(Path.GetTempPath(), "FoundryVisualStudioTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);

            var result = VisualStudioToolchainService.InspectInstallation(root);

            Assert.False(result.IsReady);
            Assert.Contains(result.Problems, item => item.Contains("Desktop development with C++", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    internal sealed class TemporaryVisualStudioInstallation : IDisposable
    {
        private TemporaryVisualStudioInstallation(string root) => Root = root;

        public string Root { get; }

        public static TemporaryVisualStudioInstallation Create(params string[] versions)
        {
            var root = Path.Combine(Path.GetTempPath(), "FoundryVisualStudioTests", Guid.NewGuid().ToString("N"));
            foreach (var version in versions)
            {
                var tools = Path.Combine(root, "VC", "Tools", "MSVC", version, "bin", "Hostx64", "x64");
                Directory.CreateDirectory(tools);
                foreach (var name in new[] { "cl.exe", "link.exe", "lib.exe", "dumpbin.exe" })
                {
                    File.WriteAllText(Path.Combine(tools, name), name);
                }
            }
            var commonTools = Path.Combine(root, "Common7", "Tools");
            Directory.CreateDirectory(commonTools);
            File.WriteAllText(Path.Combine(commonTools, "VsDevCmd.bat"), "@echo off");
            return new(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
