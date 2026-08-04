using System.Text;
using CreatorsForge.Foundry.Editor;

namespace CreatorsForge.Foundry.Editor.Tests;

public sealed class SnippetServiceTests
{
    private readonly CphIntelligenceService cph =
        CphIntelligenceService.LoadEmbedded();
    private readonly SnippetService snippets;

    public SnippetServiceTests()
    {
        snippets = SnippetService.LoadEmbedded(cph.Catalogue);
    }

    [Fact]
    public void EmbeddedCatalogueContainsVersionedVerifiedInventory()
    {
        Assert.Equal(1, snippets.Catalogue.SchemaVersion);
        Assert.Equal("1.2.0", snippets.Catalogue.Revision);
        Assert.Equal(30, snippets.Catalogue.Snippets.Count);
        Assert.Equal(20, snippets.Catalogue.Snippets.Count(item => item.Kind == "method"));
        Assert.Equal(10, snippets.Catalogue.Snippets.Count(item => item.Kind == "workflow"));
        Assert.All(
            snippets.Catalogue.Snippets,
            snippet =>
            {
                Assert.Equal("built-in", snippet.Source);
                Assert.NotEmpty(snippet.Profiles);
                Assert.NotEmpty(snippet.RequiredMethods);
                Assert.NotNull(snippet.Guide);
                Assert.False(snippet.Security.FileAccess);
                Assert.False(snippet.Security.NetworkAccess);
                Assert.False(snippet.Security.ProcessExecution);
            });
    }

    [Fact]
    public void CompletionMatchesPrefixAndProfileAtReplacementBoundary()
    {
        const string source = "        cph.global.";

        var completions = snippets.GetCompletions(
            source,
            source.Length,
            "1.0.4-stable");

        Assert.Equal(
            ["cph.global.get", "cph.global.set", "cph.global.unset"],
            completions.Select(item => item.Prefix));
        Assert.All(
            completions,
            item => Assert.Equal(8, item.ReplacementStart));
    }

    [Fact]
    public void Beta6ReceivesVerifiedBuiltInSnippets()
    {
        const string source = "cph.global.";

        var completions = snippets.GetCompletions(
            source,
            source.Length,
            "1.0.5-beta.6");

        Assert.NotEmpty(completions);
        Assert.All(completions, item =>
            Assert.Contains("1.0.5-beta.6", item.Availability, StringComparison.Ordinal));
    }

    [Fact]
    public void CompletionExcludesSnippetOutsideSelectedProfile()
    {
        var definition = snippets.Catalogue.Snippets[0] with
        {
            Id = "test.alpha-only",
            Prefixes = ["alpha.only"],
            Profiles = ["1.0.5-alpha.34"],
        };
        var isolated = new SnippetService(
            new(1, "test", [definition]));
        const string source = "alpha.";

        Assert.Empty(isolated.GetCompletions(
            source,
            source.Length,
            "1.0.4-stable"));
        Assert.Single(isolated.GetCompletions(
            source,
            source.Length,
            "1.0.5-alpha.34"));
    }

