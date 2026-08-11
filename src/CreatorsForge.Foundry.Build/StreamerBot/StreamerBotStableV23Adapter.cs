using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public sealed record StreamerBotExportArtifact(
    string ImportCode,
    string PayloadJson,
    StreamerBotExportReport Report);

public sealed record StreamerBotExportReport(
    int SchemaVersion,
    string Adapter,
    int PayloadVersion,
    string ExportedFrom,
    string ProjectId,
    string ProjectVersion,
    int ActionCount,
    int CommandCount,
    int QueueCount,
    string PayloadSha256,
    bool RoundTripVerified);

public static class DeterministicStreamerBotId
{
    public static string Create(string projectId, string kind, string logicalId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalId);

        var input = Encoding.UTF8.GetBytes($"{projectId}\n{kind}\n{logicalId}");
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        Span<byte> bytes = hash[..16];

        // Mark the value as a name-derived UUID while retaining 122 hash bits.
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString("D");
    }
}

public static class StreamerBotStableV23Adapter
{
    public const int PayloadVersion = 23;
    public const string ExportedFrom = "1.0.4";
    public const string AdapterName = "streamerbot-stable-v23";

    private static readonly byte[] Magic = "SBAE"u8.ToArray();
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
    };
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static StreamerBotExportArtifact Encode(
        StreamerBotDefinition definition,
        string projectId,
        string projectName,
        string projectVersion,
        string bridgeSource)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectVersion);
        ArgumentNullException.ThrowIfNull(bridgeSource);

        var validationErrors = StreamerBotDefinitionLoader.Validate(definition);
        if (validationErrors.Length > 0)
        {
            throw new InvalidOperationException(
                $"The Streamer.bot definition is invalid: {string.Join(" ", validationErrors)}");
        }

        var payload = CreatePayload(
            definition,
            projectId,
            projectName,
            projectVersion,
            bridgeSource);
        var payloadJson = payload.ToJsonString(IndentedOptions) + "\n";
        var importCode = EncodeEnvelope(payloadJson);
        var decoded = Decode(importCode);
        if (!JsonNode.DeepEquals(payload, decoded))
        {
            throw new InvalidDataException(
                "The generated Streamer.bot export failed structural round-trip verification.");
        }

        var payloadHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));
        return new(
            importCode,
            payloadJson,
            new(
                1,
                AdapterName,
                PayloadVersion,
                ExportedFrom,
                projectId,
                projectVersion,
                definition.Actions.Count,
                definition.Commands.Count,
                definition.Queues.Count,
                payloadHash,
                RoundTripVerified: true));
    }

    public static JsonObject Decode(string importCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importCode);

        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(importCode.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                "The Streamer.bot import code is not valid Base64.",
                exception);
        }

        if (envelope.Length < Magic.Length ||
            !envelope.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "The Streamer.bot import code does not begin with the SBAE signature.");
        }

        try
        {
            using var compressed = new MemoryStream(
                envelope,
                Magic.Length,
                envelope.Length - Magic.Length,
                writable: false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            if (output.Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException(
                    "The Streamer.bot payload exceeds Foundry's 16 MiB safety limit.");
            }

            var payload = JsonNode.Parse(output.ToArray()) as JsonObject ??
                throw new InvalidDataException(
                    "The Streamer.bot payload root must be a JSON object.");
            if (payload["version"]?.GetValue<int>() != PayloadVersion)
            {
                throw new InvalidDataException(
                    $"The stable-v23 decoder requires payload version {PayloadVersion}.");
            }

            if (payload["data"] is not JsonObject data ||
                data["actions"] is not JsonArray ||
                data["commands"] is not JsonArray ||
                data["queues"] is not JsonArray)
            {
                throw new InvalidDataException(
                    "The stable-v23 payload is missing actions, commands, or queues.");
            }

            return payload;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException)
        {
            throw new InvalidDataException(
                "The SBAE envelope does not contain a valid GZip JSON payload.",
                exception);
        }
    }

    public static StreamerBotDefinition DecodeDefinition(string importCode)
    {
        var payload = Decode(importCode);
        var metadata = payload["meta"] as JsonObject;
        var data = payload["data"]!.AsObject();

        var queues = data["queues"]!.AsArray()
            .Select(item =>
            {
                var value = item?.AsObject() ??
                    throw new InvalidDataException("A queue must be an object.");
                return new StreamerBotQueueDefinition(
                    RequiredString(value, "id"),
                    RequiredString(value, "name"),
                    RequiredBoolean(value, "blocking"));
            })
            .ToArray();
        var commands = data["commands"]!.AsArray()
            .Select(item =>
            {
                var value = item?.AsObject() ??
                    throw new InvalidDataException("A command must be an object.");
                return new StreamerBotCommand(
                    RequiredString(value, "id"),
                    RequiredString(value, "name"),
                    RequiredString(value, "command").Split(
                        ["\r\n", "\n"],
                        StringSplitOptions.RemoveEmptyEntries),
                    RequiredBoolean(value, "enabled"),
                    RequiredBoolean(value, "caseSensitive"),
                    RequiredInteger(value, "globalCooldown"),
                    RequiredInteger(value, "userCooldown"),
                    IgnoreBotAccount: RequiredBoolean(value, "ignoreBotAccount"),
                    IgnoreInternalMessages: RequiredBoolean(value, "ignoreInternal"),
                    Sources: RequiredInteger(value, "sources"));
            })
            .ToArray();
        var actions = data["actions"]!.AsArray()
            .Select(item => DecodeAction(item?.AsObject() ??
                throw new InvalidDataException("An action must be an object.")))
            .ToArray();

        var definition = new StreamerBotDefinition
        {
            Metadata = new()
            {
                Author = metadata?["author"]?.GetValue<string>() ?? string.Empty,
                Description =
                    metadata?["description"]?.GetValue<string>() ?? string.Empty,
                MinimumVersion = payload["minimumVersion"]?.GetValue<string>() ??
                    "1.0.0-alpha.1",
            },
            Queues = queues,
            Commands = commands,
            Actions = actions,
        };
        var errors = StreamerBotDefinitionLoader.Validate(definition);
        if (errors.Length > 0)
        {
            throw new InvalidDataException(
                $"The stable-v23 payload is structurally invalid: {string.Join(" ", errors)}");
        }

        return definition;
    }

    public static string SerializeReport(StreamerBotExportReport report) =>
        JsonSerializer.Serialize(report, ReportOptions) + "\n";

    private static JsonObject CreatePayload(
        StreamerBotDefinition definition,
        string projectId,
        string projectName,
        string projectVersion,
        string bridgeSource)
    {
        var queueIds = definition.Queues.ToDictionary(
            item => item.Id,
            item => DeterministicStreamerBotId.Create(projectId, "queue", item.Id),
            StringComparer.Ordinal);
        var commandIds = definition.Commands.ToDictionary(
            item => item.Id,
            item => DeterministicStreamerBotId.Create(projectId, "command", item.Id),
            StringComparer.Ordinal);

        var actions = new JsonArray();
        foreach (var action in definition.Actions)
        {
            var triggers = new JsonArray();
            foreach (var trigger in action.Triggers)
            {
                triggers.Add(trigger.Kind switch
                {
                    "command" => new JsonObject
                    {
                        ["commandId"] = commandIds[trigger.CommandId!],
                        ["id"] = DeterministicStreamerBotId.Create(
                            projectId,
                            $"action/{action.Id}/trigger",
                            trigger.Id),
                        ["type"] = 401,
                        ["enabled"] = trigger.Enabled,
                        ["exclusions"] = new JsonArray(),
                    },
                    "test" => new JsonObject
                    {
                        ["variables"] = new JsonObject(),
                        ["id"] = DeterministicStreamerBotId.Create(
                            projectId,
                            $"action/{action.Id}/trigger",
                            trigger.Id),
                        ["type"] = 702,
                        ["enabled"] = trigger.Enabled,
                        ["exclusions"] = new JsonArray(),
                    },
                    _ => throw new InvalidOperationException(
                        $"Unsupported trigger kind '{trigger.Kind}'."),
                });
            }

            var subActions = new JsonArray();
            for (var index = 0; index < action.SubActions.Count; index++)
            {
                var subAction = action.SubActions[index];
                var id = DeterministicStreamerBotId.Create(
                    projectId,
                    $"action/{action.Id}/subAction",
                    subAction.Id);
                subActions.Add(subAction.Kind switch
                {
                    "setArgument" => new JsonObject
                    {
                        ["variableName"] = subAction.VariableName,
                        ["value"] = subAction.Value ?? string.Empty,
                        ["autoType"] = subAction.AutoType,
                        ["id"] = id,
                        ["weight"] = subAction.Weight,
                        ["type"] = 123,
                        ["parentId"] = null,
                        ["enabled"] = subAction.Enabled,
                        ["index"] = index,
                    },
                    "executeBridge" => new JsonObject
                    {
                        ["name"] = null,
                        ["description"] = null,
                        ["references"] = new JsonArray(),
                        ["byteCode"] = Convert.ToBase64String(
                            Encoding.UTF8.GetBytes(bridgeSource)),
                        ["precompile"] = false,
                        ["delayStart"] = false,
                        ["saveResultToVariable"] = false,
                        ["saveToVariable"] = null,
                        ["id"] = id,
                        ["weight"] = subAction.Weight,
                        ["type"] = 99999,
                        ["parentId"] = null,
                        ["enabled"] = subAction.Enabled,
                        ["index"] = index,
                    },
                    _ => throw new InvalidOperationException(
                        $"Unsupported sub-action kind '{subAction.Kind}'."),
                });
            }

            actions.Add(new JsonObject
            {
                ["id"] = DeterministicStreamerBotId.Create(
                    projectId,
                    "action",
                    action.Id),
                ["queue"] = action.QueueId is null ? null : queueIds[action.QueueId],
                ["enabled"] = action.Enabled,
                ["excludeFromHistory"] = action.ExcludeFromHistory,
                ["excludeFromPending"] = action.ExcludeFromPending,
                ["name"] = action.Name,
                ["group"] = action.Group ?? string.Empty,
                ["alwaysRun"] = action.AlwaysRun,
                ["randomAction"] = action.RandomAction,
                ["concurrent"] = action.Concurrent,
                ["triggers"] = triggers,
                ["subActions"] = subActions,
                ["collapsedGroups"] = new JsonArray(),
            });
        }

        var queues = new JsonArray();
        foreach (var queue in definition.Queues)
        {
            queues.Add(new JsonObject
            {
                ["id"] = queueIds[queue.Id],
                ["blocking"] = queue.Blocking,
                ["name"] = queue.Name,
            });
        }

        var commands = new JsonArray();
        foreach (var command in definition.Commands)
        {
            commands.Add(new JsonObject
            {
                ["permittedUsers"] = new JsonArray(),
                ["permittedGroups"] = new JsonArray(),
                ["id"] = commandIds[command.Id],
                ["name"] = command.Name,
                ["enabled"] = command.Enabled,
                ["include"] = false,
                ["mode"] = 0,
                ["command"] = string.Join("\r\n", command.Commands),
                ["regexExplicitCapture"] = false,
                ["location"] = 0,
                ["ignoreBotAccount"] = command.IgnoreBotAccount,
                ["ignoreInternal"] = command.IgnoreInternalMessages,
                ["sources"] = command.Sources,
                ["persistCounter"] = false,
                ["persistUserCounter"] = false,
                ["caseSensitive"] = command.CaseSensitive,
                ["globalCooldown"] = command.GlobalCooldown,
                ["userCooldown"] = command.UserCooldown,
                ["group"] = null,
                ["grantType"] = 0,
            });
        }

        return new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["name"] = projectName,
                ["author"] = definition.Metadata.Author,
                ["version"] = projectVersion,
                ["description"] = definition.Metadata.Description,
                ["autoRunAction"] = null,
                ["minimumVersion"] = null,
            },
            ["data"] = new JsonObject
            {
                ["actions"] = actions,
                ["queues"] = queues,
                ["commands"] = commands,
                ["websocketServers"] = new JsonArray(),
                ["websocketClients"] = new JsonArray(),
                ["timers"] = new JsonArray(),
            },
            ["version"] = PayloadVersion,
            ["exportedFrom"] = ExportedFrom,
            ["minimumVersion"] = definition.Metadata.MinimumVersion,
        };
    }

    private static StreamerBotAction DecodeAction(JsonObject value)
    {
        var triggers = (value["triggers"] as JsonArray ??
                throw new InvalidDataException("An action requires triggers."))
            .Select(item =>
            {
                var trigger = item?.AsObject() ??
                    throw new InvalidDataException("A trigger must be an object.");
                var type = RequiredInteger(trigger, "type");
                return type switch
                {
                    401 => new StreamerBotTrigger(
                        RequiredString(trigger, "id"),
                        "command",
                        RequiredBoolean(trigger, "enabled"),
                        RequiredString(trigger, "commandId")),
                    702 => new StreamerBotTrigger(
                        RequiredString(trigger, "id"),
                        "test",
                        RequiredBoolean(trigger, "enabled"),
                        null),
                    _ => throw new InvalidDataException(
                        $"Trigger type {type} is unsupported by stable-v23."),
                };
            })
            .ToArray();
        var subActions = (value["subActions"] as JsonArray ??
                throw new InvalidDataException("An action requires subActions."))
            .Select(item =>
            {
                var subAction = item?.AsObject() ??
                    throw new InvalidDataException("A sub-action must be an object.");
                var type = RequiredInteger(subAction, "type");
                return type switch
                {
                    123 => new StreamerBotSubAction(
                        RequiredString(subAction, "id"),
                        "setArgument",
                        RequiredBoolean(subAction, "enabled"),
                        RequiredString(subAction, "variableName"),
                        subAction["value"]?.GetValue<string>(),
                        RequiredBoolean(subAction, "autoType"),
                        Weight: RequiredDouble(subAction, "weight")),
                    99999 => new StreamerBotSubAction(
                        RequiredString(subAction, "id"),
                        "executeBridge",
                        RequiredBoolean(subAction, "enabled"),
                        null,
                        null,
                        false,
                        Weight: RequiredDouble(subAction, "weight")),
                    _ => throw new InvalidDataException(
                        $"Sub-action type {type} is unsupported by stable-v23."),
                };
            })
            .ToArray();

        return new(
            RequiredString(value, "id"),
            RequiredString(value, "name"),
            RequiredBoolean(value, "enabled"),
            value["queue"]?.GetValue<string>(),
            RequiredBoolean(value, "concurrent"),
            RequiredBoolean(value, "alwaysRun"),
            triggers,
            subActions,
            Group: value["group"]?.GetValue<string>(),
            RandomAction: RequiredBoolean(value, "randomAction"),
            ExcludeFromPending: RequiredBoolean(value, "excludeFromPending"),
            ExcludeFromHistory: RequiredBoolean(value, "excludeFromHistory"));
    }

    private static string RequiredString(JsonObject value, string property) =>
        value[property]?.GetValue<string>() ??
        throw new InvalidDataException($"Property '{property}' must be a string.");

    private static bool RequiredBoolean(JsonObject value, string property) =>
        value[property]?.GetValue<bool>() ??
        throw new InvalidDataException($"Property '{property}' must be a Boolean.");

    private static int RequiredInteger(JsonObject value, string property) =>
        value[property]?.GetValue<int>() ??
        throw new InvalidDataException($"Property '{property}' must be an integer.");

    private static double RequiredDouble(JsonObject value, string property) =>
        value[property]?.GetValue<double>() ??
        throw new InvalidDataException($"Property '{property}' must be a number.");

    private static string EncodeEnvelope(string payloadJson)
    {
        using var envelope = new MemoryStream();
        envelope.Write(Magic);
        using (var gzip = new GZipStream(
            envelope,
            CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(payloadJson));
        }

        return Convert.ToBase64String(envelope.ToArray());
    }
}
