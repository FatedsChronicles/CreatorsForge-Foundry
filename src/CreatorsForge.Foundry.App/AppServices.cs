using CreatorsForge.Foundry.Build;
using CreatorsForge.Foundry.Editor;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public sealed class AppServices
{
    private AppServices(
        RecentProjectsStore recentProjects,
        FoundrySettingsStore settings,
        RecoveryStore recovery,
        FoundryFailureReportService failureReports,
        string stateRoot,
        FoundryBuildOrchestrator builder,
        IRoslynEditorService editor,
        ICphIntelligenceService cphIntelligence,
        IObsNativeIntelligenceService obsNativeIntelligence)
    {
        RecentProjects = recentProjects;
        Settings = settings;
        Recovery = recovery;
        FailureReports = failureReports;
        StateRoot = stateRoot;
        Builder = builder;
        Editor = editor;
        CphIntelligence = cphIntelligence;
        ObsNativeIntelligence = obsNativeIntelligence;
    }

    public RecentProjectsStore RecentProjects { get; }

    public FoundrySettingsStore Settings { get; }

    public RecoveryStore Recovery { get; }

    public FoundryFailureReportService FailureReports { get; }

    public string StateRoot { get; }

    public FoundryBuildOrchestrator Builder { get; }

    public IRoslynEditorService Editor { get; }

    public ICphIntelligenceService CphIntelligence { get; }

    public IObsNativeIntelligenceService ObsNativeIntelligence { get; }

    public static AppServices CreateDefault()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Creators Forge",
            "Foundry");
        return new(
            new RecentProjectsStore(Path.Combine(root, "recent-projects.json")),
            new FoundrySettingsStore(Path.Combine(root, "settings.json")),
            new RecoveryStore(Path.Combine(root, "recovery")),
            new FoundryFailureReportService(Path.Combine(root, "failures")),
            root,
            new FoundryBuildOrchestrator(),
            new RoslynEditorService(),
            CphIntelligenceProvider.Default,
            ObsNativeIntelligenceProvider.Default);
    }

    public MainWindowViewModel CreateMainWindowViewModel() => new(
        RecentProjects,
        Settings,
        Recovery,
        Builder,
        Editor,
        CphIntelligence,
        ObsNativeIntelligence);
}
