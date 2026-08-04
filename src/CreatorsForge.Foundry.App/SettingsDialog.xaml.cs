using System.Globalization;
using System.Windows;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class SettingsDialog : Window
{
    private readonly FoundryUserSettings originalSettings;

    public SettingsDialog(FoundryUserSettings settings)
    {
        originalSettings = settings;
        InitializeComponent();
        ProjectDirectoryTextBox.Text = settings.DefaultProjectDirectory;
        AutosaveTextBox.Text = settings.AutosaveSeconds.ToString(
            CultureInfo.InvariantCulture);
        UpdateLocationTextBox.Text = settings.UpdateManifestLocation;
        UpdateChannelComboBox.SelectedIndex = settings.UpdateChannel == FoundryUpdateChannel.Prerelease ? 1 : 0;
        NetworkAccessCheckBox.IsChecked = settings.AllowNetworkAccess;
        IncludePathsCheckBox.IsChecked = settings.IncludePathsInDiagnosticBundles;
        ThemeComboBox.SelectedIndex = settings.Theme switch
        {
            FoundryThemePreference.Dark => 1,
            FoundryThemePreference.Light => 2,
            _ => 0,
        };
    }

    public FoundryUserSettings? Settings { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(ProjectDirectoryTextBox.Text)
                ? ProjectDirectoryTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false,
            Title = "Select default project folder",
        };
        if (dialog.ShowDialog(this) == true)
        {
            ProjectDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectDirectoryTextBox.Text) ||
            !int.TryParse(
                AutosaveTextBox.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var autosaveSeconds) ||
            autosaveSeconds is < 10 or > 600)
        {
            MessageBox.Show(
                this,
                "Enter a project folder and an autosave interval from 10 to 600 seconds.",
                "Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Settings = originalSettings with
        {
            DefaultProjectDirectory = ProjectDirectoryTextBox.Text.Trim(),
            AutosaveSeconds = autosaveSeconds,
            UpdateManifestLocation = string.IsNullOrWhiteSpace(UpdateLocationTextBox.Text) ? null : UpdateLocationTextBox.Text.Trim(),
            UpdateChannel = UpdateChannelComboBox.SelectedIndex == 1
                ? FoundryUpdateChannel.Prerelease
                : FoundryUpdateChannel.Stable,
            AllowNetworkAccess = NetworkAccessCheckBox.IsChecked == true,
            IncludePathsInDiagnosticBundles = IncludePathsCheckBox.IsChecked == true,
            Theme = ThemeComboBox.SelectedIndex switch
            {
                1 => FoundryThemePreference.Dark,
                2 => FoundryThemePreference.Light,
                _ => FoundryThemePreference.System,
            },
        };
        DialogResult = true;
    }
}
