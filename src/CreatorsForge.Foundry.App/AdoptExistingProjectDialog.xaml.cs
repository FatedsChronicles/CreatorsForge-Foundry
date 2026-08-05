using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class AdoptExistingProjectDialog : Window
{
    private readonly ExternalProjectAnalysis analysis;

    public AdoptExistingProjectDialog(ExternalProjectAnalysis analysis)
    {
        this.analysis = analysis;
        InitializeComponent();
        FolderTextBox.Text = analysis.ProjectDirectory;
        var folderName = new DirectoryInfo(analysis.ProjectDirectory).Name;
        NameTextBox.Text = folderName;
        IdTextBox.Text = $"com.example.{CreateSlug(folderName)}";
        ProviderComboBox.SelectedIndex = analysis.ManagedSources.Count > 0 ? 0 : 1;
    }

    public ExternalProjectAdoptionRequest? Request { get; private set; }

    private void Provider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileComboBox is null || PreviewList is null || SummaryText is null || SafetyText is null)
        {
            return;
        }

        var provider = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var isObs = string.Equals(provider, "obsstudio", StringComparison.Ordinal);
        ProfileComboBox.Items.Clear();
        if (isObs)
        {
            ProfileComboBox.Items.Add("32.x-windows-x64");
        }
        else
        {
            foreach (var profile in FoundryStreamerBotProfiles.Ordered)
            {
                ProfileComboBox.Items.Add(profile);
            }
        }
        ProfileComboBox.SelectedIndex = 0;

        var sources = isObs ? analysis.NativeSources : analysis.ManagedSources;
        PreviewList.ItemsSource = sources;
        SummaryText.Text = isObs
            ? $"{sources.Count} C source file(s) will become native build inputs."
            : $"{sources.Count} C# source file(s) will become managed build inputs.";
        SafetyText.Text =
            $"{analysis.OtherFiles.Count} other file(s) remain available in Solution Explorer but are not added to build inputs. " +
            $"{analysis.SkippedDirectoryCount} ignored, deep, or linked folder(s) were skipped.";
        AdoptButton.IsEnabled = sources.Count > 0;
    }

    private void Adopt_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var id = IdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id))
        {
            MessageBox.Show(this, "Project name and ID are required.", "Adopt existing project", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var provider = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "streamerbot";
        Request = new(
            analysis,
            name,
            id,
            ProfileComboBox.SelectedItem?.ToString() ?? "1.0.4-stable",
            provider,
            AuthorTextBox.Text.Trim());
        DialogResult = true;
    }

    private static string CreateSlug(string value)
    {
        var slug = string.Concat(value.ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-')).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "existing-project" : slug;
    }
}
