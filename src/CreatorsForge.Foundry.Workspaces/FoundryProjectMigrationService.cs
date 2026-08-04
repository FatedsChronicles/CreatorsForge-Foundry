using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryMigrationPlan(
    string ProjectPath,
    int SourceSchemaVersion,
    int TargetSchemaVersion,
    bool IsRequired,
    IReadOnlyList<string> Changes,
    string BackupPath);

public sealed record FoundryMigrationInspection(
    FoundryMigrationPlan? Plan,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess => Plan is not null && Diagnostics.All(item => !item.IsError);
}

public sealed record FoundryMigrationResult(
    FoundryWorkspace? Workspace,
    FoundryMigrationPlan? Plan,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess => Workspace is not null && Diagnostics.All(item => !item.IsError);
}

public static class FoundryProjectMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<FoundryMigrationInspection> InspectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath)) return InspectionFailure("CFW1501", "The project manifest does not exist.", fullPath);
        try
        {
            var json = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return InspectionFailure("CFW1502", "The project manifest must contain a JSON object.", fullPath);
            var sourceVersion = document.RootElement.TryGetProperty("schemaVersion", out var versionElement)
                ? versionElement.GetInt32()
                : 0;
            if (sourceVersion > FoundryProjectManifest.CurrentSchemaVersion)
                return InspectionFailure("CFW1503", $"Schema {sourceVersion} was created by a newer Foundry version and cannot be migrated backward.", fullPath);
            if (sourceVersion is not (0 or FoundryProjectManifest.CurrentSchemaVersion))
                return InspectionFailure("CFW1504", $"No reviewed migration path exists from schema {sourceVersion}.", fullPath);

            var changes = new List<string>();
            if (sourceVersion == 0) changes.Add("Set schemaVersion to 1.");
            if (!document.RootElement.TryGetProperty("features", out _)) changes.Add("Add explicit feature defaults.");
            if (!document.RootElement.TryGetProperty("components", out _)) changes.Add("Add an empty reusable-component inventory.");
            if (!document.RootElement.TryGetProperty("template", out _)) changes.Add("Record inferred template provenance.");
            var required = sourceVersion == 0;
            if (!required) changes.Clear();
            return new(new(
                fullPath,
                sourceVersion,
                FoundryProjectManifest.CurrentSchemaVersion,
                required,
                changes,
                fullPath + ".schema0.backup"), []);
        }
        catch (JsonException exception) { return InspectionFailure("CFW1502", $"The project JSON is invalid: {exception.Message}", fullPath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return InspectionFailure("CFW1501", $"The project manifest could not be read: {exception.Message}", fullPath); }
    }

    public static async Task<FoundryMigrationResult> MigrateAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(projectPath, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsSuccess) return new(null, inspection.Plan, inspection.Diagnostics);
        var plan = inspection.Plan!;
        if (!plan.IsRequired)
        {
            var current = await FoundryWorkspaceService.OpenAsync(plan.ProjectPath, cancellationToken).ConfigureAwait(false);
            return new(current.Value, plan, current.Diagnostics);
        }

        var originalJson = await File.ReadAllTextAsync(plan.ProjectPath, cancellationToken).ConfigureAwait(false);
        FoundryProjectManifest legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<FoundryProjectManifest>(originalJson, JsonOptions) ?? throw new JsonException("Manifest is empty.");
        }
        catch (JsonException exception)
        {
            return new(null, plan, [Error("CFW1505", $"The legacy manifest could not be interpreted: {exception.Message}", plan.ProjectPath)]);
        }

        var template = InferTemplate(legacy);
        var provider = legacy.Target?.Provider;
        var outputs = legacy.Outputs is { Count: > 0 }
            ? legacy.Outputs
            : string.Equals(provider, "obsstudio", StringComparison.Ordinal)
                ? [FoundryOutputKinds.ObsPlugin, FoundryOutputKinds.ObsPluginPackage]
                : [FoundryOutputKinds.ManagedLibrary, FoundryOutputKinds.CphInlineBridge, FoundryOutputKinds.StreamerBotPackage];
        var migrated = legacy with
        {
            SchemaVersion = FoundryProjectManifest.CurrentSchemaVersion,
            Features = legacy.Features ?? new FoundryFeatures(),
            Components = legacy.Components ?? [],
            Template = legacy.Template ?? (template is null ? null : new FoundryProjectTemplateReference
            {
                Id = template.Id,
                Revision = template.Revision,
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["migration"] = "schema-0" },
            }),
            TargetDefinition = legacy.TargetDefinition ?? (string.Equals(provider, "streamerbot", StringComparison.Ordinal) ? "streamerbot/streamerbot.json" : null),
            Outputs = outputs,
        };
        var diagnostics = FoundryProjectValidator.Validate(migrated, plan.ProjectPath);
        if (diagnostics.Any(item => item.IsError))
            return new(null, plan, diagnostics.Append(Error("CFW1506", "Migration was not written because the resulting schema-v1 project is invalid.", plan.ProjectPath)).ToArray());

        try
        {
            if (File.Exists(plan.BackupPath))
            {
                var existing = await File.ReadAllTextAsync(plan.BackupPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(existing, originalJson, StringComparison.Ordinal))
                    return new(null, plan, [Error("CFW1507", "The migration backup already exists with different content.", plan.BackupPath)]);
            }
            else
            {
                await AtomicFile.WriteTextAsync(plan.BackupPath, originalJson, cancellationToken).ConfigureAwait(false);
            }
            await AtomicFile.WriteTextAsync(plan.ProjectPath, JsonSerializer.Serialize(migrated, JsonOptions) + "\n", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(null, plan, [Error("CFW1508", $"Migration could not be saved: {exception.Message}", plan.ProjectPath)]);
        }

        var opened = await FoundryWorkspaceService.OpenAsync(plan.ProjectPath, cancellationToken).ConfigureAwait(false);
        return new(opened.Value, plan, opened.Diagnostics);
    }

    private static FoundryProjectTemplateDescriptor? InferTemplate(FoundryProjectManifest manifest)
    {
        var id = manifest.Target?.Provider switch
        {
            "streamerbot" => FoundryProjectTemplateService.StreamerBotCommand,
            "obsstudio" => manifest.ObsPlugin?.Design?.Template switch
            {
                FoundryObsDesign.ModuleStarterTemplate => FoundryProjectTemplateService.ObsModule,
                FoundryObsDesign.ConfigurableFilterTemplate => FoundryProjectTemplateService.ObsConfigurableFilter,
                FoundryObsDesign.VideoInputTemplate => FoundryProjectTemplateService.ObsVideoInput,
                FoundryObsDesign.OutputTemplate => FoundryProjectTemplateService.ObsOutput,
                _ => FoundryProjectTemplateService.ObsPassthroughFilter,
            },
            _ => null,
        };
        return id is null ? null : FoundryProjectTemplateService.Templates.Single(item => item.Id == id);
    }

    private static FoundryMigrationInspection InspectionFailure(string code, string message, string path) => new(null, [Error(code, message, path)]);
    private static FoundryDiagnostic Error(string code, string message, string path) => new(code, FoundryDiagnosticSeverity.Error, message, new FoundryDiagnosticLocation(path));
}
