using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Importers;

namespace Walkabout.Tests
{
    /// <summary>
    /// Covers Exporters, one of the four classes the final whole-branch review of the
    /// business-layer subsystem migration found had zero test coverage (Task 10). Exporters
    /// only touches WPF through IBusinessLayerUiCallback (PromptSaveFileName/OpenExportedFile/
    /// ShowError), which is faked here exactly like the rest of this migration's tests.
    /// </summary>
    [TestFixture]
    public class ExportersTests
    {
        private class FakeBusinessLayerUiCallback : IBusinessLayerUiCallback
        {
            public string PromptSaveFileNameResult { get; set; }
            public string LastPromptSaveFileNameFilter { get; private set; }
            public string LastErrorMessage { get; private set; }
            public string LastErrorTitle { get; private set; }
            public bool OpenExportedFileCalled { get; private set; }
            public string LastOpenedFilePath { get; private set; }
            public bool LastOpenedApplyXslt { get; private set; }

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
            public CsvMap PromptForCsvFieldMapping(string[] expectedColumns, IEnumerable<string> headers, CsvMap existingMap) => null;

            public string PromptSaveFileName(string filter)
            {
                this.LastPromptSaveFileNameFilter = filter;
                return this.PromptSaveFileNameResult;
            }

            public void OpenExportedFile(string filePath, bool applyXsltTransform)
            {
                this.OpenExportedFileCalled = true;
                this.LastOpenedFilePath = filePath;
                this.LastOpenedApplyXslt = applyXsltTransform;
            }

            public void MoveAttachments(Transaction original, Account newAccount) { }
            public Account PickAccount(MyMoney money, Account accountTemplate, string prompt) => null;
        }

        private static Transaction CreateSampleTransaction(MyMoney money, Account account)
        {
            var payee = money.Payees.FindPayee("ACME Corp", true);
            var t = money.Transactions.NewTransaction(account);
            t.Date = new System.DateTime(2024, 1, 15);
            t.Payee = payee;
            t.Amount = 42.50m;
            t.Memo = "Sample";
            return t;
        }

        #region ExportString (internal)

        [Test]
        public void ExportString_WithTransactions_IncludesActionAttributeAndTransactionData()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var t = CreateSampleTransaction(money, account);
            var exporters = new Exporters(new FakeBusinessLayerUiCallback());

            string xml = exporters.ExportString("Delete", new object[] { t });

            Assert.That(xml, Does.Contain("action=\"Delete\""));
            Assert.That(xml, Does.Contain("Transaction"));
            Assert.That(xml, Does.Contain("Checking"));
        }

        [Test]
        public void ExportString_NoData_ProducesRootWithNoTransactions()
        {
            var exporters = new Exporters(new FakeBusinessLayerUiCallback());

            string xml = exporters.ExportString("Delete", new object[0]);

            Assert.That(xml, Does.Contain("<Accounts"));
            Assert.That(xml, Does.Not.Contain("<Transaction"));
        }

        #endregion

        #region Export (public, real file I/O)

        [Test]
        public void Export_CsvExtension_WritesCsvFileAndOpensIt()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var t = CreateSampleTransaction(money, account);
            var callback = new FakeBusinessLayerUiCallback();
            var exporters = new Exporters(callback);
            string path = Path.Combine(Path.GetTempPath(), "ExportersTests_" + System.Guid.NewGuid().ToString("N") + ".csv");

