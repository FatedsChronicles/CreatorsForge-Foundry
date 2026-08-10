using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class NewProjectDialog : Window
{
    public NewProjectDialog(FoundryUserSettings settings)
    {
        InitializeComponent();
        ProjectLocationTextBox.Text = settings.DefaultProjectDirectory;
        RefreshProviderOptions();
    }

    public FoundryProjectCreationRequest? Request { get; private set; }

    private void TargetProvider_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ProfileComboBox is null || ProjectTypeDescriptionText is null ||
            TemplateComboBox is null)
        {
            return;
        }

        RefreshProviderOptions();
    }

    private void RefreshProviderOptions()
    {
        var provider = (TargetProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        ProfileComboBox.Items.Clear();
        TemplateComboBox.ItemsSource = FoundryProjectTemplateService.Templates
            .Where(item => string.Equals(item.Provider, provider, StringComparison.Ordinal))
            .ToArray();
        if (string.Equals(provider, "obsstudio", StringComparison.Ordinal))
        {
            ProfileComboBox.Items.Add(new ComboBoxItem { Content = "32.x-windows-x64" });
            ProjectTypeDescriptionText.Text =
                "Foundry creates a C17 Windows x64 video filter using the pinned OBS 32.1.2 SDK for exact verified OBS 32.1.2 and 32.2.1 runtimes, generated module adapter, deterministic plugin ZIP, and package IR.";
        }
        else
        {
            foreach (var profile in FoundryStreamerBotProfiles.Ordered)
            {
                ProfileComboBox.Items.Add(new ComboBoxItem { Content = profile });
            }
            ProjectTypeDescriptionText.Text =
                "Foundry creates a managed library, args-log-v1 CPHInline bridge, structured Streamer.bot definition, and stable-v23 import package.";
        }

        ProfileComboBox.SelectedIndex = 0;
        TemplateComboBox.SelectedIndex = TemplateComboBox.Items.Count > 1 ? 1 : 0;
    }

    private void Template_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateComboBox?.SelectedItem is not FoundryProjectTemplateDescriptor template ||
            DescriptionTextBox is null)
        {
            return;
        }

        DescriptionTextBox.Text = template.Description;
        ProjectTypeDescriptionText.Text = $"{template.Kind}: {template.Description}";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(ProjectLocationTextBox.Text)
                ? ProjectLocationTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false,
            Title = "Select parent folder",
        };
        if (dialog.ShowDialog(this) == true)
        {
            ProjectLocationTextBox.Text = dialog.FolderName;
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = ProjectNameTextBox.Text.Trim();
        var id = ProjectIdTextBox.Text.Trim();
        var parent = ProjectLocationTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(parent))
        {
            MessageBox.Show(
                this,
                "Project name, ID, and parent folder are required.",
                "Create project",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var folderName = string.Concat(name.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(folderName))
        {
            MessageBox.Show(
                this,
                "Project name must contain at least one letter or digit.",
                "Create project",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var profile = (ProfileComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ??
            FoundryStreamerBotProfiles.Stable107;
        var provider = (TargetProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ??
            "streamerbot";
        var template = TemplateComboBox.SelectedItem as FoundryProjectTemplateDescriptor;
        Request = new(
            Path.Combine(parent, folderName),
            name,
            id,
            profile,
            provider,
            template?.Id,
            AuthorTextBox.Text.Trim(),
            DescriptionTextBox.Text.Trim());
        DialogResult = true;
    }
}
