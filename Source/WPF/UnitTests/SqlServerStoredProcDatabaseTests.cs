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
            var category = money.Categories.GetOrCreateCategory("SqlServerStoredProcDatabaseTests:Category", CategoryType.Expense);
            category.Description = "Test category";
            category.Budget = 250.00m;
            category.OnInserted();

            db.UpdateCategories(money.Categories);

            var reloaded = new MyMoney();
            db.ReadCategories(reloaded.Categories, reloaded);
            var found = reloaded.Categories.FindCategory("SqlServerStoredProcDatabaseTests:Category");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Description, Is.EqualTo("Test category"));
            Assert.That(found.Budget, Is.EqualTo(250.00m));

            found.Description = "Updated description";
            db.UpdateCategories(reloaded.Categories);

            var afterUpdate = new MyMoney();
            db.ReadCategories(afterUpdate.Categories, afterUpdate);
            var updated = afterUpdate.Categories.FindCategory("SqlServerStoredProcDatabaseTests:Category");
            Assert.That(updated.Description, Is.EqualTo("Updated description"));

            updated.OnDelete();
            var toDelete = new Categories(afterUpdate);
            toDelete.Add(updated);
            db.UpdateCategories(toDelete);

            var afterDelete = new MyMoney();
            db.ReadCategories(afterDelete.Categories, afterDelete);
            Assert.That(afterDelete.Categories.FindCategory("SqlServerStoredProcDatabaseTests:Category"), Is.Null);
        }
    }
}
