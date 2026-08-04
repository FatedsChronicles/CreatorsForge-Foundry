using System.Text;
using System.Windows;
using System.Windows.Controls;
using CreatorsForge.Foundry.Editor;

namespace CreatorsForge.Foundry.App;

public partial class ObsApiReferenceDialog : Window
{
    private readonly ObsNativeCatalogue catalogue;
    private readonly string profile;

    public ObsApiReferenceDialog(ObsNativeCatalogue catalogue, string profile)
    {
        this.catalogue = catalogue;
        this.profile = profile;
        InitializeComponent();
        ProfileText.Text = $"Profile: {profile} - SDK: {catalogue.SdkVersion}";
        RevisionText.Text = $"Catalogue {catalogue.Revision}";
        RefreshSymbols();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshSymbols();

    private void SymbolsList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        DetailsTextBox.Text = SymbolsList.SelectedItem is ObsNativeSymbol symbol
            ? FormatSymbol(symbol)
            : "Select an OBS symbol.";
    }

    private void RefreshSymbols()
    {
        var filter = SearchTextBox?.Text?.Trim() ?? string.Empty;
        var symbols = catalogue.Symbols
            .Where(symbol => symbol.Profiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
            .Where(symbol =>
                filter.Length == 0 ||
                symbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                symbol.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                symbol.Header.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                symbol.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SymbolsList.ItemsSource = symbols;
        if (symbols.Length != 0)
        {
            SymbolsList.SelectedIndex = 0;
        }
    }

    private static string FormatSymbol(ObsNativeSymbol symbol)
    {
        var builder = new StringBuilder()
            .AppendLine(symbol.Name)
            .Append(symbol.Kind).Append(" - ").AppendLine(symbol.Category)
            .Append("Header: <").Append(symbol.Header).AppendLine(">")
            .Append("Minimum SDK: ").AppendLine(symbol.MinimumVersion)
            .AppendLine()
            .AppendLine(symbol.Signature)
            .AppendLine()
            .AppendLine(symbol.Summary);
        if (symbol.Parameters.Count != 0)
        {
            builder.AppendLine().AppendLine("Parameters");
            foreach (var parameter in symbol.Parameters)
            {
                builder.Append("  ").Append(parameter.Name).Append(": ")
                    .AppendLine(parameter.Description);
            }
        }

        if (!string.IsNullOrWhiteSpace(symbol.Caution))
        {
            builder.AppendLine().Append("Caution: ").AppendLine(symbol.Caution);
        }

        if (!string.IsNullOrWhiteSpace(symbol.DocumentationUrl))
        {
            builder.AppendLine().Append("Official reference: ")
                .AppendLine(symbol.DocumentationUrl);
        }

        return builder.ToString();
    }
}
