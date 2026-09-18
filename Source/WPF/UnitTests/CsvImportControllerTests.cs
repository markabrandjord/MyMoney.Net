using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Importers;
using Walkabout.StockQuotes;
using Walkabout.Utilities;

namespace Walkabout.Tests
{
    /// <summary>
    /// Covers CsvImportController.ImportCsv, one of the four classes the final whole-branch
    /// review of the business-layer subsystem migration found had zero test coverage (Task 10).
    /// CsvImportController is `internal`, which is fine - UnitTests has InternalsVisibleTo
    /// access via MyMoney.Business's AssemblyInfo.cs, exactly like CsvTransactionImporter in
    /// CsvTransactionImporterTests.cs.
    /// </summary>
    [TestFixture]
    public class CsvImportControllerTests
    {
        private class FakeImportProgressReporter : IImportProgressReporter
        {
            public ThreadSafeObservableCollection<DownloadData> Entries { get; private set; }
            public DownloadData Selected { get; private set; }
            public void SetEntries(ThreadSafeObservableCollection<DownloadData> entries) => this.Entries = entries;
            public void SelectEntry(DownloadData entry) => this.Selected = entry;
        }

        private class FakeBusinessLayerUiCallback : IBusinessLayerUiCallback
        {
            public Account PickAccountResult { get; set; }
            public CsvMap PromptForCsvFieldMappingResult { get; set; }
            public string LastErrorMessage { get; private set; }
            public string LastErrorTitle { get; private set; }

            public Account PickAccount(MyMoney money, Account accountTemplate, string prompt) => this.PickAccountResult;
            public CsvMap PromptForCsvFieldMapping(string[] expectedColumns, IEnumerable<string> headers, CsvMap existingMap) => this.PromptForCsvFieldMappingResult;

            public void ShowError(string message, string title)
            {
                this.LastErrorMessage = message;
                this.LastErrorTitle = title;
            }

            public bool Confirm(string message, string title) => true;
            public bool ConfirmOkCancel(string message, string title) => true;
            public bool ConfirmWithDetails(string message, string title, string details) => true;
            public void ClearOutputLog(string heading) { }
            public void AppendErrorLog(string errorMessages, string logFilePath, bool activate) { }
            public string PromptSaveFileName(string filter) => null;
            public void OpenExportedFile(string filePath, bool applyXsltTransform) { }
            public void MoveAttachments(Transaction original, Account newAccount) { }
        }

        private static CsvMap CreateBankFieldMap()
        {
            return new CsvMap
            {
                Fields = new List<CsvFieldMap>
                {
                    new CsvFieldMap { Header = "Date", Field = "Date" },
                    new CsvFieldMap { Header = "Payee", Field = "Payee" },
                    new CsvFieldMap { Header = "Memo", Field = "Memo" },
                    new CsvFieldMap { Header = "Amount", Field = "Amount" },
                    new CsvFieldMap { Header = "FITID", Field = "FITID" },
                }
            };
        }

        [Test]
        public void ImportCsv_ValidFileNoAccountNumberColumn_PromptsForAccountAndImportsOneTransaction()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            account.Type = AccountType.Checking;
            var reporter = new FakeImportProgressReporter();
            var callback = new FakeBusinessLayerUiCallback
            {
                PickAccountResult = account,
                PromptForCsvFieldMappingResult = CreateBankFieldMap(),
            };
            var cache = new StockQuoteCache(money, new DownloadLog());
            string dbDir = Path.Combine(Path.GetTempPath(), "CsvImportControllerTests_" + Guid.NewGuid().ToString("N"));
            string csvFile = Path.Combine(Path.GetTempPath(), "CsvImportControllerTests_" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                File.WriteAllText(csvFile, "Date,Payee,Memo,Amount,FITID\r\n01/15/2024,ACME Corp,Test memo,-50.00,FIT1\r\n");
                var controller = new CsvImportController(reporter, callback, money, dbDir, cache);

                int total = controller.ImportCsv(csvFile).GetAwaiter().GetResult();

                Assert.AreEqual(1, total);
                Assert.AreEqual(1, money.Transactions.Count);
                Assert.IsNotNull(reporter.Entries);
                Assert.AreEqual(1, reporter.Entries.Count);
                // Real behavior verified by reading ImportCsv: DownloadData.Success is only ever
                // set true on the "Account Number" grouped-import branch - the plain
                // single-account path exercised here never sets it, so it stays at its default
                // (false) even on a fully successful import. Documented, not assumed.
                Assert.IsFalse(reporter.Entries[0].Success);
                Assert.That(reporter.Entries[0].Message, Does.Contain("Downloaded 1 new transactions"));
                Assert.AreSame(reporter.Entries[0], reporter.Selected);
                Assert.IsNull(callback.LastErrorMessage);
            }
            finally
            {
                if (File.Exists(csvFile))
                {
                    File.Delete(csvFile);
                }
                if (Directory.Exists(dbDir))
                {
                    Directory.Delete(dbDir, true);
                }
            }
        }

        [Test]
        public void ImportCsv_FileDoesNotExist_ReportsErrorAndReturnsZero()
        {
            var money = new MyMoney();
            var reporter = new FakeImportProgressReporter();
            var callback = new FakeBusinessLayerUiCallback();
            var cache = new StockQuoteCache(money, new DownloadLog());
            var controller = new CsvImportController(reporter, callback, money, null, cache);
            string missingFile = Path.Combine(Path.GetTempPath(), "CsvImportControllerTests_" + Guid.NewGuid().ToString("N") + ".csv");

            int total = controller.ImportCsv(missingFile).GetAwaiter().GetResult();

            Assert.AreEqual(0, total);
            Assert.IsFalse(string.IsNullOrEmpty(callback.LastErrorMessage));
            Assert.AreEqual("Import Error", callback.LastErrorTitle);
            Assert.IsNotNull(reporter.Entries, "SetEntries runs before the file is opened");
            Assert.AreEqual(0, reporter.Entries.Count);
        }

        [Test]
        public void ImportCsv_FileHasHeaderButNoDataRows_ReportsErrorAndReturnsZero()
        {
            // Genuine malformed .csv content: CsvDocument.Read throws ".csv file is empty" when
            // there are headers but zero data rows (verified in CsvDocument.cs, not assumed).
            // CsvImportController's outer catch(Exception) reports it via ShowError instead of
            // letting it propagate.
            var money = new MyMoney();
            var reporter = new FakeImportProgressReporter();
            var callback = new FakeBusinessLayerUiCallback();
            var cache = new StockQuoteCache(money, new DownloadLog());
            var controller = new CsvImportController(reporter, callback, money, null, cache);
            string csvFile = Path.Combine(Path.GetTempPath(), "CsvImportControllerTests_" + Guid.NewGuid().ToString("N") + ".csv");

            try
            {
                File.WriteAllText(csvFile, "Date,Payee,Memo,Amount,FITID\r\n");

                int total = controller.ImportCsv(csvFile).GetAwaiter().GetResult();

                Assert.AreEqual(0, total);
                Assert.That(callback.LastErrorMessage, Does.Contain("empty"));
            }
            finally
            {
                if (File.Exists(csvFile))
                {
                    File.Delete(csvFile);
                }
            }
        }
    }
}