            try
            {
                exporters.Export(path, new object[] { t });

                Assert.IsTrue(File.Exists(path));
                string content = File.ReadAllText(path);
                Assert.That(content, Does.Contain("ACME Corp"));
                Assert.IsTrue(callback.OpenExportedFileCalled);
                Assert.AreEqual(path, callback.LastOpenedFilePath);
                Assert.IsFalse(callback.LastOpenedApplyXslt);
                Assert.IsNull(callback.LastErrorMessage, "a successful export must not report an error");
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void Export_XmlExtension_WritesXmlFileAndOpensItWithXsltTransform()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var t = CreateSampleTransaction(money, account);
            var callback = new FakeBusinessLayerUiCallback();
            var exporters = new Exporters(callback);
            string path = Path.Combine(Path.GetTempPath(), "ExportersTests_" + System.Guid.NewGuid().ToString("N") + ".xml");

            try
            {
                exporters.Export(path, new object[] { t });

                Assert.IsTrue(File.Exists(path));
                string content = File.ReadAllText(path);
                Assert.That(content, Does.Contain("<root>"));
                Assert.IsTrue(callback.OpenExportedFileCalled);
                Assert.IsTrue(callback.LastOpenedApplyXslt, "xml export should request the Xslt transform");
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void Export_EmptyData_StillCreatesFileWithoutError()
        {
            var callback = new FakeBusinessLayerUiCallback();
            var exporters = new Exporters(callback);
            string path = Path.Combine(Path.GetTempPath(), "ExportersTests_" + System.Guid.NewGuid().ToString("N") + ".csv");

            try
            {
                exporters.Export(path, new object[0]);

                Assert.IsTrue(File.Exists(path));
                // Export opens the StreamWriter with Encoding.UTF8, whose 3-byte BOM preamble is
                // always written even when no other content is - so the file isn't literally
                // 0 bytes, but its text content is empty since no rows means CsvTransactionFormat
                // never writes a header.
                Assert.AreEqual(string.Empty, File.ReadAllText(path));
                Assert.IsNull(callback.LastErrorMessage);
                Assert.IsTrue(callback.OpenExportedFileCalled);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void Export_UnsupportedExtension_ReportsErrorViaCallbackInsteadOfThrowing()
        {
            // Genuine invalid input: Export's own code throws "Expecting either .xml or .csv
            // file extension" internally, but that throw is caught inside Export itself and
            // reported through the UI callback rather than propagating - verified here rather
            // than assumed.
            var callback = new FakeBusinessLayerUiCallback();
            var exporters = new Exporters(callback);
            string path = Path.Combine(Path.GetTempPath(), "ExportersTests_" + System.Guid.NewGuid().ToString("N") + ".txt");

            Assert.DoesNotThrow(() => exporters.Export(path, new object[0]));

            Assert.IsFalse(File.Exists(path));
            Assert.That(callback.LastErrorMessage, Does.Contain("Expecting either .xml or .csv file extension"));
            Assert.AreEqual("Export Error", callback.LastErrorTitle);
            Assert.IsFalse(callback.OpenExportedFileCalled);
        }

        #endregion

        #region ExportPrompt

        [Test]
        public void ExportPrompt_UserCancelsFileDialog_DoesNotExportAnything()
        {
            // FakeBusinessLayerUiCallback.PromptSaveFileNameResult defaults to null, which is
            // exactly what a real "user clicked Cancel" dialog result would be.
            var callback = new FakeBusinessLayerUiCallback();
            var exporters = new Exporters(callback);

            exporters.ExportPrompt(new object[0]);

            Assert.IsFalse(callback.OpenExportedFileCalled);
            Assert.IsNull(callback.LastErrorMessage);
        }

        [Test]
        public void ExportPrompt_UserPicksFileName_ExportsToThatFile()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var t = CreateSampleTransaction(money, account);
            string path = Path.Combine(Path.GetTempPath(), "ExportersTests_" + System.Guid.NewGuid().ToString("N") + ".csv");
            var callback = new FakeBusinessLayerUiCallback { PromptSaveFileNameResult = path };
            var exporters = new Exporters(callback);

            try
            {
                exporters.ExportPrompt(new object[] { t });

                Assert.IsTrue(File.Exists(path));
                Assert.IsTrue(callback.OpenExportedFileCalled);
                Assert.That(callback.LastPromptSaveFileNameFilter, Does.Contain("csv").IgnoreCase);
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
    }
}
