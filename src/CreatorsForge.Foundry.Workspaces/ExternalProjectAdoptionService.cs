using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public static class ExternalProjectAdoptionService
{
    private const int MaximumFiles = 10_000;
    private const int MaximumDepth = 32;
    private static readonly HashSet<string> IgnoredDirectories = new(
        [".git", ".vs", ".idea", "bin", "obj", "build", "TestResults", "node_modules"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static Task<WorkspaceOperationResult<ExternalProjectAnalysis>> AnalyzeAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(projectDirectory, cancellationToken), cancellationToken);

    public static async Task<WorkspaceOperationResult<FoundryWorkspace>> AdoptAsync(
        ExternalProjectAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var analysis = request.Analysis;
        var refreshed = await AnalyzeAsync(analysis.ProjectDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (!refreshed.IsSuccess)
        {
            return new(null, refreshed.Diagnostics);
        }

        if (!refreshed.Value!.ManagedSources.SequenceEqual(analysis.ManagedSources, StringComparer.Ordinal) ||
            !refreshed.Value.NativeSources.SequenceEqual(analysis.NativeSources, StringComparer.Ordinal) ||
            !refreshed.Value.ExistingFoundryProjects.SequenceEqual(analysis.ExistingFoundryProjects, StringComparer.Ordinal))
        {
            return Failure<FoundryWorkspace>(
                "CFW0509",
                "The source folder changed after preview. Analyze it again before adopting it.",
                analysis.ProjectDirectory);
        }

        analysis = refreshed.Value;
        if (analysis.ExistingFoundryProjects.Count > 0)
        {
            return Failure<FoundryWorkspace>(
                "CFW0505",
                "This folder already contains a Foundry project. Open the existing .foundryproj file instead.",
                analysis.ProjectDirectory);
        }

        var isObs = string.Equals(request.TargetProvider, "obsstudio", StringComparison.Ordinal);
        var sources = isObs ? analysis.NativeSources : analysis.ManagedSources;
        if (sources.Count == 0)
        {
            return Failure<FoundryWorkspace>(
                "CFW0506",
                isObs
                    ? "An OBS project requires at least one existing .c source file."
                    : "A Streamer.bot project requires at least one existing .cs source file.",
                analysis.ProjectDirectory);
        }

        var identifier = CreateIdentifier(request.Name);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Failure<FoundryWorkspace>(
                "CFW0507",
                "The project name must contain at least one letter or digit.",
                analysis.ProjectDirectory);
        }

        var name = request.Name.Trim();
        var id = request.Id.Trim();
        var author = string.IsNullOrWhiteSpace(request.Author) ? "Creator" : request.Author.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? $"Existing {name} source adopted by Creators Forge Foundry."
            : request.Description.Trim();
        var manifest = isObs
            ? new FoundryProjectManifest
            {
                Name = name,
                Id = id,
                Version = "0.1.0",
                Target = new FoundryTarget { Provider = "obsstudio", Profile = request.TargetProfile.Trim() },
                NativeBuild = new FoundryNativeBuild { Sources = analysis.NativeSources },
                ObsPlugin = new FoundryObsPlugin
                {
                    Contract = FoundryObsPlugin.MinimalContract,
                    ModuleName = CreateObsModuleName(id),
                    EntrySymbol = request.ObsEntrySymbol.Trim(),
                    DisplayName = name,
                    Author = author,
                    Description = description,
                    ApiVersion = FoundryObsPlugin.MinimalApiVersion,
                },
                Outputs = [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage],
            }
            : new FoundryProjectManifest
            {
                Name = name,
                Id = id,
                Version = "0.1.0",
                Target = new FoundryTarget { Provider = "streamerbot", Profile = request.TargetProfile.Trim() },
                Features = new FoundryFeatures { MockRuntime = true },
                ManagedBuild = new FoundryManagedBuild
                {
                    AssemblyName = $"CreatorsForge.Extensions.{identifier}",
                    Sources = analysis.ManagedSources,
                },
                Outputs = [FoundryOutputKinds.ManagedLibrary],
            };

        var projectPath = Path.Combine(analysis.ProjectDirectory, $"{identifier}.foundryproj");
        var diagnostics = FoundryProjectValidator.Validate(manifest, projectPath);
        if (diagnostics.Any(item => item.IsError))
        {
            return new(null, diagnostics);
        }

        var sidecarCreated = false;
        try
        {
            await using var stream = new FileStream(
                projectPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous);
            sidecarCreated = true;
            await JsonSerializer.SerializeAsync(stream, manifest, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (sidecarCreated)
            {
                TryDeleteCreatedSidecar(projectPath);
            }
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (sidecarCreated)
            {
                TryDeleteCreatedSidecar(projectPath);
            }
            return Failure<FoundryWorkspace>(
                "CFW0508",
                $"The Foundry sidecar project could not be created: {exception.Message}",
                projectPath);
        }

        return await FoundryWorkspaceService.OpenAsync(projectPath, cancellationToken)
            .ConfigureAwait(false);
    }

    private static WorkspaceOperationResult<ExternalProjectAnalysis> Analyze(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        string root;
        try
        {
            root = Path.GetFullPath(projectDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure<ExternalProjectAnalysis>("CFW0501", $"The selected folder is invalid: {exception.Message}", projectDirectory);
        }

        if (!Directory.Exists(root))
        {
            return Failure<ExternalProjectAnalysis>("CFW0502", "The selected folder does not exist.", root);
        }

        var managed = new List<string>();
        var native = new List<string>();
        var other = new List<string>();
        var projects = new List<string>();
        var skipped = 0;
        var count = 0;
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        try
        {
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = stack.Pop();
                foreach (var file in Directory.EnumerateFiles(current.Path).Order(StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++count > MaximumFiles)
                    {
                        return Failure<ExternalProjectAnalysis>("CFW0503", $"The folder contains more than {MaximumFiles:N0} files. Narrow the project before adopting it.", root);
                    }

                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    switch (Path.GetExtension(file).ToLowerInvariant())
                    {
                        case ".cs": managed.Add(relative); break;
                        case ".c": native.Add(relative); break;
                        case ".foundryproj": projects.Add(relative); break;
                        default: other.Add(relative); break;
                    }
                }

                foreach (var directory in Directory.EnumerateDirectories(current.Path).OrderDescending(StringComparer.OrdinalIgnoreCase))
                {
                    var info = new DirectoryInfo(directory);
                    if (IgnoredDirectories.Contains(info.Name) ||
                        current.Depth >= MaximumDepth ||
                        info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        skipped++;
                        continue;
                    }

                    stack.Push((directory, current.Depth + 1));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure<ExternalProjectAnalysis>("CFW0504", $"The folder could not be analyzed safely: {exception.Message}", root);
        }

        return new(new(root, managed, native, other, projects, skipped), []);
    }

    private static string CreateIdentifier(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit));

    private static string CreateObsModuleName(string id)
    {
        var value = string.Concat(id.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-'));
        return value.Trim('-');
    }

    private static void TryDeleteCreatedSidecar(string projectPath)
    {
        try
        {
            File.Delete(projectPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static WorkspaceOperationResult<T> Failure<T>(string code, string message, string path)
        where T : class =>
        new(null, [new FoundryDiagnostic(
            code,
            FoundryDiagnosticSeverity.Error,
            message,
            new FoundryDiagnosticLocation(path))]);
}
