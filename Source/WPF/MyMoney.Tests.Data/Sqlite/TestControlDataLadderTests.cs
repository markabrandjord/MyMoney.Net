using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using MyMoney.TestKit;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class TestControlDataLadderTests
    {
        private InMemorySqliteStore fixture;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.fixture = InMemorySqliteStore.Create();
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private IMoneyStoreTestControl Control => this.fixture.TestControl;

        private Account Save(string name)
        {
            var a = new Account(this.container)
            {
                Id = checked((int)this.fixture.Query.NextAccountId()),
                Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 0m
            };
            this.fixture.Store.SaveRoot(a);
            return a;
        }

        private void InsertOnlineAccount(long id)
        {
            using (var cmd = new SQLiteCommand(
                "INSERT INTO OnlineAccounts (Id, Name) VALUES (@id, 'bank');", this.fixture.Connection))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        [Test]
        public void Tables_AreTheDataTablesAndNeverTheLedger()
        {
            var names = this.Control.Tables.Select(t => t.Name).OrderBy(n => n).ToList();

            Assert.That(names, Is.EqualTo(new[] { "Accounts", "Categories", "OnlineAccounts" }));
        }

        [Test]
        public void TableRefsExposedByTheTestKit_AreExactlyTheIntrospectedDataTables()
        {
            // Spec section 2.6.6's anti-drift guard. Add a table without a TableRef, or leave a
            // TableRef behind after dropping a table, and this goes red.
            Assert.That(
                this.Control.Tables.Select(t => t.Name).OrderBy(n => n),
                Is.EqualTo(TableRef.All.Select(t => t.Name).OrderBy(n => n)));
        }

        [Test]
        public void ClearTable_EmptiesOneTableAndReportsTheCount()
        {
            this.Save("Checking");
            this.Save("Savings");
            this.InsertOnlineAccount(1);

            ClearResult result = this.Control.ClearTable(TableRef.Accounts);

            Assert.That(result.RowsDeleted[TableRef.Accounts], Is.EqualTo(2));
            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
            Assert.That(this.Control.RowCount(TableRef.OnlineAccounts), Is.EqualTo(1));
        }

        [Test]
        public void ClearTableOfTRoot_IsTheSameAsClearTableOfItsTableRef()
        {
            this.Save("Checking");

            this.Control.ClearTable<Account>();

            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
        }

        [Test]
        public void ClearAllData_EmptiesEveryDataTableButLeavesEverySchemaObject()
        {
            this.InsertOnlineAccount(1);
            this.Save("Checking");
            int versionBefore = this.Control.CurrentSchemaVersion;

            ClearResult result = this.Control.ClearAllData();

            Assert.That(result.RowsDeleted.Values.Sum(), Is.EqualTo(2));
            Assert.That(this.Control.Tables.All(t => this.Control.RowCount(t) == 0), Is.True);
            Assert.That(this.Control.CurrentSchemaVersion, Is.EqualTo(versionBefore),
                "ClearAllData must leave __SchemaHistory alone - spec 2.6.1's two ladders.");
            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void ClearAllData_RespectsForeignKeyOrderWithFkEnforcementOn()
        {
            this.InsertOnlineAccount(1);
            using (var cmd = new SQLiteCommand(
                "UPDATE Accounts SET OnlineAccount = 1 WHERE Id = @id;", this.fixture.Connection))
            {
                Account a = this.Save("Checking");
                cmd.Parameters.AddWithValue("@id", a.Id);
                cmd.ExecuteNonQuery();
            }

            Assert.DoesNotThrow(() => this.Control.ClearAllData());
        }

        [Test]
        public void ClearAllData_IsIdempotent()
        {
            this.Save("Checking");
            this.Control.ClearAllData();

            ClearResult second = this.Control.ClearAllData();

            Assert.That(second.RowsDeleted.Values.Sum(), Is.EqualTo(0));
        }

        [Test]
        public void AfterAClearWithResetIdentity_TheNextRowGetsTheSameIdAsInAFreshlyPavedDatabase()
        {
            // Spec section 2.6.4's cross-engine trap, pinned. This is the only thing that keeps
            // the ResetIdentity option honest, and Plan B's SQL Server arm must satisfy it too.
            this.Save("Checking");
            this.Save("Savings");

            this.Control.ClearTable(TableRef.Accounts);

            Assert.That(this.fixture.Query.NextAccountId(), Is.EqualTo(1));

            using (InMemorySqliteStore pristine = InMemorySqliteStore.Create())
            {
                Assert.That(this.fixture.Query.NextAccountId(), Is.EqualTo(pristine.Query.NextAccountId()));
            }
        }

        [Test]
        public void ClearTables_ClearsOnlyTheNamedSubset()
        {
            this.InsertOnlineAccount(1);
            this.Save("Checking");

            this.Control.ClearTables(new List<TableRef> { TableRef.Accounts });

            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
            Assert.That(this.Control.RowCount(TableRef.OnlineAccounts), Is.EqualTo(1));
        }
    }
}
