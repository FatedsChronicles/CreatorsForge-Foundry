using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public enum StreamerBotImportFindingSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record StreamerBotImportFinding(
    string Code,
    StreamerBotImportFindingSeverity Severity,
    string Message,
    string? Path = null);

public sealed record StreamerBotImportSummary(
    string Name,
    string Author,
    string Version,
    int PayloadVersion,
    string ExportedFrom,
    string? MinimumVersion,
    int ActionCount,
    int CommandCount,
    int QueueCount,
    int EditableCount,
    int OpaqueCount,
    int CSharpCount,
    int AbsolutePathCount,
    int ExternalReferenceCount,
    string SourceSha256,
    string SuggestedProfile);

public sealed record StreamerBotImportAnalysis(
    StreamerBotImportSummary? Summary,
    StreamerBotDefinition? Definition,
    JsonObject? Payload,
    IReadOnlyDictionary<string, string> CSharpSources,
    IReadOnlyList<StreamerBotImportFinding> Findings)
{
    public bool CanCreateProject =>
        Summary is not null && Definition is not null && Payload is not null &&
        Findings.All(item => item.Severity != StreamerBotImportFindingSeverity.Error);
}

public static class StreamerBotEnvelopeCodec
{
    public const int MaximumImportCodeCharacters = 16 * 1024 * 1024;
    public const int MaximumDecodedJsonBytes = 64 * 1024 * 1024;
    private static readonly byte[] Magic = "SBAE"u8.ToArray();

    public static JsonObject Decode(string importCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importCode);
        if (importCode.Length > MaximumImportCodeCharacters)
        {
            throw new InvalidDataException("The import code exceeds Foundry's 16 MiB text limit.");
        }

        var compact = string.Concat(importCode.Where(character => !char.IsWhiteSpace(character)));
        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(compact);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The Streamer.bot import code is not valid Base64 text.", exception);
        }

        if (envelope.Length <= Magic.Length ||
            !envelope.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException("The import code does not begin with the SBAE signature.");
        }

        try
        {
            using var compressed = new MemoryStream(envelope, Magic.Length, envelope.Length - Magic.Length, false);
            using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            using var decoded = new MemoryStream();
            var buffer = new byte[81920];
            var total = 0;
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                total = checked(total + read);
                if (total > MaximumDecodedJsonBytes)
                {
                    throw new InvalidDataException("The decoded payload exceeds Foundry's 64 MiB safety limit.");
                }
                decoded.Write(buffer, 0, read);
            }

            var json = new UTF8Encoding(false, true).GetString(decoded.ToArray());
            return JsonNode.Parse(
                json,
                nodeOptions: null,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                }) as JsonObject ?? throw new InvalidDataException("The decoded payload root must be a JSON object.");
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The decoded payload is not strict UTF-8 text.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The decoded payload is not valid bounded JSON.", exception);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (IOException exception)
        {
            throw new InvalidDataException("The SBAE payload is not a valid GZip stream.", exception);
        }
    }

    public static string Encode(JsonObject payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var json = payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        using var envelope = new MemoryStream();
        envelope.Write(Magic);
        using (var gzip = new GZipStream(envelope, CompressionLevel.SmallestSize, true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }
        return Convert.ToBase64String(envelope.ToArray());
    }
}

