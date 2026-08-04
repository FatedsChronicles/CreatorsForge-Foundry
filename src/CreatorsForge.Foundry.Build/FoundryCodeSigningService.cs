using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build;

public sealed record FoundrySigningResult(
    bool IsSuccess,
    IReadOnlyList<string> SignedFiles,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public static class FoundryCodeSigningService
{
    private static readonly DateTimeOffset ArchiveTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<FoundrySigningResult> SignReleasePayloadsAsync(
        string releaseDirectory,
        FoundrySigningConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseDirectory);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.Enabled) return new(true, [], []);
        if (!File.Exists(configuration.ToolPath))
            return Failure("CFR2101", "The configured Windows signing tool does not exist.", configuration.ToolPath ?? releaseDirectory);

        var signed = new List<string>();
        foreach (var binary in Directory.EnumerateFiles(releaseDirectory, "*.dll", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.signing{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            var result = await SignAndVerifyAsync(binary, configuration, cancellationToken).ConfigureAwait(false);
            if (result is not null) return Failure("CFR2102", result, binary);
            signed.Add(Path.GetRelativePath(releaseDirectory, binary).Replace('\\', '/'));
        }

        foreach (var archivePath in Directory.EnumerateFiles(releaseDirectory, "*.zip", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var result = await SignArchiveDllsAsync(releaseDirectory, archivePath, configuration, signed, cancellationToken).ConfigureAwait(false);
            if (result is not null) return Failure("CFR2103", result, archivePath);
        }
        return new(true, signed, []);
    }

    private static async Task<string?> SignArchiveDllsAsync(
        string releaseDirectory,
        string archivePath,
        FoundrySigningConfiguration configuration,
        List<string> signed,
        CancellationToken cancellationToken)
    {
        var staging = Path.Combine(releaseDirectory, ".signing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(Path.TrimEndingDirectorySeparator(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return $"Archive entry '{entry.FullName}' escapes signing staging.";
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination);
                }
            }
            foreach (var binary in Directory.EnumerateFiles(staging, "*.dll", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var result = await SignAndVerifyAsync(binary, configuration, cancellationToken).ConfigureAwait(false);
                if (result is not null) return result;
                signed.Add(Path.GetRelativePath(staging, binary).Replace('\\', '/') + " (embedded)");
            }
            File.Delete(archivePath);
            await using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var output = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).OrderBy(path => Path.GetRelativePath(staging, path), StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(staging, file).Replace('\\', '/');
                var entry = output.CreateEntry(relative, CompressionLevel.Optimal);
                entry.LastWriteTime = ArchiveTimestamp;
                await using var entryStream = entry.Open();
                await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                await source.CopyToAsync(entryStream, cancellationToken).ConfigureAwait(false);
            }
            return null;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            var parent = Path.GetDirectoryName(staging)!;
            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
        }
    }

    private static async Task<string?> SignAndVerifyAsync(
        string path,
        FoundrySigningConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            var sign = new List<string> { "sign", "/fd", "SHA256", "/sha1", configuration.CertificateThumbprint! };
            if (!string.IsNullOrWhiteSpace(configuration.TimestampUrl))
            {
                sign.AddRange(["/tr", configuration.TimestampUrl, "/td", "SHA256"]);
            }
            sign.Add(path);
            var signResult = await RunAsync(configuration.ToolPath!, sign, cancellationToken).ConfigureAwait(false);
            if (signResult.ExitCode != 0) return $"signtool sign failed: {signResult.Error}";
            var verifyResult = await RunAsync(configuration.ToolPath!, ["verify", "/pa", "/v", path], cancellationToken).ConfigureAwait(false);
            return verifyResult.ExitCode == 0 ? null : $"signtool verification failed: {verifyResult.Error}";
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return $"The signing tool could not complete: {exception.Message}";
        }
    }

    private static async Task<(int ExitCode, string Error)> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The signing tool did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, (await error.ConfigureAwait(false)) + (await output.ConfigureAwait(false)));
    }

    private static FoundrySigningResult Failure(string code, string message, string path) =>
        new(false, [], [new FoundryDiagnostic(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(path))]);
}
