using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CreatorsForge.Foundry.Workspaces;

public sealed class RecoveryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string recoveryDirectory;

    public string RecoveryDirectory => recoveryDirectory;

    public RecoveryStore(string recoveryDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        this.recoveryDirectory = recoveryDirectory;
    }

    public async Task WriteAsync(
        string documentPath,
        string text,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(documentPath);
        var recovery = new RecoveryDocument(
            fullPath,
            text,
            DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(recovery, SerializerOptions);
        await AtomicFile.WriteTextAsync(
            GetRecoveryPath(fullPath),
            $"{json}\n",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<RecoveryDocument?> ReadAsync(
        string documentPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(documentPath);
        var recoveryPath = GetRecoveryPath(fullPath);
        if (!File.Exists(recoveryPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(
                recoveryPath,
                cancellationToken).ConfigureAwait(false);
            var recovery = JsonSerializer.Deserialize<RecoveryDocument>(
                json,
                SerializerOptions);
            return recovery is not null &&
                string.Equals(
                    recovery.DocumentPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase)
                ? recovery
                : null;
        }
        catch (Exception exception) when (
            exception is IOException
                or JsonException
                or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task DeleteAsync(string documentPath)
    {
        var recoveryPath = GetRecoveryPath(Path.GetFullPath(documentPath));
        if (File.Exists(recoveryPath))
        {
            File.Delete(recoveryPath);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<RecoveryDocument>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(recoveryDirectory)) return [];
        var documents = new List<RecoveryDocument>();
        foreach (var path in Directory.EnumerateFiles(recoveryDirectory, "*.recovery.json").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (JsonSerializer.Deserialize<RecoveryDocument>(json, SerializerOptions) is { } document)
                    documents.Add(document);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return documents.OrderByDescending(item => item.RecoveredAtUtc).ToArray();
    }

    private string GetRecoveryPath(string fullDocumentPath)
    {
        var normalized = fullDocumentPath.ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Path.Combine(
            recoveryDirectory,
            $"{Convert.ToHexStringLower(hash)}.recovery.json");
    }
}
