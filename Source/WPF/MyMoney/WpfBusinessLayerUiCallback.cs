using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using Walkabout.Controls;
using Walkabout.Data;
using Walkabout.Utilities;

namespace Walkabout
{
    public class WpfBusinessLayerUiCallback : IBusinessLayerUiCallback
    {
        private readonly IServiceProvider provider;

        // provider resolves WPF-only services (e.g. OutputPane) that ClearOutputLog/AppendErrorLog
        // need but MyMoney.Business can't reference directly. MainWindow already implements
        // IServiceProvider and is the provider StockQuoteManager itself is constructed with.
        public WpfBusinessLayerUiCallback(IServiceProvider provider)
        {
            this.provider = provider;
        }

        public void ShowError(string message, string title)
        {
            MessageBoxEx.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Confirm(string message, string title)
        {
            return MessageBoxEx.Show(message, title, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        public bool ConfirmOkCancel(string message, string title)
        {
            return MessageBoxEx.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK;
        }

        public void ClearOutputLog(string heading)
        {
            OutputPane output = (OutputPane)this.provider.GetService(typeof(OutputPane));
            output.Clear();
            output.AppendHeading(heading);
        }

        public void AppendErrorLog(string errorMessages, string logFilePath, bool activate)
        {
            Paragraph p = new Paragraph();
            p.Inlines.Add(errorMessages);
            if (!string.IsNullOrEmpty(logFilePath))
            {
                p.Inlines.Add("See ");
                var link = new Hyperlink() { NavigateUri = new Uri("file://" + logFilePath) };
                link.Cursor = Cursors.Arrow;
                link.PreviewMouseLeftButtonDown += this.OnShowLogFile;
                link.Inlines.Add("Log File");
                p.Inlines.Add(link);
                p.Inlines.Add(" for details");
            }
            OutputPane output = (OutputPane)this.provider.GetService(typeof(OutputPane));
            output.AppendParagraph(p);
            if (activate)
            {
                output.Show();
            }
        }

        private void OnShowLogFile(object sender, RoutedEventArgs e)
        {
            Hyperlink link = (Hyperlink)sender;
            Uri uri = link.NavigateUri;
            InternetExplorer.OpenUrl(IntPtr.Zero, uri.AbsoluteUri);
        }
    }
}
