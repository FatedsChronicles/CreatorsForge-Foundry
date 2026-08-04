using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Testing;

public sealed record FoundryCompatibilityMatrixCell(
    string Id,
    string Provider,
    string Profile,
    string RuntimeVersion,
    string? RuntimePath,
    FoundryTestOutcome Outcome,
    FoundryTestRunResult Result);

public sealed record FoundryCompatibilityMatrixResult
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string ProjectId { get; init; }
    public required string ProjectVersion { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset FinishedAtUtc { get; init; }
    public required FoundryTestOutcome Outcome { get; init; }
    public IReadOnlyList<FoundryCompatibilityMatrixCell> Cells { get; init; } = [];
    public IReadOnlyList<FoundryDiagnostic> Diagnostics { get; init; } = [];
    public string? ResultPath { get; init; }

    [JsonIgnore]
    public bool IsSuccess =>
        Outcome == FoundryTestOutcome.Passed && Diagnostics.All(item => !item.IsError);
}

public sealed record FoundryCompatibilityMatrixRequest(
    FoundryProjectManifest Manifest,
    string ProjectPath,
    string ArtifactPath,
    IReadOnlyList<string> ObsRoots,
    string? NativeHostAssembly = null,
    TimeSpan? Timeout = null);

public static class FoundryCompatibilityMatrixRunner
{
    public const string RelativeResultPath = "test-results/compatibility-matrix.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<FoundryCompatibilityMatrixResult> RunAsync(
        FoundryCompatibilityMatrixRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = DateTimeOffset.UtcNow;
        var projectRoot = Path.GetDirectoryName(Path.GetFullPath(request.ProjectPath))!;
        var resultPath = FoundryTestRunner.ResolveResultPath(projectRoot, RelativeResultPath);
        var diagnostics = new List<FoundryDiagnostic>();
        var cells = new List<FoundryCompatibilityMatrixCell>();
        var target = request.Manifest.Target;
        if (target is null)
        {
            diagnostics.Add(Error("CFT3003", "The project does not declare a target provider and profile.", request.ProjectPath));
        }

        var definitionPath = ResolveProjectPath(projectRoot, request.Manifest.TestDefinition, diagnostics, request.ProjectPath);
        FoundryTestDefinitionLoadResult? loaded = null;
        if (definitionPath is not null && target is not null)
        {
            loaded = await FoundryTestDefinitionLoader.LoadAsync(
                definitionPath,
                target.Provider,
                cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(loaded.Diagnostics);
        }

        var configuredProfiles = loaded?.Definition?.Profiles ?? [];
        var profiles = configuredProfiles.Count > 0
            ? configuredProfiles
            : target is null ? [] : [target.Profile];

        if (loaded?.IsSuccess == true)
        {
            if (string.Equals(target!.Provider, FoundryTestProviders.StreamerBot, StringComparison.Ordinal))
            {
                foreach (var profile in profiles)
                {
                    var cellManifest = request.Manifest with
                    {
                        Target = target with { Profile = profile },
                    };
                    var result = await FoundryProviderTestOrchestrator.RunAsync(
                        new(
                            cellManifest,
                            request.ProjectPath,
                            request.ArtifactPath,
                            ResultRelativePath: $"test-results/matrix/{ToFileName(profile)}.json"),
                        cancellationToken).ConfigureAwait(false);
                    cells.Add(new(profile, FoundryTestProviders.StreamerBot, profile, "mock-runtime-v1", null, result.Outcome, result));
                }
            }
            else if (request.ObsRoots.Count == 0 || string.IsNullOrWhiteSpace(request.NativeHostAssembly))
            {
                diagnostics.Add(Error("CFT3001", "The OBS compatibility matrix requires at least one --obs installation.", request.ProjectPath));
            }
            else
            {
                var roots = request.ObsRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var profile in profiles)
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    var obsRoot = roots[rootIndex];
                    var executable = Path.Combine(Path.GetFullPath(obsRoot), "bin", "64bit", "obs64.exe");
                    var version = File.Exists(executable)
                        ? FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "unknown"
                        : "missing";
                    var cellId = $"{profile}-{version}-{rootIndex + 1}";
                    var cellManifest = request.Manifest with
                    {
                        Target = target with { Profile = profile },
                    };
                    var result = await FoundryProviderTestOrchestrator.RunAsync(
                        new(
                            cellManifest,
                            request.ProjectPath,
                            request.ArtifactPath,
                            obsRoot,
                            request.NativeHostAssembly,
                            $"test-results/matrix/{ToFileName(cellId)}.json",
                            request.Timeout),
                        cancellationToken).ConfigureAwait(false);
                    cells.Add(new(cellId, FoundryTestProviders.ObsStudio, profile, version, Path.GetFullPath(obsRoot), result.Outcome, result));
                }
            }
        }

        var outcome = diagnostics.Any(item => item.IsError) || cells.Any(item => item.Outcome == FoundryTestOutcome.Error)
            ? FoundryTestOutcome.Error
            : cells.Any(item => item.Outcome == FoundryTestOutcome.Failed)
                ? FoundryTestOutcome.Failed
                : cells.Count > 0 ? FoundryTestOutcome.Passed : FoundryTestOutcome.Skipped;
        var matrix = new FoundryCompatibilityMatrixResult
        {
            ProjectId = request.Manifest.Id,
            ProjectVersion = request.Manifest.Version,
            StartedAtUtc = started,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Outcome = outcome,
            Cells = cells,
            Diagnostics = diagnostics,
            ResultPath = resultPath,
        };
        await WriteAsync(resultPath, matrix, cancellationToken).ConfigureAwait(false);
        return matrix;
    }

    private static string? ResolveProjectPath(
        string root,
        string? relative,
        List<FoundryDiagnostic> diagnostics,
        string projectPath)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            diagnostics.Add(Error("CFT3003", "The project does not declare a testDefinition.", projectPath));
            return null;
        }

        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        diagnostics.Add(Error("CFT3003", "The test definition must remain inside the project directory.", projectPath));
        return null;
    }

    private static string ToFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
    }

    private static async Task WriteAsync(
        string path,
        FoundryCompatibilityMatrixResult result,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(result, JsonOptions) + "\n",
                new UTF8Encoding(false),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static FoundryDiagnostic Error(string code, string message, string path) => new(
        code,
        FoundryDiagnosticSeverity.Error,
        message,
        new(path));
}
