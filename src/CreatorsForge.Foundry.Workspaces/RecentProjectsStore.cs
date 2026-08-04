using System.Text.Json;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Workspaces;

public sealed class RecentProjectsStore
{
    private const int MaximumEntries = 10;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string statePath;

    public RecentProjectsStore(string statePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        this.statePath = statePath;
    }

    public async Task<StateLoadResult<IReadOnlyList<RecentProjectEntry>>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(statePath))
        {
            return new([], []);
        }

        try
        {
            var json = await File.ReadAllTextAsync(
                statePath,
                cancellationToken).ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize<List<RecentProjectEntry>>(
                    json,
                    SerializerOptions) ??
                [];
            return new(
                entries
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.ProjectPath))
                    .DistinctBy(
                        entry => Path.GetFullPath(entry.ProjectPath),
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(entry => entry.LastOpenedUtc)
                    .Take(MaximumEntries)
                    .ToArray(),
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(
                [],
                [Warning(
                    "CFW2001",
                    $"Recent projects could not be loaded: {exception.Message}")]);
        }
    }

    public async Task SaveOpenedProjectAsync(
        string projectPath,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var current = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var entries = current.Value
            .Where(entry =>
                !string.Equals(
                    Path.GetFullPath(entry.ProjectPath),
                    fullPath,
                    StringComparison.OrdinalIgnoreCase))
            .Prepend(new(fullPath, projectName, DateTimeOffset.UtcNow))
            .Take(MaximumEntries)
            .ToArray();
        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        await AtomicFile.WriteTextAsync(
            statePath,
            $"{json}\n",
            cancellationToken).ConfigureAwait(false);
    }

    private static FoundryDiagnostic Warning(string code, string message) => new(
        code,
        FoundryDiagnosticSeverity.Warning,
        message);
}
