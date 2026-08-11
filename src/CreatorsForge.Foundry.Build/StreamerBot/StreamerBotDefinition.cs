using System.Text.Json;
using System.Text.Json.Serialization;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public sealed record StreamerBotDefinition
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public StreamerBotMetadata Metadata { get; init; } = new();
    public StreamerBotImportProvenance? Import { get; init; }
    public IReadOnlyList<StreamerBotQueueDefinition> Queues { get; init; } = [];
    public IReadOnlyList<StreamerBotCommand> Commands { get; init; } = [];
    public IReadOnlyList<StreamerBotAction> Actions { get; init; } = [];
    public IReadOnlyList<StreamerBotResourceDefinition> Resources { get; init; } = [];
}

public sealed record StreamerBotMetadata
{
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string MinimumVersion { get; init; } = "1.0.0-alpha.1";
    public string? MaximumTestedVersion { get; init; }
    public string? Documentation { get; init; }
}

public sealed record StreamerBotImportProvenance(
    string Adapter,
    int PayloadVersion,
    string ExportedFrom,
    string? MinimumVersion,
    string SourceSha256,
    string PreservationFile,
    bool HasOpaqueContent,
    string? SourceAttribution);

public sealed record StreamerBotQueueDefinition(
    string Id,
    string Name,
    bool Blocking,
    string? SourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReadOnly = false,
    string? PreservationKey = null,
    string? Description = null);

public sealed record StreamerBotCommand(
    string Id,
    string Name,
    IReadOnlyList<string> Commands,
    bool Enabled,
    bool CaseSensitive,
    int GlobalCooldown,
    int UserCooldown,
    string? SourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReadOnly = false,
    string? PreservationKey = null,
    bool IgnoreBotAccount = true,
    bool IgnoreInternalMessages = true,
    int Sources = 1,
    string? Description = null);

public sealed record StreamerBotAction(
    string Id,
    string Name,
    bool Enabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? QueueId,
    bool Concurrent,
    bool AlwaysRun,
    IReadOnlyList<StreamerBotTrigger> Triggers,
    IReadOnlyList<StreamerBotSubAction> SubActions,
    string? SourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReadOnly = false,
    string? PreservationKey = null,
    string? Group = null,
    string? Description = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool RandomAction = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ExcludeFromPending = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ExcludeFromHistory = false);

public sealed record StreamerBotTrigger(
    string Id,
    string Kind,
    bool Enabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? CommandId,
    int? SourceType = null,
    string? SourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReadOnly = false,
    string? PreservationKey = null);

public sealed record StreamerBotSubAction(
    string Id,
    string Kind,
    bool Enabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? VariableName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Value,
    bool AutoType,
    string? SourcePath = null,
    int? SourceType = null,
    string? SourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ReadOnly = false,
    string? PreservationKey = null,
    IReadOnlyList<string>? References = null,
    double Weight = 0);

public sealed record StreamerBotResourceDefinition(
    string Id,
    string Name,
    string Type,
    bool Required,
    string Portability,
    string? Description = null,
    string? SuggestedValue = null,
    string? ValidationPattern = null,
    IReadOnlyList<StreamerBotResourceBinding>? Bindings = null);

public sealed record StreamerBotResourceBinding(
    string EntityType,
    string EntityId,
    string Property);

public static class StreamerBotResourceTypes
{
    public static IReadOnlyList<string> All { get; } =
    [
        "obsScene", "obsSource", "obsFilter", "obsInput", "obsTransition",
        "twitchReward", "platformAccount", "localFile", "localFolder",
        "executable", "url", "integrationConnection", "custom",
    ];
}

public static class StreamerBotResourcePortability
{
    public const string Portable = "portable";
    public const string ReconnectByName = "reconnectByName";
    public const string ConfirmAfterImport = "confirmAfterImport";
    public const string ManualConfiguration = "manualConfiguration";

    public static IReadOnlyList<string> All { get; } =
        [Portable, ReconnectByName, ConfirmAfterImport, ManualConfiguration];
}

public sealed record StreamerBotDefinitionLoadResult(
    StreamerBotDefinition? Definition,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Definition is not null && Errors.Count == 0;
}

