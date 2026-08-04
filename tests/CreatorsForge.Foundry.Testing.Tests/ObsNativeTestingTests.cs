using CreatorsForge.Foundry.NativeTestHost;

namespace CreatorsForge.Foundry.Testing.Tests;

public sealed class ObsNativeTestingTests
{
    [Fact]
    public void AbiInspectorReadsWindowsX64ExportsWithoutLoadingTheDll()
    {
        var kernel32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "kernel32.dll");

        var result = ObsAbiInspector.Inspect(kernel32);

        Assert.True(result.IsPortableExecutable);
        Assert.True(result.IsX64);
        Assert.True(result.IsDll);
        Assert.Contains("LoadLibraryW", result.Exports);
        Assert.Contains("obs_module_load", result.MissingRequiredExports);
    }

    [Fact]
    public async Task NativeHostSuccessUsesStructuredChildProcessProtocol()
    {
        using var fixture = new ProcessFixture();

        var result = await ObsNativeProcessRunner.RunAsync(
            fixture.Request("self-success"),
            typeof(NativeTestHostMarker).Assembly.Location,
            fixture.WorkingDirectory,
            TimeSpan.FromSeconds(10));

        Assert.True(result.Completed, result.Failure);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.HostResult!.ModuleLoadSucceeded);
    }

    [Fact]
    public async Task NativeHostCrashIsContainedAndReported()
    {
        using var fixture = new ProcessFixture();

        var result = await ObsNativeProcessRunner.RunAsync(
            fixture.Request("self-crash"),
            typeof(NativeTestHostMarker).Assembly.Location,
            fixture.WorkingDirectory,
            TimeSpan.FromSeconds(10));

        Assert.False(result.Completed);
        Assert.False(result.TimedOut);
        Assert.Null(result.HostResult);
        Assert.Contains("0xC0000005", result.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeHostTimeoutKillsOnlyTheChildProcess()
    {
        using var fixture = new ProcessFixture();

        var result = await ObsNativeProcessRunner.RunAsync(
            fixture.Request("self-hang"),
            typeof(NativeTestHostMarker).Assembly.Location,
            fixture.WorkingDirectory,
            TimeSpan.FromMilliseconds(500));

        Assert.False(result.Completed);
        Assert.True(result.TimedOut);
        Assert.Contains("timeout", result.Failure, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProcessFixture : IDisposable
    {
        public ProcessFixture()
        {
            WorkingDirectory = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.NativeHostTests",
                Guid.NewGuid().ToString("N"));
        }

        public string WorkingDirectory { get; }

        public ObsNativeHostRequest Request(string mode) => new()
        {
            PluginPath = Path.Combine(WorkingDirectory, "plugin.dll"),
            ObsRoot = WorkingDirectory,
            Mode = mode,
        };

        public void Dispose()
        {
            if (Directory.Exists(WorkingDirectory))
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
        }
    }
}
