using System.Collections.Generic;
using Walkabout.Importers;

namespace Walkabout.Data
{
    /// <summary>
    /// MyMoney.Business has no WPF dependency, so code that needs to show
    /// an error or ask the user a yes/no question (StockQuoteManager,
    /// the Importers, Ofx.cs's protocol-edge-case prompts) goes through
    /// this instead of calling MessageBoxEx directly. MyMoney.csproj
    /// supplies the real, WPF-backed implementation. Mirrors
    /// MyMoney.Data's IDataLayerUiCallback, which MyMoney.Business cannot
    /// reference directly (MyMoney.Data already references
    /// MyMoney.Business; the reverse would be circular).
    /// </summary>
    public interface IBusinessLayerUiCallback
    {
        void ShowError(string message, string title);
        bool Confirm(string message, string title);
        bool ConfirmOkCancel(string message, string title);

        /// <summary>
        /// Clears StockQuoteManager's output log pane and starts a new section with the
        /// given heading. Added in Task 5 alongside AppendErrorLog: StockQuoteManager's
        /// progress/error reporting goes through a concrete WPF OutputPane UserControl
        /// (Walkabout.Controls), not just simple message boxes, so those two touch points
        /// needed their own callback methods beyond ShowError/Confirm/ConfirmOkCancel.
        /// </summary>
        void ClearOutputLog(string heading);

        /// <summary>
        /// Appends an error report to StockQuoteManager's output log pane, optionally with a
        /// clickable link to a log file, and optionally brings the pane into view.
        /// </summary>
        void AppendErrorLog(string errorMessages, string logFilePath, bool activate);

        /// <summary>
        /// Added in Task 6: CsvTransactionImporter.EditCsvMap used to construct and show a WPF
        /// CsvImportDialog (a real Window) directly to let the user map .csv columns to known
        /// transaction fields - deeper coupling than the plan's research anticipated (it only
        /// flagged a single dialog-owner line). MyMoney.csproj's adapter shows the real dialog
        /// (pre-populated from either the raw headers or an existing map) and returns the
        /// resulting CsvMap, or null if the user cancels. CsvMap itself is a plain data class
        /// that lives in MyMoney.Business (Importers/CsvImporter.cs), not a WPF type.
        /// </summary>
        CsvMap PromptForCsvFieldMapping(string[] expectedColumns, IEnumerable<string> headers, CsvMap existingMap);

        /// <summary>
        /// Added in Task 6: Exporters.ExportPrompt used to show a WPF SaveFileDialog owned by
        /// the main window directly - also deeper coupling than the plan's research anticipated.
        /// Returns the chosen file path, or null if the user cancels.
        /// </summary>
        string PromptSaveFileName(string filter);

        /// <summary>
        /// Added in Task 6: Exporters.Export used to open the just-written file via the
        /// WPF-project-only, internal Utilities.InternetExplorer helper (Shell-executes it, or
        /// for .xml exports, first runs it through an XSLT transform before opening in edit
        /// mode) - another WPF/OS-shell touch point the plan's research didn't catch.
        /// </summary>
        void OpenExportedFile(string filePath, bool applyXsltTransform);

        /// <summary>
        /// Added in Task 6: XmlImporter.ImportObjects's "cut" (move-transaction-between-accounts)
        /// path used to resolve Walkabout.Attachments.AttachmentManager via an IServiceProvider
        /// (typeof(AttachmentManager)) and call MoveAttachments directly. AttachmentManager
        /// itself references System.Windows and isn't scheduled to move into MyMoney.Business by
        /// this plan, so this one touch point goes through the callback instead; XmlImporter's
        /// constructor now takes IBusinessLayerUiCallback in place of the IServiceProvider it
        /// used only for this single lookup.
        /// </summary>
        void MoveAttachments(Transaction original, Account newAccount);

        /// <summary>
        /// Added in Task 6: CsvImportController/CsvTransactionImporter used to call
        /// Walkabout.Dialogs.AccountHelper.PickAccount directly - another real WPF dialog
        /// (SelectAccountDialog, and possibly AccountDialog) construction the plan's research
        /// didn't catch, on top of the CsvImportDialog one it did flag. Returns null if the user
        /// cancels, matching AccountHelper.PickAccount's existing contract.
        /// </summary>
        Account PickAccount(MyMoney money, Account accountTemplate, string prompt);
    }
}
