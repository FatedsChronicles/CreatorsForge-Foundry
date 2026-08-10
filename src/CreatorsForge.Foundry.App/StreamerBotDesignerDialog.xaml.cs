using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Build.StreamerBot;

namespace CreatorsForge.Foundry.App;

public partial class StreamerBotDesignerDialog : Window
{
    public static IReadOnlyList<string> TriggerKinds { get; } = ["command", "test", "opaque"];
    public static IReadOnlyList<string> SubActionKinds { get; } =
        ["setArgument", "executeBridge", "executeCSharp", "opaque"];

    private readonly string definitionPath;
    private readonly ObservableCollection<ActionRow> actions = [];
    private readonly ObservableCollection<CommandRow> commands = [];
    private readonly ObservableCollection<QueueRow> queues = [];
    private StreamerBotImportProvenance? import;

    public string? RequestedSourcePath { get; private set; }

    public StreamerBotDesignerDialog(string definitionPath)
    {
        this.definitionPath = definitionPath;
        InitializeComponent();
        LoadDefinition();
    }

    private void LoadDefinition()
    {
        var result = StreamerBotDefinitionLoader.Load(
            File.ReadAllText(definitionPath));
        if (!result.IsSuccess)
        {
            throw new InvalidDataException(string.Join(" ", result.Errors));
        }

        var definition = result.Definition!;
        import = definition.Import;
        AuthorTextBox.Text = definition.Metadata.Author;
        DescriptionTextBox.Text = definition.Metadata.Description;
        foreach (var item in definition.Queues)
        {
            queues.Add(new(item.Id, item.Name, item.Blocking, item.SourceId, item.ReadOnly, item.PreservationKey));
        }

        foreach (var item in definition.Commands)
        {
            commands.Add(new(
                item.Id,
                item.Name,
                string.Join(", ", item.Commands),
                item.Enabled,
                item.CaseSensitive,
                item.GlobalCooldown,
                item.UserCooldown, item.SourceId, item.ReadOnly, item.PreservationKey));
        }

        foreach (var item in definition.Actions)
        {
            actions.Add(new(item));
        }

        QueuesGrid.ItemsSource = queues;
        CommandsGrid.ItemsSource = commands;
        ActionsGrid.ItemsSource = actions;
        ActionsGrid.SelectedIndex = actions.Count > 0 ? 0 : -1;
        StatusText.Text = Path.GetFileName(definitionPath);
    }

