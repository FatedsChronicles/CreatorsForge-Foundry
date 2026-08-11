using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Editor;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "WPF owns the window lifetime; OnClosed disposes the terminal session and token source.")]
public partial class MainWindow : Window
{
    private const string ProjectTreeItemDataFormat = "CreatorsForge.Foundry.ProjectTreeItem";
    private readonly AppServices services;
    private readonly MainWindowViewModel viewModel;
    private readonly string? startupProjectPath;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly DispatcherTimer autosaveTimer = new();
    private bool allowClose;
    private bool isBusy;
    private Point projectTreeDragStart;
    private ProjectTreeItemViewModel? projectTreeDragItem;

    public MainWindow(
        AppServices services,
        string? startupProjectPath,
        bool isSmokeTest = false)
    {
        this.services = services;
        this.startupProjectPath = startupProjectPath;
        viewModel = services.CreateMainWindowViewModel();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.Workspace))
            {
                SnippetProvider.Reload(
                    SnippetProvider.UserDirectory,
                    viewModel.Workspace?.ProjectRoot);
                TerminalWorkspaceChanged();
            }
        };
        InitializeComponent();
        InitializeTerminal();
        if (!isSmokeTest)
        {
            Loaded += MainWindow_Loaded;
        }

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        autosaveTimer.Tick += AutosaveTimer_Tick;
    }

    internal async Task<bool> RunSmokeTestAsync(
        CancellationToken cancellationToken)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await viewModel.InitializeAsync(cancellationToken);
        ApplyLayout(viewModel.Settings.Layout);

        if (!string.IsNullOrWhiteSpace(startupProjectPath) &&
            !await OpenPathAsync(
                startupProjectPath,
                cancellationToken,
                recordRecent: false))
        {
            return false;
        }

        var source = viewModel.Workspace?.Manifest.ManagedBuild?.Sources
            .FirstOrDefault(path =>
                string.Equals(
                    Path.GetExtension(path),
                    ".cs",
                    StringComparison.OrdinalIgnoreCase)) ??
            viewModel.Workspace?.Manifest.NativeBuild?.Sources
                .FirstOrDefault(path =>
                    string.Equals(
                        Path.GetExtension(path),
                        ".c",
                        StringComparison.OrdinalIgnoreCase));
        if (source is not null &&
            !await viewModel.OpenDocumentAsync(
                Path.Combine(
                    viewModel.Workspace!.ProjectRoot,
                    source.Replace('/', Path.DirectorySeparatorChar)),
                cancellationToken))
        {
            return false;
        }

        Show();
        await Dispatcher.InvokeAsync(
            () => UpdateLayout(),
            DispatcherPriority.ContextIdle,
            cancellationToken);
        var snippetBrowser = new SnippetBrowserDialog(
            SnippetProvider.Default,
            viewModel.Workspace?.Manifest.Target?.Profile ?? "1.0.4-stable");
        var snippetBrowserReady = snippetBrowser.Content is not null;
        snippetBrowser.Close();
        var obsReference = new ObsApiReferenceDialog(
            ObsNativeIntelligenceProvider.Default.Catalogue,
            viewModel.Workspace?.Manifest.Target?.Profile ?? "32.x-windows-x64");
        var obsReferenceReady = obsReference.Content is not null &&
            ObsNativeIntelligenceProvider.Default.Catalogue.Symbols.Count > 0;
        obsReference.Close();
        var designerReady = true;
        if (viewModel.Workspace?.Manifest.TargetDefinition is { Length: > 0 } definition)
        {
            var designer = new StreamerBotDesignerDialog(
                Path.Combine(
                    viewModel.Workspace.ProjectRoot,
                    definition.Replace('/', Path.DirectorySeparatorChar)),
                viewModel.Workspace.Manifest.Target?.Profile);
            var palette = new StreamerBotOperationPaletteDialog(
                "subAction",
                viewModel.Workspace.Manifest.Target?.Profile);
            designerReady = designer.Content is not null &&
                designer.ResourcesReadyForSmokeTest &&
                designer.CSharpAuthoringReadyForSmokeTest &&
                palette.Content is not null &&
                new OperationReferenceChoice("command-id", "Friendly command").ToString() == "Friendly command";
            palette.Close();
            designer.Close();
        }

        var obsDesignerReady = true;
        if (string.Equals(
                viewModel.Workspace?.Manifest.Target?.Provider,
                "obsstudio",
                StringComparison.OrdinalIgnoreCase))
        {
            var obsDesigner = new ObsPluginDesignerDialog(viewModel.Workspace!);
            obsDesignerReady = obsDesigner.Content is not null;
            obsDesigner.Close();
        }

        var deploymentReady = true;
        if (string.Equals(
            viewModel.Workspace?.Manifest.Target?.Provider,
            "streamerbot",
            StringComparison.OrdinalIgnoreCase))
        {
            var deploymentDialog = new DeploymentDialog(
                viewModel.Workspace!,
                viewModel.Settings);
            deploymentReady = deploymentDialog.Content is not null &&
                deploymentDialog.InstallationLabelsReady;
            deploymentDialog.Close();
        }
        else if (string.Equals(
                     viewModel.Workspace?.Manifest.Target?.Provider,
                     "obsstudio",
                     StringComparison.OrdinalIgnoreCase))
        {
            var deploymentDialog = new ObsDeploymentDialog(
                viewModel.Workspace!,
                viewModel.Settings);
            deploymentReady = deploymentDialog.Content is not null &&
                deploymentDialog.InstallationLabelsReady;
            deploymentDialog.Close();
        }

        var testExplorerReady = true;
        if (viewModel.Workspace is not null)
        {
            var testExplorer = new TestExplorerDialog(
                viewModel.Workspace,
                viewModel.Settings,
                viewModel.Builder);
            testExplorerReady = testExplorer.Content is not null;
            testExplorer.Close();
        }

        var darkSyntaxHighlightingReady = FoundrySyntaxHighlighting.Dark is not null;
        var previewShortcutReady = IsPreviewShortcut(
            Key.P,
            ModifierKeys.Control | ModifierKeys.Shift);
        var terminalShortcutReady = IsTerminalShortcut(
            Key.T,
            ModifierKeys.Control);
        var terminalReady = TerminalInput is not null &&
            TerminalOutput is not null &&
            TerminalTab is not null;
        var newProjectItemDialog = new NewProjectItemDialog("Folder: src");
        var newProjectItemDialogReady = newProjectItemDialog.Content is not null &&
            newProjectItemDialog.ItemTypes.All(option =>
                string.Equals(option.ToString(), option.DisplayName, StringComparison.Ordinal));
        newProjectItemDialog.Close();

        var importDialog = new StreamerBotImportDialog(viewModel.Settings);
        var importFixture = StreamerBotStableV23Adapter.Encode(
            new StreamerBotDefinition
            {
                Metadata = new() { Author = "Smoke", Description = "Safe import smoke fixture" },
                Queues = [new("queue", "Default", false)],
                Commands = [new("command", "Hello", ["!hello"], true, false, 0, 0)],
                Actions = [new("action", "Hello", true, "queue", false, false,
                    [new("trigger", "command", true, "command")],
                    [new("argument", "setArgument", true, "message", "Hello", true)])],
            },
            "com.creatorsforge.smoke.import", "Import Smoke", "1.0.0", string.Empty);
        var streamerBotImportReady = importDialog.Content is not null &&
            importDialog.AnalyzeForSmokeTest(importFixture.ImportCode) &&
            importDialog.VerifyCreationSuggestionsForSmokeTest();
        importDialog.Close();

        var previewDesignerReady = true;
        if (viewModel.Workspace is not null)
        {
            var previewDesigner = new PreviewDesignerDialog(
                viewModel.Workspace,
                viewModel.Settings,
                viewModel.Builder);
            var expectedSource = string.Equals(
                viewModel.Workspace.Manifest.Target?.Provider,
                "obsstudio",
                StringComparison.OrdinalIgnoreCase)
                ? viewModel.Workspace.Manifest.ObsPlugin?.Design?.Source
                : null;
            previewDesignerReady = previewDesigner.Content is not null &&
                !previewDesigner.SelectedKindDisplayText.Contains("PreviewKindOption", StringComparison.Ordinal) &&
                !previewDesigner.SelectedViewportDisplayText.Contains("ViewportOption", StringComparison.Ordinal) &&
                previewDesigner.SelectedViewportDisplayText == "HD 1280 x 720" &&
                (expectedSource is null || previewDesigner.SelectedSourceDisplayText == expectedSource);
            if (previewDesignerReady)
            {
                previewDesignerReady = await previewDesigner.RunRuntimeSmokeTestAsync();
            }
            await previewDesigner.DisposeAsync();
            previewDesigner.Close();
        }

        var succeeded = IsVisible &&
            (source is null || FindVisualChild<CodeEditor>(DocumentTabs) is not null) &&
            SnippetProvider.Default.Catalogue.Snippets.Count > 0 &&
            snippetBrowserReady &&
            obsReferenceReady &&
            designerReady &&
            obsDesignerReady &&
            deploymentReady &&
            testExplorerReady &&
            newProjectItemDialogReady &&
            streamerBotImportReady &&
            previewDesignerReady &&
            previewShortcutReady &&
            terminalShortcutReady &&
            terminalReady &&
            darkSyntaxHighlightingReady;
        allowClose = true;
        Close();
        return succeeded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await viewModel.InitializeAsync(lifetimeCancellation.Token);
            ApplyLayout(viewModel.Settings.Layout);
            UpdateAutosaveTimer();

            if (!viewModel.Settings.FirstRunCompleted)
            {
                var setup = new ProductSetupDialog(viewModel.Settings, services.StateRoot) { Owner = this };
                if (setup.ShowDialog() == true)
                    await viewModel.SaveSettingsAsync(setup.CompletedSettings, lifetimeCancellation.Token);
            }

            if (!string.IsNullOrWhiteSpace(startupProjectPath))
            {
                await RunBusyAsync(() =>
                    OpenPathAsync(startupProjectPath, lifetimeCancellation.Token));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception);
        }
    }

    private async void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        var dialog = new NewProjectDialog(viewModel.Settings)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunBusyAsync(() =>
            viewModel.CreateProjectAsync(
                dialog.Request!,
                lifetimeCancellation.Token));
    }

    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".foundryproj",
            Filter = "Foundry projects (*.foundryproj)|*.foundryproj",
            InitialDirectory = Directory.Exists(viewModel.Settings.DefaultProjectDirectory)
                ? viewModel.Settings.DefaultProjectDirectory
                : null,
            Title = "Open Creators Forge Foundry project",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync(() =>
            viewModel.OpenProjectAsync(dialog.FileName, lifetimeCancellation.Token));
    }

    private async void AdoptExistingFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        var picker = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(viewModel.Settings.DefaultProjectDirectory)
                ? viewModel.Settings.DefaultProjectDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false,
            Title = "Select an existing source folder",
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        var analysis = await ExternalProjectAdoptionService.AnalyzeAsync(
            picker.FolderName,
            lifetimeCancellation.Token);
        if (!analysis.IsSuccess)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, analysis.Diagnostics.Select(item => $"{item.Code}: {item.Message}")),
                "Adopt existing folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (analysis.Value!.ExistingFoundryProjects.Count > 0)
        {
            MessageBox.Show(
                this,
                "This folder already contains a Foundry project. Open its .foundryproj file instead; Foundry will not overwrite it.",
                "Foundry project already present",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new AdoptExistingProjectDialog(analysis.Value) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunBusyAsync(() => viewModel.AdoptExternalProjectAsync(
            dialog.Request!,
            lifetimeCancellation.Token));
    }

    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return;
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".foundryworkspace",
            Filter = "Foundry workspaces (*.foundryworkspace)|*.foundryworkspace",
            Title = "Open multi-project workspace",
        };
        if (dialog.ShowDialog(this) == true)
            await RunBusyAsync(() => viewModel.OpenWorkspaceSetAsync(dialog.FileName, lifetimeCancellation.Token));
    }

    private async void NewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return;
        var projects = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = true,
            DefaultExt = ".foundryproj",
            Filter = "Foundry projects (*.foundryproj)|*.foundryproj",
            Title = "Choose projects for the workspace",
        };
        if (projects.ShowDialog(this) != true) return;
        var save = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".foundryworkspace",
            Filter = "Foundry workspaces (*.foundryworkspace)|*.foundryworkspace",
            FileName = "CreatorsForge.foundryworkspace",
            InitialDirectory = FindCommonDirectory(projects.FileNames),
            Title = "Save multi-project workspace",
        };
        if (save.ShowDialog(this) != true) return;
        await RunBusyAsync(async () =>
        {
            var result = await FoundryWorkspaceSetService.CreateAsync(
                save.FileName,
                Path.GetFileNameWithoutExtension(save.FileName),
                projects.FileNames,
                lifetimeCancellation.Token);
            if (!result.IsSuccess)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Workspace not created", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return await viewModel.OpenWorkspaceSetAsync(save.FileName, lifetimeCancellation.Token);
        });
    }

    private async void AddProjectToWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.WorkspaceSet is null)
        {
            MessageBox.Show(this, "Open a multi-project workspace first.", "Add Project", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".foundryproj",
            Filter = "Foundry projects (*.foundryproj)|*.foundryproj",
            Title = "Add project to workspace",
        };
        if (dialog.ShowDialog(this) != true) return;
        var result = await FoundryWorkspaceSetService.AddProjectAsync(viewModel.WorkspaceSet, dialog.FileName, lifetimeCancellation.Token);
        if (!result.IsSuccess)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Project not added", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RunBusyAsync(() => viewModel.OpenWorkspaceSetAsync(result.Value!.WorkspacePath, lifetimeCancellation.Token));
    }

    private async void ExportProjectTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Workspace is null)
        {
            MessageBox.Show(this, "Open a project before exporting a template.", "Export Template", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!await viewModel.SaveAllAsync(lifetimeCancellation.Token)) return;
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".foundrytemplate",
            Filter = "Foundry project templates (*.foundrytemplate)|*.foundrytemplate",
            FileName = string.Concat(viewModel.Workspace.Manifest.Name.Where(char.IsLetterOrDigit)) + ".foundrytemplate",
            Title = "Export project template",
        };
        if (dialog.ShowDialog(this) != true) return;
        var diagnostics = await FoundryTemplateInterchangeService.ExportAsync(viewModel.Workspace, dialog.FileName, lifetimeCancellation.Token);
        if (diagnostics.Any(item => item.IsError))
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Template not exported", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(this, $"Template exported to:\n{dialog.FileName}", "Export Template", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ImportProjectTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return;
        var picker = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".foundrytemplate",
            Filter = "Foundry project templates (*.foundrytemplate)|*.foundrytemplate",
            Title = "Import project template",
        };
        if (picker.ShowDialog(this) != true) return;
        var loaded = await FoundryTemplateInterchangeService.LoadAsync(picker.FileName, lifetimeCancellation.Token);
        if (loaded.Package is null)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, loaded.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Template not imported", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new TemplateImportDialog(picker.FileName, loaded.Package, viewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await RunBusyAsync(async () =>
        {
            var result = await FoundryTemplateInterchangeService.ImportAsync(dialog.Request!, lifetimeCancellation.Token);
            if (!result.IsSuccess)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Template not imported", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return await viewModel.OpenProjectAsync(result.Value!.ProjectPath, lifetimeCancellation.Token);
        });
    }

    private async void MigrateProject_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardOrSaveAsync()) return;
        var picker = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".foundryproj",
            Filter = "Foundry projects (*.foundryproj)|*.foundryproj",
            Title = "Select legacy project to migrate",
        };
        if (picker.ShowDialog(this) != true) return;
        var inspection = await FoundryProjectMigrationService.InspectAsync(picker.FileName, lifetimeCancellation.Token);
        if (!inspection.IsSuccess)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, inspection.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Migration unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!inspection.Plan!.IsRequired)
        {
            MessageBox.Show(this, "This project already uses the current schema and does not require migration.", "Project Migration", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var message = "The following changes will be made:\n\n" + string.Join("\n", inspection.Plan.Changes.Select(item => "• " + item)) +
            $"\n\nBackup:\n{inspection.Plan.BackupPath}\n\nContinue?";
        if (MessageBox.Show(this, message, "Migrate Project", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunBusyAsync(async () =>
        {
            var result = await FoundryProjectMigrationService.MigrateAsync(picker.FileName, lifetimeCancellation.Token);
            if (!result.IsSuccess)
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Migration failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return await viewModel.OpenProjectAsync(result.Workspace!.ProjectPath, lifetimeCancellation.Token);
        });
    }

    private async void RecentProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: RecentProjectEntry recent,
            } ||
            !await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        await RunBusyAsync(() => OpenPathAsync(recent.ProjectPath, lifetimeCancellation.Token));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedDocument is not null)
        {
            await RunBusyAsync(() =>
                viewModel.SaveDocumentAsync(
                    viewModel.SelectedDocument,
                    lifetimeCancellation.Token));
        }
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() =>
            viewModel.SaveAllAsync(lifetimeCancellation.Token));

    private async void CloseDocument_Click(object sender, RoutedEventArgs e)
    {
        var document = viewModel.SelectedDocument;
        if (document is null)
        {
            return;
        }

        await TryCloseDocumentAsync(document);
    }

    private async void CloseDocumentTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { DataContext: DocumentViewModel document })
        {
            await TryCloseDocumentAsync(document);
        }
    }

    private async Task<bool> TryCloseDocumentAsync(DocumentViewModel document)
    {
        if (document.IsDirty)
        {
            var decision = MessageBox.Show(
                this,
                $"Save changes to {document.FileName}?",
                "Close document",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (decision == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (decision == MessageBoxResult.Yes &&
                !await viewModel.SaveDocumentAsync(document, lifetimeCancellation.Token))
            {
                return false;
            }
        }

        viewModel.CloseDocument(document);
        return true;
    }

    private async void CloseProject_Click(object sender, RoutedEventArgs e)
    {
        if (await ConfirmDiscardOrSaveAsync())
        {
            viewModel.CloseWorkspace();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private async void Build_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => viewModel.BuildAsync(lifetimeCancellation.Token));

    private async void BuildWorkspace_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => viewModel.BuildWorkspaceSetAsync(lifetimeCancellation.Token));

    private async void Release_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => viewModel.ReleaseAsync(lifetimeCancellation.Token));

    private async void PublishingMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Workspace is null)
        {
            MessageBox.Show(this, "Open a project before editing publishing metadata.", "Publishing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new PublishingMetadataDialog(viewModel.Workspace) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Publishing is not null)
        {
            await RunBusyAsync(() => viewModel.SavePublishingSettingsAsync(
                dialog.Publishing, dialog.Version, lifetimeCancellation.Token));
        }
    }

    private async void ValidatePublishing_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => viewModel.ValidatePublishingAsync(lifetimeCancellation.Token));

    private async void Publish_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => viewModel.PublishAsync(lifetimeCancellation.Token));

    private async void TestExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Workspace is null)
        {
            MessageBox.Show(
                this,
                "Open a project before using Test Explorer.",
                "Test Explorer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await viewModel.SaveAllAsync(lifetimeCancellation.Token) ||
            !await viewModel.RefreshWorkspaceAsync(lifetimeCancellation.Token))
        {
            return;
        }

        var dialog = new TestExplorerDialog(
            viewModel.Workspace!,
            viewModel.Settings,
            viewModel.Builder)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        if (dialog.NavigationDiagnostic?.Location is
            {
                FilePath: { Length: > 0 } filePath,
            } location)
        {
            await NavigateToSourceAsync(new(
                filePath,
                checked((int)Math.Min(location.Line ?? 1, int.MaxValue)),
                checked((int)Math.Min(location.Column ?? 1, int.MaxValue))));
        }
    }

    private async void StreamerBotDesigner_Click(object sender, RoutedEventArgs e)
    {
        var workspace = viewModel.Workspace;
        if (workspace?.Manifest.TargetDefinition is not { Length: > 0 } relativePath)
        {
            MessageBox.Show(
                this,
                "This project does not declare a targetDefinition.",
                "Streamer.bot Designer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await viewModel.SaveAllAsync(lifetimeCancellation.Token))
        {
            return;
        }

        var definitionPath = Path.GetFullPath(
            Path.Combine(
                workspace.ProjectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(definitionPath))
        {
            MessageBox.Show(
                this,
                $"The target definition does not exist:\n{definitionPath}",
                "Streamer.bot Designer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var dialog = new StreamerBotDesignerDialog(
                definitionPath,
                workspace.Manifest.Target?.Profile)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() == true)
            {
                await viewModel.OpenProjectAsync(
                    workspace.ProjectPath,
                    lifetimeCancellation.Token);
                if (dialog.RequestedSourcePath is { Length: > 0 } sourcePath)
                {
                    await NavigateToSourceAsync(new(
                        Path.Combine(workspace.ProjectRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar)),
                        1,
                        1));
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException)
        {
            ShowUnexpectedError(exception);
        }
    }

    private async void ImportStreamerBotCode_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new StreamerBotImportDialog(viewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CreatedProjectPath is { Length: > 0 } projectPath)
        {
            await RunBusyAsync(() => viewModel.OpenProjectAsync(projectPath, lifetimeCancellation.Token));
        }
    }

    private async void ObsPluginDesigner_Click(object sender, RoutedEventArgs e)
    {
        var workspace = viewModel.Workspace;
        if (workspace?.Manifest.ObsPlugin is null ||
            !string.Equals(
                workspace.Manifest.Target?.Provider,
                "obsstudio",
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "Open an OBS Studio plugin project before using the designer.",
                "OBS Plugin Designer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!await viewModel.SaveAllAsync(lifetimeCancellation.Token))
        {
            return;
        }

        var dialog = new ObsPluginDesignerDialog(workspace)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true &&
            dialog.UpdatedPlugin is { } plugin &&
            dialog.UpdatedDesign is { } design &&
            dialog.GeneratedSource is { } generatedSource)
        {
            await RunBusyAsync(() => viewModel.SaveObsPluginDesignAsync(
                plugin,
                design,
                generatedSource,
                lifetimeCancellation.Token));
        }
    }

    private async void PreviewDesigner_Click(object sender, RoutedEventArgs e)
    {
        var workspace = viewModel.Workspace;
        if (workspace is null)
        {
            MessageBox.Show(this, "Open a project before using Design Preview.", "Design Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!await viewModel.SaveAllAsync(lifetimeCancellation.Token)) return;

        var dialog = new PreviewDesignerDialog(
            workspace,
            viewModel.Settings,
            viewModel.Builder) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await viewModel.RefreshWorkspaceAsync(lifetimeCancellation.Token);
        }
    }

    private void PackageViewer_Click(object sender, RoutedEventArgs e)
    {
        var projectRoot = viewModel.Workspace?.ProjectRoot;
        if (projectRoot is null ||
            !File.Exists(Path.Combine(projectRoot, "build", "package-ir.json")))
        {
            MessageBox.Show(
                this,
                "Build the project before opening the package viewer.",
                "Package Viewer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            new PackageViewerDialog(projectRoot)
            {
                Owner = this,
            }.ShowDialog();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or System.Text.Json.JsonException)
        {
            ShowUnexpectedError(exception);
        }
    }

    private async void Deployment_Click(object sender, RoutedEventArgs e)
    {
        var workspace = viewModel.Workspace;
        if (workspace is null)
        {
            MessageBox.Show(
                this,
                "Open a project before managing deployment.",
                "Safe deployment",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (string.Equals(
                workspace.Manifest.Target?.Provider,
                "obsstudio",
                StringComparison.Ordinal))
        {
            var obsDialog = new ObsDeploymentDialog(workspace, viewModel.Settings)
            {
                Owner = this,
            };
            obsDialog.ShowDialog();
            if (obsDialog.SelectedInstallationRoot is { } obsRoot)
            {
                var remembered = (viewModel.Settings.ObsInstallations ?? [])
                    .Append(obsRoot)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                await viewModel.SaveSettingsAsync(
                    viewModel.Settings with { ObsInstallations = remembered },
                    lifetimeCancellation.Token);
            }

            return;
        }

        if (!string.Equals(
                workspace.Manifest.Target?.Provider,
                "streamerbot",
                StringComparison.Ordinal))
        {
            MessageBox.Show(
                this,
                "This project provider does not support managed deployment.",
                "Safe deployment",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        while (workspace is not null)
        {
            var dialog = new DeploymentDialog(workspace, viewModel.Settings)
            {
                Owner = this,
            };
            dialog.ShowDialog();
            if (dialog.SelectedInstallationRoot is { } selectedRoot)
            {
                var remembered = (viewModel.Settings.StreamerBotInstallations ?? [])
                    .Append(selectedRoot)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                await viewModel.SaveSettingsAsync(
                    viewModel.Settings with
                    {
                        StreamerBotInstallations = remembered,
                    },
                    lifetimeCancellation.Token);
            }

            if (!dialog.PackagingEnabled)
            {
                return;
            }

            if (!await viewModel.OpenProjectAsync(
                    workspace.ProjectPath,
                    lifetimeCancellation.Token) ||
                !await viewModel.BuildAsync(lifetimeCancellation.Token))
            {
                return;
            }

            workspace = viewModel.Workspace;
        }
    }

    private async void FormatDocument_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedDocument is { } document)
        {
            await RunBusyAsync(() =>
                viewModel.FormatDocumentAsync(
                    document,
                    lifetimeCancellation.Token));
        }
    }

    private void CphReference_Click(object sender, RoutedEventArgs e)
    {
        var profile = viewModel.Workspace?.Manifest.Target?.Profile ??
            "1.0.4-stable";
        var dialog = new CphReferenceDialog(
            CphIntelligenceProvider.Default.Catalogue,
            profile)
        {
            Owner = this,
        };
        dialog.ShowDialog();
    }

    private void ObsApiReference_Click(object sender, RoutedEventArgs e)
    {
        var profile = viewModel.Workspace?.Manifest.Target?.Profile ??
            "32.x-windows-x64";
        new ObsApiReferenceDialog(
            ObsNativeIntelligenceProvider.Default.Catalogue,
            profile)
        {
            Owner = this,
        }.ShowDialog();
    }

    private void SnippetBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedDocument is null ||
            FindVisualChild<CodeEditor>(DocumentTabs) is not { } editor)
        {
            MessageBox.Show(
                this,
                "Open a C# document before inserting a snippet.",
                "Snippet Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var profile = viewModel.Workspace?.Manifest.Target?.Profile ??
            "1.0.4-stable";
        var dialog = new SnippetBrowserDialog(
            SnippetProvider.Default,
            profile,
            viewModel.Workspace?.ProjectRoot)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true &&
            dialog.SelectedSnippetId is { } snippetId &&
            !editor.InsertGuidedSnippet(
                SnippetProvider.Default,
                snippetId,
                dialog.GuidedValues))
        {
            MessageBox.Show(
                this,
                "The snippet values are no longer valid. Reopen the browser and review the configuration.",
                "Snippet insertion failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void ReusableComponents_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Workspace is null)
        {
            MessageBox.Show(
                this,
                "Open a project before adding a reusable component.",
                "Reusable Components",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new ReusableComponentsDialog(viewModel.Workspace)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true && dialog.UpdatedWorkspace is not null)
        {
            var workspaceSetPath = viewModel.WorkspaceSet?.WorkspacePath;
            await RunBusyAsync(() => workspaceSetPath is null
                ? viewModel.OpenProjectAsync(dialog.UpdatedWorkspace.ProjectPath, lifetimeCancellation.Token)
                : viewModel.OpenWorkspaceSetAsync(workspaceSetPath, lifetimeCancellation.Token));
        }
    }

    private void CodeEditor_FormatRequested(object? sender, EventArgs e) =>
        FormatDocument_Click(sender ?? this, new RoutedEventArgs());

    private async void CodeEditor_DefinitionRequested(
        object? sender,
        EditorPositionEventArgs e)
    {
        if (e.Document is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var location = await viewModel.FindDefinitionAsync(
                e.Document,
                e.Offset,
                lifetimeCancellation.Token);
            return location is not null &&
                await NavigateToSourceAsync(location);
        });
    }

    private async void ProblemsList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ProblemsList.SelectedItem is not FoundryDiagnostic
            {
                Location:
                {
                    FilePath: { } filePath,
                    Line: { } line,
                },
            })
        {
            return;
        }

        var extension = Path.GetExtension(filePath);
        if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".c", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".h", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await NavigateToSourceAsync(
            new(
                filePath,
                checked((int)Math.Min(line, int.MaxValue)),
                checked((int)Math.Min(
                    ((FoundryDiagnostic)ProblemsList.SelectedItem).Location?.Column ?? 1,
                    int.MaxValue))));
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(viewModel.Settings)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await viewModel.SaveSettingsAsync(
                dialog.Settings!,
                lifetimeCancellation.Token);
            ((App)Application.Current).ApplyTheme(dialog.Settings!.Theme);
            UpdateAutosaveTimer();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            ShowUnexpectedError(exception);
        }
    }

    private async void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        var reset = viewModel.Settings with { Layout = ShellLayout.Default };
        await viewModel.SaveSettingsAsync(reset, lifetimeCancellation.Token);
        ApplyLayout(ShellLayout.Default);
    }

    private async void ObsSdkManager_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ObsSdkDialog(viewModel.Settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await viewModel.SaveSettingsAsync(
                    dialog.UpdatedSettings!,
                    lifetimeCancellation.Token);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ShowUnexpectedError(exception);
            }
        }
    }

    private async void RunSetup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProductSetupDialog(viewModel.Settings, services.StateRoot) { Owner = this };
        if (dialog.ShowDialog() == true)
            await viewModel.SaveSettingsAsync(dialog.CompletedSettings, lifetimeCancellation.Token);
    }

    private void RecoveryDiagnostics_Click(object sender, RoutedEventArgs e) =>
        new RecoveryDiagnosticsDialog(services, viewModel.Settings) { Owner = this }.ShowDialog();

    private void CheckUpdates_Click(object sender, RoutedEventArgs e) =>
        new UpdateDialog(viewModel.Settings, services.StateRoot) { Owner = this }.ShowDialog();

    private void Privacy_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Foundry has no telemetry and uploads nothing automatically. Projects, settings, recovery snapshots, build output, and failure reports remain local. Network access is disabled by default and is used only for an update check or SDK download that you explicitly start.",
        "Privacy and offline use", MessageBoxButton.OK, MessageBoxImage.Information);

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(
            this,
            "Creators Forge Foundry\n\nA source-first Streamer.bot extension and OBS plugin studio.",
            "About Creators Forge Foundry",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private async void ProjectTree_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is not ProjectTreeItemViewModel item)
        {
            return;
        }

        if (item.ProjectPath is { } projectPath &&
            !string.Equals(projectPath, viewModel.Workspace?.ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!await ConfirmDiscardOrSaveAsync() ||
                !await viewModel.ActivateProjectAsync(projectPath, lifetimeCancellation.Token))
                return;
            if (item.IsProjectRoot) return;
        }

        if (item.IsDirectory) return;

        if (!item.IsEditable)
        {
            MessageBox.Show(
                this,
                "This file type is not opened in the text workspace.",
                "Unsupported document",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await RunBusyAsync(() =>
            viewModel.OpenDocumentAsync(item.FullPath, lifetimeCancellation.Token));
    }

    private void ProjectTree_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private async void AddProjectItem_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.Workspace is null)
        {
            MessageBox.Show(
                this,
                "Open or create a project before adding files.",
                "Add project item",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var selectedItem = ProjectTree.SelectedItem as ProjectTreeItemViewModel;
        if (selectedItem?.ProjectPath is { } projectPath &&
            !string.Equals(projectPath, viewModel.Workspace.ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!await ConfirmDiscardOrSaveAsync() ||
                !await viewModel.ActivateProjectAsync(projectPath, lifetimeCancellation.Token))
            {
                return;
            }

            selectedItem = null;
        }

        var targetDirectory = selectedItem switch
        {
            { IsDirectory: true } => selectedItem.FullPath,
            not null => Path.GetDirectoryName(selectedItem.FullPath)!,
            _ => viewModel.Workspace.ProjectRoot,
        };
        var relativeTarget = Path.GetRelativePath(viewModel.Workspace.ProjectRoot, targetDirectory);
        var targetDescription = relativeTarget == "."
            ? $"Project: {viewModel.Workspace.Manifest.Name}"
            : $"Folder: {relativeTarget}";
        var dialog = new NewProjectItemDialog(targetDescription) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var created = await viewModel.CreateProjectItemAsync(
                targetDirectory,
                dialog.ItemName,
                dialog.SelectedKind,
                lifetimeCancellation.Token);
            if (created is { IsDirectory: false })
            {
                await viewModel.OpenDocumentAsync(created.FullPath, lifetimeCancellation.Token);
            }

            return created is not null;
        });
    }

    private async void RefreshProjectTree_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(() => viewModel.RefreshProjectTreeAsync(lifetimeCancellation.Token));

    private async void RenameProjectItem_Click(object sender, RoutedEventArgs e)
    {
        var item = await PrepareSelectedProjectItemMutationAsync();
        if (item is null)
        {
            return;
        }

        var dialog = new RenameProjectItemDialog(item.PhysicalName) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RunBusyAsync(() => viewModel.RenameProjectItemAsync(
                item.FullPath,
                dialog.NewName,
                lifetimeCancellation.Token));
        }
    }

    private void ProjectTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        projectTreeDragStart = e.GetPosition(ProjectTree);
        projectTreeDragItem = FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject)
            ?.DataContext as ProjectTreeItemViewModel;
    }

    private void ProjectTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            projectTreeDragItem is null ||
            projectTreeDragItem.IsProjectRoot)
        {
            return;
        }

        var position = e.GetPosition(ProjectTree);
        if (Math.Abs(position.X - projectTreeDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - projectTreeDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = projectTreeDragItem;
        projectTreeDragItem = null;
        if (item.ProjectPath is { } projectPath &&
            !string.Equals(projectPath, viewModel.Workspace?.ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var data = new DataObject(ProjectTreeItemDataFormat, item);
        DragDrop.DoDragDrop(ProjectTree, data, DragDropEffects.Move);
    }

    private void ProjectTree_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetProjectTreeDrop(e, out _, out _)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ProjectTree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TryGetProjectTreeDrop(e, out var source, out var destination))
        {
            return;
        }

        var blocker = viewModel.GetProjectItemMutationBlocker(source!.FullPath);
        if (blocker is not null)
        {
            MessageBox.Show(
                this,
                blocker,
                "Project item protected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await RunBusyAsync(() => viewModel.MoveProjectItemAsync(
            source.FullPath,
            GetProjectTreeDropDirectory(destination!),
            lifetimeCancellation.Token));
    }

    private bool TryGetProjectTreeDrop(
        DragEventArgs e,
        out ProjectTreeItemViewModel? source,
        out ProjectTreeItemViewModel? destination)
    {
        source = e.Data.GetData(ProjectTreeItemDataFormat) as ProjectTreeItemViewModel;
        destination = FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject)
            ?.DataContext as ProjectTreeItemViewModel;
        if (source is null || destination is null)
        {
            return false;
        }

        var destinationDirectory = GetProjectTreeDropDirectory(destination);

        if (string.Equals(
                Path.GetDirectoryName(source.FullPath),
                destinationDirectory,
                StringComparison.OrdinalIgnoreCase) ||
            (source.IsDirectory &&
             (string.Equals(source.FullPath, destinationDirectory, StringComparison.OrdinalIgnoreCase) ||
              destinationDirectory.StartsWith(
                  $"{Path.TrimEndingDirectorySeparator(source.FullPath)}{Path.DirectorySeparatorChar}",
                  StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        var sourceProject = source.ProjectPath ?? viewModel.Workspace?.ProjectPath;
        var destinationProject = destination.ProjectPath ?? viewModel.Workspace?.ProjectPath;
        return string.Equals(sourceProject, destinationProject, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(sourceProject, viewModel.Workspace?.ProjectPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProjectTreeDropDirectory(ProjectTreeItemViewModel target) =>
        target.IsDirectory ? target.FullPath : Path.GetDirectoryName(target.FullPath)!;

    private void ProjectTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F2)
        {
            e.Handled = true;
            RenameProjectItem_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete)
        {
            e.Handled = true;
            RecycleProjectItem_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            e.Handled = true;
            CopyProjectItemPath_Click(sender, e);
        }
    }

    private async void RecycleProjectItem_Click(object sender, RoutedEventArgs e)
    {
        var item = await PrepareSelectedProjectItemMutationAsync();
        if (item is null)
        {
            return;
        }

        var decision = MessageBox.Show(
            this,
            $"Move '{item.Name}' to the Recycle Bin?\n\nThis can be restored through Windows Recycle Bin.",
            "Remove project item",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision == MessageBoxResult.Yes)
        {
            await RunBusyAsync(() => viewModel.RecycleProjectItemAsync(
                item.FullPath,
                lifetimeCancellation.Token));
        }
    }

    private void RevealProjectItem_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectTree.SelectedItem is not ProjectTreeItemViewModel item)
        {
            return;
        }

        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        if (!item.IsDirectory)
        {
            start.ArgumentList.Add("/select,");
        }

        start.ArgumentList.Add(item.FullPath);
        using var process = Process.Start(start);
    }

    private void CopyProjectItemPath_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectTree.SelectedItem is ProjectTreeItemViewModel item)
        {
            Clipboard.SetText(item.RelativePath);
        }
    }

    private void CopyProjectItemFullPath_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectTree.SelectedItem is ProjectTreeItemViewModel item)
        {
            Clipboard.SetText(item.FullPath);
        }
    }

    private async Task<ProjectTreeItemViewModel?> PrepareSelectedProjectItemMutationAsync()
    {
        if (ProjectTree.SelectedItem is not ProjectTreeItemViewModel item)
        {
            return null;
        }

        if (item.IsProjectRoot)
        {
            MessageBox.Show(
                this,
                "The project root cannot be renamed or removed from Solution Explorer.",
                "Project item protected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        if (item.ProjectPath is { } projectPath &&
            !string.Equals(projectPath, viewModel.Workspace?.ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!await ConfirmDiscardOrSaveAsync() ||
                !await viewModel.ActivateProjectAsync(projectPath, lifetimeCancellation.Token))
            {
                return null;
            }
        }

        var blocker = viewModel.GetProjectItemMutationBlocker(item.FullPath);
        if (blocker is not null)
        {
            MessageBox.Show(
                this,
                blocker,
                "Project item protected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        return item;
    }

    private async void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        if (isBusy)
        {
            return;
        }

        try
        {
            await viewModel.AutosaveRecoveryAsync(lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            e.Handled = true;
            Save_Click(sender, e);
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) &&
                 e.Key == Key.S)
        {
            e.Handled = true;
            SaveAll_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
        {
            e.Handled = true;
            Build_Click(sender, e);
        }
        else if (Keyboard.Modifiers ==
                     (ModifierKeys.Control | ModifierKeys.Shift) &&
                 e.Key == Key.T)
        {
            e.Handled = true;
            TestExplorer_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            e.Handled = true;
            OpenProject_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            e.Handled = true;
            NewProject_Click(sender, e);
        }
        else if (Keyboard.Modifiers ==
                     (ModifierKeys.Control | ModifierKeys.Alt) &&
                 e.Key == Key.F)
        {
            e.Handled = true;
            FormatDocument_Click(sender, e);
        }
        else if (Keyboard.Modifiers ==
                     (ModifierKeys.Control | ModifierKeys.Shift) &&
                 e.Key == Key.I)
        {
            e.Handled = true;
            SnippetBrowser_Click(sender, e);
        }
        else if (IsPreviewShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            PreviewDesigner_Click(sender, e);
        }
        else if (IsTerminalShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            ToggleTerminal_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            e.Handled = true;
            Settings_Click(sender, e);
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.F1)
        {
            e.Handled = true;
            RunSetup_Click(sender, e);
        }

    }

    internal static bool IsPreviewShortcut(Key key, ModifierKeys modifiers) =>
        key == Key.P &&
        modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

    internal static bool IsTerminalShortcut(Key key, ModifierKeys modifiers) =>
        key == Key.T && modifiers == ModifierKeys.Control;

    private async Task<bool> NavigateToSourceAsync(EditorSourceLocation location)
    {
        if (!await viewModel.OpenDocumentAsync(
            location.FilePath,
            lifetimeCancellation.Token))
        {
            return false;
        }

        await Dispatcher.InvokeAsync(
            () =>
            {
                var editor = FindVisualChild<CodeEditor>(DocumentTabs);
                editor?.NavigateTo(location.Line, location.Column);
            },
            DispatcherPriority.Loaded);
        return true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose)
        {
            lifetimeCancellation.Cancel();
            return;
        }

        e.Cancel = true;
        if (!await ConfirmDiscardOrSaveAsync())
        {
            return;
        }

        autosaveTimer.Stop();
        try
        {
            var settings = viewModel.Settings with { Layout = CaptureLayout() };
            await viewModel.SaveSettingsAsync(settings, CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var decision = MessageBox.Show(
                this,
                $"Settings could not be saved:\n{exception.Message}\n\nExit anyway?",
                "Creators Forge Foundry",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decision != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await StopTerminalForShutdownAsync();
        allowClose = true;
        Close();
    }

    private async Task<bool> ConfirmDiscardOrSaveAsync()
    {
        if (!viewModel.HasDirtyDocuments)
        {
            return true;
        }

        var decision = MessageBox.Show(
            this,
            "Save all modified documents before continuing?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return decision switch
        {
            MessageBoxResult.Yes =>
                await viewModel.SaveAllAsync(lifetimeCancellation.Token),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private async Task RunBusyAsync(Func<Task<bool>> operation)
    {
        if (isBusy)
        {
            return;
        }

        isBusy = true;
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowUnexpectedError(exception);
        }
        finally
        {
            isBusy = false;
        }
    }

    private void ApplyLayout(ShellLayout layout)
    {
        WindowState = WindowState.Normal;
        Width = layout.WindowWidth;
        Height = layout.WindowHeight;
        Left = Math.Clamp(
            layout.WindowLeft,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenLeft +
            Math.Max(0, SystemParameters.VirtualScreenWidth - Width));
        Top = Math.Clamp(
            layout.WindowTop,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop +
            Math.Max(0, SystemParameters.VirtualScreenHeight - Height));
        ProjectColumn.Width = new(layout.ProjectPanelWidth);
        InspectorColumn.Width = new(layout.InspectorPanelWidth);
        BottomRow.Height = new(layout.BottomPanelHeight);

        if (layout.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private ShellLayout CaptureLayout()
    {
        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        return new(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            ProjectColumn.ActualWidth,
            InspectorColumn.ActualWidth,
            BottomRow.ActualHeight,
            WindowState == WindowState.Maximized);
    }

    private void UpdateAutosaveTimer()
    {
        autosaveTimer.Stop();
        autosaveTimer.Interval = TimeSpan.FromSeconds(
            viewModel.Settings.AutosaveSeconds);
        autosaveTimer.Start();
    }

    private async void ShowUnexpectedError(Exception exception)
    {
        string report;
        try { report = await services.FailureReports.WriteAsync(exception, "Desktop operation"); }
        catch { report = "The local failure report could not be written."; }
        MessageBox.Show(this, $"{exception.Message}\n\nLocal failure report:\n{report}", "Creators Forge Foundry", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private Task<bool> OpenPathAsync(
        string path,
        CancellationToken cancellationToken,
        bool recordRecent = true) =>
        path.EndsWith(".foundryworkspace", StringComparison.OrdinalIgnoreCase)
            ? viewModel.OpenWorkspaceSetAsync(path, cancellationToken, recordRecent)
            : viewModel.OpenProjectAsync(path, cancellationToken, recordRecent);

    private static string? FindCommonDirectory(string[] paths)
    {
        if (paths.Length == 0) return null;
        var candidate = Path.GetDirectoryName(Path.GetFullPath(paths[0]));
        while (candidate is not null && paths.Any(path =>
                   !Path.GetFullPath(path).StartsWith(
                       Path.TrimEndingDirectorySeparator(candidate) + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase)))
        {
            candidate = Path.GetDirectoryName(candidate);
        }
        return candidate;
    }

    protected override void OnClosed(EventArgs e)
    {
        autosaveTimer.Stop();
        lifetimeCancellation.Cancel();
        terminalSession.DisposeAsync().AsTask().GetAwaiter().GetResult();
        lifetimeCancellation.Dispose();
        base.OnClosed(e);
    }
}
