using System.Reflection;
using System.Windows;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class UpdateDialog : Window
{
    private readonly FoundryUserSettings settings;
    private readonly string stateRoot;
    private FoundryUpdateManifest? update;
    private string? stagedInstallerPath;
    public UpdateDialog(FoundryUserSettings settings, string stateRoot) { this.settings = settings; this.stateRoot = stateRoot; InitializeComponent(); OutputText.Text = "No network request has been made."; }
    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        IsEnabled = false;
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var current = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+', 2)[0]
                ?? assembly.GetName().Version?.ToString(3)
                ?? "0.0.0";
            var result = await FoundryUpdateService.CheckAsync(settings.UpdateManifestLocation, current, settings.AllowNetworkAccess);
            update = result.IsUpdateAvailable ? result.Manifest : null;
            stagedInstallerPath = null;
            DownloadButton.Content = "Stage Verified Update";
            DownloadButton.IsEnabled = update is not null;
            OutputText.Text = result.IsSuccess
                ? result.IsUpdateAvailable ? $"Update {result.Manifest!.Version} is available.\nPublished: {result.Manifest.PublishedAtUtc:u}\nPackage: {result.Manifest.PackageUrl}" : $"Foundry {current} is up to date."
                : string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}\n{item.SuggestedFix}"));
        }
        finally { IsEnabled = true; }
    }
    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (stagedInstallerPath is not null)
        {
            var confirmation = MessageBox.Show(
                this,
                "Save your work before continuing. Foundry will close after the verified Windows updater starts. Continue?",
                "Install Foundry Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes) return;
            var launch = FoundryUpdateService.LaunchInstaller(stagedInstallerPath);
            if (!launch.IsSuccess)
            {
                OutputText.AppendText(Environment.NewLine + string.Join(Environment.NewLine, launch.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
                return;
            }
            Application.Current.Shutdown();
            return;
        }
        if (update is null) return;
        IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => OutputText.AppendText(Environment.NewLine + message));
            var result = await FoundryUpdateService.StageAsync(update, Path.Combine(stateRoot, "updates"), settings.AllowNetworkAccess, progress);
            if (result.PackagePath is null)
            {
                OutputText.AppendText(Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
            }
            else if (string.Equals(Path.GetExtension(result.PackagePath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                stagedInstallerPath = result.PackagePath;
                DownloadButton.Content = "Install Verified Update";
                OutputText.AppendText($"\nVerified Windows updater staged at:\n{result.PackagePath}\n\nSelect Install Verified Update when you are ready to close Foundry and start setup.");
            }
            else
            {
                DownloadButton.IsEnabled = false;
                OutputText.AppendText($"\nLegacy update package staged at:\n{result.PackagePath}\n\nThis package is not a Phase 19 native updater.");
            }
        }
        finally { IsEnabled = true; }
    }
}
