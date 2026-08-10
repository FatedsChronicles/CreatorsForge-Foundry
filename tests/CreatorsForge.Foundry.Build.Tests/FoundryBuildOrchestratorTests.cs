using System.Security.Cryptography;
using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class FoundryBuildOrchestratorTests
{
    private static readonly byte[] AssemblyBytes = "deterministic-test-assembly"u8.ToArray();

    [Fact]
    public void GenerateBridgeMatchesReviewedGoldenFile()
    {
        var bridge = new FoundryCphInlineBridge
        {
            Contract = FoundryCphInlineBridge.SupportedContract,
            EntryType = "CreatorsForge.Tests.Extension.EntryPoint",
            EntryMethod = "Execute",
        };
        var expected = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Golden",
                "CPHInline.args-log-v1.cs"));

        var actual = CphInlineBridgeGenerator.Generate(bridge);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GenerateBridgeRejectsUnvalidatedEntryPoint()
    {
        var bridge = new FoundryCphInlineBridge
        {
            Contract = FoundryCphInlineBridge.SupportedContract,
            EntryType = "Injected.Type\npublic class Other",
            EntryMethod = "Execute",
        };

        Assert.Throws<ArgumentException>(() => CphInlineBridgeGenerator.Generate(bridge));
    }

    [Fact]
    public async Task BuildAsyncProducesHashedPackageIntermediate()
    {
        using var project = TemporaryProject.Create();
        var runner = new SuccessfulBuildRunner(AssemblyBytes);
        var orchestrator = new FoundryBuildOrchestrator(runner);

        var result = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, runner.InvocationCount);
        Assert.Equal(2, result.PackageIntermediate?.Artifacts.Count);
        Assert.StartsWith(
            "1.0.0+",
            result.PackageIntermediate?.Target.CphCatalogueRevision);
        var artifact = result.PackageIntermediate!.Artifacts.Single(
            item => item.Kind == "managedAssembly");
        Assert.Equal("managed/CreatorsForge.Tests.Extension.dll", artifact.Path);
        Assert.Equal(AssemblyBytes.Length, artifact.Size);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(AssemblyBytes)),
            artifact.Sha256);
        var bridge = result.PackageIntermediate.Artifacts.Single(
            item => item.Kind == "cphInlineBridge");
        Assert.Equal("bridge/CPHInline.cs", bridge.Path);
        Assert.True(File.Exists(Path.Combine(project.Root, "build", bridge.Path)));
        Assert.True(File.Exists(result.PackageIntermediatePath));
    }

    [Fact]
    public async Task BuildAsyncProducesIdenticalProjectAndPackageIrAcrossRuns()
    {
        using var project = TemporaryProject.Create();
        var orchestrator = new FoundryBuildOrchestrator(
            new SuccessfulBuildRunner(AssemblyBytes));

        var first = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);
        var firstProject = await File.ReadAllBytesAsync(
            Path.Combine(
                project.Root,
                "build",
                "obj",
                "managed",
                "Foundry.Managed.csproj"));
        var firstPackage = await File.ReadAllBytesAsync(first.PackageIntermediatePath!);
        var firstBridge = await File.ReadAllBytesAsync(
            Path.Combine(project.Root, "build", "bridge", "CPHInline.cs"));

        var second = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);
        var secondProject = await File.ReadAllBytesAsync(
            Path.Combine(
                project.Root,
                "build",
                "obj",
                "managed",
                "Foundry.Managed.csproj"));
        var secondPackage = await File.ReadAllBytesAsync(second.PackageIntermediatePath!);
        var secondBridge = await File.ReadAllBytesAsync(
            Path.Combine(project.Root, "build", "bridge", "CPHInline.cs"));

        Assert.Equal(firstProject, secondProject);
        Assert.Equal(firstPackage, secondPackage);
        Assert.Equal(firstBridge, secondBridge);
        Assert.DoesNotContain(
            project.Root,
            System.Text.Encoding.UTF8.GetString(secondProject),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Entry%24Point.cs",
            System.Text.Encoding.UTF8.GetString(secondProject),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncReportsMissingSourcesWithoutStartingCompiler()
    {
        using var project = TemporaryProject.Create();
        File.Delete(project.SourcePath);
        var runner = new SuccessfulBuildRunner(AssemblyBytes);
        var orchestrator = new FoundryBuildOrchestrator(runner);

        var result = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsSuccess);
        Assert.Equal("CFB0002", diagnostic.Code);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Fact]
    public async Task BuildAsyncRejectsPackageWithoutDefinition()
    {
        using var project = TemporaryProject.Create();
        var runner = new SuccessfulBuildRunner(AssemblyBytes);
        var orchestrator = new FoundryBuildOrchestrator(runner);
        var manifest = project.Manifest with
        {
            Outputs = [FoundryOutputKinds.StreamerBotPackage],
        };

        var result = await orchestrator.BuildAsync(
            manifest,
            project.ManifestPath,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFP0027");
        Assert.Equal(0, runner.InvocationCount);
    }

    [Fact]
    public async Task BuildAsyncProducesVerifiedStreamerBotPackage()
    {
        using var project = TemporaryProject.Create(includeStreamerBotPackage: true);
        var orchestrator = new FoundryBuildOrchestrator(
            new SuccessfulBuildRunner(AssemblyBytes));

        var result = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.PackageIntermediate!.Artifacts.Count);
        var package = result.PackageIntermediate.Artifacts.Single(
            item => item.Kind == "streamerBotPackage");
        var report = result.PackageIntermediate.Artifacts.Single(
            item => item.Kind == "streamerBotPackageReport");
        var packagePath = Path.Combine(project.Root, "build", package.Path);
        var reportPath = Path.Combine(project.Root, "build", report.Path);
        Assert.True(File.Exists(packagePath));
        Assert.True(File.Exists(reportPath));
        var decoded = StreamerBot.StreamerBotStableV23Adapter.Decode(
            await File.ReadAllTextAsync(packagePath));
        Assert.Equal(23, decoded["version"]!.GetValue<int>());
        Assert.Contains(
            "\"roundTripVerified\": true",
            await File.ReadAllTextAsync(reportPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnchangedStreamerBotPackageBuildProducesIdenticalArtifactSet()
    {
        using var project = TemporaryProject.Create(includeStreamerBotPackage: true);
        var orchestrator = new FoundryBuildOrchestrator(
            new SuccessfulBuildRunner(AssemblyBytes));

        var first = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        var firstArtifacts = await ReadArtifactSetAsync(project.Root, first);
        var firstIr = await File.ReadAllBytesAsync(first.PackageIntermediatePath!);

        var second = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        var secondArtifacts = await ReadArtifactSetAsync(project.Root, second);
        var secondIr = await File.ReadAllBytesAsync(second.PackageIntermediatePath!);

        Assert.Equal(firstArtifacts.Keys, secondArtifacts.Keys);
        foreach (var path in firstArtifacts.Keys)
        {
            Assert.Equal(firstArtifacts[path], secondArtifacts[path]);
        }

        Assert.Equal(firstIr, secondIr);
    }

    [Fact]
    public async Task BuildAsyncConvertsCompilerOutputToStructuredDiagnostics()
    {
        using var project = TemporaryProject.Create();
        var compilerOutput =
            $"{project.SourcePath}(4,17): error CS1002: ; expected [Foundry.Managed.csproj]";
        var runner = new FixedBuildRunner(new(1, compilerOutput, string.Empty));
        var orchestrator = new FoundryBuildOrchestrator(runner);

        var result = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(["CS1002", "CFB0005"], result.Diagnostics.Select(item => item.Code));
        Assert.Equal(4, result.Diagnostics[0].Location?.Line);
        Assert.Equal(17, result.Diagnostics[0].Location?.Column);
        Assert.Contains("CS1002", result.Diagnostics[1].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsyncVerifiesEntryPointAgainstManagedAssembly()
    {
        using var project = TemporaryProject.Create();
        var runner = new BridgeFailureRunner(AssemblyBytes);
        var orchestrator = new FoundryBuildOrchestrator(runner);

        var result = await orchestrator.BuildAsync(
            project.Manifest,
            project.ManifestPath,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(["CS0117", "CFB0009"], result.Diagnostics.Select(item => item.Code));
        Assert.Equal(2, runner.InvocationCount);
        Assert.Null(result.PackageIntermediate);
    }

    private sealed class SuccessfulBuildRunner(byte[] assemblyBytes) : IBuildProcessRunner
    {
        public int InvocationCount { get; private set; }

        public async Task<BuildProcessResult> RunAsync(
            BuildProcessRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            var outputIndex = request.Arguments
                .Select((value, index) => (value, index))
                .Single(item => item.value == "--output")
                .index;
            var outputDirectory = request.Arguments[outputIndex + 1];
            Directory.CreateDirectory(outputDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(outputDirectory, "CreatorsForge.Tests.Extension.dll"),
                assemblyBytes,
                cancellationToken);
            return new(0, "Build succeeded.", string.Empty);
        }
    }

    private static async Task<SortedDictionary<string, byte[]>> ReadArtifactSetAsync(
        string projectRoot,
        FoundryBuildResult result)
    {
        var values = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var artifact in result.PackageIntermediate!.Artifacts)
        {
            values.Add(
                artifact.Path,
                await File.ReadAllBytesAsync(Path.Combine(
                    projectRoot,
                    "build",
                    artifact.Path.Replace('/', Path.DirectorySeparatorChar))));
        }

        return values;
    }

    private sealed class FixedBuildRunner(BuildProcessResult result) : IBuildProcessRunner
    {
        public Task<BuildProcessResult> RunAsync(
            BuildProcessRequest request,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class BridgeFailureRunner(byte[] assemblyBytes) : IBuildProcessRunner
    {
        public int InvocationCount { get; private set; }

        public async Task<BuildProcessResult> RunAsync(
            BuildProcessRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (InvocationCount == 1)
            {
                var outputIndex = request.Arguments
                    .Select((value, index) => (value, index))
                    .Single(item => item.value == "--output")
                    .index;
                var outputDirectory = request.Arguments[outputIndex + 1];
                Directory.CreateDirectory(outputDirectory);
                await File.WriteAllBytesAsync(
                    Path.Combine(outputDirectory, "CreatorsForge.Tests.Extension.dll"),
                    assemblyBytes,
                    cancellationToken);
                return new(0, "Build succeeded.", string.Empty);
            }

            return new(
                1,
                "CPHInline.cs(22,63): error CS0117: 'EntryPoint' does not contain a definition for 'Execute'",
                string.Empty);
        }
    }

    private sealed class TemporaryProject : IDisposable
    {
        private TemporaryProject(
            string root,
            string manifestPath,
            string sourcePath,
            FoundryProjectManifest manifest)
        {
            Root = root;
            ManifestPath = manifestPath;
            SourcePath = sourcePath;
            Manifest = manifest;
        }

        public string Root { get; }

        public string ManifestPath { get; }

        public string SourcePath { get; }

        public FoundryProjectManifest Manifest { get; }

        public static TemporaryProject Create(bool includeStreamerBotPackage = false)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.Tests",
                Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(root, "src", "Entry$Point.cs");
            var manifestPath = Path.Combine(root, "Test.foundryproj");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(
                sourcePath,
                "public static class EntryPoint { public static void Run() { } }");
            File.WriteAllText(manifestPath, "{}");
            var targetDefinition = Path.Combine(root, "streamerbot", "streamerbot.json");
            if (includeStreamerBotPackage)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetDefinition)!);
                File.WriteAllText(
                    targetDefinition,
                    StreamerBot.StreamerBotDefinitionLoader.Serialize(
                        StreamerBotStableV23AdapterTests.CreateDefinition()));
            }

            var manifest = new FoundryProjectManifest
            {
                Name = "Build Test",
                Id = "com.creatorsforge.tests.build",
                Version = "1.0.0",
                Target = new FoundryTarget
                {
                    Provider = "streamerbot",
                    Profile = "1.0.4-stable",
                },
                ManagedBuild = new FoundryManagedBuild
                {
                    AssemblyName = "CreatorsForge.Tests.Extension",
                    Sources = ["src/Entry$Point.cs"],
                },
                CphInlineBridge = new FoundryCphInlineBridge
                {
                    Contract = FoundryCphInlineBridge.SupportedContract,
                    EntryType = "CreatorsForge.Tests.Extension.EntryPoint",
                    EntryMethod = "Execute",
                },
                TargetDefinition = includeStreamerBotPackage
                    ? "streamerbot/streamerbot.json"
                    : null,
                Outputs =
                [
                    FoundryOutputKinds.ManagedLibrary,
                    FoundryOutputKinds.CphInlineBridge,
                    .. includeStreamerBotPackage
                        ? [FoundryOutputKinds.StreamerBotPackage]
                        : Array.Empty<string>(),
                ],
            };

            return new(root, manifestPath, sourcePath, manifest);
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
