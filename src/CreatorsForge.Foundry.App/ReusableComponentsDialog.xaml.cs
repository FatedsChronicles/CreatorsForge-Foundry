using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Workspaces;

namespace CreatorsForge.Foundry.App;

public partial class ReusableComponentsDialog : Window
{
    private readonly FoundryWorkspace workspace;

    public ReusableComponentsDialog(FoundryWorkspace workspace)
    {
        this.workspace = workspace;
        InitializeComponent();
        var provider = workspace.Manifest.Target!.Provider;
        ProviderText.Text = $"Compatible with {provider}. Components are copied into the project and recorded as deterministic build inputs.";
        ComponentsList.ItemsSource = FoundryReusableComponentService.Components
            .Where(item => string.Equals(item.Provider, provider, StringComparison.Ordinal))
            .ToArray();
        ComponentsList.SelectedIndex = ComponentsList.Items.Count == 0 ? -1 : 0;
    }

    public FoundryWorkspace? UpdatedWorkspace { get; private set; }

    private void ComponentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComponentsList.SelectedItem is not FoundryReusableComponentDescriptor component)
        {
            AddButton.IsEnabled = false;
            StatusText.Text = string.Empty;
            return;
        }

        var installed = (workspace.Manifest.Components ?? []).Any(item =>
            string.Equals(item.Id, component.Id, StringComparison.Ordinal));
        AddButton.IsEnabled = !installed;
        StatusText.Text = installed
            ? $"Installed: {component.Version}"
            : $"{component.Language} · version {component.Version}";
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (ComponentsList.SelectedItem is not FoundryReusableComponentDescriptor component) return;
        AddButton.IsEnabled = false;
        var result = await FoundryReusableComponentService.InstallAsync(workspace, component.Id);
        if (!result.IsSuccess)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")),
                "Component not added",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            AddButton.IsEnabled = true;
            return;
        }

        UpdatedWorkspace = result.Value;
        DialogResult = true;
    }
}
