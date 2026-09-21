using System;
using System.Collections.Generic;
using System.Data.SQLite;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

// See SqliteMoneyStoreOpenTests: SqliteConnectionFactory is deliberately duplicated into both
// SQLite assemblies, so a project referencing both must say which copy it means.
using SqliteConnectionFactory = Walkabout.Data.Sqlite.Provisioning.SqliteConnectionFactory;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteWriteInsertTests
    {
        private SqliteMoneyStoreProvisioner provisioner;
        private SqliteMoneyStore store;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);
            this.store = SqliteMoneyStore.OpenOver(this.provisioner.Connection,
                new SqliteStoreOptions("books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown()
        {
            this.store?.Dispose();
            this.provisioner?.Dispose();
        }

        private Account NewAccount(int id, string name) =>
            new Account(this.container)
            {
                Id = id,
                Name = name,
                Type = AccountType.Checking,
                Currency = "USD",
                OpeningBalance = 1234.5678m,
                Description = "opened by a test",
            };

        private long RowCount()
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Accounts;", this.provisioner.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void SaveRoot_OnANewAccount_InsertsItAndAssignsRowVersionOne()
        {
            Account a = this.NewAccount(1, "Checking");

            this.store.SaveRoot(a);

            Assert.That(this.RowCount(), Is.EqualTo(1));
            Assert.That(a.RowVersion, Is.EqualTo(1));
            Assert.That(a.IsInserted, Is.False, "OnUpdated() should have cleared the inserted flag after commit.");
        }

        [Test]
        public void SaveRoot_StoresMoneyAsIntegerTenThousandths()
        {
            this.store.SaveRoot(this.NewAccount(1, "Checking"));

            using (var cmd = new SQLiteCommand(
                "SELECT typeof(OpeningBalance), OpeningBalance FROM Accounts WHERE Id = 1;",
                this.provisioner.Connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetString(0), Is.EqualTo("integer"));
                Assert.That(reader.GetInt64(1), Is.EqualTo(12345678L));
            }
        }

        [Test]
        public void SaveRoots_InsertsAWholeBatchInOneTransactionAndVersionsThemAll()
        {
            var roots = new List<IAggregateRoot>
            {
                this.NewAccount(1, "Checking"),
                this.NewAccount(2, "Savings"),
                this.NewAccount(3, "Brokerage"),
            };

            this.store.SaveRoots(roots);

            Assert.That(this.RowCount(), Is.EqualTo(3));
            foreach (Account a in roots)
            {
                Assert.That(a.RowVersion, Is.EqualTo(1), $"{a.Name} did not get its version back.");
            }
        }

        [Test]
        public void SaveRoots_WhenOneRootCollides_RollsBackTheWholeBatch()
        {
            // R-CRUD-2: atomic at the call boundary, including in-memory side effects. The first
            // root's statement "succeeded" before the second one collided, so without the
            // postCommitActions deferral its RowVersion would claim a commit the rollback undid.
            this.store.SaveRoot(this.NewAccount(2, "Existing"));

            Account first = this.NewAccount(1, "Checking");
            Account colliding = this.NewAccount(2, "Collides");

            Assert.Throws<ConcurrencyConflictException>(
                () => this.store.SaveRoots(new List<IAggregateRoot> { first, colliding }));

            Assert.That(this.RowCount(), Is.EqualTo(1));
            Assert.That(first.RowVersion, Is.EqualTo(0), "A rolled-back insert must not leave a RowVersion behind.");
            Assert.That(first.IsInserted, Is.True, "A rolled-back insert must leave the root still dirty.");
        }

        [Test]
        public void SaveRoot_OnARootWithNoIdAssigned_ThrowsArgumentException()
        {
            Account a = new Account(this.container) { Name = "No Id", Type = AccountType.Cash };

            // Account's own default is id == -1. Id allocation stays a caller concern - with no
            // ambient graph, IMoneyQuery.NextAccountId is where a caller gets one (task 15).
            Assert.Throws<ArgumentException>(() => this.store.SaveRoot(a));
        }
    }
}
