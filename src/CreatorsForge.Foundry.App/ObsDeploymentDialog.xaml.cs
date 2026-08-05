using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Workspaces;
using CreatorsForge.Foundry.Workspaces.Deployment;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the dialog lifetime; Closed cancels and disposes the token source.")]
public partial class ObsDeploymentDialog : Window
{
    private readonly FoundryWorkspace workspace;
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<InstallationChoice> installations = [];
    private ObsDeploymentPlan? currentPlan;

    public ObsDeploymentDialog(FoundryWorkspace workspace, FoundryUserSettings settings)
    {
        this.workspace = workspace;
        InitializeComponent();
        foreach (var installation in ObsInstallationDiscovery.Discover(
                     settings.ObsInstallations ?? [],
                     workspace.ProjectRoot))
        {
            AddInstallation(installation);
        }

        InstallationComboBox.ItemsSource = installations;
        InstallationComboBox.SelectedIndex = installations.Count > 0 ? 0 : -1;
        if (installations.Count == 0)
        {
            DetailsTextBox.Text =
                "No OBS installation was discovered. Select Browse and choose the folder containing bin\\64bit\\obs64.exe.";
        }

        Closed += (_, _) =>
        {
            cancellation.Cancel();
            cancellation.Dispose();
        };
    }

    public string? SelectedInstallationRoot =>
        (InstallationComboBox.SelectedItem as InstallationChoice)?.Installation.RootPath;

    public bool WasApplied { get; private set; }

    internal bool InstallationLabelsReady => installations.All(choice =>
        string.Equals(choice.ToString(), choice.DisplayName, StringComparison.Ordinal)) &&
        (InstallationComboBox.SelectedItem is not InstallationChoice selected ||
         string.Equals(InstallationComboBox.Text, selected.DisplayName, StringComparison.Ordinal));

