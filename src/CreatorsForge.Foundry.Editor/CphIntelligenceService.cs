using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CreatorsForge.Foundry.Editor;

public interface ICphIntelligenceService
{
    CphCatalogue Catalogue { get; }

    IReadOnlyList<CphCompletionItem> GetCompletions(
        string source,
        int position,
        string profile);

    CphSignatureHelp? GetSignatureHelp(
        string source,
        int position,
        string profile);

    CphIntelligenceResult Analyze(
        string source,
        string filePath,
        string profile);
}

public sealed partial class CphIntelligenceService : ICphIntelligenceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, CphMethod> methods;

    public CphIntelligenceService(CphCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
        methods = catalogue.Methods.ToDictionary(
            method => method.Name,
            StringComparer.Ordinal);
    }

    public CphCatalogue Catalogue { get; }

    public static CphIntelligenceService LoadEmbedded()
    {
        var assembly = typeof(CphIntelligenceService).Assembly;
        const string resourceName =
            "CreatorsForge.Foundry.Editor.Catalogs.streamerbot-cph-v1.json";
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded CPH catalogue '{resourceName}' was not found.");
        var catalogue = JsonSerializer.Deserialize<CphCatalogue>(
            stream,
            SerializerOptions) ??
            throw new InvalidOperationException(
                "The embedded CPH catalogue could not be deserialized.");
        if (catalogue.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"CPH catalogue schema {catalogue.SchemaVersion} is not supported.");
        }

        return new(catalogue);
    }

    public IReadOnlyList<CphCompletionItem> GetCompletions(
        string source,
        int position,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var prefix = GetCompletionPrefix(source, position);
        if (prefix is null)
        {
            return [];
        }

        return Catalogue.Methods
            .Where(method =>
                method.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                AvailableOverloads(method, profile).Length != 0)
            .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .Select(method => new CphCompletionItem(
                method.Name,
                method.Category,
                method.Summary,
                method.Status,
                FormatAvailability(method),
                method.DocumentationUrl,
                method.Example,
                AvailableOverloads(method, profile)))
            .ToArray();
    }

    public CphSignatureHelp? GetSignatureHelp(
        string source,
        int position,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var invocation = FindInvocation(source, position);
        if (invocation is null ||
            !methods.TryGetValue(invocation.Value.MethodName, out var method))
        {
            return null;
        }

        var overloads = AvailableOverloads(method, profile);
        return overloads.Length == 0
            ? null
            : new(
                method.Name,
                invocation.Value.ActiveParameter,
                overloads,
                method.Summary,
                FormatAvailability(method));
    }

    public CphIntelligenceResult Analyze(
        string source,
        string filePath,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var diagnostics = new List<FoundryDiagnostic>();
        var memberAccesses = root.DescendantNodes()
                     .OfType<MemberAccessExpressionSyntax>()
                     .Where(node =>
                         node.Expression is IdentifierNameSyntax
                         {
                             Identifier.ValueText: "CPH",
                         })
                     .ToArray();
        foreach (var memberAccess in memberAccesses)
        {
            var name = memberAccess.Name.Identifier.ValueText;
            if (name.Length == 0)
            {
                continue;
            }

            var line = memberAccess.Name.GetLocation()
                .GetLineSpan()
                .StartLinePosition;
            var location = new FoundryDiagnosticLocation(
                filePath,
                Line: line.Line + 1,
                Column: line.Character + 1);
            if (!methods.TryGetValue(name, out var method))
            {
                diagnostics.Add(new(
                    "CFC0003",
                    FoundryDiagnosticSeverity.Error,
                    $"CPH method '{name}' is not present in catalogue {Catalogue.Revision}.",
                    location,
                    "Choose a method offered by CPH completion."));
                continue;
            }

            var availableOverloads = AvailableOverloads(method, profile);
            var argumentCount = memberAccess.Parent is InvocationExpressionSyntax invocation
                ? invocation.ArgumentList.Arguments.Count
                : -1;
            var matchingOverloads = argumentCount < 0
                ? method.Overloads
                : method.Overloads
                    .Where(overload =>
                        OverloadAcceptsArgumentCount(overload, argumentCount))
                    .ToArray();
            var isUnavailable = availableOverloads.Length == 0 ||
                (matchingOverloads.Count != 0 &&
                 !matchingOverloads.Any(overload =>
                     overload.Profiles.Contains(profile, StringComparer.Ordinal)));
            if (isUnavailable)
            {
                diagnostics.Add(new(
                    "CFC0001",
                    FoundryDiagnosticSeverity.Error,
                    $"CPH.{name} is unavailable for Streamer.bot profile '{profile}'.",
                    location,
                    $"Select a compatible profile ({string.Join(", ", AllProfiles(method))}) or use another method."));
            }

            if (string.Equals(
                method.Status,
                "deprecated",
                StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new(
                    "CFC0002",
                    FoundryDiagnosticSeverity.Warning,
                    $"CPH.{name} is deprecated.",
                    location,
                    method.RelatedMethods.Count == 0
                        ? "Use a supported replacement."
                        : $"Consider {string.Join(" or ", method.RelatedMethods.Select(item => $"CPH.{item}"))}."));
            }
        }

        return new(diagnostics);
    }

    private static string? GetCompletionPrefix(string source, int position)
    {
        var bounded = Math.Clamp(position, 0, source.Length);
        var match = CompletionPattern().Match(source[..bounded]);
        return match.Success ? match.Groups["prefix"].Value : null;
    }

    private static (string MethodName, int ActiveParameter)? FindInvocation(
        string source,
        int position)
    {
        var bounded = Math.Clamp(position, 0, source.Length);
        var prefix = source[..bounded];
        var match = InvocationPattern().Match(prefix);
        if (!match.Success)
        {
            return null;
        }

        var arguments = match.Groups["arguments"].Value;
        var depth = 0;
        var activeParameter = 0;
        foreach (var character in arguments)
        {
            if (character is '(' or '[' or '{')
            {
                depth++;
            }
            else if (character is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (character == ',' && depth == 0)
            {
                activeParameter++;
            }
        }

        return (match.Groups["method"].Value, activeParameter);
    }

    private static CphOverload[] AvailableOverloads(
        CphMethod method,
        string profile) =>
        method.Overloads
            .Where(overload =>
                overload.Profiles.Contains(profile, StringComparer.Ordinal))
            .ToArray();

    private static bool OverloadAcceptsArgumentCount(
        CphOverload overload,
        int argumentCount)
    {
        var required = overload.Parameters.Count(parameter => !parameter.IsOptional);
        return argumentCount >= required && argumentCount <= overload.Parameters.Count;
    }

    private static IEnumerable<string> AllProfiles(CphMethod method) =>
        method.Overloads
            .SelectMany(overload => overload.Profiles)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

    private static string FormatAvailability(CphMethod method) =>
        string.Join(", ", AllProfiles(method));

    [GeneratedRegex(
        @"(?:^|[^\p{L}\p{N}_])CPH\.(?<prefix>[\p{L}\p{N}_]*)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex CompletionPattern();

    [GeneratedRegex(
        @"CPH\.(?<method>[A-Za-z_][A-Za-z0-9_]*)\((?<arguments>[^;{}]*)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex InvocationPattern();
}

public static class CphIntelligenceProvider
{
    private static readonly Lazy<CphIntelligenceService> DefaultService =
        new(CphIntelligenceService.LoadEmbedded);

    public static CphIntelligenceService Default => DefaultService.Value;
}
