using System.Text.Json;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Core.Projects;

/// <summary>
/// Loads a Foundry manifest as data. It never loads or executes project code.
/// </summary>
public static class FoundryProjectLoader
{
    public const long MaximumManifestBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    public static async Task<FoundryProjectLoadResult> LoadAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Failure(
                null,
                "CFL0001",
                "A project manifest path is required.",
                "Pass the path to a .foundryproj file.");
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(projectPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                projectPath,
                "CFL0001",
                $"The project manifest path is invalid: {exception.Message}",
                "Pass a valid path to a .foundryproj file.");
        }

        if (!string.Equals(
                Path.GetExtension(fullPath),
                ".foundryproj",
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                fullPath,
                "CFL0002",
                "The project manifest must use the .foundryproj extension.",
                "Rename the manifest with a .foundryproj extension.");
        }

        if (!File.Exists(fullPath))
        {
            return Failure(
                fullPath,
                "CFL0001",
                "The project manifest does not exist.",
                "Check the path and try again.");
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > MaximumManifestBytes)
            {
                return Failure(
                    fullPath,
                    "CFL0003",
                    $"The project manifest exceeds the {MaximumManifestBytes} byte safety limit.",
                    "Remove generated or binary data from the manifest.");
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var manifest = await JsonSerializer.DeserializeAsync<FoundryProjectManifest>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);

            return manifest is null
                ? Failure(
                    fullPath,
                    "CFL0006",
                    "The project manifest does not contain a JSON object.",
                    "Add a schemaVersion and the required project fields.")
                : new(fullPath, manifest, []);
        }
        catch (JsonException exception)
        {
            var line = exception.LineNumber is null ? null : exception.LineNumber + 1;
            var column = exception.BytePositionInLine is null
                ? null
                : exception.BytePositionInLine + 1;
            var location = new FoundryDiagnosticLocation(
                fullPath,
                exception.Path,
                line,
                column);
            var diagnostic = new FoundryDiagnostic(
                "CFL0005",
                FoundryDiagnosticSeverity.Error,
                $"The project manifest is not valid JSON: {exception.Message}",
                location,
                "Correct the JSON syntax or field type and try again.");

            return new(fullPath, null, [diagnostic]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                fullPath,
                "CFL0004",
                $"The project manifest could not be read: {exception.Message}",
                "Check that the file is accessible and try again.");
        }
    }

    private static FoundryProjectLoadResult Failure(
        string? projectPath,
        string code,
        string message,
        string suggestedFix)
    {
        var location = projectPath is null
            ? null
            : new FoundryDiagnosticLocation(projectPath);
        var diagnostic = new FoundryDiagnostic(
            code,
            FoundryDiagnosticSeverity.Error,
            message,
            location,
            suggestedFix);

        return new(projectPath, null, [diagnostic]);
    }
}
