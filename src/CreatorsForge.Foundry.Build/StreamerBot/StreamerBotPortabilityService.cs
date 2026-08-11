using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CreatorsForge.Foundry.Build.StreamerBot;

public sealed record StreamerBotPortabilityReport(
    int SchemaVersion,
    StreamerBotPortabilitySummary Summary,
    IReadOnlyList<StreamerBotPortabilityItem> Resources);

public sealed record StreamerBotPortabilitySummary(
    int Total,
    int Portable,
    int ReconnectByName,
    int ConfirmAfterImport,
    int ManualConfiguration,
    int Required,
    int Unused);

public sealed record StreamerBotPortabilityItem(
    string Id,
    string Name,
    string Type,
    bool Required,
    string Portability,
    bool HasSuggestedValue,
    bool IsAbsoluteMachinePath,
    IReadOnlyList<StreamerBotResourceBinding> Bindings);

public static class StreamerBotPortabilityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] CredentialTerms =
        ["password", "passwd", "secret", "token", "api key", "apikey", "oauth", "credential"];

    public static StreamerBotPortabilityReport CreateReport(StreamerBotDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var resources = definition.Resources
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => new StreamerBotPortabilityItem(
                item.Id,
                item.Name,
                item.Type,
                item.Required,
                item.Portability,
                !string.IsNullOrWhiteSpace(item.SuggestedValue),
                IsAbsoluteMachinePath(item),
                (item.Bindings ?? [])
                    .OrderBy(binding => binding.EntityType, StringComparer.Ordinal)
                    .ThenBy(binding => binding.EntityId, StringComparer.Ordinal)
                    .ThenBy(binding => binding.Property, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
        return new(
            1,
            new(
                resources.Length,
                resources.Count(item => item.Portability == StreamerBotResourcePortability.Portable),
                resources.Count(item => item.Portability == StreamerBotResourcePortability.ReconnectByName),
                resources.Count(item => item.Portability == StreamerBotResourcePortability.ConfirmAfterImport),
                resources.Count(item => item.Portability == StreamerBotResourcePortability.ManualConfiguration),
                resources.Count(item => item.Required),
                resources.Count(item => item.Bindings.Count == 0)),
            resources);
    }

    public static string Serialize(StreamerBotPortabilityReport report) =>
        JsonSerializer.Serialize(report, JsonOptions) + "\n";

    internal static bool IsCredentialLike(StreamerBotResourceDefinition resource)
    {
        if (string.IsNullOrWhiteSpace(resource.SuggestedValue)) return false;
        var label = $"{resource.Name} {resource.Description}";
        if (CredentialTerms.Any(term => label.Contains(term, StringComparison.OrdinalIgnoreCase))) return true;
        var value = resource.SuggestedValue.Trim();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(value, "^(gh[opsu]_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,})$",
                   RegexOptions.CultureInvariant);
    }

    internal static bool IsAbsoluteMachinePath(StreamerBotResourceDefinition resource) =>
        resource.Type is "localFile" or "localFolder" or "executable" &&
        !string.IsNullOrWhiteSpace(resource.SuggestedValue) &&
        (Path.IsPathFullyQualified(resource.SuggestedValue) ||
         resource.SuggestedValue.StartsWith("\\\\", StringComparison.Ordinal));
}
