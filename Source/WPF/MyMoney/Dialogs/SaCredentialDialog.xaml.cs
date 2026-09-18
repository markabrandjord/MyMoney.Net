using System.Windows;
using Walkabout.Data;

namespace Walkabout.Dialogs
{
    public partial class SaCredentialDialog : Window, ISaCredentialPrompt
    {
        public SaCredentialDialog()
        {
            this.InitializeComponent();
        }

        public bool TryGetSaPassword(string server, out string password)
        {
            this.TextBlockPrompt.Text = $"Enter the 'sa' password for server '{server}' to bootstrap it for MyMoney.";
            bool? result = this.ShowDialog();
            password = result == true ? this.TextBoxPassword.Password : null;
            return result == true;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
