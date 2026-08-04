using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Core.Projects;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class ObsPluginDesignerDialog : Window
{
    private readonly FoundryWorkspace workspace;
    private bool initialized;

    public ObsPluginDesignerDialog(FoundryWorkspace workspace)
    {
        this.workspace = workspace;
        InitializeComponent();
        LoadDesign();
    }

    public FoundryObsPlugin? UpdatedPlugin { get; private set; }

    public FoundryObsDesign? UpdatedDesign { get; private set; }

    public string? GeneratedSource { get; private set; }

    private void LoadDesign()
    {
        var plugin = workspace.Manifest.ObsPlugin ??
            throw new InvalidOperationException("The project has no OBS plugin metadata.");
        var sources = workspace.Manifest.NativeBuild?.Sources;
        var source = sources is { Count: > 0 }
            ? sources[0]
            : throw new InvalidOperationException("The project has no native source.");
        var design = plugin.Design ?? new FoundryObsDesign
        {
            Template = FoundryObsDesign.PassthroughFilterTemplate,
            Source = source,
            ComponentId = $"{workspace.Manifest.Id}.filter",
            ComponentName = plugin.DisplayName,
        };

        ContractText.Text = $"{plugin.Contract} - API {plugin.ApiVersion} - SDK {plugin.SdkVersion ?? "none"}";
        ModuleNameTextBox.Text = plugin.ModuleName;
        DisplayNameTextBox.Text = plugin.DisplayName;
        AuthorTextBox.Text = plugin.Author;
        DescriptionTextBox.Text = plugin.Description;
        TemplateComboBox.ItemsSource = ObsPluginTemplateService.Templates;
        TemplateComboBox.SelectedItem = ObsPluginTemplateService.Templates.First(item =>
            item.Id == design.Template);
        SourceComboBox.ItemsSource = workspace.Manifest.NativeBuild!.Sources;
        SourceComboBox.SelectedItem = design.Source;
        ComponentIdTextBox.Text = design.ComponentId;
        ComponentNameTextBox.Text = design.ComponentName;
        initialized = true;
        RefreshPreview();
    }

    private void Template_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateComboBox.SelectedItem is ObsPluginTemplateDescriptor template)
        {
            TemplateDescriptionText.Text = template.Description;
            TemplateResultText.Text = template.Result;
        }

        RefreshPreview();
    }

    private void DesignField_Changed(object sender, EventArgs e) =>
        RefreshPreview();

    private void RefreshPreview()
    {
        if (!initialized || !TryCreateDesign(out var plugin, out var design))
        {
            return;
        }

        var result = ObsPluginTemplateService.Generate(plugin!, design!);
        GeneratedPreviewTextBox.Text = result.Source ?? string.Empty;
        StatusText.Text = result.IsSuccess
            ? "Preview is valid. Review it before replacing the source file."
            : result.Errors.Count > 0
                ? result.Errors[0]
                : "Complete the design fields.";

        var sourcePath = Path.Combine(
            workspace.ProjectRoot,
            design!.Source.Replace('/', Path.DirectorySeparatorChar));
        CurrentSourceTextBox.Text = File.Exists(sourcePath)
            ? File.ReadAllText(sourcePath)
            : "The selected source file does not exist.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCreateDesign(out var plugin, out var design))
        {
            return;
        }

        var generated = ObsPluginTemplateService.Generate(plugin!, design!);
        if (!generated.IsSuccess)
        {
            ShowValidation(generated.Errors);
            return;
        }

        var manifest = workspace.Manifest with
        {
            ObsPlugin = plugin! with { Design = design },
        };
        var errors = FoundryProjectValidator.Validate(manifest, workspace.ProjectPath)
            .Where(diagnostic => diagnostic.IsError)
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")
            .ToArray();
        if (errors.Length != 0)
        {
            ShowValidation(errors);
            return;
        }

        if (!string.Equals(
                CurrentSourceTextBox.Text,
                generated.Source,
                StringComparison.Ordinal) &&
            ConfirmOverwriteCheckBox.IsChecked != true)
        {
            StatusText.Text = "Review the preview and confirm source replacement.";
            MessageBox.Show(
                this,
                "The generated template will replace the selected C source. Review both preview tabs, then select the confirmation box.",
                "Source replacement confirmation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        UpdatedPlugin = plugin;
        UpdatedDesign = design;
        GeneratedSource = generated.Source;
        DialogResult = true;
    }

    private bool TryCreateDesign(
        out FoundryObsPlugin? plugin,
        out FoundryObsDesign? design)
    {
        plugin = null;
        design = null;
        if (!initialized ||
            workspace.Manifest.ObsPlugin is not { } existing ||
            TemplateComboBox.SelectedItem is not ObsPluginTemplateDescriptor template ||
            SourceComboBox.SelectedItem is not string source)
        {
            return false;
        }

        plugin = existing with
        {
            ModuleName = ModuleNameTextBox.Text.Trim(),
            DisplayName = DisplayNameTextBox.Text.Trim(),
            Author = AuthorTextBox.Text.Trim(),
            Description = DescriptionTextBox.Text.Trim(),
        };
        design = new FoundryObsDesign
        {
            Template = template.Id,
            Source = source,
            ComponentId = ComponentIdTextBox.Text.Trim(),
            ComponentName = ComponentNameTextBox.Text.Trim(),
            AdditionalProperties = existing.Design?.AdditionalProperties,
        };
        return true;
    }

    private void ShowValidation(IEnumerable<string> errors)
    {
        var messages = errors.ToArray();
        StatusText.Text = messages.FirstOrDefault() ?? "Design validation failed.";
        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, messages),
            "OBS design validation",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
