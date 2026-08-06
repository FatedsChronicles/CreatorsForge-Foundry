using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.PreviewHost;

internal sealed record PreviewProviderAdapterResult(
    string AdapterId,
    string DisplayName,
    IReadOnlyList<PreviewRuntimeElement> Elements,
    IReadOnlyList<string> Logs);

internal static class PreviewProviderAdapterRegistry
{
    private const int MaximumElements = 48;

    public static PreviewProviderAdapterResult Render(PreviewDesignSurface surface) =>
        surface.Adapter?.Id switch
        {
            PreviewAdapterIds.StaticWeb => RenderStaticWeb(surface),
            PreviewAdapterIds.WinForms => RenderWinForms(surface),
            PreviewAdapterIds.ObsComponent => RenderObs(surface),
            _ => RenderGeneric(surface),
        };

    private static PreviewProviderAdapterResult RenderStaticWeb(PreviewDesignSurface surface)
    {
        var title = Metadata(surface, "documentTitle", "Untitled preview");
        var elements = new List<PreviewRuntimeElement>
        {
            Element("browser-chrome", "safe-browser", title, "browser-chrome", 0, 0, surface.ViewportWidth, 48, surface),
            Element("security-badge", "scripts-blocked", "Scripts blocked", "badge", Math.Max(8, surface.ViewportWidth - 158), 9, 146, 30, surface),
        };
        elements.AddRange(surface.Elements.Select(element => Transform(
            element,
            Role(element.Kind),
            element.X,
            element.Y + 56,
            element.Width,
            Math.Min(element.Height, Math.Max(20, surface.ViewportHeight - element.Y - 64)),
            surface)));
        return Result(
            surface,
            PreviewAdapterIds.StaticWeb,
            "Static web - safe document",
            elements,
            "Applied browser-like chrome and semantic web control styling with scripts and external navigation disabled.");
    }

    private static PreviewProviderAdapterResult RenderWinForms(PreviewDesignSurface surface)
    {
        var title = Metadata(surface, "windowTitle", "Windows form");
        var elements = new List<PreviewRuntimeElement>
        {
            Element("form-surface", "form-client", string.Empty, "form-surface", 12, 42, surface.ViewportWidth - 24, surface.ViewportHeight - 54, surface),
            Element("form-title", "form-title", title, "form-chrome", 12, 10, surface.ViewportWidth - 24, 34, surface),
        };
        elements.AddRange(surface.Elements.Select(element => Transform(
            element,
            Role(element.Kind),
            element.X + 12,
            element.Y + 42,
            element.Width,
            element.Height,
            surface)));
        return Result(
            surface,
            PreviewAdapterIds.WinForms,
            "WinForms - isolated design model",
            elements,
            "Applied Windows form chrome and native-control roles without loading the managed assembly.");
    }

    private static PreviewProviderAdapterResult RenderObs(PreviewDesignSurface surface)
    {
        var component = Metadata(surface, "componentName", "OBS component");
        var canvasWidth = Math.Max(180, surface.ViewportWidth * 0.68);
        var propertiesX = canvasWidth + 24;
        var propertiesWidth = Math.Max(160, surface.ViewportWidth - propertiesX - 16);
        var contentHeight = Math.Max(100, surface.ViewportHeight - 72);
        var elements = new List<PreviewRuntimeElement>
        {
            Element("obs-toolbar", "obs-toolbar", $"OBS Preview - {component}", "obs-chrome", 0, 0, surface.ViewportWidth, 44, surface),
            Element("obs-preview", "obs-program", "Program canvas", "obs-preview", 16, 56, canvasWidth, contentHeight, surface),
            Element("obs-properties", "obs-properties", "Properties", "obs-properties", propertiesX, 56, propertiesWidth, contentHeight, surface),
        };
        elements.AddRange(surface.Elements.Select(element => Transform(
            element,
            element.Kind == "obs-template" ? "badge" : "canvas",
            24 + element.X / surface.ViewportWidth * Math.Max(120, canvasWidth - 32),
            64 + element.Y / surface.ViewportHeight * Math.Max(80, contentHeight - 24),
            element.Width / surface.ViewportWidth * Math.Max(120, canvasWidth - 32),
            element.Height / surface.ViewportHeight * Math.Max(80, contentHeight - 24),
            surface)));
        return Result(
            surface,
            PreviewAdapterIds.ObsComponent,
            "OBS Studio - component model",
            elements,
            "Applied OBS program-canvas and properties-panel composition without loading libobs or the plugin DLL.");
    }

    private static PreviewProviderAdapterResult RenderGeneric(PreviewDesignSurface surface)
    {
        var elements = surface.Elements
            .Select(element => Transform(
                element,
                Role(element.Kind),
                element.X,
                element.Y,
                element.Width,
                element.Height,
                surface))
            .ToArray();
        return Result(
            surface,
            PreviewAdapterIds.Generic,
            "Generic isolated renderer",
            elements,
            "No provider-specific adapter descriptor was supplied; used the generic bounded renderer.");
    }

    private static PreviewProviderAdapterResult Result(
        PreviewDesignSurface surface,
        string adapterId,
        string displayName,
        IEnumerable<PreviewRuntimeElement> elements,
        string log) => new(
            adapterId,
            displayName,
            elements.Take(MaximumElements).ToArray(),
            [$"Adapter input kind: {surface.Kind}.", log]);

    private static PreviewRuntimeElement Transform(
        PreviewDesignElement element,
        string role,
        double x,
        double y,
        double width,
        double height,
        PreviewDesignSurface surface) =>
        Element(element.Kind, element.Name, element.Label, role, x, y, width, height, surface);

    private static PreviewRuntimeElement Element(
        string kind,
        string name,
        string label,
        string role,
        double x,
        double y,
        double width,
        double height,
        PreviewDesignSurface surface) => new(
            kind,
            name,
            label,
            role,
            Math.Clamp(x, 0, surface.ViewportWidth - 20),
            Math.Clamp(y, 0, surface.ViewportHeight - 20),
            Math.Clamp(width, 20, surface.ViewportWidth),
            Math.Clamp(height, 20, surface.ViewportHeight));

    private static string Metadata(PreviewDesignSurface surface, string key, string fallback) =>
        surface.Adapter?.Metadata.TryGetValue(key, out var value) == true &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    private static string Role(string kind) => kind.ToLowerInvariant() switch
    {
        "button" => "action",
        "input" or "textbox" or "richtextbox" or "combobox" or "listbox" or "checkbox" => "input",
        "header" or "nav" or "footer" => "chrome",
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "label" => "heading",
        "img" or "picturebox" => "media",
        "obs-canvas" => "canvas",
        "obs-template" => "badge",
        _ => "panel",
    };
}
