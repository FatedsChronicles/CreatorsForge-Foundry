using System.Text.Json;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public sealed record StreamerBotDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public StreamerBotMetadata Metadata { get; init; } = new();
    public IReadOnlyList<StreamerBotQueueDefinition> Queues { get; init; } = [];
    public IReadOnlyList<StreamerBotCommand> Commands { get; init; } = [];
    public IReadOnlyList<StreamerBotAction> Actions { get; init; } = [];
}

public sealed record StreamerBotMetadata
{
    public string Author { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed record StreamerBotQueueDefinition(string Id, string Name, bool Blocking);

public sealed record StreamerBotCommand(
    string Id,
    string Name,
    IReadOnlyList<string> Commands,
    bool Enabled,
    bool CaseSensitive,
    int GlobalCooldown,
    int UserCooldown);

public sealed record StreamerBotAction(
    string Id,
    string Name,
    bool Enabled,
    string? QueueId,
    bool Concurrent,
    bool AlwaysRun,
    IReadOnlyList<StreamerBotTrigger> Triggers,
    IReadOnlyList<StreamerBotSubAction> SubActions);

public sealed record StreamerBotTrigger(
    string Id,
    string Kind,
    bool Enabled,
    string? CommandId);

public sealed record StreamerBotSubAction(
    string Id,
    string Kind,
    bool Enabled,
    string? VariableName,
    string? Value,
    bool AutoType);

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

        var errors = Validate(value);
        return new(value, errors);
    }

    public static string Serialize(StreamerBotDefinition value) =>
        JsonSerializer.Serialize(value, WriteOptions) + "\n";

    public static string[] Validate(StreamerBotDefinition value)
    {
        var errors = new List<string>();
        if (value.SchemaVersion != 1)
        {
            errors.Add($"Schema {value.SchemaVersion} is unsupported.");
        }

        if (value.Queues is null || value.Commands is null || value.Actions is null)
        {
            return ["queues, commands, and actions must be JSON arrays."];
        }

        ValidateUnique(value.Queues.Select(item => item.Id), "queue", errors);
        ValidateUnique(value.Commands.Select(item => item.Id), "command", errors);
        ValidateUnique(value.Actions.Select(item => item.Id), "action", errors);
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
                if (trigger.Kind is not ("command" or "test"))
                {
                    errors.Add($"Trigger '{trigger.Id}' has unsupported kind '{trigger.Kind}'.");
                }
                else if (trigger.Kind == "command" &&
                         (trigger.CommandId is null || !commands.Contains(trigger.CommandId)))
                {
                    errors.Add($"Trigger '{trigger.Id}' references a missing command.");
                }
            }

            foreach (var subAction in action.SubActions)
            {
                if (subAction.Kind is not ("setArgument" or "executeBridge"))
                {
                    errors.Add($"Sub-action '{subAction.Id}' has unsupported kind '{subAction.Kind}'.");
                }
                else if (subAction.Kind == "setArgument" &&
                         string.IsNullOrWhiteSpace(subAction.VariableName))
                {
                    errors.Add($"Set Argument '{subAction.Id}' requires variableName.");
                }
            }
        }

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
}
