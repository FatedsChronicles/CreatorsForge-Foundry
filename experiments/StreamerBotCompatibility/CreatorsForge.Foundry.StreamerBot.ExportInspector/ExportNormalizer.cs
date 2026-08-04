using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CreatorsForge.Foundry.StreamerBot.ExportInspector;

public sealed partial class ExportNormalizer
{
    private readonly Dictionary<Guid, string> guidTokens = [];
    private readonly List<string> absolutePathProperties = [];

    public NormalizedExport Normalize(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        JsonNode normalized = NormalizeNode(root, "$", propertyName: null);
        return new NormalizedExport(
            normalized,
            guidTokens.Count,
            absolutePathProperties.AsReadOnly());
    }

    private JsonNode NormalizeNode(JsonNode node, string path, string? propertyName)
    {
        if (node is JsonObject jsonObject)
        {
            var normalizedObject = new JsonObject();
            foreach (KeyValuePair<string, JsonNode?> property in jsonObject.OrderBy(
                property => property.Key,
                StringComparer.Ordinal))
            {
                normalizedObject[property.Key] = property.Value is null
                    ? null
                    : NormalizeNode(
                        property.Value,
                        $"{path}.{property.Key}",
                        property.Key);
            }

            return normalizedObject;
        }

        if (node is JsonArray jsonArray)
        {
            var normalizedArray = new JsonArray();
            for (int index = 0; index < jsonArray.Count; index++)
            {
                normalizedArray.Add(
                    jsonArray[index] is null
                        ? null
                        : NormalizeNode(
                            jsonArray[index]!,
                            $"{path}[{index}]",
                            propertyName: null));
            }

            return normalizedArray;
        }

        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            if (Guid.TryParse(text, out Guid guid))
            {
                if (!guidTokens.TryGetValue(guid, out string? token))
                {
                    token = $"<guid:{guidTokens.Count + 1}>";
                    guidTokens.Add(guid, token);
                }

                return JsonValue.Create(token)!;
            }

            if (IsTimestampProperty(propertyName)
                && DateTimeOffset.TryParse(text, out _))
            {
                return JsonValue.Create("<timestamp>")!;
            }

            if (AbsolutePathRegex().IsMatch(text))
            {
                absolutePathProperties.Add(path);
                return JsonValue.Create("<absolute-path>")!;
            }

            if (string.Equals(propertyName, "byteCode", StringComparison.Ordinal))
            {
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                return JsonValue.Create(
                    $"<byte-code length={text.Length} sha256={Convert.ToHexString(hash)}>")!;
            }
        }

        return node.DeepClone();
    }

    private static bool IsTimestampProperty(string? propertyName)
    {
        return string.Equals(propertyName, "t", StringComparison.OrdinalIgnoreCase)
            || propertyName?.EndsWith("At", StringComparison.OrdinalIgnoreCase) == true
            || propertyName?.EndsWith("Time", StringComparison.OrdinalIgnoreCase) == true;
    }

    [GeneratedRegex(@"(?:^|[""'])[A-Za-z]:[\\/]|^\\\\", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathRegex();
}

public sealed record NormalizedExport(
    JsonNode Root,
    int DistinctGuidCount,
    IReadOnlyList<string> AbsolutePathProperties);