    private void ActionsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var action = ActionsGrid.SelectedItem as ActionRow;
        TriggersGrid.ItemsSource = action?.Triggers;
        SubActionsGrid.ItemsSource = action?.SubActions;
    }

    private void AddAction_Click(object sender, RoutedEventArgs e)
    {
        var item = new ActionRow
        {
            Id = UniqueId("action", actions.Select(value => value.Id)),
            Name = "New action",
            Enabled = true,
        };
        actions.Add(item);
        ActionsGrid.SelectedItem = item;
    }

    private void RemoveAction_Click(object sender, RoutedEventArgs e) =>
        RemoveSelected(actions, ActionsGrid);

    private void AddCommand_Click(object sender, RoutedEventArgs e)
    {
        var item = new CommandRow(
            UniqueId("command", commands.Select(value => value.Id)),
            "New command",
            "!command",
            true,
            false,
            0,
            0);
        commands.Add(item);
        CommandsGrid.SelectedItem = item;
    }

    private void RemoveCommand_Click(object sender, RoutedEventArgs e) =>
        RemoveSelected(commands, CommandsGrid);

    private void AddQueue_Click(object sender, RoutedEventArgs e)
    {
        var item = new QueueRow(
            UniqueId("queue", queues.Select(value => value.Id)),
            "New queue",
            false);
        queues.Add(item);
        QueuesGrid.SelectedItem = item;
    }

    private void RemoveQueue_Click(object sender, RoutedEventArgs e) =>
        RemoveSelected(queues, QueuesGrid);

    private void AddTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionRow action)
        {
            return;
        }

        var item = new TriggerRow
        {
            Id = UniqueId("trigger", action.Triggers.Select(value => value.Id)),
            Kind = "test",
            Enabled = true,
        };
        action.Triggers.Add(item);
        TriggersGrid.SelectedItem = item;
    }

    private void RemoveTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is ActionRow action)
        {
            RemoveSelected(action.Triggers, TriggersGrid);
        }
    }

    private void AddSubAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionRow action)
        {
            return;
        }

        var item = new SubActionRow
        {
            Id = UniqueId(
                "subAction",
                action.SubActions.Select(value => value.Id)),
            Kind = "executeBridge",
            Enabled = true,
        };
        action.SubActions.Add(item);
        SubActionsGrid.SelectedItem = item;
    }

    private void RemoveSubAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is ActionRow action)
        {
            RemoveSelected(action.SubActions, SubActionsGrid);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdits();
        var definition = new StreamerBotDefinition
        {
            Metadata = new()
            {
                Author = AuthorTextBox.Text,
                Description = DescriptionTextBox.Text,
            },
            Import = import,
            Queues = queues.Select(item =>
                new StreamerBotQueueDefinition(
                    item.Id.Trim(),
                    item.Name.Trim(),
                    item.Blocking,
                    item.SourceId,
                    item.ReadOnly,
                    item.PreservationKey)).ToArray(),
            Commands = commands.Select(item =>
                new StreamerBotCommand(
                    item.Id.Trim(),
                    item.Name.Trim(),
                    item.Aliases.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries),
                    item.Enabled,
                    item.CaseSensitive,
                    item.GlobalCooldown,
                    item.UserCooldown,
                    item.SourceId,
                    item.ReadOnly,
                    item.PreservationKey)).ToArray(),
            Actions = actions.Select(item => item.ToDefinition()).ToArray(),
        };
        var errors = StreamerBotDefinitionLoader.Validate(definition);
        if (errors.Length > 0)
        {
            StatusText.Text = errors[0];
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, errors),
                "Definition validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var temporaryPath = definitionPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            StreamerBotDefinitionLoader.Serialize(definition));
        File.Move(temporaryPath, definitionPath, overwrite: true);
        DialogResult = true;
    }

    private void CommitGridEdits()
    {
        foreach (var grid in new[]
                 {
                     ActionsGrid,
                     CommandsGrid,
                     QueuesGrid,
                     TriggersGrid,
                     SubActionsGrid,
                 })
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }
    }

    private static string UniqueId(string prefix, IEnumerable<string> values)
    {
        var used = values.ToHashSet(StringComparer.Ordinal);
        for (var index = 1; ; index++)
        {
            var candidate = $"{prefix}-{index}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static void RemoveSelected<T>(
        ObservableCollection<T> collection,
        DataGrid grid)
    {
        if (grid.SelectedItem is T item && item is not IDesignerRow { ReadOnly: true })
        {
            collection.Remove(item);
        }
    }

    private void OpenCSharpSource_Click(object sender, RoutedEventArgs e)
    {
        if (SubActionsGrid.SelectedItem is SubActionRow { Kind: "executeCSharp", SourcePath: { Length: > 0 } sourcePath })
        {
            RequestedSourcePath = sourcePath;
            DialogResult = true;
        }
    }

    private void RemoveAbsoluteReferences_Click(object sender, RoutedEventArgs e)
    {
        if (SubActionsGrid.SelectedItem is not SubActionRow { Kind: "executeCSharp" } subAction)
        {
            StatusText.Text = "Select an editable Execute C# sub-action first.";
            return;
        }

        var existing = subAction.References ?? [];
        var portable = existing.Where(reference =>
            !Path.IsPathFullyQualified(reference) &&
            !reference.StartsWith("\\\\", StringComparison.Ordinal)).ToArray();
        var removed = existing.Count - portable.Length;
        subAction.References = portable;
        SubActionsGrid.Items.Refresh();
        StatusText.Text = removed == 0
            ? "The selected Execute C# sub-action has no absolute references."
            : $"Removed {removed} absolute reference(s). Save the definition, then build again.";
    }

    private void Grid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is IDesignerRow { ReadOnly: true }) e.Cancel = true;
    }

    private interface IDesignerRow
    {
        bool ReadOnly { get; }
    }

    private sealed class QueueRow(string id, string name, bool blocking, string? sourceId = null,
        bool readOnly = false, string? preservationKey = null) : IDesignerRow
    {
        public string Id { get; set; } = id;
        public string Name { get; set; } = name;
        public bool Blocking { get; set; } = blocking;
        public string? SourceId { get; } = sourceId;
        public bool ReadOnly { get; } = readOnly;
        public string? PreservationKey { get; } = preservationKey;
        public string Mode => ReadOnly ? "Read-only" : "Editable";
    }

    private sealed class CommandRow(
        string id,
        string name,
        string aliases,
        bool enabled,
        bool caseSensitive,
        int globalCooldown,
        int userCooldown,
        string? sourceId = null,
        bool readOnly = false,
        string? preservationKey = null) : IDesignerRow
    {
        public string Id { get; set; } = id;
        public string Name { get; set; } = name;
        public string Aliases { get; set; } = aliases;
        public bool Enabled { get; set; } = enabled;
        public bool CaseSensitive { get; set; } = caseSensitive;
        public int GlobalCooldown { get; set; } = globalCooldown;
        public int UserCooldown { get; set; } = userCooldown;
        public string? SourceId { get; } = sourceId;
        public bool ReadOnly { get; } = readOnly;
        public string? PreservationKey { get; } = preservationKey;
        public string Mode => ReadOnly ? "Read-only" : "Editable";
    }

    private sealed class ActionRow : IDesignerRow
    {
        public ActionRow()
        {
        }

        public ActionRow(StreamerBotAction value)
        {
            Id = value.Id;
            Name = value.Name;
            Enabled = value.Enabled;
            QueueId = value.QueueId;
            Concurrent = value.Concurrent;
            AlwaysRun = value.AlwaysRun;
            SourceId = value.SourceId;
            ReadOnly = value.ReadOnly;
            PreservationKey = value.PreservationKey;
            foreach (var item in value.Triggers)
            {
                Triggers.Add(new()
                {
                    Id = item.Id,
                    Kind = item.Kind,
                    Enabled = item.Enabled,
                    CommandId = item.CommandId,
                    SourceType = item.SourceType,
                    SourceId = item.SourceId,
                    ReadOnly = item.ReadOnly,
                    PreservationKey = item.PreservationKey,
                });
            }

            foreach (var item in value.SubActions)
            {
                SubActions.Add(new()
                {
                    Id = item.Id,
                    Kind = item.Kind,
                    Enabled = item.Enabled,
                    VariableName = item.VariableName,
                    Value = item.Value,
                    AutoType = item.AutoType,
                    SourcePath = item.SourcePath,
                    SourceType = item.SourceType,
                    SourceId = item.SourceId,
                    ReadOnly = item.ReadOnly,
                    PreservationKey = item.PreservationKey,
                    References = item.References,
                });
            }
        }

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string? QueueId { get; set; }
        public bool Concurrent { get; set; }
        public bool AlwaysRun { get; set; }
        public string? SourceId { get; set; }
        public bool ReadOnly { get; set; }
        public string? PreservationKey { get; set; }
        public string Mode => ReadOnly ? "Read-only" : "Editable";
        public ObservableCollection<TriggerRow> Triggers { get; } = [];
        public ObservableCollection<SubActionRow> SubActions { get; } = [];

        public StreamerBotAction ToDefinition() => new(
            Id.Trim(),
            Name.Trim(),
            Enabled,
            string.IsNullOrWhiteSpace(QueueId) ? null : QueueId.Trim(),
            Concurrent,
            AlwaysRun,
            Triggers.Select(item => new StreamerBotTrigger(
                item.Id.Trim(),
                item.Kind,
                item.Enabled,
                string.IsNullOrWhiteSpace(item.CommandId)
                    ? null
                    : item.CommandId.Trim(),
                item.SourceType,
                item.SourceId,
                item.ReadOnly,
                item.PreservationKey)).ToArray(),
            SubActions.Select(item => new StreamerBotSubAction(
                item.Id.Trim(),
                item.Kind,
                item.Enabled,
                string.IsNullOrWhiteSpace(item.VariableName)
                    ? null
                    : item.VariableName.Trim(),
                item.Value,
                item.AutoType,
                item.SourcePath,
                item.SourceType,
                item.SourceId,
                item.ReadOnly,
                item.PreservationKey,
                item.References)).ToArray(),
            SourceId,
            ReadOnly,
            PreservationKey);
    }

    private sealed class TriggerRow : IDesignerRow
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = "test";
        public bool Enabled { get; set; }
        public string? CommandId { get; set; }
        public int? SourceType { get; set; }
        public string? SourceId { get; set; }
        public bool ReadOnly { get; set; }
        public string? PreservationKey { get; set; }
        public string Mode => ReadOnly ? "Read-only" : "Editable";
    }

    private sealed class SubActionRow : IDesignerRow
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = "executeBridge";
        public bool Enabled { get; set; }
        public string? VariableName { get; set; }
        public string? Value { get; set; }
        public bool AutoType { get; set; }
        public string? SourcePath { get; set; }
        public int? SourceType { get; set; }
        public string? SourceId { get; set; }
        public bool ReadOnly { get; set; }
        public string? PreservationKey { get; set; }
        public IReadOnlyList<string>? References { get; set; }
        public string ReferenceList
        {
            get => string.Join("; ", References ?? []);
            set => References = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        public string Mode => ReadOnly ? "Read-only" : "Editable";
    }
}
