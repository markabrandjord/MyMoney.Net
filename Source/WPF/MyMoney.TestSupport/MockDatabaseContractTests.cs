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

        [Test]
        public void SaveOne_TransactionWithSplits_CommitsSplitsAsOneUnit()
        {
            // R2's central promise: SaveOne<Transaction> must commit its owned Splits as one
            // unit with the transaction itself.
            MyMoney money = new MyMoney();
            Account account = money.Accounts.AddAccount("Checking");
            Category groceries = money.Categories.GetOrCreateCategory("Groceries", CategoryType.Expense);
            Category gas = money.Categories.GetOrCreateCategory("Gas", CategoryType.Expense);

            Transaction transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2026, 1, 15);
            transaction.Amount = -75.00m;
            money.Transactions.AddTransaction(transaction);
            transaction.Splits = new Splits(transaction, transaction);
            Split first = transaction.Splits.AddSplit(0);
            first.Amount = -50.00m;
            first.Category = groceries;
            first.Memo = "First split";
            Split second = transaction.Splits.AddSplit(1);
            second.Amount = -25.00m;
            second.Category = gas;
            second.Memo = "Second split";

            this.Database.SaveOne(transaction);

            MyMoney reloaded = this.Database.Load(null);
            Account reloadedAccount = reloaded.Accounts.FindAccount("Checking");
            Transaction foundTransaction = reloaded.Transactions.GetTransactionsFrom(reloadedAccount)[0];

            Assert.That(foundTransaction.IsSplit, Is.True);
            Assert.That(foundTransaction.Splits.Count, Is.EqualTo(2));

            bool foundFirst = false;
            bool foundSecond = false;
            foreach (Split split in foundTransaction.Splits)
            {
                if (split.Memo == "First split")
                {
                    foundFirst = true;
                    Assert.That(split.Amount, Is.EqualTo(-50.00m));
                    Assert.That(split.Category.Name, Is.EqualTo("Groceries"));
                }
                else if (split.Memo == "Second split")
                {
                    foundSecond = true;
                    Assert.That(split.Amount, Is.EqualTo(-25.00m));
                    Assert.That(split.Category.Name, Is.EqualTo("Gas"));
                }
            }
            Assert.That(foundFirst, Is.True, "First split should have round-tripped");
            Assert.That(foundSecond, Is.True, "Second split should have round-tripped");
        }

        [Test]
        public void RestoreRowVersions_CoversEveryIAggregateRootType()
        {
            // Safety net: if a new IAggregateRoot type is ever added to Money.cs without a
            // matching entry in MockDatabase.RestoredAggregateRootCollections, that type's
            // RowVersion never gets restored on Load(), causing a permanent
            // ConcurrencyConflictException on every save - with no symptom that points here.
            HashSet<Type> restoredTypes = new HashSet<Type>();
            foreach (var entry in MockDatabase.RestoredAggregateRootCollections)
            {
                restoredTypes.Add(entry.RootType);
            }

            List<Type> allAggregateRootTypes = new List<Type>();
            foreach (Type t in typeof(IAggregateRoot).Assembly.GetTypes())
            {
                if (t.IsClass && !t.IsAbstract && typeof(IAggregateRoot).IsAssignableFrom(t))
                {
                    allAggregateRootTypes.Add(t);
                }
            }

            Assert.That(allAggregateRootTypes, Is.Not.Empty,
                "Sanity check: expected at least one IAggregateRoot implementor in the assembly.");

            foreach (Type t in allAggregateRootTypes)
            {
                Assert.That(restoredTypes.Contains(t), Is.True,
                    t.Name + " implements IAggregateRoot but MockDatabase.RestoredAggregateRootCollections " +
                    "has no entry for it - Load() would never restore its RowVersion.");
            }
        }
    }
}
