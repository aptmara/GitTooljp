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
        
        // 起動時は選択画面を表示（自動でリポジトリを開かない）
        // ユーザーがリポジトリを選択するか、最近使ったリポジトリから選ぶ

        var gitHubService = new GitHubService(runner);

        var stateService = new StateService(gitService, gitHubService);
        var toolDetector = new ToolDetector();
        var settingsService = new SettingsService();

        var viewModel = new MainViewModel(gitService, gitHubService, stateService, toolDetector, settingsService);
        
        // Initial Refresh (Silent)
        Loaded += async (s, e) => 
        {
             await viewModel.InitializeAsync();
        };

        DataContext = viewModel;
    }

    private void RecentRepo_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem item && item.Content is string path)
        {
             if (DataContext is MainViewModel vm)
             {
                 if (vm.OpenRecentRepositoryCommand.CanExecute(path))
                    vm.OpenRecentRepositoryCommand.Execute(path);
             }
        }
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