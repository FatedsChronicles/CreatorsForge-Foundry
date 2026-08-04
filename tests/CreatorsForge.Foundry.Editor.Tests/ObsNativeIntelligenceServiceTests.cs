namespace CreatorsForge.Foundry.Editor.Tests;

public sealed class ObsNativeIntelligenceServiceTests
{
    private readonly ObsNativeIntelligenceService service =
        ObsNativeIntelligenceService.LoadEmbedded();

    [Fact]
    public void EmbeddedCatalogueIsPinnedAndContainsCoreFilterApis()
    {
        Assert.Equal("32.1.2", service.Catalogue.SdkVersion);
        Assert.Equal("obs-libobs-32.1.2-v1", service.Catalogue.Revision);
        Assert.Equal(35, service.Catalogue.Symbols.Count);
        Assert.Contains(
            service.Catalogue.Symbols,
            symbol => symbol.Name == "obs_register_source" && symbol.Header == "obs-source.h");
        Assert.Contains(
            service.Catalogue.Symbols,
            symbol => symbol.Name == "obs_source_skip_video_filter");
    }

    [Fact]
    public void CompletionFiltersByTypedNativePrefix()
    {
        var source = "void render(void *data) { obs_source_process_ }";
        var position = source.IndexOf(" }", StringComparison.Ordinal);
        var items = service.GetCompletions(source, position, "32.x-windows-x64");

        Assert.Equal(
            ["obs_source_process_filter_begin", "obs_source_process_filter_end"],
            items.Select(item => item.Name));
        Assert.All(items, item => Assert.Equal(26, item.ReplacementStart));
    }

    [Fact]
    public void ExplicitCompletionCanFindUnprefixedObsUtility()
    {
        var items = service.GetCompletions("bl", 2, "32.x-windows-x64");

        var item = Assert.Single(items);
        Assert.Equal("blog", item.Name);
        Assert.Equal("util/base.h", item.Header);
    }

    [Fact]
    public void SignatureHelpTracksActiveParameter()
    {
        const string source = "obs_properties_add_int(props, \"count\", ";
        var help = service.GetSignatureHelp(
            source,
            source.Length,
            "32.x-windows-x64");

        Assert.NotNull(help);
        Assert.Equal("obs_properties_add_int", help.Symbol.Name);
        Assert.Equal(2, help.ActiveParameter);
        Assert.Equal("description", help.Symbol.Parameters[2].Name);
    }

    [Fact]
    public void AnalysisReportsUnknownCallAndMissingModuleHeaderWithoutTypeNoise()
    {
        const string source = "void run(obs_source_t *source)\n{\n    obs_source_mispelled(source);\n}";
        var result = service.Analyze(
            source,
            @"C:\project\plugin.c",
            "32.x-windows-x64");

        Assert.Equal(["CFN1003", "CFN1001"], result.Diagnostics.Select(item => item.Code));
        Assert.Equal(3, result.Diagnostics[1].Location!.Line);
        Assert.Equal(5, result.Diagnostics[1].Location!.Column);
    }

    [Fact]
    public void AnalysisIgnoresCommentsStringsAndUserDeclarations()
    {
        const string source = """
            #include <obs-module.h>
            // obs_unknown(comment);
            const char *text = "obs_unknown(string)";
            bool obs_module_load(void) { return true; }
            """;

        var result = service.Analyze(
            source,
            @"C:\project\plugin.c",
            "32.x-windows-x64");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DefinitionMapsCatalogueSymbolToPinnedHeader()
    {
        const string source = "obs_source_skip_video_filter(data);";
        var definition = service.FindDefinition(source, 10);

        Assert.Equal("obs.h", definition!.Header);
        Assert.Equal("obs_source_skip_video_filter", definition.Symbol);
    }

    [Fact]
    public void ProfileDiagnosticsRejectUnavailableCatalogueSymbol()
    {
        var symbol = service.Catalogue.Symbols.Single(item =>
            item.Name == "obs_source_skip_video_filter") with
        {
            Profiles = ["future-windows-x64"],
        };
        var profileService = new ObsNativeIntelligenceService(
            service.Catalogue with { Symbols = [symbol] });
        const string source = "#include <obs-module.h>\nvoid render(obs_source_t *data) { obs_source_skip_video_filter(data); }";

        var result = profileService.Analyze(
            source,
            @"C:\project\plugin.c",
            "32.x-windows-x64");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("CFN1002", diagnostic.Code);
        Assert.Equal(2, diagnostic.Location!.Line);
    }

    [Fact]
    public void PassthroughFilterSourceHasNoCatalogueDiagnostics()
    {
        const string source = """
            #include <obs-module.h>
            static void foundry_render(void *data)
            {
                obs_source_skip_video_filter(data);
            }

            bool foundry_load(void)
            {
                struct obs_source_info info = {0};
                info.type = OBS_SOURCE_TYPE_FILTER;
                info.output_flags = OBS_SOURCE_VIDEO;
                obs_register_source(&info);
                return true;
            }
            """;

        var result = service.Analyze(
            source,
            @"C:\project\plugin.c",
            "32.x-windows-x64");

        Assert.Empty(result.Diagnostics);
    }
}
