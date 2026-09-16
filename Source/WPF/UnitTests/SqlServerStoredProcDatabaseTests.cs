using System;
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
    }
}
