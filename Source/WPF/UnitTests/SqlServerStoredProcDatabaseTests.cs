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
    }
}
