using System.Globalization;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.Workspaces.Tests;

public sealed class ThemeAccessibilityTests
{
    [Fact]
    public async Task ThemePreferenceRoundTripsAsAReadableName()
    {
        using var temporary = TestDirectory.Create();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        var store = new FoundrySettingsStore(settingsPath);
        var settings = FoundryUserSettings.CreateDefault() with
        {
            Theme = FoundryThemePreference.Light,
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var json = await File.ReadAllTextAsync(settingsPath, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Contains("\"theme\": \"Light\"", json, StringComparison.Ordinal);
        Assert.Equal(FoundryThemePreference.Light, loaded.Value.Theme);
        Assert.Empty(loaded.Diagnostics);
    }

    [Fact]
    public async Task MissingOrUnknownThemeFallsBackToSystem()
    {
        using var temporary = TestDirectory.Create();
        var settingsPath = Path.Combine(temporary.Path, "settings.json");
        var store = new FoundrySettingsStore(settingsPath);
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "defaultProjectDirectory": "C:\\Foundry",
              "autosaveSeconds": 30,
              "layout": {},
              "theme": 99
            }
            """,
            CancellationToken.None);

        var unknown = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(FoundryThemePreference.System, unknown.Value.Theme);

        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "defaultProjectDirectory": "C:\\Foundry",
              "autosaveSeconds": 30,
              "layout": {}
            }
            """,
            CancellationToken.None);

        var missing = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(FoundryThemePreference.System, missing.Value.Theme);
    }

    [Fact]
    public void FixedThemeTextColoursMeetWcagNormalTextContrast()
    {
        foreach (var palette in new[]
                 {
                     FoundryThemePalettes.Dark,
                     FoundryThemePalettes.Light,
                 })
        {
            AssertContrast(palette.Text, palette.Window);
            AssertContrast(palette.Text, palette.Panel);
            AssertContrast(palette.Text, palette.Editor);
            AssertContrast(palette.Text, palette.Button);
            AssertContrast(palette.Text, palette.MenuSelection);
            AssertContrast(palette.MutedText, palette.Panel);
            AssertContrast(palette.MutedText, palette.MenuSelection);
            AssertContrast(palette.Accent, palette.Panel);
            AssertContrast(palette.Error, palette.Panel);
        }
    }

    private static void AssertContrast(string foreground, string background)
    {
        var ratio = ContrastRatio(foreground, background);
        Assert.True(
            ratio >= 4.5,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{foreground} on {background} has a contrast ratio of {ratio:F2}:1."));
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
            (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        var red = Channel(color, 1);
        var green = Channel(color, 3);
        var blue = Channel(color, 5);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double Channel(string color, int offset)
    {
        var value = int.Parse(
            color.AsSpan(offset, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture) / 255d;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private sealed class TestDirectory : IDisposable
    {
        private TestDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TestDirectory Create() => new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FoundryThemeTests",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
