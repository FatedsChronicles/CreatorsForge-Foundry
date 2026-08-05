using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Workspaces;

public enum PreviewRuntimeStatus
{
    Stopped,
    Starting,
    Running,
    Completed,
    Failed,
    TimedOut,
}

public sealed record PreviewRuntimeRequest(
    string SessionId,
    int Generation,
    DateTimeOffset RequestedAtUtc,
    PreviewDesignSurface Surface);

public sealed record PreviewRuntimeElement(
    string Kind,
    string Name,
    string Label,
    string VisualRole,
    double X,
    double Y,
    double Width,
    double Height);

public sealed record PreviewRuntimeFrame(
    string SessionId,
    int Generation,
    DateTimeOffset RenderedAtUtc,
    string Kind,
    string Source,
    int ViewportWidth,
    int ViewportHeight,
    string SourceSha256,
    IReadOnlyList<PreviewRuntimeElement> Elements);

public sealed record PreviewRuntimeHostResult(
    bool Succeeded,
    PreviewRuntimeFrame? Frame,
    IReadOnlyList<string> Logs,
    string? Error);

public sealed record PreviewRuntimeResult(
    PreviewRuntimeStatus Status,
    PreviewRuntimeFrame? Frame,
    TimeSpan Duration,
    IReadOnlyList<string> Logs,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess => Status == PreviewRuntimeStatus.Completed && Frame is not null;
}

public sealed record PreviewRuntimeState(
    PreviewRuntimeStatus Status,
    int Generation,
    string Message,
    DateTimeOffset ChangedAtUtc);

public sealed class PreviewRuntimeSession : IAsyncDisposable
{
    private const int MaximumOutputCharacters = 64 * 1024;
    private const int MaximumResultBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly object sync = new();
    private readonly string hostAssembly;
    private readonly string sessionRoot;
    private readonly TimeSpan timeout;
    private CancellationTokenSource? activeCancellation;
    private int generation;

    public PreviewRuntimeSession(
        string hostAssembly,
        string stateRoot,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        this.hostAssembly = Path.GetFullPath(hostAssembly);
        sessionRoot = Path.Combine(Path.GetFullPath(stateRoot), "preview-runtime");
        this.timeout = timeout ?? TimeSpan.FromSeconds(8);
        SessionId = Guid.NewGuid().ToString("N");
        State = new(PreviewRuntimeStatus.Stopped, 0, "Runtime preview is stopped.", DateTimeOffset.UtcNow);
    }

    public string SessionId { get; }

    public PreviewRuntimeState State { get; private set; }

    public event EventHandler<PreviewRuntimeState>? StateChanged;

    public async Task<PreviewRuntimeResult> RefreshAsync(
        PreviewDesignSurface surface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        CancellationTokenSource runCancellation;
        int currentGeneration;
        lock (sync)
        {
            activeCancellation?.Cancel();
            activeCancellation?.Dispose();
            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            runCancellation = activeCancellation;
            currentGeneration = ++generation;
        }

        SetState(PreviewRuntimeStatus.Starting, currentGeneration, "Starting isolated preview host...");
        var started = Stopwatch.StartNew();
        string? runDirectory = null;
        try
        {
            if (!File.Exists(hostAssembly))
            {
                return Failure(
                    PreviewRuntimeStatus.Failed,
                    currentGeneration,
                    started.Elapsed,
                    "CFW2310",
                    "The isolated preview host is missing.",
                    "Repair or reinstall Foundry, then retry preview.");
            }

            runDirectory = Path.Combine(sessionRoot, SessionId, currentGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(runDirectory);
            var requestPath = Path.Combine(runDirectory, "request.json");
            var resultPath = Path.Combine(runDirectory, "result.json");
            var request = new PreviewRuntimeRequest(SessionId, currentGeneration, DateTimeOffset.UtcNow, surface);
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(request, WriteOptions) + "\n",
                new UTF8Encoding(false),
                runCancellation.Token).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = runDirectory,
            };
            startInfo.ArgumentList.Add(hostAssembly);
            startInfo.ArgumentList.Add(requestPath);
            startInfo.ArgumentList.Add(resultPath);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Failure(
                    PreviewRuntimeStatus.Failed,
                    currentGeneration,
                    started.Elapsed,
                    "CFW2311",
                    "The isolated preview host could not be started.",
                    "Confirm the .NET desktop runtime is installed, then retry preview.");
            }

