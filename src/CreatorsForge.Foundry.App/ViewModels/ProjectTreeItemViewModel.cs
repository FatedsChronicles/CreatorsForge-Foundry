using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public sealed class ProjectTreeItemViewModel
{
    private static readonly HashSet<string> EditableExtensions =
        new HashSet<string>(
            [
                ".cs", ".c", ".h", ".foundryproj", ".json", ".md", ".txt", ".xml",
                ".props", ".targets", ".resx", ".yml", ".yaml",
            ],
            StringComparer.OrdinalIgnoreCase);

    public ProjectTreeItemViewModel(ProjectTreeNode node, string? projectPath = null)
    {
        Name = node.Name;
        FullPath = node.FullPath;
        RelativePath = node.RelativePath;
        IsDirectory = node.IsDirectory;
        ProjectPath = projectPath;
        Children = node.Children
            .Select(child => new ProjectTreeItemViewModel(child, projectPath))
            .ToArray();
    }

    public ProjectTreeItemViewModel(FoundryWorkspace workspace, bool isActive)
    {
        Name = isActive ? $"● {workspace.Manifest.Name}" : workspace.Manifest.Name;
        FullPath = workspace.ProjectRoot;
        RelativePath = workspace.Manifest.Name;
        IsDirectory = true;
        IsProjectRoot = true;
        ProjectPath = workspace.ProjectPath;
        Children = workspace.ProjectTree
            .Select(node => new ProjectTreeItemViewModel(node, workspace.ProjectPath))
            .ToArray();
    }

    public string Name { get; }

    public string FullPath { get; }

    public string RelativePath { get; }

    public bool IsDirectory { get; }

    public bool IsProjectRoot { get; }

    public string? ProjectPath { get; }

    public bool IsEditable =>
        !IsDirectory && EditableExtensions.Contains(Path.GetExtension(FullPath));

    public IReadOnlyList<ProjectTreeItemViewModel> Children { get; }
}
