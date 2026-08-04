using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Packaging;
using CreatorsForge.Foundry.NativeTestHost;
using CreatorsForge.Foundry.Testing;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the dialog lifetime; closing cancels and the active run disposes its token source.")]
public partial class TestExplorerDialog : Window
{
    private readonly FoundryWorkspace workspace;
    private readonly FoundryUserSettings settings;
    private readonly FoundryBuildOrchestrator builder;
    private readonly List<FoundryTestExplorerEntry> entries = [];
    private readonly List<FoundryDiagnostic> runDiagnostics = [];
    private CancellationTokenSource? runCancellation;

    public TestExplorerDialog(
        FoundryWorkspace workspace,
        FoundryUserSettings settings,
        FoundryBuildOrchestrator builder)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        InitializeComponent();

        ProjectSummaryText.Text =
            $"{workspace.Manifest.Name} · {workspace.Manifest.Target?.Provider} · {workspace.Manifest.Target?.Profile}";
        OutcomeFilter.ItemsSource = new[] { "All", "Passed", "Failed", "Error", "Skipped" };
        OutcomeFilter.SelectedIndex = 0;

        if (IsObsProject)
        {
            ObsRuntimePanel.Visibility = Visibility.Visible;
            foreach (var installation in settings.ObsInstallations ?? [])
            {
                AddObsInstallation(installation, select: false);
            }

            if (ObsInstallationsList.Items.Count > 0)
            {
                ObsInstallationsList.SelectedIndex = 0;
            }
        }
    }

    public FoundryDiagnostic? NavigationDiagnostic { get; private set; }

    private bool IsObsProject => string.Equals(
        workspace.Manifest.Target?.Provider,
        FoundryTestProviders.ObsStudio,
        StringComparison.Ordinal);

    private async void RunTests_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(matrix: false);

    private async void RunMatrix_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(matrix: true);

    private void CancelRun_Click(object sender, RoutedEventArgs e) => runCancellation?.Cancel();

    private async Task RunAsync(bool matrix)
    {
        if (runCancellation is not null)
        {
            return;
        }

        var obsRoots = GetSelectedObsRoots();
        if (IsObsProject && obsRoots.Length == 0)
        {
            MessageBox.Show(
                this,
                "Select a disposable OBS installation first. Use Add OBS if it is not listed.",
                "Test Explorer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        runCancellation = new CancellationTokenSource();
        SetRunning(true);
        entries.Clear();
        runDiagnostics.Clear();
        ApplyFilter();
        DiagnosticsGrid.ItemsSource = null;
        DetailsTextBox.Text = "Building the current project before testing...";
        StatusText.Text = matrix ? "Building compatibility matrix..." : "Building tests...";

        try
        {
            var cancellationToken = runCancellation.Token;
            var build = await builder.BuildAsync(
                workspace.Manifest,
                workspace.ProjectPath,
                cancellationToken);
            runDiagnostics.AddRange(build.Diagnostics);
            if (!build.IsSuccess)
            {
                FinishWithDiagnostics("Build failed; tests were not started.");
                return;
            }

            var artifactKind = IsObsProject
                ? FoundryPackageArtifactKinds.NativeObsPlugin
                : FoundryPackageArtifactKinds.ManagedAssembly;
            var artifact = build.PackageIntermediate!.Artifacts.SingleOrDefault(item => item.Kind == artifactKind);
            if (artifact is null)
            {
                runDiagnostics.Add(new(
                    "CFT4001",
                    FoundryDiagnosticSeverity.Error,
                    $"The build did not produce the required {artifactKind} test artifact.",
                    new(workspace.ProjectPath)));
                FinishWithDiagnostics("Required test artifact is missing.");
                return;
            }

            var artifactPath = Path.Combine(
                workspace.ProjectRoot,
                "build",
                artifact.Path.Replace('/', Path.DirectorySeparatorChar));
            if (matrix)
            {
                var result = await FoundryCompatibilityMatrixRunner.RunAsync(
                    new(
                        workspace.Manifest,
                        workspace.ProjectPath,
                        artifactPath,
                        obsRoots,
                        IsObsProject ? typeof(NativeTestHostMarker).Assembly.Location : null),
                    cancellationToken);
                entries.AddRange(FoundryTestExplorerProjection.FromMatrix(result));
                runDiagnostics.AddRange(result.Diagnostics);
                FinishRun(
                    $"Matrix {result.Outcome.ToString().ToLowerInvariant()}: " +
                    $"{result.Cells.Count} cells, {entries.Count} case results. {result.ResultPath}");
            }
            else
            {
                var result = await FoundryProviderTestOrchestrator.RunAsync(
                    new(
                        workspace.Manifest,
                        workspace.ProjectPath,
                        artifactPath,
                        IsObsProject ? obsRoots[0] : null,
                        IsObsProject ? typeof(NativeTestHostMarker).Assembly.Location : null),
                    cancellationToken);
                entries.AddRange(FoundryTestExplorerProjection.FromRun(result));
                runDiagnostics.AddRange(result.Diagnostics);
                FinishRun(
                    $"Tests {result.Outcome.ToString().ToLowerInvariant()}: " +
                    $"{entries.Count(item => item.Outcome == FoundryTestOutcome.Passed)} passed, " +
                    $"{entries.Count(item => item.Outcome == FoundryTestOutcome.Failed)} failed, " +
                    $"{entries.Count(item => item.Outcome == FoundryTestOutcome.Error)} errors. {result.ResultPath}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Test run cancelled.";
            DetailsTextBox.Text = "The active test run was cancelled.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException)
        {
            runDiagnostics.Add(new(
                "CFT4002",
                FoundryDiagnosticSeverity.Error,
                $"Test Explorer could not complete the run: {exception.Message}",
                new(workspace.ProjectPath)));
            FinishWithDiagnostics("Test run ended with an error.");
        }
        finally
        {
            runCancellation.Dispose();
            runCancellation = null;
            SetRunning(false);
        }
    }

    private void FinishRun(string summary)
    {
        ApplyFilter();
        StatusText.Text = summary;
        DiagnosticsGrid.ItemsSource = runDiagnostics.Distinct().ToArray();
        EntriesGrid.SelectedIndex = EntriesGrid.Items.Count > 0 ? 0 : -1;
        UpdateDiagnosticButton();
    }

    private void FinishWithDiagnostics(string summary)
    {
        StatusText.Text = summary;
        DetailsTextBox.Text = summary;
        DiagnosticsGrid.ItemsSource = runDiagnostics.Distinct().ToArray();
        UpdateDiagnosticButton();
    }

    private void Filter_Changed(object sender, EventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        FoundryTestOutcome? outcome = OutcomeFilter.SelectedIndex switch
        {
            1 => FoundryTestOutcome.Passed,
            2 => FoundryTestOutcome.Failed,
            3 => FoundryTestOutcome.Error,
            4 => FoundryTestOutcome.Skipped,
            _ => null,
        };
        EntriesGrid.ItemsSource = entries
            .Where(item => item.Matches(FilterTextBox.Text, outcome))
            .ToArray();
    }

    private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EntriesGrid.SelectedItem is not FoundryTestExplorerEntry entry)
        {
            DetailsTextBox.Text = "Select a test result to inspect its event, assertions, logs, and CPH calls.";
            DiagnosticsGrid.ItemsSource = runDiagnostics.Distinct().ToArray();
        }
        else
        {
            DetailsTextBox.Text = entry.CreateDetails();
            DiagnosticsGrid.ItemsSource = runDiagnostics
                .Concat(entry.Diagnostics)
                .Distinct()
                .ToArray();
        }

        UpdateDiagnosticButton();
    }

    private void DiagnosticsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        SelectDiagnosticForNavigation();

    private void DiagnosticsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateDiagnosticButton();

    private void OpenDiagnostic_Click(object sender, RoutedEventArgs e) => SelectDiagnosticForNavigation();

    private void SelectDiagnosticForNavigation()
    {
        if (DiagnosticsGrid.SelectedItem is not FoundryDiagnostic diagnostic ||
            diagnostic.Location?.FilePath is not { Length: > 0 })
        {
            return;
        }

        NavigationDiagnostic = diagnostic;
        Close();
    }

    private void AddObs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a disposable OBS Studio installation",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            AddObsInstallation(dialog.FolderName, select: true);
        }
    }

    private void AddObsInstallation(string path, bool select)
    {
        var fullPath = Path.GetFullPath(path);
        var existing = ObsInstallationsList.Items.Cast<string>().FirstOrDefault(item =>
            string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            ObsInstallationsList.Items.Add(fullPath);
            existing = fullPath;
        }

        if (select)
        {
            ObsInstallationsList.SelectedItems.Clear();
            ObsInstallationsList.SelectedItems.Add(existing);
        }
    }

    private string[] GetSelectedObsRoots() => IsObsProject
        ? ObsInstallationsList.SelectedItems.Cast<string>().ToArray()
        : [];

    private void SetRunning(bool running)
    {
        RunTestsButton.IsEnabled = !running;
        RunMatrixButton.IsEnabled = !running;
        CancelRunButton.IsEnabled = running;
        ObsInstallationsList.IsEnabled = !running;
    }

    private void UpdateDiagnosticButton() => OpenDiagnosticButton.IsEnabled =
        DiagnosticsGrid.SelectedItem is FoundryDiagnostic diagnostic &&
        !string.IsNullOrWhiteSpace(diagnostic.Location?.FilePath);

    private void Window_Closing(object? sender, CancelEventArgs e) => runCancellation?.Cancel();
}
