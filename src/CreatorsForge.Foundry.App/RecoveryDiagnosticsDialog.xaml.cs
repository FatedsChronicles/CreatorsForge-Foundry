using System.Windows;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class RecoveryDiagnosticsDialog : Window
{
    private readonly AppServices services;
    private readonly FoundryUserSettings settings;
    public RecoveryDiagnosticsDialog(AppServices services, FoundryUserSettings settings) { this.services = services; this.settings = settings; InitializeComponent(); Loaded += LoadedAsync; }
    private async void LoadedAsync(object sender, RoutedEventArgs e)
    {
        var recovery = await services.Recovery.ListAsync();
        var failures = services.FailureReports.ListReports();
        SummaryText.Text = $"{recovery.Count} recovery snapshots and {failures.Count} local failure reports. Nothing is uploaded automatically.";
        ItemsList.ItemsSource = recovery.Select(item => $"Recovery  {item.RecoveredAtUtc:u}  {item.DocumentPath}").Concat(failures.Select(path => $"Failure   {Path.GetFileName(path)}")).ToArray();
    }
    private async void CreateBundle_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Foundry diagnostic bundle (*.zip)|*.zip", FileName = $"foundry-diagnostics-{DateTime.UtcNow:yyyyMMdd}.zip" };
        if (dialog.ShowDialog(this) != true) return;
        await services.FailureReports.CreateBundleAsync(dialog.FileName, settings, settings.IncludePathsInDiagnosticBundles);
        MessageBox.Show(this, "The local diagnostic bundle was created with an issue-report template. Review every file before sharing.", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
