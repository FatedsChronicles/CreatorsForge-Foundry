using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using CreatorsForge.Foundry.StreamerBot.ExportInspector;

namespace CreatorsForge.Foundry.StreamerBot.ExportInspector.Tests;

public sealed class ExportEnvelopeTests
{
    [Fact]
    public void DecodeReadsSbaeGzipJsonEnvelope()
    {
        const string json = """
            {"version":24,"t":"2026-07-24T10:00:00+01:00","actions":[]}
            """;
        string importCode = CreateImportCode(json);

        DecodedExport decoded = ExportEnvelope.Decode(importCode);

        Assert.Equal(json, decoded.Json);
        Assert.Equal(Encoding.UTF8.GetByteCount(json), decoded.JsonBytes);
        Assert.Equal(24, decoded.Root["version"]!.GetValue<int>());
    }

    [Fact]
    public void DecodeRejectsAnUnexpectedEnvelopeSignature()
    {
        string importCode = Convert.ToBase64String("NOPE"u8);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => ExportEnvelope.Decode(importCode));

        Assert.Contains("SBAE", exception.Message);
    }

    [Fact]
    public void DecodeRejectsImportCodeAboveTheSafetyLimit()
    {
        string importCode = new('A', ExportEnvelope.MaxImportCodeCharacters + 1);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => ExportEnvelope.Decode(importCode));

        Assert.Contains("safety limit", exception.Message);
    }

    [Fact]
    public void NormalizeRemovesNondeterministicAndMachineSpecificValues()
    {
        JsonNode root = JsonNode.Parse(
            """
            {
              "id": "9a526583-5416-45fd-8e7c-58c8fbe475ba",
              "parentId": "9a526583-5416-45fd-8e7c-58c8fbe475ba",
              "t": "2026-07-24T10:00:00+01:00",
              "reference": "F:\\Extensions\\Probe.dll",
              "byteCode": "compiled-value"
            }
            """)!;

        NormalizedExport normalized = new ExportNormalizer().Normalize(root);

        Assert.Equal("<guid:1>", normalized.Root["id"]!.GetValue<string>());
        Assert.Equal("<guid:1>", normalized.Root["parentId"]!.GetValue<string>());
        Assert.Equal("<timestamp>", normalized.Root["t"]!.GetValue<string>());
        Assert.Equal("<absolute-path>", normalized.Root["reference"]!.GetValue<string>());
        Assert.StartsWith(
            "<byte-code length=14 sha256=",
            normalized.Root["byteCode"]!.GetValue<string>());
        Assert.Equal(["$.reference"], normalized.AbsolutePathProperties);
    }

    [Fact]
    public void InspectReportsSchemaWithoutIncludingPropertyValues()
    {
        const string json = """
            {"version":24,"actions":[{"name":"Sensitive fixture value"}]}
            """;
        string importCode = CreateImportCode(json);
        string outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"foundry-export-inspector-{Guid.NewGuid():N}");

        try
        {
            ExportInspectionReport report = ExportInspection.Inspect(
                "fixture",
                importCode,
                outputDirectory);

            Assert.Contains("$.actions:array", report.Schema);
            Assert.Contains("$.actions[].name:string", report.Schema);
            Assert.DoesNotContain(
                report.Schema,
                entry => entry.Contains("Sensitive fixture value", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void InspectReportsValueFreeNativeOperationShapes()
    {
        const string json = """
            {
              "version": 24,
              "data": {
                "actions": [{
                  "triggers": [{"id":"secret-trigger-id","type":401,"enabled":true,"commandId":"secret-command-id"}],
                  "subActions": [
                    {"id":"one","type":123,"enabled":true,"variableName":"secret-name","value":"secret-value","autoType":true},
                    {"id":"two","type":123,"enabled":false,"variableName":"other","value":"other-value","autoType":false}
                  ]
                }]
              }
            }
            """;
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"foundry-native-shapes-{Guid.NewGuid():N}");
        try
        {
            var report = ExportInspection.Inspect("fixture", CreateImportCode(json), outputDirectory);
            var trigger = Assert.Single(report.NativeOperations, item => item.EntityKind == "trigger");
            var subAction = Assert.Single(report.NativeOperations, item => item.EntityKind == "subAction");
            Assert.Equal(401, trigger.NativeType);
            Assert.Equal(123, subAction.NativeType);
            Assert.Equal(2, subAction.Occurrences);
            Assert.Contains("variableName:string", subAction.Properties);
            Assert.DoesNotContain(report.NativeOperations.SelectMany(item => item.Properties),
                value => value.Contains("secret", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
        }
    }

    private static string CreateImportCode(string json)
    {
        using var envelope = new MemoryStream();
        envelope.Write("SBAE"u8);

        using (var gzip = new GZipStream(envelope, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }

        return Convert.ToBase64String(envelope.ToArray());
    }
}
