using System.Threading.Tasks;

namespace Walkabout.StockQuotes
{
    /// <summary>
    /// StockQuoteCache needs exactly two members from the app-level DownloadLog
    /// class (StockQuotes/StockQuoteManager.cs, which stays in MyMoney.csproj -
    /// the StockQuote fetch/throttle machinery is out of scope for this
    /// extraction per the plan's Non-Goals). This interface lets StockQuoteCache
    /// depend on the shape it needs instead of the concrete DownloadLog type,
    /// the same pattern used for DatabaseSettings.MigrateSettings /
    /// ISettingsMigrationSource. DownloadLog itself can't implement this yet -
    /// MyMoney.csproj (where it lives) can't reference
    /// Walkabout.StockQuotes.IStockDownloadLog until its ProjectReference to
    /// MyMoney.Business exists (Task 4).
    /// </summary>
    public interface IStockDownloadLog
    {
        string Folder { get; }

        Task<StockQuoteHistory> GetHistory(string symbol);
    }
}
