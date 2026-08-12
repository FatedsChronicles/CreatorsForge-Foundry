using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private readonly HashSet<string> createdSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private bool isRefreshingActionSuggestions;
    private bool isRefreshingCommandSuggestions;
    private readonly string? profile;
    private StreamerBotImportProvenance? import;
    private string minimumVersion = "1.0.0-alpha.1";
    private string? maximumTestedVersion;
    private string? documentation;

    public ObservableCollection<string> GroupOptions { get; } = [];
    public ObservableCollection<string> CommandGroupOptions { get; } = [];
    public ObservableCollection<QueueChoice> QueueOptions { get; } = [];

    public string? RequestedSourcePath { get; private set; }
    public bool HasSavedChanges { get; private set; }
    internal bool ResourcesReadyForSmokeTest =>
        ResourcesGrid is not null && ResourceTypes.Count >= 13 && PortabilityOptions.Count == 4;
    internal bool CSharpAuthoringReadyForSmokeTest =>
        AddExecuteCSharpButton is not null && ConvertToCSharpButton is not null;

    internal bool VerifyActionSuggestionsForSmokeTest()
    {
        if (actions.Count == 0 || queues.Count == 0) return false;
        actions[0].Group = "Shared Group";
        actions[0].QueueId = queues[0].Id;
        RefreshActionSuggestions();
        var groupCount = GroupOptions.Count;
        var queueCount = QueueOptions.Count;
        RefreshActionSuggestions();
        return GroupOptions.Count == groupCount &&
               QueueOptions.Count == queueCount &&
               GroupOptions.Contains("Shared Group") &&
               QueueOptions.Any(item => item.Id == queues[0].Id && item.Name == queues[0].Name) &&
               actions[0].QueueName == queues[0].Name;
    }

    internal bool VerifyCSharpConversionForSmokeTest()
    {
        if (actions.FirstOrDefault(item => !item.ReadOnly) is not { } action) return false;
        var row = new SubActionRow
        {
            Id = UniqueId("smoke-convert", action.SubActions.Select(item => item.Id)),
            Kind = "setArgument",
            Enabled = true,
            VariableName = "smokeValue",
            Value = "quoted \"value\"",
            AutoType = false,
            SourceType = 123,
            Weight = action.RandomAction ? 1 : 0,
        };
        action.SubActions.Add(row);
        var index = action.SubActions.IndexOf(row);
        var preview = StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(
            row.ToDefinition(), action.Id);
        var converted = ApplyCSharpConversion(action, row, preview);
        return action.SubActions.IndexOf(converted) == index &&
               converted.Id == row.Id && converted.Enabled == row.Enabled &&
               converted.Weight == row.Weight && converted.Kind == "executeCSharp" &&
               converted.CSharpState == "Generated";
    }

    internal bool VerifyCommandGroupsForSmokeTest()
    {
        if (commands.Count == 0) return false;
        commands[0].Group = "Creator Commands";
        RefreshCommandSuggestions();
        var count = CommandGroupOptions.Count;
        RefreshCommandSuggestions();
        return CommandGroupOptions.Count == count &&
               CommandGroupOptions.Contains("Creator Commands");
    }

    internal static bool VerifyEditableComboBoxForSmokeTest()
    {
        var editor = new ComboBox
        {
            IsEditable = true,
            Style = (Style)Application.Current.FindResource(typeof(ComboBox)),
        };
        editor.ApplyTemplate();
        if (editor.Template.FindName("PART_EditableTextBox", editor) is not TextBox textBox ||
            textBox.Visibility != Visibility.Visible || textBox.IsReadOnly)
            return false;
        textBox.Text = "New typed group";
        return editor.Text == "New typed group";
    }

    public StreamerBotDesignerDialog(string definitionPath, string? profile = null)
    {
        this.definitionPath = definitionPath;
        this.profile = profile;
        InitializeComponent();
        PreviewKeyDown += Designer_PreviewKeyDown;
        LoadDefinition();
    }

    private void Designer_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control &&
            e.Key == System.Windows.Input.Key.S)
        {
            e.Handled = true;
            SaveDefinition();
        }
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
                item.IgnoreBotAccount, item.IgnoreInternalMessages, item.Sources, item.Description,
                item.Group));
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
        RefreshActionSuggestions();
        RefreshCommandSuggestions();
        RefreshGeneratedSourceStates();
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
        RefreshActionSuggestions();
        RefreshValidation();
    }

    private void Grid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, QueuesGrid)) RefreshActionSuggestions();
        if (ReferenceEquals(sender, CommandsGrid)) RefreshCommandSuggestions();
        RefreshValidation();
    }

    private void RefreshCommandSuggestions()
    {
        if (isRefreshingCommandSuggestions) return;
        isRefreshingCommandSuggestions = true;
        try
        {
            var groups = commands.Select(item => item.Group?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();
            SynchronizeChoices(CommandGroupOptions, groups);
        }
        finally
        {
            isRefreshingCommandSuggestions = false;
        }
    }

    private void RefreshActionSuggestions()
    {
        if (isRefreshingActionSuggestions) return;
        isRefreshingActionSuggestions = true;
        try
        {
            var groups = actions.Select(item => item.Group?.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray();
            SynchronizeChoices(GroupOptions, groups);

            var queueChoices = queues.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new QueueChoice(item.Id, item.Name))
                .ToArray();
            SynchronizeChoices(QueueOptions, queueChoices);
            var names = QueueOptions.ToDictionary(item => item.Id, item => item.Name, StringComparer.Ordinal);
            foreach (var action in actions)
                action.QueueName = action.QueueId is not null && names.TryGetValue(action.QueueId, out var name)
                    ? name
                    : string.Empty;
        }
        finally
        {
            isRefreshingActionSuggestions = false;
        }
    }

    private static void SynchronizeChoices<T>(ObservableCollection<T> target, IReadOnlyList<T> values)
    {
        if (target.SequenceEqual(values)) return;
        target.Clear();
        foreach (var value in values) target.Add(value);
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
            sources: 1,
            group: null);
        commands.Add(item);
        CommandsGrid.SelectedItem = item;
    }

    private void RemoveCommand_Click(object sender, RoutedEventArgs e) =>
        RemoveSelected(commands, CommandsGrid);

    private void DuplicateCommand_Click(object sender, RoutedEventArgs e)
    {
        if (CommandsGrid.SelectedItem is not CommandRow source) return;
        if (source.ReadOnly)
        {
            StatusText.Text = "Preserved read-only commands cannot be duplicated safely.";
            return;
        }

        var copy = source.Duplicate(UniqueId("command", commands.Select(value => value.Id)));
        commands.Insert(CommandsGrid.SelectedIndex + 1, copy);
        CommandsGrid.SelectedItem = copy;
        RefreshCommandSuggestions();
    }

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

    private void AddExecuteCSharp_Click(object sender, RoutedEventArgs e)
    {
        if (ActionsGrid.SelectedItem is not ActionRow action || action.ReadOnly) return;
        var id = UniqueId("subAction", action.SubActions.Select(value => value.Id));
        try
        {
            var created = StreamerBotCSharpAuthoringService.CreateManual(id, action.Id,
                weight: action.RandomAction ? 1 : 0);
            StreamerBotCSharpAuthoringService.WriteNewSource(
                ProjectRoot, created.SubAction.SourcePath!, created.Source);
            createdSourcePaths.Add(StreamerBotCSharpAuthoringService.ResolveConfinedSourcePath(
                ProjectRoot, created.SubAction.SourcePath!));
            var item = SubActionRow.FromDefinition(created.SubAction, "Manual");
            action.SubActions.Add(item);
            SubActionsGrid.SelectedItem = item;
            StatusText.Text = $"Created editable Execute C# source at {item.SourcePath}.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = $"Execute C# source could not be created: {exception.Message}";
        }
    }

    private void ConvertSubActionToCSharp_Click(object sender, RoutedEventArgs e)
    {
        CommitGridEdits();
        if (ActionsGrid.SelectedItem is not ActionRow action ||
            SubActionsGrid.SelectedItem is not SubActionRow selected)
        {
            StatusText.Text = "Select an editable Set Argument sub-action first.";
            return;
        }

        try
        {
            var preview = StreamerBotCSharpAuthoringService.PreviewSetArgumentConversion(
                selected.ToDefinition(), action.Id);
            var dialog = new StreamerBotCSharpConversionDialog(preview.Summary, preview.Source)
            {
                Owner = this,
            };
            if (dialog.ShowDialog() != true) return;
            ApplyCSharpConversion(action, selected, preview);
            RefreshValidation();
            StatusText.Text = $"Converted to editable Execute C# at {preview.RelativePath}.";
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, "Conversion unavailable",
                MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = exception.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = $"Conversion did not change the sub-action: {exception.Message}";
        }
    }

    private SubActionRow ApplyCSharpConversion(
        ActionRow action,
        SubActionRow selected,
        StreamerBotCSharpConversionPreview preview)
    {
        var selectedIndex = action.SubActions.IndexOf(selected);
        if (selectedIndex < 0)
            throw new InvalidOperationException("The selected sub-action is no longer part of this action.");
        var created = StreamerBotCSharpAuthoringService.WriteNewSourceOrVerify(
            ProjectRoot, preview.RelativePath, preview.Source);
        if (created)
            createdSourcePaths.Add(StreamerBotCSharpAuthoringService.ResolveConfinedSourcePath(
                ProjectRoot, preview.RelativePath));
        var converted = SubActionRow.FromDefinition(preview.ConvertedSubAction, "Generated");
        action.SubActions[selectedIndex] = converted;
        SubActionsGrid.SelectedItem = converted;
        SubActionsGrid.ScrollIntoView(converted);
        return converted;
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

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDefinition();

    private bool SaveDefinition()
    {
        CommitGridEdits();
        RefreshGeneratedSourceStates();
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
            return false;
        }

        var temporaryPath = definitionPath + ".tmp";
        File.WriteAllText(temporaryPath, StreamerBotDefinitionLoader.Serialize(definition));
        File.Move(temporaryPath, definitionPath, overwrite: true);
        HasSavedChanges = true;
        StatusText.Text = $"Saved {Path.GetFileName(definitionPath)}. The Designer remains open.";
        return true;
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
                    item.Description,
                    string.IsNullOrWhiteSpace(item.Group) ? null : item.Group.Trim())).ToArray(),
            Actions = actions.Select(item => item.ToDefinition()).ToArray(),
            Resources = resources.Select(item => item.ToDefinition()).ToArray(),
        };

    private void RefreshValidation(StreamerBotDefinition? definition = null)
    {
        definition ??= CreateDefinition();
        var diagnostics = StreamerBotDefinitionDiagnostics.Analyze(definition, profile);
        ValidationGrid.ItemsSource = diagnostics;
        var errors = diagnostics.Count(item => item.Severity == StreamerBotDefinitionDiagnosticSeverity.Error);
        var warnings = diagnostics.Count - errors;
        StatusText.Text = errors > 0
            ? $"{errors} error(s) and {warnings} warning(s). Errors block saving and building."
            : warnings > 0
                ? $"{warnings} warning(s). Warnings do not block saving or building."
                : "Definition is valid with no warnings.";
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
        createdSourcePaths.Add(copyPath);
        copy.SourcePath = relativeCopyPath;
        copy.Generation = null;
        copy.DetachedFromGenerator = false;
        copy.CSharpState = "Manual";
    }

    private void OpenCSharpSource_Click(object sender, RoutedEventArgs e)
    {
        if (SubActionsGrid.SelectedItem is SubActionRow { Kind: "executeCSharp", SourcePath: { Length: > 0 } sourcePath })
        {
            RequestedSourcePath = sourcePath;
            if (SaveDefinition()) DialogResult = true;
            else RequestedSourcePath = null;
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

    protected override void OnClosed(EventArgs e)
    {
        if (DialogResult != true)
        {
            foreach (var path in createdSourcePaths)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        base.OnClosed(e);
    }

    private string ProjectRoot => Directory.GetParent(Path.GetDirectoryName(definitionPath)!)!.FullName;

    private void RefreshGeneratedSourceStates()
    {
        foreach (var item in actions.SelectMany(action => action.SubActions)
                     .Where(item => item.Kind == "executeCSharp"))
        {
            string? source = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(item.SourcePath))
                {
                    var path = StreamerBotCSharpAuthoringService.ResolveConfinedSourcePath(ProjectRoot, item.SourcePath);
                    if (File.Exists(path)) source = File.ReadAllText(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                item.CSharpState = "Missing";
                continue;
            }
            var state = StreamerBotCSharpAuthoringService.GetState(item.ToDefinition(), source);
            item.DetachedFromGenerator = state == StreamerBotGeneratedSourceState.Detached;
            item.CSharpState = state.ToString();
        }
        SubActionsGrid.Items.Refresh();
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
        string? description = null,
        string? group = null) : IDesignerRow
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
        public string? Group { get; set; } = group;
        public string? SourceId { get; } = sourceId;
        public bool ReadOnly { get; } = readOnly;
        public string? PreservationKey { get; } = preservationKey;
        public string Mode => ReadOnly ? "Read-only" : "Editable";

        public CommandRow Duplicate(string id) => new(
            id,
            Name + " Copy",
            Aliases,
            Enabled,
            CaseSensitive,
            GlobalCooldown,
            UserCooldown,
            ignoreBotAccount: IgnoreBotAccount,
            ignoreInternalMessages: IgnoreInternalMessages,
            sources: Sources,
            description: Description,
            group: Group);
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

    private sealed class ActionRow : IDesignerRow, INotifyPropertyChanged
    {
        private string queueName = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
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
                SubActions.Add(SubActionRow.FromDefinition(item,
                    item.Kind == "executeCSharp"
                        ? item.Generation is null ? "Manual/imported" : item.DetachedFromGenerator ? "Detached" : "Generated"
                        : string.Empty));
            }
        }

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string? QueueId { get; set; }
        public string QueueName
        {
            get => queueName;
            set
            {
                if (string.Equals(queueName, value, StringComparison.Ordinal)) return;
                queueName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QueueName)));
            }
        }
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
            SubActions.Select(item => item.ToDefinition()).ToArray(),
            SourceId,
            ReadOnly,
            PreservationKey,
            Group,
            Description,
            RandomAction,
            ExcludeFromPending,
            ExcludeFromHistory);
    }

    public sealed record QueueChoice(string Id, string Name)
    {
        public override string ToString() => Name;
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
        public StreamerBotCSharpGeneration? Generation { get; set; }
        public bool DetachedFromGenerator { get; set; }
        public string CSharpState { get; set; } = string.Empty;
        public string ReferenceList
        {
            get => string.Join("; ", References ?? []);
            set => References = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        public string Mode => ReadOnly ? "Read-only" : "Editable";
        public StreamerBotSubAction ToDefinition() => new(
            Id.Trim(), Kind, Enabled,
            string.IsNullOrWhiteSpace(VariableName) ? null : VariableName.Trim(),
            Value, AutoType, SourcePath, SourceType, SourceId, ReadOnly,
            PreservationKey, References, Weight, Generation, DetachedFromGenerator);

        public static SubActionRow FromDefinition(StreamerBotSubAction value, string cSharpState) => new()
        {
            Id = value.Id, Kind = value.Kind, Enabled = value.Enabled,
            VariableName = value.VariableName, Value = value.Value, AutoType = value.AutoType,
            SourcePath = value.SourcePath, SourceType = value.SourceType, SourceId = value.SourceId,
            ReadOnly = value.ReadOnly, PreservationKey = value.PreservationKey,
            References = value.References, Weight = value.Weight, Generation = value.Generation,
            DetachedFromGenerator = value.DetachedFromGenerator, CSharpState = cSharpState,
        };

        public void Apply(StreamerBotSubAction value, string cSharpState)
        {
            Id = value.Id; Kind = value.Kind; Enabled = value.Enabled;
            VariableName = value.VariableName; Value = value.Value; AutoType = value.AutoType;
            SourcePath = value.SourcePath; SourceType = value.SourceType; SourceId = value.SourceId;
            ReadOnly = value.ReadOnly; PreservationKey = value.PreservationKey;
            References = value.References; Weight = value.Weight; Generation = value.Generation;
            DetachedFromGenerator = value.DetachedFromGenerator; CSharpState = cSharpState;
        }

        public SubActionRow Duplicate(string id) => new()
        {
            Id = id, Kind = Kind, Enabled = Enabled, VariableName = VariableName, Value = Value,
            AutoType = AutoType, SourcePath = SourcePath, SourceType = SourceType,
            References = References?.ToArray(), Weight = Weight,
            CSharpState = Kind == "executeCSharp" ? "Manual" : string.Empty,
        };
    }

    private static string KnownArguments(string entityKind, string modelKind) =>
        OperationCatalogue.Find(entityKind, modelKind) is { } operation
            ? string.Join(", ", operation.ArgumentsProduced)
            : string.Empty;
}
