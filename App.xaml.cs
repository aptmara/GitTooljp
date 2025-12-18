using System.Configuration;
using System.Data;
using System.Windows;

namespace SimplePRClient;

/// @brief App.xaml の相互作用ロジック
/// 作成者: 山内陽
public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception, "UI/Dispatcher");
        // Optional: Set e.Handled = true if you want to prevent crash, 
        // but user asked for logging when "dropping" the app, so we let it bubble up or just close.
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogError(e.ExceptionObject as Exception, "AppDomain");
    }

    private void LogError(Exception? ex, string source)
    {
        if (ex == null) return;
        try
        {
            string logFile = "error.log";
            string errorMessage = $"[{DateTime.Now}] [{source}] Unhandled Exception:\n{ex}\n\n";
            System.IO.File.AppendAllText(logFile, errorMessage);
            MessageBox.Show($"予期せぬエラーが発生しました。\nログを保存しました: {System.IO.Path.GetFullPath(logFile)}\n\n{ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Failed to log
        }
    }
}

