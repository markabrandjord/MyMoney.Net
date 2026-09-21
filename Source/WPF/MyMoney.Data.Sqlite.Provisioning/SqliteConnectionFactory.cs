using System;
using System.Data.SQLite;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// Open a connection and apply the pragma sequence.
    ///
    /// DELIBERATELY DUPLICATED in MyMoney.Data.Sqlite (copy B). Spec section 1's Recommendation
    /// is explicit: do NOT create a shared MyMoney.Data.Sqlite.Common assembly and do NOT use
    /// InternalsVisibleTo; accept the ~40 lines of overlap between store and provisioner instead.
    /// Task 22 adds a Tier-0 test that the store assembly does not reference this one, so the
    /// duplication is enforced rather than merely intended. If this file ever grows past
    /// connection construction and open/close, that is the signal the tier boundary was drawn in
    /// the wrong place - move the boundary, do not punch through it (spec section 6.2).
    /// </summary>
    public static class SqliteConnectionFactory
    {
        public const string InMemoryDataSource = ":memory:";

        public static bool IsInMemory(string dataSource) =>
            string.Equals(dataSource, InMemoryDataSource, StringComparison.OrdinalIgnoreCase);

        public static SQLiteConnection Open(string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                throw new ArgumentException("A SQLite data source is required.", nameof(dataSource));
            }

            var builder = new SQLiteConnectionStringBuilder { DataSource = dataSource };
            var connection = new SQLiteConnection(builder.ConnectionString);
            connection.Open();

            Execute(connection, "PRAGMA foreign_keys = ON;");

            // busy_timeout: a second writer retries for this long instead of failing immediately
            // with SQLITE_BUSY. 5000ms carried forward from SqliteDatabase.Connect().
            Execute(connection, "PRAGMA busy_timeout = 5000;");

            if (!IsInMemory(dataSource))
            {
                // WAL: readers never block writers, writers never block readers - the concurrency
                // model section 1.6a's human-plus-agent goal needs. NOT applicable to :memory:,
                // which reports "memory"; asking for it there is not an error, but demanding the
                // answer be "wal" would be. Spec section 2.4's known gotcha.
                Execute(connection, "PRAGMA journal_mode = WAL;");
            }

            return connection;
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }
    }
}
