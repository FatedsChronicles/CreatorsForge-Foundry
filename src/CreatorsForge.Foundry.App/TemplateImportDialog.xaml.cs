using System.Windows;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class TemplateImportDialog : Window
{
    private readonly string templatePath;

    public TemplateImportDialog(
        string templatePath,
        FoundryTemplatePackage package,
        FoundryUserSettings settings)
    {
        this.templatePath = templatePath;
        InitializeComponent();
        TemplateNameText.Text = package.Name;
        TemplateDescriptionText.Text = $"{package.Provider} · {package.Version} · {package.Description}";
        ProjectNameTextBox.Text = package.Name;
        ProjectIdTextBox.Text = package.Project!.Id;
        ProfileComboBox.ItemsSource = package.Provider == "obsstudio"
            ? new[] { "32.x-windows-x64" }
            : FoundryStreamerBotProfiles.Ordered;
        ProfileComboBox.SelectedItem = package.Project.Target!.Profile;
        if (ProfileComboBox.SelectedItem is null) ProfileComboBox.SelectedIndex = 0;
        ProjectDirectoryTextBox.Text = Path.Combine(settings.DefaultProjectDirectory, string.Concat(package.Name.Where(char.IsLetterOrDigit)));
    }

    public FoundryTemplateImportRequest? Request { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = false, Title = "Select an empty project folder" };
        if (dialog.ShowDialog(this) == true) ProjectDirectoryTextBox.Text = dialog.FolderName;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text) || string.IsNullOrWhiteSpace(ProjectIdTextBox.Text) ||
            string.IsNullOrWhiteSpace(ProjectDirectoryTextBox.Text) || ProfileComboBox.SelectedItem is not string profile)
        {
            MessageBox.Show(this, "Project name, ID, profile, and folder are required.", "Import Template", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Request = new(templatePath, ProjectDirectoryTextBox.Text.Trim(), ProjectNameTextBox.Text.Trim(), ProjectIdTextBox.Text.Trim(), profile);
        DialogResult = true;
    }
}
