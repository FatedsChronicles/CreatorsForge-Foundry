using System.Windows;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class PublishingMetadataDialog : Window
{
    public PublishingMetadataDialog(FoundryWorkspace workspace)
    {
        InitializeComponent();
        var publishing = workspace.Manifest.Publishing ?? new FoundryPublishing
        {
            PackageName = workspace.Manifest.Id,
            Summary = $"{workspace.Manifest.Name} extension",
        };
        VersionText.Text = workspace.Manifest.Version;
        PackageNameText.Text = publishing.PackageName;
        SummaryText.Text = publishing.Summary;
        AuthorsText.Text = string.Join(", ", publishing.Authors);
        LicenseText.Text = publishing.LicenseFile;
        ChangelogText.Text = publishing.ChangelogFile;
        HomepageText.Text = publishing.Homepage;
        RepositoryText.Text = publishing.Repository;
        TagsText.Text = string.Join(", ", publishing.Tags);
        DependenciesText.Text = string.Join(Environment.NewLine, publishing.Dependencies.Select(item =>
            string.Join(" | ", item.Kind, item.Name, item.Version, item.License ?? string.Empty, item.Source ?? string.Empty)));
        SigningEnabled.IsChecked = publishing.Signing.Enabled;
        SigningToolText.Text = publishing.Signing.ToolPath;
        ThumbprintText.Text = publishing.Signing.CertificateThumbprint;
        TimestampText.Text = publishing.Signing.TimestampUrl;
        UpdateSigningState();
    }

    public FoundryPublishing? Publishing { get; private set; }
    public string Version { get; private set; } = string.Empty;

    private void Patch_Click(object sender, RoutedEventArgs e) => Bump(2);
    private void Minor_Click(object sender, RoutedEventArgs e) => Bump(1);
    private void Major_Click(object sender, RoutedEventArgs e) => Bump(0);
    private void SigningChanged(object sender, RoutedEventArgs e) => UpdateSigningState();

    private void Bump(int part)
    {
        var values = VersionText.Text.Split(['-', '+'], 2)[0].Split('.');
        if (values.Length != 3 || !values.All(item => int.TryParse(item, out _))) return;
        var numbers = values.Select(int.Parse).ToArray();
        numbers[part]++;
        for (var index = part + 1; index < numbers.Length; index++) numbers[index] = 0;
        VersionText.Text = string.Join('.', numbers);
    }

    private void UpdateSigningState()
    {
        if (SigningToolText is null) return;
        var enabled = SigningEnabled.IsChecked == true;
        SigningToolText.IsEnabled = enabled;
        ThumbprintText.IsEnabled = enabled;
        TimestampText.IsEnabled = enabled;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dependencies = DependenciesText.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseDependency).ToArray();
            Publishing = new FoundryPublishing
            {
                PackageName = PackageNameText.Text,
                Summary = SummaryText.Text,
                Authors = SplitList(AuthorsText.Text),
                LicenseFile = LicenseText.Text,
                ChangelogFile = ChangelogText.Text,
                Homepage = HomepageText.Text,
                Repository = RepositoryText.Text,
                Tags = SplitList(TagsText.Text),
                Dependencies = dependencies,
                Signing = new()
                {
                    Enabled = SigningEnabled.IsChecked == true,
                    ToolPath = SigningToolText.Text,
                    CertificateThumbprint = ThumbprintText.Text,
                    TimestampUrl = TimestampText.Text,
                },
            };
            Version = VersionText.Text.Trim();
            DialogResult = true;
        }
        catch (FormatException exception)
        {
            MessageBox.Show(this, exception.Message, "Dependency format", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static FoundryPublishingDependency ParseDependency(string line)
    {
        var parts = line.Split('|').Select(item => item.Trim()).ToArray();
        if (parts.Length is < 3 or > 5)
            throw new FormatException($"Dependency '{line}' must contain kind, name, and version separated by |.");
        return new()
        {
            Kind = parts[0], Name = parts[1], Version = parts[2],
            License = parts.Length > 3 ? parts[3] : null,
            Source = parts.Length > 4 ? parts[4] : null,
        };
    }

    private static string[] SplitList(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
