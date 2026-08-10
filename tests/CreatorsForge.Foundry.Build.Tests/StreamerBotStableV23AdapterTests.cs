using System.Text;
using System.Text.Json;
using CreatorsForge.Foundry.Build.StreamerBot;

namespace CreatorsForge.Foundry.Build.Tests;

public sealed class StreamerBotStableV23AdapterTests
{
    [Fact]
    public void DeterministicIdsAreStableAndNamespaced()
    {
        var first = DeterministicStreamerBotId.Create(
            "com.creatorsforge.tests",
            "action",
            "hello");
        var repeated = DeterministicStreamerBotId.Create(
            "com.creatorsforge.tests",
            "action",
            "hello");
        var otherKind = DeterministicStreamerBotId.Create(
            "com.creatorsforge.tests",
            "command",
            "hello");

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, otherKind);
        Assert.True(Guid.TryParseExact(first, "D", out _));
    }

    [Fact]
    public void StableV23ExportIsDeterministicAndRoundTrips()
    {
        var definition = CreateDefinition();
        const string bridge = "public class CPHInline { public bool Execute() => true; }\n";

        var first = StreamerBotStableV23Adapter.Encode(
            definition,
            "com.creatorsforge.tests",
            "Adapter Test",
            "1.2.3",
            bridge);
        var second = StreamerBotStableV23Adapter.Encode(
            definition,
            "com.creatorsforge.tests",
            "Adapter Test",
            "1.2.3",
            bridge);
        var decoded = StreamerBotStableV23Adapter.Decode(first.ImportCode);
        var decodedDefinition =
            StreamerBotStableV23Adapter.DecodeDefinition(first.ImportCode);

        Assert.Equal(first.ImportCode, second.ImportCode);
        Assert.Equal(23, decoded["version"]!.GetValue<int>());
        Assert.Equal("1.0.4", decoded["exportedFrom"]!.GetValue<string>());
        Assert.True(first.Report.RoundTripVerified);
        Assert.Single(decodedDefinition.Actions);
        Assert.Equal(
            ["setArgument", "executeBridge"],
            decodedDefinition.Actions[0].SubActions.Select(item => item.Kind));
        Assert.Equal(first.Report.PayloadSha256, second.Report.PayloadSha256);

        var data = decoded["data"]!.AsObject();
        var action = Assert.Single(data["actions"]!.AsArray())!.AsObject();
        var command = Assert.Single(data["commands"]!.AsArray())!.AsObject();
        var queue = Assert.Single(data["queues"]!.AsArray())!.AsObject();
        Assert.Equal(queue["id"]!.GetValue<string>(), action["queue"]!.GetValue<string>());
        Assert.Equal(
            command["id"]!.GetValue<string>(),
            action["triggers"]![0]!["commandId"]!.GetValue<string>());
        var executeBridge = action["subActions"]![1]!.AsObject();
        Assert.Empty(executeBridge["references"]!.AsArray());
        Assert.Equal(
            bridge,
            Encoding.UTF8.GetString(
                Convert.FromBase64String(
                    executeBridge["byteCode"]!.GetValue<string>())));
    }

    [Fact]
    public void StableV23DecoderRejectsOtherPayloadVersions()
    {
        var export = StreamerBotStableV23Adapter.Encode(
            CreateDefinition(),
            "com.creatorsforge.tests",
            "Adapter Test",
            "1.0.0",
            "public class CPHInline {}\n");
        var bytes = Convert.FromBase64String(export.ImportCode);
        using var compressed = new MemoryStream(bytes, 4, bytes.Length - 4);
        using var gzip = new System.IO.Compression.GZipStream(
            compressed,
            System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var changed = reader.ReadToEnd().Replace(
            "\"version\": 23",
            "\"version\": 24",
            StringComparison.Ordinal);
        using var envelope = new MemoryStream();
        envelope.Write("SBAE"u8);
        using (var output = new System.IO.Compression.GZipStream(
            envelope,
            System.IO.Compression.CompressionLevel.SmallestSize,
            leaveOpen: true))
        {
            output.Write(Encoding.UTF8.GetBytes(changed));
        }

        Assert.Throws<InvalidDataException>(() =>
            StreamerBotStableV23Adapter.Decode(
                Convert.ToBase64String(envelope.ToArray())));
    }

    [Fact]
    public void StableV23RoundTripsExpandedActionAndCommandOptions()
    {
        var original = CreateDefinition();
        original = original with
        {
            Metadata = original.Metadata with { MinimumVersion = "1.0.7" },
            Commands = [original.Commands[0] with
            {
                IgnoreBotAccount = false,
                IgnoreInternalMessages = false,
                Sources = 7,
            }],
            Actions = [original.Actions[0] with
            {
                Group = "Moderation",
                RandomAction = true,
                ExcludeFromPending = true,
                ExcludeFromHistory = true,
                SubActions = original.Actions[0].SubActions.Select((item, index) =>
                    item with { Weight = index + 1 }).ToArray(),
            }],
        };

        var export = StreamerBotStableV23Adapter.Encode(original, "com.creatorsforge.tests",
            "Expanded options", "1.1.0-beta.1", "public class CPHInline {}\n");
        var decoded = StreamerBotStableV23Adapter.DecodeDefinition(export.ImportCode);

        Assert.Equal("1.0.7", decoded.Metadata.MinimumVersion);
        Assert.False(decoded.Commands[0].IgnoreBotAccount);
        Assert.False(decoded.Commands[0].IgnoreInternalMessages);
        Assert.Equal(7, decoded.Commands[0].Sources);
        Assert.Equal("Moderation", decoded.Actions[0].Group);
        Assert.True(decoded.Actions[0].RandomAction);
        Assert.True(decoded.Actions[0].ExcludeFromPending);
        Assert.True(decoded.Actions[0].ExcludeFromHistory);
        Assert.Equal([1d, 2d], decoded.Actions[0].SubActions.Select(item => item.Weight));
    }

    [Fact]
    public void DefinitionLoaderRejectsBrokenReferences()
    {
        var invalid = CreateDefinition() with
        {
            Actions =
            [
                CreateDefinition().Actions[0] with
                {
                    QueueId = "missing",
                    Triggers =
                    [
                        new("missing-command", "command", true, "missing"),
                    ],
                },
            ],
        };

        var errors = StreamerBotDefinitionLoader.Validate(invalid);

        Assert.Contains(errors, error => error.Contains("missing queue", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("missing command", StringComparison.Ordinal));
    }

    [Fact]
    public void PublishedSchemasAndSampleDefinitionAreLoadable()
    {
        var schemaDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Schemas");
        foreach (var schemaPath in Directory.EnumerateFiles(
                     schemaDirectory,
                     "*.schema.json"))
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
            Assert.Equal(
                "https://json-schema.org/draft/2020-12/schema",
                schema.RootElement.GetProperty("$schema").GetString());
            Assert.Equal(
                JsonValueKind.Object,
                schema.RootElement.ValueKind);
        }

        var sample = StreamerBotDefinitionLoader.Load(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fixtures",
                    "streamerbot.json")));
        Assert.True(sample.IsSuccess);
        Assert.Single(sample.Definition!.Actions);
    }

    internal static StreamerBotDefinition CreateDefinition() => new()
    {
        Metadata = new()
        {
            Author = "Creators Forge",
            Description = "Round-trip fixture",
        },
        Queues =
        [
            new("default", "Default", false),
        ],
        Commands =
        [
            new("hello", "Hello", ["!hello", "!hi"], true, false, 5, 10),
        ],
        Actions =
        [
            new(
                "hello",
                "Hello",
                true,
                "default",
                false,
                false,
                [
                    new("command", "command", true, "hello"),
                    new("test", "test", true, null),
                ],
                [
                    new("argument", "setArgument", true, "message", "hello", true),
                    new("bridge", "executeBridge", true, null, null, false),
                ]),
        ],
    };
}
