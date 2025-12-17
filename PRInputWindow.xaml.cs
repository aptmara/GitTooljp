namespace SimplePRClient;

using System.Windows;

public partial class PRInputWindow : Window
{
    public string PRTitle => TitleTextBox.Text;
    public string PRBody => BodyTextBox.Text;

    public PRInputWindow(string defaultTitle, string defaultBody)
    {
        InitializeComponent();
        TitleTextBox.Text = defaultTitle;
        BodyTextBox.Text = defaultBody;
        TitleTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