    private async void InstallationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ClearPlan();
        await CheckHealthAsync();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false,
            Title = "Select the OBS Studio installation folder",
            InitialDirectory = SelectedInstallationRoot,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var installation = ObsInstallationDiscovery.TryInspect(dialog.FolderName);
        if (installation is null)
        {
            MessageBox.Show(
                this,
                "The selected folder does not contain bin\\64bit\\obs64.exe.",
                "Invalid OBS installation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var choice = AddInstallation(installation);
        InstallationComboBox.Items.Refresh();
        InstallationComboBox.SelectedItem = choice;
    }

    private async void PreviewInstall_Click(object sender, RoutedEventArgs e) =>
        await PreviewAsync(root => ObsDeploymentService.CreateInstallPlanAsync(
            workspace.Manifest,
            workspace.ProjectRoot,
            root,
            cancellation.Token));

    private async void PreviewRollback_Click(object sender, RoutedEventArgs e) =>
        await PreviewAsync(root => ObsDeploymentService.CreateRollbackPlanAsync(
            workspace.Manifest,
            root,
            cancellation.Token));

    private async void PreviewUninstall_Click(object sender, RoutedEventArgs e) =>
        await PreviewAsync(root => ObsDeploymentService.CreateUninstallPlanAsync(
            workspace.Manifest,
            root,
            cancellation.Token));

    private async void CheckHealth_Click(object sender, RoutedEventArgs e) =>
        await CheckHealthAsync();

    private async Task PreviewAsync(Func<string, Task<ObsDeploymentPlan>> createPlan)
    {
        var root = SelectedInstallationRoot;
        if (root is null)
        {
            MessageBox.Show(this, "Select an OBS installation first.", "OBS deployment", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            IsEnabled = false;
            ShowPlan(await createPlan(root));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DetailsTextBox.Text = exception.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void ShowPlan(ObsDeploymentPlan plan)
    {
        currentPlan = plan;
        OperationsGrid.ItemsSource = plan.Files;
        PlanSummaryText.Text =
            $"{plan.Operation}: {plan.ProjectName} {plan.ProjectVersion} → {plan.InstallationRoot}";
        var messages = plan.Diagnostics.Select(item =>
            $"{item.Severity} {item.Code}: {item.Message}").ToList();
        if (plan.IsReady)
        {
            messages.Add(
                "Foundry will write an ownership receipt and recoverable backups under .foundry\\obs before changing files.");
        }

        DetailsTextBox.Text = string.Join(Environment.NewLine, messages);
        LogStatusText.Text = "Apply the reviewed plan, launch and close OBS, then Check Health.";
        ConfirmationCheckBox.IsChecked = false;
        ConfirmationCheckBox.IsEnabled = plan.IsReady;
        ApplyButton.IsEnabled = plan.IsReady;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (currentPlan is null || ConfirmationCheckBox.IsChecked != true)
        {
            MessageBox.Show(
                this,
                "Review the plan and select the confirmation checkbox before applying it.",
                "Explicit confirmation required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var decision = MessageBox.Show(
            this,
            $"Apply the reviewed {currentPlan.Operation} plan to:\n{currentPlan.InstallationRoot}?",
            "Confirm OBS deployment",
            MessageBoxButton.YesNo,
            currentPlan.Operation == DeploymentOperation.Uninstall
                ? MessageBoxImage.Warning
                : MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsEnabled = false;
            var result = await ObsDeploymentService.ApplyAsync(
                currentPlan,
                currentPlan.Fingerprint,
                cancellation.Token);
            DetailsTextBox.Text = result.IsSuccess
                ? currentPlan.Operation == DeploymentOperation.Uninstall
                    ? "Uninstall completed. User-owned files were restored where backups existed."
                    : $"{currentPlan.Operation} completed. Receipt: {result.Receipt?.DeploymentId ?? "removed"}."
                : string.Join(Environment.NewLine, result.Diagnostics.Select(item =>
                    $"{item.Severity} {item.Code}: {item.Message}"));
            if (result.IsSuccess)
            {
                WasApplied = true;
                ConfirmationCheckBox.IsChecked = false;
                ConfirmationCheckBox.IsEnabled = false;
                ApplyButton.IsEnabled = false;
                await CheckHealthAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async Task CheckHealthAsync()
    {
        var root = SelectedInstallationRoot;
        if (root is null)
        {
            return;
        }

        try
        {
            IsEnabled = false;
            var health = await ObsDeploymentService.InspectHealthAsync(
                workspace.Manifest,
                workspace.ProjectRoot,
                root,
                cancellation.Token);
            currentPlan = null;
            OperationsGrid.ItemsSource = health.Files.Select(item => new HealthFileRow(
                item.State.ToString(),
                item.DestinationRelativePath,
                item.Size,
                item.ExpectedSha256)).ToArray();
            PlanSummaryText.Text = $"{health.State}: {health.Summary}";
            DetailsTextBox.Text = string.Join(
                Environment.NewLine,
                new[]
                {
                    $"Project version: {health.ProjectVersion}",
                    $"Installed version: {health.InstalledVersion ?? "not installed"}",
                    $"Running installation: {health.InstallationVersion}",
                    $"Receipt installation: {health.ReceiptInstallationVersion ?? "none"}",
                    $"Current package matches receipt: {FormatNullableBoolean(health.CurrentPackageMatchesReceipt)}",
                    $"Action: {health.RecommendedAction}",
                }.Concat(health.Diagnostics.Select(item =>
                    $"{item.Severity} {item.Code}: {item.Message}")));
            LogStatusText.Text =
                $"Log: {health.Log.State} — {health.Log.Summary}" +
                (health.Log.LogPath is null ? string.Empty : $"\n{health.Log.LogPath}") +
                (health.Log.RelevantLines.Count == 0
                    ? string.Empty
                    : $"\n{string.Join(Environment.NewLine, health.Log.RelevantLines)}");
            RepairButton.Content = health.State is
                DeploymentHealthState.MissingFiles or
                DeploymentHealthState.ModifiedFiles or
                DeploymentHealthState.RedeployRecommended or
                DeploymentHealthState.LogFailure
                ? "Preview Repair / Redeploy"
                : health.State == DeploymentHealthState.UpdateAvailable
                    ? "Preview Update"
                    : "Preview Install / Update";
            ConfirmationCheckBox.IsChecked = false;
            ConfirmationCheckBox.IsEnabled = false;
            ApplyButton.IsEnabled = false;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private InstallationChoice AddInstallation(ObsInstallation installation)
    {
        var existing = installations.FirstOrDefault(item => string.Equals(
            item.Installation.RootPath,
            installation.RootPath,
            StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var choice = new InstallationChoice(
            installation,
            $"{installation.Version} — {installation.RootPath}");
        installations.Add(choice);
        return choice;
    }

    private void ClearPlan()
    {
        currentPlan = null;
        OperationsGrid.ItemsSource = null;
        PlanSummaryText.Text = "Preview an operation to review its exact changes.";
        ConfirmationCheckBox.IsChecked = false;
        ConfirmationCheckBox.IsEnabled = false;
        ApplyButton.IsEnabled = false;
    }

    private static string FormatNullableBoolean(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "not available",
    };

    private sealed record HealthFileRow(
        string Change,
        string DestinationRelativePath,
        long Size,
        string Sha256);

    private sealed record InstallationChoice(ObsInstallation Installation, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