            SetState(PreviewRuntimeStatus.Running, currentGeneration, "Rendering in isolated preview host...");
            var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(runCancellation.Token);
            timeoutCancellation.CancelAfter(timeout);
            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !runCancellation.IsCancellationRequested;
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            if (runCancellation.IsCancellationRequested)
            {
                var stoppedLogs = BuildLogs(
                    await outputTask.ConfigureAwait(false),
                    await errorTask.ConfigureAwait(false));
                SetState(PreviewRuntimeStatus.Stopped, currentGeneration, "Runtime preview stopped.");
                return new(PreviewRuntimeStatus.Stopped, null, started.Elapsed, stoppedLogs, []);
            }

            var logs = BuildLogs(await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
            if (timedOut)
            {
                return Failure(
                    PreviewRuntimeStatus.TimedOut,
                    currentGeneration,
                    started.Elapsed,
                    "CFW2312",
                    $"The isolated preview host exceeded the {timeout.TotalSeconds:0.#}-second timeout.",
                    "Restart preview. If the timeout repeats, inspect the runtime log.",
                    logs);
            }

            PreviewRuntimeHostResult? hostResult = null;
            if (File.Exists(resultPath) && new FileInfo(resultPath).Length <= MaximumResultBytes)
            {
                try
                {
                    hostResult = JsonSerializer.Deserialize<PreviewRuntimeHostResult>(
                        await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false),
                        ReadOptions);
                }
                catch (JsonException)
                {
                }
            }

            if (process.ExitCode != 0 || hostResult is not { Succeeded: true, Frame: not null })
            {
                var exitCode = unchecked((uint)process.ExitCode);
                return Failure(
                    PreviewRuntimeStatus.Failed,
                    currentGeneration,
                    started.Elapsed,
                    "CFW2313",
                    hostResult?.Error ?? $"The isolated preview host exited with 0x{exitCode:X8} without a valid frame.",
                    "Restart preview and inspect the runtime log. Foundry itself remains isolated from the failure.",
                    logs.Concat(hostResult?.Logs ?? []).ToArray());
            }

            var combinedLogs = logs.Concat(hostResult.Logs).TakeLast(200).ToArray();
            SetState(PreviewRuntimeStatus.Completed, currentGeneration, "Runtime frame ready.");
            return new(PreviewRuntimeStatus.Completed, hostResult.Frame, started.Elapsed, combinedLogs, []);
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            SetState(PreviewRuntimeStatus.Stopped, currentGeneration, "Runtime preview stopped.");
            return new(PreviewRuntimeStatus.Stopped, null, started.Elapsed, [], []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            return Failure(
                PreviewRuntimeStatus.Failed,
                currentGeneration,
                started.Elapsed,
                "CFW2311",
                $"The isolated preview host could not run: {exception.Message}",
                "Check local application storage and the .NET runtime, then retry preview.");
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(activeCancellation, runCancellation))
                {
                    activeCancellation = null;
                }
            }
            runCancellation.Dispose();
            DeleteOwnedRunDirectory(runDirectory);
        }
    }

    public Task<PreviewRuntimeResult> RestartAsync(
        PreviewDesignSurface surface,
        CancellationToken cancellationToken = default)
    {
        Stop();
        return RefreshAsync(surface, cancellationToken);
    }

    public void Stop()
    {
        lock (sync)
        {
            activeCancellation?.Cancel();
        }
        SetState(PreviewRuntimeStatus.Stopped, generation, "Runtime preview stopped.");
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        lock (sync)
        {
            activeCancellation?.Dispose();
            activeCancellation = null;
        }
        return ValueTask.CompletedTask;
    }

    private PreviewRuntimeResult Failure(
        PreviewRuntimeStatus status,
        int currentGeneration,
        TimeSpan duration,
        string code,
        string message,
        string fix,
        IReadOnlyList<string>? logs = null)
    {
        SetState(status, currentGeneration, message);
        return new(
            status,
            null,
            duration,
            logs ?? [],
            [new(code, FoundryDiagnosticSeverity.Error, message, null, fix)]);
    }

    private void SetState(PreviewRuntimeStatus status, int currentGeneration, string message)
    {
        var state = new PreviewRuntimeState(status, currentGeneration, message, DateTimeOffset.UtcNow);
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private static string[] BuildLogs(string output, string error)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { Limit(output), Limit(error) }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return combined.Split(
                ["\r\n", "\n"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(200)
            .ToArray();
    }

    private static string Limit(string value) =>
        value.Length <= MaximumOutputCharacters
            ? value
            : value[..MaximumOutputCharacters] + "\n[output truncated]";

    private void DeleteOwnedRunDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionRoot));
        var candidate = Path.GetFullPath(path);
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        try
        {
            Directory.Delete(candidate, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
