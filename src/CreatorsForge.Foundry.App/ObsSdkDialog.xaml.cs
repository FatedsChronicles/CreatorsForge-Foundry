using System.Windows;
using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Build.ObsStudio;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class ObsSdkDialog : Window
{
    private readonly bool allowNetworkAccess;
    private string? archiveDirectory;
    public ObsSdkDialog(bool allowNetworkAccess = false)
    {
        this.allowNetworkAccess = allowNetworkAccess;
        InitializeComponent();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = ObsSdkManager.Inspect();
        StatusText.Text = status.IsReady
            ? $"Ready — OBS SDK {status.Version}"
            : $"Not ready — OBS SDK {status.Version}";
        SdkPathText.Text = status.SdkRoot;
        var product = FoundryProductHealthService.Inspect();
        ProgressTextBox.Text = string.Join(Environment.NewLine, product.Checks
            .Where(item => item.Id is "cmake" or "msvc" or "obs-sdk")
            .Select(item => $"{(item.IsReady ? "READY" : "NEEDS ATTENTION")}  {item.Name}: {item.Details}"));
        InstallButton.Content = status.IsReady ? "Verify SDK" : "Install SDK";
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
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
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsEnabled = false;
            ProgressTextBox.Clear();
            var progress = new Progress<string>(message =>
            {
                ProgressTextBox.AppendText(message + Environment.NewLine);
                ProgressTextBox.ScrollToEnd();
            });
            var status = await ObsSdkManager.InstallAsync(
                archiveDirectory: archiveDirectory,
                progress: progress,
                cancellationToken: CancellationToken.None);
            if (!status.IsReady)
            {
                throw new InvalidOperationException(status.Message);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException or
                System.Net.Http.HttpRequestException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "OBS SDK installation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
            RefreshStatus();
        }
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
