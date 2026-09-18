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
    }
}
