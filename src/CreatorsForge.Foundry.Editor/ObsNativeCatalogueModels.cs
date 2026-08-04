using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Editor;

public sealed record ObsNativeCatalogue(
    int SchemaVersion,
    string Revision,
    string SdkVersion,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<ObsNativeSymbol> Symbols);

public sealed record ObsNativeSymbol(
    string Name,
    string Kind,
    string Category,
    string Header,
    string Signature,
    string Summary,
    string MinimumVersion,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<ObsNativeParameter> Parameters,
    string? DocumentationUrl = null,
    string? Caution = null);

public sealed record ObsNativeParameter(
    string Name,
    string Description);

public sealed record ObsNativeCompletionItem(
    string Name,
    string Kind,
    string Category,
    string Signature,
    string Summary,
    string Header,
    string Availability,
    string? DocumentationUrl,
    int ReplacementStart);

public sealed record ObsNativeSignatureHelp(
    ObsNativeSymbol Symbol,
    int ActiveParameter);

public sealed record ObsNativeDefinition(
    string Header,
    string Symbol);

public sealed record ObsNativeIntelligenceResult(
    IReadOnlyList<FoundryDiagnostic> Diagnostics);
