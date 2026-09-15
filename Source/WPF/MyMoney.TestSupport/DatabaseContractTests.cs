using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    /// <summary>
    /// Shared IDatabase contract tests: save/reload data integrity and the
    /// PersistentObject dirty-tracking lifecycle (Insert/Update/Delete),
    /// expressed purely in terms of IDatabase so the same test bodies run
    /// against every backing-store flavor. Assertions are scoped to
    /// post-reload state only - see this plan's Global Constraints and the
    /// design spec's "Ruling" section for why.
    /// </summary>
    public abstract class DatabaseContractTests
    {
        protected IDatabase Database { get; private set; }

        [SetUp]
        public virtual void SetUp()
        {
            this.Database = this.CreateDatabase();
            this.Database.Create();
        }

        [TearDown]
        public virtual void TearDown()
        {
            this.Database?.Disconnect();
        }

        protected abstract IDatabase CreateDatabase();

        private static MyMoney BuildSampleMoney()
        {
            MyMoney money = new MyMoney();
            Category category = money.Categories.GetOrCreateCategory("Auto:Gas", CategoryType.Expense);
            Payee payee = money.Payees.FindPayee("Shell", true);
            Account account = money.Accounts.AddAccount("Checking");
            Transaction t = money.Transactions.NewTransaction(account);
            t.Date = new DateTime(2026, 1, 15);
            t.Amount = -42.50m;
            t.Payee = payee;
            t.Category = category;
            t.Memo = "Fill-up";
            money.Transactions.AddTransaction(t);
            return money;
        }

        [Test]
        public void SaveAndReload_PreservesAccountAndTransactionData()
        {
            MyMoney money = BuildSampleMoney();

            this.Database.Save(money);
            MyMoney reloaded = this.Database.Load(null);

            Account account = reloaded.Accounts.FindAccount("Checking");
            Assert.That(account, Is.Not.Null);

            var transactions = reloaded.Transactions.GetTransactionsFrom(account);
            Assert.That(transactions.Count, Is.EqualTo(1));
            Transaction t = transactions[0];
            Assert.That(t.Amount, Is.EqualTo(-42.50m));
            Assert.That(t.Memo, Is.EqualTo("Fill-up"));
            Assert.That(t.Payee, Is.Not.Null);
            Assert.That(t.Payee.Name, Is.EqualTo("Shell"));
            Assert.That(t.Category, Is.Not.Null);
            Assert.That(t.Category.Name, Is.EqualTo("Auto:Gas"));
        }

        [Test]
        public void Insert_ThenReload_ItemAppearsWithCleanState()
        {
            MyMoney money = BuildSampleMoney();
            this.Database.Save(money);

            MyMoney reloaded = this.Database.Load(null);
            reloaded.Accounts.AddAccount("Savings");
            this.Database.Save(reloaded);

            MyMoney reloadedAgain = this.Database.Load(null);
            Account found = reloadedAgain.Accounts.FindAccount("Savings");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.IsInserted, Is.False);
            Assert.That(found.IsChanged, Is.False);
        }

        [Test]
        public void Update_ThenReload_ChangePersisted()
        {
            MyMoney money = BuildSampleMoney();
            this.Database.Save(money);

            MyMoney reloaded = this.Database.Load(null);
            Account account = reloaded.Accounts.FindAccount("Checking");
            account.Description = "Updated description";
            this.Database.Save(reloaded);

            MyMoney reloadedAgain = this.Database.Load(null);
            Account found = reloadedAgain.Accounts.FindAccount("Checking");
            Assert.That(found.Description, Is.EqualTo("Updated description"));
        }

        [Test]
        public void Delete_ThenReload_ItemIsGone()
        {
            MyMoney money = BuildSampleMoney();
            money.Categories.GetOrCreateCategory("ToDelete", CategoryType.Expense);
            this.Database.Save(money);

            MyMoney reloaded = this.Database.Load(null);
            Category toDelete = reloaded.Categories.FindCategory("ToDelete");
            Assert.That(toDelete, Is.Not.Null);
            reloaded.Categories.RemoveCategory(toDelete);
            this.Database.Save(reloaded);

            MyMoney reloadedAgain = this.Database.Load(null);
            Category shouldBeGone = reloadedAgain.Categories.FindCategory("ToDelete");
            Assert.That(shouldBeGone, Is.Null);
        }
    }
}
