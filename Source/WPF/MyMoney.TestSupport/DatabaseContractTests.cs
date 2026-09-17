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
    public abstract partial class DatabaseContractTests
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

        [Test]
        public void Load_BeforeAnySave_ReturnsEmptyGraph()
        {
            MyMoney money = this.Database.Load(null);

            Assert.That(money, Is.Not.Null);
            Assert.That(money.Accounts.Count, Is.EqualTo(0));
        }

        [Test]
        public void Create_ThenCheckExists_ReturnsTrue()
        {
            Assert.That(this.Database.Exists, Is.True);
        }

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

        // Regression test for the final whole-branch review's Fix 1 and Fix 2:
        // - Fix 1: Save(MyMoney)'s call order used to write Accounts before Categories and
        //   Transactions (which own Investment) before Securities, violating the FK dependency
        //   order Load() already required ("Must populate the Categories before the Account").
        // - Fix 2: Investment.Security had no AllowNulls on its ColumnObjectMapping and its write
        //   sites wrote a -1 sentinel instead of NULL, which is incompatible with a real FK
        //   constraint pointing at Securities(Id).
        // This creates a new Category, an Account whose CategoryForPrincipal points at it, a new
        // Security, and a new Transaction/Investment whose Security points at it, all in one
        // fresh MyMoney saved once, and asserts the reload succeeds and every relationship
        // resolves. Before Fixes 1/2 this either throws an FK-violation on Save (real SQL
        // engines) or silently loses the CategoryForPrincipal/Investment.Security links.
        [Test]
        public void SaveAndReload_ResolvesAccountCategoryAndInvestmentSecurityForwardReferences()
        {
            MyMoney money = new MyMoney();
            Category principalCategory = money.Categories.GetOrCreateCategory("Loan:Principal", CategoryType.Expense);
            Account account = money.Accounts.AddAccount("Mortgage");
            account.CategoryForPrincipal = principalCategory;

            Security security = money.Securities.FindSecurity("ACME", true);
            security.Symbol = "ACME";

            Transaction t = money.Transactions.NewTransaction(account);
            t.Date = new DateTime(2026, 1, 15);
            t.Amount = -1000m;
            t.GetOrCreateInvestment().Security = security;
            money.Transactions.AddTransaction(t);

            this.Database.Save(money);
            MyMoney reloaded = this.Database.Load(null);

            Account reloadedAccount = reloaded.Accounts.FindAccount("Mortgage");
            Assert.That(reloadedAccount, Is.Not.Null);
            Assert.That(reloadedAccount.CategoryForPrincipal, Is.Not.Null);
            Assert.That(reloadedAccount.CategoryForPrincipal.Name, Is.EqualTo("Loan:Principal"));

            var transactions = reloaded.Transactions.GetTransactionsFrom(reloadedAccount);
            Assert.That(transactions.Count, Is.EqualTo(1));
            Transaction reloadedTransaction = transactions[0];
            Assert.That(reloadedTransaction.Investment, Is.Not.Null);
            Assert.That(reloadedTransaction.Investment.Security, Is.Not.Null);
            Assert.That(reloadedTransaction.Investment.Security.Name, Is.EqualTo("ACME"));
        }
    }
}
