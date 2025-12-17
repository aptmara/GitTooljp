namespace SimplePRClient;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls;
using SimplePRClient.Services;
using SimplePRClient.ViewModels;

/// @brief メインウィンドウの相互作用ロジック
/// 作成者: 山内陽
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Simple DI setup for MVP
        var runner = new ProcessRunner();
        var gitService = new GitService(runner);
        
        // Find git root automatically from current dir if possible, or simple default
        // The plan says "Start from current dir", assuming user launches app from repo or selects it.
        // For now, let's use Environment.CurrentDirectory
        var root = GitService.FindGitRoot(Environment.CurrentDirectory);
        if (root != null)
        {
             gitService.SetRepository(root);
        }
        else
        {
             // If not found, one might want to prompt folder selection. 
             // For build-check MVP, we leave it empty or user sets it via logic (not implemented yet).
             // StateService will return flags indicating issues.
        }

        var gitHubService = new GitHubService(runner);
        if (root != null) gitHubService.SetRepository(root);

        var stateService = new StateService(gitService, gitHubService);
        var toolDetector = new ToolDetector();

        var viewModel = new MainViewModel(gitService, gitHubService, stateService, toolDetector);
        
        // Initial Refresh (Silent)
        Loaded += async (s, e) => 
        {
             await viewModel.InitializeAsync();
        };

        DataContext = viewModel;
    }

    // click copy
    private void LogListBox_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement((ListBox)sender, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item != null)
        {
            // Copy content
            var text = item.Content?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
            }
        }
    }

    // Ctrl+C copy
    private void LogListBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.C && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            var listBox = sender as System.Windows.Controls.ListBox;
            if (listBox != null && listBox.SelectedItems.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var item in listBox.SelectedItems)
                {
                    sb.AppendLine(item.ToString());
                }
                Clipboard.SetText(sb.ToString().TrimEnd());
                // Mark handled to prevent default beep if any
                e.Handled = true;
            }
        }
    }
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // IsError (true) -> Red, else Black
        if (value is bool isError && isError)
            return Brushes.Red;
        return Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}