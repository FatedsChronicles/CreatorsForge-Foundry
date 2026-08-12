namespace CreatorsForge.Foundry.Core.Projects;

public sealed record FoundryProjectNameSuggestion(
    string ProjectName,
    string PackageId,
    string DestinationFolder);

public static class FoundryProjectNamingService
{
    public static FoundryProjectNameSuggestion Suggest(
        string projectName,
        string parentDirectory,
        string packageFallback = "my-extension",
        string folderFallback = "MyExtension")
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
            $"com.example.{(slug.Length == 0 ? packageFallback : slug)}",
            Path.Combine(parentDirectory, folder.Length == 0 ? folderFallback : folder));
    }
}
