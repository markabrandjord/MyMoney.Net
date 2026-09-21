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
    public class SqliteWriteDeleteTests
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

        private Account New(int id, string name) => new Account(this.container)
        {
            Id = id, Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 0m
        };

        private long RowCount()
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Accounts;", this.provisioner.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void DeleteRoot_OnAPersistedAccount_RemovesTheRow()
        {
            Account a = this.New(1, "Checking");
            this.store.SaveRoot(a);

            a.OnDelete();
            this.store.DeleteRoot(a);

            Assert.That(this.RowCount(), Is.EqualTo(0));
        }

        [Test]
        public void DeleteRoot_OnAStaleRoot_ThrowsConcurrencyConflict()
        {
            Account a = this.New(1, "Checking");
            this.store.SaveRoot(a);

            using (var cmd = new SQLiteCommand("UPDATE Accounts SET Version = Version + 1 WHERE Id = 1;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.OnDelete();

            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.store.DeleteRoot(a));

            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(this.RowCount(), Is.EqualTo(1));
        }

        [Test]
        public void SaveRoots_CanCommitAChangedSurvivorAndADeletedVictimTogether()
        {
            // The merge shape: SaveRoots is mixed-state by definition and asserts nothing.
            Account survivor = this.New(1, "Checking");
            Account victim = this.New(2, "Duplicate");
            this.store.SaveRoots(new List<IAggregateRoot> { survivor, victim });

            survivor.Name = "Checking (merged)";
            victim.OnDelete();
            this.store.SaveRoots(new List<IAggregateRoot> { survivor, victim });

            Assert.That(this.RowCount(), Is.EqualTo(1));
            Assert.That(survivor.RowVersion, Is.EqualTo(2));
        }
    }
}
