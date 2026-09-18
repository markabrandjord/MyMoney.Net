using System.IO;
using System.Windows;
using Microsoft.Win32;
using Walkabout.Utilities;

namespace Walkabout.Dialogs
{
    public partial class NewSqliteDatabaseDialog : Window
    {
        public string DisplayName { get; private set; }
        public string FilePath { get; private set; }
        public bool TestDatabase { get; private set; }

        public NewSqliteDatabaseDialog()
        {
            this.InitializeComponent();
#if !DEBUG
            this.CheckBoxTestDatabase.Visibility = Visibility.Collapsed;
#endif
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "MyMoney Database (*.mmdb)|*.mmdb", DefaultExt = ".mmdb", CheckFileExists = false };
            if (dlg.ShowDialog(this) == true)
            {
                this.TextBoxFile.Text = dlg.FileName;
            }
        }

        private void OnCreate(object sender, RoutedEventArgs e)
        {
            this.FilePath = this.TextBoxFile.Text.Trim();
            this.DisplayName = string.IsNullOrWhiteSpace(this.TextBoxDisplayName.Text)
                ? Path.GetFileName(this.FilePath)
                : this.TextBoxDisplayName.Text.Trim();
#if DEBUG
            this.TestDatabase = this.CheckBoxTestDatabase.IsChecked == true;
#endif

            if (string.IsNullOrEmpty(this.FilePath) || string.IsNullOrEmpty(this.DisplayName))
            {
                MessageBoxEx.Show("File location and display name are both required.", "New Database", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
        }
    }
}
