using System.Text;
using System.Windows;
using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Core.Compatibility;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class StreamerBotImportDialog : Window
{
    private StreamerBotImportAnalysis? analysis;

    public StreamerBotImportDialog(FoundryUserSettings settings)
    {
        InitializeComponent();
        DestinationText.Text = Path.Combine(settings.DefaultProjectDirectory, "ImportedExtension");
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

    private void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { CheckFileExists = true, Filter = "Streamer.bot exports (*.txt;*.streamerbot)|*.txt;*.streamerbot|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        var info = new FileInfo(dialog.FileName);
        if (info.Length > StreamerBotEnvelopeCodec.MaximumImportCodeCharacters)
        {
            MessageBox.Show(this, "That file exceeds the 16 MiB import-code limit.", "Import Streamer.bot code", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ImportCodeText.Text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
        Analyze();
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
            NameText.Text = summary.Name;
            AuthorText.Text = summary.Author;
            IdText.Text = SuggestId(summary.Name);
            ProfileCombo.SelectedItem = FoundryStreamerBotProfiles.Supported.Contains(summary.SuggestedProfile) ? summary.SuggestedProfile : FoundryStreamerBotProfiles.Stable107;
            var parent = Path.GetDirectoryName(DestinationText.Text);
            if (!string.IsNullOrWhiteSpace(parent)) DestinationText.Text = Path.Combine(parent, SafeFolder(summary.Name));
        }
        lines.AddRange(analysis.Findings.Select(item => $"{item.Severity.ToString().ToUpperInvariant()} {item.Code} {item.Path}: {item.Message}"));
        AnalysisText.Text = string.Join(Environment.NewLine, lines);
        AnalysisStatusText.Text = analysis.CanCreateProject ? "Analysis complete. The project can be created." : "Analysis found blocking issues; no files have been written.";
        CreateButton.IsEnabled = analysis.CanCreateProject;
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = false, Title = "Choose the parent folder for the imported project" };
        if (dialog.ShowDialog(this) == true) DestinationText.Text = Path.Combine(dialog.FolderName, SafeFolder(NameText.Text));
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

    private static string SafeFolder(string value)
    {
        var result = string.Concat(value.Where(char.IsLetterOrDigit));
        return result.Length == 0 ? "ImportedExtension" : result;
    }

    private static string SuggestId(string value)
    {
        var slug = string.Join('-', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray())).Where(word => word.Length > 0));
        return $"com.example.{(slug.Length == 0 ? "imported-extension" : slug)}";
    }
}
