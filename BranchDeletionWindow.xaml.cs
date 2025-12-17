using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SimplePRClient
{
    public partial class BranchDeletionWindow : Window
    {
        public List<string> SelectedBranches { get; private set; } = new List<string>();

        public BranchDeletionWindow(IEnumerable<string> branches)
        {
            InitializeComponent();
            BranchListBox.ItemsSource = branches;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (BranchListBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("削除するブランチを選択してください。", "未選択", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedBranches = BranchListBox.SelectedItems.Cast<string>().ToList();
            DialogResult = true;
            Close();
        }
    }
}
