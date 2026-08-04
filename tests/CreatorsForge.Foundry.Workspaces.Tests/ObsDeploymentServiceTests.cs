using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces.Deployment;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class ObsDeploymentServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public void DiscoveryFindsOnlyObsInstallations()
    {
        using var fixture = new Fixture();
        var invalid = Path.Combine(fixture.Root, "not-obs");
        Directory.CreateDirectory(invalid);

        var installations = ObsInstallationDiscovery.Discover(
            [invalid, fixture.InstallationRoot]);

        var installation = Assert.Single(installations, item =>
            string.Equals(item.RootPath, fixture.InstallationRoot, StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith(
            Path.Combine("bin", "64bit", "obs64.exe"),
            installation.ExecutablePath,
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("config", "obs-studio", "logs"),
            installation.LogDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewIsBlockedWhileSelectedObsExecutableIsRunning()
    {
        using var fixture = new Fixture();
        var systemPing = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "ping.exe");
        File.Copy(systemPing, fixture.ObsExecutable, overwrite: true);
        fixture.WriteBuild("1.0.0", "module-v1", "data-v1");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fixture.ObsExecutable,
            Arguments = "127.0.0.1 -n 8 -w 1000",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            Assert.True(SpinWait.SpinUntil(
                () => ObsInstallationDiscovery.TryInspect(fixture.InstallationRoot) is { } installation &&
                      ObsInstallationDiscovery.IsRunning(installation),
                TimeSpan.FromSeconds(3)));

            var plan = await ObsDeploymentService.CreateInstallPlanAsync(
                Fixture.Manifest("1.0.0"),
                fixture.ProjectRoot,
                fixture.InstallationRoot);

            Assert.False(plan.IsReady);
            Assert.Contains(plan.Diagnostics, item => item.Code == "CFO1009");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    [Fact]
    public async Task InstallUpdateRollbackAndUninstallPreserveUserOwnedFiles()
    {
        using var fixture = new Fixture();
        var destination = fixture.ModuleDestination;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllTextAsync(destination, "user-owned-module");

        fixture.WriteBuild("1.0.0", "module-v1", "data-v1");
        var install = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True(install.IsReady, Format(install.Diagnostics));
        Assert.Equal(DeploymentOperation.Install, install.Operation);
        Assert.Equal(2, install.Files.Count);
        Assert.Contains(install.Files, item =>
            item.DestinationRelativePath.EndsWith("plugin-config.json", StringComparison.Ordinal));
        var installed = await ObsDeploymentService.ApplyAsync(install, install.Fingerprint);
        Assert.True(installed.IsSuccess, Format(installed.Diagnostics));
        Assert.Equal("module-v1", await File.ReadAllTextAsync(destination));
        Assert.Equal("data-v1", await File.ReadAllTextAsync(fixture.DataDestination));

        fixture.WriteBuild("2.0.0", "module-v2", "data-v2");
        var update = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("2.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True(update.IsReady, Format(update.Diagnostics));
        Assert.Equal(DeploymentOperation.Update, update.Operation);
        Assert.True((await ObsDeploymentService.ApplyAsync(update, update.Fingerprint)).IsSuccess);
        Assert.Equal("module-v2", await File.ReadAllTextAsync(destination));

        var rollback = await ObsDeploymentService.CreateRollbackPlanAsync(
            Fixture.Manifest("2.0.0"),
            fixture.InstallationRoot);
        Assert.True(rollback.IsReady, Format(rollback.Diagnostics));
        var rolledBack = await ObsDeploymentService.ApplyAsync(rollback, rollback.Fingerprint);
        Assert.True(rolledBack.IsSuccess, Format(rolledBack.Diagnostics));
        Assert.Equal("1.0.0", rolledBack.Receipt?.ProjectVersion);
        Assert.Equal("module-v1", await File.ReadAllTextAsync(destination));

        var uninstall = await ObsDeploymentService.CreateUninstallPlanAsync(
            Fixture.Manifest("1.0.0"),
            fixture.InstallationRoot);
        Assert.True(uninstall.IsReady, Format(uninstall.Diagnostics));
        Assert.True((await ObsDeploymentService.ApplyAsync(uninstall, uninstall.Fingerprint)).IsSuccess);
        Assert.Equal("user-owned-module", await File.ReadAllTextAsync(destination));
        Assert.False(File.Exists(fixture.DataDestination));
        Assert.False(File.Exists(fixture.ReceiptPath));
    }

    [Fact]
    public async Task HealthDetectsModifiedFilesAndProtectsUninstall()
    {
        using var fixture = new Fixture();
        fixture.WriteBuild("1.0.0", "module-v1", "data-v1");
        var plan = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True((await ObsDeploymentService.ApplyAsync(plan, plan.Fingerprint)).IsSuccess);
        await File.WriteAllTextAsync(fixture.ModuleDestination, "externally-modified");

        var health = await ObsDeploymentService.InspectHealthAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.ModifiedFiles, health.State);
        Assert.Contains(health.Files, item => item.State == DeploymentFileHealthState.Modified);

        var uninstall = await ObsDeploymentService.CreateUninstallPlanAsync(
            Fixture.Manifest("1.0.0"),
            fixture.InstallationRoot);
        Assert.False(uninstall.IsReady);
        Assert.Contains(uninstall.Diagnostics, item => item.Code == "CFO2102");
    }

    [Fact]
    public async Task HealthUsesPostInstallObsLog()
    {
        using var fixture = new Fixture();
        fixture.WriteBuild("1.0.0", "module-v1", "data-v1");
        var plan = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True((await ObsDeploymentService.ApplyAsync(plan, plan.Fingerprint)).IsSuccess);

        var beforeLog = await ObsDeploymentService.InspectHealthAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.LogNotObserved, beforeLog.State);

        var logPath = Path.Combine(fixture.LogDirectory, "2026-07-26 15-00-00.txt");
        await File.WriteAllTextAsync(
            logPath,
            "15:00:00.000: [Creators Forge] registered obs-deployment-test\n");
        File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow.AddSeconds(1));
        var healthy = await ObsDeploymentService.InspectHealthAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.Healthy, healthy.State);
        Assert.Equal(ObsLogHealthState.Healthy, healthy.Log.State);

        await File.WriteAllTextAsync(
            logPath,
            "15:01:00.000: Failed to load module obs-deployment-test.dll\n");
        File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow.AddSeconds(2));
        var failed = await ObsDeploymentService.InspectHealthAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.LogFailure, failed.State);
        Assert.NotEmpty(failed.Log.RelevantLines);
    }

    [Fact]
    public async Task PackageEntryOutsideModuleScopeIsRejected()
    {
        using var fixture = new Fixture();
        fixture.WriteBuild(
            "1.0.0",
            "module-v1",
            "data-v1",
            ("obs-plugins/64bit/unrelated.dll", "not-owned"));

        var plan = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);

        Assert.False(plan.IsReady);
        Assert.Contains(plan.Diagnostics, item => item.Code == "CFO1006");
        Assert.False(File.Exists(Path.Combine(
            fixture.InstallationRoot,
            "obs-plugins",
            "64bit",
            "unrelated.dll")));
    }

    [Fact]
    public async Task PackageMetadataMustMatchOpenProject()
    {
        using var fixture = new Fixture();
        fixture.WriteBuild("1.0.0", "module-v1", "data-v1");
        fixture.ReplacePackageMetadata("com.example.another-project", "1.0.0", "obs-deployment-test");

        var plan = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);

        Assert.False(plan.IsReady);
        Assert.Contains(plan.Diagnostics, item => item.Code == "CFO1006");
    }

    [Fact]
    public async Task RepeatedUpdateRetainsRemovedDataReceipt()
    {
        using var fixture = new Fixture();
        fixture.WriteBuild("1.0.0", "module-v1", "data-v1");
        var install = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("1.0.0"), fixture.ProjectRoot, fixture.InstallationRoot);
        Assert.True((await ObsDeploymentService.ApplyAsync(install, install.Fingerprint)).IsSuccess);

        fixture.WriteBuildWithoutData("2.0.0", "module-v2");
        var removeData = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("2.0.0"), fixture.ProjectRoot, fixture.InstallationRoot);
        Assert.True((await ObsDeploymentService.ApplyAsync(removeData, removeData.Fingerprint)).IsSuccess);
        Assert.False(File.Exists(fixture.DataDestination));

        fixture.WriteBuildWithoutData("3.0.0", "module-v3");
        var repeat = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("3.0.0"), fixture.ProjectRoot, fixture.InstallationRoot);
        var result = await ObsDeploymentService.ApplyAsync(repeat, repeat.Fingerprint);

        Assert.True(result.IsSuccess, Format(result.Diagnostics));
        Assert.Contains(result.Receipt!.Files, item =>
            item.DestinationRelativePath.EndsWith("plugin-config.json", StringComparison.Ordinal) &&
            !item.IsInstalled);
    }

    [Fact]
    public async Task UpdatePreservesNewlyClaimedPreExistingDataForUninstall()
    {
        using var fixture = new Fixture();
        fixture.WriteBuildWithoutData("1.0.0", "module-v1");
        var install = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("1.0.0"), fixture.ProjectRoot, fixture.InstallationRoot);
        Assert.True((await ObsDeploymentService.ApplyAsync(install, install.Fingerprint)).IsSuccess);

        Directory.CreateDirectory(Path.GetDirectoryName(fixture.DataDestination)!);
        await File.WriteAllTextAsync(fixture.DataDestination, "user-owned-data");
        fixture.WriteBuild("2.0.0", "module-v2", "managed-data");
        var update = await ObsDeploymentService.CreateInstallPlanAsync(
            Fixture.Manifest("2.0.0"), fixture.ProjectRoot, fixture.InstallationRoot);
        Assert.True((await ObsDeploymentService.ApplyAsync(update, update.Fingerprint)).IsSuccess);
        Assert.Equal("managed-data", await File.ReadAllTextAsync(fixture.DataDestination));

        var uninstall = await ObsDeploymentService.CreateUninstallPlanAsync(
            Fixture.Manifest("2.0.0"), fixture.InstallationRoot);
        var result = await ObsDeploymentService.ApplyAsync(uninstall, uninstall.Fingerprint);

        Assert.True(result.IsSuccess, Format(result.Diagnostics));
        Assert.Equal("user-owned-data", await File.ReadAllTextAsync(fixture.DataDestination));
    }

    [Fact]
    public void ReceiptSchemaIsPublishedJsonSchema()
    {
        var schema = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Schemas",
            "obs-deployment-receipt-v1.schema.json");
        using var document = JsonDocument.Parse(File.ReadAllText(schema));
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
            Root = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.ObsDeploymentTests",
                Guid.NewGuid().ToString("N"));
            ProjectRoot = Path.Combine(Root, "project");
            InstallationRoot = Path.Combine(Root, "obs");
            LogDirectory = Path.Combine(InstallationRoot, "config", "obs-studio", "logs");
            Directory.CreateDirectory(Path.Combine(ProjectRoot, "src"));
            Directory.CreateDirectory(Path.Combine(InstallationRoot, "bin", "64bit"));
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(Path.Combine(ProjectRoot, "src", "plugin.c"), "#include <stdbool.h>\n");
            File.WriteAllBytes(
                Path.Combine(InstallationRoot, "bin", "64bit", "obs64.exe"),
                "test executable"u8.ToArray());
        }

        public string Root { get; }
        public string ProjectRoot { get; }
        public string InstallationRoot { get; }
        public string LogDirectory { get; }
        public string ObsExecutable => Path.Combine(InstallationRoot, "bin", "64bit", "obs64.exe");
        public string ModuleDestination => Path.Combine(
            InstallationRoot,
            "obs-plugins",
            "64bit",
            "obs-deployment-test.dll");
        public string DataDestination => Path.Combine(
            InstallationRoot,
            "data",
            "obs-plugins",
            "obs-deployment-test",
            "plugin-config.json");
        public string ReceiptPath => Path.Combine(
            InstallationRoot,
            ".foundry",
            "obs",
            "receipts",
            "com.creatorsforge.obs-deployment-test.json");

        public static FoundryProjectManifest Manifest(string version) => new()
        {
            Name = "OBS Deployment Test",
            Id = "com.creatorsforge.obs-deployment-test",
            Version = version,
            Target = new FoundryTarget
            {
                Provider = "obsstudio",
                Profile = "32.x-windows-x64",
            },
            NativeBuild = new FoundryNativeBuild { Sources = ["src/plugin.c"] },
            ObsPlugin = new FoundryObsPlugin
            {
                Contract = FoundryObsPlugin.SdkContract,
                ModuleName = "obs-deployment-test",
                DisplayName = "OBS Deployment Test",
                Author = "Creators Forge",
                Description = "Deployment fixture",
                ApiVersion = FoundryObsPlugin.SupportedSdkVersion,
                SdkVersion = FoundryObsPlugin.SupportedSdkVersion,
            },
            Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
        };

        public void WriteBuild(
            string version,
            string moduleContent,
            string dataContent,
            params (string Path, string Content)[] extraEntries)
        {
            WriteBuildCore(version, moduleContent, dataContent, extraEntries);
        }

        public void WriteBuildWithoutData(string version, string moduleContent)
        {
            WriteBuildCore(version, moduleContent, null, []);
        }

        public void ReplacePackageMetadata(string projectId, string version, string moduleName)
        {
            var packagePath = Path.Combine(
                ProjectRoot, "build", "obs", "package", "obs-deployment-test.zip");
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.GetEntry("foundry-package.json")!.Delete();
                WriteEntry(archive, "foundry-package.json", JsonSerializer.Serialize(new
                {
                    projectId,
                    projectVersion = version,
                    moduleName,
                }));
            }

            WritePackageIr(version, packagePath);
        }

        private void WriteBuildCore(
            string version,
            string moduleContent,
            string? dataContent,
            (string Path, string Content)[] extraEntries)
        {
            var build = Path.Combine(ProjectRoot, "build");
            var packagePath = Path.Combine(build, "obs", "package", "obs-deployment-test.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "obs-plugins/64bit/obs-deployment-test.dll", moduleContent);
                if (dataContent is not null)
                {
                    WriteEntry(archive, "data/obs-plugins/obs-deployment-test/plugin-config.json", dataContent);
                }
                WriteEntry(archive, "foundry-package.json", JsonSerializer.Serialize(new
                {
                    projectId = "com.creatorsforge.obs-deployment-test",
                    projectVersion = version,
                    moduleName = "obs-deployment-test",
                }));
                foreach (var entry in extraEntries)
                {
                    WriteEntry(archive, entry.Path, entry.Content);
                }
            }

            WritePackageIr(version, packagePath);
        }

        private void WritePackageIr(string version, string packagePath)
        {
            var build = Path.Combine(ProjectRoot, "build");
            var bytes = File.ReadAllBytes(packagePath);
            var artifact = new FoundryPackageArtifact(
                FoundryPackageArtifactKinds.ObsPluginPackage,
                "obs/package/obs-deployment-test.zip",
                bytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
            var package = new FoundryPackageIntermediate
            {
                Project = new("com.creatorsforge.obs-deployment-test", "OBS Deployment Test", version),
                Target = new(
                    "obsstudio",
                    "32.x-windows-x64",
                    "native-c17-windows-x64",
                    ObsApiVersion: "32.1.2",
                    ObsSdkVersion: "32.1.2"),
                Artifacts = [artifact],
            };
            Directory.CreateDirectory(build);
            File.WriteAllText(
                Path.Combine(build, "package-ir.json"),
                JsonSerializer.Serialize(package, JsonOptions));
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
