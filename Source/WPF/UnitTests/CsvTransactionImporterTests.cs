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
    /// Covers CsvMap and CsvTransactionImporter (both defined in CsvImporter.cs), two of the
    /// four classes the final whole-branch review of the business-layer subsystem migration
    /// found had zero test coverage (Task 10). CsvTransactionImporter is constructed directly
    /// here rather than through CsvImportController, so its Import(CsvDocument)/Commit() API
    /// can be exercised with controlled CsvMap/CsvDocument test data - exactly the
    /// dialog-collects-data-then-calls-a-business-API pattern the rest of this migration's
    /// tests already use. CsvTransactionImporter, CsvMap and UserCanceledException are all
    /// `internal` to Walkabout.Importers, which is fine - UnitTests has InternalsVisibleTo
    /// access via MyMoney.Business's AssemblyInfo.cs.
    /// </summary>
    [TestFixture]
    public class CsvTransactionImporterTests
    {
        private class FakeBusinessLayerUiCallback : IBusinessLayerUiCallback
        {
            public CsvMap PromptForCsvFieldMappingResult { get; set; }

            public CsvMap PromptForCsvFieldMapping(string[] expectedColumns, IEnumerable<string> headers, CsvMap existingMap) => this.PromptForCsvFieldMappingResult;

            public void ShowError(string message, string title) { }
            public bool Confirm(string message, string title) => true;
            public bool ConfirmOkCancel(string message, string title) => true;
            public bool ConfirmWithDetails(string message, string title, string details) => true;
            public void ClearOutputLog(string heading) { }
            public void AppendErrorLog(string errorMessages, string logFilePath, bool activate) { }
            public string PromptSaveFileName(string filter) => null;
            public void OpenExportedFile(string filePath, bool applyXsltTransform) { }
            public void MoveAttachments(Transaction original, Account newAccount) { }
            public Account PickAccount(MyMoney money, Account accountTemplate, string prompt) => null;
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

        private static CsvDocument CreateCsv(params string[][] rows)
        {
            var csv = new CsvDocument();
            csv.Headers.AddRange(new[] { "Date", "Payee", "Memo", "Amount", "FITID" });
            foreach (var row in rows)
            {
                csv.Rows.Add(new List<string>(row));
            }
            return csv;
        }

        #region CsvMap

        [Test]
        public void Load_FileDoesNotExist_ReturnsEmptyMapWithFileNameSet()
        {
            string path = Path.Combine(Path.GetTempPath(), "CsvMapTests_" + Guid.NewGuid().ToString("N") + ".xml");

            CsvMap map = CsvMap.Load(path);

            Assert.IsNotNull(map);
            Assert.AreEqual(path, map.FileName);
            Assert.IsNull(map.Fields);
        }

        [Test]
        public void Save_Then_Load_RoundTripsFields()
        {
            string path = Path.Combine(Path.GetTempPath(), "CsvMapTests_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var map = CreateBankFieldMap();
                map.FileName = path;
                map.Negate = true;

                map.Save();
                CsvMap reloaded = CsvMap.Load(path);

                Assert.IsTrue(reloaded.Negate);
                Assert.IsNotNull(reloaded.Fields);
                Assert.AreEqual(5, reloaded.Fields.Count);
                Assert.AreEqual("Amount", reloaded.Fields[3].Header);
                Assert.AreEqual("Amount", reloaded.Fields[3].Field);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        #endregion

        #region CsvTransactionImporter

        [Test]
        public void Import_ThenCommit_ValidRow_CreatesTransactionWithMappedFields()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            account.Type = AccountType.Checking;
            var data = new DownloadData(null, account);
            var cache = new StockQuoteCache(money, new DownloadLog());
            var importer = new CsvTransactionImporter(
                money, account, CreateBankFieldMap(), data, CsvTransactionImporter.BankAccountFields, cache, new FakeBusinessLayerUiCallback());
            var csv = CreateCsv(new[] { "01/15/2024", "ACME Corp", "Test memo", "-50.00", "FIT1" });

            int count = importer.Import(csv);
            Assert.AreEqual(1, count);

            importer.Commit().GetAwaiter().GetResult();

            Assert.AreEqual(1, money.Transactions.Count);
            Transaction t = money.Transactions.GetTransactionsFrom(account)[0];
            Assert.AreEqual(new DateTime(2024, 1, 15), t.Date);
            Assert.AreEqual("ACME Corp", t.Payee.Name);
            Assert.AreEqual("Test memo", t.Memo);
            Assert.AreEqual(-50.00m, t.Amount);
            Assert.AreEqual("FIT1", t.FITID);
            Assert.AreEqual(TransactionStatus.Electronic, t.Status);
            Assert.AreEqual(TransactionFlags.Unaccepted, t.Flags);
            Assert.IsTrue(t.IsDownloaded);
            Assert.AreEqual(1, data.Added.Count);
        }

        [Test]
        public void Import_ThenCommit_RowWithUnparseableDate_IsSkippedByCommit()
        {
            // Genuine malformed CSV content: ImportRow silently drops any row whose Date field
            // fails to parse (real behavior read from CsvImporter.cs's ImportRow, not assumed -
            // it returns early when t.Date == DateTime.MinValue). Import(csv) itself still
            // returns the *total* row count regardless of how many rows were actually usable -
            // only Commit()'s effect on the money graph reveals the skip.
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var data = new DownloadData(null, account);
            var cache = new StockQuoteCache(money, new DownloadLog());
            var importer = new CsvTransactionImporter(
                money, account, CreateBankFieldMap(), data, CsvTransactionImporter.BankAccountFields, cache, new FakeBusinessLayerUiCallback());
            var csv = CreateCsv(
                new[] { "01/15/2024", "ACME Corp", "Good row", "-50.00", "FIT1" },
                new[] { "not a date", "Bad Payee", "Bad row", "-10.00", "FIT2" });

            int count = importer.Import(csv);
            Assert.AreEqual(2, count, "Import reports total CSV rows, not rows successfully parsed");

            importer.Commit().GetAwaiter().GetResult();

            Assert.AreEqual(1, money.Transactions.Count, "the unparseable-date row must not produce a transaction");
        }

        [Test]
        public void Import_HeadersDoNotMatchMapAndUserCancelsMapping_ThrowsUserCanceledException()
        {
            // Genuine invalid input at this layer: the CsvMap's Fields don't match the incoming
            // CSV headers, so Import falls into EditCsvMap, which prompts via
            // IBusinessLayerUiCallback.PromptForCsvFieldMapping. This exercises the code AROUND
            // that call (per the task brief, testing the dialog itself is out of scope): a fake
            // that returns null - exactly what happens when a user cancels the real dialog -
            // makes EditCsvMap throw UserCanceledException, which is real, observable behavior.
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var data = new DownloadData(null, account);
            var cache = new StockQuoteCache(money, new DownloadLog());
            var mismatchedMap = new CsvMap
            {
                Fields = new List<CsvFieldMap> { new CsvFieldMap { Header = "SomethingElse", Field = "Date" } }
            };
            var callback = new FakeBusinessLayerUiCallback { PromptForCsvFieldMappingResult = null };
            var importer = new CsvTransactionImporter(
                money, account, mismatchedMap, data, CsvTransactionImporter.BankAccountFields, cache, callback);
            var csv = CreateCsv(new[] { "01/15/2024", "ACME Corp", "Test memo", "-50.00", "FIT1" });

            Assert.Throws<UserCanceledException>(() => importer.Import(csv));
        }

        [Test]
        public void Commit_NoRowsImported_DoesNotThrowOrTouchMoney()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var data = new DownloadData(null, account);
            var cache = new StockQuoteCache(money, new DownloadLog());
            var importer = new CsvTransactionImporter(
                money, account, CreateBankFieldMap(), data, CsvTransactionImporter.BankAccountFields, cache, new FakeBusinessLayerUiCallback());

            Assert.DoesNotThrowAsync(async () => await importer.Commit());
            Assert.AreEqual(0, money.Transactions.Count);
        }

        #endregion
    }
}
