using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Build.StreamerBot;

namespace CreatorsForge.Foundry.App;

public partial class StreamerBotDesignerDialog : Window
{
    private static readonly StreamerBotOperationCatalogueService OperationCatalogue =
        StreamerBotOperationCatalogueService.LoadEmbedded();
    public static IReadOnlyList<string> TriggerKinds { get; } = ["command", "test", "opaque"];
    public static IReadOnlyList<string> SubActionKinds { get; } =
        ["setArgument", "executeBridge", "executeCSharp", "opaque"];
    public static IReadOnlyList<string> ResourceTypes => StreamerBotResourceTypes.All;
    public static IReadOnlyList<string> PortabilityOptions => StreamerBotResourcePortability.All;

    private readonly string definitionPath;
    private readonly ObservableCollection<ActionRow> actions = [];
    private readonly ObservableCollection<CommandRow> commands = [];
    private readonly ObservableCollection<QueueRow> queues = [];
    private readonly ObservableCollection<ResourceRow> resources = [];
    private readonly string? profile;
    private StreamerBotImportProvenance? import;
    private string minimumVersion = "1.0.0-alpha.1";
    private string? maximumTestedVersion;
    private string? documentation;

    public string? RequestedSourcePath { get; private set; }
    internal bool ResourcesReadyForSmokeTest =>
        ResourcesGrid is not null && ResourceTypes.Count >= 13 && PortabilityOptions.Count == 4;

