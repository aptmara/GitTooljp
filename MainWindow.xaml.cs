namespace SimplePRClient;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
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