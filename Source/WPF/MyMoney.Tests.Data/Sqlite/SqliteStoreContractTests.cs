using System;
using System.Data.SQLite;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;

namespace Walkabout.Tests.Data.Sqlite
{
    /// <summary>
    /// The SQLite arm of the shared contract suite. Plan B's slice 8 adds exactly one more file
    /// like this one for SQL Server.
    /// </summary>
    [TestFixture]
    public class SqliteStoreContractTests : StoreContractTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        protected override IMoneyStore Store => this.fixture.Store;

        protected override IMoneyQuery Query => this.fixture.Query;

        protected override IMoneyStoreProvisioner Provisioner => this.fixture.Provisioner;

        protected override int ExpectedStoreMaxSchemaVersion => SqliteMoneyStore.MaxKnownSchemaVersion;

        protected override int InsteadOfTriggerCount()
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' "
                + "AND UPPER(sql) LIKE '%INSTEAD OF%';",
                this.fixture.Connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }
    }
}
