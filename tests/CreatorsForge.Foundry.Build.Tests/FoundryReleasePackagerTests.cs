using System.IO.Compression;
using System.Security.Cryptography;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class FoundryReleasePackagerTests
{
    [Fact]
    public async Task StreamerBotReleaseContainsVerifiedPayloadManifestAndInstructions()
    {
        using var project = TemporaryReleaseProject.CreateStreamerBot();
        var result = await new FoundryReleasePackager(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 26, 15, 0, 0, TimeSpan.Zero)))
            .CreateAsync(project.Manifest, project.ManifestPath, project.Build);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.ArchivePath));
        Assert.All(result.Manifest!.Validation.GetType().GetProperties(), property =>
            Assert.True((bool)property.GetValue(result.Manifest.Validation)!));
        Assert.Contains(result.Manifest.Files, item => item.Kind == "readme");
        Assert.Contains(result.Manifest.Files, item => item.Kind == "packageIr");
        using var archive = ZipFile.OpenRead(result.ArchivePath!);
        Assert.Equal(
            [
                "README.md",
                "bridge/CPHInline.cs",
                "foundry-build.json",
                "managed/Test.Extension.dll",
                "package-ir.json",
                "streamerbot/test.streamerbot",
            ],
            archive.Entries.Select(item => item.FullName).Order(StringComparer.Ordinal));
        Assert.All(archive.Entries, item => Assert.Equal(1980, item.LastWriteTime.Year));
        var readme = await File.ReadAllTextAsync(Path.Combine(result.ReleaseDirectory!, "README.md"));
        Assert.Contains("Install in Streamer.bot", readme, StringComparison.Ordinal);
        Assert.Contains("compiler reference", readme, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModifiedBuildArtifactIsRejectedBeforeReleaseAssembly()
    {
        using var project = TemporaryReleaseProject.CreateStreamerBot();
        await File.AppendAllTextAsync(
            Path.Combine(project.Root, "build", "managed", "Test.Extension.dll"),
            "tampered");

        var result = await new FoundryReleasePackager().CreateAsync(
            project.Manifest,
            project.ManifestPath,
            project.Build);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFR1004");
        Assert.False(Directory.Exists(Path.Combine(project.Root, "build", "release")));
    }

    [Fact]
    public async Task ObsReleaseGeneratesDisposableHostInstructions()
    {
        using var project = TemporaryReleaseProject.CreateObs();
        var result = await new FoundryReleasePackager().CreateAsync(
            project.Manifest,
            project.ManifestPath,
            project.Build);

        Assert.True(result.IsSuccess);
        var readme = await File.ReadAllTextAsync(Path.Combine(result.ReleaseDirectory!, "README.md"));
        Assert.Contains("Install in OBS Studio", readme, StringComparison.Ordinal);
        Assert.Contains("close OBS cleanly", readme, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("streamerbot")]
    [InlineData("obsstudio")]
    public async Task FixedTimeReleaseIsByteIdenticalAcrossRuns(string provider)
    {
        using var project = string.Equals(provider, "streamerbot", StringComparison.Ordinal)
            ? TemporaryReleaseProject.CreateStreamerBot()
            : TemporaryReleaseProject.CreateObs();
        var packager = new FoundryReleasePackager(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 9, 30, 0, TimeSpan.Zero)));

        var first = await packager.CreateAsync(project.Manifest, project.ManifestPath, project.Build);
        Assert.True(first.IsSuccess);
        var firstArchive = await File.ReadAllBytesAsync(first.ArchivePath!);
        var firstManifest = await File.ReadAllBytesAsync(first.ManifestPath!);

        var second = await packager.CreateAsync(project.Manifest, project.ManifestPath, project.Build);
        Assert.True(second.IsSuccess);
        var secondArchive = await File.ReadAllBytesAsync(second.ArchivePath!);
        var secondManifest = await File.ReadAllBytesAsync(second.ManifestPath!);

        Assert.Equal(firstArchive, secondArchive);
        Assert.Equal(firstManifest, secondManifest);
    }

    [Fact]
    public async Task PublishingReleaseIncludesLegalFilesChecklistInventoryAndReproducibilityReport()
    {
        using var project = TemporaryReleaseProject.CreateStreamerBot();
        await File.WriteAllTextAsync(Path.Combine(project.Root, "LICENSE.txt"), "MIT\n");
        await File.WriteAllTextAsync(Path.Combine(project.Root, "CHANGELOG.md"), "# 1.2.3\n\nInitial release.\n");
        var manifest = project.Manifest with { Publishing = CreatePublishing(project.Manifest.Id) };
        var packager = new FoundryReleasePackager(
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero)));

        var first = await packager.CreatePublishingAsync(manifest, project.ManifestPath, project.Build);
        Assert.True(first.IsSuccess);
        Assert.True(File.Exists(first.ReproducibilityReportPath));
        Assert.Contains(first.Manifest!.Dependencies, item => item.Name == "Streamer.bot");
        Assert.False(first.Manifest.Signing.Requested);
        using var archive = ZipFile.OpenRead(first.ArchivePath!);
        Assert.Contains(archive.Entries, item => item.FullName == "LICENSE.txt");
        Assert.Contains(archive.Entries, item => item.FullName == "CHANGELOG.md");
        Assert.Contains(archive.Entries, item => item.FullName == "publishing-checklist.json");

        var bytes = await File.ReadAllBytesAsync(first.ArchivePath!);
        archive.Dispose();
        var second = await packager.CreatePublishingAsync(manifest, project.ManifestPath, project.Build);
        Assert.True(second.IsSuccess);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(second.ArchivePath!));
    }

    [Fact]
    public async Task PublishingReleaseRejectsMissingLegalFiles()
    {
        using var project = TemporaryReleaseProject.CreateStreamerBot();
        var manifest = project.Manifest with { Publishing = CreatePublishing(project.Manifest.Id) };

        var result = await new FoundryReleasePackager().CreatePublishingAsync(
            manifest, project.ManifestPath, project.Build);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFR2001");
    }

    [Fact]
    public async Task CodeSigningRefusesMissingConfiguredTool()
    {
        using var project = TemporaryReleaseProject.CreateStreamerBot();
        var result = await FoundryCodeSigningService.SignReleasePayloadsAsync(
            project.Root,
            new FoundrySigningConfiguration
            {
                Enabled = true,
                ToolPath = Path.Combine(project.Root, "missing-signtool.exe"),
                CertificateThumbprint = "00112233445566778899AABBCCDDEEFF00112233",
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("CFR2101", Assert.Single(result.Diagnostics).Code);
    }

    private static FoundryPublishing CreatePublishing(string packageName) => new()
    {
        PackageName = packageName,
        Summary = "A verified release fixture.",
        Authors = ["Creators Forge"],
        Dependencies = [new() { Name = "Example Library", Version = "1.0.0", Kind = "library", License = "MIT" }],
    };

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TemporaryReleaseProject : IDisposable
    {
        private TemporaryReleaseProject(
            string root,
            string manifestPath,
            FoundryProjectManifest manifest,
            FoundryBuildResult build)
        {
            Root = root;
            ManifestPath = manifestPath;
            Manifest = manifest;
            Build = build;
        }

        public string Root { get; }
        public string ManifestPath { get; }
        public FoundryProjectManifest Manifest { get; }
        public FoundryBuildResult Build { get; }

        public static TemporaryReleaseProject CreateStreamerBot()
        {
            var root = CreateRoot();
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "streamerbot"));
            File.WriteAllText(Path.Combine(root, "src", "Extension.cs"), "public sealed class Extension {}\n");
            File.WriteAllText(Path.Combine(root, "streamerbot", "definition.json"), "{}\n");
            var manifest = new FoundryProjectManifest
            {
                Name = "Release Test",
                Id = "com.creatorsforge.release-test",
                Version = "1.2.3",
                Target = new FoundryTarget
                {
                    Provider = "streamerbot",
                    Profile = "1.0.4-stable",
                },
                ManagedBuild = new FoundryManagedBuild
                {
                    AssemblyName = "Test.Extension",
                    Sources = ["src/Extension.cs"],
                },
                CphInlineBridge = new FoundryCphInlineBridge
                {
                    Contract = FoundryCphInlineBridge.SupportedContract,
                    EntryType = "ReleaseTest.Extension",
                    EntryMethod = "Execute",
                },
                TargetDefinition = "streamerbot/definition.json",
                Outputs =
                [
                    FoundryOutputKinds.ManagedLibrary,
                    FoundryOutputKinds.CphInlineBridge,
                    FoundryOutputKinds.StreamerBotPackage,
                ],
            };
            return Create(
                root,
                manifest,
                new FoundryPackageTarget(
                    "streamerbot",
                    "1.0.4-stable",
                    "net481",
                    "1.0.0+123456789abc"),
                [
                    (FoundryPackageArtifactKinds.ManagedAssembly, "managed/Test.Extension.dll", "assembly"),
                    (FoundryPackageArtifactKinds.CphInlineBridge, "bridge/CPHInline.cs", "bridge"),
                    (FoundryPackageArtifactKinds.StreamerBotPackage, "streamerbot/test.streamerbot", "package"),
                ]);
        }

        public static TemporaryReleaseProject CreateObs()
        {
            var root = CreateRoot();
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "plugin.c"), "#include <stdbool.h>\n");
            var manifest = new FoundryProjectManifest
            {
                Name = "OBS Release Test",
                Id = "com.creatorsforge.obs-release-test",
                Version = "2.0.0",
                Target = new FoundryTarget { Provider = "obsstudio", Profile = "32.x-windows-x64" },
                NativeBuild = new FoundryNativeBuild { Sources = ["src/plugin.c"] },
                ObsPlugin = new FoundryObsPlugin
                {
                    Contract = FoundryObsPlugin.SdkContract,
                    ModuleName = "obs-release-test",
                    DisplayName = "OBS Release Test",
                    Author = "Creators Forge",
                    Description = "Release fixture",
                    ApiVersion = FoundryObsPlugin.SupportedSdkVersion,
                    SdkVersion = FoundryObsPlugin.SupportedSdkVersion,
                },
                Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
            };
            return Create(
                root,
                manifest,
                new FoundryPackageTarget(
                    "obsstudio",
                    "32.x-windows-x64",
                    "native-c17-windows-x64",
                    ObsApiVersion: "32.1.2",
                    ObsSdkVersion: "32.1.2"),
                [
                    (FoundryPackageArtifactKinds.NativeObsPlugin, "obs/bin/obs-release-test.dll", "native"),
                    (FoundryPackageArtifactKinds.ObsPluginPackage, "obs/package/obs-release-test.zip", "zip"),
                ]);
        }

        private static TemporaryReleaseProject Create(
            string root,
            FoundryProjectManifest manifest,
            FoundryPackageTarget target,
            IReadOnlyList<(string Kind, string Path, string Content)> contents)
        {
            var artifacts = new List<FoundryPackageArtifact>();
            foreach (var item in contents)
            {
                var path = Path.Combine(root, "build", item.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var bytes = System.Text.Encoding.UTF8.GetBytes(item.Content);
                File.WriteAllBytes(path, bytes);
                artifacts.Add(new(
                    item.Kind,
                    item.Path,
                    bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes))));
            }

            var package = new FoundryPackageIntermediate
            {
                Project = new(manifest.Id, manifest.Name, manifest.Version),
                Target = target,
                Artifacts = artifacts,
            };
            var manifestPath = Path.Combine(root, "Test.foundryproj");
            File.WriteAllText(manifestPath, "{}\n");
            return new(
                root,
                manifestPath,
                manifest,
                new FoundryBuildResult(
                    Path.Combine(root, "build"),
                    Path.Combine(root, "build", "package-ir.json"),
                    package,
                    []));
        }

        private static string CreateRoot()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.ReleaseTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
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
