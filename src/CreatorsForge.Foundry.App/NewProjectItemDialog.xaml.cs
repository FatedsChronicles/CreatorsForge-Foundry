using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class NewProjectItemDialog : Window
{
    private string? suggestedName;

    public NewProjectItemDialog(string targetDescription)
    {
        TargetDescription = targetDescription;
        InitializeComponent();
        ItemTypeComboBox.SelectedIndex = 0;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    public string TargetDescription { get; }

    public IReadOnlyList<ProjectItemTypeOption> ItemTypes { get; } =
    [
        new("C# class or script", WorkspaceProjectItemKind.CSharp, "NewScript.cs"),
        new("C++ source", WorkspaceProjectItemKind.Cpp, "plugin.cpp"),
        new("C source", WorkspaceProjectItemKind.C, "module.c"),
        new("C/C++ header", WorkspaceProjectItemKind.Header, "module.h"),
        new("JSON document", WorkspaceProjectItemKind.Json, "settings.json"),
        new("XML document", WorkspaceProjectItemKind.Xml, "document.xml"),
        new("HTML document", WorkspaceProjectItemKind.Html, "panel.html"),
        new("CSS stylesheet", WorkspaceProjectItemKind.Css, "panel.css"),
        new("JavaScript", WorkspaceProjectItemKind.JavaScript, "panel.js"),
        new("Markdown document", WorkspaceProjectItemKind.Markdown, "README.md"),
        new("Text document", WorkspaceProjectItemKind.Text, "notes.txt"),
        new("CMake list", WorkspaceProjectItemKind.CMake, "CMakeLists.txt"),
        new("Folder", WorkspaceProjectItemKind.Folder, "NewFolder"),
    ];

    public WorkspaceProjectItemKind SelectedKind =>
        ((ProjectItemTypeOption)ItemTypeComboBox.SelectedItem).Kind;

    public string ItemName => NameTextBox.Text.Trim();

    private void ItemTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemTypeComboBox.SelectedItem is not ProjectItemTypeOption option)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(NameTextBox.Text) ||
            string.Equals(NameTextBox.Text, suggestedName, StringComparison.Ordinal))
        {
            suggestedName = option.SuggestedName;
            NameTextBox.Text = suggestedName;
            NameTextBox.SelectAll();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ItemName))
        {
            MessageBox.Show(this, "Enter a file or folder name.", "Add project item", MessageBoxButton.OK, MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    public sealed record ProjectItemTypeOption(
        string DisplayName,
        WorkspaceProjectItemKind Kind,
        string SuggestedName)
    {
        public override string ToString() => DisplayName;
    }
}
