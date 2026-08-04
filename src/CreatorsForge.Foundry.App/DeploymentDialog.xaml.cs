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
public partial class DeploymentDialog : Window
{
    private readonly FoundryWorkspace workspace;
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<InstallationChoice> installations = [];
    private StreamerBotDeploymentPlan? currentPlan;
    private StreamerBotDeploymentHealth? currentHealth;
    private string? currentImportPackagePath;

    public DeploymentDialog(
        FoundryWorkspace workspace,
        FoundryUserSettings settings)
    {
        this.workspace = workspace;
        InitializeComponent();

        foreach (var installation in StreamerBotInstallationDiscovery.Discover(
                     settings.StreamerBotInstallations ?? [],
                     workspace.ProjectRoot))
        {
            AddInstallation(installation);
        }

        InstallationComboBox.ItemsSource = installations;
        var targetProfile = workspace.Manifest.Target?.Profile;
        var matchingIndex = installations.FindIndex(item =>
            string.Equals(
                item.Installation.Profile,
                targetProfile,
                StringComparison.OrdinalIgnoreCase));
        InstallationComboBox.SelectedIndex = matchingIndex >= 0
            ? matchingIndex
            : installations.Count > 0 ? 0 : -1;
        if (installations.Count == 0)
        {
            DetailsTextBox.Text =
                "No installation was discovered. Select Browse and choose the folder containing Streamer.bot.exe.";
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

    public bool PackagingEnabled { get; private set; }

    private async void InstallationComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        ClearPlan();
        await CheckHealthAsync();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Multiselect = false,
            Title = "Select the folder containing Streamer.bot.exe",
            InitialDirectory = SelectedInstallationRoot,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var installation = StreamerBotInstallationDiscovery.TryInspect(
            dialog.FolderName);
        if (installation is null)
        {
            MessageBox.Show(
                this,
                "The selected folder does not contain Streamer.bot.exe.",
                "Invalid installation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var choice = AddInstallation(installation);
        InstallationComboBox.Items.Refresh();
        InstallationComboBox.SelectedItem = choice;
    }

    private async void PreviewInstall_Click(object sender, RoutedEventArgs e) =>
        await PreviewAsync(root =>
            StreamerBotDeploymentService.CreateInstallPlanAsync(
                workspace.Manifest,
                workspace.ProjectRoot,
                root,
                cancellation.Token));

    private async void CheckHealth_Click(object sender, RoutedEventArgs e) =>
        await CheckHealthAsync();

    private async void PreviewRollback_Click(object sender, RoutedEventArgs e) =>
        await PreviewAsync(root =>
            StreamerBotDeploymentService.CreateRollbackPlanAsync(
                workspace.Manifest,
                root,
                cancellation.Token));

    private async void PreviewUninstall_Click(object sender, RoutedEventArgs e) =>
        await PreviewAsync(root =>
            StreamerBotDeploymentService.CreateUninstallPlanAsync(
                workspace.Manifest,
                root,
                cancellation.Token));

    private async Task PreviewAsync(
        Func<string, Task<StreamerBotDeploymentPlan>> createPlan)
    {
        var root = SelectedInstallationRoot;
        if (root is null)
        {
            MessageBox.Show(
                this,
                "Select a Streamer.bot installation first.",
                "Deployment",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            IsEnabled = false;
            var plan = await createPlan(root);
            ShowPlan(plan);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException)
        {
            DetailsTextBox.Text = exception.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void ShowPlan(StreamerBotDeploymentPlan plan)
    {
        currentPlan = plan;
        currentHealth = null;
        currentImportPackagePath = plan.ImportPackagePath;
        OperationsGrid.ItemsSource = plan.Files;
        PlanSummaryText.Text =
            $"{plan.Operation}: {plan.ProjectName} {plan.ProjectVersion} → {plan.InstallationRoot}";
        var messages = plan.Diagnostics.Select(item =>
            $"{item.Severity} {item.Code}: {item.Message}").ToList();
        if (plan.IsReady)
        {
            messages.Add(
                "Foundry will create a receipt and recoverable backups under .foundry before replacing files.");
            if (plan.ImportPackagePath is not null)
            {
                messages.Add(
                    "After deployment, copy the import code, import it in Streamer.bot, add the deployed DLL as a compiler reference, and compile the Execute C# sub-action.");
            }
        }

        DetailsTextBox.Text = string.Join(Environment.NewLine, messages);
        EnablePackagingButton.Visibility = plan.Diagnostics.Any(item =>
            item.Code == "CFD1004")
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConfirmationCheckBox.IsChecked = false;
        ConfirmationCheckBox.IsEnabled = plan.IsReady;
        ApplyButton.IsEnabled = plan.IsReady;
        CopyImportCodeButton.IsEnabled =
            plan.ImportPackagePath is not null &&
            File.Exists(plan.ImportPackagePath);
        ChecklistGroup.IsEnabled = false;
    }

    private async void EnablePackaging_Click(object sender, RoutedEventArgs e)
    {
        var decision = MessageBox.Show(
            this,
            "Add streamerBotPackage to this project and create a starter structured definition if one does not already exist?",
            "Enable Streamer.bot package output",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            IsEnabled = false;
            var result = await FoundryWorkspaceService.EnableStreamerBotPackagingAsync(
                workspace,
                cancellation.Token);
            if (!result.IsSuccess)
            {
                DetailsTextBox.Text = string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(item =>
                        $"{item.Severity} {item.Code}: {item.Message}"));
                return;
            }

            PackagingEnabled = true;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsEnabled = true;
        }
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
            "Confirm deployment operation",
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
            var result = await StreamerBotDeploymentService.ApplyAsync(
                currentPlan,
                currentPlan.Fingerprint,
                cancellation.Token);
            DetailsTextBox.Text = result.IsSuccess
                ? CreateSuccessMessage(currentPlan, result)
                : string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(item =>
                        $"{item.Severity} {item.Code}: {item.Message}"));
            if (result.IsSuccess)
            {
                WasApplied = true;
                PlanSummaryText.Text = $"{currentPlan.Operation} completed.";
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
            var health = await StreamerBotDeploymentService.InspectHealthAsync(
                workspace.Manifest,
                workspace.ProjectRoot,
                root,
                cancellation.Token);
            ShowHealth(health);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException)
        {
            DetailsTextBox.Text = exception.Message;
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void ShowHealth(StreamerBotDeploymentHealth health)
    {
        currentPlan = null;
        currentHealth = health;
        currentImportPackagePath = health.CurrentImportPackagePath;
        OperationsGrid.ItemsSource = health.Files.Select(file => new HealthFileRow(
            file.State.ToString(),
            file.DestinationRelativePath,
            file.Size,
            file.ExpectedSha256)).ToArray();
        PlanSummaryText.Text = $"{health.State}: {health.Summary}";
        var details = new List<string>
        {
            $"Installation: {health.InstallationRoot}",
            $"Project version: {health.ProjectVersion}",
            $"Installed version: {health.InstalledVersion ?? "not installed"}",
            $"Running installation: {health.InstallationVersion}",
            $"Receipt installation: {health.ReceiptInstallationVersion ?? "none"}",
            $"Current package matches receipt: {FormatNullableBoolean(health.CurrentPackageMatchesReceipt)}",
            $"Action: {health.RecommendedAction}",
        };
        details.AddRange(health.Diagnostics.Select(item =>
            $"{item.Severity} {item.Code}: {item.Message}"));
        DetailsTextBox.Text = string.Join(Environment.NewLine, details);

        PackageImportedCheckBox.IsChecked = health.Verification.PackageImported;
        CompilerReferenceCheckBox.IsChecked =
            health.Verification.CompilerReferenceAdded;
        CodeCompiledCheckBox.IsChecked = health.Verification.CodeCompiled;
        RuntimeVerifiedCheckBox.IsChecked = health.Verification.RuntimeVerified;
        ChecklistGroup.IsEnabled = health.DeploymentId is not null;
        RepairButton.Content = health.State is
            DeploymentHealthState.MissingFiles or
            DeploymentHealthState.ModifiedFiles or
            DeploymentHealthState.RedeployRecommended
            ? "Preview Repair / Redeploy"
            : health.State == DeploymentHealthState.UpdateAvailable
                ? "Preview Update"
                : "Preview Install / Update";
        ConfirmationCheckBox.IsChecked = false;
        ConfirmationCheckBox.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        CopyImportCodeButton.IsEnabled =
            currentImportPackagePath is not null &&
            File.Exists(currentImportPackagePath);
        EnablePackagingButton.Visibility = Visibility.Collapsed;
    }

    private async void SaveChecklist_Click(object sender, RoutedEventArgs e)
    {
        if (currentHealth?.DeploymentId is not { } deploymentId)
        {
            return;
        }

        var verification = new StreamerBotDeploymentVerification
        {
            PackageImported = PackageImportedCheckBox.IsChecked == true,
            CompilerReferenceAdded = CompilerReferenceCheckBox.IsChecked == true,
            CodeCompiled = CodeCompiledCheckBox.IsChecked == true,
            RuntimeVerified = RuntimeVerifiedCheckBox.IsChecked == true,
        };
        try
        {
            IsEnabled = false;
            var result = await StreamerBotDeploymentService.SaveVerificationAsync(
                currentHealth.InstallationRoot,
                currentHealth.ProjectId,
                deploymentId,
                verification,
                cancellation.Token);
            if (!result.IsSuccess)
            {
                DetailsTextBox.Text = string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Select(item =>
                        $"{item.Severity} {item.Code}: {item.Message}"));
                return;
            }

            await CheckHealthAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void CopyImportCode_Click(object sender, RoutedEventArgs e)
    {
        if (currentImportPackagePath is not { } path || !File.Exists(path))
        {
            return;
        }

        Clipboard.SetText(File.ReadAllText(path).Trim());
        DetailsTextBox.Text =
            "Import code copied. In Streamer.bot, choose Import, paste the code, then add the deployed DLL as the Execute C# compiler reference.";
    }

    private static string CreateSuccessMessage(
        StreamerBotDeploymentPlan plan,
        DeploymentApplyResult result)
    {
        if (plan.Operation is DeploymentOperation.Rollback)
        {
            return $"Rollback completed. Active receipt version: {result.Receipt?.ProjectVersion ?? "none"}.";
        }

        if (plan.Operation is DeploymentOperation.Uninstall)
        {
            return "Uninstall completed. Pre-existing files were restored where backups existed.";
        }

        var assembly = plan.Files.Count > 0
            ? plan.Files[0].DestinationRelativePath
            : null;
        return
            $"Deployment completed and receipt {result.Receipt?.DeploymentId} was written.\n" +
            $"Next: import the package, add '{Path.Combine(plan.InstallationRoot, assembly ?? string.Empty)}' as the compiler reference, compile, and run the action.";
    }

    private InstallationChoice AddInstallation(StreamerBotInstallation installation)
    {
        var existing = installations.FirstOrDefault(item =>
            string.Equals(
                item.Installation.RootPath,
                installation.RootPath,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var choice = new InstallationChoice(
            installation,
            $"{installation.Profile} — {installation.RootPath}");
        installations.Add(choice);
        return choice;
    }

    private void ClearPlan()
    {
        currentPlan = null;
        currentHealth = null;
        currentImportPackagePath = null;
        OperationsGrid.ItemsSource = null;
        PlanSummaryText.Text = "Preview an operation to review its exact changes.";
        ConfirmationCheckBox.IsChecked = false;
        ConfirmationCheckBox.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        CopyImportCodeButton.IsEnabled = false;
        EnablePackagingButton.Visibility = Visibility.Collapsed;
        ChecklistGroup.IsEnabled = false;
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

    private sealed record InstallationChoice(
        StreamerBotInstallation Installation,
        string DisplayName);
}
