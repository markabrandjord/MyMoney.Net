using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    /// <summary>
    /// Shared test-database cleanup for every SQL Server-backed test fixture that runs against the
    /// shared, persistent "MyMoney" database on Redmond (unlike SqliteDatabaseContractTests' fresh-
    /// temp-file-per-test, there's no "fresh database per test" here - see
    /// docs/superpowers/specs/2026-09-15-sql-server-user-tier-crud-design.md's "Verification"
    /// section).
    /// </summary>
    public static class SqlServerTestDatabase
    {
        private static readonly string[] TablesToWipe =
        {
            "Splits", "Investments", "Transactions", "TransactionExtras", "StockSplits", "Aliases",
            "AccountAliases", "LoanPayments", "RentUnits", "RentBuildings",
            "Accounts", "OnlineAccounts", "Securities", "Currencies", "Categories", "Payees"
        };

        /// <summary>
        /// Tables with a `MyMoneyTest`-tier `_Test_Reset` stored proc (the 11 IAggregateRoot tables
        /// Phase 2c added these for - see docs/superpowers/plans/
        /// 2026-09-17-persistence-concurrency-phase2c.md, Task 14). The remaining 5 tables in
        /// TablesToWipe (Splits, Investments, TransactionExtras, AccountAliases, RentUnits - owned-
        /// child/non-aggregate-root tables) have no such proc and still use the raw admin DELETE
        /// below; TablesToWipe's existing FK-safe order is preserved regardless of which path a
        /// given table takes.
        /// </summary>
        private static readonly Dictionary<string, string> TestResetProcs = new Dictionary<string, string>
        {
            ["Categories"] = "dbo.Categories_Test_Reset",
            ["Currencies"] = "dbo.Currencies_Test_Reset",
            ["OnlineAccounts"] = "dbo.OnlineAccounts_Test_Reset",
            ["Accounts"] = "dbo.Accounts_Test_Reset",
            ["Payees"] = "dbo.Payees_Test_Reset",
            ["Aliases"] = "dbo.Aliases_Test_Reset",
            ["Securities"] = "dbo.Securities_Test_Reset",
            ["StockSplits"] = "dbo.StockSplits_Test_Reset",
            ["LoanPayments"] = "dbo.LoanPayments_Test_Reset",
            ["RentBuildings"] = "dbo.RentBuildings_Test_Reset",
            ["Transactions"] = "dbo.Transactions_Test_Reset",
        };

        /// <summary>
        /// Null if a TestDatabase: true SQL Server entry is registered;
        /// otherwise an explanatory message for a caller's Assert.Ignore.
        /// </summary>
        public static string GetSkipReasonIfNotConfigured()
        {
            var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
            bool hasTestEntry = registry.Databases.Values.Any(e => e.Engine == DataEngineType.SqlServer && e.TestDatabase);
            return hasTestEntry
                ? null
                : "No SQL Server database in the registry is flagged TestDatabase: true -- register one via File | New | SQL Server... with \"Test database:\" checked.";
        }

        /// <summary>
        /// MyMoneyUser-tier connection string for the registered test
        /// database. Throws (does not return null) if none is configured --
        /// callers that want to skip instead must check
        /// GetSkipReasonIfNotConfigured() first, matching
        /// SqlServerStoredProcDatabaseTests' existing pattern.
        /// </summary>
        public static string GetUserConnectionString()
        {
            var (registry, entry) = GetTestEntryOrThrow();
            return registry.BuildConnectionString(entry, DatabaseRole.User);
        }

        /// <summary>
        /// Deletes every row from every table in the registered test
        /// database. Fails fast (does not skip) if no TestDatabase: true
        /// SQL Server entry is registered -- same explicit, unskippable
        /// safety posture the old env-var guard had, just sourced from the
        /// registry's TestDatabase flag instead of a separate ack variable.
        /// </summary>
        public static void WipeAllTables()
        {
            var (registry, entry) = GetTestEntryOrThrow();
            string adminConnectionString = registry.BuildConnectionString(entry, DatabaseRole.Admin);
            string testConnectionString = registry.BuildConnectionString(entry, DatabaseRole.Test);

            using (var adminConnection = new SqlConnection(adminConnectionString))
            using (var testConnection = new SqlConnection(testConnectionString))
            {
                adminConnection.Open();
                testConnection.Open();

                foreach (string table in TablesToWipe)
                {
                    if (TestResetProcs.TryGetValue(table, out string procName))
                    {
                        using (var command = new SqlCommand(procName, testConnection) { CommandType = CommandType.StoredProcedure })
                        {
                            command.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var command = new SqlCommand($"DELETE FROM dbo.[{table}];", adminConnection))
                        {
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        private static (DatabaseRegistry registry, DatabaseEntry entry) GetTestEntryOrThrow()
        {
            var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
            var entry = registry.Databases.Values.FirstOrDefault(e => e.Engine == DataEngineType.SqlServer && e.TestDatabase);
            if (entry == null)
            {
                throw new InvalidOperationException(
                    "No SQL Server database in the registry is flagged TestDatabase: true. Register one via File | New | SQL Server... with \"Test database:\" checked before running SQL-Server-backed tests.");
            }
            return (registry, entry);
        }
    }
}
