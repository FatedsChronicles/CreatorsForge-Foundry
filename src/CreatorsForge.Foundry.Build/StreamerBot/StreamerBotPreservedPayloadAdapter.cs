using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CreatorsForge.Foundry.Build.StreamerBot;

/// <summary>
/// Re-exports an imported payload by patching supported values into a clone of
/// the preserved wire document. Imported code is read as text and is never run.
/// </summary>
public static class StreamerBotPreservedPayloadAdapter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static async Task<StreamerBotExportArtifact> EncodeAsync(
        StreamerBotDefinition definition,
        string projectRoot,
        string projectId,
        string projectName,
        string projectVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var import = definition.Import ?? throw new InvalidOperationException("The definition has no import provenance.");
        if (import.Adapter != "streamerbot-preserved-v1" || import.PayloadVersion is not (23 or 24))
            throw new InvalidOperationException("Only verified v23 and v24 preserved payloads can be re-exported.");

        var preservationPath = ResolveConfined(projectRoot, import.PreservationFile, ".json");
        var preservationText = await File.ReadAllTextAsync(preservationPath, cancellationToken).ConfigureAwait(false);
        var preservation = JsonNode.Parse(preservationText, documentOptions: new() { MaxDepth = 128 }) as JsonObject ??
            throw new InvalidDataException("The import preservation sidecar is not a JSON object.");
        var payload = preservation["payload"]?.DeepClone() as JsonObject ??
            throw new InvalidDataException("The preservation sidecar does not contain a payload.");
        if (payload["version"]?.GetValue<int>() != import.PayloadVersion ||
            payload["exportedFrom"]?.GetValue<string>() != import.ExportedFrom)
            throw new InvalidDataException("The preservation sidecar does not match the imported payload provenance.");

        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToJsonString())));
        if (!string.Equals(sourceHash, import.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The preserved payload has changed since import. Analyze the original package again.");

        RejectUnsafeReferences(definition);
        PatchMetadata(payload, definition, projectName, projectVersion);
        var data = payload["data"]!.AsObject();
        PatchQueues(data["queues"]!.AsArray(), definition.Queues, projectId);
        PatchCommands(data["commands"]!.AsArray(), definition.Commands, projectId);
        await PatchActionsAsync(data["actions"]!.AsArray(), definition, projectRoot, projectId, cancellationToken).ConfigureAwait(false);
        if (ContainsAbsoluteMachinePath(payload))
            throw new InvalidOperationException("The preserved import contains an absolute machine path. Remove, map, or explicitly resolve it before export.");

        var payloadJson = payload.ToJsonString(Indented) + "\n";
        var importCode = StreamerBotEnvelopeCodec.Encode(payload);
        if (!JsonNode.DeepEquals(payload, StreamerBotEnvelopeCodec.Decode(importCode)))
            throw new InvalidDataException("The preserved package failed structural round-trip verification.");
        return new(importCode, payloadJson, new(
            1, import.Adapter, import.PayloadVersion, import.ExportedFrom, projectId, projectVersion,
            definition.Actions.Count, definition.Commands.Count, definition.Queues.Count,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson))), true));
    }

    private static void PatchMetadata(JsonObject payload, StreamerBotDefinition definition, string name, string version)
    {
        var meta = payload["meta"] as JsonObject ?? new JsonObject();
        payload["meta"] = meta;
        meta["name"] = name;
        meta["version"] = version;
        meta["author"] = definition.Metadata.Author;
        meta["description"] = definition.Metadata.Description;
        payload["minimumVersion"] = definition.Metadata.MinimumVersion;
    }

    private static void PatchQueues(JsonArray wireItems, IReadOnlyList<StreamerBotQueueDefinition> items, string projectId)
    {
        var wire = ById(wireItems);
        foreach (var item in items.Where(item => !item.ReadOnly))
        {
            var id = item.SourceId ?? DeterministicStreamerBotId.Create(projectId, "queue", item.Id);
            if (!wire.TryGetValue(id, out var target))
            {
                target = new JsonObject { ["id"] = id };
                wireItems.Add(target);
                wire[id] = target;
            }
            target["name"] = item.Name;
            target["blocking"] = item.Blocking;
        }
    }

    private static void PatchCommands(JsonArray wireItems, IReadOnlyList<StreamerBotCommand> items, string projectId)
    {
        var wire = ById(wireItems);
        foreach (var item in items.Where(item => !item.ReadOnly))
        {
            var id = item.SourceId ?? DeterministicStreamerBotId.Create(projectId, "command", item.Id);
            if (!wire.TryGetValue(id, out var target))
            {
                target = new JsonObject { ["id"] = id };
                wireItems.Add(target);
                wire[id] = target;
            }
            target["name"] = item.Name;
            target["command"] = string.Join("\n", item.Commands);
            target["enabled"] = item.Enabled;
            target["caseSensitive"] = item.CaseSensitive;
            target["globalCooldown"] = item.GlobalCooldown;
            target["userCooldown"] = item.UserCooldown;
            target["ignoreBotAccount"] = item.IgnoreBotAccount;
            if (target.ContainsKey("ignoreInternal"))
                target["ignoreInternal"] = item.IgnoreInternalMessages;
            target["sources"] = item.Sources;
        }
    }

    private static async Task PatchActionsAsync(JsonArray wireItems, StreamerBotDefinition definition,
        string projectRoot, string projectId, CancellationToken cancellationToken)
    {
        var wire = ById(wireItems);
        var queuesByLogicalId = definition.Queues.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var commandsByLogicalId = definition.Commands.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var action in definition.Actions.Where(item => !item.ReadOnly))
        {
            var actionWireId = action.SourceId ?? DeterministicStreamerBotId.Create(projectId, "action", action.Id);
            if (!wire.TryGetValue(actionWireId, out var target))
            {
                target = new JsonObject
                {
                    ["id"] = actionWireId,
                    ["triggers"] = new JsonArray(),
                    ["subActions"] = new JsonArray(),
                };
                wireItems.Add(target);
                wire[actionWireId] = target;
            }
            target["name"] = action.Name;
            target["enabled"] = action.Enabled;
            target["concurrent"] = action.Concurrent;
            target["alwaysRun"] = action.AlwaysRun;
            target["group"] = action.Group ?? string.Empty;
            target["randomAction"] = action.RandomAction;
            target["excludeFromPending"] = action.ExcludeFromPending;
            target["excludeFromHistory"] = action.ExcludeFromHistory;
            target["queue"] = action.QueueId is not null && queuesByLogicalId.TryGetValue(action.QueueId, out var queue)
                ? queue.SourceId ?? DeterministicStreamerBotId.Create(projectId, "queue", queue.Id) : null;

            var triggerWire = ById(target["triggers"] as JsonArray ?? []);
            var triggerArray = target["triggers"] as JsonArray ?? [];
            target["triggers"] = triggerArray;
            foreach (var trigger in action.Triggers.Where(item => !item.ReadOnly))
            {
                var triggerWireId = trigger.SourceId ?? DeterministicStreamerBotId.Create(projectId, $"trigger:{action.Id}", trigger.Id);
                if (!triggerWire.TryGetValue(triggerWireId, out var triggerTarget))
                {
                    triggerTarget = new JsonObject { ["id"] = triggerWireId, ["type"] = trigger.Kind == "command" ? 401 : 702 };
                    triggerArray.Add(triggerTarget);
                    triggerWire[triggerWireId] = triggerTarget;
                }
                triggerTarget["enabled"] = trigger.Enabled;
                if (trigger.Kind == "command" && trigger.CommandId is not null &&
                    commandsByLogicalId.TryGetValue(trigger.CommandId, out var command))
                    triggerTarget["commandId"] = command.SourceId ?? DeterministicStreamerBotId.Create(projectId, "command", command.Id);
            }

            var subActionWire = ById(target["subActions"] as JsonArray ?? []);
            var subActionArray = target["subActions"] as JsonArray ?? [];
            target["subActions"] = subActionArray;
            foreach (var subAction in action.SubActions.Where(item => !item.ReadOnly))
            {
                var subWireId = subAction.SourceId ?? DeterministicStreamerBotId.Create(projectId, $"subAction:{action.Id}", subAction.Id);
                if (!subActionWire.TryGetValue(subWireId, out var subTarget))
                {
                    if (subAction.Kind == "executeBridge")
                        throw new InvalidOperationException("Imported package-only projects cannot add Execute Bridge sub-actions because they do not contain a managed bridge.");
                    subTarget = new JsonObject
                    {
                        ["id"] = subWireId,
                        ["type"] = subAction.Kind == "setArgument" ? 123 : 99999,
                        ["references"] = subAction.Kind == "executeCSharp"
                            ? new JsonArray((subAction.References ?? [])
                                .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
                            : null,
                    };
                    subActionArray.Add(subTarget);
                    subActionWire[subWireId] = subTarget;
                }
                subTarget["enabled"] = subAction.Enabled;
                subTarget["weight"] = subAction.Weight;
                if (subAction.Kind == "setArgument")
                {
                    subTarget["type"] = 123;
                    subTarget["variableName"] = subAction.VariableName;
                    subTarget["value"] = subAction.Value;
                    subTarget["autoType"] = subAction.AutoType;
                    subTarget.Remove("byteCode");
                    subTarget.Remove("references");
                }
                else if (subAction.Kind == "executeCSharp")
                {
                    subTarget["type"] = 99999;
                    subTarget.Remove("variableName");
                    subTarget.Remove("value");
                    subTarget.Remove("autoType");
                    subTarget["references"] = new JsonArray((subAction.References ?? [])
                        .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                    var sourcePath = ResolveConfined(projectRoot, subAction.SourcePath!, ".cs");
                    var source = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                    if (Encoding.UTF8.GetByteCount(source) > 1024 * 1024)
                        throw new InvalidDataException($"Execute C# source '{subAction.SourcePath}' exceeds 1 MiB.");
                    subTarget["byteCode"] = Convert.ToBase64String(new UTF8Encoding(false, true).GetBytes(source));
                }
            }

            var orderedSubActions = new JsonArray();
            var usedSubActionIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < action.SubActions.Count; index++)
            {
                var subAction = action.SubActions[index];
                var id = subAction.SourceId ?? DeterministicStreamerBotId.Create(projectId, $"subAction:{action.Id}", subAction.Id);
                if (!subActionWire.TryGetValue(id, out var orderedTarget)) continue;
                var clone = orderedTarget.DeepClone().AsObject();
                clone["index"] = index;
                orderedSubActions.Add(clone);
                usedSubActionIds.Add(id);
            }
            foreach (var leftover in subActionArray.OfType<JsonObject>())
            {
                var id = leftover["id"]?.GetValue<string>();
                if (id is null || !usedSubActionIds.Contains(id)) orderedSubActions.Add(leftover.DeepClone());
            }
            target["subActions"] = orderedSubActions;
        }

        var orderedActions = new JsonArray();
        var usedActionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in definition.Actions)
        {
            var id = action.SourceId ?? DeterministicStreamerBotId.Create(projectId, "action", action.Id);
            if (!wire.TryGetValue(id, out var target)) continue;
            orderedActions.Add(target.DeepClone());
            usedActionIds.Add(id);
        }
        foreach (var leftover in wireItems.OfType<JsonObject>())
        {
            var id = leftover["id"]?.GetValue<string>();
            if (id is null || !usedActionIds.Contains(id)) orderedActions.Add(leftover.DeepClone());
        }
        wireItems.Clear();
        foreach (var item in orderedActions) wireItems.Add(item?.DeepClone());
    }

    private static Dictionary<string, JsonObject> ById(JsonArray items) => items
        .OfType<JsonObject>()
        .Where(item => item["id"] is JsonValue)
        .GroupBy(item => item["id"]!.GetValue<string>(), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private static void RejectUnsafeReferences(StreamerBotDefinition definition)
    {
        foreach (var reference in definition.Actions.SelectMany(action => action.SubActions)
                     .SelectMany(item => item.References ?? []))
        {
            if (Path.IsPathFullyQualified(reference) || reference.StartsWith("\\\\", StringComparison.Ordinal))
                throw new InvalidOperationException("Absolute compiler-reference paths must be removed, mapped, or explicitly resolved before export.");
        }
    }

    private static bool ContainsAbsoluteMachinePath(JsonNode? node)
    {
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            return Path.IsPathFullyQualified(text) || text.StartsWith("\\\\", StringComparison.Ordinal);
        if (node is JsonArray array) return array.Any(ContainsAbsoluteMachinePath);
        if (node is JsonObject value) return value.Any(property => ContainsAbsoluteMachinePath(property.Value));
        return false;
    }

    private static string ResolveConfined(string root, string relativePath, string extension)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("A required project-relative path is missing.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An imported path leaves the project or has an unexpected file type.");
        return path;
    }
}
