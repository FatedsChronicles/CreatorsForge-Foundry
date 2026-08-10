using CreatorsForge.Foundry.Core.Compatibility;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public enum StreamerBotDefinitionDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record StreamerBotDefinitionDiagnostic(
    string Code,
    StreamerBotDefinitionDiagnosticSeverity Severity,
    string Message,
    string Path,
    string? EntityId = null);

public sealed record StreamerBotFeatureCompatibility(
    string Profile,
    bool ActionToggles,
    bool WeightedRandomActions,
    bool RichCommandOptions);

/// <summary>
/// Central policy for Streamer.bot features whose wire fields have been
/// verified in retained representative exports. New profiles are added here,
/// rather than being inferred in the desktop editor.
/// </summary>
public static class StreamerBotFeatureCompatibilityRegistry
{
    private static readonly Dictionary<string, StreamerBotFeatureCompatibility> Profiles =
        FoundryStreamerBotProfiles.Ordered.ToDictionary(
            profile => profile,
            profile => new StreamerBotFeatureCompatibility(
                profile,
                ActionToggles: true,
                WeightedRandomActions: true,
                RichCommandOptions: true),
            StringComparer.Ordinal);

    public static StreamerBotFeatureCompatibility Resolve(string? profile)
    {
        if (profile is null)
            return new("definition-only", true, true, true);
        return Profiles.TryGetValue(profile, out var compatibility)
            ? compatibility
            : new(profile, false, false, false);
    }
}

public static class StreamerBotDefinitionDiagnostics
{
    private const double MaximumWeight = 1_000_000;

    public static IReadOnlyList<StreamerBotDefinitionDiagnostic> Analyze(
        StreamerBotDefinition definition,
        string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var diagnostics = new List<StreamerBotDefinitionDiagnostic>();
        var compatibility = StreamerBotFeatureCompatibilityRegistry.Resolve(profile);
        AnalyzeActions(definition, compatibility, diagnostics);
        AnalyzeCommands(definition, diagnostics);
        AnalyzeQueues(definition, diagnostics);
        return diagnostics;
    }

    private static void AnalyzeActions(
        StreamerBotDefinition definition,
        StreamerBotFeatureCompatibility compatibility,
        List<StreamerBotDefinitionDiagnostic> diagnostics)
    {
        foreach (var duplicate in definition.Actions
                     .Where(item => !item.ReadOnly)
                     .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Key.Length > 0 && group.Count() > 1))
        {
            diagnostics.Add(new("SBD2001", StreamerBotDefinitionDiagnosticSeverity.Warning,
                $"Multiple actions use the display name '{duplicate.Key}'. Stable IDs remain safe, but name-based calls may be ambiguous.",
                "$.actions", duplicate.First().Id));
        }

        for (var actionIndex = 0; actionIndex < definition.Actions.Count; actionIndex++)
        {
            var action = definition.Actions[actionIndex];
            var path = $"$.actions[{actionIndex}]";
            if (!action.ReadOnly && action.Triggers.Count == 0)
                diagnostics.Add(new("SBD2002", StreamerBotDefinitionDiagnosticSeverity.Warning,
                    $"Action '{action.Name}' has no trigger. This is valid when another action or C# invokes it.", path, action.Id));

            if ((action.RandomAction || action.ExcludeFromPending || action.ExcludeFromHistory) &&
                !compatibility.ActionToggles)
                diagnostics.Add(new("SBD1001", StreamerBotDefinitionDiagnosticSeverity.Error,
                    $"Action toggles have not been verified for profile '{compatibility.Profile}'.", path, action.Id));

            for (var subActionIndex = 0; subActionIndex < action.SubActions.Count; subActionIndex++)
            {
                var subAction = action.SubActions[subActionIndex];
                var subPath = $"{path}.subActions[{subActionIndex}]";
                if (!double.IsFinite(subAction.Weight) || subAction.Weight < 0 || subAction.Weight > MaximumWeight)
                    diagnostics.Add(new("SBD1002", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Sub-action '{subAction.Id}' weight must be between 0 and {MaximumWeight:N0}.", subPath, subAction.Id));
                else if (action.RandomAction && subAction.Enabled && !subAction.ReadOnly && subAction.Weight <= 0)
                    diagnostics.Add(new("SBD1003", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Enabled sub-action '{subAction.Id}' requires a positive weight while Random Action is enabled.", subPath, subAction.Id));
                else if (subAction.Weight > 0 && !compatibility.WeightedRandomActions)
                    diagnostics.Add(new("SBD1004", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Weighted sub-actions have not been verified for profile '{compatibility.Profile}'.", subPath, subAction.Id));
            }

            if (action.Concurrent)
            {
                var produced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var subAction in action.SubActions.Where(item => item.Enabled && !item.ReadOnly))
                {
                    if (subAction.Value is { Length: > 0 } value &&
                        produced.Any(argument => value.Contains($"%{argument}%", StringComparison.OrdinalIgnoreCase)))
                        diagnostics.Add(new("SBD2003", StreamerBotDefinitionDiagnosticSeverity.Warning,
                            $"Concurrent action '{action.Name}' contains an order-dependent argument expression; execution order is not deterministic.",
                            path, action.Id));
                    if (subAction.Kind == "setArgument" && subAction.VariableName is { Length: > 0 } variable)
                        produced.Add(variable);
                }
            }
        }
    }

    private static void AnalyzeCommands(
        StreamerBotDefinition definition,
        List<StreamerBotDefinitionDiagnostic> diagnostics)
    {
        var aliases = new Dictionary<string, StreamerBotCommand>(StringComparer.OrdinalIgnoreCase);
        var linkedCommands = definition.Actions.SelectMany(action => action.Triggers)
            .Where(trigger => trigger.Kind == "command" && trigger.CommandId is not null)
            .Select(trigger => trigger.CommandId!)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < definition.Commands.Count; index++)
        {
            var command = definition.Commands[index];
            if (command.ReadOnly) continue;
            var path = $"$.commands[{index}]";
            if (command.Sources <= 0)
                diagnostics.Add(new("SBD1005", StreamerBotDefinitionDiagnosticSeverity.Error,
                    $"Command '{command.Name}' must enable at least one source.", path, command.Id));
            foreach (var alias in command.Commands.Select(value => value.Trim()))
            {
                if (!alias.StartsWith('!'))
                    diagnostics.Add(new("SBD1006", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Command alias '{alias}' must begin with '!'.", path, command.Id));
                if (aliases.TryGetValue(alias, out var existing))
                    diagnostics.Add(new("SBD1007", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Command alias '{alias}' conflicts with command '{existing.Name}'.", path, command.Id));
                else aliases[alias] = command;
            }
            if (!linkedCommands.Contains(command.Id))
                diagnostics.Add(new("SBD2004", StreamerBotDefinitionDiagnosticSeverity.Warning,
                    $"Command '{command.Name}' has no linked command trigger.", path, command.Id));
        }
    }

    private static void AnalyzeQueues(
        StreamerBotDefinition definition,
        List<StreamerBotDefinitionDiagnostic> diagnostics)
    {
        var usedQueues = definition.Actions.Where(action => action.QueueId is not null)
            .Select(action => action.QueueId!).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < definition.Queues.Count; index++)
        {
            var queue = definition.Queues[index];
            if (!queue.ReadOnly && !usedQueues.Contains(queue.Id))
                diagnostics.Add(new("SBD2005", StreamerBotDefinitionDiagnosticSeverity.Warning,
                    $"Queue '{queue.Name}' is not assigned to an action.", $"$.queues[{index}]", queue.Id));
        }
    }
}
