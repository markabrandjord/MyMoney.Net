using Walkabout.Data;
using Walkabout.Importers;
using Walkabout.Views.Controls;

namespace Walkabout.Controls
{
    public class DownloadControlProgressReporter : IImportProgressReporter
    {
        private readonly DownloadControl control;

        public DownloadControlProgressReporter(DownloadControl control)
        {
            this.control = control;
        }

        public void SetEntries(ThreadSafeObservableCollection<DownloadData> entries)
        {
            this.control.DownloadEventTree.ItemsSource = entries;
        }

        public void SelectEntry(DownloadData entry)
        {
            this.control.SelectEntry(entry);
        }
    }
}
