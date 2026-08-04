using CreatorsForge.Foundry.Core.Diagnostics;
using CreatorsForge.Foundry.Core.Projects;

namespace CreatorsForge.Foundry.Workspaces;

public sealed record FoundryWorkspace(
    string ProjectPath,
    string ProjectRoot,
    FoundryProjectManifest Manifest,
    IReadOnlyList<ProjectTreeNode> ProjectTree);

public sealed record ProjectTreeNode(
    string Name,
    string FullPath,
    string RelativePath,
    bool IsDirectory,
    IReadOnlyList<ProjectTreeNode> Children);

public sealed record WorkspaceOperationResult<T>(
    T? Value,
    IReadOnlyList<FoundryDiagnostic> Diagnostics)
    where T : class
{
    public bool IsSuccess =>
        Value is not null && Diagnostics.All(diagnostic => !diagnostic.IsError);
}

public sealed record StateLoadResult<T>(
    T Value,
    IReadOnlyList<FoundryDiagnostic> Diagnostics);

public sealed record FoundryProjectCreationRequest(
    string ProjectDirectory,
    string Name,
    string Id,
    string TargetProfile,
    string TargetProvider = "streamerbot",
    string? TemplateId = null,
    string Author = "Creator",
    string? Description = null);

public sealed record WorkspaceDocument(
    string FullPath,
    string RelativePath,
    string Text,
    DateTimeOffset LastWriteUtc);

public sealed record RecentProjectEntry(
    string ProjectPath,
    string Name,
    DateTimeOffset LastOpenedUtc);

public sealed record RecoveryDocument(
    string DocumentPath,
    string Text,
    DateTimeOffset RecoveredAtUtc);

public sealed record ShellLayout(
    double WindowLeft = 120,
    double WindowTop = 80,
    double WindowWidth = 1440,
    double WindowHeight = 900,
    double ProjectPanelWidth = 280,
    double InspectorPanelWidth = 300,
    double BottomPanelHeight = 230,
    bool IsMaximized = false)
{
    public static ShellLayout Default { get; } = new();
}

public enum FoundryThemePreference
{
    System,
    Dark,
    Light,
}

public sealed record FoundryThemePalette(
    string Window,
    string Panel,
    string Border,
    string Text,
    string MutedText,
    string Accent,
    string Editor,
    string MenuSelection,
    string Button,
    string Error);

public static class FoundryThemePalettes
{
    public static FoundryThemePalette Dark { get; } = new(
        Window: "#15181D",
        Panel: "#1D2128",
        Border: "#4A5361",
        Text: "#F5F7FA",
        MutedText: "#B9C2CF",
        Accent: "#FF9D32",
        Editor: "#101318",
        MenuSelection: "#3D4A5C",
        Button: "#29313C",
        Error: "#FF8A80");

    public static FoundryThemePalette Light { get; } = new(
        Window: "#F5F7FA",
        Panel: "#FFFFFF",
        Border: "#9AA5B1",
        Text: "#17202B",
        MutedText: "#52606F",
        Accent: "#A84400",
        Editor: "#FFFFFF",
        MenuSelection: "#D7E5F5",
        Button: "#E8EDF3",
        Error: "#B42318");
}

public sealed record FoundryUserSettings(
    string DefaultProjectDirectory,
    int AutosaveSeconds,
    ShellLayout Layout,
    IReadOnlyList<string>? StreamerBotInstallations = null,
    IReadOnlyList<string>? ObsInstallations = null,
    bool FirstRunCompleted = false,
    string? UpdateManifestLocation = "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/latest/download/foundry-update.json",
    bool AllowNetworkAccess = false,
    bool IncludePathsInDiagnosticBundles = false,
    FoundryThemePreference Theme = FoundryThemePreference.System)
{
    public const string DefaultUpdateManifestLocation =
        "https://github.com/FatedsChronicles/CreatorsForge-Foundry/releases/latest/download/foundry-update.json";

    public static FoundryUserSettings CreateDefault() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Creators Forge Foundry"),
        30,
        ShellLayout.Default,
        [],
        [],
        false,
        DefaultUpdateManifestLocation,
        false,
        false,
        FoundryThemePreference.System);
}
