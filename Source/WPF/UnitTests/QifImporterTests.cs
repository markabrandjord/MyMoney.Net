using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Importers;
using Walkabout.Utilities;

namespace Walkabout.Tests
{
    [TestFixture]
    public class QifImporterTests
    {
        private class FakeImportProgressReporter : IImportProgressReporter
        {
            public ThreadSafeObservableCollection<DownloadData> Entries { get; private set; }
            public DownloadData Selected { get; private set; }
            public void SetEntries(ThreadSafeObservableCollection<DownloadData> entries) => this.Entries = entries;
            public void SelectEntry(DownloadData entry) => this.Selected = entry;
        }

        // Implements the full IBusinessLayerUiCallback surface as of Task 6 (10 methods: the
        // original 3 from Task 4, ClearOutputLog/AppendErrorLog from Task 5, and
        // PromptForCsvFieldMapping/PromptSaveFileName/OpenExportedFile/MoveAttachments/
        // PickAccount added in this task). Only ShowError/Confirm are exercised by
        // QifImporter, but a fake implementing an interface has to implement the whole thing.
        private class FakeBusinessLayerUiCallback : IBusinessLayerUiCallback
        {
            public string LastErrorMessage { get; private set; }
            public string LastErrorTitle { get; private set; }

            public void ShowError(string message, string title)
            {
                this.LastErrorMessage = message;
                this.LastErrorTitle = title;
            }

            public bool Confirm(string message, string title) => true;
            public bool ConfirmOkCancel(string message, string title) => true;
            public void ClearOutputLog(string heading) { }
            public void AppendErrorLog(string errorMessages, string logFilePath, bool activate) { }
            public CsvMap PromptForCsvFieldMapping(string[] expectedColumns, IEnumerable<string> headers, CsvMap existingMap) => null;
            public string PromptSaveFileName(string filter) => null;
            public void OpenExportedFile(string filePath, bool applyXsltTransform) { }
            public void MoveAttachments(Transaction original, Account newAccount) { }
            public Account PickAccount(MyMoney money, Account accountTemplate, string prompt) => null;
        }

        [Test]
        public void Import_NoAccountSelected_ReportsErrorViaCallback()
        {
            var reporter = new FakeImportProgressReporter();
            var callback = new FakeBusinessLayerUiCallback();
            var importer = new QifImporter(reporter, callback, new MyMoney());

            // QifImporter.SpecialImportFileName's basename ("~IMPORT~") never matches a real
            // account, and equals Path.GetFileNameWithoutExtension(SpecialImportFileName), so
            // the "create a new account?" Confirm prompt is skipped entirely and Import falls
            // straight into the "no account selected" branch when currentlySelectedAccount
            // is null - which reports via ShowError and returns null without ever touching the
            // file on disk.
            Account result = importer.Import(null, QifImporter.SpecialImportFileName, out int count);

            Assert.IsNull(result);
            Assert.AreEqual(0, count);
            Assert.IsFalse(string.IsNullOrEmpty(callback.LastErrorMessage));
        }
    }
}
