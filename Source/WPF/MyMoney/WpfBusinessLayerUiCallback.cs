using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Win32;
using Walkabout.Attachments;
using Walkabout.Controls;
using Walkabout.Data;
using Walkabout.Dialogs;
using Walkabout.Importers;
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

        // Added in Task 7: DatabaseLifecycle's schema-upgrade prompt used the five-argument
        // MessageBoxEx.Show overload (message, title, details, buttons, image) directly.
        public bool ConfirmWithDetails(string message, string title, string details)
        {
            return MessageBoxEx.Show(message, title, details, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
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

        // Added in Task 6, alongside the Importers move: CsvTransactionImporter.EditCsvMap used
        // to construct/own/ShowDialog a real WPF CsvImportDialog directly.
        public CsvMap PromptForCsvFieldMapping(string[] expectedColumns, IEnumerable<string> headers, CsvMap existingMap)
        {
            CsvImportDialog cd = new CsvImportDialog(expectedColumns);
            cd.Owner = Application.Current.MainWindow;
            if (headers != null)
            {
                cd.SetHeaders(headers);
            }
            else
            {
                cd.SetMap(existingMap);
            }
            if (cd.ShowDialog() == true)
            {
                return cd.Mapping;
            }
            return null;
        }

        // Added in Task 6: Exporters.ExportPrompt used to construct/show a WPF SaveFileDialog
        // owned by the main window directly.
        public string PromptSaveFileName(string filter)
        {
            SaveFileDialog sd = new SaveFileDialog();
            sd.Filter = filter;
            if (sd.ShowDialog(Application.Current.MainWindow) == true)
            {
                return sd.FileName;
            }
            return null;
        }

        // Added in Task 6: Exporters.Export used to shell-execute the just-written file via the
        // WPF-project-only, internal Utilities.InternetExplorer helper directly.
        public void OpenExportedFile(string filePath, bool applyXsltTransform)
        {
            if (applyXsltTransform)
            {
                InternetExplorer.EditTransform(IntPtr.Zero, filePath);
            }
            else
            {
                InternetExplorer.OpenUrl(IntPtr.Zero, filePath);
            }
        }

        // Added in Task 6: XmlImporter's "cut" path used to resolve AttachmentManager via
        // IServiceProvider.GetService(typeof(AttachmentManager)) directly.
        public void MoveAttachments(Transaction original, Account newAccount)
        {
            AttachmentManager mgr = this.provider.GetService(typeof(AttachmentManager)) as AttachmentManager;
            mgr?.MoveAttachments(original, newAccount);
        }

        // Added in Task 6: CsvImportController/CsvTransactionImporter used to call
        // Walkabout.Dialogs.AccountHelper.PickAccount directly.
        public Account PickAccount(MyMoney money, Account accountTemplate, string prompt)
        {
            return Walkabout.Dialogs.AccountHelper.PickAccount(money, accountTemplate, prompt);
        }
    }
}
