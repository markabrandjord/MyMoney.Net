using Walkabout.Data;

namespace Walkabout.Importers
{
    /// <summary>
    /// QifImporter and CsvImportController used to hold a direct reference
    /// to the WPF DownloadControl (a UserControl) and manipulate its
    /// DownloadEventTree.ItemsSource/SelectEntry directly. This interface
    /// replaces that direct reference so both importers can live in
    /// MyMoney.Business. MyMoney.csproj supplies a thin adapter forwarding
    /// to the real DownloadControl.
    /// </summary>
    public interface IImportProgressReporter
    {
        void SetEntries(ThreadSafeObservableCollection<DownloadData> entries);
        void SelectEntry(DownloadData entry);
    }
}