    public StreamerBotDesignerDialog(string definitionPath, string? profile = null)
    {
        this.definitionPath = definitionPath;
        this.profile = profile;
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
        minimumVersion = definition.Metadata.MinimumVersion;
        maximumTestedVersion = definition.Metadata.MaximumTestedVersion;
        documentation = definition.Metadata.Documentation;
        AuthorTextBox.Text = definition.Metadata.Author;
        DescriptionTextBox.Text = definition.Metadata.Description;
        foreach (var item in definition.Queues)
        {
            queues.Add(new(item.Id, item.Name, item.Blocking, item.SourceId, item.ReadOnly, item.PreservationKey, item.Description));
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
                item.UserCooldown, item.SourceId, item.ReadOnly, item.PreservationKey,
                item.IgnoreBotAccount, item.IgnoreInternalMessages, item.Sources, item.Description));
        }

        foreach (var item in definition.Actions)
        {
            actions.Add(new(item));
        }

        foreach (var item in definition.Resources)
        {
            resources.Add(new(item));
        }

        QueuesGrid.ItemsSource = queues;
        CommandsGrid.ItemsSource = commands;
        ActionsGrid.ItemsSource = actions;
        ResourcesGrid.ItemsSource = resources;
        ActionsGrid.SelectedIndex = actions.Count > 0 ? 0 : -1;
        StatusText.Text = Path.GetFileName(definitionPath);
        RefreshValidation();
    }

    private void ActionsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        var action = ActionsGrid.SelectedItem as ActionRow;
        TriggersGrid.ItemsSource = action?.Triggers;
        SubActionsGrid.ItemsSource = action?.SubActions;
        WeightColumn.IsReadOnly = action is null || !action.RandomAction;
    }

    private void ActionsGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (ActionsGrid.SelectedItem is ActionRow action)
            WeightColumn.IsReadOnly = !action.RandomAction;
        RefreshValidation();
    }

    private void Grid_CurrentCellChanged(object? sender, EventArgs e) => RefreshValidation();

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

    private void DuplicateAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionRow source || source.ReadOnly) return;
        if (source.Triggers.Any(item => item.ReadOnly) || source.SubActions.Any(item => item.ReadOnly))
        {
            StatusText.Text = "Actions containing preserved read-only items cannot be duplicated safely.";
            return;
        }
        var copy = source.Duplicate(UniqueId("action", actions.Select(value => value.Id)));
        try
        {
            CopyCSharpSources(source.SubActions, copy.SubActions, copy.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = $"The action could not be duplicated: {exception.Message}";
            return;
        }
        actions.Insert(ActionsGrid.SelectedIndex + 1, copy);
        ActionsGrid.SelectedItem = copy;
    }

    private void MoveActionUp_Click(object sender, RoutedEventArgs e) => MoveSelected(actions, ActionsGrid, -1);
    private void MoveActionDown_Click(object sender, RoutedEventArgs e) => MoveSelected(actions, ActionsGrid, 1);

    private void AddCommand_Click(object sender, RoutedEventArgs e)
    {
        var item = new CommandRow(
            UniqueId("command", commands.Select(value => value.Id)),
            "New command",
            "!command",
            true,
            false,
            0,
            0,
            ignoreBotAccount: true,
            ignoreInternalMessages: true,
            sources: 1);
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
            false,
            description: string.Empty);
        queues.Add(item);
        QueuesGrid.SelectedItem = item;
    }

    private void RemoveQueue_Click(object sender, RoutedEventArgs e) =>
        RemoveSelected(queues, QueuesGrid);

    private void AddResource_Click(object sender, RoutedEventArgs e)
    {
        var item = new ResourceRow
        {
            Id = UniqueId("resource", resources.Select(value => value.Id)),
            Name = "New resource",
            Type = "custom",
            Required = true,
            Portability = StreamerBotResourcePortability.ManualConfiguration,
        };
        resources.Add(item);
        ResourcesGrid.SelectedItem = item;
    }

    private void RemoveResource_Click(object sender, RoutedEventArgs e) =>
        RemoveSelected(resources, ResourcesGrid);

    private void AddTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionRow action)
        {
            return;
        }

        var palette = new StreamerBotOperationPaletteDialog(
            "trigger",
            profile,
            commands.Select(item => new OperationReferenceChoice(item.Id, item.Name)).ToArray())
        {
            Owner = this,
        };
        if (palette.ShowDialog() != true || palette.Selection is not { } selection) return;
        var item = new TriggerRow
        {
            Id = UniqueId("trigger", action.Triggers.Select(value => value.Id)),
            Kind = selection.Operation.ModelKind,
            Enabled = true,
            CommandId = selection.Values.GetValueOrDefault("commandId"),
            SourceType = selection.Operation.NativeType,
            RuntimeArguments = string.Join(", ", selection.Operation.ArgumentsProduced),
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

        var palette = new StreamerBotOperationPaletteDialog("subAction", profile)
        {
            Owner = this,
        };
        if (palette.ShowDialog() != true || palette.Selection is not { } selection) return;
        var item = new SubActionRow
        {
            Id = UniqueId(
                "subAction",
                action.SubActions.Select(value => value.Id)),
            Kind = selection.Operation.ModelKind,
            Enabled = true,
            VariableName = selection.Values.GetValueOrDefault("variableName"),
            Value = selection.Values.GetValueOrDefault("value"),
            AutoType = bool.TryParse(selection.Values.GetValueOrDefault("autoType"), out var autoType) && autoType,
            SourceType = selection.Operation.NativeType,
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

    private void DuplicateSubAction_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionRow action ||
            SubActionsGrid.SelectedItem is not SubActionRow source || source.ReadOnly) return;
        var copy = source.Duplicate(UniqueId("subAction", action.SubActions.Select(value => value.Id)));
        try
        {
            CopyCSharpSource(source, copy, action.Id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = $"The sub-action could not be duplicated: {exception.Message}";
            return;
        }
        action.SubActions.Insert(SubActionsGrid.SelectedIndex + 1, copy);
        SubActionsGrid.SelectedItem = copy;
    }

    private void MoveSubActionUp_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is ActionRow action) MoveSelected(action.SubActions, SubActionsGrid, -1);
    }

    private void MoveSubActionDown_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is ActionRow action) MoveSelected(action.SubActions, SubActionsGrid, 1);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdits();
        var definition = CreateDefinition();
        var errors = StreamerBotDefinitionLoader.Validate(definition);
        RefreshValidation(definition);
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
        File.WriteAllText(temporaryPath, StreamerBotDefinitionLoader.Serialize(definition));
        File.Move(temporaryPath, definitionPath, overwrite: true);
        DialogResult = true;
    }

    private StreamerBotDefinition CreateDefinition() => new()
    {
            Metadata = new()
            {
                Author = AuthorTextBox.Text,
                Description = DescriptionTextBox.Text,
                MinimumVersion = minimumVersion,
                MaximumTestedVersion = maximumTestedVersion,
                Documentation = documentation,
            },
            Import = import,
            Queues = queues.Select(item =>
                new StreamerBotQueueDefinition(
                    item.Id.Trim(),
                    item.Name.Trim(),
                    item.Blocking,
                    item.SourceId,
                    item.ReadOnly,
                    item.PreservationKey,
                    item.Description)).ToArray(),
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
                    item.PreservationKey,
                    item.IgnoreBotAccount,
                    item.IgnoreInternalMessages,
                    item.Sources,
                    item.Description)).ToArray(),
            Actions = actions.Select(item => item.ToDefinition()).ToArray(),
            Resources = resources.Select(item => item.ToDefinition()).ToArray(),
        };

    private void RefreshValidation(StreamerBotDefinition? definition = null)
    {
        definition ??= CreateDefinition();
        ValidationGrid.ItemsSource = StreamerBotDefinitionDiagnostics.Analyze(definition, profile);
    }

    private void CommitGridEdits()
    {
        foreach (var grid in new[]
                 {
                     ActionsGrid,
                     CommandsGrid,
                     QueuesGrid,
                     ResourcesGrid,
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

    private static void MoveSelected<T>(ObservableCollection<T> collection, DataGrid grid, int offset)
    {
        if (grid.SelectedItem is not T item || item is IDesignerRow { ReadOnly: true }) return;
        var current = collection.IndexOf(item);
        var target = current + offset;
        if (current < 0 || target < 0 || target >= collection.Count) return;
        collection.Move(current, target);
        grid.SelectedItem = item;
    }

    private void CopyCSharpSources(
        IReadOnlyList<SubActionRow> sources,
        IReadOnlyList<SubActionRow> copies,
        string actionId)
    {
        for (var index = 0; index < sources.Count; index++)
            CopyCSharpSource(sources[index], copies[index], actionId);
    }

    private void CopyCSharpSource(SubActionRow source, SubActionRow copy, string actionId)
    {
        if (source.Kind != "executeCSharp" || string.IsNullOrWhiteSpace(source.SourcePath)) return;
        var projectRoot = Directory.GetParent(Path.GetDirectoryName(definitionPath)!)!.FullName;
        var sourcePath = Path.GetFullPath(Path.Combine(projectRoot,
            source.SourcePath.Replace('/', Path.DirectorySeparatorChar)));
        var relativeCopyPath = $"streamerbot/code/{actionId}/{copy.Id}.cs";
        var copyPath = Path.GetFullPath(Path.Combine(projectRoot,
            relativeCopyPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!sourcePath.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !copyPath.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The Execute C# source path leaves the project.");
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        File.Copy(sourcePath, copyPath, overwrite: false);
        copy.SourcePath = relativeCopyPath;
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
        bool readOnly = false, string? preservationKey = null, string? description = null) : IDesignerRow
    {
        public string Id { get; set; } = id;
        public string Name { get; set; } = name;
        public bool Blocking { get; set; } = blocking;
        public string? Description { get; set; } = description;
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
        string? preservationKey = null,
        bool ignoreBotAccount = true,
        bool ignoreInternalMessages = true,
        int sources = 1,
        string? description = null) : IDesignerRow
    {
        public string Id { get; set; } = id;
        public string Name { get; set; } = name;
        public string Aliases { get; set; } = aliases;
        public bool Enabled { get; set; } = enabled;
        public bool CaseSensitive { get; set; } = caseSensitive;
        public int GlobalCooldown { get; set; } = globalCooldown;
        public int UserCooldown { get; set; } = userCooldown;
        public bool IgnoreBotAccount { get; set; } = ignoreBotAccount;
        public bool IgnoreInternalMessages { get; set; } = ignoreInternalMessages;
        public int Sources { get; set; } = sources;
        public string? Description { get; set; } = description;
        public string? SourceId { get; } = sourceId;
        public bool ReadOnly { get; } = readOnly;
        public string? PreservationKey { get; } = preservationKey;
        public string Mode => ReadOnly ? "Read-only" : "Editable";
    }

    private sealed class ResourceRow : IDesignerRow
    {
        public ResourceRow() { }

        public ResourceRow(StreamerBotResourceDefinition value)
        {
            Id = value.Id;
            Name = value.Name;
            Type = value.Type;
            Required = value.Required;
            Portability = value.Portability;
            Description = value.Description;
            SuggestedValue = value.SuggestedValue;
            ValidationPattern = value.ValidationPattern;
            Bindings = string.Join("; ", (value.Bindings ?? []).Select(binding =>
                $"{binding.EntityType}:{binding.EntityId}:{binding.Property}"));
        }

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "custom";
        public bool Required { get; set; }
        public string Portability { get; set; } = StreamerBotResourcePortability.ManualConfiguration;
        public string? Description { get; set; }
        public string? SuggestedValue { get; set; }
        public string? ValidationPattern { get; set; }
        public string Bindings { get; set; } = string.Empty;
        public bool ReadOnly => false;
        public string Usage => ParseBindings().Length == 0 ? "Unused" : $"{ParseBindings().Length} binding(s)";

        public StreamerBotResourceDefinition ToDefinition() => new(
            Id.Trim(), Name.Trim(), Type, Required, Portability,
            Description, SuggestedValue, ValidationPattern, ParseBindings());

        private StreamerBotResourceBinding[] ParseBindings() => Bindings
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value =>
            {
                var parts = value.Split(':', 3, StringSplitOptions.TrimEntries);
                return parts.Length == 3
                    ? new StreamerBotResourceBinding(parts[0], parts[1], parts[2])
                    : new StreamerBotResourceBinding(string.Empty, value, string.Empty);
            })
            .ToArray();
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
            Group = value.Group;
            Description = value.Description;
            RandomAction = value.RandomAction;
            ExcludeFromPending = value.ExcludeFromPending;
            ExcludeFromHistory = value.ExcludeFromHistory;
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
                    RuntimeArguments = KnownArguments("trigger", item.Kind),
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
                    Weight = item.Weight,
                });
            }
        }

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string? QueueId { get; set; }
        public bool Concurrent { get; set; }
        public bool AlwaysRun { get; set; }
        public string? Group { get; set; }
        public string? Description { get; set; }
        public bool RandomAction { get; set; }
        public bool ExcludeFromPending { get; set; }
        public bool ExcludeFromHistory { get; set; }
        public string? SourceId { get; set; }
        public bool ReadOnly { get; set; }
        public string? PreservationKey { get; set; }
        public string Mode => ReadOnly ? "Read-only" : "Editable";
        public ObservableCollection<TriggerRow> Triggers { get; } = [];
        public ObservableCollection<SubActionRow> SubActions { get; } = [];

        public ActionRow Duplicate(string id)
        {
            var copy = new ActionRow
            {
                Id = id, Name = Name + " Copy", Enabled = Enabled, QueueId = QueueId,
                Concurrent = Concurrent, AlwaysRun = AlwaysRun, Group = Group, Description = Description,
                RandomAction = RandomAction, ExcludeFromPending = ExcludeFromPending,
                ExcludeFromHistory = ExcludeFromHistory,
            };
            foreach (var trigger in Triggers)
                copy.Triggers.Add(trigger.Duplicate(UniqueId("trigger", copy.Triggers.Select(value => value.Id))));
            foreach (var subAction in SubActions)
                copy.SubActions.Add(subAction.Duplicate(UniqueId("subAction", copy.SubActions.Select(value => value.Id))));
            return copy;
        }

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
                item.References,
                item.Weight)).ToArray(),
            SourceId,
            ReadOnly,
            PreservationKey,
            Group,
            Description,
            RandomAction,
            ExcludeFromPending,
            ExcludeFromHistory);
    }

    private sealed class TriggerRow : IDesignerRow
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = "test";
        public bool Enabled { get; set; }
        public string? CommandId { get; set; }
        public int? SourceType { get; set; }
        public string RuntimeArguments { get; set; } = string.Empty;
        public string? SourceId { get; set; }
        public bool ReadOnly { get; set; }
        public string? PreservationKey { get; set; }
        public string Mode => ReadOnly ? "Read-only" : "Editable";
        public TriggerRow Duplicate(string id) => new()
        {
            Id = id, Kind = Kind, Enabled = Enabled, CommandId = CommandId, SourceType = SourceType,
            RuntimeArguments = RuntimeArguments,
        };
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
        public double Weight { get; set; }
        public string ReferenceList
        {
            get => string.Join("; ", References ?? []);
            set => References = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        public string Mode => ReadOnly ? "Read-only" : "Editable";
        public SubActionRow Duplicate(string id) => new()
        {
            Id = id, Kind = Kind, Enabled = Enabled, VariableName = VariableName, Value = Value,
            AutoType = AutoType, SourcePath = SourcePath, SourceType = SourceType,
            References = References?.ToArray(), Weight = Weight,
        };
    }

    private static string KnownArguments(string entityKind, string modelKind) =>
        OperationCatalogue.Find(entityKind, modelKind) is { } operation
            ? string.Join(", ", operation.ArgumentsProduced)
            : string.Empty;
}
