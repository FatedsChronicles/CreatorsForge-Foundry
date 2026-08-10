using System.Diagnostics;
using System.Text;

namespace CreatorsForge.Foundry.Workspaces;

/// <summary>
/// Owns a non-elevated PowerShell process used by the Foundry desktop terminal.
/// </summary>
public sealed class IntegratedTerminalSession : IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private Process? process;
    private bool disposed;

    public event EventHandler<TerminalOutputEventArgs>? OutputReceived;

    public event EventHandler? StateChanged;

    public bool IsRunning => process is { HasExited: false };

    public string? WorkingDirectory { get; private set; }

    public async Task StartAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        if (!Directory.Exists(fullWorkingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Terminal working directory does not exist: {fullWorkingDirectory}");
        }

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                if (string.Equals(
                    WorkingDirectory,
                    fullWorkingDirectory,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await StopCoreAsync().ConfigureAwait(false);
            }
            else if (process is not null)
            {
                DetachAndDispose(process);
                process = null;
                WorkingDirectory = null;
            }

            var executable = FindWindowsPowerShell();
            var startInfo = new ProcessStartInfo(executable)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = fullWorkingDirectory,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NoExit");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("-");

            var startedProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            startedProcess.OutputDataReceived += Process_OutputDataReceived;
            startedProcess.ErrorDataReceived += Process_ErrorDataReceived;
            startedProcess.Exited += Process_Exited;

            try
            {
                if (!startedProcess.Start())
                {
                    throw new InvalidOperationException("Windows PowerShell did not start.");
                }

                process = startedProcess;
                WorkingDirectory = fullWorkingDirectory;
                startedProcess.BeginOutputReadLine();
                startedProcess.BeginErrorReadLine();
            }
            catch
            {
                DetachAndDispose(startedProcess);
                throw;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendCommandAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var activeProcess = process;
            if (activeProcess is null || activeProcess.HasExited)
            {
                throw new InvalidOperationException("Start the terminal before running a command.");
            }

            var encodedCommand = Convert.ToBase64String(
                Encoding.Unicode.GetBytes(command));
            var invocation =
                "$__foundryCommand=[Text.Encoding]::Unicode.GetString(" +
                $"[Convert]::FromBase64String('{encodedCommand}'));" +
                ". ([ScriptBlock]::Create($__foundryCommand)) *>&1 | " +
                "Out-String -Stream | ForEach-Object { [Console]::Out.WriteLine($_) }";
            await activeProcess.StandardInput.WriteLineAsync(
                    invocation.AsMemory(),
                    cancellationToken)
                .ConfigureAwait(false);
            await activeProcess.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (disposed)
        {
            return;
        }

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            disposed = true;
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
        }
    }

    private async Task StopCoreAsync()
    {
        var activeProcess = process;
        process = null;
        WorkingDirectory = null;
        if (activeProcess is null)
        {
            return;
        }

        try
        {
            if (!activeProcess.HasExited)
            {
                try
                {
                    activeProcess.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the HasExited check and Kill.
                }
                await activeProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            DetachAndDispose(activeProcess);
        }
    }

    private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            OutputReceived?.Invoke(this, new(e.Data, isError: false));
        }
    }

    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            OutputReceived?.Invoke(this, new(e.Data, isError: true));
        }
    }

    private void Process_Exited(object? sender, EventArgs e) =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private void DetachAndDispose(Process target)
    {
        target.OutputDataReceived -= Process_OutputDataReceived;
        target.ErrorDataReceived -= Process_ErrorDataReceived;
        target.Exited -= Process_Exited;
        target.Dispose();
    }

    private static string FindWindowsPowerShell()
    {
        var executable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        return File.Exists(executable)
            ? executable
            : throw new FileNotFoundException(
                "Windows PowerShell could not be located.",
                executable);
    }
}

public sealed class TerminalOutputEventArgs(string text, bool isError) : EventArgs
{
    public string Text { get; } = text;

    public bool IsError { get; } = isError;
}
