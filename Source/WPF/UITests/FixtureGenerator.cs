using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UITests
{
    /// <summary>
    /// Regenerates Fixtures/PayeeSmokeTest.mmdb. Marked [Explicit] so it
    /// never runs as part of a normal test pass -- the fixture is a
    /// checked-in binary, not something rebuilt on every run. Re-run this
    /// deliberately (e.g. `dotnet test --filter FixtureGenerator`) only
    /// when the fixture's shape needs to change.
    /// </summary>
    [Explicit("Regenerates the checked-in fixture file; not part of normal test runs.")]
    public class FixtureGenerator
    {
        [Test]
        public void RegeneratePayeeSmokeTestFixture()
        {
            string outputPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Fixtures", "PayeeSmokeTest.mmdb");
            outputPath = Path.GetFullPath(outputPath);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var money = new MyMoney();

            var payee = money.Payees.AddPayee(1);
            payee.Name = "Payee Smoke Test";

            var account = new Account();
            account.Name = "Smoke Test Checking";
            money.Accounts.AddAccount(account);

            var transaction = new Transaction();
            transaction.Account = account;
            transaction.Date = new DateTime(2026, 1, 1);
            transaction.Amount = -10.00M;
            transaction.Payee = payee;
            money.Transactions.AddTransaction(transaction);

            var db = new SqliteDatabase();
            db.DatabasePath = outputPath;
            db.Create();
            db.Save(money);

            Assert.That(File.Exists(outputPath), Is.True);
            TestContext.Out.WriteLine($"Wrote fixture to {outputPath}");
        }
    }
}
