using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Editor;

public sealed record SnippetCatalogue(
    int SchemaVersion,
    string Revision,
    IReadOnlyList<SnippetDefinition> Snippets);

public sealed record SnippetDefinition(
    string Id,
    string Name,
    string Version,
    string Author,
    string Target,
    string Language,
    string Kind,
    string Description,
    string Source,
    IReadOnlyList<string> Prefixes,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> RequiredMethods,
    IReadOnlyList<string> Body,
    SnippetSecurity Security,
    SnippetGuide? Guide = null);

public sealed record SnippetSecurity(
    bool FileAccess,
    bool NetworkAccess,
    bool ProcessExecution);

public sealed record SnippetGuide(
    IReadOnlyList<SnippetGuideField> Fields);

public sealed record SnippetGuideField(
    int Index,
    string Label,
    string Description,
    string ValueKind,
    IReadOnlyList<string> Options);

public sealed record SnippetLoadResult(
    SnippetCatalogue? Catalogue,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
{
    public bool IsSuccess =>
        Catalogue is not null &&
        Diagnostics.All(diagnostic => !diagnostic.IsError);
}

public sealed record SnippetLibraryLoadResult(
    SnippetService Service,
    IReadOnlyList<FoundryDiagnostic> Diagnostics,
    IReadOnlyList<string> LoadedFiles);

public sealed record SnippetCompletionItem(
    string Id,
    string Prefix,
    string Name,
    string Kind,
    string Description,
    string Source,
    string Availability,
    SnippetSecurity Security,
    int ReplacementStart);

public sealed record SnippetPlaceholder(
    int Index,
    int Offset,
    int Length,
    string DefaultValue);

public sealed record SnippetExpansion(
    string SnippetId,
    string Text,
    IReadOnlyList<SnippetPlaceholder> Placeholders);

public sealed record GuidedSnippetExpansionResult(
    SnippetExpansion? Expansion,
    IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Expansion is not null && Errors.Count == 0;
}