    [Fact]
    public void ExpansionRemovesMarkersAndPreservesIndentationAndNavigationOrder()
    {
        var expansion = snippets.Expand(
            "creatorsforge.streamerbot.arguments.try-get",
            "        ",
            "\r\n");

        Assert.DoesNotContain("${", expansion.Text, StringComparison.Ordinal);
        Assert.Contains("\r\n        {", expansion.Text, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3, 0], expansion.Placeholders.Select(item => item.Index));
        Assert.Equal(
            "argumentName",
            expansion.Text.Substring(
                expansion.Placeholders[0].Offset,
            expansion.Placeholders[0].Length));
    }

    [Fact]
    public void GuidedExpansionEscapesStringsAndRetainsEditableSpans()
    {
        var result = snippets.ExpandGuided(
            "creatorsforge.streamerbot.chat.send-message",
            new Dictionary<int, string>
            {
                [1] = "Hello \"chat\"\nNext line",
                [2] = "false",
                [3] = "true",
            },
            "    ",
            "\n");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "CPH.SendMessage(\"Hello \\\"chat\\\"\\nNext line\", false, true);",
            result.Expansion!.Text);
        Assert.Equal(
            [1, 2, 3, 0],
            result.Expansion.Placeholders.Select(item => item.Index));
    }

    [Fact]
    public void GuidedExpansionRejectsInvalidIdentifier()
    {
        var result = snippets.ExpandGuided(
            "creatorsforge.streamerbot.arguments.try-get",
            new Dictionary<int, string>
            {
                [1] = "userName",
                [2] = "string",
                [3] = "not valid",
            },
            string.Empty,
            "\n");

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors,
            error => error.Contains("valid C# identifier", StringComparison.Ordinal));
    }

    [Fact]
    public void LoaderReportsMalformedPlaceholderAndUnknownProfile()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "revision": "test",
              "snippets": [{
                "id": "test.invalid",
                "name": "Invalid",
                "version": "1.0.0",
                "author": "Test",
                "target": "streamerbot",
                "language": "csharp",
                "kind": "method",
                "description": "Invalid fixture.",
                "source": "project",
                "prefixes": ["test.invalid"],
                "profiles": ["unknown-profile"],
                "categories": ["test"],
                "requiredMethods": ["LogInfo"],
                "body": ["CPH.LogInfo(\"${bad}\");"],
                "security": {
                  "fileAccess": false,
                  "networkAccess": false,
                  "processExecution": false
                }
              }]
            }
            """;

        var result = SnippetCatalogueLoader.Load(json, cph.Catalogue);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Diagnostics, item => item.Code == "CFS5003");
        Assert.Contains(result.Diagnostics, item => item.Code == "CFS5004");
    }

    [Fact]
    public async Task UserCatalogueImportsAndParticipatesInCompletion()
    {
        using var temporary = TemporaryDirectory.Create();
        var source = Path.Combine(temporary.Path, "my-snippets.json");
        var userDirectory = Path.Combine(temporary.Path, "library");
        await File.WriteAllTextAsync(source, CreateUserCatalogue("my.log", "my.log"));

        var result = await SnippetProvider.ImportUserCatalogueAsync(source, userDirectory);

        Assert.DoesNotContain(result.Diagnostics, item => item.IsError);
        Assert.Single(result.LoadedFiles);
        Assert.Contains(result.Service.Catalogue.Snippets, item => item.Id == "my.log" && item.Source == "user");
        Assert.Single(result.Service.GetCompletions("my.", 3, "1.0.4-stable"));
    }

    [Fact]
    public void ExternalCatalogueCollisionIsRejectedWithoutReplacingBuiltIns()
    {
        using var temporary = TemporaryDirectory.Create();
        File.WriteAllText(
            Path.Combine(temporary.Path, "collision.json"),
            CreateUserCatalogue("my.collision", "cph.sendmessage"));

        var result = SnippetProvider.Reload(temporary.Path);

        Assert.Contains(result.Diagnostics, item => item.Code == "CFS5005");
        Assert.DoesNotContain(result.Service.Catalogue.Snippets, item => item.Id == "my.collision");
    }

    private static string CreateUserCatalogue(string id, string prefix) => $$"""
        {
          "schemaVersion": 1,
          "revision": "1.0.0",
          "snippets": [{
            "id": "{{id}}",
            "name": "My log helper",
            "version": "1.0.0",
            "author": "Test user",
            "target": "streamerbot",
            "language": "csharp",
            "kind": "method",
            "description": "A user-created log helper.",
            "source": "user",
            "prefixes": ["{{prefix}}"],
            "profiles": ["1.0.4-stable"],
            "categories": ["logging"],
            "requiredMethods": ["LogInfo"],
            "body": ["CPH.LogInfo(\"${1:message}\");$0"],
            "security": { "fileAccess": false, "networkAccess": false, "processExecution": false }
          }]
        }
        """;

    [Fact]
    public async Task EveryBuiltInDefaultExpansionCompilesForEveryDeclaredProfile()
    {
        var roslyn = new RoslynEditorService();
        foreach (var profile in cph.Catalogue.Profiles.Select(item => item.Id))
        {
            var source = BuildCompilationFixture(profile);
            var result = await roslyn.AnalyzeAsync(
                [new(@"C:\project\BuiltInSnippets.cs", source)],
                CancellationToken.None);

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.IsError);
        }
    }

    private string BuildCompilationFixture(string profile)
    {
        var selectedSnippets = snippets.Catalogue.Snippets
            .Where(item => item.Profiles.Contains(profile, StringComparer.Ordinal))
            .ToArray();
        var requiredMethods = selectedSnippets
            .SelectMany(item => item.RequiredMethods)
            .Distinct(StringComparer.Ordinal)
            .Select(name => cph.Catalogue.Methods.Single(method => method.Name == name))
            .SelectMany(method => method.Overloads)
            .Where(overload =>
                overload.Profiles.Contains(profile, StringComparer.Ordinal))
            .Select(overload => overload.Signature)
            .Where(signature =>
                !signature.Contains("Streamer.bot.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var builder = new StringBuilder(
            """
            public abstract class SnippetCphProxy
            {
            """.ReplaceLineEndings());
        foreach (var signature in requiredMethods)
        {
            builder
                .Append("    public abstract ")
                .Append(signature)
                .AppendLine(";");
        }

        builder.AppendLine("}").AppendLine().Append(
            """
            public sealed class BuiltInSnippetFixture
            {
                public SnippetCphProxy CPH { get; set; }

            """.ReplaceLineEndings());
        var index = 0;
        foreach (var snippet in selectedSnippets)
        {
            var guided = snippets.ExpandGuided(
                snippet.Id,
                new Dictionary<int, string>(),
                "        ",
                "\r\n");
            Assert.True(guided.IsSuccess);
            var expansion = guided.Expansion!;
            builder
                .Append("    public bool Snippet")
                .Append(index++)
                .AppendLine("()")
                .AppendLine("    {")
                .Append("        ")
                .AppendLine(expansion.Text)
                .AppendLine("        return true;")
                .AppendLine("    }")
                .AppendLine();
        }

        return builder.AppendLine("}").ToString();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "foundry-snippets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new(path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
