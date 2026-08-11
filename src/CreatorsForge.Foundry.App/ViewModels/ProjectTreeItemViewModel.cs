using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public sealed class ProjectTreeItemViewModel
{
    private static readonly HashSet<string> EditableExtensions =
        new HashSet<string>(
            [
                ".cs", ".cpp", ".cxx", ".cc", ".c", ".h", ".hpp",
                ".foundryproj", ".json", ".md", ".txt", ".xml", ".html",
                ".htm", ".css", ".js", ".ts", ".props", ".targets", ".resx",
                ".yml", ".yaml", ".cmake",
            ],
            StringComparer.OrdinalIgnoreCase);

    public ProjectTreeItemViewModel(
        ProjectTreeNode node,
        string? projectPath = null,
        IReadOnlyDictionary<string, string>? displayLabels = null)
    {
        Name = displayLabels is not null && displayLabels.TryGetValue(node.RelativePath, out var displayName)
            ? displayName
            : node.Name;
        FullPath = node.FullPath;
        RelativePath = node.RelativePath;
        IsDirectory = node.IsDirectory;
        ProjectPath = projectPath;
        Children = node.Children
            .Select(child => new ProjectTreeItemViewModel(child, projectPath, displayLabels))
            .ToArray();
    }

    public ProjectTreeItemViewModel(
        FoundryWorkspace workspace,
        bool isActive,
        IReadOnlyDictionary<string, string>? displayLabels = null)
    {
        Name = isActive ? $"● {workspace.Manifest.Name}" : workspace.Manifest.Name;
        FullPath = workspace.ProjectRoot;
        RelativePath = workspace.Manifest.Name;
        IsDirectory = true;
        IsProjectRoot = true;
        ProjectPath = workspace.ProjectPath;
        Children = workspace.ProjectTree
            .Select(node => new ProjectTreeItemViewModel(node, workspace.ProjectPath, displayLabels))
            .ToArray();
    }

    public string Name { get; }

    public string PhysicalName => Path.GetFileName(FullPath);

    public string FullPath { get; }

    public string RelativePath { get; }

    public bool IsDirectory { get; }

    public bool IsProjectRoot { get; }

    public string? ProjectPath { get; }

    public bool IsEditable =>
        !IsDirectory && EditableExtensions.Contains(Path.GetExtension(FullPath));

    public string IconLabel => GetIconLabel();

    public IReadOnlyList<ProjectTreeItemViewModel> Children { get; }

    private string GetIconLabel()
    {
        if (IsProjectRoot) return "SLN";
        if (IsDirectory) return "▸";

        return Path.GetExtension(FullPath).ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".cpp" or ".cxx" or ".cc" => "C++",
            ".c" => "C",
            ".h" or ".hpp" => "H",
            ".json" => "{ }",
            ".xml" or ".html" or ".htm" => "<>",
            ".css" => "CSS",
            ".js" => "JS",
            ".ts" => "TS",
            ".md" => "MD",
            ".foundryproj" => "CF",
            ".yml" or ".yaml" => "YML",
            ".props" or ".targets" or ".cmake" => "⚙",
            ".txt" when string.Equals(Name, "CMakeLists.txt", StringComparison.OrdinalIgnoreCase) => "CMake",
            ".txt" => "TXT",
            _ => "FILE",
        };
    }
}
