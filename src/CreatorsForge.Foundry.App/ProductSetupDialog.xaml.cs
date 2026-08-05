using System.Windows;
using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class ProductSetupDialog : Window
{
    private FoundryUserSettings settings;
    private readonly string stateRoot;
    public ProductSetupDialog(FoundryUserSettings settings, string stateRoot)
    {
        this.settings = settings;
        this.stateRoot = stateRoot;
        InitializeComponent();
        RefreshHealth();
    }
    private void RefreshHealth()
    {
        var health = FoundryProductHealthService.Inspect(
            stateRoot,
            settings.VisualStudioInstallationRoot);
        ChecksList.ItemsSource = health.Checks.Select(check => new CheckView(check, check.IsReady ? "READY" : check.Required ? "REQUIRED" : "OPTIONAL")).ToArray();
        SummaryText.Text = health.IsReady
            ? health.NativeToolchainReady ? "Foundry and the complete OBS toolchain are ready." : "Foundry is ready. Optional OBS development tools can be completed later."
            : "A required desktop dependency needs attention before Foundry can work reliably.";
    }
    public FoundryUserSettings CompletedSettings { get; private set; } = null!;
    private void Toolchain_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ObsSdkDialog(settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            settings = dialog.UpdatedSettings!;
            RefreshHealth();
        }
    }
    private void Finish_Click(object sender, RoutedEventArgs e) { CompletedSettings = settings with { FirstRunCompleted = true }; DialogResult = true; }
    private sealed record CheckView(FoundryProductCheck Check, string Status);
}
