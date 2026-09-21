using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// Everyday reads and writes over one SQLite file (or one :memory: database). No DDL, no
    /// destructive bulk operations: this assembly contains no DROP TABLE, no VACUUM INTO over an
    /// existing file, and no DELETE FROM without a WHERE Id=.
    ///
    /// The honest limit, stated where the code is rather than only in the design document (spec
    /// section 6.1): this arrangement does NOT prevent a buggy, compromised or deliberately
    /// reflective production process from destroying the file. What it gives is that the
    /// destructive code is not in the shipped assembly set, that production code cannot name the
    /// type it would need, and that a build and a test fail if either becomes untrue.
    /// </summary>
    public sealed class SqliteMoneyStore : MoneyStoreBase
    {
        /// <summary>
        /// The highest schema version this store's SQL was written against. Kept as a constant
        /// rather than read from SchemaStepCatalog because this assembly deliberately does not
        /// reference the provisioning one; Task 17's contract suite asserts the two agree, so
        /// they cannot silently drift.
        /// </summary>
        public const int MaxKnownSchemaVersion = 4;

        private readonly SqliteStoreOptions options;
        private readonly SQLiteConnection connection;
        private readonly bool ownsConnection;
        private readonly int schemaVersion;

        private SqliteMoneyStore(SqliteStoreOptions options, SQLiteConnection connection, bool ownsConnection, int schemaVersion)
        {
            this.options = options;
            this.connection = connection;
            this.ownsConnection = ownsConnection;
            this.schemaVersion = schemaVersion;
        }

        /// <summary>Open a store over its own connection to <c>options.DataSource</c>.</summary>
        public static SqliteMoneyStore Open(SqliteStoreOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            SQLiteConnection connection = SqliteConnectionFactory.Open(options.DataSource);
            try
            {
                return new SqliteMoneyStore(options, connection, true, CheckSchemaVersion(connection, options));
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Open a store over an EXISTING connection, which the caller keeps ownership of. An
        /// in-memory database lives exactly as long as its connection, so the T-1 fixture (spec
        /// section 2.4) needs the store, the query and the test control to share one - and
        /// SqliteDatabase already holds one long-lived cached connection, so this is the lifetime
        /// model the codebase already has.
        /// </summary>
        public static SqliteMoneyStore OpenOver(SQLiteConnection connection, SqliteStoreOptions options)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new SqliteMoneyStore(options, connection, false, CheckSchemaVersion(connection, options));
        }

        public SQLiteConnection Connection => this.connection;

        public override StoreIdentity Identity => new StoreIdentity(
            this.options.DisplayName, DbFlavor.Sqlite, this.options.IsTestDatabase, this.schemaVersion);

        public override IReadOnlyList<Account> LoadAccounts() => throw new NotImplementedException("Task 15.");

        protected override void WriteRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            foreach (IAggregateRoot root in roots)
            {
                if (!(root is Account))
                {
                    // Not NotImplementedException-as-a-capability-signal (spec section 3.2): this
                    // is a deliberate, tested statement that Plan A's vertical stops at Account.
                    // The slice that brings this root type extends WriteRoots and deletes this.
                    throw new NotSupportedException(
                        $"This build's SQLite store writes Account roots only; it was handed a "
                        + $"{root.GetType().Name}. Support for that root type arrives with the slice "
                        + "that creates its table.");
                }
            }

            throw new NotImplementedException("Task 12.");
        }

        public override void Dispose()
        {
            if (this.ownsConnection)
            {
                this.connection?.Dispose();
            }
        }

        /// <summary>
        /// Spec section 1.8's second bullet: ApplyTo handles "the app is newer than the database";
        /// the reverse needs a refusal, not a best-effort open. Version 0 (no ledger at all) is
        /// refused by the same check and the same exception type, because a store that opened
        /// cleanly against an unprovisioned database would fail on its first statement with
        /// "no such table: Accounts" instead of saying what is actually wrong.
        /// </summary>
        private static int CheckSchemaVersion(SQLiteConnection connection, SqliteStoreOptions options)
        {
            int version = ReadSchemaVersion(connection);

            if (version == 0)
            {
                throw new SchemaTooNewException(
                    $"'{options.DisplayName}' has no MyMoney schema in it yet. Create or upgrade it "
                    + "before opening it for reading and writing.");
            }

            if (version > MaxKnownSchemaVersion)
            {
                throw new SchemaTooNewException(
                    $"'{options.DisplayName}' was created or upgraded by a newer version of MyMoney "
                    + $"(schema version {version}); this one understands up to version "
                    + $"{MaxKnownSchemaVersion}. Update MyMoney to open it.");
            }

            return version;
        }

        private static int ReadSchemaVersion(SQLiteConnection connection)
        {
            using (var exists = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__SchemaHistory';",
                connection))
            {
                if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                {
                    return 0;
                }
            }

            using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(Version), 0) FROM __SchemaHistory;", connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }
}