public static partial class StreamerBotImportService
{
    private const int MaximumEntities = 10_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] PreservedCollectionNames = ["websocketServers", "websocketClients", "timers"];

    [GeneratedRegex("(?:^|[_.-])(password|passwd|secret|oauth|token|api[-_]?key|authorization|credential)(?:$|[_.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialNamePattern();

    [GeneratedRegex("^(?:[A-Za-z]:[\\\\/]|\\\\\\\\)", RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteWindowsPathPattern();

    public static StreamerBotImportAnalysis Analyze(string importCode)
    {
        JsonObject payload;
        try
        {
            payload = StreamerBotEnvelopeCodec.Decode(importCode);
        }
        catch (InvalidDataException exception)
        {
            return Failure("CFI1001", exception.Message);
        }

        var findings = new List<StreamerBotImportFinding>();
        var payloadVersion = ReadInteger(payload, "version");
        var exportedFrom = ReadString(payload, "exportedFrom");
        if (payloadVersion is null || string.IsNullOrWhiteSpace(exportedFrom) ||
            payload["meta"] is not JsonObject ||
            payload["data"] is not JsonObject data ||
            data["actions"] is not JsonArray actions ||
            data["commands"] is not JsonArray commands ||
            data["queues"] is not JsonArray queues)
        {
            return Failure("CFI1003", "The payload is missing its version, provenance, actions, commands, or queues contract.");
        }

        var entityCount = actions.Count + commands.Count + queues.Count;
        foreach (var action in actions.OfType<JsonObject>())
        {
            entityCount = checked(entityCount + ((action["triggers"] as JsonArray)?.Count ?? 0));
            entityCount = checked(entityCount + ((action["subActions"] as JsonArray)?.Count ?? 0));
        }
        foreach (var name in new[] { "websocketServers", "websocketClients", "timers" })
        {
            if (data[name] is JsonArray collection) entityCount += collection.Count;
        }
        if (entityCount > MaximumEntities)
        {
            return Failure("CFI1004", $"The payload contains {entityCount:N0} entities; Foundry permits at most {MaximumEntities:N0}.");
        }

        var verified = payloadVersion is 23 or 24;
        if (!verified)
        {
            findings.Add(new(
                "CFI1005",
                StreamerBotImportFindingSeverity.Error,
                $"Payload version {payloadVersion} has not been verified. Foundry can inspect it but cannot create a project.",
                "$.version"));
        }

        ScanPayload(payload, "$", findings);
        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToJsonString())));
        var mapping = new ImportMapping(payloadVersion.Value, exportedFrom!, sourceHash, findings);
        var definition = verified ? mapping.Map(payload) : null;
        var meta = payload["meta"] as JsonObject;
        var summary = new StreamerBotImportSummary(
            ReadString(meta, "name") ?? "Imported Streamer.bot Extension",
            ReadString(meta, "author") ?? string.Empty,
            ReadString(meta, "version") ?? "0.1.0",
            payloadVersion.Value,
            exportedFrom!,
            ReadString(payload, "minimumVersion"),
            actions.Count,
            commands.Count,
            queues.Count,
            mapping.EditableCount,
            mapping.OpaqueCount,
            mapping.CSharpSources.Count,
            findings.Count(item => item.Code == "CFI1102"),
            findings.Count(item => item.Code == "CFI1103"),
            sourceHash,
            SuggestedProfile(exportedFrom!));
        return new(summary, definition, payload, mapping.CSharpSources, findings);
    }

    private static StreamerBotImportAnalysis Failure(string code, string message) =>
        new(null, null, null, new Dictionary<string, string>(),
            [new(code, StreamerBotImportFindingSeverity.Error, message)]);

    private static void ScanPayload(JsonNode? node, string path, List<StreamerBotImportFinding> findings)
    {
        if (node is JsonObject value)
        {
            foreach (var property in value)
            {
                var propertyPath = $"{path}.{property.Key}";
                if (CredentialNamePattern().IsMatch(property.Key) &&
                    property.Value is JsonValue credential &&
                    !string.IsNullOrWhiteSpace(credential.ToString()))
                {
                    findings.Add(new("CFI1006", StreamerBotImportFindingSeverity.Error,
                        "A credential-like value is present. Remove it in Streamer.bot before importing; its value is intentionally hidden.", propertyPath));
                }
                ScanPayload(property.Value, propertyPath, findings);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++) ScanPayload(array[index], $"{path}[{index}]", findings);
        }
        else if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            if (AbsoluteWindowsPathPattern().IsMatch(text))
            {
                findings.Add(new("CFI1102", StreamerBotImportFindingSeverity.Warning,
                    "An absolute machine path must be removed or mapped before export.", path));
            }
            if (path.EndsWith(".references", StringComparison.OrdinalIgnoreCase) || path.Contains(".references[", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new("CFI1103", StreamerBotImportFindingSeverity.Warning,
                    "The import declares a compiler or external reference that requires portability review.", path));
            }
        }
    }

    private static string SuggestedProfile(string exportedFrom) => exportedFrom switch
    {
        "1.0.4" => "1.0.4-stable",
        "1.0.5-alpha.34" => "1.0.5-alpha.34",
        "1.0.5-beta.1" => "1.0.5-beta.1",
        "1.0.5-beta.6" => "1.0.5-beta.6",
        "1.0.7" => "1.0.7-stable",
        _ => "1.0.7-stable",
    };

    private static string? ReadString(JsonObject? value, string property) =>
        value?[property] is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;

    private static int? ReadInteger(JsonObject? value, string property) =>
        value?[property] is JsonValue scalar && scalar.TryGetValue<int>(out var number) ? number : null;

    private sealed class ImportMapping(int payloadVersion, string exportedFrom, string sourceHash, List<StreamerBotImportFinding> findings)
    {
        public Dictionary<string, string> CSharpSources { get; } = new(StringComparer.Ordinal);
        public int EditableCount { get; private set; }
        public int OpaqueCount { get; private set; }

        public StreamerBotDefinition Map(JsonObject payload)
        {
            var data = payload["data"]!.AsObject();
            var queueIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var queues = data["queues"]!.AsArray().Select((node, index) =>
            {
                var wire = node?.AsObject() ?? new JsonObject();
                var sourceId = ReadString(wire, "id") ?? $"queue-{index}";
                var id = LogicalId("queue", sourceId, index);
                queueIds[sourceId] = id;
                EditableCount++;
                return new StreamerBotQueueDefinition(id, ReadString(wire, "name") ?? $"Queue {index + 1}",
                    wire["blocking"]?.GetValue<bool>() ?? false, sourceId);
            }).ToArray();

            var commandIds = new Dictionary<string, string>(StringComparer.Ordinal);
            var commands = data["commands"]!.AsArray().Select((node, index) =>
            {
                var wire = node?.AsObject() ?? new JsonObject();
                var sourceId = ReadString(wire, "id") ?? $"command-{index}";
                var id = LogicalId("command", sourceId, index);
                commandIds[sourceId] = id;
                var isEditable = wire["command"] is JsonValue && wire["name"] is JsonValue;
                if (isEditable) EditableCount++; else OpaqueCount++;
                if (!isEditable) findings.Add(new("CFI1101", StreamerBotImportFindingSeverity.Warning,
                    "A command uses an unrecognized build-specific representation and will be preserved read-only.", $"$.data.commands[{index}]"));
                return new StreamerBotCommand(id, ReadString(wire, "name") ?? $"Preserved command {index + 1}",
                    (ReadString(wire, "command") ?? "<preserved>").Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries),
                    wire["enabled"]?.GetValue<bool>() ?? true, wire["caseSensitive"]?.GetValue<bool>() ?? false,
                    ReadInteger(wire, "globalCooldown") ?? 0, ReadInteger(wire, "userCooldown") ?? 0,
                    sourceId, !isEditable, isEditable ? null : $"commands/{index}" );
            }).ToArray();

            var actions = data["actions"]!.AsArray().Select((node, actionIndex) =>
                MapAction(node?.AsObject() ?? new JsonObject(), actionIndex, queueIds, commandIds)).ToArray();
            var meta = payload["meta"] as JsonObject;
            var hasOpaque = OpaqueCount > 0 ||
                PreservedCollectionNames.Any(name => data[name] is JsonArray { Count: > 0 });
            return new StreamerBotDefinition
            {
                Metadata = new()
                {
                    Author = ReadString(meta, "author") ?? string.Empty,
                    Description = ReadString(meta, "description") ?? string.Empty,
                },
                Import = new("streamerbot-preserved-v1", payloadVersion, exportedFrom,
                    ReadString(payload, "minimumVersion"), sourceHash,
                    "streamerbot/import-preservation.json", hasOpaque, null),
                Queues = queues,
                Commands = commands,
                Actions = actions,
            };
        }

        private StreamerBotAction MapAction(JsonObject wire, int actionIndex,
            Dictionary<string, string> queueIds, Dictionary<string, string> commandIds)
        {
            var sourceId = ReadString(wire, "id") ?? $"action-{actionIndex}";
            var id = LogicalId("action", sourceId, actionIndex);
            EditableCount++;
            var triggers = (wire["triggers"] as JsonArray ?? []).Select((node, index) =>
            {
                var item = node?.AsObject() ?? new JsonObject();
                var type = ReadInteger(item, "type") ?? -1;
                var triggerSourceId = ReadString(item, "id") ?? $"trigger-{index}";
                var triggerId = LogicalId($"{id}-trigger", triggerSourceId, index);
                if (type is 401 or 702) EditableCount++; else OpaqueCount++;
                if (type is not (401 or 702)) findings.Add(new("CFI1101", StreamerBotImportFindingSeverity.Warning,
                    $"Trigger type {type} is not visually editable and will be preserved read-only.", $"$.data.actions[{actionIndex}].triggers[{index}]"));
                var commandSource = ReadString(item, "commandId");
                return new StreamerBotTrigger(triggerId, type == 401 ? "command" : type == 702 ? "test" : "opaque",
                    item["enabled"]?.GetValue<bool>() ?? true,
                    commandSource is not null && commandIds.TryGetValue(commandSource, out var commandId) ? commandId : null,
                    type, triggerSourceId, type is not (401 or 702), type is 401 or 702 ? null : $"actions/{actionIndex}/triggers/{index}");
            }).ToArray();
            var subActions = (wire["subActions"] as JsonArray ?? []).Select((node, index) =>
                MapSubAction(node?.AsObject() ?? new JsonObject(), actionIndex, id, index)).ToArray();
            var queueSource = ReadString(wire, "queue");
            return new(id, ReadString(wire, "name") ?? $"Action {actionIndex + 1}",
                wire["enabled"]?.GetValue<bool>() ?? true,
                queueSource is not null && queueIds.TryGetValue(queueSource, out var queueId) ? queueId : null,
                wire["concurrent"]?.GetValue<bool>() ?? false,
                wire["alwaysRun"]?.GetValue<bool>() ?? false,
                triggers, subActions, sourceId);
        }

        private StreamerBotSubAction MapSubAction(JsonObject item, int actionIndex, string actionId, int index)
        {
            var type = ReadInteger(item, "type") ?? -1;
            var sourceId = ReadString(item, "id") ?? $"subAction-{index}";
            var id = LogicalId($"{actionId}-sub", sourceId, index);
            if (type == 123)
            {
                EditableCount++;
                return new(id, "setArgument", item["enabled"]?.GetValue<bool>() ?? true,
                    ReadString(item, "variableName"), ReadString(item, "value") ?? item["value"]?.ToString(),
                    item["autoType"]?.GetValue<bool>() ?? false, SourceType: type, SourceId: sourceId);
            }
            if (type == 99999 && ReadString(item, "byteCode") is { } encoded)
            {
                try
                {
                    var sourceBytes = Convert.FromBase64String(encoded);
                    if (sourceBytes.Length > 1024 * 1024) throw new InvalidDataException("Execute C# source exceeds 1 MiB.");
                    var source = StrictUtf8.GetString(sourceBytes);
                    var path = $"streamerbot/code/{actionId}/{id}.cs";
                    CSharpSources[path] = source;
                    EditableCount++;
                    var references = (item["references"] as JsonArray ?? []).Select(value => value?.ToString() ?? string.Empty).Where(value => value.Length > 0).ToArray();
                    return new(id, "executeCSharp", item["enabled"]?.GetValue<bool>() ?? true,
                        null, null, false, path, type, sourceId, false, null, references);
                }
                catch (Exception exception) when (exception is FormatException or DecoderFallbackException or InvalidDataException)
                {
                    findings.Add(new("CFI1104", StreamerBotImportFindingSeverity.Warning,
                        "An Execute C# body could not be decoded safely and will be preserved read-only.",
                        $"$.data.actions[{actionIndex}].subActions[{index}]"));
                }
            }

            OpaqueCount++;
            findings.Add(new("CFI1101", StreamerBotImportFindingSeverity.Warning,
                $"Sub-action type {type} is not visually editable and will be preserved read-only.",
                $"$.data.actions[{actionIndex}].subActions[{index}]"));
            return new(id, "opaque", item["enabled"]?.GetValue<bool>() ?? true,
                null, null, false, null, type, sourceId, true,
                $"actions/{actionIndex}/subActions/{index}");
        }

        private static string LogicalId(string kind, string sourceId, int index)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}\n{sourceId}\n{index}")));
            var prefix = new string(kind.Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray()).Trim('-');
            return $"{(prefix.Length == 0 ? "item" : prefix)}-{hash[..12]}";
        }
    }
}

