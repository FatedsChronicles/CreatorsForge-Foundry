using System.Collections;
using System.Diagnostics;

namespace CreatorsForge.Foundry.Build;

public sealed record BuildProcessRequest(
    string FileName,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

public sealed record BuildProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public interface IBuildProcessRunner
{
    Task<BuildProcessResult> RunAsync(
        BuildProcessRequest request,
        CancellationToken cancellationToken);
}

public sealed class DotNetBuildProcessRunner : IBuildProcessRunner
{
    public async Task<BuildProcessResult> RunAsync(
        BuildProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        NormalizeWindowsPathEnvironment(startInfo);

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The dotnet build process did not start.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    private static void NormalizeWindowsPathEnvironment(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var inherited = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Select(entry => new KeyValuePair<string, string>(
                (string)entry.Key,
                entry.Value?.ToString() ?? string.Empty))
            .ToArray();
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in inherited.Where(entry =>
                     !string.Equals(entry.Key, "PATH", StringComparison.OrdinalIgnoreCase)))
        {
            normalized[entry.Key] = entry.Value;
        }

        normalized["Path"] = string.Join(
            Path.PathSeparator,
            inherited
                .Where(entry => string.Equals(
                    entry.Key,
                    "PATH",
                    StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Value.Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        normalized["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        normalized["DOTNET_NOLOGO"] = "1";

        startInfo.Environment.Clear();
        foreach (var entry in normalized)
        {
            startInfo.Environment[entry.Key] = entry.Value;
        }
    }
}
