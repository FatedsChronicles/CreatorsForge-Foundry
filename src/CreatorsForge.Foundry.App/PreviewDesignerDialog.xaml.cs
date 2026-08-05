using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.PreviewHost;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class PreviewDesignerDialog : Window, IAsyncDisposable
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
    private readonly PreviewRuntimeSession runtimeSession;
    private readonly DispatcherTimer autoRefreshTimer;
    private CancellationTokenSource? refreshCancellation;
    private FileSystemWatcher? sourceWatcher;
    private PreviewDesignSurface? lastSurface;
    private bool initialized;
    private bool disposed;

    public PreviewDesignerDialog(FoundryWorkspace workspace)
    {
        this.workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        InitializeComponent();
        var stateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Creators Forge",
            "Foundry");
        runtimeSession = new PreviewRuntimeSession(
            typeof(PreviewHostMarker).Assembly.Location,
            stateRoot);
        runtimeSession.StateChanged += RuntimeSession_StateChanged;
        autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(650),
        };
        autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        KindComboBox.ItemsSource = KindOptions;
        ViewportComboBox.ItemsSource = ViewportOptions;
        var configured = workspace.Manifest.Preview;
        EnabledCheckBox.IsChecked = configured?.Enabled ?? true;
        KindComboBox.SelectedItem = KindOptions.First(item => item.Id == InferKind(workspace, configured));
        var configuredWidth = configured?.Width ?? 1280;
        var configuredHeight = configured?.Height ?? 720;
        WidthTextBox.Text = configuredWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HeightTextBox.Text = configuredHeight.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ViewportComboBox.SelectedItem = ViewportOptions.FirstOrDefault(item =>
            item.Width == configuredWidth && item.Height == configuredHeight) ?? ViewportOptions[^1];
        RefreshSources(configured?.Source);
        initialized = true;
        SetConfigurationControlsEnabled();
        Loaded += async (_, _) => await RefreshPreviewAsync(showErrors: false);
        Closed += PreviewDesignerDialog_Closed;
    }

    public FoundryWorkspace? UpdatedWorkspace { get; private set; }

    internal string SelectedKindDisplayText => KindComboBox.Text;

    internal string SelectedSourceDisplayText => SourceComboBox.Text;

    internal string SelectedViewportDisplayText => ViewportComboBox.Text;

    internal async Task<bool> RunRuntimeSmokeTestAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedSourceDisplayText))
        {
            return Content is not null;
        }
        await RefreshPreviewAsync(showErrors: false);
        return lastSurface is not null &&
            runtimeSession.State.Status == PreviewRuntimeStatus.Completed &&
            RuntimeLogTextBox.Text.Contains("scripts were not loaded", StringComparison.Ordinal);
    }

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
        SourceComboBox.SelectedItem = !string.IsNullOrWhiteSpace(preferred)
            ? sources.FirstOrDefault(source => string.Equals(source, preferred, StringComparison.OrdinalIgnoreCase))
            : sources.Count > 0 ? sources[0] : null;
    }

    private async Task RefreshPreviewAsync(bool showErrors, bool restart = false)
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        var cancellationToken = refreshCancellation.Token;
        if (!TryCreateConfiguration(out var configuration, showErrors))
        {
            PreviewStatusText.Text = "No eligible source is selected. Add a supported .html file, enable WinForms with a declared .cs source, or use OBS design metadata.";
            return;
        }
        PreviewStatusText.Text = "Analyzing source structure...";
        var result = await PreviewDesignService.AnalyzeAsync(workspace, configuration, cancellationToken);
        if (!result.IsSuccess)
        {
            DesignCanvas.Children.Clear();
            PreviewStatusText.Text = string.Join(" ", result.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
            return;
        }
        lastSurface = result.Value!;
        ConfigureSourceWatcher(configuration.Source);
        RenderStructural(lastSurface);
        PreviewStatusText.Text = "Structural frame ready. Starting isolated runtime renderer...";
        var runtimeResult = restart
            ? await runtimeSession.RestartAsync(lastSurface, cancellationToken)
            : await runtimeSession.RefreshAsync(lastSurface, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        RuntimeLogTextBox.Text = runtimeResult.Logs.Count == 0
            ? "No runtime host output was produced."
            : string.Join(Environment.NewLine, runtimeResult.Logs);
        if (runtimeResult.IsSuccess)
        {
            RenderRuntime(runtimeResult.Frame!);
            PreviewStatusText.Text =
                $"Isolated runtime frame generation {runtimeResult.Frame!.Generation} completed in {runtimeResult.Duration.TotalMilliseconds:0} ms. Source SHA-256: {runtimeResult.Frame.SourceSha256[..12]}...";
            return;
        }

        var diagnosticText = string.Join(" ", runtimeResult.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        PreviewStatusText.Text = string.IsNullOrWhiteSpace(diagnosticText)
            ? "Runtime preview stopped. The last structural frame remains available."
            : $"{diagnosticText} The last structural frame remains available.";
    }

    private void RenderStructural(PreviewDesignSurface surface)
    {
        DesignCanvas.Children.Clear();
        DesignCanvas.Width = surface.ViewportWidth;
        DesignCanvas.Height = surface.ViewportHeight;
        SurfaceTitleText.Text = $"{workspace.Manifest.Name} - {surface.Kind}";
        ViewportStatusText.Text = $"{surface.ViewportWidth} x {surface.ViewportHeight}";
        foreach (var element in surface.Elements)
        {
            AddVisualElement(
                new(
                    element.Kind,
                    element.Name,
                    element.Label,
                    "panel",
                    element.X,
                    element.Y,
                    element.Width,
                    element.Height));
        }
        PreviewStatusText.Text = $"{surface.Notice} Source SHA-256: {surface.SourceSha256[..12]}...";
    }

    private void RenderRuntime(PreviewRuntimeFrame frame)
    {
        DesignCanvas.Children.Clear();
        DesignCanvas.Width = frame.ViewportWidth;
        DesignCanvas.Height = frame.ViewportHeight;
        SurfaceTitleText.Text = $"{workspace.Manifest.Name} - isolated {frame.Kind}";
        ViewportStatusText.Text = $"{frame.ViewportWidth} x {frame.ViewportHeight}";
        foreach (var element in frame.Elements)
        {
            AddVisualElement(element);
        }
    }

    private void AddVisualElement(PreviewRuntimeElement element)
    {
        var label = new TextBlock
        {
            Margin = new Thickness(element.VisualRole == "badge" ? 6 : 10),
            Text = element.Label,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = element.VisualRole is "heading" or "action" or "badge"
                ? FontWeights.Bold
                : FontWeights.SemiBold,
            VerticalAlignment = element.VisualRole == "action"
                ? VerticalAlignment.Center
                : VerticalAlignment.Top,
            HorizontalAlignment = element.VisualRole == "action"
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var border = new Border
        {
            Width = Math.Max(20, element.Width),
            Height = Math.Max(20, element.Height),
            BorderThickness = new Thickness(element.VisualRole is "action" or "canvas" ? 3 : 1.5),
            CornerRadius = new CornerRadius(element.VisualRole is "action" or "badge" ? 7 : 3),
            Child = label,
            ToolTip = $"{element.Kind}: {element.Name} ({element.VisualRole})",
            Opacity = element.VisualRole == "media" ? 0.82 : 1,
        };
        border.SetResourceReference(
            Border.BackgroundProperty,
            element.VisualRole is "input" or "media"
                ? "EditorBackgroundBrush"
                : "ButtonBackgroundBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        Canvas.SetLeft(border, element.X);
        Canvas.SetTop(border, element.Y);
        DesignCanvas.Children.Add(border);
    }

    private bool TryCreateConfiguration(out FoundryPreview configuration, bool showErrors)
    {
        configuration = new();
        if (KindComboBox.SelectedItem is not PreviewKindOption kind ||
            SourceComboBox.SelectedItem is not string source ||
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
            Source = source.Replace('\\', '/'),
            Width = width,
            Height = height,
        };
        return true;
    }

    private async void RefreshPreview_Click(object sender, RoutedEventArgs e) =>
        await RefreshPreviewAsync(showErrors: true);

    private async void RestartRuntime_Click(object sender, RoutedEventArgs e) =>
        await RefreshPreviewAsync(showErrors: true, restart: true);

    private void StopRuntime_Click(object sender, RoutedEventArgs e)
    {
        refreshCancellation?.Cancel();
        runtimeSession.Stop();
        PreviewStatusText.Text = "Runtime preview stopped. The last completed frame remains visible.";
    }

    private void Kind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        RefreshSources();
        ScheduleAutoRefresh();
    }

    private void Source_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        ScheduleAutoRefresh();
    }

    private void Viewport_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || ViewportComboBox.SelectedItem is not ViewportOption { Width: > 0, Height: > 0 } viewport) return;
        WidthTextBox.Text = viewport.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HeightTextBox.Text = viewport.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ScheduleAutoRefresh();
    }

    private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        if (AutoRefreshCheckBox.IsChecked == true)
        {
            ScheduleAutoRefresh();
        }
        else
        {
            autoRefreshTimer.Stop();
        }
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
        AutoRefreshCheckBox.IsEnabled = enabled;
        StopRuntimeButton.IsEnabled = enabled &&
            runtimeSession.State.Status is PreviewRuntimeStatus.Starting or PreviewRuntimeStatus.Running;
        RestartRuntimeButton.IsEnabled = enabled;
    }

    private void ConfigureSourceWatcher(string relativeSource)
    {
        sourceWatcher?.Dispose();
        sourceWatcher = null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.ProjectRoot));
            var fullPath = Path.GetFullPath(Path.Combine(root, relativeSource.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (directory is null || !Directory.Exists(directory) || string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }
            sourceWatcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            sourceWatcher.Changed += SourceWatcher_Changed;
            sourceWatcher.Created += SourceWatcher_Changed;
            sourceWatcher.Renamed += SourceWatcher_Changed;
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException or PathTooLongException)
        {
            RuntimeLogTextBox.Text = $"Automatic source refresh is unavailable: {exception.Message}";
        }
    }

    private void SourceWatcher_Changed(object sender, FileSystemEventArgs e) =>
        Dispatcher.BeginInvoke(ScheduleAutoRefresh, DispatcherPriority.Background);

    private void ScheduleAutoRefresh()
    {
        if (!IsLoaded || AutoRefreshCheckBox.IsChecked != true)
        {
            return;
        }
        autoRefreshTimer.Stop();
        autoRefreshTimer.Start();
        RuntimeStateText.Text = "Source change detected; refresh queued...";
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        autoRefreshTimer.Stop();
        await RefreshPreviewAsync(showErrors: false);
    }

    private void RuntimeSession_StateChanged(object? sender, PreviewRuntimeState state) =>
        Dispatcher.BeginInvoke(
            () =>
            {
                RuntimeStateText.Text = $"{state.Status}: {state.Message}";
                StopRuntimeButton.IsEnabled = state.Status is PreviewRuntimeStatus.Starting or PreviewRuntimeStatus.Running;
                RestartRuntimeButton.IsEnabled = EnabledCheckBox.IsChecked == true;
            },
            DispatcherPriority.Background);

    private async void PreviewDesignerDialog_Closed(object? sender, EventArgs e)
    {
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        autoRefreshTimer.Stop();
        sourceWatcher?.Dispose();
        sourceWatcher = null;
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = null;
        runtimeSession.StateChanged -= RuntimeSession_StateChanged;
        await runtimeSession.DisposeAsync();
        GC.SuppressFinalize(this);
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

    private sealed record PreviewKindOption(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record ViewportOption(string DisplayName, int Width, int Height)
    {
        public override string ToString() => DisplayName;
    }
}
