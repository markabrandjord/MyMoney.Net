using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
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
    public class SqliteReadTests
    {
        private SqliteMoneyStoreProvisioner provisioner;
        private SqliteMoneyStore store;
        private SqliteMoneyQuery query;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);
            this.store = SqliteMoneyStore.OpenOver(this.provisioner.Connection,
                new SqliteStoreOptions("books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.query = SqliteMoneyQuery.OpenOver(this.provisioner.Connection);
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown()
        {
            this.store?.Dispose();
            this.provisioner?.Dispose();
        }

        [Test]
        public void LoadAccounts_RoundTripsEveryPersistedColumn()
        {
            var original = new Account(this.container)
            {
                Id = 7,
                AccountId = "12345",
                OfxAccountId = "OFX-1",
                Name = "Checking",
                Type = AccountType.Brokerage,
                Description = "main account",
                OpeningBalance = -98765.4321m,
                LastSync = new DateTime(2026, 2, 3, 4, 5, 6, 789),
                LastBalance = new DateTime(2026, 2, 4, 0, 0, 0, 0),
                SyncGuid = new SqlGuid(Guid.Parse("11112222-3333-4444-5555-666677778888")),
                Flags = AccountFlags.Budgeted | AccountFlags.TaxDeferred,
                Currency = "EUR",
                WebSite = "https://example.invalid",
                ReconcileWarning = 3,
            };
            this.store.SaveRoot(original);

            Account loaded = this.store.LoadAccounts().Single();

            Assert.That(loaded.Id, Is.EqualTo(7));
            Assert.That(loaded.AccountId, Is.EqualTo("12345"));
            Assert.That(loaded.OfxAccountId, Is.EqualTo("OFX-1"));
            Assert.That(loaded.Name, Is.EqualTo("Checking"));
            Assert.That(loaded.Type, Is.EqualTo(AccountType.Brokerage));
            Assert.That(loaded.Description, Is.EqualTo("main account"));
            Assert.That(loaded.OpeningBalance, Is.EqualTo(-98765.4321m));
            Assert.That(loaded.LastSync, Is.EqualTo(new DateTime(2026, 2, 3, 4, 5, 6, 789)));
            Assert.That(loaded.LastBalance, Is.EqualTo(new DateTime(2026, 2, 4, 0, 0, 0, 0)));
            Assert.That(loaded.SyncGuid.Value, Is.EqualTo(Guid.Parse("11112222-3333-4444-5555-666677778888")));
            Assert.That(loaded.Flags, Is.EqualTo(AccountFlags.Budgeted | AccountFlags.TaxDeferred));
            Assert.That(loaded.Currency, Is.EqualTo("EUR"));
            Assert.That(loaded.WebSite, Is.EqualTo("https://example.invalid"));
            Assert.That(loaded.ReconcileWarning, Is.EqualTo(3));
        }

        [Test]
        public void LoadAccounts_ReturnsRootsAtChangeStateNone_CarryingTheAuthoritativeVersion()
        {
            // A freshly constructed PersistentObject starts at ChangeType.Inserted and every
            // setter fires OnChanged. A loaded root left at Inserted would make its next SaveRoot
            // an INSERT against an existing row - a conflict on an ordinary edit.
            var a = new Account(this.container) { Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m };
            this.store.SaveRoot(a);

            Account loaded = this.store.LoadAccounts().Single();

            Assert.That(loaded.IsInserted, Is.False);
            Assert.That(loaded.IsChanged, Is.False);
            Assert.That(loaded.IsDeleted, Is.False);
            Assert.That(loaded.RowVersion, Is.EqualTo(1));
            Assert.That(loaded.Parent, Is.Not.Null, "A loaded root must be parented - see CLAUDE.md's new Transaction() gotcha.");
        }

        [Test]
        public void ARootLoadedThenEditedThenSaved_Updates()
        {
            var a = new Account(this.container) { Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m };
            this.store.SaveRoot(a);

            Account loaded = this.store.LoadAccounts().Single();
            loaded.Name = "Renamed";
            this.store.SaveRoot(loaded);

            Assert.That(this.store.LoadAccounts().Single().Name, Is.EqualTo("Renamed"));
            Assert.That(this.store.LoadAccounts().Single().RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void ListAccounts_ReturnsProjectionsForEveryAccount()
        {
            this.Save(1, "Checking", AccountFlags.None);
            this.Save(2, "Old Savings", AccountFlags.Closed);

            IReadOnlyList<AccountRow> rows = this.query.ListAccounts(AccountQuery.All);

            Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Checking", "Old Savings" }));
            Assert.That(rows[1].IsClosed, Is.True);
            Assert.That(rows[0].OpeningBalance, Is.EqualTo(25.5m));
        }

        [Test]
        public void ListAccounts_CanExcludeClosedAccounts()
        {
            this.Save(1, "Checking", AccountFlags.None);
            this.Save(2, "Old Savings", AccountFlags.Closed);

            IReadOnlyList<AccountRow> rows = this.query.ListAccounts(new AccountQuery(Array.Empty<long>(), false));

            Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Checking" }));
        }

        [Test]
        public void ListAccounts_CanFilterByIds()
        {
            this.Save(1, "Checking", AccountFlags.None);
            this.Save(2, "Savings", AccountFlags.None);
            this.Save(3, "Brokerage", AccountFlags.None);

            IReadOnlyList<AccountRow> rows = this.query.ListAccounts(new AccountQuery(new long[] { 3, 1 }, true));

            Assert.That(rows.Select(r => r.Id), Is.EqualTo(new long[] { 1, 3 }));
        }

        [Test]
        public void NextAccountId_IsOneOnAnEmptyDatabaseAndMaxPlusOneAfterwards()
        {
            Assert.That(this.query.NextAccountId(), Is.EqualTo(1));

            this.Save(1, "Checking", AccountFlags.None);
            this.Save(9, "Savings", AccountFlags.None);

            Assert.That(this.query.NextAccountId(), Is.EqualTo(10));
        }

        private void Save(int id, string name, AccountFlags flags)
        {
            this.store.SaveRoot(new Account(this.container)
            {
                Id = id, Name = name, Type = AccountType.Checking, Currency = "USD",
                OpeningBalance = 25.5m, Flags = flags
            });
        }
    }
}
