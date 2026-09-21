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
    public class SqliteWriteUpdateTests
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

        private Account Saved(int id, string name)
        {
            var a = new Account(this.container)
            {
                Id = id, Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 10m
            };
            this.store.SaveRoot(a);
            return a;
        }

        private string NameInDatabase(int id)
        {
            using (var cmd = new SQLiteCommand("SELECT Name FROM Accounts WHERE Id = @id;", this.provisioner.Connection))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return Convert.ToString(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void SaveRoot_OnAChangedAccount_UpdatesTheRowAndBumpsTheVersion()
        {
            Account a = this.Saved(1, "Checking");

            a.Name = "Current Account";
            this.store.SaveRoot(a);

            Assert.That(this.NameInDatabase(1), Is.EqualTo("Current Account"));
            Assert.That(a.RowVersion, Is.EqualTo(2));
            Assert.That(a.IsChanged, Is.False);
        }

        [Test]
        public void SaveRoot_OnAStaleRoot_ThrowsConcurrencyConflictCarryingTheStoredVersion()
        {
            Account a = this.Saved(1, "Checking");
            Account other = this.Saved(2, "Savings");

            // Someone else wrote row 1 since 'a' was loaded.
            using (var cmd = new SQLiteCommand(
                "UPDATE Accounts SET Name = 'Theirs', Version = Version + 1 WHERE Id = 1;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.Name = "Mine";

            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.store.SaveRoot(a));

            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));
            Assert.That(this.NameInDatabase(1), Is.EqualTo("Theirs"));
            Assert.That(other.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void AfterAConflict_TheSameCallIsStillAdmissible()
        {
            // Spec section 1.6a consequence 2: the postCommitActions deferral means a failed write
            // leaves the root's change state untouched, which is what makes the business-layer
            // retry loop legal at all. Under a unified SaveRoot this was true but invisible; with
            // a state precondition on the method it becomes load-bearing.
            Account a = this.Saved(1, "Checking");

            using (var cmd = new SQLiteCommand("UPDATE Accounts SET Version = Version + 1 WHERE Id = 1;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.Name = "Mine";
            Assert.Throws<ConcurrencyConflictException>(() => this.store.SaveRoot(a));

            Assert.That(a.IsChanged, Is.True);
            Assert.That(a.RowVersion, Is.EqualTo(1), "A failed write must not advance RowVersion.");

            // Re-query, reapply, retry - what AddAccountService will do in Task 24.
            a.RowVersion = 2;
            Assert.DoesNotThrow(() => this.store.SaveRoot(a));
            Assert.That(this.NameInDatabase(1), Is.EqualTo("Mine"));
        }

        [Test]
        public void SaveRoot_OnACleanAccount_IsASilentNoOp()
        {
            // Spec section 1.6c: SaveRoot deliberately admits None and no-ops. Account has no
            // owned children, so for this slice nothing at all is written.
            Account a = this.Saved(1, "Checking");
            long before = a.RowVersion;

            Assert.DoesNotThrow(() => this.store.SaveRoot(a));

            Assert.That(a.RowVersion, Is.EqualTo(before));
            Assert.That(this.NameInDatabase(1), Is.EqualTo("Checking"));
        }

        [Test]
        public void SaveRoots_WithOneStaleRootAmongMany_RollsBackEveryWrite()
        {
            Account a = this.Saved(1, "Checking");
            Account b = this.Saved(2, "Savings");

            using (var cmd = new SQLiteCommand("UPDATE Accounts SET Version = Version + 1 WHERE Id = 2;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.Name = "A2";
            b.Name = "B2";

            Assert.Throws<ConcurrencyConflictException>(
                () => this.store.SaveRoots(new List<IAggregateRoot> { a, b }));

            Assert.That(this.NameInDatabase(1), Is.EqualTo("Checking"),
                "The first root's successful statement must have been rolled back.");
            Assert.That(a.RowVersion, Is.EqualTo(1),
                "The first root must not carry a RowVersion claiming a commit the rollback undid.");
        }
    }
}
