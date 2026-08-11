using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Build.StreamerBot;

namespace CreatorsForge.Foundry.App;

public partial class StreamerBotOperationPaletteDialog : Window
{
    private static readonly StreamerBotOperationCatalogueService Catalogue =
        StreamerBotOperationCatalogueService.LoadEmbedded();
    private readonly string entityKind;
    private readonly string? profile;
    private readonly IReadOnlyList<OperationReferenceChoice> commands;
    private readonly Dictionary<string, FrameworkElement> editors = new(StringComparer.Ordinal);

    public StreamerBotOperationPaletteDialog(
        string entityKind,
        string? profile,
        IReadOnlyList<OperationReferenceChoice>? commands = null)
    {
        this.entityKind = entityKind;
        this.profile = profile;
        this.commands = commands ?? [];
        InitializeComponent();
        Title = entityKind == "trigger" ? "Add Streamer.bot trigger" : "Add Streamer.bot sub-action";
        RefreshOperations();
    }

    public StreamerBotOperationSelection? Selection { get; private set; }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshOperations();

    private void RefreshOperations()
    {
        if (!IsInitialized) return;
        var currentId = (OperationsList.SelectedItem as StreamerBotOperationDefinition)?.Id;
        var items = Catalogue.Search(entityKind, profile, SearchTextBox.Text);
        OperationsList.ItemsSource = items;
        var selected = items.FirstOrDefault(item => item.Id == currentId);
        OperationsList.SelectedItem = selected ?? (items.Count > 0 ? items[0] : null);
        StatusText.Text = $"{items.Count} verified operation{(items.Count == 1 ? string.Empty : "s")} - catalogue {Catalogue.Catalogue.Revision}";
    }

    private void OperationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        editors.Clear();
        FieldsPanel.Children.Clear();
        ValidationText.Text = string.Empty;
        if (OperationsList.SelectedItem is not StreamerBotOperationDefinition operation)
        {
            OperationNameText.Text = "No compatible operations";
            DescriptionText.Text = "No verified catalogue entry matches this filter and profile.";
            CompatibilityText.Text = string.Empty;
            ArgumentsText.Text = string.Empty;
            AddButton.IsEnabled = false;
            return;
        }

        OperationNameText.Text = operation.Name;
        DescriptionText.Text = operation.Description;
        CompatibilityText.Text = $"{operation.Category} - {operation.OutputMode} - native type {operation.NativeType} - {profile ?? "definition-only"}";
        var consumes = operation.ArgumentsConsumed.Count == 0 ? "none" : string.Join(", ", operation.ArgumentsConsumed);
        var produces = operation.ArgumentsProduced.Count == 0 ? "none" : string.Join(", ", operation.ArgumentsProduced);
        ArgumentsText.Text = $"Consumes: {consumes}. Produces: {produces}.";
        foreach (var field in operation.Fields) AddField(field);
        AddButton.IsEnabled = operation.Fields.All(field => field.Type != "commandReference" || commands.Count > 0);
        if (!AddButton.IsEnabled)
            ValidationText.Text = "Create a project command before adding this trigger.";
    }

    private void AddField(StreamerBotOperationField field)
    {
        var label = new TextBlock
        {
            Text = field.Required ? $"{field.Label} *" : field.Label,
            Margin = new Thickness(0, 8, 0, 3),
        };
        FieldsPanel.Children.Add(label);
        FrameworkElement editor = field.Type switch
        {
            "boolean" => new CheckBox { IsChecked = bool.TryParse(field.DefaultValue, out var value) && value },
            "commandReference" => new ComboBox
            {
                ItemsSource = commands,
                DisplayMemberPath = nameof(OperationReferenceChoice.Name),
                SelectedValuePath = nameof(OperationReferenceChoice.Id),
                SelectedIndex = commands.Count > 0 ? 0 : -1,
            },
            "multiline" => new TextBox
            {
                Text = field.DefaultValue ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
            },
            _ => new TextBox { Text = field.DefaultValue ?? string.Empty },
        };
        editor.ToolTip = field.Help;
        FieldsPanel.Children.Add(editor);
        editors[field.Id] = editor;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (OperationsList.SelectedItem is not StreamerBotOperationDefinition operation) return;
        var values = operation.Fields.ToDictionary(field => field.Id, ReadValue, StringComparer.Ordinal);
        var missing = operation.Fields.FirstOrDefault(field => field.Required && string.IsNullOrWhiteSpace(values[field.Id]));
        if (missing is not null)
        {
            ValidationText.Text = $"{missing.Label} is required.";
            return;
        }
        Selection = new(operation, values);
        DialogResult = true;
    }

    private string? ReadValue(StreamerBotOperationField field) => editors[field.Id] switch
    {
        CheckBox checkBox => (checkBox.IsChecked == true).ToString().ToLowerInvariant(),
        ComboBox comboBox => comboBox.SelectedValue?.ToString(),
        TextBox textBox => textBox.Text,
        _ => null,
    };
}

public sealed record OperationReferenceChoice(string Id, string Name);

public sealed record StreamerBotOperationSelection(
    StreamerBotOperationDefinition Operation,
    IReadOnlyDictionary<string, string?> Values);
