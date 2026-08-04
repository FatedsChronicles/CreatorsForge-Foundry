using System.Windows;
using System.Windows.Media;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

internal static class FoundryThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static FoundryThemePreference Resolve(
        FoundryThemePreference preference) => preference switch
    {
        FoundryThemePreference.Dark => FoundryThemePreference.Dark,
        FoundryThemePreference.Light => FoundryThemePreference.Light,
        _ => IsWindowsLightTheme()
            ? FoundryThemePreference.Light
            : FoundryThemePreference.Dark,
    };

    public static void Apply(
        ResourceDictionary resources,
        FoundryThemePreference preference,
        bool highContrast)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (highContrast)
        {
            ApplyHighContrast(resources);
            return;
        }

        var palette = Resolve(preference) == FoundryThemePreference.Light
            ? FoundryThemePalettes.Light
            : FoundryThemePalettes.Dark;
        Set(resources, "WindowBackgroundBrush", palette.Window);
        Set(resources, "PanelBackgroundBrush", palette.Panel);
        Set(resources, "PanelBorderBrush", palette.Border);
        Set(resources, "TextBrush", palette.Text);
        Set(resources, "MutedTextBrush", palette.MutedText);
        Set(resources, "AccentBrush", palette.Accent);
        Set(resources, "EditorBackgroundBrush", palette.Editor);
        Set(resources, "MenuSelectionBrush", palette.MenuSelection);
        Set(resources, "ButtonBackgroundBrush", palette.Button);
        Set(resources, "ErrorTextBrush", palette.Error);
        SetSystemBrushes(resources, palette);
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static void ApplyHighContrast(ResourceDictionary resources)
    {
        resources["WindowBackgroundBrush"] = SystemColors.WindowBrush;
        resources["PanelBackgroundBrush"] = SystemColors.WindowBrush;
        resources["PanelBorderBrush"] = SystemColors.WindowTextBrush;
        resources["TextBrush"] = SystemColors.WindowTextBrush;
        resources["MutedTextBrush"] = SystemColors.GrayTextBrush;
        resources["AccentBrush"] = SystemColors.HighlightBrush;
        resources["EditorBackgroundBrush"] = SystemColors.WindowBrush;
        resources["MenuSelectionBrush"] = SystemColors.HighlightBrush;
        resources["ButtonBackgroundBrush"] = SystemColors.ControlBrush;
        resources["ErrorTextBrush"] = SystemColors.WindowTextBrush;
        resources[SystemColors.MenuBrushKey] = SystemColors.MenuBrush;
        resources[SystemColors.MenuBarBrushKey] = SystemColors.MenuBarBrush;
        resources[SystemColors.MenuTextBrushKey] = SystemColors.MenuTextBrush;
        resources[SystemColors.MenuHighlightBrushKey] = SystemColors.HighlightBrush;
        resources[SystemColors.HighlightBrushKey] = SystemColors.HighlightBrush;
        resources[SystemColors.HighlightTextBrushKey] = SystemColors.HighlightTextBrush;
        resources[SystemColors.HotTrackBrushKey] = SystemColors.HotTrackBrush;
        resources[SystemColors.ControlBrushKey] = SystemColors.ControlBrush;
        resources[SystemColors.ControlTextBrushKey] = SystemColors.ControlTextBrush;
        resources[SystemColors.WindowBrushKey] = SystemColors.WindowBrush;
        resources[SystemColors.WindowTextBrushKey] = SystemColors.WindowTextBrush;
        resources[SystemColors.InactiveSelectionHighlightBrushKey] =
            SystemColors.InactiveSelectionHighlightBrush;
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] =
            SystemColors.InactiveSelectionHighlightTextBrush;
    }

    private static void SetSystemBrushes(
        ResourceDictionary resources,
        FoundryThemePalette palette)
    {
        resources[SystemColors.MenuBrushKey] = Brush(palette.Panel);
        resources[SystemColors.MenuBarBrushKey] = Brush(palette.Panel);
        resources[SystemColors.MenuTextBrushKey] = Brush(palette.Text);
        resources[SystemColors.MenuHighlightBrushKey] = Brush(palette.MenuSelection);
        resources[SystemColors.HighlightBrushKey] = Brush(palette.MenuSelection);
        resources[SystemColors.HighlightTextBrushKey] = Brush(palette.Text);
        resources[SystemColors.HotTrackBrushKey] = Brush(palette.Accent);
        resources[SystemColors.ControlBrushKey] = Brush(palette.Button);
        resources[SystemColors.ControlTextBrushKey] = Brush(palette.Text);
        resources[SystemColors.WindowBrushKey] = Brush(palette.Panel);
        resources[SystemColors.WindowTextBrushKey] = Brush(palette.Text);
        resources[SystemColors.InactiveSelectionHighlightBrushKey] =
            Brush(palette.MenuSelection);
        resources[SystemColors.InactiveSelectionHighlightTextBrushKey] =
            Brush(palette.Text);
    }

    private static void Set(
        ResourceDictionary resources,
        string key,
        string color) => resources[key] = Brush(color);

    private static SolidColorBrush Brush(string color) => new(
        (Color)ColorConverter.ConvertFromString(color));

}
