using System.Collections.ObjectModel;
using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Build.ObsStudio;
using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Editor;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly RecentProjectsStore recentProjectsStore;
    private readonly FoundrySettingsStore settingsStore;
    private readonly RecoveryStore recoveryStore;
    private readonly FoundryBuildOrchestrator builder;
    private readonly IRoslynEditorService editor;
    private readonly ICphIntelligenceService cphIntelligence;
    private readonly IObsNativeIntelligenceService obsNativeIntelligence;
    private readonly List<FoundryDiagnostic> editorDiagnostics = [];

    private FoundryWorkspace? workspace;
    private FoundryWorkspaceSet? workspaceSet;
    private DocumentViewModel? selectedDocument;
    private FoundryUserSettings settings = FoundryUserSettings.CreateDefault();
    private string buildLog = "Build output will appear here.";
    private string consoleLog = "Creators Forge Foundry desktop shell ready.";
    private string statusText = "Ready";
    private long editorAnalysisRevision;

    public MainWindowViewModel(
        RecentProjectsStore recentProjectsStore,
        FoundrySettingsStore settingsStore,
        RecoveryStore recoveryStore,
        FoundryBuildOrchestrator builder,
        IRoslynEditorService editor,
        ICphIntelligenceService cphIntelligence,
        IObsNativeIntelligenceService obsNativeIntelligence)
    {
        this.recentProjectsStore = recentProjectsStore;
        this.settingsStore = settingsStore;
        this.recoveryStore = recoveryStore;
        this.builder = builder;
        this.editor = editor;
        this.cphIntelligence = cphIntelligence;
        this.obsNativeIntelligence = obsNativeIntelligence;
    }

    public ObservableCollection<ProjectTreeItemViewModel> ProjectItems { get; } = [];

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public ObservableCollection<FoundryDiagnostic> Problems { get; } = [];

    public ObservableCollection<RecentProjectEntry> RecentProjects { get; } = [];

    public FoundryBuildOrchestrator Builder => builder;

    public FoundryWorkspace? Workspace
    {
        get => workspace;
        private set
        {
            if (SetProperty(ref workspace, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(TargetSummary));
                OnPropertyChanged(nameof(HasWorkspace));
            }
        }
    }

    public FoundryWorkspaceSet? WorkspaceSet
    {
        get => workspaceSet;
        private set
        {
            if (SetProperty(ref workspaceSet, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ProjectName));
                OnPropertyChanged(nameof(TargetSummary));
            }
        }
    }

    public DocumentViewModel? SelectedDocument
    {
        get => selectedDocument;
        set => SetProperty(ref selectedDocument, value);
    }

    public FoundryUserSettings Settings
    {
        get => settings;
        private set => SetProperty(ref settings, value);
    }

    public string WindowTitle => Workspace is null
        ? "Creators Forge Foundry"
        : $"{(WorkspaceSet?.Manifest.Name ?? Workspace.Manifest.Name)} — Creators Forge Foundry";

    public string ProjectName => WorkspaceSet is null
        ? Workspace?.Manifest.Name ?? "No project open"
        : $"{WorkspaceSet.Manifest.Name} ({WorkspaceSet.Projects.Count} projects)";

    public string TargetSummary => Workspace?.Manifest.Target is null
        ? "No target selected"
        : $"{Workspace.Manifest.Target.Provider} · {Workspace.Manifest.Target.Profile}" +
          (WorkspaceSet is null ? string.Empty : " · active project");

    public bool HasWorkspace => Workspace is not null;

    public bool HasDirtyDocuments => Documents.Any(document => document.IsDirty);

    public string BuildLog
    {
        get => buildLog;
        private set => SetProperty(ref buildLog, value);
    }

    public string ConsoleLog
    {
        get => consoleLog;
        private set => SetProperty(ref consoleLog, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settingsResult = await settingsStore.LoadAsync(cancellationToken);
        Settings = settingsResult.Value;
        AddDiagnostics(settingsResult.Diagnostics);
        await RefreshRecentProjectsAsync(cancellationToken);
    }

    public async Task<bool> OpenProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        StatusText = "Opening project…";
        var result = await FoundryWorkspaceService.OpenAsync(
            projectPath,
            cancellationToken);
        ReplaceProblems(result.Diagnostics);
        if (!result.IsSuccess)
        {
            StatusText = "Project open failed";
            return false;
        }

        WorkspaceSet = null;
        ApplyWorkspace(result.Value!, clearDocuments: true);
        await recentProjectsStore.SaveOpenedProjectAsync(
            result.Value!.ProjectPath,
            result.Value.Manifest.Name,
            cancellationToken);
        await RefreshRecentProjectsAsync(cancellationToken);
        AppendConsole($"Opened {result.Value.ProjectPath}");
        StatusText = "Project opened";
        return true;
    }


    public async Task<bool> OpenWorkspaceSetAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        StatusText = "Opening workspace…";
        var result = await FoundryWorkspaceSetService.LoadAsync(workspacePath, cancellationToken);
        ReplaceProblems(result.Diagnostics);
        if (!result.IsSuccess)
        {
            StatusText = "Workspace open failed";
            return false;
        }

        WorkspaceSet = result.Value;
        ApplyWorkspace(result.Value!.ActiveProject, clearDocuments: true);
        PopulateWorkspaceProjects();
        await recentProjectsStore.SaveOpenedProjectAsync(
            result.Value.WorkspacePath,
            result.Value.Manifest.Name,
            cancellationToken);
        await RefreshRecentProjectsAsync(cancellationToken);
        AppendConsole($"Opened workspace {result.Value.WorkspacePath}");
        StatusText = "Workspace opened";
        return true;
    }

    public Task<bool> ActivateProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WorkspaceSet is null) return Task.FromResult(false);
        WorkspaceSet = FoundryWorkspaceSetService.Activate(WorkspaceSet, projectPath);
        ApplyWorkspace(WorkspaceSet.ActiveProject, clearDocuments: true);
        PopulateWorkspaceProjects();
        AppendConsole($"Activated {Workspace!.Manifest.Name}");
        StatusText = "Active project changed";
        return Task.FromResult(true);
    }

    public async Task<bool> CreateProjectAsync(
        FoundryProjectCreationRequest request,
        CancellationToken cancellationToken)
    {
        StatusText = "Creating project…";
        var result = await FoundryWorkspaceService.CreateAsync(
            request,
            cancellationToken);
        ReplaceProblems(result.Diagnostics);
        if (!result.IsSuccess)
        {
            StatusText = "Project creation failed";
            return false;
        }

        WorkspaceSet = null;
        ApplyWorkspace(result.Value!, clearDocuments: true);
        await recentProjectsStore.SaveOpenedProjectAsync(
            result.Value!.ProjectPath,
            result.Value.Manifest.Name,
            cancellationToken);
        await RefreshRecentProjectsAsync(cancellationToken);
        AppendConsole($"Created {result.Value.ProjectPath}");
        StatusText = "Project created";
        return true;
    }

    public async Task<bool> OpenDocumentAsync(
        string documentPath,
        CancellationToken cancellationToken)
    {
        if (Workspace is null)
        {
            return false;
        }

        var existing = Documents.FirstOrDefault(document =>
            string.Equals(
                document.FullPath,
                documentPath,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedDocument = existing;
            return true;
        }

        if (TryGetObsSdkReferencePath(documentPath, out var referencePath))
        {
            return await OpenObsSdkReferenceAsync(referencePath, cancellationToken);
        }

        var result = await WorkspaceDocumentService.LoadAsync(
            Workspace.ProjectRoot,
            documentPath,
            cancellationToken);
        AddDiagnostics(result.Diagnostics);
        if (!result.IsSuccess)
        {
            StatusText = "Document open failed";
            return false;
        }

        var loaded = result.Value!;
        var document = new DocumentViewModel(
            loaded.FullPath,
            loaded.RelativePath,
            loaded.Text,
            loaded.LastWriteUtc,
            Workspace.Manifest.Target?.Profile ?? "1.0.4-stable");
        var recovery = await recoveryStore.ReadAsync(
            loaded.FullPath,
            cancellationToken);
        if (recovery is not null && recovery.RecoveredAtUtc > loaded.LastWriteUtc)
        {
            document.Restore(recovery.Text);
            AppendConsole($"Recovered unsaved content for {loaded.RelativePath}");
        }

        Documents.Add(document);
        document.PropertyChanged += Document_PropertyChanged;
        SelectedDocument = document;
        StatusText = loaded.RelativePath;
        await AnalyzeEditorAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SaveDocumentAsync(
        DocumentViewModel document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsReadOnly)
        {
            StatusText = "SDK reference headers are read-only.";
            return true;
        }

        if (Workspace is null)
        {
            return false;
        }

        var result = await WorkspaceDocumentService.SaveAsync(
            Workspace.ProjectRoot,
            document.FullPath,
            document.Text,
            cancellationToken);
        AddDiagnostics(result.Diagnostics);
        if (!result.IsSuccess)
        {
            StatusText = "Save failed";
            return false;
        }

        document.MarkSaved(result.Value!.LastWriteUtc);
        try
        {
            await recoveryStore.DeleteAsync(document.FullPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            AddDiagnostics(
                [
                    new FoundryDiagnostic(
                        "CFW2201",
                        FoundryDiagnosticSeverity.Warning,
                        $"The document was saved, but its recovery snapshot could not be removed: {exception.Message}",
                        new FoundryDiagnosticLocation(document.FullPath))
                ]);
        }

        StatusText = $"Saved {document.RelativePath}";
        return true;
    }

    public async Task<bool> SaveAllAsync(CancellationToken cancellationToken)
    {
        foreach (var document in Documents.Where(item => item.IsDirty).ToArray())
        {
            if (!await SaveDocumentAsync(document, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<bool> SaveObsPluginDesignAsync(
        FoundryObsPlugin plugin,
        FoundryObsDesign design,
        string generatedSource,
        CancellationToken cancellationToken)
    {
        if (Workspace is null)
        {
            return false;
        }

        StatusText = "Saving OBS design…";
        var result = await FoundryWorkspaceService.SaveObsPluginDesignAsync(
            Workspace,
            plugin,
            design,
            generatedSource,
            cancellationToken);
        AddDiagnostics(result.Diagnostics);
        if (!result.IsSuccess)
        {
            StatusText = "OBS design save failed";
            return false;
        }

        var updatedWorkspace = result.Value!;
        ApplyWorkspace(updatedWorkspace, clearDocuments: false);
        var sourcePath = Path.GetFullPath(Path.Combine(
            updatedWorkspace.ProjectRoot,
            design.Source.Replace('/', Path.DirectorySeparatorChar)));
        var openSource = Documents.FirstOrDefault(document =>
            string.Equals(document.FullPath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (openSource is not null)
        {
            openSource.Reload(generatedSource, File.GetLastWriteTimeUtc(sourcePath));
        }

        await AnalyzeEditorAsync(cancellationToken);
        AppendConsole($"Saved OBS design using {design.Template}");
        StatusText = "OBS design saved";
        return true;
    }

    public async Task AutosaveRecoveryAsync(CancellationToken cancellationToken)
    {
        foreach (var document in Documents.Where(item => item.IsDirty))
        {
            try
            {
                await recoveryStore.WriteAsync(
                    document.FullPath,
                    document.Text,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                AddDiagnostics(
                    [new(
                        "CFW2201",
                        FoundryDiagnosticSeverity.Warning,
                        $"Recovery autosave failed for {document.RelativePath}: {exception.Message}")]);
            }
        }
    }

    public async Task<bool> BuildAsync(CancellationToken cancellationToken)
    {
        if (Workspace is null || !await SaveAllAsync(cancellationToken))
        {
            return false;
        }

        StatusText = "Building…";
        BuildLog = "Validating workspace…";
        var refreshed = await FoundryWorkspaceService.OpenAsync(
            Workspace.ProjectPath,
            cancellationToken);
        ReplaceProblems(refreshed.Diagnostics);
        if (!refreshed.IsSuccess)
        {
            BuildLog = "Build stopped because the project manifest is invalid.";
            StatusText = "Build failed";
            return false;
        }

        Workspace = refreshed.Value;
        var result = await builder.BuildAsync(
            Workspace!.Manifest,
            Workspace.ProjectPath,
            cancellationToken);
        AddDiagnostics(result.Diagnostics);
        if (!result.IsSuccess)
        {
            BuildLog = string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}"));
            StatusText = "Build failed";
            return false;
        }

        var targetRevision = result.PackageIntermediate!.Target.CphCatalogueRevision is { } cphRevision
            ? $"CPH catalogue: {cphRevision}"
            : $"OBS API: {result.PackageIntermediate.Target.ObsApiVersion}; SDK: {result.PackageIntermediate.Target.ObsSdkVersion ?? "none"}";
        BuildLog = string.Join(
            Environment.NewLine,
            new[]
            {
                $"Build succeeded: {Workspace.Manifest.Name}",
                targetRevision,
            }
                .Concat(result.PackageIntermediate!.Artifacts.Select(artifact =>
                    $"{artifact.Kind}: build/{artifact.Path} ({artifact.Sha256})"))
                .Append("Package IR: build/package-ir.json"));
        AppendConsole($"Built {Workspace.Manifest.Name} {Workspace.Manifest.Version}");
        StatusText = "Build succeeded";
        return true;
    }

    public async Task<bool> RefreshWorkspaceAsync(CancellationToken cancellationToken)
    {
        if (Workspace is null)
        {
            return false;
        }

        var refreshed = await FoundryWorkspaceService.OpenAsync(
            Workspace.ProjectPath,
            cancellationToken);
        ReplaceProblems(refreshed.Diagnostics);
        if (!refreshed.IsSuccess)
        {
            StatusText = "Project refresh failed";
            return false;
        }

        ApplyWorkspace(refreshed.Value!, clearDocuments: false);
        return true;
    }

    public async Task<bool> BuildWorkspaceSetAsync(CancellationToken cancellationToken)
    {
        if (WorkspaceSet is null) return await BuildAsync(cancellationToken);
        if (!await SaveAllAsync(cancellationToken)) return false;

        StatusText = "Building workspace…";
        var lines = new List<string> { $"Workspace: {WorkspaceSet.Manifest.Name}" };
        var diagnostics = new List<FoundryDiagnostic>();
        foreach (var project in WorkspaceSet.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refreshed = await FoundryWorkspaceService.OpenAsync(project.ProjectPath, cancellationToken);
            diagnostics.AddRange(refreshed.Diagnostics);
            if (!refreshed.IsSuccess)
            {
                lines.Add($"FAILED  {project.Manifest.Name}: project validation");
                continue;
            }

            var result = await builder.BuildAsync(refreshed.Value!.Manifest, refreshed.Value.ProjectPath, cancellationToken);
            diagnostics.AddRange(result.Diagnostics);
            lines.Add(result.IsSuccess
                ? $"PASSED  {project.Manifest.Name} {project.Manifest.Version}"
                : $"FAILED  {project.Manifest.Name}");
        }

        ReplaceProblems(diagnostics);
        BuildLog = string.Join(Environment.NewLine, lines);
        var succeeded = !diagnostics.Any(item => item.IsError) && lines.Skip(1).All(line => line.StartsWith("PASSED", StringComparison.Ordinal));
        StatusText = succeeded ? "Workspace build succeeded" : "Workspace build failed";
        AppendConsole(succeeded ? $"Built workspace {WorkspaceSet.Manifest.Name}" : $"Workspace build failed: {WorkspaceSet.Manifest.Name}");
        return succeeded;
    }

    public async Task<bool> ReleaseAsync(CancellationToken cancellationToken)
    {
        if (Workspace is null || !await SaveAllAsync(cancellationToken))
        {
            return false;
        }

        StatusText = "Creating release…";
        BuildLog = "Validating and building release…";
        var refreshed = await FoundryWorkspaceService.OpenAsync(
            Workspace.ProjectPath,
            cancellationToken);
        ReplaceProblems(refreshed.Diagnostics);
        if (!refreshed.IsSuccess)
        {
            BuildLog = "Release stopped because the project manifest is invalid.";
            StatusText = "Release failed";
            return false;
        }

        Workspace = refreshed.Value;
        var buildResult = await builder.BuildAsync(
            Workspace!.Manifest,
            Workspace.ProjectPath,
            cancellationToken);
        AddDiagnostics(buildResult.Diagnostics);
        if (!buildResult.IsSuccess)
        {
            BuildLog = string.Join(
                Environment.NewLine,
                buildResult.Diagnostics.Select(item =>
                    $"{item.Severity} {item.Code}: {item.Message}"));
            StatusText = "Release build failed";
            return false;
        }

        var releaseResult = await new FoundryReleasePackager().CreateAsync(
            Workspace.Manifest,
            Workspace.ProjectPath,
            buildResult,
            cancellationToken);
        AddDiagnostics(releaseResult.Diagnostics.Except(buildResult.Diagnostics));
        if (!releaseResult.IsSuccess)
        {
            BuildLog = string.Join(
                Environment.NewLine,
                releaseResult.Diagnostics.Select(item =>
                    $"{item.Severity} {item.Code}: {item.Message}"));
            StatusText = "Release failed";
            return false;
        }

        BuildLog = string.Join(
            Environment.NewLine,
            $"Release succeeded: {Workspace.Manifest.Name} {Workspace.Manifest.Version}",
            $"Bundle: {releaseResult.ReleaseDirectory}",
            $"Archive: {releaseResult.ArchivePath}",
            $"Manifest: {releaseResult.ManifestPath}",
            $"Verified files: {releaseResult.Manifest!.Files.Count}");
        AppendConsole($"Released {Workspace.Manifest.Name} {Workspace.Manifest.Version}");
        StatusText = "Release succeeded";
        return true;
    }

    public async Task<bool> SavePublishingSettingsAsync(
        FoundryPublishing publishing,
        string version,
        CancellationToken cancellationToken)
    {
        if (Workspace is null) return false;
        if (!await SaveAllAsync(cancellationToken)) return false;

        var refreshed = await FoundryWorkspaceService.OpenAsync(
            Workspace.ProjectPath,
            cancellationToken);
        ReplaceProblems(refreshed.Diagnostics);
        if (!refreshed.IsSuccess)
        {
            BuildLog = string.Join(
                Environment.NewLine,
                refreshed.Diagnostics.Select(item =>
                    $"{item.Severity} {item.Code}: {item.Message}"));
            StatusText = "Publishing settings were not saved";
            return false;
        }

        var result = await FoundryPublishingService.SaveReleaseSettingsAsync(
            refreshed.Value!, publishing, version, cancellationToken);
        ReplaceProblems(result.Diagnostics);
        if (!result.IsSuccess)
        {
            BuildLog = string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(item =>
                    $"{item.Severity} {item.Code}: {item.Message}"));
            StatusText = "Publishing settings were not saved";
            return false;
        }

        var savedWorkspace = result.Value!;
        SynchronizeWorkspaceSet(savedWorkspace);
        ApplyWorkspace(savedWorkspace, clearDocuments: false);
        await ReloadOpenDocumentAsync(savedWorkspace.ProjectPath, cancellationToken);
        if (WorkspaceSet is not null)
        {
            PopulateWorkspaceProjects();
        }

        StatusText = "Publishing settings saved";
        AppendConsole($"Updated publishing metadata for {Workspace.Manifest.Name} {Workspace.Manifest.Version}");
        return true;
    }

    public Task<bool> ValidatePublishingAsync(CancellationToken cancellationToken) =>
        RunPublishingAsync(createArchive: false, cancellationToken);

    public Task<bool> PublishAsync(CancellationToken cancellationToken) =>
        RunPublishingAsync(createArchive: true, cancellationToken);

    private async Task<bool> RunPublishingAsync(bool createArchive, CancellationToken cancellationToken)
    {
        if (Workspace is null || !await SaveAllAsync(cancellationToken)) return false;
        StatusText = createArchive ? "Publishing release…" : "Validating publishing readiness…";
        var refreshed = await FoundryWorkspaceService.OpenAsync(Workspace.ProjectPath, cancellationToken);
        if (!refreshed.IsSuccess)
        {
            ReplaceProblems(refreshed.Diagnostics);
            BuildLog = "Publishing stopped because the project manifest is invalid.";
            StatusText = "Publishing validation failed";
            return false;
        }

        ApplyWorkspace(refreshed.Value!, clearDocuments: false);
        var build = await builder.BuildAsync(Workspace.Manifest, Workspace.ProjectPath, cancellationToken);
        if (!build.IsSuccess)
        {
            ReplaceProblems(build.Diagnostics);
            BuildLog = string.Join(Environment.NewLine, build.Diagnostics.Select(item => $"{item.Severity} {item.Code}: {item.Message}"));
            StatusText = "Publishing build failed";
            return false;
        }

        if (!createArchive)
        {
            var readiness = FoundryPublishingReadinessService.Inspect(Workspace.Manifest, Workspace.ProjectPath, build);
            ReplaceProblems(build.Diagnostics.Concat(readiness.Diagnostics));
            BuildLog = string.Join(Environment.NewLine,
                readiness.Checklist.Select(item => $"{(item.Passed ? "PASSED" : item.Required ? "FAILED" : "OPTIONAL")}  {item.Name} — {item.Details}"));
            StatusText = readiness.IsReady ? "Publishing validation passed" : "Publishing validation failed";
            AppendConsole(readiness.IsReady ? "Publishing validation passed" : "Publishing validation failed");
            return readiness.IsReady;
        }

        var release = await new FoundryReleasePackager().CreatePublishingAsync(
            Workspace.Manifest, Workspace.ProjectPath, build, cancellationToken);
        ReplaceProblems(release.Diagnostics);
        if (!release.IsSuccess)
        {
            BuildLog = string.Join(Environment.NewLine, release.Diagnostics.Select(item => $"{item.Severity} {item.Code}: {item.Message}"));
            StatusText = "Publishing failed";
            return false;
        }

        BuildLog = string.Join(Environment.NewLine,
            $"Publishing succeeded: {Workspace.Manifest.Name} {Workspace.Manifest.Version}",
            $"Archive: {release.ArchivePath}",
            $"Release manifest: {release.ManifestPath}",
            $"Reproducibility report: {release.ReproducibilityReportPath}",
            $"Dependencies recorded: {release.Manifest!.Dependencies.Count}",
            $"Signed files: {release.Manifest.Signing.SignedFiles.Count}");
        StatusText = "Publishing succeeded";
        AppendConsole($"Published {Workspace.Manifest.Name} {Workspace.Manifest.Version}");
        return true;
    }

    public async Task<bool> FormatDocumentAsync(
        DocumentViewModel document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(
            Path.GetExtension(document.FullPath),
            ".cs",
            StringComparison.OrdinalIgnoreCase))
        {
            StatusText = IsNativePath(document.FullPath)
                ? "Native formatting is not bundled; use the build diagnostics to verify C17 source."
                : "Formatting is available for C# documents.";
            return false;
        }

        var sources = await LoadEditorSourcesAsync(cancellationToken);
        document.Text = await editor.FormatAsync(
            sources,
            document.FullPath,
            cancellationToken);
        StatusText = $"Formatted {document.RelativePath}";
        await AnalyzeEditorAsync(cancellationToken);
        return true;
    }

    public async Task<EditorSourceLocation?> FindDefinitionAsync(
        DocumentViewModel document,
        int position,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsNativePath(document.FullPath))
        {
            var definition = obsNativeIntelligence.FindDefinition(
                document.Text,
                position);
            if (definition is null)
            {
                StatusText = "OBS definition not found in the pinned catalogue";
                return null;
            }

            var status = ObsSdkManager.Inspect();
            if (!status.IsReady)
            {
                StatusText = "Install or repair the pinned OBS SDK to open headers";
                return null;
            }

            var headerPath = Path.Combine(
                status.SdkRoot,
                "sources",
                "libobs",
                definition.Header.Replace('/', Path.DirectorySeparatorChar));
            var nativeLocation = await FindNativeHeaderSymbolAsync(
                headerPath,
                definition.Symbol,
                cancellationToken);
            StatusText = nativeLocation is null
                ? "Definition not found in the installed SDK header"
                : $"Pinned OBS SDK {status.Version} definition found";
            return nativeLocation;
        }

        var sources = await LoadEditorSourcesAsync(cancellationToken);
        var location = await editor.FindDefinitionAsync(
            sources,
            document.FullPath,
            position,
            cancellationToken);
        StatusText = location is null ? "Definition not found" : "Definition found";
        return location;
    }

    public async Task SaveSettingsAsync(
        FoundryUserSettings newSettings,
        CancellationToken cancellationToken)
    {
        await settingsStore.SaveAsync(newSettings, cancellationToken);
        Settings = newSettings;
    }

    public void CloseDocument(DocumentViewModel document)
    {
        document.PropertyChanged -= Document_PropertyChanged;
        var index = Documents.IndexOf(document);
        Documents.Remove(document);
        SelectedDocument = Documents.Count == 0
            ? null
            : Documents[Math.Clamp(index, 0, Documents.Count - 1)];
        QueueEditorAnalysis();
    }

    public void CloseWorkspace()
    {
        editorAnalysisRevision++;
        UnsubscribeDocuments();
        Workspace = null;
        WorkspaceSet = null;
        ProjectItems.Clear();
        Documents.Clear();
        SelectedDocument = null;
        Problems.Clear();
        StatusText = "Ready";
        AppendConsole("Closed workspace");
    }

    private void ApplyWorkspace(FoundryWorkspace value, bool clearDocuments)
    {
        Workspace = value;
        ProjectItems.Clear();
        foreach (var item in value.ProjectTree)
        {
            ProjectItems.Add(new(item));
        }

        if (clearDocuments)
        {
            UnsubscribeDocuments();
            Documents.Clear();
            SelectedDocument = null;
        }
    }

    private void SynchronizeWorkspaceSet(FoundryWorkspace updatedProject)
    {
        if (WorkspaceSet is null)
        {
            return;
        }

        var projects = WorkspaceSet.Projects
            .Select(project => string.Equals(
                project.ProjectPath,
                updatedProject.ProjectPath,
                StringComparison.OrdinalIgnoreCase)
                ? updatedProject
                : project)
            .ToArray();
        WorkspaceSet = WorkspaceSet with
        {
            Projects = projects,
            ActiveProject = updatedProject,
        };
    }

    private async Task ReloadOpenDocumentAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        var document = Documents.FirstOrDefault(item => string.Equals(
            item.FullPath,
            fullPath,
            StringComparison.OrdinalIgnoreCase));
        if (document is null)
        {
            return;
        }

        var persistedText = await File.ReadAllTextAsync(fullPath, cancellationToken);
        document.Reload(persistedText, File.GetLastWriteTimeUtc(fullPath));
        try
        {
            await recoveryStore.DeleteAsync(fullPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            AddDiagnostics(
                [new(
                    "CFW2201",
                    FoundryDiagnosticSeverity.Warning,
                    $"Publishing settings were saved, but the old recovery snapshot could not be removed: {exception.Message}",
                    new FoundryDiagnosticLocation(fullPath))]);
        }
    }

    private void PopulateWorkspaceProjects()
    {
        if (WorkspaceSet is null) return;
        ProjectItems.Clear();
        foreach (var project in WorkspaceSet.Projects)
        {
            ProjectItems.Add(new(
                project,
                string.Equals(
                    project.ProjectPath,
                    Workspace!.ProjectPath,
                    StringComparison.OrdinalIgnoreCase)));
        }
    }

    private async Task RefreshRecentProjectsAsync(CancellationToken cancellationToken)
    {
        var result = await recentProjectsStore.LoadAsync(cancellationToken);
        RecentProjects.Clear();
        foreach (var entry in result.Value)
        {
            RecentProjects.Add(entry);
        }

        AddDiagnostics(result.Diagnostics);
    }

    private void ReplaceProblems(IEnumerable<FoundryDiagnostic> diagnostics)
    {
        Problems.Clear();
        editorDiagnostics.Clear();
        AddDiagnostics(diagnostics);
    }

    private void AddDiagnostics(IEnumerable<FoundryDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Problems.Add(diagnostic);
        }
    }

    private async Task AnalyzeEditorAsync(CancellationToken cancellationToken)
    {
        var revision = ++editorAnalysisRevision;
        var sources = await LoadEditorSourcesAsync(cancellationToken);
        var result = await AnalyzeSourcesAsync(sources, cancellationToken);
        if (revision == editorAnalysisRevision)
        {
            ReplaceEditorDiagnostics(result.Diagnostics);
        }
    }

    private async Task<EditorSourceDocument[]> LoadEditorSourcesAsync(
        CancellationToken cancellationToken)
    {
        if (Workspace is null)
        {
            return [];
        }

        var relativePaths = Workspace.Manifest.NativeBuild is { } nativeBuild
            ? nativeBuild.Sources
                .Concat(Documents
                    .Where(document => IsNativePath(document.FullPath) && !document.IsReadOnly)
                    .Select(document => document.RelativePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
            : Workspace.Manifest.ManagedBuild?.Sources ?? [];

        var sources = new List<EditorSourceDocument>();
        foreach (var relativePath in relativePaths)
        {
            var fullPath = Path.GetFullPath(
                Path.Combine(
                    Workspace.ProjectRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var supported = Workspace.Manifest.NativeBuild is not null
                ? IsNativePath(fullPath)
                : string.Equals(
                    Path.GetExtension(fullPath),
                    ".cs",
                    StringComparison.OrdinalIgnoreCase);
            if (!supported)
            {
                continue;
            }

            var openDocument = Documents.FirstOrDefault(document =>
                string.Equals(
                    document.FullPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase));
            if (openDocument is not null)
            {
                sources.Add(new(fullPath, openDocument.Text));
                continue;
            }

            var loaded = await WorkspaceDocumentService.LoadAsync(
                Workspace.ProjectRoot,
                fullPath,
                cancellationToken);
            if (loaded.IsSuccess)
            {
                sources.Add(new(fullPath, loaded.Value!.Text));
            }
        }

        return [.. sources];
    }

    private void Document_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.Text))
        {
            QueueEditorAnalysis();
        }
    }

    private void QueueEditorAnalysis()
    {
        var revision = ++editorAnalysisRevision;
        _ = AnalyzeEditorAfterDelayAsync(revision);
    }

    private async Task AnalyzeEditorAfterDelayAsync(long revision)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350));
            if (revision != editorAnalysisRevision || Workspace is null)
            {
                return;
            }

            var sources = await LoadEditorSourcesAsync(CancellationToken.None);
            var result = await AnalyzeSourcesAsync(sources, CancellationToken.None);
            if (revision == editorAnalysisRevision)
            {
                ReplaceEditorDiagnostics(result.Diagnostics);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or InvalidOperationException
                or UnauthorizedAccessException)
        {
            if (revision == editorAnalysisRevision)
            {
                ReplaceEditorDiagnostics(
                    [
                        new(
                            "CFE0001",
                            FoundryDiagnosticSeverity.Warning,
                            $"Live C# analysis failed: {exception.Message}")
                    ]);
            }
        }
    }

    private void ReplaceEditorDiagnostics(
        IEnumerable<FoundryDiagnostic> diagnostics)
    {
        foreach (var diagnostic in editorDiagnostics)
        {
            Problems.Remove(diagnostic);
        }

        editorDiagnostics.Clear();
        foreach (var diagnostic in diagnostics)
        {
            editorDiagnostics.Add(diagnostic);
            Problems.Add(diagnostic);
        }
    }

    private async Task<EditorAnalysisResult> AnalyzeSourcesAsync(
        IReadOnlyList<EditorSourceDocument> sources,
        CancellationToken cancellationToken)
    {
        if (Workspace?.Manifest.NativeBuild is not null)
        {
            var nativeProfile = Workspace.Manifest.Target?.Profile ??
                "32.x-windows-x64";
            return new(sources.SelectMany(source =>
                obsNativeIntelligence.Analyze(
                    source.Text,
                    source.FilePath,
                    nativeProfile).Diagnostics).ToArray());
        }

        var roslyn = await editor.AnalyzeAsync(sources, cancellationToken);
        var profile = Workspace?.Manifest.Target?.Profile ?? "1.0.4-stable";
        var diagnostics = roslyn.Diagnostics
            .Concat(sources.SelectMany(source =>
                cphIntelligence.Analyze(
                    source.Text,
                    source.FilePath,
                    profile).Diagnostics))
            .ToArray();
        return new(diagnostics);
    }

    private async Task<bool> OpenObsSdkReferenceAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var document = new DocumentViewModel(
                fullPath,
                $"OBS SDK/{Path.GetFileName(fullPath)}",
                text,
                File.GetLastWriteTimeUtc(fullPath),
                Workspace?.Manifest.Target?.Profile ?? "32.x-windows-x64",
                isReadOnly: true);
            Documents.Add(document);
            SelectedDocument = document;
            StatusText = $"Read-only OBS SDK {ObsSdkManager.Version} header";
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            AddDiagnostics(
                [
                    new(
                        "CFN1004",
                        FoundryDiagnosticSeverity.Error,
                        $"The pinned OBS SDK header could not be opened: {exception.Message}",
                        new FoundryDiagnosticLocation(fullPath))
                ]);
            return false;
        }
    }

    private static bool TryGetObsSdkReferencePath(
        string candidate,
        out string fullPath)
    {
        fullPath = Path.GetFullPath(candidate);
        var status = ObsSdkManager.Inspect();
        if (!status.IsReady || !File.Exists(fullPath))
        {
            return false;
        }

        var headerRoot = Path.GetFullPath(Path.Combine(
            status.SdkRoot,
            "sources",
            "libobs"));
        return fullPath.StartsWith(
            $"{Path.TrimEndingDirectorySeparator(headerRoot)}{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetExtension(fullPath), ".h", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<EditorSourceLocation?> FindNativeHeaderSymbolAsync(
        string headerPath,
        string symbol,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(headerPath))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(headerPath, cancellationToken);
        for (var index = 0; index < lines.Length; index++)
        {
            var column = lines[index].IndexOf(symbol, StringComparison.Ordinal);
            if (column >= 0)
            {
                return new(headerPath, index + 1, column + 1);
            }
        }

        return null;
    }

    private static bool IsNativePath(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".c", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".h", StringComparison.OrdinalIgnoreCase);
    }

    private void UnsubscribeDocuments()
    {
        foreach (var document in Documents)
        {
            document.PropertyChanged -= Document_PropertyChanged;
        }
    }

    private void AppendConsole(string message) =>
        ConsoleLog = $"{ConsoleLog}{Environment.NewLine}{DateTime.Now:T}  {message}";
}
