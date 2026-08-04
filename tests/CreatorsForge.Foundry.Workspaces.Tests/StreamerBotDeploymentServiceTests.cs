using System.Security.Cryptography;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces.Deployment;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class StreamerBotDeploymentServiceTests
{
    private static readonly JsonSerializerOptions ReceiptOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Theory]
    [InlineData("1.0.4", "1.0.4-stable")]
    [InlineData("1.0.4.0", "1.0.4-stable")]
    [InlineData("1.0.5 alpha.34", "1.0.5-alpha.34")]
    [InlineData("1.0.5-alpha.34", "1.0.5-alpha.34")]
    [InlineData("1.0.5 beta.1", "1.0.5-beta.1")]
    [InlineData("1.0.5 beta.6", "1.0.5-beta.6")]
    public void FileVersionsMapToExactCompatibilityProfiles(
        string fileVersion,
        string expectedProfile)
    {
        Assert.Equal(
            expectedProfile,
            StreamerBotInstallationDiscovery.ToCompatibilityProfile(fileVersion));
    }

    [Fact]
    public void DiscoveryReturnsOnlyStreamerBotInstallations()
    {
        using var fixture = DeploymentFixture.Create();
        var invalid = Path.Combine(fixture.Root, "not-streamerbot");
        Directory.CreateDirectory(invalid);

        var discovered = StreamerBotInstallationDiscovery.Discover(
            [invalid, fixture.InstallationRoot]);

        var installation = Assert.Single(discovered);
        Assert.Equal(fixture.InstallationRoot, installation.RootPath);
        Assert.EndsWith(
            "Streamer.bot.exe",
            installation.ExecutablePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallUpdateRollbackAndUninstallAreRecoverable()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "assembly-v1"u8.ToArray());

        var installPlan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True(installPlan.IsReady);
        Assert.Equal(DeploymentOperation.Install, installPlan.Operation);
        Assert.Equal(
            DeploymentFileChange.Create,
            Assert.Single(installPlan.Files).Change);

        var unconfirmed = await StreamerBotDeploymentService.ApplyAsync(
            installPlan,
            "not-the-reviewed-fingerprint");
        Assert.False(unconfirmed.IsSuccess);
        Assert.False(File.Exists(fixture.DeployedAssemblyPath));

        var installed = await StreamerBotDeploymentService.ApplyAsync(
            installPlan,
            installPlan.Fingerprint);
        Assert.True(installed.IsSuccess);
        Assert.Equal(
            "assembly-v1"u8.ToArray(),
            await File.ReadAllBytesAsync(fixture.DeployedAssemblyPath));
        Assert.True(File.Exists(fixture.ReceiptPath));

        fixture.WriteBuild("2.0.0", "assembly-v2"u8.ToArray());
        var updatePlan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("2.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True(updatePlan.IsReady);
        Assert.Equal(DeploymentOperation.Update, updatePlan.Operation);
        Assert.Equal(
            DeploymentFileChange.Replace,
            Assert.Single(updatePlan.Files).Change);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            updatePlan,
            updatePlan.Fingerprint)).IsSuccess);
        Assert.Equal(
            "assembly-v2"u8.ToArray(),
            await File.ReadAllBytesAsync(fixture.DeployedAssemblyPath));

        var rollbackPlan =
            await StreamerBotDeploymentService.CreateRollbackPlanAsync(
                DeploymentFixture.CreateManifest("2.0.0"),
                fixture.InstallationRoot);
        Assert.True(rollbackPlan.IsReady);
        Assert.Equal(
            DeploymentFileChange.Restore,
            Assert.Single(rollbackPlan.Files).Change);
        var rolledBack = await StreamerBotDeploymentService.ApplyAsync(
            rollbackPlan,
            rollbackPlan.Fingerprint);
        Assert.True(rolledBack.IsSuccess);
        Assert.Equal("1.0.0", rolledBack.Receipt?.ProjectVersion);
        Assert.Equal(
            "assembly-v1"u8.ToArray(),
            await File.ReadAllBytesAsync(fixture.DeployedAssemblyPath));

        var uninstallPlan =
            await StreamerBotDeploymentService.CreateUninstallPlanAsync(
                DeploymentFixture.CreateManifest("1.0.0"),
                fixture.InstallationRoot);
        Assert.True(uninstallPlan.IsReady);
        Assert.Equal(
            DeploymentFileChange.Delete,
            Assert.Single(uninstallPlan.Files).Change);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            uninstallPlan,
            uninstallPlan.Fingerprint)).IsSuccess);
        Assert.False(File.Exists(fixture.DeployedAssemblyPath));
        Assert.False(File.Exists(fixture.ReceiptPath));
    }

    [Fact]
    public async Task RollbackRejectsModifiedFileCapturedByRepair()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "assembly-v1"u8.ToArray());
        var manifest = DeploymentFixture.CreateManifest("1.0.0");
        var install = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            install,
            install.Fingerprint)).IsSuccess);

        await File.WriteAllBytesAsync(
            fixture.DeployedAssemblyPath,
            "externally-modified"u8.ToArray());
        var repair = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            repair,
            repair.Fingerprint)).IsSuccess);

        var rollback = await StreamerBotDeploymentService.CreateRollbackPlanAsync(
            manifest,
            fixture.InstallationRoot);

        Assert.False(rollback.IsReady);
        Assert.Contains(rollback.Diagnostics, item => item.Code == "CFD1105");
        Assert.Equal(
            "assembly-v1"u8.ToArray(),
            await File.ReadAllBytesAsync(fixture.DeployedAssemblyPath));
    }

    [Fact]
    public async Task UninstallRestoresPreexistingFile()
    {
        using var fixture = DeploymentFixture.Create();
        await File.WriteAllBytesAsync(
            fixture.DeployedAssemblyPath,
            "preexisting"u8.ToArray());
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());

        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentFileChange.Replace, Assert.Single(plan.Files).Change);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            plan,
            plan.Fingerprint)).IsSuccess);

        var uninstall =
            await StreamerBotDeploymentService.CreateUninstallPlanAsync(
                DeploymentFixture.CreateManifest("1.0.0"),
                fixture.InstallationRoot);
        Assert.Equal(
            DeploymentFileChange.Restore,
            Assert.Single(uninstall.Files).Change);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            uninstall,
            uninstall.Fingerprint)).IsSuccess);
        Assert.Equal(
            "preexisting"u8.ToArray(),
            await File.ReadAllBytesAsync(fixture.DeployedAssemblyPath));
    }

    [Fact]
    public async Task ModifiedInstalledFileBlocksRemoval()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());
        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            plan,
            plan.Fingerprint)).IsSuccess);

        await File.WriteAllBytesAsync(
            fixture.DeployedAssemblyPath,
            "user-modified"u8.ToArray());
        var uninstall =
            await StreamerBotDeploymentService.CreateUninstallPlanAsync(
                DeploymentFixture.CreateManifest("1.0.0"),
                fixture.InstallationRoot);

        Assert.False(uninstall.IsReady);
        Assert.Contains(uninstall.Diagnostics, item => item.Code == "CFD1103");
        Assert.Equal(
            "user-modified"u8.ToArray(),
            await File.ReadAllBytesAsync(fixture.DeployedAssemblyPath));
    }

    [Fact]
    public async Task ArtifactAndDestinationChangesAfterPreviewAreRejected()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());
        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        await File.WriteAllBytesAsync(
            fixture.ManagedArtifactPath,
            "tampered"u8.ToArray());

        var result = await StreamerBotDeploymentService.ApplyAsync(
            plan,
            plan.Fingerprint);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFD2003");
        Assert.False(File.Exists(fixture.DeployedAssemblyPath));
    }

    [Fact]
    public async Task DestinationChangeAfterPreviewIsRejected()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());
        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        await File.WriteAllBytesAsync(
            fixture.DeployedAssemblyPath,
            "appeared-after-preview"u8.ToArray());

        var result = await StreamerBotDeploymentService.ApplyAsync(
            plan,
            plan.Fingerprint);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFD2005");
        Assert.Equal(
            "appeared-after-preview"u8.ToArray(),
            await File.ReadAllBytesAsync(fixture.DeployedAssemblyPath));
    }

    [Fact]
    public void DeploymentReceiptSchemaIsPublishedJsonSchema()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "Schemas",
                    "streamerbot-deployment-receipt-v1.schema.json")));

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(
            1,
            schema.RootElement
                .GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetInt32());
    }

    [Fact]
    public async Task OtherProjectReceiptBlocksDestinationCollision()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());
        var receipts = Path.Combine(
            fixture.InstallationRoot,
            ".foundry",
            "receipts");
        Directory.CreateDirectory(receipts);
        await File.WriteAllTextAsync(
            Path.Combine(receipts, "com.other.project.json"),
            JsonSerializer.Serialize(
                new StreamerBotDeploymentReceipt
                {
                    DeploymentId = Guid.NewGuid().ToString("N"),
                    ProjectId = "com.other.project",
                    ProjectName = "Other",
                    ProjectVersion = "1.0.0",
                    TargetProfile = "1.0.4-stable",
                    InstallationVersion = "1.0.4",
                    InstalledAtUtc = DateTimeOffset.UtcNow,
                    Files =
                    [
                        new(
                            "Extension.dll",
                            new string('0', 64),
                            1,
                            null,
                            null),
                    ],
                },
                ReceiptOptions));

        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);

        Assert.False(plan.IsReady);
        Assert.Contains(plan.Diagnostics, item => item.Code == "CFD1010");
    }

    [Fact]
    public async Task CorruptedCurrentReceiptBlocksUpdate()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.ReceiptPath)!);
        await File.WriteAllTextAsync(fixture.ReceiptPath, "{ invalid");

        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            DeploymentFixture.CreateManifest("1.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);

        Assert.False(plan.IsReady);
        Assert.Contains(plan.Diagnostics, item => item.Code == "CFD1009");
    }

    [Fact]
    public async Task HealthCheckAndCompletionChecklistReachHealthyState()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());
        var manifest = DeploymentFixture.CreateManifest("1.0.0");
        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        var installed = await StreamerBotDeploymentService.ApplyAsync(
            plan,
            plan.Fingerprint);
        Assert.True(installed.IsSuccess);

        var incomplete = await StreamerBotDeploymentService.InspectHealthAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(
            DeploymentHealthState.CompletionRequired,
            incomplete.State);
        Assert.Equal(
            DeploymentFileHealthState.Verified,
            Assert.Single(incomplete.Files).State);
        Assert.True(incomplete.CurrentPackageMatchesReceipt);

        var verification = new StreamerBotDeploymentVerification
        {
            PackageImported = true,
            CompilerReferenceAdded = true,
            CodeCompiled = true,
            RuntimeVerified = true,
        };
        var saved = await StreamerBotDeploymentService.SaveVerificationAsync(
            fixture.InstallationRoot,
            manifest.Id,
            incomplete.DeploymentId!,
            verification);
        Assert.True(saved.IsSuccess);
        Assert.NotNull(saved.Receipt?.Verification.VerifiedAtUtc);

        var healthy = await StreamerBotDeploymentService.InspectHealthAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.Healthy, healthy.State);
        Assert.True(healthy.Verification.IsComplete);
    }

    [Fact]
    public async Task HealthCheckDetectsMissingModifiedAndOutdatedDeployments()
    {
        using var fixture = DeploymentFixture.Create();
        fixture.WriteBuild("1.0.0", "foundry"u8.ToArray());
        var manifest = DeploymentFixture.CreateManifest("1.0.0");
        var plan = await StreamerBotDeploymentService.CreateInstallPlanAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.True((await StreamerBotDeploymentService.ApplyAsync(
            plan,
            plan.Fingerprint)).IsSuccess);

        File.Delete(fixture.DeployedAssemblyPath);
        var missing = await StreamerBotDeploymentService.InspectHealthAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.MissingFiles, missing.State);

        await File.WriteAllBytesAsync(
            fixture.DeployedAssemblyPath,
            "modified"u8.ToArray());
        var modified = await StreamerBotDeploymentService.InspectHealthAsync(
            manifest,
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.ModifiedFiles, modified.State);

        await File.WriteAllBytesAsync(
            fixture.DeployedAssemblyPath,
            "foundry"u8.ToArray());
        var outdated = await StreamerBotDeploymentService.InspectHealthAsync(
            DeploymentFixture.CreateManifest("2.0.0"),
            fixture.ProjectRoot,
            fixture.InstallationRoot);
        Assert.Equal(DeploymentHealthState.UpdateAvailable, outdated.State);
    }

    private sealed class DeploymentFixture : IDisposable
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        private DeploymentFixture(string root)
        {
            Root = root;
            ProjectRoot = Path.Combine(root, "project");
            InstallationRoot = Path.Combine(root, "installation");
            Directory.CreateDirectory(ProjectRoot);
            Directory.CreateDirectory(InstallationRoot);
            File.WriteAllBytes(
                Path.Combine(InstallationRoot, "Streamer.bot.exe"),
                "fixture"u8.ToArray());
        }

        public string Root { get; }
        public string ProjectRoot { get; }
        public string InstallationRoot { get; }
        public string ManagedArtifactPath =>
            Path.Combine(ProjectRoot, "build", "managed", "Extension.dll");
        public string DeployedAssemblyPath =>
            Path.Combine(InstallationRoot, "Extension.dll");
        public string ReceiptPath =>
            Path.Combine(
                InstallationRoot,
                ".foundry",
                "receipts",
                "com.example.extension.json");

        public static DeploymentFixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.Deployment.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new(root);
        }

        public static FoundryProjectManifest CreateManifest(string version) => new()
        {
            Name = "Extension",
            Id = "com.example.extension",
            Version = version,
            Target = new()
            {
                Provider = "streamerbot",
                Profile = "1.0.4-stable",
            },
            Outputs =
            [
                FoundryOutputKinds.ManagedLibrary,
                FoundryOutputKinds.CphInlineBridge,
                FoundryOutputKinds.StreamerBotPackage,
            ],
        };

        public void WriteBuild(string version, byte[] assembly)
        {
            var buildRoot = Path.Combine(ProjectRoot, "build");
            var managedDirectory = Path.Combine(buildRoot, "managed");
            var streamerBotDirectory = Path.Combine(buildRoot, "streamerbot");
            Directory.CreateDirectory(managedDirectory);
            Directory.CreateDirectory(streamerBotDirectory);
            File.WriteAllBytes(ManagedArtifactPath, assembly);
            var importPath = Path.Combine(
                streamerBotDirectory,
                "com.example.extension.streamerbot");
            File.WriteAllText(importPath, "import-code");
            var artifacts = new[]
            {
                Artifact(
                    FoundryPackageArtifactKinds.ManagedAssembly,
                    "managed/Extension.dll",
                    ManagedArtifactPath),
                Artifact(
                    FoundryPackageArtifactKinds.StreamerBotPackage,
                    "streamerbot/com.example.extension.streamerbot",
                    importPath),
            };
            var package = new FoundryPackageIntermediate
            {
                Project = new("com.example.extension", "Extension", version),
                Target = new(
                    "streamerbot",
                    "1.0.4-stable",
                    "net481",
                    "1.0.0+fixture000000"),
                Artifacts = artifacts,
            };
            File.WriteAllText(
                Path.Combine(buildRoot, "package-ir.json"),
                JsonSerializer.Serialize(package, Options));
        }

        private static FoundryPackageArtifact Artifact(
            string kind,
            string path,
            string fullPath)
        {
            var bytes = File.ReadAllBytes(fullPath);
            return new(
                kind,
                path,
                bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
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
