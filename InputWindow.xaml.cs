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
    
    // XAML-less construction for simplicity or need XAML?
    // Let's assume we need XAML for this too as it was deleted.
    // I'll create XAML for this in next step.
    // Wait, simple InputBox might be better. 
    // But since I'm restoring, I should restore what was intended.
    // The previously deleted files included InputWindow.xaml.
    // I will recreate it.
    
    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
