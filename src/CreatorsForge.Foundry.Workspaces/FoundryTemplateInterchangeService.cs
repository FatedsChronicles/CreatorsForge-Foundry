using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryTemplateFile(string Path, string Content);

public sealed record FoundryTemplatePackage
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public FoundryProjectManifest? Project { get; init; }
    public IReadOnlyList<FoundryTemplateFile> Files { get; init; } = [];
}

public sealed record FoundryTemplateImportRequest(
    string TemplatePath,
    string ProjectDirectory,
    string Name,
    string Id,
    string TargetProfile);

public static class FoundryTemplateInterchangeService
{
    public const long MaximumTemplateBytes = 4 * 1024 * 1024;
    private const int MaximumFiles = 256;
    private static readonly HashSet<string> AllowedExtensions = new(
        [".cs", ".c", ".h", ".json", ".md", ".txt", ".xml", ".props", ".targets", ".resx", ".yml", ".yaml"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static async Task<IReadOnlyList<FoundryDiagnostic>> ExportAsync(
        FoundryWorkspace workspace,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var files = Flatten(workspace.ProjectTree)
            .Where(node => !node.IsDirectory && AllowedExtensions.Contains(Path.GetExtension(node.FullPath)))
            .Where(node => !string.Equals(node.FullPath, workspace.ProjectPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(node => node.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (files.Length > MaximumFiles)
            return [Error("CFW1401", $"A template may contain at most {MaximumFiles} text files.", destinationPath)];

        var packageFiles = new List<FoundryTemplateFile>();
        long totalBytes = 0;
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file.FullPath, cancellationToken).ConfigureAwait(false);
            totalBytes += Encoding.UTF8.GetByteCount(content);
            if (totalBytes > MaximumTemplateBytes)
                return [Error("CFW1402", $"Template text exceeds the {MaximumTemplateBytes} byte safety limit.", destinationPath)];
            packageFiles.Add(new(file.RelativePath, content));
        }

        var package = new FoundryTemplatePackage
        {
            Id = workspace.Manifest.Id + ".template-v1",
            Name = workspace.Manifest.Name,
            Version = workspace.Manifest.Version,
            Provider = workspace.Manifest.Target!.Provider,
            Description = workspace.Manifest.Template?.Parameters.GetValueOrDefault("description") ?? $"Reusable template exported from {workspace.Manifest.Name}.",
            Project = workspace.Manifest,
            Files = packageFiles,
        };
        await AtomicFile.WriteTextAsync(destinationPath, JsonSerializer.Serialize(package, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
        return [];
    }

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> ImportAsync(
        FoundryTemplateImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var loaded = await LoadAsync(request.TemplatePath, cancellationToken).ConfigureAwait(false);
        if (loaded.Package is null) return new(null, loaded.Diagnostics);
        var package = loaded.Package;
        var directory = Path.GetFullPath(request.ProjectDirectory);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            return Failure("CFW1403", "The imported project directory must be empty.", directory);

        var original = package.Project!;
        var oldIdentifier = CreateIdentifier(original.Name);
        var newIdentifier = CreateIdentifier(request.Name);
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        AddReplacement(replacements, original.Name, request.Name.Trim());
        AddReplacement(replacements, original.Id, request.Id.Trim());
        AddReplacement(replacements, original.Target!.Profile, request.TargetProfile.Trim());
        AddReplacement(replacements, oldIdentifier, newIdentifier);
        if (original.ManagedBuild?.AssemblyName is { Length: > 0 } assembly)
            replacements[assembly] = ReplaceAll(assembly, replacements);
        if (original.ObsPlugin?.ModuleName is { Length: > 0 } module)
            replacements[module] = CreateObsModuleName(request.Id);

        var manifestJson = ReplaceAll(JsonSerializer.Serialize(original, JsonOptions), replacements);
        var manifest = JsonSerializer.Deserialize<FoundryProjectManifest>(manifestJson, JsonOptions)! with
        {
            Template = new FoundryProjectTemplateReference
            {
                Id = package.Id,
                Revision = package.Version,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sourceTemplate"] = Path.GetFileName(request.TemplatePath),
                    ["description"] = package.Description,
                },
            },
        };
        var projectPath = Path.Combine(directory, newIdentifier + ".foundryproj");
        var diagnostics = FoundryProjectValidator.Validate(manifest, projectPath);
        if (diagnostics.Any(item => item.IsError)) return new(null, diagnostics);

        foreach (var file in package.Files)
        {
            if (!IsSafeFilePath(file.Path)) return Failure("CFW1404", $"Template file path '{file.Path}' is unsafe.", request.TemplatePath);
        }

        var createdDirectory = !Directory.Exists(directory);
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in package.Files.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                await AtomicFile.WriteTextAsync(
                    Path.Combine(directory, file.Path.Replace('/', Path.DirectorySeparatorChar)),
                    ReplaceAll(file.Content, replacements),
                    cancellationToken).ConfigureAwait(false);
            }
            await AtomicFile.WriteTextAsync(projectPath, JsonSerializer.Serialize(manifest, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { if (createdDirectory) TryDelete(directory); throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (createdDirectory) TryDelete(directory);
            return Failure("CFW1405", $"The template could not be imported: {exception.Message}", request.TemplatePath);
        }
        return await FoundryWorkspaceService.OpenAsync(projectPath, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(FoundryTemplatePackage? Package, IReadOnlyList<FoundryDiagnostic> Diagnostics)> LoadAsync(
        string templatePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(templatePath);
        if (!File.Exists(fullPath)) return (null, [Error("CFW1410", "The template file does not exist.", fullPath)]);
        if (new FileInfo(fullPath).Length > MaximumTemplateBytes)
            return (null, [Error("CFW1411", "The template exceeds the safety limit.", fullPath)]);
        try
        {
            var package = JsonSerializer.Deserialize<FoundryTemplatePackage>(await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false), JsonOptions);
            var diagnostics = ValidatePackage(package, fullPath);
            return diagnostics.Count == 0 ? (package, diagnostics) : (null, diagnostics);
        }
        catch (JsonException exception) { return (null, [Error("CFW1412", $"The template JSON is invalid: {exception.Message}", fullPath)]); }
    }

    private static List<FoundryDiagnostic> ValidatePackage(FoundryTemplatePackage? package, string path)
    {
        var diagnostics = new List<FoundryDiagnostic>();
        if (package is null || package.SchemaVersion != FoundryTemplatePackage.CurrentSchemaVersion)
            diagnostics.Add(Error("CFW1413", "Template schema version 1 is required.", path));
        if (package is null) return diagnostics;
        if (string.IsNullOrWhiteSpace(package.Id) || string.IsNullOrWhiteSpace(package.Name) || string.IsNullOrWhiteSpace(package.Version) ||
            package.Provider is not ("streamerbot" or "obsstudio") || string.IsNullOrWhiteSpace(package.Description) || package.Project is null)
            diagnostics.Add(Error("CFW1414", "Template metadata and project blueprint are required.", path));
        else
        {
            if (!string.Equals(package.Provider, package.Project.Target?.Provider, StringComparison.Ordinal))
                diagnostics.Add(Error("CFW1417", "Template provider must match its project blueprint.", path));
            diagnostics.AddRange(FoundryProjectValidator.Validate(package.Project, path).Where(item => item.IsError));
        }
        if (package.Files is null || package.Files.Count > MaximumFiles || package.Files.Any(file => file is null || !IsSafeFilePath(file.Path)))
            diagnostics.Add(Error("CFW1415", "The template contains too many files or an unsafe file path.", path));
        if (package.Files?.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != package.Files?.Count)
            diagnostics.Add(Error("CFW1416", "Template file paths must be unique.", path));
        return diagnostics;
    }

    private static IEnumerable<ProjectTreeNode> Flatten(IEnumerable<ProjectTreeNode> nodes)
    {
        foreach (var node in nodes) { yield return node; foreach (var child in Flatten(node.Children)) yield return child; }
    }

    private static bool IsSafeFilePath(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        !path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal) && AllowedExtensions.Contains(Path.GetExtension(path));
    private static string ReplaceAll(string value, IReadOnlyDictionary<string, string> replacements) =>
        replacements.Where(item => item.Key.Length != 0).OrderByDescending(item => item.Key.Length).Aggregate(value, (current, item) => current.Replace(item.Key, item.Value, StringComparison.Ordinal));
    private static void AddReplacement(Dictionary<string, string> replacements, string? source, string replacement)
    {
        if (!string.IsNullOrEmpty(source) && !replacements.ContainsKey(source)) replacements[source] = replacement;
    }
    private static string CreateIdentifier(string name) => string.Concat(name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    private static string CreateObsModuleName(string id) => id.Trim().ToLowerInvariant().Replace('.', '-').Replace('_', '-');
    private static void TryDelete(string path) { try { Directory.Delete(path, true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { } }
    private static FoundryDiagnostic Error(string code, string message, string path) => new(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(path));
    private static WorkspaceOperationResult<FoundryWorkspace> Failure(string code, string message, string path) => new(null, [Error(code, message, path)]);
}
