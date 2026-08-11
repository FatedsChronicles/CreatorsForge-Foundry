using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class StreamerBotImportDialog : Window
{
    private StreamerBotImportAnalysis? analysis;
    private readonly string defaultProjectDirectory;
    private string destinationParent;
    private bool updatingSuggestions;
    private bool idManuallyEdited;
    private bool destinationManuallyEdited;
    private bool analysisDefaultsApplied;

    public StreamerBotImportDialog(FoundryUserSettings settings)
    {
        InitializeComponent();
        defaultProjectDirectory = settings.DefaultProjectDirectory;
        destinationParent = defaultProjectDirectory;
        ApplySuggestedValues("Imported Extension", resetManualState: true);
        ProfileCombo.ItemsSource = FoundryStreamerBotProfiles.Ordered;
        ProfileCombo.SelectedItem = FoundryStreamerBotProfiles.Stable107;
    }

    public string? CreatedProjectPath { get; private set; }

    internal bool AnalyzeForSmokeTest(string importCode)
    {
        ImportCodeText.Text = importCode;
        Analyze();
        return analysis?.CanCreateProject == true && CreateButton.IsEnabled &&
            AnalysisText.Text.Contains("payload v23", StringComparison.Ordinal);
    }

    internal bool VerifyCreationSuggestionsForSmokeTest()
    {
        ApplySuggestedValues("My Export", resetManualState: true);
        NameText.Text = "Bot Eliminator";
        var derived = IdText.Text == "com.example.bot-eliminator" &&
            DestinationText.Text == Path.Combine(defaultProjectDirectory, "BotEliminator");
        IdText.Text = "org.example.manual";
        NameText.Text = "Renamed Project";
        var idProtected = IdText.Text == "org.example.manual" &&
            DestinationText.Text == Path.Combine(defaultProjectDirectory, "RenamedProject");
        var manualDestination = Path.Combine(defaultProjectDirectory, "ChosenManually");
        DestinationText.Text = manualDestination;
        NameText.Text = "Final Project";
        return derived && idProtected && DestinationText.Text == manualDestination;
    }

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Streamer.bot exports (*.txt;*.sb;*.streamerbot)|*.txt;*.sb;*.streamerbot|Developer exports (any extension)|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;
        await LoadImportFileAsync(dialog.FileName);
    }

    private async Task LoadImportFileAsync(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            var text = await StreamerBotImportFileReader.ReadAsync(fullPath);
            ImportCodeText.Text = text;
            AnalysisStatusText.Text = $"Loaded {info.Name}; analyzing safely...";
            Analyze();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            MessageBox.Show(this, exception.Message, "Import Streamer.bot code", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportDropTarget_DragEnter(object sender, DragEventArgs e)
    {
        UpdateDropEffect(e);
        if (e.Effects == DragDropEffects.Copy) ImportDropTarget.BorderBrush = (Brush)FindResource("AccentBrush");
    }

    private void ImportDropTarget_DragLeave(object sender, DragEventArgs e) =>
        RestoreDropTargetBorder();

    private void ImportDropTarget_DragOver(object sender, DragEventArgs e) => UpdateDropEffect(e);

    private async void ImportDropTarget_Drop(object sender, DragEventArgs e)
    {
        RestoreDropTargetBorder();
        if (!TryGetSingleDroppedFile(e.Data, out var path))
        {
            MessageBox.Show(this, "Drop exactly one local export file. Folders, shortcuts, URLs, and multiple files are not accepted.",
                "Import Streamer.bot code", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await LoadImportFileAsync(path);
    }

    private void RestoreDropTargetBorder() =>
        ImportDropTarget.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "PanelBorderBrush");

    private static void UpdateDropEffect(DragEventArgs e)
    {
        e.Effects = TryGetSingleDroppedFile(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool TryGetSingleDroppedFile(IDataObject data, out string path)
    {
        path = string.Empty;
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files)
            return false;
        path = files[0];
        return File.Exists(path) && !string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase);
    }

    private void Analyze_Click(object sender, RoutedEventArgs e) => Analyze();

    private void Analyze()
    {
        analysis = StreamerBotImportService.Analyze(ImportCodeText.Text);
        var summary = analysis.Summary;
        var lines = new List<string>();
        if (summary is not null)
        {
            lines.Add($"Source: Streamer.bot {summary.ExportedFrom}; payload v{summary.PayloadVersion}");
            lines.Add($"Entities: {summary.ActionCount} actions, {summary.CommandCount} commands, {summary.QueueCount} queues");
            lines.Add($"Editable: {summary.EditableCount}; preserved read-only: {summary.OpaqueCount}; Execute C#: {summary.CSharpCount}");
            lines.Add($"External references: {summary.ExternalReferenceCount}; absolute paths: {summary.AbsolutePathCount}");
            lines.Add(string.Empty);
            if (!analysisDefaultsApplied)
            {
                ApplySuggestedValues(summary.Name, resetManualState: true);
                AuthorText.Text = summary.Author;
                analysisDefaultsApplied = true;
            }
            ProfileCombo.SelectedItem = FoundryStreamerBotProfiles.Supported.Contains(summary.SuggestedProfile) ? summary.SuggestedProfile : FoundryStreamerBotProfiles.Stable107;
        }
        lines.AddRange(analysis.Findings.Select(item => $"{item.Severity.ToString().ToUpperInvariant()} {item.Code} {item.Path}: {item.Message}"));
        AnalysisText.Text = string.Join(Environment.NewLine, lines);
        AnalysisStatusText.Text = analysis.CanCreateProject ? "Analysis complete. The project can be created." : "Analysis found blocking issues; no files have been written.";
        CreateButton.IsEnabled = analysis.CanCreateProject;
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = false, Title = "Choose the parent folder for the imported project" };
        if (dialog.ShowDialog(this) == true)
        {
            destinationParent = dialog.FolderName;
            updatingSuggestions = true;
            DestinationText.Text = StreamerBotImportNamingService.Suggest(NameText.Text, destinationParent).DestinationFolder;
            updatingSuggestions = false;
            destinationManuallyEdited = false;
        }
    }

    private void NameText_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (updatingSuggestions) return;
        UpdateDerivedSuggestions();
    }

    private void IdText_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!updatingSuggestions) idManuallyEdited = true;
    }

    private void DestinationText_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!updatingSuggestions)
        {
            destinationManuallyEdited = true;
            if (Path.GetDirectoryName(DestinationText.Text) is { Length: > 0 } parent) destinationParent = parent;
        }
    }

    private void ResetId_Click(object sender, RoutedEventArgs e)
    {
        idManuallyEdited = false;
        UpdateDerivedSuggestions();
    }

    private void ResetDestination_Click(object sender, RoutedEventArgs e)
    {
        destinationManuallyEdited = false;
        updatingSuggestions = true;
        DestinationText.Text = StreamerBotImportNamingService.Suggest(NameText.Text, destinationParent).DestinationFolder;
        updatingSuggestions = false;
    }

    private void ApplySuggestedValues(string name, bool resetManualState)
    {
        if (resetManualState)
        {
            idManuallyEdited = false;
            destinationManuallyEdited = false;
        }
        updatingSuggestions = true;
        NameText.Text = name;
        var suggestion = StreamerBotImportNamingService.Suggest(name, destinationParent);
        if (!idManuallyEdited) IdText.Text = suggestion.PackageId;
        if (!destinationManuallyEdited) DestinationText.Text = suggestion.DestinationFolder;
        updatingSuggestions = false;
    }

    private void UpdateDerivedSuggestions()
    {
        updatingSuggestions = true;
        var suggestion = StreamerBotImportNamingService.Suggest(NameText.Text, destinationParent);
        if (!idManuallyEdited) IdText.Text = suggestion.PackageId;
        if (!destinationManuallyEdited) DestinationText.Text = suggestion.DestinationFolder;
        updatingSuggestions = false;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (analysis is null || !analysis.CanCreateProject) return;
        CreateButton.IsEnabled = false;
        var result = await StreamerBotImportProjectService.CreateAsync(new(
            DestinationText.Text.Trim(), NameText.Text.Trim(), IdText.Text.Trim(), VersionText.Text.Trim(),
            AuthorText.Text.Trim(), ProfileCombo.SelectedItem?.ToString() ?? FoundryStreamerBotProfiles.Stable107,
            string.IsNullOrWhiteSpace(AttributionText.Text) ? null : AttributionText.Text.Trim(), analysis));
        if (!result.IsSuccess)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, result.Findings.Select(item => $"{item.Code}: {item.Message}")),
                "Import Streamer.bot code", MessageBoxButton.OK, MessageBoxImage.Warning);
            CreateButton.IsEnabled = true;
            return;
        }
        CreatedProjectPath = result.ProjectPath;
        DialogResult = true;
    }

}
