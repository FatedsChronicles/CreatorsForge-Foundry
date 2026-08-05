using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class PreviewDesignerDialog : Window
{
    private static readonly PreviewKindOption[] KindOptions =
    [
        new(FoundryPreview.StaticWebKind, "Static web structure"),
        new(FoundryPreview.WinFormsKind, "WinForms structure"),
        new(FoundryPreview.ObsComponentKind, "OBS component structure"),
    ];
    private static readonly ViewportOption[] ViewportOptions =
    [
        new("HD 1280 x 720", 1280, 720),
        new("Full HD 1920 x 1080", 1920, 1080),
        new("Compact 800 x 600", 800, 600),
        new("Portrait 720 x 1280", 720, 1280),
        new("Custom", 0, 0),
    ];
    private readonly FoundryWorkspace workspace;
    private bool initialized;

    public PreviewDesignerDialog(FoundryWorkspace workspace)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        InitializeComponent();
        KindComboBox.ItemsSource = KindOptions;
        ViewportComboBox.ItemsSource = ViewportOptions;
        var configured = workspace.Manifest.Preview;
        EnabledCheckBox.IsChecked = configured?.Enabled ?? true;
        KindComboBox.SelectedItem = KindOptions.First(item => item.Id == InferKind(workspace, configured));
        WidthTextBox.Text = (configured?.Width ?? 1280).ToString(System.Globalization.CultureInfo.InvariantCulture);
        HeightTextBox.Text = (configured?.Height ?? 720).ToString(System.Globalization.CultureInfo.InvariantCulture);
        ViewportComboBox.SelectedItem = ViewportOptions.FirstOrDefault(item =>
            item.Width == configured?.Width && item.Height == configured?.Height) ?? ViewportOptions[^1];
        RefreshSources(configured?.Source);
        initialized = true;
        SetConfigurationControlsEnabled();
        Loaded += async (_, _) => await RefreshPreviewAsync(showErrors: false);
    }

    public FoundryWorkspace? UpdatedWorkspace { get; private set; }

    private static string InferKind(FoundryWorkspace workspace, FoundryPreview? configured)
    {
        if (configured is not null && FoundryPreview.SupportedKinds.Contains(configured.Kind))
        {
            return configured.Kind;
        }
        if (string.Equals(workspace.Manifest.Target?.Provider, "obsstudio", StringComparison.Ordinal) &&
            workspace.Manifest.ObsPlugin?.Design is not null)
        {
            return FoundryPreview.ObsComponentKind;
        }
        if (workspace.Manifest.Features.WinForms)
        {
            return FoundryPreview.WinFormsKind;
        }
        return FoundryPreview.StaticWebKind;
    }

    private void RefreshSources(string? preferred = null)
    {
        if (KindComboBox.SelectedItem is not PreviewKindOption kind) return;
        var sources = PreviewDesignService.GetCandidateSources(workspace, kind.Id);
        SourceComboBox.ItemsSource = sources;
        SourceComboBox.Text = !string.IsNullOrWhiteSpace(preferred) &&
            sources.Contains(preferred, StringComparer.OrdinalIgnoreCase)
                ? preferred
                : sources.Count > 0 ? sources[0] : preferred ?? string.Empty;
    }

    private async Task RefreshPreviewAsync(bool showErrors)
    {
        if (!TryCreateConfiguration(out var configuration, showErrors))
        {
            PreviewStatusText.Text = "No eligible source is selected. Add a supported .html file, enable WinForms with a declared .cs source, or use OBS design metadata.";
            return;
        }
        PreviewStatusText.Text = "Analyzing source structure...";
        var result = await PreviewDesignService.AnalyzeAsync(workspace, configuration);
        if (!result.IsSuccess)
        {
            DesignCanvas.Children.Clear();
            PreviewStatusText.Text = string.Join(" ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            return;
        }
        Render(result.Value!);
    }

    private void Render(PreviewDesignSurface surface)
    {
        DesignCanvas.Children.Clear();
        DesignCanvas.Width = surface.ViewportWidth;
        DesignCanvas.Height = surface.ViewportHeight;
        SurfaceTitleText.Text = $"{workspace.Manifest.Name} - {surface.Kind}";
        ViewportStatusText.Text = $"{surface.ViewportWidth} x {surface.ViewportHeight}";
        foreach (var element in surface.Elements)
        {
            var label = new TextBlock
            {
                Margin = new Thickness(8),
                Text = element.Label,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var border = new Border
            {
                Width = Math.Max(20, element.Width),
                Height = Math.Max(20, element.Height),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(4),
                Child = label,
                ToolTip = $"{element.Kind}: {element.Name}",
            };
            border.SetResourceReference(Border.BackgroundProperty, "ButtonBackgroundBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            Canvas.SetLeft(border, element.X);
            Canvas.SetTop(border, element.Y);
            DesignCanvas.Children.Add(border);
        }
        PreviewStatusText.Text = $"{surface.Notice} Source SHA-256: {surface.SourceSha256[..12]}...";
    }

    private bool TryCreateConfiguration(out FoundryPreview configuration, bool showErrors)
    {
        configuration = new();
        if (KindComboBox.SelectedItem is not PreviewKindOption kind ||
            string.IsNullOrWhiteSpace(SourceComboBox.Text) ||
            !int.TryParse(WidthTextBox.Text, out var width) ||
            !int.TryParse(HeightTextBox.Text, out var height))
        {
            if (showErrors)
            {
                MessageBox.Show(this, "Choose a preview kind and source, then enter a numeric viewport width and height.", "Design Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return false;
        }
        configuration = new()
        {
            Enabled = EnabledCheckBox.IsChecked == true,
            Kind = kind.Id,
            Source = SourceComboBox.Text.Trim().Replace('\\', '/'),
            Width = width,
            Height = height,
        };
        return true;
    }

    private async void RefreshPreview_Click(object sender, RoutedEventArgs e) =>
        await RefreshPreviewAsync(showErrors: true);

    private void Kind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        RefreshSources();
    }

    private void Viewport_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || ViewportComboBox.SelectedItem is not ViewportOption { Width: > 0, Height: > 0 } viewport) return;
        WidthTextBox.Text = viewport.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HeightTextBox.Text = viewport.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void PreviewSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        SetConfigurationControlsEnabled();
    }

    private void SetConfigurationControlsEnabled()
    {
        var enabled = EnabledCheckBox.IsChecked == true;
        KindComboBox.IsEnabled = enabled;
        SourceComboBox.IsEnabled = enabled;
        ViewportComboBox.IsEnabled = enabled;
        WidthTextBox.IsEnabled = enabled;
        HeightTextBox.IsEnabled = enabled;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        FoundryPreview? configuration = null;
        if (EnabledCheckBox.IsChecked == true &&
            !TryCreateConfiguration(out configuration, showErrors: true)) return;
        var result = await FoundryWorkspaceService.SavePreviewAsync(workspace, configuration);
        if (!result.IsSuccess)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")), "Preview settings could not be saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        UpdatedWorkspace = result.Value;
        DialogResult = true;
    }

    private sealed record PreviewKindOption(string Id, string DisplayName);
    private sealed record ViewportOption(string DisplayName, int Width, int Height);
}
