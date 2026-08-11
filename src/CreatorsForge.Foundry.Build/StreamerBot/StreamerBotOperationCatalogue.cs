using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Core.Compatibility;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public sealed record StreamerBotOperationCatalogue(
    int SchemaVersion,
    string Revision,
    IReadOnlyList<StreamerBotOperationDefinition> Operations);

public sealed record StreamerBotOperationDefinition(
    string Id,
    string EntityKind,
    string ModelKind,
    string Category,
    string Name,
    string Description,
    int NativeType,
    string OutputMode,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<StreamerBotOperationField> Fields,
    IReadOnlyList<string> ArgumentsConsumed,
    IReadOnlyList<string> ArgumentsProduced,
    string? Documentation = null);

public sealed record StreamerBotOperationField(
    string Id,
    string Label,
    string Type,
    bool Required,
    string? DefaultValue = null,
    string? Help = null);

public sealed record StreamerBotOperationCatalogueLoadResult(
    StreamerBotOperationCatalogue? Catalogue,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Catalogue is not null && Errors.Count == 0;
}

public sealed class StreamerBotOperationCatalogueService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly HashSet<string> EntityKinds = new(["trigger", "subAction"], StringComparer.Ordinal);
    private static readonly HashSet<string> FieldTypes = new(
        ["text", "multiline", "boolean", "commandReference", "variableExpression"], StringComparer.Ordinal);
    private readonly Dictionary<string, StreamerBotOperationDefinition> operations;

    public StreamerBotOperationCatalogueService(StreamerBotOperationCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
        operations = catalogue.Operations.ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    public StreamerBotOperationCatalogue Catalogue { get; }

    public static StreamerBotOperationCatalogueService LoadEmbedded()
    {
        const string resourceName =
            "CreatorsForge.Foundry.Build.StreamerBot.Catalogues.streamerbot-operations-v1.json";
        using var stream = typeof(StreamerBotOperationCatalogueService).Assembly
            .GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Embedded Streamer.bot operation catalogue '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = Load(reader.ReadToEnd());
        if (!result.IsSuccess)
            throw new InvalidOperationException("The embedded Streamer.bot operation catalogue is invalid: " +
                string.Join(" ", result.Errors));
        return new(result.Catalogue!);
    }

    public static StreamerBotOperationCatalogueLoadResult Load(string json)
    {
        StreamerBotOperationCatalogue? catalogue;
        try
        {
            catalogue = JsonSerializer.Deserialize<StreamerBotOperationCatalogue>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            return new(null, [$"Catalogue JSON is invalid: {exception.Message}"]);
        }

        if (catalogue is null) return new(null, ["Catalogue JSON is empty."]);
        var errors = Validate(catalogue);
        return new(errors.Count == 0 ? catalogue : null, errors);
    }

    public IReadOnlyList<StreamerBotOperationDefinition> Search(
        string entityKind,
        string? profile,
        string? query = null)
    {
        var text = query?.Trim() ?? string.Empty;
        return Catalogue.Operations
            .Where(item => item.EntityKind == entityKind)
            .Where(item => profile is null || item.Profiles.Contains(profile, StringComparer.Ordinal))
            .Where(item => text.Length == 0 ||
                item.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public StreamerBotOperationDefinition Get(string id) => operations.TryGetValue(id, out var operation)
        ? operation
        : throw new KeyNotFoundException($"Operation '{id}' is not present in catalogue {Catalogue.Revision}.");

    public StreamerBotOperationDefinition? Find(string entityKind, string modelKind) =>
        Catalogue.Operations.FirstOrDefault(item =>
            item.EntityKind == entityKind && item.ModelKind == modelKind);

    private static List<string> Validate(StreamerBotOperationCatalogue catalogue)
    {
        var errors = new List<string>();
        if (catalogue.SchemaVersion != 1) errors.Add($"Catalogue schema {catalogue.SchemaVersion} is unsupported.");
        if (string.IsNullOrWhiteSpace(catalogue.Revision)) errors.Add("Catalogue revision is required.");
        if (catalogue.Operations is null) return ["Catalogue operations must be an array."];
        foreach (var duplicate in catalogue.Operations.GroupBy(item => item.Id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
            errors.Add($"Operation ID '{duplicate.Key}' is duplicated.");
        foreach (var operation in catalogue.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.Id) || string.IsNullOrWhiteSpace(operation.Name) ||
                string.IsNullOrWhiteSpace(operation.ModelKind) || !EntityKinds.Contains(operation.EntityKind))
                errors.Add($"Operation '{operation.Id}' has invalid identity fields.");
            if (operation.NativeType <= 0) errors.Add($"Operation '{operation.Id}' requires a verified native type.");
            if (operation.OutputMode is not ("native" or "csharp" or "native-or-csharp"))
                errors.Add($"Operation '{operation.Id}' has unsupported output mode '{operation.OutputMode}'.");
            if (operation.Profiles.Count == 0 || operation.Profiles.Any(profile => !FoundryStreamerBotProfiles.Supported.Contains(profile)))
                errors.Add($"Operation '{operation.Id}' declares an unknown or empty profile set.");
            foreach (var field in operation.Fields)
                if (string.IsNullOrWhiteSpace(field.Id) || string.IsNullOrWhiteSpace(field.Label) || !FieldTypes.Contains(field.Type))
                    errors.Add($"Operation '{operation.Id}' has invalid field '{field.Id}'.");
            foreach (var duplicate in operation.Fields.GroupBy(field => field.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
                errors.Add($"Operation '{operation.Id}' duplicates field '{duplicate.Key}'.");
        }
        return errors;
    }
}
