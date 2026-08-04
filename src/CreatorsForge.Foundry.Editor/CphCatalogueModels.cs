using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Editor;

public sealed record CphCatalogue(
    int SchemaVersion,
    string Revision,
    IReadOnlyList<CphCatalogueProfile> Profiles,
    IReadOnlyList<CphMethod> Methods);

public sealed record CphCatalogueProfile(
    string Id,
    string ProductVersion,
    string Channel,
    string InterfaceSha256);

public sealed record CphMethod(
    string Name,
    string Category,
    string Platform,
    string Summary,
    string Status,
    string MinimumVersion,
    string? DocumentationUrl,
    string? Example,
    IReadOnlyList<string> RelatedMethods,
    IReadOnlyList<string> Cautions,
    IReadOnlyList<CphOverload> Overloads);

public sealed record CphOverload(
    string Signature,
    string ReturnType,
    IReadOnlyList<CphParameter> Parameters,
    IReadOnlyList<string> Profiles);

public sealed record CphParameter(
    string Name,
    string Type,
    string Description,
    bool IsOptional,
    string? DefaultValue);

public sealed record CphCompletionItem(
    string Name,
    string Category,
    string Summary,
    string Status,
    string Availability,
    string? DocumentationUrl,
    string? Example,
    IReadOnlyList<CphOverload> Overloads);

public sealed record CphSignatureHelp(
    string MethodName,
    int ActiveParameter,
    IReadOnlyList<CphOverload> Overloads,
    string Summary,
    string Availability);

public sealed record CphIntelligenceResult(
    IReadOnlyList<FoundryDiagnostic> Diagnostics);
