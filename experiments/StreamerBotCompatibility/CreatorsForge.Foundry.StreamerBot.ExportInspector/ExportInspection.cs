using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CreatorsForge.Foundry.StreamerBot.ExportInspector;

public static class ExportInspection
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static ExportInspectionReport Inspect(
        string sourceName,
        string importCode,
        string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        DecodedExport decoded = ExportEnvelope.Decode(importCode);
        NormalizedExport normalized = new ExportNormalizer().Normalize(decoded.Root);

        Directory.CreateDirectory(outputDirectory);

        string decodedPath = Path.Combine(outputDirectory, "decoded.json");
        string normalizedPath = Path.Combine(outputDirectory, "normalized.json");
        string reportPath = Path.Combine(outputDirectory, "inspection.json");

        File.WriteAllText(
            decodedPath,
            decoded.Root.ToJsonString(JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        File.WriteAllText(
            normalizedPath,
            normalized.Root.ToJsonString(JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        var report = new ExportInspectionReport(
            sourceName,
            decoded.EnvelopeBytes,
            decoded.JsonBytes,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(importCode.Trim()))),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(decoded.Json))),
            normalized.DistinctGuidCount,
            normalized.AbsolutePathProperties,
            GetRootProperties(decoded.Root),
            GetSchema(decoded.Root));

        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        return report;
    }

    private static string[] GetRootProperties(JsonNode root)
    {
        return root is JsonObject jsonObject
            ? jsonObject
                .Select(property => property.Key)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
    }

    private static string[] GetSchema(JsonNode root)
    {
        var schema = new HashSet<string>(StringComparer.Ordinal);
        AddSchema(root, "$", schema);
        return schema.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddSchema(JsonNode node, string path, ISet<string> schema)
    {
        if (node is JsonObject jsonObject)
        {
            schema.Add($"{path}:object");
            foreach (KeyValuePair<string, JsonNode?> property in jsonObject)
            {
                if (property.Value is null)
                {
                    schema.Add($"{path}.{property.Key}:null");
                }
                else
                {
                    AddSchema(property.Value, $"{path}.{property.Key}", schema);
                }
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            schema.Add($"{path}:array");
            foreach (JsonNode? item in jsonArray)
            {
                if (item is not null)
                {
                    AddSchema(item, $"{path}[]", schema);
                }
            }

            return;
        }

        schema.Add($"{path}:{node.GetValueKind().ToString().ToLowerInvariant()}");
    }
}

public sealed record ExportInspectionReport(
    string SourceName,
    int EnvelopeBytes,
    int JsonBytes,
    string ImportCodeSha256,
    string DecodedJsonSha256,
    int DistinctGuidCount,
    IReadOnlyList<string> AbsolutePathProperties,
    IReadOnlyList<string> RootProperties,
    IReadOnlyList<string> Schema);
