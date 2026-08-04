using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Editor;

public interface ISnippetService
{
    SnippetCatalogue Catalogue { get; }

    IReadOnlyList<SnippetCompletionItem> GetCompletions(
        string source,
        int position,
        string profile);

    SnippetExpansion Expand(
        string snippetId,
        string indentation,
        string newLine);

    GuidedSnippetExpansionResult ExpandGuided(
        string snippetId,
        IReadOnlyDictionary<int, string> values,
        string indentation,
        string newLine);
}

public sealed partial class SnippetService : ISnippetService
{
    private readonly Dictionary<string, SnippetDefinition> snippets;

    public SnippetService(SnippetCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
        snippets = catalogue.Snippets.ToDictionary(
            snippet => snippet.Id,
            StringComparer.Ordinal);
    }

    public SnippetCatalogue Catalogue { get; }

    public static SnippetService LoadEmbedded(CphCatalogue cphCatalogue)
    {
        ArgumentNullException.ThrowIfNull(cphCatalogue);
        var assembly = typeof(SnippetService).Assembly;
        const string resourceName =
            "CreatorsForge.Foundry.Editor.Snippets.streamerbot-builtins-v1.json";
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException(
                $"Embedded snippet catalogue '{resourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var result = SnippetCatalogueLoader.Load(
            reader.ReadToEnd(),
            cphCatalogue);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                "The embedded snippet catalogue is invalid: " +
                string.Join(
                    " ",
                    result.Diagnostics.Select(diagnostic =>
                        $"{diagnostic.Code}: {diagnostic.Message}")));
        }

        return new(result.Catalogue!);
    }

    public IReadOnlyList<SnippetCompletionItem> GetCompletions(
        string source,
        int position,
        string profile)
    {
        ArgumentNullException.ThrowIfNull(source);
        var token = FindPrefixToken(source, position);
        if (token is null)
        {
            return [];
        }

        return Catalogue.Snippets
            .Where(snippet =>
                snippet.Profiles.Contains(profile, StringComparer.Ordinal))
            .SelectMany(snippet => snippet.Prefixes.Select(prefix =>
                (Snippet: snippet, Prefix: prefix)))
            .Where(candidate =>
                candidate.Prefix.StartsWith(
                    token.Value.Text,
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Prefix, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new SnippetCompletionItem(
                candidate.Snippet.Id,
                candidate.Prefix,
                candidate.Snippet.Name,
                candidate.Snippet.Kind,
                candidate.Snippet.Description,
                candidate.Snippet.Source,
                string.Join(", ", candidate.Snippet.Profiles),
                candidate.Snippet.Security,
                token.Value.Start))
            .ToArray();
    }

    public SnippetExpansion Expand(
        string snippetId,
        string indentation,
        string newLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snippetId);
        ArgumentNullException.ThrowIfNull(indentation);
        ArgumentException.ThrowIfNullOrEmpty(newLine);
        if (!snippets.TryGetValue(snippetId, out var snippet))
        {
            throw new ArgumentException(
                $"Snippet '{snippetId}' is not present in catalogue {Catalogue.Revision}.",
                nameof(snippetId));
        }

        var template = string.Join(newLine + indentation, snippet.Body);
        return SnippetExpansionParser.Parse(snippet.Id, template);
    }

    public GuidedSnippetExpansionResult ExpandGuided(
        string snippetId,
        IReadOnlyDictionary<int, string> values,
        string indentation,
        string newLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snippetId);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(indentation);
        ArgumentException.ThrowIfNullOrEmpty(newLine);
        if (!snippets.TryGetValue(snippetId, out var snippet))
        {
            return new(
                null,
                [$"Snippet '{snippetId}' is not present in catalogue {Catalogue.Revision}."]);
        }

        var defaults = SnippetExpansionParser.Parse(
            snippet.Id,
            string.Join("\n", snippet.Body))
            .Placeholders
            .Where(placeholder => placeholder.Index > 0)
            .ToDictionary(
                placeholder => placeholder.Index,
                placeholder => placeholder.DefaultValue);
        var fields = GetGuideFields(snippet, defaults);
        var errors = new List<string>();
        var replacements = new Dictionary<int, string>();
        foreach (var field in fields)
        {
            var rawValue = values.TryGetValue(field.Index, out var supplied)
                ? supplied
                : defaults.GetValueOrDefault(field.Index, string.Empty);
            var error = ValidateGuidedValue(field, rawValue);
            if (error is not null)
            {
                errors.Add($"{field.Label}: {error}");
                continue;
            }

            replacements[field.Index] = EncodeGuidedValue(field, rawValue);
        }

        if (errors.Count != 0)
        {
            return new(null, errors);
        }

        var template = string.Join(newLine + indentation, snippet.Body);
        return new(
            SnippetExpansionParser.Parse(
                snippet.Id,
                template,
                replacements),
            []);
    }

    public SnippetDefinition GetDefinition(string snippetId) =>
        snippets.TryGetValue(snippetId, out var snippet)
            ? snippet
            : throw new ArgumentException(
                $"Snippet '{snippetId}' is not present in catalogue {Catalogue.Revision}.",
                nameof(snippetId));

    public IReadOnlyList<SnippetGuideField> GetGuideFields(string snippetId)
    {
        var snippet = GetDefinition(snippetId);
        var defaults = Expand(snippetId, string.Empty, "\n")
            .Placeholders
            .Where(placeholder => placeholder.Index > 0)
            .ToDictionary(
                placeholder => placeholder.Index,
                placeholder => placeholder.DefaultValue);
        return GetGuideFields(snippet, defaults);
    }

    private static SnippetGuideField[] GetGuideFields(
        SnippetDefinition snippet,
        IReadOnlyDictionary<int, string> defaults)
    {
        if (snippet.Guide?.Fields is { Count: > 0 } fields)
        {
            return fields.OrderBy(field => field.Index).ToArray();
        }

        return defaults
            .OrderBy(item => item.Key)
            .Select(item => new SnippetGuideField(
                item.Key,
                Humanize(item.Value, item.Key),
                "Enter the C# value for this placeholder.",
                "code",
                []))
            .ToArray();
    }

    private static string? ValidateGuidedValue(
        SnippetGuideField field,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "A value is required.";
        }

        if (field.Options.Count != 0 &&
            !field.Options.Contains(value, StringComparer.Ordinal))
        {
            return $"Choose one of: {string.Join(", ", field.Options)}.";
        }

        return field.ValueKind switch
        {
            "identifier" when !IdentifierPattern().IsMatch(value) =>
                "Enter a valid C# identifier.",
            "integer" when !int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out _) =>
                "Enter a whole number.",
            "boolean" when value is not ("true" or "false") =>
                "Choose true or false.",
            _ => null,
        };
    }

    private static string EncodeGuidedValue(
        SnippetGuideField field,
        string value) =>
        field.ValueKind == "string"
            ? value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
            : value.Trim();

    private static string Humanize(string value, int index)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"Value {index}";
        }

        var builder = new StringBuilder(value.Length + 4);
        for (var characterIndex = 0; characterIndex < value.Length; characterIndex++)
        {
            var character = value[characterIndex];
            if (characterIndex > 0 && char.IsUpper(character))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        var result = builder.ToString();
        return char.ToUpperInvariant(result[0]) + result[1..];
    }

    private static (string Text, int Start)? FindPrefixToken(
        string source,
        int position)
    {
        var bounded = Math.Clamp(position, 0, source.Length);
        var start = bounded;
        while (start > 0 && IsPrefixCharacter(source[start - 1]))
        {
            start--;
        }

        var text = source[start..bounded];
        return text.Length >= 2 && PrefixPattern().IsMatch(text)
            ? (text, start)
            : null;
    }

    private static bool IsPrefixCharacter(char character) =>
        char.IsLetterOrDigit(character) ||
        character is '.' or '-' or '_';

    [GeneratedRegex(
        @"^[A-Za-z][A-Za-z0-9]*(?:[._-][A-Za-z0-9]*)*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex PrefixPattern();

    [GeneratedRegex(
        @"^@?[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex IdentifierPattern();
}

public static partial class SnippetCatalogueLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SnippetLoadResult Load(
        string json,
        CphCatalogue cphCatalogue)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(cphCatalogue);
        SnippetCatalogue? catalogue;
        try
        {
            catalogue = JsonSerializer.Deserialize<SnippetCatalogue>(
                json,
                SerializerOptions);
        }
        catch (JsonException exception)
        {
            return new(
                null,
                [Error("CFS5001", $"Snippet JSON is invalid: {exception.Message}")]);
        }

        if (catalogue is null)
        {
            return new(
                null,
                [Error("CFS5001", "Snippet JSON did not contain a catalogue.")]);
        }

        var diagnostics = Validate(catalogue, cphCatalogue);
        return new(catalogue, diagnostics);
    }

    private static FoundryDiagnostic[] Validate(
        SnippetCatalogue catalogue,
        CphCatalogue cphCatalogue)
    {
        var diagnostics = new List<FoundryDiagnostic>();
        if (catalogue.SchemaVersion != 1)
        {
            diagnostics.Add(Error(
                "CFS5001",
                $"Snippet schema {catalogue.SchemaVersion} is not supported."));
        }

        if (string.IsNullOrWhiteSpace(catalogue.Revision))
        {
            diagnostics.Add(Error(
                "CFS5003",
                "The snippet catalogue revision is required."));
        }

        if (catalogue.Snippets is null)
        {
            diagnostics.Add(Error(
                "CFS5003",
                "The snippet catalogue must contain a snippets array."));
            return [.. diagnostics];
        }

        var profileIds = cphCatalogue.Profiles
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.Ordinal);
        var cphMethods = cphCatalogue.Methods.ToDictionary(
            method => method.Name,
            StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snippet in catalogue.Snippets)
        {
            if (snippet is null)
            {
                diagnostics.Add(Error(
                    "CFS5003",
                    "The snippet catalogue contains a null definition."));
                continue;
            }

            ValidateMetadata(snippet, diagnostics);
            if (!ids.Add(snippet.Id ?? string.Empty))
            {
                diagnostics.Add(Error(
                    "CFS5002",
                    $"Snippet id '{snippet.Id}' is duplicated."));
            }

            foreach (var prefix in snippet.Prefixes ?? [])
            {
                if (!prefixes.Add(prefix))
                {
                    diagnostics.Add(Error(
                        "CFS5002",
                        $"Snippet prefix '{prefix}' is duplicated."));
                }
            }

            foreach (var profile in snippet.Profiles ?? [])
            {
                if (!profileIds.Contains(profile))
                {
                    diagnostics.Add(Error(
                        "CFS5003",
                        $"Snippet '{snippet.Id}' declares unknown profile '{profile}'."));
                }
            }

            foreach (var methodName in snippet.RequiredMethods ?? [])
            {
                if (!cphMethods.TryGetValue(methodName, out var method))
                {
                    diagnostics.Add(Error(
                        "CFS5003",
                        $"Snippet '{snippet.Id}' requires unknown CPH method '{methodName}'."));
                    continue;
                }

                foreach (var profile in snippet.Profiles ?? [])
                {
                    if (!method.Overloads.Any(overload =>
                        overload.Profiles.Contains(profile, StringComparer.Ordinal)))
                    {
                        diagnostics.Add(Error(
                            "CFS5003",
                            $"Snippet '{snippet.Id}' requires CPH.{methodName}, which is unavailable for '{profile}'."));
                    }
                }
            }

            try
            {
                var expansion = SnippetExpansionParser.Parse(
                    snippet.Id ?? string.Empty,
                    string.Join("\n", snippet.Body ?? []));
                ValidateGuide(snippet, expansion, diagnostics);
            }
            catch (FormatException exception)
            {
                diagnostics.Add(Error(
                    "CFS5004",
                    $"Snippet '{snippet.Id}' has an invalid body: {exception.Message}"));
            }
        }

        return [.. diagnostics];
    }

    private static void ValidateGuide(
        SnippetDefinition snippet,
        SnippetExpansion expansion,
        List<FoundryDiagnostic> diagnostics)
    {
        if (snippet.Guide is null)
        {
            return;
        }

        if (snippet.Guide.Fields is null or { Count: 0 })
        {
            diagnostics.Add(Error(
                "CFS5003",
                $"Snippet '{snippet.Id}' has an empty guide."));
            return;
        }

        var placeholderIndices = expansion.Placeholders
            .Where(placeholder => placeholder.Index > 0)
            .Select(placeholder => placeholder.Index)
            .ToHashSet();
        var fieldIndices = new HashSet<int>();
        foreach (var field in snippet.Guide.Fields)
        {
            if (field is null ||
                field.Index <= 0 ||
                string.IsNullOrWhiteSpace(field.Label) ||
                string.IsNullOrWhiteSpace(field.Description) ||
                field.ValueKind is not (
                    "string" or
                    "code" or
                    "identifier" or
                    "type" or
                    "boolean" or
                    "integer") ||
                field.Options is null)
            {
                diagnostics.Add(Error(
                    "CFS5003",
                    $"Snippet '{snippet.Id}' has an invalid guide field."));
                continue;
            }

            if (!fieldIndices.Add(field.Index))
            {
                diagnostics.Add(Error(
                    "CFS5003",
                    $"Snippet '{snippet.Id}' has duplicate guide field {field.Index}."));
            }

            if (!placeholderIndices.Contains(field.Index))
            {
                diagnostics.Add(Error(
                    "CFS5003",
                    $"Snippet '{snippet.Id}' guide field {field.Index} has no matching placeholder."));
            }
        }

        if (!placeholderIndices.SetEquals(fieldIndices))
        {
            diagnostics.Add(Error(
                "CFS5003",
                $"Snippet '{snippet.Id}' guide must describe every numbered placeholder."));
        }
    }

    private static void ValidateMetadata(
        SnippetDefinition snippet,
        List<FoundryDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(snippet.Id) ||
            !IdPattern().IsMatch(snippet.Id))
        {
            diagnostics.Add(Error(
                "CFS5003",
                $"Snippet id '{snippet.Id}' is invalid."));
        }

        if (string.IsNullOrWhiteSpace(snippet.Name) ||
            string.IsNullOrWhiteSpace(snippet.Description) ||
            string.IsNullOrWhiteSpace(snippet.Author) ||
            !VersionPattern().IsMatch(snippet.Version ?? string.Empty) ||
            !string.Equals(snippet.Target, "streamerbot", StringComparison.Ordinal) ||
            !string.Equals(snippet.Language, "csharp", StringComparison.Ordinal) ||
            snippet.Kind is not ("method" or "workflow") ||
            snippet.Source is not ("built-in" or "project" or "user" or "community") ||
            snippet.Security is null ||
            snippet.Prefixes is null or { Count: 0 } ||
            snippet.Profiles is null or { Count: 0 } ||
            snippet.Categories is null or { Count: 0 } ||
            snippet.Body is null or { Count: 0 })
        {
            diagnostics.Add(Error(
                "CFS5003",
                $"Snippet '{snippet.Id}' has incomplete or invalid metadata."));
        }

        foreach (var prefix in snippet.Prefixes ?? [])
        {
            if (!PrefixPattern().IsMatch(prefix))
            {
                diagnostics.Add(Error(
                    "CFS5003",
                    $"Snippet '{snippet.Id}' has invalid prefix '{prefix}'."));
            }
        }
    }

    private static FoundryDiagnostic Error(string code, string message) =>
        new(code, FoundryDiagnosticSeverity.Error, message);

    [GeneratedRegex(
        @"^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex IdPattern();

    [GeneratedRegex(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(
        @"^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex PrefixPattern();
}

internal static class SnippetExpansionParser
{
    public static SnippetExpansion Parse(
        string snippetId,
        string template,
        IReadOnlyDictionary<int, string>? replacements = null)
    {
        var output = new StringBuilder(template.Length);
        var placeholders = new List<SnippetPlaceholder>();
        for (var index = 0; index < template.Length;)
        {
            if (template[index] != '$')
            {
                output.Append(template[index++]);
                continue;
            }

            if (index + 1 < template.Length && template[index + 1] == '0')
            {
                placeholders.Add(new(0, output.Length, 0, string.Empty));
                index += 2;
                continue;
            }

            if (index + 1 >= template.Length || template[index + 1] != '{')
            {
                output.Append(template[index++]);
                continue;
            }

            var close = template.IndexOf('}', index + 2);
            if (close < 0)
            {
                throw new FormatException("A placeholder is missing its closing brace.");
            }

            var marker = template[(index + 2)..close];
            var separator = marker.IndexOf(':');
            if (separator <= 0 ||
                !int.TryParse(marker[..separator], out var placeholderIndex) ||
                placeholderIndex <= 0)
            {
                throw new FormatException(
                    $"Placeholder '${{{marker}}}' must use '${{positiveIndex:defaultText}}'.");
            }

            var defaultValue = marker[(separator + 1)..];
            var expandedValue = replacements is not null &&
                replacements.TryGetValue(placeholderIndex, out var replacement)
                ? replacement
                : defaultValue;
            placeholders.Add(new(
                placeholderIndex,
                output.Length,
                expandedValue.Length,
                expandedValue));
            output.Append(expandedValue);
            index = close + 1;
        }

        var ordered = placeholders
            .OrderBy(placeholder => placeholder.Index == 0 ? int.MaxValue : placeholder.Index)
            .ThenBy(placeholder => placeholder.Offset)
            .ToArray();
        var duplicate = ordered
            .GroupBy(placeholder => placeholder.Index)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new FormatException(
                $"Placeholder index {duplicate.Key} is declared more than once.");
        }

        return new(snippetId, output.ToString(), ordered);
    }
}

public static class SnippetProvider
{
    private static readonly object Sync = new();
    private static SnippetService current =
        SnippetService.LoadEmbedded(CphIntelligenceProvider.Default.Catalogue);

    public static SnippetService Default
    {
        get { lock (Sync) return current; }
    }

    public static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Creators Forge",
        "Foundry",
        "snippets");

    public static SnippetLibraryLoadResult Reload(
        string userDirectory,
        string? projectRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        var cph = CphIntelligenceProvider.Default.Catalogue;
        var builtIn = SnippetService.LoadEmbedded(cph).Catalogue;
        var diagnostics = new List<FoundryDiagnostic>();
        var loadedFiles = new List<string>();
        var definitions = new List<SnippetDefinition>(builtIn.Snippets);
        var ids = definitions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var prefixes = definitions.SelectMany(item => item.Prefixes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = new[]
        {
            userDirectory,
            projectRoot is null ? null : Path.Combine(projectRoot, ".foundry", "snippets"),
        };

        foreach (var directory in directories.Where(item => item is not null).Cast<string>())
        {
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var result = SnippetCatalogueLoader.Load(File.ReadAllText(file), cph);
                    if (!result.IsSuccess)
                    {
                        diagnostics.AddRange(result.Diagnostics.Select(item => item with
                        {
                            Location = new FoundryDiagnosticLocation(file),
                        }));
                        continue;
                    }

                    if (result.Catalogue!.Snippets.Any(snippet => snippet.Source == "built-in"))
                    {
                        diagnostics.Add(new(
                            "CFS5008",
                            FoundryDiagnosticSeverity.Error,
                            $"External catalogue '{Path.GetFileName(file)}' cannot claim built-in provenance.",
                            new FoundryDiagnosticLocation(file)));
                        continue;
                    }

                    var conflict = result.Catalogue.Snippets.FirstOrDefault(snippet =>
                        ids.Contains(snippet.Id) || snippet.Prefixes.Any(prefixes.Contains));
                    if (conflict is not null)
                    {
                        diagnostics.Add(new(
                            "CFS5005",
                            FoundryDiagnosticSeverity.Error,
                            $"Catalogue '{Path.GetFileName(file)}' conflicts at snippet '{conflict.Id}' or one of its prefixes.",
                            new FoundryDiagnosticLocation(file),
                            "Give user and project snippets unique IDs and prefixes."));
                        continue;
                    }

                    definitions.AddRange(result.Catalogue.Snippets);
                    foreach (var snippet in result.Catalogue.Snippets)
                    {
                        ids.Add(snippet.Id);
                        foreach (var prefix in snippet.Prefixes) prefixes.Add(prefix);
                    }
                    loadedFiles.Add(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new("CFS5006", FoundryDiagnosticSeverity.Error, $"Snippet catalogue could not be read: {exception.Message}", new FoundryDiagnosticLocation(file)));
                }
            }
        }

        var service = new SnippetService(new(1, $"{builtIn.Revision}+external.{loadedFiles.Count}", definitions));
        lock (Sync) current = service;
        return new(service, diagnostics, loadedFiles);
    }

    public static async Task<SnippetLibraryLoadResult> ImportUserCatalogueAsync(
        string sourcePath,
        string userDirectory,
        string? projectRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        Directory.CreateDirectory(userDirectory);
        var fileName = Path.GetFileName(sourcePath);
        var destination = Path.Combine(userDirectory, fileName);
        if (File.Exists(destination))
        {
            return new(Default, [new("CFS5007", FoundryDiagnosticSeverity.Error, $"A user catalogue named '{fileName}' already exists.", new FoundryDiagnosticLocation(destination))], []);
        }

        var json = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var validation = SnippetCatalogueLoader.Load(json, CphIntelligenceProvider.Default.Catalogue);
        if (!validation.IsSuccess)
        {
            return new(Default, validation.Diagnostics, []);
        }

        var existingIds = Default.Catalogue.Snippets.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var existingPrefixes = Default.Catalogue.Snippets.SelectMany(item => item.Prefixes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflict = validation.Catalogue!.Snippets.FirstOrDefault(item =>
            item.Source == "built-in" || existingIds.Contains(item.Id) || item.Prefixes.Any(existingPrefixes.Contains));
        if (conflict is not null)
        {
            return new(Default, [new("CFS5005", FoundryDiagnosticSeverity.Error, $"Snippet '{conflict.Id}' conflicts with the active library or claims built-in provenance.", new FoundryDiagnosticLocation(sourcePath))], []);
        }

        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return Reload(userDirectory, projectRoot);
    }
}
