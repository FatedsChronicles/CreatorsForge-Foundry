using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public static class FoundryPublishingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> SaveMetadataAsync(
        FoundryWorkspace workspace,
        FoundryPublishing publishing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(publishing);
        var updated = workspace.Manifest with { Publishing = Normalize(publishing) };
        var diagnostics = FoundryProjectValidator.Validate(updated, workspace.ProjectPath);
        if (diagnostics.Any(item => item.IsError)) return new(null, diagnostics);
        try
        {
            await AtomicFile.WriteTextAsync(
                workspace.ProjectPath,
                JsonSerializer.Serialize(updated, JsonOptions) + "\n",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("CFW1601", $"Publishing metadata could not be saved: {exception.Message}", workspace.ProjectPath);
        }
        return await FoundryWorkspaceService.OpenAsync(workspace.ProjectPath, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> SaveReleaseSettingsAsync(
        FoundryWorkspace workspace,
        FoundryPublishing publishing,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(publishing);
        var updated = workspace.Manifest with
        {
            Publishing = Normalize(publishing),
            Version = version.Trim(),
        };
        var diagnostics = FoundryProjectValidator.Validate(updated, workspace.ProjectPath);
        if (diagnostics.Any(item => item.IsError)) return new(null, diagnostics);
        try
        {
            await AtomicFile.WriteTextAsync(
                workspace.ProjectPath,
                JsonSerializer.Serialize(updated, JsonOptions) + "\n",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("CFW1603", $"Publishing settings could not be saved: {exception.Message}", workspace.ProjectPath);
        }
        return await FoundryWorkspaceService.OpenAsync(workspace.ProjectPath, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> SetVersionAsync(
        FoundryWorkspace workspace,
        string versionOrBump,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var version = versionOrBump.ToLowerInvariant() switch
        {
            "major" => Bump(workspace.Manifest.Version, 0),
            "minor" => Bump(workspace.Manifest.Version, 1),
            "patch" => Bump(workspace.Manifest.Version, 2),
            _ => versionOrBump.Trim(),
        };
        var updated = workspace.Manifest with { Version = version };
        var diagnostics = FoundryProjectValidator.Validate(updated, workspace.ProjectPath);
        if (diagnostics.Any(item => item.IsError)) return new(null, diagnostics);
        try
        {
            await AtomicFile.WriteTextAsync(workspace.ProjectPath, JsonSerializer.Serialize(updated, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("CFW1602", $"Project version could not be saved: {exception.Message}", workspace.ProjectPath);
        }
        return await FoundryWorkspaceService.OpenAsync(workspace.ProjectPath, cancellationToken).ConfigureAwait(false);
    }

    private static FoundryPublishing Normalize(FoundryPublishing publishing) => publishing with
    {
        PackageName = publishing.PackageName.Trim(),
        Summary = publishing.Summary.Trim(),
        Authors = (publishing.Authors ?? []).Select(item => item.Trim()).Where(item => item.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        LicenseFile = publishing.LicenseFile.Trim().Replace('\\', '/'),
        ChangelogFile = publishing.ChangelogFile.Trim().Replace('\\', '/'),
        Homepage = NullIfEmpty(publishing.Homepage),
        Repository = NullIfEmpty(publishing.Repository),
        Tags = (publishing.Tags ?? []).Select(item => item.Trim()).Where(item => item.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
        Dependencies = (publishing.Dependencies ?? []).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
        Signing = publishing.Signing ?? new FoundrySigningConfiguration(),
    };

    private static string Bump(string version, int part)
    {
        var core = version.Split(['-', '+'], 2)[0].Split('.');
        if (core.Length != 3 || !core.All(item => int.TryParse(item, out _))) return versionOrInvalid();
        var values = core.Select(int.Parse).ToArray();
        values[part]++;
        for (var index = part + 1; index < values.Length; index++) values[index] = 0;
        return string.Join('.', values);

        static string versionOrInvalid() => string.Empty;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static WorkspaceOperationResult<FoundryWorkspace> Failure(string code, string message, string path) =>
        new(null, [new FoundryDiagnostic(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(path))]);
}
