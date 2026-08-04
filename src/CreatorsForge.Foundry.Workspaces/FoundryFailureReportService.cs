using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryFailureReport(
    int SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string Context,
    string ExceptionType,
    string Message,
    string? StackTrace,
    string ApplicationVersion,
    string OperatingSystem,
    string RuntimeVersion);

public sealed class FoundryFailureReportService(string reportDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public string ReportDirectory { get; } = Path.GetFullPath(reportDirectory);

    public async Task<string> WriteAsync(Exception exception, string context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Directory.CreateDirectory(ReportDirectory);
        var report = new FoundryFailureReport(1, DateTimeOffset.UtcNow, context, exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message, exception.StackTrace, typeof(FoundryFailureReportService).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Environment.OSVersion.VersionString, Environment.Version.ToString());
        var path = Path.Combine(ReportDirectory, $"failure-{report.CreatedAtUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        await AtomicFile.WriteTextAsync(path, JsonSerializer.Serialize(report, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
        return path;
    }

    public IReadOnlyList<string> ListReports() => Directory.Exists(ReportDirectory)
        ? Directory.EnumerateFiles(ReportDirectory, "failure-*.json").OrderByDescending(path => path, StringComparer.Ordinal).Take(100).ToArray()
        : [];

    public async Task<string> CreateBundleAsync(string destinationPath, FoundryUserSettings settings, bool includePaths, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        await using var stream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var includedReports = new List<object>();
        foreach (var reportPath in ListReports())
        {
            var entry = archive.CreateEntry($"failures/{Path.GetFileName(reportPath)}", CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            await using var destination = entry.Open();
            await using var source = new FileStream(reportPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            includedReports.Add(new { file = Path.GetFileName(reportPath), size = new FileInfo(reportPath).Length });
        }
        var summary = new
        {
            schemaVersion = 1,
            createdAtUtc = DateTimeOffset.UtcNow,
            operatingSystem = Environment.OSVersion.VersionString,
            runtimeVersion = Environment.Version.ToString(),
            networkAccessEnabled = settings.AllowNetworkAccess,
            pathsIncluded = includePaths,
            defaultProjectDirectory = includePaths ? settings.DefaultProjectDirectory : "[redacted]",
        };
        var summaryEntry = archive.CreateEntry("system-summary.json", CompressionLevel.Optimal);
        summaryEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var writer = new StreamWriter(summaryEntry.Open(), new UTF8Encoding(false)))
            await writer.WriteAsync(JsonSerializer.Serialize(summary, JsonOptions).AsMemory(), cancellationToken).ConfigureAwait(false);
        var manifestEntry = archive.CreateEntry("bundle-manifest.json", CompressionLevel.Optimal);
        manifestEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var manifestWriter = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
            await manifestWriter.WriteAsync(JsonSerializer.Serialize(new { schemaVersion = 1, createdAtUtc = DateTimeOffset.UtcNow, pathsIncluded = includePaths, reports = includedReports }, JsonOptions).AsMemory(), cancellationToken).ConfigureAwait(false);
        var issueEntry = archive.CreateEntry("issue-report.md", CompressionLevel.Optimal);
        issueEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var issueWriter = new StreamWriter(issueEntry.Open(), new UTF8Encoding(false)))
            await issueWriter.WriteAsync(IssueReportTemplate.AsMemory(), cancellationToken).ConfigureAwait(false);
        return destinationPath;
    }

    public const string IssueReportTemplate = """
        # Foundry private alpha issue report

        Foundry version:
        Windows version:
        Project or sample:
        Provider and host version:

        ## What happened

        Expected:

        Actual:

        ## Steps to reproduce

        1.

        ## Diagnostics

        Diagnostic codes shown by Foundry:
        Reproduces after restart: Yes / No
        Diagnostic bundle reviewed before sharing: Yes / No

        Do not include stream keys, credentials, private source, or personal data.
        """;
}
