using System.Text;
using CreatorsForge.Foundry.Core.Projects;

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
        var suggestion = FoundryProjectNamingService.Suggest(
            projectName, parentDirectory, "imported-extension", "ImportedExtension");
        return new(suggestion.ProjectName, suggestion.PackageId, suggestion.DestinationFolder);
    }
}
