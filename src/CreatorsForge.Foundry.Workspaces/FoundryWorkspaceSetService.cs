using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryWorkspaceSetManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> Projects { get; init; } = [];

    public string? StartupProject { get; init; }
}

public sealed record FoundryWorkspaceSet(
    string WorkspacePath,
    string WorkspaceRoot,
    FoundryWorkspaceSetManifest Manifest,
    IReadOnlyList<FoundryWorkspace> Projects,
    FoundryWorkspace ActiveProject);

public static class FoundryWorkspaceSetService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static async Task<WorkspaceOperationResult<FoundryWorkspaceSet>> LoadAsync(
        string workspacePath,
        CancellationToken cancellationToken = default)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(workspacePath); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure("CFW1301", $"The workspace path is invalid: {exception.Message}", workspacePath);
        }

        if (!File.Exists(fullPath)) return Failure("CFW1302", "The workspace file does not exist.", fullPath);
        FoundryWorkspaceSetManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<FoundryWorkspaceSetManifest>(
                await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false),
                SerializerOptions);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Failure("CFW1303", $"The workspace could not be loaded: {exception.Message}", fullPath);
        }

        var validation = Validate(manifest, fullPath);
        if (validation.Count != 0) return new(null, validation);
        var root = Path.GetDirectoryName(fullPath)!;
        var projects = new List<FoundryWorkspace>();
        var diagnostics = new List<FoundryDiagnostic>();
        foreach (var relativePath in manifest!.Projects)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var result = await FoundryWorkspaceService.OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(result.Diagnostics);
            if (result.Value is not null) projects.Add(result.Value);
        }

        if (diagnostics.Any(item => item.IsError) || projects.Count != manifest.Projects.Count)
        {
            diagnostics.Add(new("CFW1304", FoundryDiagnosticSeverity.Error, "Every workspace project must load successfully before the workspace can open.", new FoundryDiagnosticLocation(fullPath)));
            return new(null, diagnostics);
        }

        var active = manifest.StartupProject is null
            ? projects[0]
            : projects[manifest.Projects
                .Select((path, index) => (path, index))
                .First(item => string.Equals(item.path, manifest.StartupProject, StringComparison.OrdinalIgnoreCase))
                .index];
        return new(new(fullPath, root, manifest, projects, active), diagnostics);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspaceSet>> CreateAsync(
        string workspacePath,
        string name,
        IReadOnlyList<string> projectPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        var fullPath = Path.GetFullPath(workspacePath);
        var root = Path.GetDirectoryName(fullPath)!;
        var relativeProjects = projectPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();
        var manifest = new FoundryWorkspaceSetManifest
        {
            Name = name.Trim(),
            Projects = relativeProjects,
            StartupProject = relativeProjects.FirstOrDefault(),
        };
        var diagnostics = Validate(manifest, fullPath);
        if (diagnostics.Count != 0) return new(null, diagnostics);
        foreach (var projectPath in projectPaths)
        {
            var project = await FoundryWorkspaceService.OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
            if (!project.IsSuccess)
            {
                return new(null, project.Diagnostics.Append(new FoundryDiagnostic(
                    "CFW1306",
                    FoundryDiagnosticSeverity.Error,
                    "The workspace was not written because a selected project is invalid.",
                    new FoundryDiagnosticLocation(fullPath))).ToArray());
            }
        }
        await SaveManifestAsync(fullPath, manifest, cancellationToken).ConfigureAwait(false);
        return await LoadAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspaceSet>> AddProjectAsync(
        FoundryWorkspaceSet workspace,
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var relative = Path.GetRelativePath(workspace.WorkspaceRoot, Path.GetFullPath(projectPath)).Replace('\\', '/');
        if (workspace.Manifest.Projects.Contains(relative, StringComparer.OrdinalIgnoreCase))
        {
            return Failure("CFW1305", "That project already belongs to this workspace.", workspace.WorkspacePath);
        }

        var manifest = workspace.Manifest with { Projects = workspace.Manifest.Projects.Append(relative).ToArray() };
        var diagnostics = Validate(manifest, workspace.WorkspacePath);
        if (diagnostics.Count != 0) return new(null, diagnostics);
        await SaveManifestAsync(workspace.WorkspacePath, manifest, cancellationToken).ConfigureAwait(false);
        return await LoadAsync(workspace.WorkspacePath, cancellationToken).ConfigureAwait(false);
    }

    public static FoundryWorkspaceSet Activate(FoundryWorkspaceSet workspace, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var project = workspace.Projects.FirstOrDefault(item => string.Equals(item.ProjectPath, Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException("The project is not a member of this workspace.", nameof(projectPath));
        return workspace with { ActiveProject = project };
    }

    private static List<FoundryDiagnostic> Validate(FoundryWorkspaceSetManifest? manifest, string path)
    {
        var diagnostics = new List<FoundryDiagnostic>();
        if (manifest is null || manifest.SchemaVersion != FoundryWorkspaceSetManifest.CurrentSchemaVersion)
            diagnostics.Add(Error("CFW1310", "Workspace schema version 1 is required.", path));
        if (manifest is null) return diagnostics;
        if (string.IsNullOrWhiteSpace(manifest.Name)) diagnostics.Add(Error("CFW1311", "Workspace name is required.", path));
        if (manifest.Projects is null or { Count: 0 }) diagnostics.Add(Error("CFW1312", "A workspace must contain at least one project.", path));
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in manifest.Projects)
            {
                if (!IsSafeProjectPath(project) || !seen.Add(project)) diagnostics.Add(Error("CFW1313", $"Workspace project path '{project}' is invalid or duplicated.", path));
            }
            if (manifest.StartupProject is not null && !manifest.Projects.Contains(manifest.StartupProject, StringComparer.OrdinalIgnoreCase))
                diagnostics.Add(Error("CFW1314", "startupProject must identify a workspace project.", path));
        }
        return diagnostics;
    }

    private static bool IsSafeProjectPath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        path.EndsWith(".foundryproj", StringComparison.OrdinalIgnoreCase) &&
        !path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal);

    private static Task SaveManifestAsync(string path, FoundryWorkspaceSetManifest manifest, CancellationToken cancellationToken) =>
        AtomicFile.WriteTextAsync(path, JsonSerializer.Serialize(manifest, SerializerOptions) + "\n", cancellationToken);

    private static FoundryDiagnostic Error(string code, string message, string path) =>
        new(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(path));

    private static WorkspaceOperationResult<FoundryWorkspaceSet> Failure(string code, string message, string path) => new(null, [Error(code, message, path)]);
}