public sealed record StreamerBotImportProjectRequest(
    string ProjectDirectory,
    string Name,
    string Id,
    string Version,
    string Author,
    string Profile,
    string? SourceAttribution,
    StreamerBotImportAnalysis Analysis);

public sealed record StreamerBotImportProjectResult(
    string? ProjectPath,
    IReadOnlyList<StreamerBotImportFinding> Findings)
{
    public bool IsSuccess => ProjectPath is not null && Findings.All(item => item.Severity != StreamerBotImportFindingSeverity.Error);
}

public static class StreamerBotImportProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async Task<StreamerBotImportProjectResult> CreateAsync(
        StreamerBotImportProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Analysis.CanCreateProject || request.Analysis.Definition is null ||
            request.Analysis.Payload is null || request.Analysis.Summary is null)
        {
            return new(null, [new("CFI1201", StreamerBotImportFindingSeverity.Error,
                "Analyze and resolve all blocking import findings before creating a project.")]);
        }

        var projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        if (Directory.Exists(projectDirectory) || File.Exists(projectDirectory))
        {
            return new(null, [new("CFI1202", StreamerBotImportFindingSeverity.Error,
                "The destination already exists. Choose a new empty project location.", projectDirectory)]);
        }

        var definition = request.Analysis.Definition with
        {
            Metadata = request.Analysis.Definition.Metadata with { Author = request.Author },
            Import = request.Analysis.Definition.Import! with { SourceAttribution = request.SourceAttribution },
        };
        var manifest = new FoundryProjectManifest
        {
            Name = request.Name.Trim(),
            Id = request.Id.Trim(),
            Version = request.Version.Trim(),
            Target = new() { Provider = "streamerbot", Profile = request.Profile },
            Features = new() { MockRuntime = true },
            TargetDefinition = "streamerbot/streamerbot.json",
            Outputs = [FoundryOutputKinds.StreamerBotPackage],
            Publishing = new()
            {
                PackageName = request.Id.Trim(),
                Summary = definition.Metadata.Description.Length > 0 ? definition.Metadata.Description : request.Name.Trim(),
                Authors = string.IsNullOrWhiteSpace(request.Author) ? ["Unknown original author"] : [request.Author.Trim()],
                LicenseFile = "LICENSE.txt",
                ChangelogFile = "CHANGELOG.md",
            },
        };
        var manifestPath = Path.Combine(projectDirectory, SafeFileName(request.Name) + ".foundryproj");
        var manifestErrors = FoundryProjectValidator.Validate(manifest, manifestPath).Where(item => item.IsError).ToArray();
        if (manifestErrors.Length > 0)
        {
            return new(null, manifestErrors.Select(item => new StreamerBotImportFinding(
                item.Code, StreamerBotImportFindingSeverity.Error, item.Message, item.Location?.JsonPath)).ToArray());
        }

        var created = false;
        try
        {
            Directory.CreateDirectory(projectDirectory);
            created = true;
            await WriteAsync(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions) + "\n", cancellationToken);
            await WriteAsync(Path.Combine(projectDirectory, "streamerbot", "streamerbot.json"),
                StreamerBotDefinitionLoader.Serialize(definition), cancellationToken);
            var preservation = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["sourceSha256"] = request.Analysis.Summary.SourceSha256,
                ["payloadVersion"] = request.Analysis.Summary.PayloadVersion,
                ["exportedFrom"] = request.Analysis.Summary.ExportedFrom,
                ["payload"] = request.Analysis.Payload.DeepClone(),
            };
            await WriteAsync(Path.Combine(projectDirectory, "streamerbot", "import-preservation.json"),
                preservation.ToJsonString(JsonOptions) + "\n", cancellationToken);
            foreach (var source in request.Analysis.CSharpSources)
            {
                var path = ResolveConfined(projectDirectory, source.Key);
                await WriteAsync(path, source.Value.EndsWith('\n') ? source.Value : source.Value + "\n", cancellationToken);
            }
            await WriteAsync(Path.Combine(projectDirectory, "CHANGELOG.md"),
                $"# Changelog\n\n## {request.Version}\n\n- Imported Streamer.bot payload v{request.Analysis.Summary.PayloadVersion} from {request.Analysis.Summary.ExportedFrom} for review and development in Foundry.\n", cancellationToken);
            await WriteAsync(Path.Combine(projectDirectory, "streamerbot", "import-report.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    request.Analysis.Summary,
                    sourceAttribution = request.SourceAttribution,
                    licenceStatus = "not-supplied",
                    findings = request.Analysis.Findings,
                }, JsonOptions) + "\n", cancellationToken);
            return new(manifestPath, request.Analysis.Findings);
        }
        catch (OperationCanceledException)
        {
            if (created) TryDelete(projectDirectory);
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (created) TryDelete(projectDirectory);
            return new(null, [new("CFI1203", StreamerBotImportFindingSeverity.Error,
                $"The imported project could not be created: {exception.Message}", projectDirectory)]);
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Trim().Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(result) ? "ImportedExtension" : result;
    }

    private static string ResolveConfined(string root, string relativePath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("An imported source path leaves the project directory.");
        return path;
    }

    private static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, path, false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
