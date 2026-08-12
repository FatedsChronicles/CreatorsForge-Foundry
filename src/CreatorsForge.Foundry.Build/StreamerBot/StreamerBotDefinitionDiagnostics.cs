using CreatorsForge.Foundry.Core.Compatibility;
using System.Text.RegularExpressions;

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
    private static readonly StreamerBotOperationCatalogueService OperationCatalogue =
        StreamerBotOperationCatalogueService.LoadEmbedded();

    public static IReadOnlyList<StreamerBotDefinitionDiagnostic> Analyze(
        StreamerBotDefinition definition,
        string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var diagnostics = new List<StreamerBotDefinitionDiagnostic>();
        var compatibility = StreamerBotFeatureCompatibilityRegistry.Resolve(profile);
        AnalyzeActions(definition, compatibility, diagnostics);
        AnalyzeCatalogueMappings(definition, profile, diagnostics);
        AnalyzeCommands(definition, diagnostics);
        AnalyzeQueues(definition, diagnostics);
        AnalyzeResources(definition, diagnostics);
        return diagnostics;
    }

    private static void AnalyzeCatalogueMappings(
        StreamerBotDefinition definition,
        string? profile,
        List<StreamerBotDefinitionDiagnostic> diagnostics)
    {
        for (var actionIndex = 0; actionIndex < definition.Actions.Count; actionIndex++)
        {
            var action = definition.Actions[actionIndex];
            for (var index = 0; index < action.Triggers.Count; index++)
                AnalyzeOperation("trigger", action.Triggers[index].Kind, action.Triggers[index].SourceType,
                    action.Triggers[index].ReadOnly, profile, $"$.actions[{actionIndex}].triggers[{index}]",
                    action.Triggers[index].Id, diagnostics);
            for (var index = 0; index < action.SubActions.Count; index++)
            {
                var item = action.SubActions[index];
                if (item.Kind is "executeBridge" or "executeCSharp") continue;
                AnalyzeOperation("subAction", item.Kind, item.SourceType, item.ReadOnly, profile,
                    $"$.actions[{actionIndex}].subActions[{index}]", item.Id, diagnostics);
            }
        }
    }

    private static void AnalyzeOperation(
        string entityKind,
        string modelKind,
        int? sourceType,
        bool readOnly,
        string? profile,
        string path,
        string entityId,
        List<StreamerBotDefinitionDiagnostic> diagnostics)
    {
        if (readOnly) return;
        var operation = OperationCatalogue.Find(entityKind, modelKind);
        if (operation is null)
        {
            diagnostics.Add(new("SBD1010", StreamerBotDefinitionDiagnosticSeverity.Error,
                $"Editable {entityKind} kind '{modelKind}' has no verified operation-catalogue mapping.", path, entityId));
            return;
        }
        if (profile is not null && !operation.Profiles.Contains(profile, StringComparer.Ordinal))
            diagnostics.Add(new("SBD1010", StreamerBotDefinitionDiagnosticSeverity.Error,
                $"'{operation.Name}' is not verified for profile '{profile}'.", path, entityId));
        if (sourceType is not null && sourceType != operation.NativeType)
            diagnostics.Add(new("SBD1011", StreamerBotDefinitionDiagnosticSeverity.Error,
                $"'{operation.Name}' declares native type {sourceType}, but catalogue {OperationCatalogue.Catalogue.Revision} requires {operation.NativeType}.",
                path, entityId));
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
                if (subAction.Kind == "executeCSharp" && subAction.DetachedFromGenerator)
                    diagnostics.Add(new("SBD2009", StreamerBotDefinitionDiagnosticSeverity.Warning,
                        $"Execute C# '{subAction.Id}' was edited after generation. Foundry will preserve the manual source and never overwrite it automatically.",
                        subPath, subAction.Id));
                if (subAction.Kind == "executeCSharp" && (subAction.References ?? []).Any(reference =>
                        Path.IsPathFullyQualified(reference) || reference.StartsWith("\\\\", StringComparison.Ordinal)))
                    diagnostics.Add(new("SBD1019", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Execute C# '{subAction.Id}' contains an absolute compiler reference. Remove it or replace it with a portable project-relative dependency before export.",
                        subPath, subAction.Id));
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

    private static void AnalyzeResources(
        StreamerBotDefinition definition,
        List<StreamerBotDefinitionDiagnostic> diagnostics)
    {
        var entities = BuildEntityIndex(definition);
        for (var index = 0; index < definition.Resources.Count; index++)
        {
            var resource = definition.Resources[index];
            var path = $"$.resources[{index}]";
            if (string.IsNullOrWhiteSpace(resource.Name) ||
                !StreamerBotResourceTypes.All.Contains(resource.Type, StringComparer.Ordinal) ||
                !StreamerBotResourcePortability.All.Contains(resource.Portability, StringComparer.Ordinal))
                diagnostics.Add(new("SBD1012", StreamerBotDefinitionDiagnosticSeverity.Error,
                    $"Resource '{resource.Id}' requires a name, supported type, and portability classification.",
                    path, resource.Id));

            if (!string.IsNullOrWhiteSpace(resource.ValidationPattern))
            {
                try { _ = new Regex(resource.ValidationPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
                catch (ArgumentException)
                {
                    diagnostics.Add(new("SBD1014", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Resource '{resource.Name}' has an invalid validation pattern.", path, resource.Id));
                }
            }

            if (resource.Type == "url" && resource.SuggestedValue is { Length: > 0 } url &&
                (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
                diagnostics.Add(new("SBD1017", StreamerBotDefinitionDiagnosticSeverity.Error,
                    $"URL resource '{resource.Name}' requires an absolute HTTP or HTTPS value.", path, resource.Id));

            if (StreamerBotPortabilityService.IsCredentialLike(resource))
                diagnostics.Add(new("SBD1015", StreamerBotDefinitionDiagnosticSeverity.Error,
                    $"Resource '{resource.Name}' appears to contain a credential. Remove the value; secrets must never be stored in Foundry projects or exports.",
                    path, resource.Id));

            var absolutePath = StreamerBotPortabilityService.IsAbsoluteMachinePath(resource);
            if (absolutePath && resource.Portability == StreamerBotResourcePortability.Portable)
                diagnostics.Add(new("SBD1016", StreamerBotDefinitionDiagnosticSeverity.Error,
                    $"Resource '{resource.Name}' uses an absolute machine path but is marked fully portable.", path, resource.Id));
            else if (absolutePath)
                diagnostics.Add(new("SBD2008", StreamerBotDefinitionDiagnosticSeverity.Warning,
                    $"Resource '{resource.Name}' uses an absolute machine path and must be reviewed on the destination system.", path, resource.Id));

            var bindings = resource.Bindings ?? [];
            if (bindings.Count == 0)
                diagnostics.Add(new("SBD2006", StreamerBotDefinitionDiagnosticSeverity.Warning,
                    $"Resource '{resource.Name}' is not used by an entity.", path, resource.Id));
            if (resource.Portability is StreamerBotResourcePortability.ReconnectByName or
                StreamerBotResourcePortability.ConfirmAfterImport or
                StreamerBotResourcePortability.ManualConfiguration)
                diagnostics.Add(new("SBD2007", StreamerBotDefinitionDiagnosticSeverity.Warning,
                    $"Resource '{resource.Name}' requires {DescribePortability(resource.Portability)}.", path, resource.Id));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                var key = $"{binding.EntityType}\u001f{binding.EntityId}\u001f{binding.Property}";
                if (!entities.Contains($"{binding.EntityType}\u001f{binding.EntityId}") ||
                    string.IsNullOrWhiteSpace(binding.Property) || !seen.Add(key))
                    diagnostics.Add(new("SBD1013", StreamerBotDefinitionDiagnosticSeverity.Error,
                        $"Resource '{resource.Name}' has a missing, duplicated, or invalid entity binding.",
                        $"{path}.bindings[{bindingIndex}]", resource.Id));
            }
        }
    }

    private static HashSet<string> BuildEntityIndex(StreamerBotDefinition definition)
    {
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var queue in definition.Queues) entities.Add($"queue\u001f{queue.Id}");
        foreach (var command in definition.Commands) entities.Add($"command\u001f{command.Id}");
        foreach (var action in definition.Actions)
        {
            entities.Add($"action\u001f{action.Id}");
            foreach (var trigger in action.Triggers) entities.Add($"trigger\u001f{trigger.Id}");
            foreach (var subAction in action.SubActions) entities.Add($"subAction\u001f{subAction.Id}");
        }
        return entities;
    }

    private static string DescribePortability(string portability) => portability switch
    {
        StreamerBotResourcePortability.ReconnectByName => "reconnection by name after import",
        StreamerBotResourcePortability.ConfirmAfterImport => "confirmation after import",
        _ => "manual configuration after import",
    };
}
