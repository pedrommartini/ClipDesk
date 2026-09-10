using System.Windows;

namespace ClipDesk.Views;

public partial class RenameWindow : Window
{
    public RenameWindow(string currentName, string? title = null, string? prompt = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(title)) Title = title;
        if (!string.IsNullOrWhiteSpace(prompt)) PromptText.Text = prompt;
        NameBox.Text = currentName;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    public string ItemName => NameBox.Text.Trim();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ItemName))
        {
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
