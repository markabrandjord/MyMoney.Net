using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// Account &lt;-&gt; storage, in ONE place. Every encoding decision the schema depends on lives
    /// here: money as INTEGER ten-thousandths (MoneyScale), DateTime as TEXT
    /// 'yyyy-MM-dd HH:mm:ss.fff', Guid as TEXT "D".
    ///
    /// OnlineAccount / CategoryIdForPrincipal / CategoryIdForInterest are written from the
    /// Account's object references, which no Plan A code path can make non-null because no slice
    /// in 1-7 produces an OnlineAccount or Category root. The null round trip is therefore
    /// LOSSLESS HERE and only here: the slice that brings those roots must revisit this file at
    /// the same time, or a save will null out a real foreign key.
    /// </summary>
    public static class AccountRowCodec
    {
        public const string Columns =
            "Id,AccountId,OfxAccountId,Name,Type,Description,OnlineAccount,OpeningBalance,LastSync," +
            "LastBalance,SyncGuid,Flags,Currency,WebSite,ReconcileWarning,CategoryIdForPrincipal," +
            "CategoryIdForInterest";

        private static readonly string[] ColumnNames = Columns.Split(',');

        private const string DateFormat = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>The SELECT list for the json_each insert: one json_extract per column.</summary>
        public static string JsonExtractList()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ColumnNames.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append("json_extract(value, '$.").Append(ColumnNames[i]).Append("')");
            }

            return sb.ToString();
        }

        public static string ToJsonArray(IReadOnlyList<Account> accounts)
        {
            var rows = new List<Dictionary<string, object>>(accounts.Count);
            foreach (Account a in accounts)
            {
                rows.Add(ToDictionary(a));
            }

            return JsonSerializer.Serialize(rows);
        }

        public static string IdsJsonArray(IReadOnlyList<Account> accounts)
        {
            var ids = new List<long>(accounts.Count);
            foreach (Account a in accounts)
            {
                ids.Add(a.Id);
            }

            return JsonSerializer.Serialize(ids);
        }

        public static Dictionary<string, object> ToDictionary(Account a) => new Dictionary<string, object>
        {
            { "Id", a.Id },
            { "AccountId", a.AccountId },
            { "OfxAccountId", a.OfxAccountId },
            { "Name", a.Name },
            { "Type", (int)a.Type },
            { "Description", a.Description },
            { "OnlineAccount", a.OnlineAccount?.Id },
            { "OpeningBalance", MoneyScale.ToStorage(a.OpeningBalance) },
            { "LastSync", ToText(a.LastSync) },
            { "LastBalance", ToText(a.LastBalance) },
            { "SyncGuid", a.SyncGuid.IsNull ? null : a.SyncGuid.Value.ToString("D") },
            { "Flags", (int)a.Flags },
            { "Currency", a.Currency },
            { "WebSite", a.WebSite },
            { "ReconcileWarning", a.ReconcileWarning },
            { "CategoryIdForPrincipal", a.CategoryForPrincipal?.Id },
            { "CategoryIdForInterest", a.CategoryForInterest?.Id },
        };

        /// <summary>Binds every column as @Name, for the per-root UPDATE path (Task 13).</summary>
        public static void AddParameters(SQLiteCommand cmd, Account a)
        {
            foreach (KeyValuePair<string, object> pair in ToDictionary(a))
            {
                cmd.Parameters.AddWithValue("@" + pair.Key, pair.Value ?? DBNull.Value);
            }
        }

        /// <summary>Materialises one row as an Account parented to <paramref name="container"/>.</summary>
        public static Account Read(SQLiteDataReader reader, Accounts container)
        {
            var a = new Account(container)
            {
                // Account.Id is int while the column is INTEGER(64) because IAggregateRoot.Id is
                // long; checked() makes a value that truly does not fit loud instead of silent.
                Id = checked((int)reader.GetInt64(reader.GetOrdinal("Id"))),
                AccountId = GetString(reader, "AccountId"),
                OfxAccountId = GetString(reader, "OfxAccountId"),
                Name = GetString(reader, "Name"),
                Type = (AccountType)reader.GetInt64(reader.GetOrdinal("Type")),
                Description = GetString(reader, "Description"),
                OpeningBalance = MoneyScale.FromStorage(reader.GetInt64(reader.GetOrdinal("OpeningBalance"))),
                Currency = GetString(reader, "Currency"),
                WebSite = GetString(reader, "WebSite"),
            };

            DateTime? lastSync = FromText(GetString(reader, "LastSync"));
            if (lastSync.HasValue)
            {
                a.LastSync = lastSync.Value;
            }

            DateTime? lastBalance = FromText(GetString(reader, "LastBalance"));
            if (lastBalance.HasValue)
            {
                a.LastBalance = lastBalance.Value;
            }

            string syncGuid = GetString(reader, "SyncGuid");
            if (!string.IsNullOrEmpty(syncGuid))
            {
                a.SyncGuid = new SqlGuid(Guid.Parse(syncGuid));
            }

            int flagsOrdinal = reader.GetOrdinal("Flags");
            if (!reader.IsDBNull(flagsOrdinal))
            {
                a.Flags = (AccountFlags)reader.GetInt64(flagsOrdinal);
            }

            int warnOrdinal = reader.GetOrdinal("ReconcileWarning");
            if (!reader.IsDBNull(warnOrdinal))
            {
                a.ReconcileWarning = checked((int)reader.GetInt64(warnOrdinal));
            }

            a.RowVersion = reader.GetInt64(reader.GetOrdinal("Version"));

            // A freshly constructed PersistentObject starts at ChangeType.Inserted, and every
            // setter above fired OnChanged. OnUpdated() puts a loaded root at None, which is what
            // makes the NEXT SaveRoot on it an UPDATE rather than an INSERT.
            a.OnUpdated();
            return a;
        }

        private static string GetString(SQLiteDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static string ToText(DateTime value) =>
            value == DateTime.MinValue ? null : value.ToString(DateFormat, CultureInfo.InvariantCulture);

        private static DateTime? FromText(string value) =>
            string.IsNullOrEmpty(value)
                ? (DateTime?)null
                : DateTime.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
    }
}
