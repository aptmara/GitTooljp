using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace SimplePRClient
{
    /// <summary>
    /// Logique d'interaction pour DependencyWindow.xaml
    /// </summary>
    public partial class DependencyWindow : Window
    {
        private bool _isGitMissing;
        private bool _isGhMissing;

        public bool RetryRequested { get; private set; } = false;

        public DependencyWindow(bool isGitMissing, bool isGhMissing)
        {
            InitializeComponent();
            _isGitMissing = isGitMissing;
            _isGhMissing = isGhMissing;

            UpdateUI();
        }

        private void UpdateUI()
        {
            GitPanel.Visibility = _isGitMissing ? Visibility.Visible : Visibility.Collapsed;
            GhPanel.Visibility = _isGhMissing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DownloadGit_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://git-scm.com/download/win");
        }

        private void DownloadGh_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl("https://cli.github.com/");
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            RetryRequested = true;
            this.Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            RetryRequested = false;
            this.Close();
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ブラウザを開けませんでした: {ex.Message}");
            }
        }
    }
}
