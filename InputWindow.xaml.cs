namespace SimplePRClient;

using System.Windows;

public partial class InputWindow : Window
{
    public string InputText => InputTextBox.Text;

    public InputWindow(string message, string title)
    {
        InitializeComponent();
        MessageText.Text = message;
        Title = title;
        InputTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
