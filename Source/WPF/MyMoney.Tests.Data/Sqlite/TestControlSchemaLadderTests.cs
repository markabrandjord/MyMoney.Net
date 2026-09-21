using System;
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
    public class TestControlSchemaLadderTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private long ObjectCount()
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';", this.fixture.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void DropAll_RemovesEverySchemaObjectIncludingTheLedger()
        {
            Assert.That(this.ObjectCount(), Is.GreaterThan(0));

            this.fixture.Provisioner.DropAll();

            Assert.That(this.ObjectCount(), Is.EqualTo(0));
            Assert.That(this.fixture.Provisioner.CurrentVersion(), Is.EqualTo(0));
        }

        [Test]
        public void DropAll_IsIdempotent()
        {
            this.fixture.Provisioner.DropAll();

            Assert.DoesNotThrow(() => this.fixture.Provisioner.DropAll());
        }

        [Test]
        public void ResetSchema_IsDropAllThenApplyTo_AndLeavesAVerifiedCleanSchema()
        {
            var a = new Account(new Accounts((PersistentObject)null))
            {
                Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m
            };
            this.fixture.Store.SaveRoot(a);

            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);

            Assert.That(this.fixture.TestControl.CurrentSchemaVersion, Is.EqualTo(SchemaStepCatalog.LatestVersion));
            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All), Is.Empty);
        }

        [Test]
        public void ResetSchema_IsIdempotent()
        {
            // Spec section 2.6.8: idempotency is what makes "nuke and pave is over" true after
            // EVERY call to a reset routine, not just the first.
            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);
            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);

            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void ResetSchema_RecoversFromADriftedSchema()
        {
            // The normal reason to reset during nuke-and-pave. ClearAllData could not do this,
            // which is why the two ladders are separate - spec section 2.6.1.
            using (var cmd = new SQLiteCommand("ALTER TABLE Accounts ADD COLUMN Rogue TEXT;", this.fixture.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.False);

            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);

            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void TableOrder_PutsAReferencingTableBeforeTheTableItReferences()
        {
            var order = SqliteTableOrder.ForDelete(this.fixture.Connection).ToList();

            Assert.That(order, Does.Not.Contain("__SchemaHistory"));
            Assert.That(order.IndexOf("Accounts"), Is.LessThan(order.IndexOf("OnlineAccounts")));
            Assert.That(order.IndexOf("Accounts"), Is.LessThan(order.IndexOf("Categories")));
        }
    }
}
