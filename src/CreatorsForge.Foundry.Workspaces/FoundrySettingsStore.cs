using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorsForge.Foundry.Core.Diagnostics;

namespace CreatorsForge.Foundry.Workspaces;

public sealed class FoundrySettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string settingsPath;

    public FoundrySettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = settingsPath;
    }

    public async Task<StateLoadResult<FoundryUserSettings>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return new(FoundryUserSettings.CreateDefault(), []);
        }

        try
        {
            var json = await File.ReadAllTextAsync(
                settingsPath,
                cancellationToken).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<FoundryUserSettings>(
                json,
                SerializerOptions);
            return settings is null
                ? Invalid("The settings file is empty.")
                : new(Normalize(settings), []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Invalid($"Settings could not be loaded: {exception.Message}");
        }
    }

    public Task SaveAsync(
        FoundryUserSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = JsonSerializer.Serialize(Normalize(settings), SerializerOptions);
        return AtomicFile.WriteTextAsync(
            settingsPath,
            $"{json}\n",
            cancellationToken);
    }

    private static FoundryUserSettings Normalize(FoundryUserSettings settings)
    {
        var defaults = FoundryUserSettings.CreateDefault();
        var layout = settings.Layout ?? ShellLayout.Default;
        return settings with
        {
            DefaultProjectDirectory = string.IsNullOrWhiteSpace(
                settings.DefaultProjectDirectory)
                ? defaults.DefaultProjectDirectory
                : settings.DefaultProjectDirectory,
            AutosaveSeconds = Math.Clamp(settings.AutosaveSeconds, 10, 600),
            StreamerBotInstallations = (settings.StreamerBotInstallations ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray(),
            ObsInstallations = (settings.ObsInstallations ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToArray(),
            UpdateManifestLocation = string.IsNullOrWhiteSpace(settings.UpdateManifestLocation)
                ? null
                : settings.UpdateManifestLocation.Trim(),
            Theme = Enum.IsDefined(settings.Theme)
                ? settings.Theme
                : FoundryThemePreference.System,
            UpdateChannel = Enum.IsDefined(settings.UpdateChannel)
                ? settings.UpdateChannel
                : FoundryUpdateChannel.Stable,
            Layout = layout with
            {
                WindowWidth = Math.Clamp(layout.WindowWidth, 900, 7680),
                WindowHeight = Math.Clamp(layout.WindowHeight, 600, 4320),
                ProjectPanelWidth = Math.Clamp(layout.ProjectPanelWidth, 180, 800),
                InspectorPanelWidth = Math.Clamp(layout.InspectorPanelWidth, 180, 800),
                BottomPanelHeight = Math.Clamp(layout.BottomPanelHeight, 120, 600),
            },
        };
    }

    private static StateLoadResult<FoundryUserSettings> Invalid(string message) => new(
        FoundryUserSettings.CreateDefault(),
        [new(
            "CFW2101",
            FoundryDiagnosticSeverity.Warning,
            message,
            SuggestedFix: "Review or reset the application settings.")]);
}
