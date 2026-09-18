using System.Windows;
using Walkabout.Data;
using Walkabout.Utilities;

namespace Walkabout.Dialogs
{
    public partial class NewSqlServerDatabaseDialog : Window
    {
        public string DisplayName { get; private set; }
        public string ServerName { get; private set; }
        public string CatalogName { get; private set; }
        public bool TestDatabase { get; private set; }

        public NewSqlServerDatabaseDialog(DatabaseRegistry registry)
        {
            this.InitializeComponent();
            foreach (string server in registry.Servers.Keys)
            {
                this.ComboBoxServer.Items.Add(server);
            }
        }

        private void OnCreate(object sender, RoutedEventArgs e)
        {
            this.CatalogName = this.TextBoxCatalog.Text.Trim();
            this.ServerName = (this.ComboBoxServer.Text ?? string.Empty).Trim();
            this.DisplayName = string.IsNullOrWhiteSpace(this.TextBoxDisplayName.Text)
                ? this.CatalogName
                : this.TextBoxDisplayName.Text.Trim();
            this.TestDatabase = this.CheckBoxTestDatabase.IsChecked == true;

            if (string.IsNullOrEmpty(this.ServerName) || string.IsNullOrEmpty(this.CatalogName) || string.IsNullOrEmpty(this.DisplayName))
            {
                MessageBoxEx.Show("Server, catalog name, and display name are all required.", "New SQL Server Database", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
        }
    }
}
