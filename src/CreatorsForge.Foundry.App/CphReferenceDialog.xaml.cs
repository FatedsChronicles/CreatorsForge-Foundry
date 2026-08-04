using System.Text;
using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Editor;

namespace CreatorsForge.Foundry.App;

public partial class CphReferenceDialog : Window
{
    private readonly CphCatalogue catalogue;
    private readonly string profile;

    public CphReferenceDialog(CphCatalogue catalogue, string profile)
    {
        this.catalogue = catalogue;
        this.profile = profile;
        InitializeComponent();
        ProfileText.Text = $"Profile: {profile}";
        RevisionText.Text = $"Catalogue {catalogue.Revision}";
        RefreshMethods();
    }

    private void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        RefreshMethods();

    private void MethodsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        DetailsTextBox.Text = MethodsList.SelectedItem is CphMethod method
            ? FormatMethod(method)
            : "Select a CPH method.";
    }

    private void RefreshMethods()
    {
        var filter = SearchTextBox?.Text?.Trim() ?? string.Empty;
        var methods = catalogue.Methods
            .Where(IsAvailable)
            .Where(method =>
                filter.Length == 0 ||
                method.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                method.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                method.Platform.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                method.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        MethodsList.ItemsSource = methods;
        if (methods.Length != 0)
        {
            MethodsList.SelectedIndex = 0;
        }
    }

    private bool IsAvailable(CphMethod method) =>
        method.Overloads.Any(overload =>
            overload.Profiles.Contains(profile, StringComparer.Ordinal));

    private string FormatMethod(CphMethod method)
    {
        var builder = new StringBuilder()
            .Append("CPH.").AppendLine(method.Name)
            .Append(method.Category).Append(" · ").AppendLine(method.Platform)
            .Append("Status: ").AppendLine(method.Status)
            .Append("Minimum version: ").AppendLine(method.MinimumVersion)
            .AppendLine()
            .AppendLine(method.Summary)
            .AppendLine();
        foreach (var overload in method.Overloads.Where(overload =>
                     overload.Profiles.Contains(profile, StringComparer.Ordinal)))
        {
            builder.AppendLine(overload.Signature);
            foreach (var parameter in overload.Parameters)
            {
                builder
                    .Append("  ")
                    .Append(parameter.Name)
                    .Append(": ")
                    .AppendLine(parameter.Description);
            }

            builder
                .Append("  Available: ")
                .AppendLine(string.Join(", ", overload.Profiles))
                .AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(method.Example))
        {
            builder.AppendLine("Example").AppendLine(method.Example).AppendLine();
        }

        foreach (var caution in method.Cautions)
        {
            builder.Append("Caution: ").AppendLine(caution);
        }

        if (method.RelatedMethods.Count != 0)
        {
            builder
                .Append("Related: ")
                .AppendLine(string.Join(", ", method.RelatedMethods.Select(name => $"CPH.{name}")));
        }

        if (!string.IsNullOrWhiteSpace(method.DocumentationUrl))
        {
            builder.Append("Official reference: ").AppendLine(method.DocumentationUrl);
        }

        return builder.ToString();
    }
}
