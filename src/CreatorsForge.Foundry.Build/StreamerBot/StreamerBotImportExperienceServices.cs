using System.Text;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public static class StreamerBotImportFileReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.Directory) ||
            string.Equals(info.Extension, ".lnk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choose one local export file, not a folder or shortcut.");
        if (info.Length > StreamerBotEnvelopeCodec.MaximumImportCodeCharacters)
            throw new InvalidDataException("That file exceeds the 16 MiB import-code limit.");

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: false);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (text.Length > StreamerBotEnvelopeCodec.MaximumImportCodeCharacters)
            throw new InvalidDataException("That file exceeds the 16 MiB import-code limit.");
        return text;
    }
}

public sealed record StreamerBotImportNameSuggestion(
    string ProjectName,
    string PackageId,
    string DestinationFolder);

public static class StreamerBotImportNamingService
{
    public static StreamerBotImportNameSuggestion Suggest(string projectName, string parentDirectory)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        var words = projectName.ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0)
            .ToArray();
        var slug = string.Join('-', words);
        var folder = string.Concat(projectName.Where(char.IsLetterOrDigit));
        return new(
            projectName,
            $"com.example.{(slug.Length == 0 ? "imported-extension" : slug)}",
            Path.Combine(parentDirectory, folder.Length == 0 ? "ImportedExtension" : folder));
    }
}
