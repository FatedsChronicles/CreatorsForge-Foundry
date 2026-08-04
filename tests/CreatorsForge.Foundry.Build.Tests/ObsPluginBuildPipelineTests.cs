using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Build.ObsStudio;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class ObsPluginBuildPipelineTests
{
    private static readonly byte[] PluginBytes = "deterministic-native-obs-plugin"u8.ToArray();

    [Fact]
    public async Task BuildProducesObsBinaryPackageAndPackageIr()
    {
        using var project = TemporaryObsProject.Create();
        var runner = new SuccessfulCMakeRunner(project.Root);
        var result = await new FoundryBuildOrchestrator(runner).BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, runner.InvocationCount);
        Assert.Equal("32.1.1", result.PackageIntermediate!.Target.ObsApiVersion);
        Assert.Null(result.PackageIntermediate.Target.CphCatalogueRevision);
        Assert.Equal(
            ["nativeObsPlugin", "obsPluginPackage"],
            result.PackageIntermediate.Artifacts.Select(item => item.Kind));

        var package = result.PackageIntermediate.Artifacts.Single(item => item.Kind == "obsPluginPackage");
        using var archive = ZipFile.OpenRead(Path.Combine(project.Root, "build", package.Path));
        Assert.Equal(
            ["obs-plugins/64bit/foundry-obs-test.dll", "foundry-package.json"],
            archive.Entries.Select(item => item.FullName));
        Assert.All(archive.Entries, item => Assert.Equal(1980, item.LastWriteTime.Year));
    }

    [Fact]
    public async Task UnchangedObsBuildProducesIdenticalPackageAndIr()
    {
        using var project = TemporaryObsProject.Create();
        var orchestrator = new FoundryBuildOrchestrator(new SuccessfulCMakeRunner(project.Root));

        var first = await orchestrator.BuildAsync(project.Manifest, project.ManifestPath);
        var firstPackage = await File.ReadAllBytesAsync(Path.Combine(
            project.Root,
            "build",
            first.PackageIntermediate!.Artifacts.Single(item => item.Kind == "obsPluginPackage").Path));
        var firstIr = await File.ReadAllBytesAsync(first.PackageIntermediatePath!);

        var second = await orchestrator.BuildAsync(project.Manifest, project.ManifestPath);
        var secondPackage = await File.ReadAllBytesAsync(Path.Combine(
            project.Root,
            "build",
            second.PackageIntermediate!.Artifacts.Single(item => item.Kind == "obsPluginPackage").Path));
        var secondIr = await File.ReadAllBytesAsync(second.PackageIntermediatePath!);

        Assert.Equal(firstPackage, secondPackage);
        Assert.Equal(firstIr, secondIr);
    }

    [Fact]
    public async Task NativeCompilerFailureProducesStructuredDiagnostic()
    {
        using var project = TemporaryObsProject.Create();
        var result = await new FoundryBuildOrchestrator(
            new NativeFailureRunner(project.ManifestPath)).BuildAsync(
                project.Manifest,
                project.ManifestPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(["C2065", "CFB1003"], result.Diagnostics.Select(item => item.Code));
        Assert.Equal(7, result.Diagnostics[0].Location!.Line);
        Assert.Equal(3, result.Diagnostics[0].Location!.Column);
    }

    [Fact]
    public void SdkStatusRejectsIncompleteCache()
    {
        var cache = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var status = ObsSdkManager.Inspect(cache);

        Assert.False(status.IsReady);
        Assert.Equal("32.1.2", status.Version);
        Assert.Contains("sdk-manifest.json", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SdkBackedBuildUsesPinnedCMakePackageAndRecordsSdkVersion()
    {
        using var project = TemporaryObsProject.Create(sdkBacked: true);
        using var cache = TemporarySdkCache.Create();
        var previous = Environment.GetEnvironmentVariable(ObsSdkManager.CacheEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ObsSdkManager.CacheEnvironmentVariable, cache.Root);
            var runner = new SuccessfulCMakeRunner(project.Root);
            var result = await new FoundryBuildOrchestrator(runner).BuildAsync(
                project.Manifest,
                project.ManifestPath);

            Assert.True(result.IsSuccess);
            Assert.Equal("32.1.2", result.PackageIntermediate!.Target.ObsSdkVersion);
            Assert.Equal(
                FoundryObsDesign.PassthroughFilterTemplate,
                result.PackageIntermediate.Target.ObsTemplateRevision);
            Assert.Equal(
                "dev.creatorsforge.obs-build-filter",
                result.PackageIntermediate.Target.ObsComponentId);
            Assert.Contains(
                runner.Requests[0].Arguments,
                argument => argument.StartsWith("-Dlibobs_DIR=", StringComparison.Ordinal));
            var cmake = await File.ReadAllTextAsync(Path.Combine(
                project.Root,
                "build",
                "obj",
                "obs",
                "CMakeLists.txt"));
            Assert.Contains("find_package(libobs 32.1.2 EXACT REQUIRED CONFIG)", cmake, StringComparison.Ordinal);
            Assert.Contains("target_link_libraries(foundry-obs-test PRIVATE OBS::libobs)", cmake, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ObsSdkManager.CacheEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void SdkStatusDetectsModifiedImportLibrary()
    {
        using var cache = TemporarySdkCache.Create();
        Assert.True(ObsSdkManager.Inspect(cache.Root).IsReady);

        File.AppendAllText(
            Path.Combine(ObsSdkManager.GetSdkRoot(cache.Root), "lib", "x64", "obs.lib"),
            "modified");

        var status = ObsSdkManager.Inspect(cache.Root);
        Assert.False(status.IsReady);
        Assert.Contains("hashes are invalid", status.Message, StringComparison.Ordinal);
    }

    private sealed class SuccessfulCMakeRunner(string projectRoot) : IBuildProcessRunner
    {
        public int InvocationCount { get; private set; }
        public List<BuildProcessRequest> Requests { get; } = [];

        public async Task<BuildProcessResult> RunAsync(BuildProcessRequest request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            Requests.Add(request);
            Assert.Equal("cmake", request.FileName);
            if (request.Arguments.Contains("--build", StringComparer.Ordinal))
            {
                var output = Path.Combine(projectRoot, "build", "obs", "bin");
                Directory.CreateDirectory(output);
                await File.WriteAllBytesAsync(
                    Path.Combine(output, "foundry-obs-test.dll"),
                    PluginBytes,
                    cancellationToken);
            }

            return new(0, "CMake succeeded.", string.Empty);
        }
    }

    private sealed class NativeFailureRunner(string sourcePath) : IBuildProcessRunner
    {
        private int invocationCount;

        public Task<BuildProcessResult> RunAsync(
            BuildProcessRequest request,
            CancellationToken cancellationToken)
        {
            invocationCount++;
            return Task.FromResult(invocationCount == 1
                ? new BuildProcessResult(0, "Configured.", string.Empty)
                : new BuildProcessResult(
                    1,
                    $"{sourcePath}(7,3): error C2065: 'missing_symbol': undeclared identifier [plugin.vcxproj]",
                    string.Empty));
        }
    }

    private sealed class TemporaryObsProject : IDisposable
    {
        private TemporaryObsProject(string root, string manifestPath, FoundryProjectManifest manifest)
        {
            Root = root;
            ManifestPath = manifestPath;
            Manifest = manifest;
        }

        public string Root { get; }
        public string ManifestPath { get; }
        public FoundryProjectManifest Manifest { get; }

        public static TemporaryObsProject Create(bool sdkBacked = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "CreatorsForge.Foundry.ObsTests", Guid.NewGuid().ToString("N"));
            var source = Path.Combine(root, "src", "plugin.c");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "#include <stdbool.h>\nbool foundry_obs_plugin_load(void) { return true; }\n");
            var manifestPath = Path.Combine(root, "ObsTest.foundryproj");
            File.WriteAllText(manifestPath, "{}");
            return new(root, manifestPath, new FoundryProjectManifest
            {
                Name = "OBS Build Test",
                Id = "dev.creatorsforge.obs-build-test",
                Version = "1.2.3",
                Target = new FoundryTarget { Provider = "obsstudio", Profile = "32.x-windows-x64" },
                NativeBuild = new FoundryNativeBuild { Sources = ["src/plugin.c"] },
                ObsPlugin = new FoundryObsPlugin
                {
                    Contract = sdkBacked
                        ? FoundryObsPlugin.SdkContract
                        : FoundryObsPlugin.MinimalContract,
                    ModuleName = "foundry-obs-test",
                    DisplayName = "Foundry OBS Test",
                    Author = "Creators Forge",
                    Description = "Test module",
                    ApiVersion = sdkBacked
                        ? FoundryObsPlugin.SupportedSdkVersion
                        : FoundryObsPlugin.MinimalApiVersion,
                    SdkVersion = sdkBacked
                        ? FoundryObsPlugin.SupportedSdkVersion
                        : null,
                    Design = sdkBacked
                        ? new FoundryObsDesign
                        {
                            Template = FoundryObsDesign.PassthroughFilterTemplate,
                            Source = "src/plugin.c",
                            ComponentId = "dev.creatorsforge.obs-build-filter",
                            ComponentName = "OBS Build Filter",
                        }
                        : null,
                },
                Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
            });
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class TemporarySdkCache : IDisposable
    {
        private TemporarySdkCache(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TemporarySdkCache Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "CreatorsForge.Foundry.SdkTests", Guid.NewGuid().ToString("N"));
            var sdk = ObsSdkManager.GetSdkRoot(root);
            var header = Path.Combine(sdk, "sources", "libobs", "obs-module.h");
            var config = Path.Combine(sdk, "sources", "libobs", "obsconfig.h");
            var dll = Path.Combine(sdk, "bin", "x64", "obs.dll");
            var library = Path.Combine(sdk, "lib", "x64", "obs.lib");
            var cmake = Path.Combine(sdk, "cmake", "libobsConfig.cmake");
            var cmakeVersion = Path.Combine(sdk, "cmake", "libobsConfigVersion.cmake");
            foreach (var path in new[] { header, config, dll, library, cmake, cmakeVersion })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Path.GetFileName(path));
            }

            static string Hash(string path) => Convert.ToHexStringLower(
                SHA256.HashData(File.ReadAllBytes(path)));
            File.WriteAllText(
                Path.Combine(sdk, "sdk-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    version = "32.1.2",
                    sourceArchiveUrl = "https://example.invalid/source",
                    sourceArchiveSha256 = new string('0', 64),
                    windowsArchiveUrl = "https://example.invalid/windows",
                    windowsArchiveSha256 = new string('0', 64),
                    obsDllSha256 = Hash(dll),
                    obsImportLibrarySha256 = Hash(library),
                }));
            return new(root);
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
