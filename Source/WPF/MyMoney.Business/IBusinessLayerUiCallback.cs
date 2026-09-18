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
    }
}
