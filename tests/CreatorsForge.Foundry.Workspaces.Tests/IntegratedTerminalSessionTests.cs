using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class IntegratedTerminalSessionTests
{
    [Fact]
    public async Task ExecutesPowerShellCommandInTheSelectedWorkingDirectory()
    {
        using var fixture = new TerminalFixture();
        await using var session = new IntegratedTerminalSession();
        var output = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, args) =>
        {
            if (args.Text.StartsWith("foundry-terminal:", StringComparison.Ordinal))
            {
                output.TrySetResult(args.Text);
            }
        };

        await session.StartAsync(fixture.Root);
        await session.SendCommandAsync(
            "Write-Output ('foundry-terminal:' + (Get-Location).Path)");

        var line = await output.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(session.IsRunning);
        Assert.Equal(Path.GetFullPath(fixture.Root), session.WorkingDirectory);
        Assert.Contains(Path.GetFullPath(fixture.Root), line, StringComparison.OrdinalIgnoreCase);

        await session.StopAsync();
        Assert.False(session.IsRunning);
        Assert.Null(session.WorkingDirectory);
    }

    [Fact]
    public async Task StartingForAnotherProjectReplacesTheExistingProcess()
    {
        using var first = new TerminalFixture();
        using var second = new TerminalFixture();
        await using var session = new IntegratedTerminalSession();

        await session.StartAsync(first.Root);
        await session.StartAsync(second.Root);

        Assert.True(session.IsRunning);
        Assert.Equal(Path.GetFullPath(second.Root), session.WorkingDirectory);
    }

    [Fact]
    public async Task RejectsAMissingWorkingDirectory()
    {
        using var fixture = new TerminalFixture();
        await using var session = new IntegratedTerminalSession();
        var missing = Path.Combine(fixture.Root, "missing");

        var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => session.StartAsync(missing));

        Assert.Contains(missing, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(session.IsRunning);
    }

    [Fact]
    public async Task StopTerminatesACommandAndItsChildProcessTree()
    {
        using var fixture = new TerminalFixture();
        await using var session = new IntegratedTerminalSession();
        var childId = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, args) =>
        {
            const string prefix = "foundry-child:";
            if (args.Text.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(args.Text[prefix.Length..], out var parsed))
            {
                childId.TrySetResult(parsed);
            }
        };

        await session.StartAsync(fixture.Root);
        await session.SendCommandAsync(
            "$child = Start-Process -FilePath $env:ComSpec " +
            "-ArgumentList '/c','ping -n 30 127.0.0.1 > nul' -PassThru; " +
            "Write-Output ('foundry-child:' + $child.Id); Wait-Process $child.Id");
        var processId = await childId.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var child = System.Diagnostics.Process.GetProcessById(processId);

        await session.StopAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(child.HasExited);
        Assert.False(session.IsRunning);
    }

    private sealed class TerminalFixture : IDisposable
    {
        public TerminalFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CreatorsForge.Foundry.Tests",
                "terminal",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
