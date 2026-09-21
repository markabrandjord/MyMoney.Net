using System;
using System.Data.SQLite;

namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// Open a connection and apply the pragma sequence.
    ///
    /// DELIBERATELY DUPLICATED from MyMoney.Data.Sqlite.Provisioning (copy A). Spec section 1's
    /// Recommendation rules out both a shared Common assembly and InternalsVisibleTo and accepts
    /// this ~40-line overlap instead; Task 22 enforces that this assembly does not reference the
    /// provisioning one. If this file ever grows past connection construction and open/close,
    /// that is the signal the tier boundary was drawn in the wrong place.
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
