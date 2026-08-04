using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CreatorsForge.Foundry.Testing;

public static class ObsNativeProcessRunner
{
    private const int MaximumOutputCharacters = 32 * 1024;
    private const int MaximumResultBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<ObsNativeProcessResult> RunAsync(
        ObsNativeHostRequest request,
        string nativeHostAssembly,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeHostAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        Directory.CreateDirectory(workingDirectory);
        var requestPath = Path.Combine(workingDirectory, "request.json");
        var resultPath = Path.Combine(workingDirectory, "result.json");
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(request, JsonOptions) + "\n",
            new UTF8Encoding(false),
            cancellationToken).ConfigureAwait(false);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add(Path.GetFullPath(nativeHostAssembly));
        startInfo.ArgumentList.Add(requestPath);
        startInfo.ArgumentList.Add(resultPath);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The native test host process could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var output = Limit(await outputTask.ConfigureAwait(false));
        var error = Limit(await errorTask.ConfigureAwait(false));
        if (timedOut)
        {
            return new(false, true, process.ExitCode, null, output, error, $"Native test host exceeded the {timeout.TotalSeconds:0.#}-second timeout.");
        }

        ObsNativeHostResult? hostResult = null;
        if (File.Exists(resultPath) && new FileInfo(resultPath).Length <= MaximumResultBytes)
        {
            try
            {
                hostResult = JsonSerializer.Deserialize<ObsNativeHostResult>(
                    await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false),
                    ReadOptions);
            }
            catch (JsonException)
            {
            }
        }

        var completed = process.ExitCode == 0 && hostResult is not null;
        var exit = unchecked((uint)process.ExitCode);
        return new(
            completed,
            false,
            process.ExitCode,
            hostResult,
            output,
            error,
            completed
                ? null
                : $"Native test host exited with 0x{exit:X8}" +
                  (hostResult?.Error is null ? "." : $": {hostResult.Error}"));
    }

    private static string Limit(string value) =>
        value.Length <= MaximumOutputCharacters
            ? value
            : value[..MaximumOutputCharacters] + "\n[output truncated]";
}
