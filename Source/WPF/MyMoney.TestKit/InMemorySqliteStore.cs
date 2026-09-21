using System;
using System.Data.SQLite;
using System.Threading;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

// SqliteConnectionFactory is deliberately duplicated into both SQLite assemblies (spec section
// 1's Recommendation, enforced by Task 22), so this project - which references both - has to say
// which copy it means. They are identical apart from the namespace.
using SqliteConnectionFactory = Walkabout.Data.Sqlite.Provisioning.SqliteConnectionFactory;

namespace MyMoney.TestKit
{
    /// <summary>
    /// T-1, implemented: the default business-test store is a REAL in-memory SQLite database, not
    /// a hand-written mock (spec section 2.4). One :memory: store per test, over one connection,
    /// disposed at teardown - no file, no cleanup, no cross-test leakage.
    ///
    /// An in-memory database lives exactly as long as its connection, so the store, the query and
    /// (from Task 18) the test control all share ONE connection. That is the lifetime model
    /// SqliteDatabase's long-lived cached connection already has, which is why this needs no new
    /// lifetime design.
    ///
    /// The schema is applied with the SAME executor production uses. If per-test provisioning ever
    /// proves too slow, the fix is Backup/VACUUM INTO cloning of a prepared template (already
    /// built, Task 9) - NOT a parallel test-only schema path, which would reintroduce drift by the
    /// back door.
    /// </summary>
    public sealed class InMemorySqliteStore : IDisposable
    {
        private static int counter;

        private readonly SqliteMoneyStore store;

        private InMemorySqliteStore(string displayName, SqliteMoneyStoreProvisioner provisioner, SqliteMoneyStore store, SqliteMoneyQuery query)
        {
            this.DisplayName = displayName;
            this.Provisioner = provisioner;
            this.store = store;
            this.Query = query;
        }

        public static InMemorySqliteStore Create(bool isTestDatabase = true)
        {
            string displayName = "in-memory-" + Interlocked.Increment(ref counter).ToString();

            var provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                displayName, SqliteConnectionFactory.InMemoryDataSource, isTestDatabase));
            try
            {
                provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);

                SqliteMoneyStore store = SqliteMoneyStore.OpenOver(
                    provisioner.Connection,
                    new SqliteStoreOptions(displayName, SqliteConnectionFactory.InMemoryDataSource, isTestDatabase));

                return new InMemorySqliteStore(
                    displayName, provisioner, store, SqliteMoneyQuery.OpenOver(provisioner.Connection));
            }
            catch
            {
                provisioner.Dispose();
                throw;
            }
        }

        public string DisplayName { get; }

        public SqliteMoneyStoreProvisioner Provisioner { get; }

        public SQLiteConnection Connection => this.Provisioner.Connection;

        public IMoneyStore Store => this.store;

        public IMoneyQuery Query { get; }

        public void Dispose()
        {
            this.store.Dispose();
            this.Provisioner.Dispose();
        }
    }
}
