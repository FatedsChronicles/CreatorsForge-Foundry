using CreatorsForge.Foundry.Editor;

namespace CreatorsForge.Foundry.Editor.Tests;

public sealed class RoslynEditorServiceTests
{
    private readonly RoslynEditorService service = new();

    [Fact]
    public async Task AnalyzeAcceptsValidNet481BridgeEntryPoint()
    {
        EditorSourceDocument[] sources =
        [
            new(
                @"C:\project\EntryPoint.cs",
                """
                using System;
                using System.Collections.Generic;

                public static class EntryPoint
                {
                    public static bool Execute(
                        IDictionary<string, object> arguments,
                        Action<string> logInformation)
                    {
                        logInformation(arguments.Count.ToString());
                        return true;
                    }
                }
                """),
        ];

        var result = await service.AnalyzeAsync(sources, CancellationToken.None);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.IsError);
    }

    [Fact]
    public async Task AnalyzeReportsCompilerLocationForCSharp73()
    {
        EditorSourceDocument[] sources =
        [
            new(
                @"C:\project\EntryPoint.cs",
                "public static class EntryPoint { public static void Run() { int value = ; } }"),
        ];

        var result = await service.AnalyzeAsync(sources, CancellationToken.None);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Code == "CS1525");
        Assert.Equal(1, diagnostic.Location?.Line);
        Assert.True(diagnostic.Location?.Column > 1);
    }

    [Fact]
    public async Task AnalyzeUsesProjectLanguageVersion()
    {
        EditorSourceDocument[] sources =
        [
            new(@"C:\project\Modern.cs", "public record Modern(string Value);"),
        ];

        var result = await service.AnalyzeAsync(sources, CancellationToken.None);

        Assert.Contains(
            result.Diagnostics,
            item => item.Code == "CS8370" && item.IsError);
    }

    [Fact]
    public async Task FormatProducesStableCSharpLayout()
    {
        var path = @"C:\project\Formatting.cs";
        EditorSourceDocument[] sources =
        [
            new(
                path,
                """
                class Example
                {
                void Run()
                {
                var value=1;
                }
                }
                """),
        ];

        var formatted = await service.FormatAsync(
            sources,
            path,
            CancellationToken.None);

        Assert.Equal(
            """
            class Example
            {
                void Run()
                {
                    var value = 1;
                }
            }
            """.ReplaceLineEndings(),
            formatted.ReplaceLineEndings());
    }

    [Fact]
    public async Task FindDefinitionNavigatesAcrossSourceDocuments()
    {
        var definitionPath = @"C:\project\Greeter.cs";
        var callerPath = @"C:\project\EntryPoint.cs";
        const string caller = """
            public static class EntryPoint
            {
                public static string Run() => Greeter.Message();
            }
            """;
        EditorSourceDocument[] sources =
        [
            new(
                definitionPath,
                "public static class Greeter { public static string Message() => \"Hello\"; }"),
            new(callerPath, caller),
        ];

        var result = await service.FindDefinitionAsync(
            sources,
            callerPath,
            caller.IndexOf("Message", StringComparison.Ordinal),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(definitionPath, result.FilePath);
        Assert.Equal(1, result.Line);
    }
}

public sealed class CphIntelligenceServiceTests
{
    private readonly CphIntelligenceService service =
        CphIntelligenceService.LoadEmbedded();

    [Fact]
    public void CatalogueContainsGeneratedProfileInventory()
    {
        Assert.StartsWith("1.0.0+", service.Catalogue.Revision);
        Assert.Equal(5, service.Catalogue.Profiles.Count);
        Assert.Contains(
            service.Catalogue.Profiles,
            profile =>
                profile.Id == "1.0.5-beta.6" &&
                profile.InterfaceSha256 == "d84df72080fe2dcecd2d82455930e350862dda637475f5e7458cd84a1e67be79");
        Assert.Contains(
            service.Catalogue.Profiles,
            profile =>
                profile.Id == "1.0.7-stable" &&
                profile.InterfaceSha256 == "aa6d8eeffa06eeb7f3e62bc6e296ce2301b67fcfdf538e46dc7676feb202bbbc");
        Assert.True(service.Catalogue.Methods.Count >= 500);
        Assert.Contains(
            service.Catalogue.Methods,
            method =>
                method.Name == "SendMessage" &&
                method.DocumentationUrl is not null &&
                method.Summary.Contains("Twitch", StringComparison.Ordinal));
    }

    [Fact]
    public void CompletionFiltersPrereleaseMethodsByProfile()
    {
        const string source = "CPH.TwitchGet";

        var stable = service.GetCompletions(
            source,
            source.Length,
            "1.0.4-stable");
        var alpha = service.GetCompletions(
            source,
            source.Length,
            "1.0.5-alpha.34");

        Assert.DoesNotContain(stable, item => item.Name == "TwitchGetMods");
        Assert.Contains(alpha, item => item.Name == "TwitchGetMods");
    }

    [Fact]
    public void SignatureHelpTracksActiveParameterAndDocumentation()
    {
        const string source = "CPH.SendMessage(\"Hello\", ";

        var help = service.GetSignatureHelp(
            source,
            source.Length,
            "1.0.4-stable");

        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
        Assert.Contains(help.Overloads, overload => overload.Parameters.Count == 3);
        Assert.Contains("Twitch", help.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityDiagnosticRejectsAlphaOnlyMethodInStableProfile()
    {
        const string source = "class Test { void Run() { CPH.TwitchGetMods(); } }";

        var stable = service.Analyze(
            source,
            @"C:\project\CPHInline.cs",
            "1.0.4-stable");
        var alpha = service.Analyze(
            source,
            @"C:\project\CPHInline.cs",
            "1.0.5-alpha.34");

        Assert.Contains(stable.Diagnostics, item => item.Code == "CFC0001");
        Assert.DoesNotContain(alpha.Diagnostics, item => item.Code == "CFC0001");
    }

    [Fact]
    public void CompatibilityDiagnosticReportsDeprecatedAndUnknownMethods()
    {
        const string source =
            "class Test { void Run() { CPH.GetUserVar<int>(\"u\", \"v\"); CPH.NotARealMethod(); } }";

        var result = service.Analyze(
            source,
            @"C:\project\CPHInline.cs",
            "1.0.4-stable");

        Assert.Contains(result.Diagnostics, item => item.Code == "CFC0002");
        Assert.Contains(result.Diagnostics, item => item.Code == "CFC0003");
    }

    [Fact]
    public void Beta6UsesExactCatalogueWithoutCompatibilityWarning()
    {
        const string source = "class Test { void Run() { CPH.TwitchGetMods(); } }";

        var result = service.Analyze(
            source,
            @"C:\project\CPHInline.cs",
            "1.0.5-beta.6");
        var completions = service.GetCompletions(
            "CPH.TwitchGet",
            "CPH.TwitchGet".Length,
            "1.0.5-beta.6");

        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "CFC0004");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "CFC0001");
        Assert.Contains(completions, item => item.Name == "TwitchGetMods");
    }

    [Fact]
    public void Stable107UsesExactCatalogueWithoutCompatibilityWarning()
    {
        const string source = "class Test { void Run() { CPH.TwitchGetMods(); } }";

        var result = service.Analyze(
            source,
            @"C:\project\CPHInline.cs",
            "1.0.7-stable");
        var completions = service.GetCompletions(
            "CPH.TwitchGet",
            "CPH.TwitchGet".Length,
            "1.0.7-stable");

        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "CFC0004");
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == "CFC0001");
        Assert.Contains(completions, item => item.Name == "TwitchGetMods");
    }
}
