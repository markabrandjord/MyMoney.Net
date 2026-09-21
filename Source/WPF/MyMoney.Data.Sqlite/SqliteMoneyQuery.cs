using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Text.Json;

namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// The SQLite implementation of the application's read path. Filtering happens in SQL, not in
    /// memory - spec section 3.3 stage 1, replacing today's "load everything then filter in C#".
    ///
    /// Everything it returns is a projection: no RowVersion, not an IAggregateRoot, so it cannot
    /// be handed to any write method. The business layer never names a view or a table here - it
    /// issues a typed AccountQuery and this class decides how to answer it (spec section 2.7.2
    /// point 4). When view-backed shapes arrive they land HERE, behind this same signature.
    ///
    /// Filters are bound parameters throughout, including the id list, which travels as one JSON
    /// parameter through json_each rather than a concatenated IN list - R-CRUD-1.
    /// </summary>
    public sealed class SqliteMoneyQuery : IMoneyQuery
    {
        private const int ClosedFlag = (int)AccountFlags.Closed;

        private readonly SQLiteConnection connection;

        private SqliteMoneyQuery(SQLiteConnection connection)
        {
            this.connection = connection;
        }

        public static SqliteMoneyQuery OpenOver(SQLiteConnection connection) =>
            new SqliteMoneyQuery(connection ?? throw new ArgumentNullException(nameof(connection)));

        public IReadOnlyList<AccountRow> ListAccounts(AccountQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            string sql =
                "SELECT Id, Name, Type, Currency, OpeningBalance, COALESCE(Flags, 0) AS Flags FROM Accounts "
                + "WHERE (@AllIds = 1 OR Id IN (SELECT value FROM json_each(@Ids))) "
                + "AND (@IncludeClosed = 1 OR (COALESCE(Flags, 0) & @ClosedFlag) = 0) "
                + "ORDER BY Id;";

            var rows = new List<AccountRow>();
            using (var cmd = new SQLiteCommand(sql, this.connection))
            {
                cmd.Parameters.AddWithValue("@AllIds", query.AccountIds.Count == 0 ? 1 : 0);
                cmd.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(query.AccountIds));
                cmd.Parameters.AddWithValue("@IncludeClosed", query.IncludeClosed ? 1 : 0);
                cmd.Parameters.AddWithValue("@ClosedFlag", ClosedFlag);

                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long flags = reader.GetInt64(5);
                        rows.Add(new AccountRow(
                            reader.GetInt64(0),
                            reader.IsDBNull(1) ? null : reader.GetString(1),
                            (AccountType)reader.GetInt64(2),
                            reader.IsDBNull(3) ? null : reader.GetString(3),
                            MoneyScale.FromStorage(reader.GetInt64(4)),
                            (flags & ClosedFlag) != 0));
                    }
                }
            }

            return rows;
        }

        public long NextAccountId()
        {
            using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(Id), 0) + 1 FROM Accounts;", this.connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }
}
