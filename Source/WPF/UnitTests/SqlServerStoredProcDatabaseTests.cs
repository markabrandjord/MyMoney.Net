using System;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class SqlServerStoredProcDatabaseTests
    {
        private const string EnvVarName = "MYMONEY_TEST_SQLSERVER_USER_CONNECTION";

        private string GetConnectionStringOrSkip()
        {
            string connectionString = Environment.GetEnvironmentVariable(EnvVarName);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore($"Set {EnvVarName} to a MyMoneyUser connection string to run this test against a real SQL Server.");
            }
            return connectionString;
        }

        [Test]
        public void InsertUpdateDeletePayee_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var payee = money.Payees.AddPayee(999001);
            payee.Name = "SqlServerStoredProcDatabaseTests Payee";
            payee.OnInserted();

            db.UpdatePayees(money.Payees);

            var reloaded = new MyMoney();
            db.ReadPayees(reloaded.Payees, reloaded);
            var found = reloaded.Payees.FindPayee("SqlServerStoredProcDatabaseTests Payee", false);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Id, Is.EqualTo(999001));

            // Exercise the dbo.Payees_Update path (p.IsChanged branch).
            found.Name = "SqlServerStoredProcDatabaseTests Payee Updated";
            db.UpdatePayees(reloaded.Payees);

            var afterUpdate = new MyMoney();
            db.ReadPayees(afterUpdate.Payees, afterUpdate);
            var updated = afterUpdate.Payees.FindPayee("SqlServerStoredProcDatabaseTests Payee Updated", false);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated.Id, Is.EqualTo(999001));
            Assert.That(afterUpdate.Payees.FindPayee("SqlServerStoredProcDatabaseTests Payee", false), Is.Null);

            updated.OnDelete();
            var toDelete = new Payees(afterUpdate);
            toDelete.Add(updated);
            db.UpdatePayees(toDelete);

            var afterDelete = new MyMoney();
            db.ReadPayees(afterDelete.Payees, afterDelete);
            Assert.That(afterDelete.Payees.FindPayee("SqlServerStoredProcDatabaseTests Payee Updated", false), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteAccount_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount(999002);
            account.Name = "SqlServerStoredProcDatabaseTests Account";
            account.Type = AccountType.Checking;
            account.Description = "Test account";
            account.OpeningBalance = 100.00m;
            account.OnInserted();

            db.UpdateAccounts(money.Accounts);

            var reloaded = new MyMoney();
            db.ReadAccounts(reloaded.Accounts, reloaded);
            var found = reloaded.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Account");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Type, Is.EqualTo(AccountType.Checking));
            Assert.That(found.Description, Is.EqualTo("Test account"));
            Assert.That(found.OpeningBalance, Is.EqualTo(100.00m));

            found.Description = "Updated description";
            db.UpdateAccounts(reloaded.Accounts);

            var afterUpdate = new MyMoney();
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            var updated = afterUpdate.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Account");
            Assert.That(updated.Description, Is.EqualTo("Updated description"));

            updated.OnDelete();
            var toDelete = new Accounts(afterUpdate);
            toDelete.Add(updated);
            db.UpdateAccounts(toDelete);

            var afterDelete = new MyMoney();
            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            Assert.That(afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Account"), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteCategory_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var category = money.Categories.GetOrCreateCategory("SqlServerStoredProcDatabaseTestsCategory", CategoryType.Expense);
            category.Description = "Test category";
            category.Budget = 250.00m;
            category.OnInserted();

            db.UpdateCategories(money.Categories);

            var reloaded = new MyMoney();
            db.ReadCategories(reloaded.Categories, reloaded);
            var found = reloaded.Categories.FindCategory("SqlServerStoredProcDatabaseTestsCategory");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Description, Is.EqualTo("Test category"));
            Assert.That(found.Budget, Is.EqualTo(250.00m));

            found.Description = "Updated description";
            db.UpdateCategories(reloaded.Categories);

            var afterUpdate = new MyMoney();
            db.ReadCategories(afterUpdate.Categories, afterUpdate);
            var updated = afterUpdate.Categories.FindCategory("SqlServerStoredProcDatabaseTestsCategory");
            Assert.That(updated.Description, Is.EqualTo("Updated description"));

            updated.OnDelete();
            var toDelete = new Categories(afterUpdate);
            toDelete.Add(updated);
            db.UpdateCategories(toDelete);

            var afterDelete = new MyMoney();
            db.ReadCategories(afterDelete.Categories, afterDelete);
            Assert.That(afterDelete.Categories.FindCategory("SqlServerStoredProcDatabaseTestsCategory"), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteCurrency_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var currency = money.Currencies.AddCurrency(0);
            currency.Symbol = "ZZT";
            currency.Name = "SqlServerStoredProcDatabaseTests Currency";
            currency.Ratio = 1.5m;
            currency.CultureCode = "en-US";
            currency.OnInserted();

            db.UpdateCurrencies(money.Currencies);

            var reloaded = new MyMoney();
            db.ReadCurrencies(reloaded.Currencies, reloaded);
            var found = reloaded.Currencies.FindCurrency("ZZT");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Name, Is.EqualTo("SqlServerStoredProcDatabaseTests Currency"));
            Assert.That(found.Ratio, Is.EqualTo(1.5m));

            found.Ratio = 2.0m;
            db.UpdateCurrencies(reloaded.Currencies);

            var afterUpdate = new MyMoney();
            db.ReadCurrencies(afterUpdate.Currencies, afterUpdate);
            var updated = afterUpdate.Currencies.FindCurrency("ZZT");
            Assert.That(updated.Ratio, Is.EqualTo(2.0m));

            updated.OnDelete();
            var toDelete = new Currencies(afterUpdate);
            toDelete.Add(updated);
            db.UpdateCurrencies(toDelete);

            var afterDelete = new MyMoney();
            db.ReadCurrencies(afterDelete.Currencies, afterDelete);
            Assert.That(afterDelete.Currencies.FindCurrency("ZZT"), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteSecurity_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var security = money.Securities.AddSecurity(0);
            security.Name = "SqlServerStoredProcDatabaseTests Security";
            security.Symbol = "ZZS";
            security.Price = 42.50m;
            security.SecurityType = SecurityType.Equity;
            security.OnInserted();

            db.UpdateSecurities(money.Securities);

            var reloaded = new MyMoney();
            db.ReadSecurities(reloaded.Securities, reloaded);
            var found = reloaded.Securities.FindSecurity("SqlServerStoredProcDatabaseTests Security", false);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Name, Is.EqualTo("SqlServerStoredProcDatabaseTests Security"));
            Assert.That(found.Price, Is.EqualTo(42.50m));

            found.Price = 50.00m;
            db.UpdateSecurities(reloaded.Securities);

            var afterUpdate = new MyMoney();
            db.ReadSecurities(afterUpdate.Securities, afterUpdate);
            var updated = afterUpdate.Securities.FindSecurity("SqlServerStoredProcDatabaseTests Security", false);
            Assert.That(updated.Price, Is.EqualTo(50.00m));

            updated.OnDelete();
            var toDelete = new Securities(afterUpdate);
            toDelete.Add(updated);
            db.UpdateSecurities(toDelete);

            var afterDelete = new MyMoney();
            db.ReadSecurities(afterDelete.Securities, afterDelete);
            Assert.That(afterDelete.Securities.FindSecurity("SqlServerStoredProcDatabaseTests Security", false), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteStockSplit_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var security = money.Securities.AddSecurity(0);
            security.Name = "SqlServerStoredProcDatabaseTests StockSplit Security";
            security.Symbol = "ZZP";
            security.OnInserted();
            db.UpdateSecurities(money.Securities);

            var split = money.StockSplits.AddStockSplit(0);
            split.Date = new DateTime(2026, 1, 1);
            split.Security = security;
            split.Numerator = 2;
            split.Denominator = 1;
            split.OnInserted();
            db.UpdateStockSplits(money.StockSplits);

            var reloaded = new MyMoney();
            db.ReadSecurities(reloaded.Securities, reloaded);
            db.ReadStockSplits(reloaded.StockSplits, reloaded);
            var reloadedSecurity = reloaded.Securities.FindSecurity("SqlServerStoredProcDatabaseTests StockSplit Security", false);
            var found = reloaded.StockSplits.FindStockSplitByDate(reloadedSecurity, split.Date);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Numerator, Is.EqualTo(2));
            Assert.That(found.Denominator, Is.EqualTo(1));

            found.Numerator = 3;
            db.UpdateStockSplits(reloaded.StockSplits);

            var afterUpdate = new MyMoney();
            db.ReadSecurities(afterUpdate.Securities, afterUpdate);
            db.ReadStockSplits(afterUpdate.StockSplits, afterUpdate);
            var afterUpdateSecurity = afterUpdate.Securities.FindSecurity("SqlServerStoredProcDatabaseTests StockSplit Security", false);
            var updated = afterUpdate.StockSplits.FindStockSplitByDate(afterUpdateSecurity, split.Date);
            Assert.That(updated.Numerator, Is.EqualTo(3));

            updated.OnDelete();
            var toDelete = new StockSplits(afterUpdate);
            toDelete.Add(updated);
            db.UpdateStockSplits(toDelete);

            afterUpdateSecurity.OnDelete();
            var securitiesToDelete = new Securities(afterUpdate);
            securitiesToDelete.Add(afterUpdateSecurity);
            db.UpdateSecurities(securitiesToDelete);

            var afterDelete = new MyMoney();
            db.ReadSecurities(afterDelete.Securities, afterDelete);
            db.ReadStockSplits(afterDelete.StockSplits, afterDelete);
            Assert.That(afterDelete.Securities.FindSecurity("SqlServerStoredProcDatabaseTests StockSplit Security", false), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteAlias_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var payee = money.Payees.AddPayee(999002);
            payee.Name = "SqlServerStoredProcDatabaseTests Alias Payee";
            payee.OnInserted();
            db.UpdatePayees(money.Payees);

            var alias = money.Aliases.AddAlias(0);
            alias.Pattern = "SqlServerStoredProcDatabaseTests Alias Pattern";
            alias.Payee = payee;
            alias.AliasType = AliasType.None;
            alias.OnInserted();
            db.UpdateAliases(money.Aliases);

            var reloaded = new MyMoney();
            db.ReadPayees(reloaded.Payees, reloaded);
            db.ReadAliases(reloaded.Aliases, reloaded);
            var found = reloaded.Aliases.FindAlias("SqlServerStoredProcDatabaseTests Alias Pattern");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Payee.Name, Is.EqualTo("SqlServerStoredProcDatabaseTests Alias Payee"));

            found.AliasType = AliasType.Regex;
            db.UpdateAliases(reloaded.Aliases);

            var afterUpdate = new MyMoney();
            db.ReadPayees(afterUpdate.Payees, afterUpdate);
            db.ReadAliases(afterUpdate.Aliases, afterUpdate);
            var updated = afterUpdate.Aliases.FindAlias("SqlServerStoredProcDatabaseTests Alias Pattern");
            Assert.That(updated.AliasType, Is.EqualTo(AliasType.Regex));

            updated.OnDelete();
            var toDelete = new Aliases(afterUpdate);
            toDelete.Add(updated);
            db.UpdateAliases(toDelete);

            var payeeToDelete = afterUpdate.Payees.FindPayee("SqlServerStoredProcDatabaseTests Alias Payee", false);
            payeeToDelete.OnDelete();
            var payeesToDelete = new Payees(afterUpdate);
            payeesToDelete.Add(payeeToDelete);
            db.UpdatePayees(payeesToDelete);

            var afterDelete = new MyMoney();
            db.ReadAliases(afterDelete.Aliases, afterDelete);
            Assert.That(afterDelete.Aliases.FindAlias("SqlServerStoredProcDatabaseTests Alias Pattern"), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteTransaction_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests Transaction Account");
            account.Type = AccountType.Checking;
            account.OnInserted();
            db.UpdateAccounts(money.Accounts);

            var transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2026, 1, 15);
            transaction.Amount = -42.50m;
            transaction.Memo = "SqlServerStoredProcDatabaseTests Transaction";
            money.Transactions.AddTransaction(transaction);
            db.UpdateTransactions(money.Transactions);

            var reloaded = new MyMoney();
            db.ReadAccounts(reloaded.Accounts, reloaded);
            db.ReadTransactions(reloaded.Transactions, reloaded);
            var reloadedAccount = reloaded.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Transaction Account");
            var found = reloaded.Transactions.GetTransactionsFrom(reloadedAccount)
                .FirstOrDefault(t => t.Memo == "SqlServerStoredProcDatabaseTests Transaction");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Amount, Is.EqualTo(-42.50m));

            found.Amount = -50.00m;
            db.UpdateTransactions(reloaded.Transactions);

            var afterUpdate = new MyMoney();
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            db.ReadTransactions(afterUpdate.Transactions, afterUpdate);
            var afterUpdateAccount = afterUpdate.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Transaction Account");
            var updated = afterUpdate.Transactions.GetTransactionsFrom(afterUpdateAccount)
                .FirstOrDefault(t => t.Memo == "SqlServerStoredProcDatabaseTests Transaction");
            Assert.That(updated.Amount, Is.EqualTo(-50.00m));

            updated.OnDelete();
            var toDelete = new Transactions(afterUpdate);
            toDelete.AddTransaction(updated);
            db.UpdateTransactions(toDelete);

            afterUpdateAccount.OnDelete();
            var accountsToDelete = new Accounts(afterUpdate);
            accountsToDelete.Add(afterUpdateAccount);
            db.UpdateAccounts(accountsToDelete);

            var afterDelete = new MyMoney();
            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            Assert.That(afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Transaction Account"), Is.Null);
        }

        [Test]
        public void InsertUpdateDeleteSplit_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests Split Account");
            account.Type = AccountType.Checking;
            account.OnInserted();
            db.UpdateAccounts(money.Accounts);

            var category = money.Categories.GetOrCreateCategory("SqlServerStoredProcDatabaseTestsSplitCategory", CategoryType.Expense);
            category.OnInserted();
            db.UpdateCategories(money.Categories);

            var transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2026, 1, 15);
            transaction.Amount = -100.00m;
            money.Transactions.AddTransaction(transaction);
            transaction.Splits = new Splits(transaction, transaction);
            var split = transaction.Splits.AddSplit(0);
            split.Amount = -100.00m;
            split.Category = category;
            split.Memo = "SqlServerStoredProcDatabaseTests Split";
            transaction.OnInserted();
            db.UpdateTransactions(money.Transactions);

            var reloaded = new MyMoney();
            db.ReadCategories(reloaded.Categories, reloaded);
            db.ReadAccounts(reloaded.Accounts, reloaded);
            db.ReadTransactions(reloaded.Transactions, reloaded);
            var reloadedAccount = reloaded.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Split Account");
            var foundTransaction = reloaded.Transactions.GetTransactionsFrom(reloadedAccount).First();
            Assert.That(foundTransaction.IsSplit, Is.True);
            Assert.That(foundTransaction.Splits.Count, Is.EqualTo(1));
            var foundSplit = Enumerable.First<Split>(foundTransaction.Splits);
            Assert.That(foundSplit.Memo, Is.EqualTo("SqlServerStoredProcDatabaseTests Split"));

            foundSplit.Memo = "Updated split memo";
            db.UpdateTransactions(reloaded.Transactions);

            var afterUpdate = new MyMoney();
            db.ReadCategories(afterUpdate.Categories, afterUpdate);
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            db.ReadTransactions(afterUpdate.Transactions, afterUpdate);
            var afterUpdateAccount = afterUpdate.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Split Account");
            var updatedTransaction = afterUpdate.Transactions.GetTransactionsFrom(afterUpdateAccount).First();
            Assert.That(Enumerable.First<Split>(updatedTransaction.Splits).Memo, Is.EqualTo("Updated split memo"));

            // Cascade-mark the splits as deleted too, mirroring what
            // Transactions.RemoveTransaction does (Money.cs) -- Transaction.OnDelete()
            // itself does NOT cascade to child Splits, so without this the split
            // row is orphaned in the database once its owning transaction is deleted.
            if (updatedTransaction.IsSplit)
            {
                updatedTransaction.Splits.RemoveAll();
            }
            updatedTransaction.OnDelete();
            var toDelete = new Transactions(afterUpdate);
            toDelete.AddTransaction(updatedTransaction);
            db.UpdateTransactions(toDelete);

            afterUpdateAccount.OnDelete();
            var accountsToDelete = new Accounts(afterUpdate);
            accountsToDelete.Add(afterUpdateAccount);
            db.UpdateAccounts(accountsToDelete);

            var categoryToDelete = afterUpdate.Categories.FindCategory("SqlServerStoredProcDatabaseTestsSplitCategory");
            categoryToDelete.OnDelete();
            var categoriesToDelete = new Categories(afterUpdate);
            categoriesToDelete.Add(categoryToDelete);
            db.UpdateCategories(categoriesToDelete);

            var afterDelete = new MyMoney();
            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            Assert.That(afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Split Account"), Is.Null);
            db.ReadTransactions(afterDelete.Transactions, afterDelete);
            Assert.That(afterDelete.Transactions.Count, Is.EqualTo(0));
        }
    }
}