public static class StreamerBotDefinitionLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling =
            System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly JsonSerializerOptions WriteOptions = new(Options)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static StreamerBotDefinitionLoadResult Load(string json)
    {
        StreamerBotDefinition? value;
        try
        {
            value = JsonSerializer.Deserialize<StreamerBotDefinition>(json, Options);
        }
        catch (JsonException exception)
        {
            return new(null, [$"Definition JSON is invalid: {exception.Message}"]);
        }

        if (value is null)
        {
            return new(null, ["Definition JSON is empty."]);
        }

        if (value.SchemaVersion is 1 or 2 or 3)
        {
            value = value with { SchemaVersion = StreamerBotDefinition.CurrentSchemaVersion };
        }

        var errors = Validate(value);
        return new(value, errors);
    }

    public static string Serialize(StreamerBotDefinition value) =>
        JsonSerializer.Serialize(value, WriteOptions) + "\n";

    public static string[] Validate(StreamerBotDefinition value)
    {
        var errors = new List<string>();
        if (value.SchemaVersion is not (1 or 2 or 3 or StreamerBotDefinition.CurrentSchemaVersion))
        {
            errors.Add($"Schema {value.SchemaVersion} is unsupported.");
        }

        if (value.Queues is null || value.Commands is null || value.Actions is null || value.Resources is null)
        {
            return ["queues, commands, actions, and resources must be JSON arrays."];
        }

        ValidateUnique(value.Queues.Select(item => item.Id), "queue", errors);
        ValidateUnique(value.Commands.Select(item => item.Id), "command", errors);
        ValidateUnique(value.Actions.Select(item => item.Id), "action", errors);
        ValidateUnique(value.Resources.Select(item => item.Id), "resource", errors);
        var queues = value.Queues.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var commands = value.Commands.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var queue in value.Queues)
        {
            if (string.IsNullOrWhiteSpace(queue.Name))
            {
                errors.Add($"Queue '{queue.Id}' requires a name.");
            }
        }

        foreach (var command in value.Commands)
        {
            if (string.IsNullOrWhiteSpace(command.Name) ||
                command.Commands is null ||
                command.Commands.Count == 0 ||
                command.Commands.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Command '{command.Id}' requires a name and at least one command.");
            }

            if (command.GlobalCooldown < 0 || command.UserCooldown < 0)
            {
                errors.Add($"Command '{command.Id}' cooldowns cannot be negative.");
            }
        }

        foreach (var action in value.Actions)
        {
            if (string.IsNullOrWhiteSpace(action.Id) || string.IsNullOrWhiteSpace(action.Name))
            {
                errors.Add("Every action requires an id and name.");
            }

            if (action.QueueId is not null && !queues.Contains(action.QueueId))
            {
                errors.Add($"Action '{action.Id}' references missing queue '{action.QueueId}'.");
            }

            if (action.Triggers is null || action.SubActions is null)
            {
                errors.Add($"Action '{action.Id}' requires triggers and subActions arrays.");
                continue;
            }

            ValidateUnique(
                action.Triggers.Select(item => item.Id),
                $"trigger in action '{action.Id}'",
                errors);
            ValidateUnique(
                action.SubActions.Select(item => item.Id),
                $"sub-action in action '{action.Id}'",
                errors);
            foreach (var trigger in action.Triggers)
            {
                if (trigger.Kind is not ("command" or "test" or "opaque"))
                {
                    errors.Add($"Trigger '{trigger.Id}' has unsupported kind '{trigger.Kind}'.");
                }
                else if (trigger.Kind == "command" &&
                         (trigger.CommandId is null || !commands.Contains(trigger.CommandId)))
                {
                    errors.Add($"Trigger '{trigger.Id}' references a missing command.");
                }
                else if (trigger.Kind == "opaque" &&
                         (!trigger.ReadOnly || string.IsNullOrWhiteSpace(trigger.PreservationKey)))
                {
                    errors.Add($"Opaque trigger '{trigger.Id}' must be read-only and reference preserved source data.");
                }
            }

            foreach (var subAction in action.SubActions)
            {
                if (subAction.Kind is not ("setArgument" or "executeBridge" or
                        "executeCSharp" or "opaque"))
                {
                    errors.Add($"Sub-action '{subAction.Id}' has unsupported kind '{subAction.Kind}'.");
                }
                else if (subAction.Kind == "setArgument" &&
                         string.IsNullOrWhiteSpace(subAction.VariableName))
                {
                    errors.Add($"Set Argument '{subAction.Id}' requires variableName.");
                }
                else if (subAction.Kind == "executeCSharp" &&
                         !IsSafeCSharpPath(subAction.SourcePath))
                {
                    errors.Add($"Execute C# '{subAction.Id}' requires a safe project-relative .cs sourcePath.");
                }
                else if (subAction.Kind == "opaque" &&
                         (!subAction.ReadOnly || string.IsNullOrWhiteSpace(subAction.PreservationKey)))
                {
                    errors.Add($"Opaque sub-action '{subAction.Id}' must be read-only and reference preserved source data.");
                }
            }
        }

        errors.AddRange(StreamerBotDefinitionDiagnostics.Analyze(value)
            .Where(item => item.Severity == StreamerBotDefinitionDiagnosticSeverity.Error)
            .Select(item => $"{item.Code}: {item.Message}"));

        return [.. errors];
    }

    private static void ValidateUnique(
        IEnumerable<string> values,
        string kind,
        List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                errors.Add($"Every {kind} id must be non-empty and unique.");
            }
        }
    }

    private static bool IsSafeCSharpPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase) &&
        !path.Replace('\\', '/').Split('/').Contains("..", StringComparer.Ordinal);
}
