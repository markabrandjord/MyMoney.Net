using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    public abstract partial class DatabaseContractTests
    {
        private static MyMoney BuildOneTransactionMoney(out Account account, out Transaction transaction)
        {
            MyMoney money = new MyMoney();
            account = money.Accounts.AddAccount("Checking");
            transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2020, 1, 1);
            transaction.Amount = -42.50m;
            transaction.Memo = "Test transaction";
            money.Transactions.AddTransaction(transaction);
            return money;
        }

        [Test]
        public void SaveOne_NewTransaction_PersistsAndSetsRowVersionToOne()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);

            this.Database.SaveOne(transaction);

            Assert.That(transaction.RowVersion, Is.EqualTo(1));
            Assert.That(transaction.IsInserted, Is.False);
            Assert.That(transaction.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Account reloadedAccount = reloaded.Accounts.FindAccount("Checking");
            Transaction found = reloaded.Transactions.FindTransactionById(transaction.Id);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Amount, Is.EqualTo(-42.50m));
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateTransactionAfterReload_IncrementsRowVersion()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction found = reloaded.Transactions.FindTransactionById(id);
            found.Memo = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Transaction foundAgain = reloadedAgain.Transactions.FindTransactionById(id);
            Assert.That(foundAgain.Memo, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleTransactionRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Transaction transactionA = readerA.Transactions.FindTransactionById(id);
            transactionA.Memo = "From A";
            this.Database.SaveOne(transactionA);

            Transaction transactionB = readerB.Transactions.FindTransactionById(id);
            transactionB.Memo = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(transactionB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Transactions.FindTransactionById(id).Memo, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_DeleteTransaction_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction toDelete = reloaded.Transactions.FindTransactionById(id);
            reloaded.Transactions.RemoveTransaction(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Transactions.FindTransactionById(id), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Transactions.FindTransactionById(id), Is.Null);
        }

        [Test]
        public void SaveOne_TransactionWithNewSplit_PersistsSplitAndSetsItClean()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);

            Category category = transaction.MyMoney.Categories.GetOrCreateCategory("SplitCategory", CategoryType.Expense);
            this.Database.SaveOne(category);
            Split split = transaction.NonNullSplits.AddSplit();
            split.Amount = -20m;
            split.Category = category;
            split.Memo = "Split A";

            this.Database.SaveOne(transaction);

            Assert.That(split.IsInserted, Is.False);
            Assert.That(split.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Transaction foundTransaction = reloaded.Transactions.FindTransactionById(transaction.Id);
            Assert.That(foundTransaction.IsSplit, Is.True);
            Split foundSplit = foundTransaction.Splits.GetSplits()[0];
            Assert.That(foundSplit.Amount, Is.EqualTo(-20m));
            Assert.That(foundSplit.Memo, Is.EqualTo("Split A"));
        }

        [Test]
        public void SaveOne_TransactionWithSplitOnlyEdit_PersistsSplitEvenThoughTransactionItselfIsUnchanged()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            Split split = transaction.NonNullSplits.AddSplit();
            split.Amount = -42.50m;
            split.Memo = "Original";
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction reloadedTransaction = reloaded.Transactions.FindTransactionById(id);
            Split reloadedSplit = reloadedTransaction.Splits.GetSplits()[0];
            reloadedSplit.Memo = "Edited";
            Assert.That(reloadedTransaction.IsChanged, Is.False,
                "precondition: only the split changed, not the transaction itself - this is what exercises " +
                "the 'process owned children regardless of the transaction's own change state' path.");

            this.Database.SaveOne(reloadedTransaction);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Transactions.FindTransactionById(id).Splits.GetSplits()[0].Memo, Is.EqualTo("Edited"));
        }

        [Test]
        public void SaveOne_DeleteTransactionWithSplits_RemovesSplitsBeforeTransactionRow()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            Split split = transaction.NonNullSplits.AddSplit();
            split.Amount = -42.50m;
            this.Database.SaveOne(transaction);
            long id = transaction.Id;

            MyMoney reloaded = this.Database.Load(null);
            Transaction toDelete = reloaded.Transactions.FindTransactionById(id);
            reloaded.Transactions.RemoveTransaction(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            // This must not throw a foreign-key violation: Splits.Transaction is a real
            // [ColumnObjectMapping] FK to Transactions.Id (confirmed during design), so deleting
            // the parent row before its Splits would fail exactly like the SqlServerStoredProcDatabase
            // bug fixed earlier this session (FK_Splits_Transaction).
            Assert.DoesNotThrow(() => this.Database.SaveOne(toDelete));

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Transactions.FindTransactionById(id), Is.Null);
        }

        [Test]
        public void SaveOne_TransactionWithInvestment_PersistsInvestment()
        {
            BuildOneTransactionMoney(out Account account, out Transaction transaction);
            this.Database.SaveOne(account);
            Security security = transaction.MyMoney.Securities.FindSymbol("MSFT", true);
            this.Database.SaveOne(security);

            Investment investment = transaction.GetOrCreateInvestment();
            investment.Security = security;
            investment.UnitPrice = 100m;
            investment.Units = 10m;
            investment.Type = InvestmentType.Buy;

            this.Database.SaveOne(transaction);

            MyMoney reloaded = this.Database.Load(null);
            Transaction found = reloaded.Transactions.FindTransactionById(transaction.Id);
            Assert.That(found.Investment, Is.Not.Null);
            Assert.That(found.Investment.Units, Is.EqualTo(10m));
            Assert.That(found.Investment.UnitPrice, Is.EqualTo(100m));
        }

        [Test]
        public void SaveTransfer_CommitsBothTransactionsTogether()
        {
            MyMoney money = new MyMoney();
            Account checking = money.Accounts.AddAccount("Checking");
            Account savings = money.Accounts.AddAccount("Savings");
            this.Database.SaveOne(checking);
            this.Database.SaveOne(savings);

            Transaction from = money.Transactions.NewTransaction(checking);
            from.Amount = -100m;
            money.Transactions.AddTransaction(from);
            Transaction to = money.Transactions.NewTransaction(savings);
            to.Amount = 100m;
            money.Transactions.AddTransaction(to);
            from.Transfer = new Transfer(from.Id, from, to);
            to.Transfer = new Transfer(to.Id, to, from);

            // Setting .Transfer auto-assigns .Payee to the well-known "Transfer" sentinel payee
            // (Transaction.Transfer's setter) - it must be persisted first, same as any other FK
            // dependency (Account, Category, etc.) every other test in this file saves before the
            // entity that references it.
            this.Database.SaveOne(money.Payees.Transfer);

            this.Database.SaveTransfer(from, to);

            Assert.That(from.RowVersion, Is.EqualTo(1));
            Assert.That(to.RowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Transaction reloadedFrom = reloaded.Transactions.FindTransactionById(from.Id);
            Transaction reloadedTo = reloaded.Transactions.FindTransactionById(to.Id);
            Assert.That(reloadedFrom.Transfer, Is.Not.Null);
            Assert.That(reloadedFrom.Transfer.Transaction.Id, Is.EqualTo(to.Id));
            Assert.That(reloadedTo.Transfer, Is.Not.Null);
            Assert.That(reloadedTo.Transfer.Transaction.Id, Is.EqualTo(from.Id));
        }

        [Test]
        public void SaveTransfer_ConflictOnOneSideRollsBackBoth()
        {
            MyMoney money = new MyMoney();
            Account checking = money.Accounts.AddAccount("Checking");
            Account savings = money.Accounts.AddAccount("Savings");
            this.Database.SaveOne(checking);
            this.Database.SaveOne(savings);
            Transaction from = money.Transactions.NewTransaction(checking);
            from.Amount = -100m;
            money.Transactions.AddTransaction(from);
            this.Database.SaveOne(from);

            MyMoney reader = this.Database.Load(null);
            Transaction staleFrom = reader.Transactions.FindTransactionById(from.Id);

            MyMoney otherWriter = this.Database.Load(null);
            Transaction otherFrom = otherWriter.Transactions.FindTransactionById(from.Id);
            otherFrom.Memo = "Changed elsewhere";
            this.Database.SaveOne(otherFrom);

            staleFrom.Memo = "Attempted";
            Transaction newTo = reader.Transactions.NewTransaction(reader.Accounts.FindAccount("Savings"));
            newTo.Amount = 100m;
            reader.Transactions.AddTransaction(newTo);

            // newTo is listed FIRST deliberately - same reasoning as SaveBatch_OneStaleRootAmongMany:
            // this only genuinely proves rollback if newTo's own write already "succeeded" within the
            // transaction before staleFrom's conflict is detected.
            Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveTransfer(newTo, staleFrom));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Transactions.FindTransactionById(newTo.Id), Is.Null);
            Assert.That(reloaded.Transactions.FindTransactionById(from.Id).Memo, Is.EqualTo("Changed elsewhere"));
        }
    }
}
