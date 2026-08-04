using System.Xml;
using System.Windows;
using System.Windows.Markup;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace CreatorsForge.Foundry.App;

internal static class FoundrySyntaxHighlighting
{
    private static readonly Lazy<IHighlightingDefinition?> DarkDefinition = new(
        LoadDarkDefinitionSafely,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static IHighlightingDefinition? Dark => DarkDefinition.Value;

    private static IHighlightingDefinition? LoadDarkDefinitionSafely()
    {
        try
        {
            return LoadDarkDefinition();
        }
        catch (Exception exception) when (
            exception is HighlightingDefinitionInvalidException or
                XamlParseException or XmlException or IOException or
                InvalidOperationException)
        {
            return null;
        }
    }

    private static IHighlightingDefinition LoadDarkDefinition()
    {
        var resource = Application.GetResourceStream(new(
            "pack://application:,,,/Assets/Foundry.CFamily.Dark.xshd",
            UriKind.Absolute)) ?? throw new InvalidOperationException(
                "The Foundry dark syntax definition is missing.");
        using var stream = resource.Stream;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        });
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
