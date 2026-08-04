using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Editor;

public interface IObsNativeIntelligenceService
{
    ObsNativeCatalogue Catalogue { get; }

    IReadOnlyList<ObsNativeCompletionItem> GetCompletions(
        string source,
        int position,
        string profile);

    ObsNativeSignatureHelp? GetSignatureHelp(
        string source,
        int position,
        string profile);

    ObsNativeDefinition? FindDefinition(string source, int position);

    ObsNativeIntelligenceResult Analyze(
        string source,
        string filePath,
        string profile);
}

public sealed partial class ObsNativeIntelligenceService : IObsNativeIntelligenceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, ObsNativeSymbol> symbols;

    public ObsNativeIntelligenceService(ObsNativeCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
        symbols = catalogue.Symbols.ToDictionary(symbol => symbol.Name, StringComparer.Ordinal);
    }

    public ObsNativeCatalogue Catalogue { get; }

    public static ObsNativeIntelligenceService LoadEmbedded()
    {
        var assembly = typeof(ObsNativeIntelligenceService).Assembly;
        const string resourceName =
            "CreatorsForge.Foundry.Editor.Catalogs.obs-libobs-32.1.2.json";
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded OBS native catalogue '{resourceName}' was not found.");
        var catalogue = JsonSerializer.Deserialize<ObsNativeCatalogue>(
            stream,
            SerializerOptions) ??
            throw new InvalidOperationException(
                "The embedded OBS native catalogue could not be deserialized.");
        if (catalogue.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"OBS native catalogue schema {catalogue.SchemaVersion} is not supported.");
        }

        return new(catalogue);
    }

    public IReadOnlyList<ObsNativeCompletionItem> GetCompletions(
        string source,
        int position,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var prefix = GetIdentifierAt(source, position, includeLeftOnly: true);
        if (prefix.Length == 0)
        {
            return [];
        }

        return Catalogue.Symbols
            .Where(symbol => IsAvailable(symbol, profile))
            .Where(symbol => symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .Select(symbol => new ObsNativeCompletionItem(
                symbol.Name,
                symbol.Kind,
                symbol.Category,
                symbol.Signature,
                symbol.Summary,
                symbol.Header,
                string.Join(", ", symbol.Profiles),
                symbol.DocumentationUrl,
                Math.Clamp(position, 0, source.Length) - prefix.Length))
            .ToArray();
    }

    public ObsNativeSignatureHelp? GetSignatureHelp(
        string source,
        int position,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var invocation = FindInvocation(source, position);
        if (invocation is null ||
            !symbols.TryGetValue(invocation.Value.Symbol, out var symbol) ||
            !IsAvailable(symbol, profile))
        {
            return null;
        }

        return new(symbol, invocation.Value.ActiveParameter);
    }

    public ObsNativeDefinition? FindDefinition(string source, int position)
    {
        ArgumentNullException.ThrowIfNull(source);
        var identifier = GetIdentifierAt(source, position, includeLeftOnly: false);
        return symbols.TryGetValue(identifier, out var symbol)
            ? new(symbol.Header, symbol.Name)
            : null;
    }

    public ObsNativeIntelligenceResult Analyze(
        string source,
        string filePath,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diagnostics = new List<FoundryDiagnostic>();
        var sanitized = StripCommentsAndStrings(source);
        var seen = new HashSet<(string Code, int Offset)>();
        foreach (Match match in ObsSymbolPattern().Matches(sanitized))
        {
            var name = match.Value;
            if (IsDeclarationName(sanitized, match.Index, name))
            {
                continue;
            }

            var location = CreateLocation(source, filePath, match.Index);
            if (!symbols.TryGetValue(name, out var symbol))
            {
                var followingText = sanitized[(match.Index + match.Length)..];
                if (!followingText.AsSpan().TrimStart().StartsWith('('))
                {
                    continue;
                }

                if (seen.Add(("CFN1001", match.Index)))
                {
                    diagnostics.Add(new(
                        "CFN1001",
                        FoundryDiagnosticSeverity.Info,
                        $"OBS symbol '{name}' is not present in pinned catalogue {Catalogue.Revision}.",
                        location,
                        "Check the spelling or verify the symbol against the pinned OBS SDK headers."));
                }

                continue;
            }

            if (!IsAvailable(symbol, profile) && seen.Add(("CFN1002", match.Index)))
            {
                diagnostics.Add(new(
                    "CFN1002",
                    FoundryDiagnosticSeverity.Error,
                    $"OBS symbol '{name}' is unavailable for profile '{profile}'.",
                    location,
                    $"Use a compatible API for {string.Join(", ", symbol.Profiles)}."));
            }
        }

        var usesObsApi = ObsSymbolPattern().IsMatch(sanitized);
        if (usesObsApi && !ObsIncludePattern().IsMatch(source))
        {
            diagnostics.Insert(0, new(
                "CFN1003",
                FoundryDiagnosticSeverity.Warning,
                "This file uses OBS APIs without including <obs-module.h>.",
                new FoundryDiagnosticLocation(filePath, Line: 1, Column: 1),
                "Add #include <obs-module.h>."));
        }

        return new(diagnostics);
    }

    private static bool IsAvailable(ObsNativeSymbol symbol, string profile) =>
        symbol.Profiles.Contains(profile, StringComparer.OrdinalIgnoreCase);

    private static string GetIdentifierAt(
        string source,
        int position,
        bool includeLeftOnly)
    {
        var bounded = Math.Clamp(position, 0, source.Length);
        var start = bounded;
        while (start > 0 && IsIdentifierCharacter(source[start - 1]))
        {
            start--;
        }

        var end = bounded;
        if (!includeLeftOnly)
        {
            while (end < source.Length && IsIdentifierCharacter(source[end]))
            {
                end++;
            }
        }

        return source[start..end];
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';

    private static (string Symbol, int ActiveParameter)? FindInvocation(
        string source,
        int position)
    {
        var bounded = Math.Clamp(position, 0, source.Length);
        var match = InvocationPattern().Match(source[..bounded]);
        if (!match.Success)
        {
            return null;
        }

        var activeParameter = 0;
        var depth = 0;
        foreach (var character in match.Groups["arguments"].Value)
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

        return (match.Groups["symbol"].Value, activeParameter);
    }

    private static bool IsDeclarationName(string source, int index, string name)
    {
        var tail = source[(index + name.Length)..];
        if (!tail.AsSpan().TrimStart().StartsWith('('))
        {
            return false;
        }

        var lineStart = source.LastIndexOf('\n', Math.Max(0, index - 1));
        var prefix = source[(lineStart + 1)..index].Trim();
        return prefix.EndsWith("bool", StringComparison.Ordinal) ||
            prefix.EndsWith("void", StringComparison.Ordinal) ||
            prefix.EndsWith("char *", StringComparison.Ordinal);
    }

    private static FoundryDiagnosticLocation CreateLocation(
        string source,
        string filePath,
        int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new(filePath, Line: line, Column: column);
    }

    private static string StripCommentsAndStrings(string source) =>
        NonCodePattern().Replace(source, match => new string(
            match.Value.Select(character => character is '\r' or '\n' ? character : ' ').ToArray()));

    [GeneratedRegex(
        @"\b(?:obs_[A-Za-z0-9_]+|OBS_[A-Z0-9_]+)\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex ObsSymbolPattern();

    [GeneratedRegex(
        "^\\s*#\\s*include\\s*[<\"]obs-module\\.h[>\"]",
        RegexOptions.CultureInvariant | RegexOptions.Multiline,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex ObsIncludePattern();

    [GeneratedRegex(
        @"(?<symbol>(?:obs_[A-Za-z0-9_]+|OBS_[A-Z0-9_]+))\s*\((?<arguments>[^;{}]*)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex InvocationPattern();

    [GeneratedRegex(
        "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex NonCodePattern();
}

public static class ObsNativeIntelligenceProvider
{
    private static readonly Lazy<ObsNativeIntelligenceService> DefaultService =
        new(ObsNativeIntelligenceService.LoadEmbedded);

    public static ObsNativeIntelligenceService Default => DefaultService.Value;
}
