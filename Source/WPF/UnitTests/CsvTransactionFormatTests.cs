using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    // Added post-review (Task 6 fix round 1): CsvTransactionFormat is the byte-identity-critical
    // piece extracted out of MyMoney.Data's CsvStore so both CsvStore.Save and
    // Exporters.ExportToCsv can call it - nothing pinned its actual output before this, so a
    // future refactor could silently drift it for both callers at once.
    [TestFixture]
    public class CsvTransactionFormatTests
    {
        [Test]
        public void WriteTransaction_EscapesEmbeddedQuotesAndHonorsOptionalColumns()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var payee = money.Payees.FindPayee("Bob \"The Builder\" Ltd", true);
            var category = money.Categories.GetOrCreateCategory("Groceries", CategoryType.Expense);
            var t = money.Transactions.NewTransaction(account);
            t.Date = new DateTime(2024, 1, 15);
            t.Payee = payee;
            t.Category = category;
            t.Amount = 12.34m;
            t.Memo = "Contains \"quotes\"";
            t.SalesTax = 1.11m;

            string csv;
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true))
                {
                    CsvTransactionFormat.WriteTransactionHeader(writer, CsvTransactionFormat.OptionalColumnFlags.SalesTax);
                    CsvTransactionFormat.WriteTransaction(writer, t, CsvTransactionFormat.OptionalColumnFlags.SalesTax);
                }
                stream.Position = 0;
                csv = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
            }

            var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.AreEqual(2, lines.Length, "Expected exactly a header line and one data line");

            // Pins the exact column set/order for the SalesTax optional-column combination -
            // this is the one place output shape (not just per-field escaping) matters.
            Assert.AreEqual("\"Account\",\"Date\",\"Payee\",\"Category\",\"Amount\",\"SalesTax\",\"Memo\"", lines[0]);

            string row = lines[1];
            // CsvSafeString doubles embedded double-quotes rather than stripping them.
            Assert.That(row, Does.Contain("Bob \"\"The Builder\"\" Ltd"));
            Assert.That(row, Does.Contain("Contains \"\"quotes\"\""));
            Assert.That(row, Does.Contain("\"Checking\""));
            Assert.That(row, Does.Contain("\"Groceries\""));
        }

        [Test]
        public void CsvSafeString_NullOrEmpty_ReturnsEmptyString()
        {
            Assert.AreEqual("", CsvTransactionFormat.CsvSafeString(null));
            Assert.AreEqual("", CsvTransactionFormat.CsvSafeString(""));
        }
    }
}
