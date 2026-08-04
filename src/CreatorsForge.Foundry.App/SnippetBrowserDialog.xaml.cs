using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Editor;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class SnippetBrowserDialog : Window
{
    private SnippetService snippets;
    private readonly string profile;
    private readonly string? projectRoot;
    private readonly Dictionary<int, Control> fieldInputs = [];

    public SnippetBrowserDialog(
        SnippetService snippets,
        string profile,
        string? projectRoot = null)
    {
        this.snippets = snippets;
        this.profile = profile;
        this.projectRoot = projectRoot;
        InitializeComponent();
        ProfileText.Text = $"Profile: {profile}";
        RevisionText.Text = $"Built-in catalogue {snippets.Catalogue.Revision}";
        KindComboBox.SelectedIndex = 0;
        RefreshSnippets();
        Loaded += (_, _) => SearchTextBox.Focus();
    }

    public string? SelectedSnippetId { get; private set; }

    public IReadOnlyDictionary<int, string> GuidedValues { get; private set; } =
        new Dictionary<int, string>();

    private void Filter_Changed(
        object sender,
        EventArgs e) =>
        RefreshSnippets();

    private void SnippetsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        ShowSelectedSnippet();

    private void RefreshSnippets()
    {
        if (SnippetsList is null)
        {
            return;
        }

        var selectedId = (SnippetsList.SelectedItem as SnippetDefinition)?.Id;
        var filter = SearchTextBox?.Text?.Trim() ?? string.Empty;
        var selectedKind = (KindComboBox?.SelectedItem as ComboBoxItem)?.Tag
            as string ?? "all";
        var filtered = snippets.Catalogue.Snippets
            .Where(snippet =>
                snippet.Profiles.Contains(profile, StringComparer.Ordinal))
            .Where(snippet =>
                selectedKind == "all" ||
                string.Equals(
                    snippet.Kind,
                    selectedKind,
                    StringComparison.Ordinal))
            .Where(snippet =>
                filter.Length == 0 ||
                snippet.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                snippet.Description.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                snippet.Prefixes.Any(prefix =>
                    prefix.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                snippet.Categories.Any(category =>
                    category.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(snippet => snippet.Prefixes[0], StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SnippetsList.ItemsSource = filtered;
        SnippetsList.SelectedItem = filtered.FirstOrDefault(snippet =>
            string.Equals(snippet.Id, selectedId, StringComparison.Ordinal));
        if (SnippetsList.SelectedItem is null && filtered.Length != 0)
        {
            SnippetsList.SelectedIndex = 0;
        }

        if (filtered.Length == 0)
        {
            ClearDetails("No compatible snippets match this filter.");
        }
    }

    private void ShowSelectedSnippet()
    {
        if (SnippetsList.SelectedItem is not SnippetDefinition snippet)
        {
            ClearDetails("Select a snippet.");
            return;
        }

        SnippetNameText.Text = snippet.Name;
        SnippetMetaText.Text =
            $"{snippet.Prefixes[0]}  ·  {snippet.Kind}  ·  {snippet.Source}";
        SnippetDescriptionText.Text = snippet.Description;
        SecurityText.Text = FormatSecurity(snippet.Security);
        BuildGuide(snippet);
        RefreshPreview();
    }

    private void BuildGuide(SnippetDefinition snippet)
    {
        GuidedFieldsPanel.Children.Clear();
        fieldInputs.Clear();
        var defaults = snippets.Expand(snippet.Id, string.Empty, "\n")
            .Placeholders
            .Where(placeholder => placeholder.Index > 0)
            .ToDictionary(
                placeholder => placeholder.Index,
                placeholder => placeholder.DefaultValue);
        foreach (var field in snippets.GetGuideFields(snippet.Id))
        {
            GuidedFieldsPanel.Children.Add(new TextBlock
            {
                Margin = new(0, 9, 0, 2),
                FontWeight = FontWeights.SemiBold,
                Text = field.Label,
            });
            GuidedFieldsPanel.Children.Add(new TextBlock
            {
                Margin = new(0, 0, 0, 4),
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                Text = field.Description,
                TextWrapping = TextWrapping.Wrap,
            });

            var defaultValue = defaults.GetValueOrDefault(
                field.Index,
                string.Empty);
            Control input;
            if (field.Options.Count != 0)
            {
                var comboBox = new ComboBox
                {
                    Padding = new(7, 5, 7, 5),
                    ItemsSource = field.Options,
                    SelectedItem = field.Options.Contains(
                        defaultValue,
                        StringComparer.Ordinal)
                        ? defaultValue
                        : field.Options[0],
                };
                comboBox.SelectionChanged += GuidedValue_Changed;
                input = comboBox;
            }
            else
            {
                var textBox = new TextBox
                {
                    Padding = new(7, 5, 7, 5),
                    Text = defaultValue,
                };
                textBox.TextChanged += GuidedValue_Changed;
                input = textBox;
            }

            System.Windows.Automation.AutomationProperties.SetName(
                input,
                field.Label);
            fieldInputs[field.Index] = input;
            GuidedFieldsPanel.Children.Add(input);
        }
    }

    private void GuidedValue_Changed(object sender, EventArgs e) =>
        RefreshPreview();

    private void RefreshPreview()
    {
        if (SnippetsList.SelectedItem is not SnippetDefinition snippet)
        {
            return;
        }

        var values = ReadValues();
        var result = snippets.ExpandGuided(
            snippet.Id,
            values,
            string.Empty,
            Environment.NewLine);
        PreviewTextBox.Text = result.Expansion?.Text ?? string.Empty;
        ValidationText.Text = string.Join(Environment.NewLine, result.Errors);
        InsertButton.IsEnabled = result.IsSuccess;
    }

    private Dictionary<int, string> ReadValues() =>
        fieldInputs.ToDictionary(
            item => item.Key,
            item => item.Value switch
            {
                TextBox textBox => textBox.Text,
                ComboBox comboBox => comboBox.SelectedItem as string ?? string.Empty,
                _ => string.Empty,
            });

    private void InsertButton_Click(object sender, RoutedEventArgs e)
    {
        if (SnippetsList.SelectedItem is not SnippetDefinition snippet)
        {
            return;
        }

        var values = ReadValues();
        if (!snippets.ExpandGuided(
            snippet.Id,
            values,
            string.Empty,
            Environment.NewLine).IsSuccess)
        {
            RefreshPreview();
            return;
        }

        SelectedSnippetId = snippet.Id;
        GuidedValues = values;
        DialogResult = true;
    }

    private async void ImportCatalogue_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".json",
            Filter = "Snippet catalogues (*.json)|*.json",
            Title = "Import user snippet catalogue",
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var result = await SnippetProvider.ImportUserCatalogueAsync(
                dialog.FileName,
                SnippetProvider.UserDirectory,
                projectRoot);
            if (result.Diagnostics.Any(item => item.IsError))
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Catalogue not imported", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            snippets = result.Service;
            RevisionText.Text = $"Combined catalogue {snippets.Catalogue.Revision} ({result.LoadedFiles.Count} external)";
            RefreshSnippets();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, "Catalogue not imported", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearDetails(string message)
    {
        SnippetNameText.Text = message;
        SnippetMetaText.Text = string.Empty;
        SnippetDescriptionText.Text = string.Empty;
        SecurityText.Text = string.Empty;
        GuidedFieldsPanel.Children.Clear();
        fieldInputs.Clear();
        PreviewTextBox.Text = string.Empty;
        ValidationText.Text = string.Empty;
        InsertButton.IsEnabled = false;
    }

    private static string FormatSecurity(SnippetSecurity security)
    {
        var capabilities = new List<string>();
        if (security.FileAccess)
        {
            capabilities.Add("file access");
        }

        if (security.NetworkAccess)
        {
            capabilities.Add("network access");
        }

        if (security.ProcessExecution)
        {
            capabilities.Add("process execution");
        }

        return capabilities.Count == 0
            ? "Security: no file, network, or process access declared."
            : $"Security declarations: {string.Join(", ", capabilities)}.";
    }
}
