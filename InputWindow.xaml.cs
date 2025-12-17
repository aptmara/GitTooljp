namespace SimplePRClient;

using System.Windows;
using System.Windows.Controls;

public partial class InputWindow : Window
{
    public string InputText => InputTextBox.Text;

    public InputWindow(string prompt, string title)
    {
        InitializeComponent();
        Title = title;
        PromptBlock.Text = prompt;
        InputTextBox.Focus();
    }
    
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}