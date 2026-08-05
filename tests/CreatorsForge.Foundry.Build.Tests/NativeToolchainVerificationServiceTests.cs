using CreatorsForge.Foundry.Build.ObsStudio;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class NativeToolchainVerificationServiceTests
{
    [Fact]
    public async Task SuccessfulVerificationConfiguresBuildsChecksArtifactAndCleansWorkspace()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var runner = new RecordingRunner(createArtifact: true);
            var service = new NativeToolchainVerificationService(
                runner,
                (_, _) => ReadyToolchain(),
                temporaryRoot);

            var result = await service.VerifyAsync("C:\\Visual Studio", "C:\\CMake\\cmake.exe");

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(6, result.Stages.Count);
            Assert.Equal("readiness", result.Stages[0].Id);
            Assert.Equal("prepare", result.Stages[1].Id);
            Assert.Equal("configure", result.Stages[2].Id);
            Assert.Equal("compile", result.Stages[3].Id);
            Assert.Equal("artifact", result.Stages[4].Id);
            Assert.Equal("cleanup", result.Stages[5].Id);
            Assert.Equal(2, runner.Requests.Count);
            Assert.Contains("-A", runner.Requests[0].Arguments);
            Assert.Contains("x64", runner.Requests[0].Arguments);
            Assert.Contains(runner.Requests[0].Arguments,
                item => item == "-DCMAKE_GENERATOR_INSTANCE=C:/Visual Studio");
            Assert.Contains(runner.Requests[0].Arguments,
                item => item.EndsWith("/obs-sdk/cmake", StringComparison.Ordinal));
            Assert.Empty(Directory.EnumerateDirectories(temporaryRoot));
        }
        finally
        {
            DeleteTemporaryRoot(temporaryRoot);
        }
    }

    [Fact]
    public async Task FailedReadinessDoesNotStartExternalProcesses()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var runner = new RecordingRunner(createArtifact: false);
            var service = new NativeToolchainVerificationService(
                runner,
                (_, _) => NotReadyToolchain(),
                temporaryRoot);

            var result = await service.VerifyAsync(null, null);

            Assert.False(result.IsSuccess);
            Assert.Empty(runner.Requests);
            Assert.Contains(result.Diagnostics, item => item.Code == "CFB1101");
        }
        finally
        {
            DeleteTemporaryRoot(temporaryRoot);
        }
    }

    [Fact]
    public async Task ConfigureFailureCapturesOutputAndCleansWorkspace()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var runner = new RecordingRunner(createArtifact: false, configureExitCode: 7);
            var service = new NativeToolchainVerificationService(
                runner,
                (_, _) => ReadyToolchain(),
                temporaryRoot);

            var result = await service.VerifyAsync("C:\\Visual Studio", "C:\\CMake\\cmake.exe");

            Assert.False(result.IsSuccess);
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("CFB1102", diagnostic.Code);
            Assert.Contains("configure output", diagnostic.Details, StringComparison.Ordinal);
            Assert.Single(runner.Requests);
            Assert.Empty(Directory.EnumerateDirectories(temporaryRoot));
        }
        finally
        {
            DeleteTemporaryRoot(temporaryRoot);
        }
    }

    private static NativeToolchainReadiness ReadyToolchain()
    {
        var visualStudio = new VisualStudioToolchain(
            "C:\\Visual Studio", "Visual Studio", "18.0", "14.50",
            "cl.exe", "link.exe", "lib.exe", "dumpbin.exe", "VsDevCmd.bat", []);
        return new(
            true,
            new(true, "C:\\CMake\\cmake.exe", "4.0", "Ready"),
            visualStudio,
            new(true, "C:\\Windows SDK", "10.0", "rc.exe", "mt.exe", "Ready"),
            new(true, "32.1.2", "C:/obs-sdk", null),
            [new("all", "All tools", true, "Ready", "None")]);
    }

    private static NativeToolchainReadiness NotReadyToolchain() =>
        ReadyToolchain() with
        {
            IsReady = false,
            Checks = [new("cmake", "CMake", false, "Missing", "Select CMake")],
        };

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "FoundryNativeVerificationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryRoot(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private sealed class RecordingRunner(bool createArtifact, int configureExitCode = 0) : IBuildProcessRunner
    {
        public List<BuildProcessRequest> Requests { get; } = [];

        public Task<BuildProcessResult> RunAsync(
            BuildProcessRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var isBuild = request.Arguments.Count > 0 && request.Arguments[0] == "--build";
            if (!isBuild && configureExitCode != 0)
            {
                return Task.FromResult(new BuildProcessResult(configureExitCode, "configure output", "configure error"));
            }
            if (isBuild && createArtifact)
            {
                var buildRoot = request.Arguments[1];
                var output = Path.Combine(buildRoot, "verified", "Release", "creators-forge-toolchain-probe.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, "probe");
            }
            return Task.FromResult(new BuildProcessResult(0, "ok", string.Empty));
        }
    }
}
