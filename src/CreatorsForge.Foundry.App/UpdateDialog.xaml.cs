using System.Reflection;
using System.Windows;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class UpdateDialog : Window
{
    private readonly FoundryUserSettings settings;
    private readonly string stateRoot;
    private FoundryUpdateManifest? update;
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
            DownloadButton.IsEnabled = update is not null;
            OutputText.Text = result.IsSuccess
                ? result.IsUpdateAvailable ? $"Update {result.Manifest!.Version} is available.\nPublished: {result.Manifest.PublishedAtUtc:u}\nPackage: {result.Manifest.PackageUrl}" : $"Foundry {current} is up to date."
                : string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}\n{item.SuggestedFix}"));
        }
        finally { IsEnabled = true; }
    }
    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (update is null) return;
        IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => OutputText.AppendText(Environment.NewLine + message));
            var result = await FoundryUpdateService.StageAsync(update, Path.Combine(stateRoot, "updates"), settings.AllowNetworkAccess, progress);
            OutputText.AppendText(result.PackagePath is null
                ? Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"))
                : $"\nVerified update staged at:\n{result.PackagePath}\n\nClose Foundry, extract the package, and run install-foundry.ps1 to update safely.");
        }
        finally { IsEnabled = true; }
    }
}
