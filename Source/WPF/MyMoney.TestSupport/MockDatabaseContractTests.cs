using System;
using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    [TestFixture]
    public class MockDatabaseContractTests : DatabaseContractTests
    {
        protected override IDatabase CreateDatabase()
        {
            return new MockDatabase();
        }

        private static MyMoney BuildOneAccountMoney(out Account account)
        {
            MyMoney money = new MyMoney();
            account = money.Accounts.AddAccount("Checking");
            return money;
        }

        [Test]
        public void SaveOne_NewAccount_PersistsAndSetsRowVersionToOne()
        {
            MyMoney money = BuildOneAccountMoney(out Account account);

            this.Database.SaveOne(account);

            Assert.That(account.RowVersion, Is.EqualTo(1));
            Assert.That(account.IsInserted, Is.False);
            Assert.That(account.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Account found = reloaded.Accounts.FindAccount("Checking");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateAfterReload_IncrementsRowVersion()
        {
            MyMoney money = BuildOneAccountMoney(out Account account);
            this.Database.SaveOne(account);

            MyMoney reloaded = this.Database.Load(null);
            Account found = reloaded.Accounts.FindAccount("Checking");
            found.Description = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Account foundAgain = reloadedAgain.Accounts.FindAccount("Checking");
            Assert.That(foundAgain.Description, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleRowVersion_ThrowsConcurrencyConflictException()
        {
            MyMoney money = BuildOneAccountMoney(out Account account);
            this.Database.SaveOne(account);

            // Two independent readers both load the same committed row.
            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Account accountA = readerA.Accounts.FindAccount("Checking");
            accountA.Description = "From A";
            this.Database.SaveOne(accountA);

            Account accountB = readerB.Accounts.FindAccount("Checking");
            accountB.Description = "From B";
            Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(accountB));

            // A's write must still be the one that stuck.
            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Accounts.FindAccount("Checking").Description, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveTransfer_CommitsBothTransactionsTogether()
        {
            MyMoney money = new MyMoney();
            Account checking = money.Accounts.AddAccount("Checking");
            Account savings = money.Accounts.AddAccount("Savings");
            Transaction from = money.Transactions.NewTransaction(checking);
            from.Amount = -100m;
            money.Transactions.AddTransaction(from);
            Transaction to = money.Transactions.NewTransaction(savings);
            to.Amount = 100m;
            money.Transactions.AddTransaction(to);

            this.Database.SaveTransfer(from, to);

            Assert.That(from.RowVersion, Is.EqualTo(1));
            Assert.That(to.RowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Transactions.GetTransactionsFrom(reloaded.Accounts.FindAccount("Checking")).Count, Is.EqualTo(1));
            Assert.That(reloaded.Transactions.GetTransactionsFrom(reloaded.Accounts.FindAccount("Savings")).Count, Is.EqualTo(1));
        }

        [Test]
        public void SaveBatch_OneStaleRootAmongMany_CommitsNoneOfThem()
        {
            MyMoney money = new MyMoney();
            Account a = money.Accounts.AddAccount("A");
            Account b = money.Accounts.AddAccount("B");
            this.Database.SaveBatch(new PersistentObject[] { a, b });

            MyMoney reader = this.Database.Load(null);
            Account staleA = reader.Accounts.FindAccount("A");
            Account staleB = reader.Accounts.FindAccount("B");

            // Someone else updates A first, so staleA's RowVersion (1) is now behind.
            MyMoney otherWriter = this.Database.Load(null);
            Account freshA = otherWriter.Accounts.FindAccount("A");
            freshA.Description = "Changed elsewhere";
            this.Database.SaveOne(freshA);

            staleA.Description = "Attempted A";
            staleB.Description = "Attempted B";
            Assert.Throws<ConcurrencyConflictException>(
                () => this.Database.SaveBatch(new PersistentObject[] { staleA, staleB }));

            MyMoney reloaded = this.Database.Load(null);
            // B must NOT have been committed even though only A conflicted.
            Assert.That(reloaded.Accounts.FindAccount("B").Description, Is.Not.EqualTo("Attempted B"));
        }
    }
}
