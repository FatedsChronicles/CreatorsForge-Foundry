using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Build.ObsStudio;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class ObsSdkDialog : Window
{
    private readonly FoundryUserSettings originalSettings;
    private readonly bool allowNetworkAccess;
    private readonly List<VisualStudioToolchain> toolchains = [];
    private string? archiveDirectory;

    public ObsSdkDialog(FoundryUserSettings settings)
    {
        originalSettings = settings;
        allowNetworkAccess = settings.AllowNetworkAccess;
        InitializeComponent();
        LoadDiscoveredToolchains(settings.VisualStudioInstallationRoot);
        RefreshStatus();
    }

    public FoundryUserSettings? UpdatedSettings { get; private set; }

    private VisualStudioToolchain? SelectedToolchain =>
        VisualStudioComboBox.SelectedItem as VisualStudioToolchain;

    private void LoadDiscoveredToolchains(string? preferredRoot)
    {
        toolchains.Clear();
        toolchains.AddRange(VisualStudioToolchainService.Discover());
        if (!string.IsNullOrWhiteSpace(preferredRoot) &&
            toolchains.All(item => !string.Equals(item.InstallationRoot, preferredRoot, StringComparison.OrdinalIgnoreCase)))
        {
            toolchains.Add(VisualStudioToolchainService.InspectInstallation(preferredRoot));
        }

        VisualStudioComboBox.ItemsSource = null;
        VisualStudioComboBox.ItemsSource = toolchains;
        VisualStudioComboBox.SelectedItem = toolchains.FirstOrDefault(item =>
            string.Equals(item.InstallationRoot, preferredRoot, StringComparison.OrdinalIgnoreCase)) ??
            toolchains.FirstOrDefault(item => item.IsReady);
    }

    private void RefreshStatus()
    {
        var selected = SelectedToolchain;
        VisualStudioStatusText.Text = selected is null
            ? "NEEDS ATTENTION — No compatible Visual Studio C++ installation was detected."
            : selected.IsReady
                ? $"READY — {selected.Summary}"
                : $"NEEDS ATTENTION — {selected.Summary}";

        var status = ObsSdkManager.Inspect();
        StatusText.Text = status.IsReady
            ? $"Ready — OBS SDK {status.Version}"
            : $"Not ready — OBS SDK {status.Version}";
        SdkPathText.Text = status.SdkRoot;
        var product = FoundryProductHealthService.Inspect(
            visualStudioInstallationRoot: selected?.InstallationRoot);
        ProgressTextBox.Text = string.Join(Environment.NewLine, product.Checks
            .Where(item => item.Id is "cmake" or "msvc" or "obs-sdk")
            .Select(item => $"{(item.IsReady ? "READY" : "NEEDS ATTENTION")}  {item.Name}: {item.Details}"));
        InstallButton.Content = status.IsReady ? "Verify SDK" : "Install SDK";
    }

    private void VisualStudio_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshStatus();

    private void AutoDetect_Click(object sender, RoutedEventArgs e)
    {
        LoadDiscoveredToolchains(null);
        if (SelectedToolchain is null)
        {
            MessageBox.Show(
                this,
                "No Visual Studio installation containing Desktop development with C++ was detected. Select its installation root manually or add the C++ workload in Visual Studio Installer.",
                "Visual Studio C++ tools not found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        RefreshStatus();
    }

    private void SelectVisualStudio_Click(object sender, RoutedEventArgs e)
    {
        var selectedRoot = SelectedToolchain?.InstallationRoot;
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(selectedRoot)
                ? selectedRoot
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Multiselect = false,
            Title = "Select the Visual Studio installation root",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var inspected = VisualStudioToolchainService.InspectInstallation(dialog.FolderName);
        if (!inspected.IsReady)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, inspected.Problems),
                "Visual Studio C++ tools are incomplete",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var existing = toolchains.FirstOrDefault(item =>
            string.Equals(item.InstallationRoot, inspected.InstallationRoot, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            toolchains.Add(inspected);
            VisualStudioComboBox.ItemsSource = null;
            VisualStudioComboBox.ItemsSource = toolchains;
            existing = inspected;
        }
        VisualStudioComboBox.SelectedItem = existing;
        RefreshStatus();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedToolchain?.IsReady != true)
        {
            MessageBox.Show(this, "Select a ready Visual Studio C++ x64 toolchain first.", "Toolchain required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (archiveDirectory is null && !allowNetworkAccess && !ObsSdkManager.Inspect().IsReady)
        {
            MessageBox.Show(this, "Network access is disabled. Enable it in Settings, or choose Use Offline Archives.", "Network access disabled", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var decision = MessageBox.Show(
            this,
            archiveDirectory is null
                ? "Foundry will download approximately 170 MB from the official OBS Studio 32.1.2 GitHub release, verify both SHA-256 checksums, and install the SDK locally. Continue?"
                : $"Foundry will use and verify the official OBS archives in:\n{archiveDirectory}\n\nContinue?",
            "Install pinned OBS SDK",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes) return;

        try
        {
            IsEnabled = false;
            ProgressTextBox.Clear();
            var progress = new Progress<string>(message =>
            {
                ProgressTextBox.AppendText(message + Environment.NewLine);
                ProgressTextBox.ScrollToEnd();
            });
            var status = await ObsSdkManager.InstallWithToolchainAsync(
                archiveDirectory: archiveDirectory,
                progress: progress,
                visualStudioInstallationRoot: SelectedToolchain.InstallationRoot,
                cancellationToken: CancellationToken.None);
            if (!status.IsReady) throw new InvalidOperationException(status.Message);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            MessageBox.Show(this, exception.Message, "OBS SDK installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            RefreshStatus();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedToolchain?.IsReady != true)
        {
            MessageBox.Show(this, "Select a valid Visual Studio C++ x64 installation before saving.", "Development toolchain", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        UpdatedSettings = originalSettings with
        {
            VisualStudioInstallationRoot = SelectedToolchain.InstallationRoot,
        };
        DialogResult = true;
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(ObsSdkManager.GetSdkRoot());

    private void OfflineArchives_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select folder containing both official OBS 32.1.2 archives", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        archiveDirectory = dialog.FolderName;
        ProgressTextBox.Text = $"Offline mode selected. Expected files:\n{ObsSdkManager.SourceArchiveName}\n{ObsSdkManager.WindowsArchiveName}";
    }
}
