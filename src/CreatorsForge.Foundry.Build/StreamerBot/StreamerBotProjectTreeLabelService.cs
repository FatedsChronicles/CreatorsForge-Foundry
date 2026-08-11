namespace CreatorsForge.Foundry.Build.StreamerBot;

/// <summary>
/// Creates display-only Solution Explorer labels for imported C# sources.
/// Paths remain stable and are never renamed from user-facing metadata.
/// </summary>
public static class StreamerBotProjectTreeLabelService
{
    public static IReadOnlyDictionary<string, string> Create(StreamerBotDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in definition.Actions)
        {
            var sources = action.SubActions
                .Select((item, index) => (Item: item, Index: index))
                .Where(value => !string.IsNullOrWhiteSpace(value.Item.SourcePath))
                .ToArray();
            foreach (var source in sources)
            {
                var relativePath = Normalize(source.Item.SourcePath!);
                var directory = Normalize(Path.GetDirectoryName(relativePath) ?? string.Empty);
                if (directory.Length > 0) labels[directory] = action.Name;
                var operationName = source.Item.Kind switch
                {
                    "executeCSharp" => "Execute C# Code",
                    "executeBridge" => "Execute Bridge",
                    _ => SplitIdentifier(source.Item.Kind),
                };
                labels[relativePath] = $"{source.Index + 1:D2} - {operationName}{Path.GetExtension(relativePath)}";
            }
        }
        return labels;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private static string SplitIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Sub-action";
        var characters = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]) && char.IsLower(value[index - 1])) characters.Add(' ');
            characters.Add(index == 0 ? char.ToUpperInvariant(value[index]) : value[index]);
        }
        return new string(characters.ToArray());
    }
}
