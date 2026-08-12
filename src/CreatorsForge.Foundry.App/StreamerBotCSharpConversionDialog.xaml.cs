using System.Windows;

namespace CreatorsForge.Foundry.App;

public partial class StreamerBotCSharpConversionDialog : Window
{
    public StreamerBotCSharpConversionDialog(string summary, string source)
    {
        InitializeComponent();
        SummaryText.Text = summary;
        SourceTextBox.Text = source;
    }

    private void Convert_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
