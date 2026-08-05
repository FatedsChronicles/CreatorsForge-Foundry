using System.Windows;

namespace CreatorsForge.Foundry.App;

public partial class RenameProjectItemDialog : Window
{
    public RenameProjectItemDialog(string currentName)
    {
        InitializeComponent();
        NameTextBox.Text = currentName;
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    public string NewName => NameTextBox.Text.Trim();

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            MessageBox.Show(this, "Enter a new name.", "Rename project item", MessageBoxButton.OK, MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
