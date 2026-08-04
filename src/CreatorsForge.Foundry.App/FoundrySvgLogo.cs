using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace CreatorsForge.Foundry.App;

internal static partial class FoundrySvgLogo
{
    private static readonly Uri ResourceUri = new(
        "pack://application:,,,/Assets/CreatorsForge.svg",
        UriKind.Absolute);

    public static DrawingImage Load()
    {
        var resource = Application.GetResourceStream(ResourceUri) ??
            throw new InvalidOperationException("The Creator Forge SVG resource is missing.");
        using var stream = resource.Stream;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        });
        var document = XDocument.Load(reader, LoadOptions.None);
        var drawing = new DrawingGroup
        {
            ClipGeometry = new RectangleGeometry(new(0, 0, 512, 512)),
        };

        foreach (var path in document.Descendants().Where(element =>
                     element.Name.LocalName == "path"))
        {
            var data = path.Attribute("d")?.Value;
            var style = path.Attribute("style")?.Value;
            if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(style))
            {
                continue;
            }

            var match = FillColorRegex().Match(style);
            if (!match.Success)
            {
                continue;
            }

            var color = Color.FromRgb(
                byte.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                byte.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                byte.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            drawing.Children.Add(new GeometryDrawing(
                new SolidColorBrush(color),
                null,
                Geometry.Parse(data)));
        }

        if (drawing.Children.Count == 0)
        {
            throw new InvalidOperationException("The Creator Forge SVG contains no usable paths.");
        }

        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    [GeneratedRegex(@"fill:rgb\((\d+),(\d+),(\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex FillColorRegex();
}
