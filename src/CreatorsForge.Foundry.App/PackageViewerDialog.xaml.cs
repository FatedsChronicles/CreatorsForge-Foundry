using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Build.StreamerBot;
using CreatorsForge.Foundry.Core.Packaging;

namespace CreatorsForge.Foundry.App;

public partial class PackageViewerDialog : Window
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions DisplayOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string projectRoot;

    public PackageViewerDialog(string projectRoot)
    {
        this.projectRoot = Path.GetFullPath(projectRoot);
        InitializeComponent();

        var packageIrPath = Path.Combine(projectRoot, "build", "package-ir.json");
        var package = JsonSerializer.Deserialize<FoundryPackageIntermediate>(
            File.ReadAllText(packageIrPath),
            ReadOptions) ?? throw new InvalidDataException("Package IR is empty.");
        ArtifactsGrid.ItemsSource = package.Artifacts;
        SummaryText.Text =
            $"{package.Project.Name} {package.Project.Version} — " +
            $"{package.Target.Provider} {package.Target.Profile} — " +
            $"{package.Artifacts.Count} artifacts";
        ArtifactsGrid.SelectedIndex = package.Artifacts.Count > 0 ? 0 : -1;
    }

    private void ArtifactsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (ArtifactsGrid.SelectedItem is not FoundryPackageArtifact artifact)
        {
            ContentsTextBox.Clear();
            return;
        }

        var fullPath = Path.GetFullPath(
            Path.Combine(
                projectRoot,
                "build",
                artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
        var buildRoot = Path.Combine(projectRoot, "build") +
            Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(buildRoot, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
        {
            ContentsTextBox.Text = "Artifact is missing or outside the build directory.";
            return;
        }

        try
        {
            ContentsTextBox.Text = artifact.Kind switch
            {
                FoundryPackageArtifactKinds.StreamerBotPackage =>
                    StreamerBotStableV23Adapter.Decode(
                        File.ReadAllText(fullPath)).ToJsonString(DisplayOptions),
                FoundryPackageArtifactKinds.StreamerBotPackageReport or
                    FoundryPackageArtifactKinds.StreamerBotPortabilityReport or
                    FoundryPackageArtifactKinds.StreamerBotImportReport or
                    FoundryPackageArtifactKinds.CphInlineBridge =>
                    File.ReadAllText(fullPath),
                _ => $"{artifact.Kind}\n{artifact.Size:N0} bytes\nSHA-256: {artifact.Sha256}",
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException)
        {
            ContentsTextBox.Text = $"Could not display artifact:\n{exception.Message}";
        }
    }
}
