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
    PreviewDesignSurface Surface,
    PreviewRuntimeExecution? Execution = null);

public static class PreviewRuntimeExecutionKinds
{
    public const string StaticWeb = "static-web-live-v1";
    public const string WinForms = "winforms-live-v1";
    public const string ObsComponent = "obs-component-live-v1";
}

public sealed record PreviewRuntimeInput(
    string Kind,
    string ProjectRoot,
    string Source,
    string? ArtifactPath = null,
    string? ObsRoot = null,
    string? ComponentId = null);

public sealed record PreviewRuntimeExecution(
    string Kind,
    string EntryPath,
    string? ArtifactPath = null,
    string? ObsRoot = null,
    string? ComponentId = null);

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
    IReadOnlyList<PreviewRuntimeElement> Elements,
    string AdapterId = PreviewAdapterIds.Generic,
    string AdapterDisplayName = "Generic isolated renderer",
    string ExecutionMode = "structural",
    string? ImagePngBase64 = null);

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
    private const int MaximumResultBytes = 16 * 1024 * 1024;
    private const int MaximumStagedFiles = 128;
    private const long MaximumStagedFileBytes = 4 * 1024 * 1024;
    private const long MaximumStagedTotalBytes = 24 * 1024 * 1024;
    private const long MaximumStagedBinaryBytes = 64 * 1024 * 1024;
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
        => await RunAsync(surface, null, cancellationToken).ConfigureAwait(false);

    public async Task<PreviewRuntimeResult> RefreshExecutableAsync(
        PreviewDesignSurface surface,
        PreviewRuntimeInput input,
        CancellationToken cancellationToken = default)
        => await RunAsync(surface, input, cancellationToken).ConfigureAwait(false);

    private async Task<PreviewRuntimeResult> RunAsync(
        PreviewDesignSurface surface,
        PreviewRuntimeInput? input,
        CancellationToken cancellationToken)
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
            var execution = input is null
                ? null
                : await StageExecutionAsync(input, runDirectory, runCancellation.Token).ConfigureAwait(false);
            var request = new PreviewRuntimeRequest(SessionId, currentGeneration, DateTimeOffset.UtcNow, surface, execution);
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

    public Task<PreviewRuntimeResult> RestartExecutableAsync(
        PreviewDesignSurface surface,
        PreviewRuntimeInput input,
        CancellationToken cancellationToken = default)
    {
        Stop();
        return RefreshExecutableAsync(surface, input, cancellationToken);
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

    private static async Task<PreviewRuntimeExecution> StageExecutionAsync(
        PreviewRuntimeInput input,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var projectRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input.ProjectRoot));
        if (!Directory.Exists(projectRoot) || IsReparsePoint(projectRoot))
        {
            throw new InvalidOperationException("The executable preview project root is unavailable or linked.");
        }
        var entry = ResolveContainedPath(projectRoot, input.Source);
        if (!File.Exists(entry) || HasReparsePoint(projectRoot, entry))
        {
            throw new InvalidOperationException("The executable preview entry source is unavailable or linked.");
        }

        var executionRoot = Path.Combine(runDirectory, "execution");
        Directory.CreateDirectory(executionRoot);
        if (string.Equals(input.Kind, PreviewRuntimeExecutionKinds.StaticWeb, StringComparison.Ordinal))
        {
            var sourceDirectory = Path.GetDirectoryName(entry)!;
            var contentRoot = Path.Combine(executionRoot, "web");
            await CopyBoundedTreeAsync(sourceDirectory, contentRoot, cancellationToken).ConfigureAwait(false);
            return new(
                input.Kind,
                Path.Combine("execution", "web", Path.GetFileName(entry)).Replace('\\', '/'));
        }

        if (string.IsNullOrWhiteSpace(input.ArtifactPath))
        {
            throw new InvalidOperationException("Executable preview requires a successful build artifact.");
        }
        var artifact = Path.GetFullPath(input.ArtifactPath);
        var buildRoot = Path.Combine(projectRoot, "build");
        if (!artifact.StartsWith(buildRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(artifact) || HasReparsePoint(projectRoot, artifact) ||
            new FileInfo(artifact).Length > MaximumStagedBinaryBytes)
        {
            throw new InvalidOperationException("The executable preview artifact is unavailable, linked, or too large.");
        }
        var binaryRoot = Path.Combine(executionRoot, "bin");
        Directory.CreateDirectory(binaryRoot);
        var stagedArtifact = Path.Combine(binaryRoot, Path.GetFileName(artifact));
        File.Copy(artifact, stagedArtifact, overwrite: true);
        long stagedBinaryBytes = new FileInfo(artifact).Length;
        if (string.Equals(input.Kind, PreviewRuntimeExecutionKinds.WinForms, StringComparison.Ordinal))
        {
            var artifactDirectory = Path.GetDirectoryName(artifact)!;
            foreach (var dependency in Directory.EnumerateFiles(artifactDirectory, "*.dll")
                         .Where(path => !string.Equals(path, artifact, StringComparison.OrdinalIgnoreCase))
                         .Take(32))
            {
                if (!IsReparsePoint(dependency) && new FileInfo(dependency).Length <= MaximumStagedFileBytes)
                {
                    stagedBinaryBytes += new FileInfo(dependency).Length;
                    if (stagedBinaryBytes > MaximumStagedBinaryBytes)
                    {
                        throw new InvalidOperationException("Executable preview dependencies exceed the 64 MiB staging limit.");
                    }
                    File.Copy(dependency, Path.Combine(binaryRoot, Path.GetFileName(dependency)), overwrite: true);
                }
            }
        }
        return new(
            input.Kind,
            input.Source.Replace('\\', '/'),
            Path.Combine("execution", "bin", Path.GetFileName(artifact)).Replace('\\', '/'),
            input.ObsRoot,
            input.ComponentId);
    }

    private static async Task CopyBoundedTreeAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        var allowed = new HashSet<string>(
            [".html", ".htm", ".css", ".js", ".json", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".woff", ".woff2", ".ttf"],
            StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var files = EnumerateFilesWithoutLinks(sourceRoot)
            .Where(path => allowed.Contains(Path.GetExtension(path)))
            .Take(MaximumStagedFiles + 1)
            .ToArray();
        if (files.Length > MaximumStagedFiles)
        {
            throw new InvalidOperationException($"Executable web preview exceeds the {MaximumStagedFiles}-file staging limit.");
        }
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(file))
            {
                throw new InvalidOperationException("Executable web preview does not follow file-system links.");
            }
            var length = new FileInfo(file).Length;
            total += length;
            if (length > MaximumStagedFileBytes || total > MaximumStagedTotalBytes)
            {
                throw new InvalidOperationException("Executable web preview exceeds its bounded staging size.");
            }
            var relative = Path.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var source = File.OpenRead(file);
            await using var target = File.Create(destination);
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();
            if (IsReparsePoint(directory))
            {
                throw new InvalidOperationException("Executable web preview does not follow directory links.");
            }
            foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory)) pending.Enqueue(child);
        }
    }

    private static string ResolveContainedPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("Executable preview paths must be project-relative.");
        }
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Executable preview path escaped the project root.");
        }
        return full;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool HasReparsePoint(string root, string path)
    {
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = new FileInfo(Path.GetFullPath(path)).Directory;
        while (current is not null && current.FullName.Length >= boundary.Length)
        {
            if (IsReparsePoint(current.FullName)) return true;
            if (string.Equals(current.FullName, boundary, StringComparison.OrdinalIgnoreCase)) break;
            current = current.Parent;
        }
        return IsReparsePoint(path);
    }

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
