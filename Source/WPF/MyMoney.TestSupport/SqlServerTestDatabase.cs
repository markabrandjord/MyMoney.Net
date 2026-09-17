using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Walkabout.Data;
using Walkabout.Utilities;

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
        public const string AdminConnectionEnvVar = "MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION";
        public const string DestructiveWipeAckEnvVar = "MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE";

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
        /// Deletes every row from every table in the target database. For the 11 tables with a
        /// MyMoneyTest-tier `_Test_Reset` proc, calls that proc via a connection built from the same
        /// two JSON config files the real app already reads (dataengine.config.json,
        /// dataengine.credentials.json) - no environment variable for this connection, per project
        /// preference for JSON-based test configuration over env vars. The remaining 5 owned-child
        /// tables still use the MyMoneyAdmin connection's raw DELETE, since they have no aggregate-
        /// root proc of their own. Fails fast (does not skip) if MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1
        /// or the admin connection string aren't set, or if dataengine.config.json/
        /// dataengine.credentials.json are missing/inconsistent with the admin connection's target -
        /// see DestructiveWipeAckEnvVar's own callers for why this is an explicit, unskippable
        /// human-set acknowledgment rather than a default-on convenience.
        /// </summary>
        public static void WipeAllTables()
        {
            string ack = Environment.GetEnvironmentVariable(DestructiveWipeAckEnvVar);
            if (ack != "1")
            {
                throw new InvalidOperationException(
                    $"{DestructiveWipeAckEnvVar}=1 must be set to acknowledge that this will " +
                    "unconditionally DELETE all rows from every table in the target database before each test. " +
                    "This is a safety guard against accidentally running SQL-Server-backed tests against a real, populated database.");
            }

            string adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionEnvVar);
            if (string.IsNullOrEmpty(adminConnectionString))
            {
                throw new InvalidOperationException(
                    $"{AdminConnectionEnvVar} must be set to a MyMoneyAdmin connection string so SQL-Server-backed tests can " +
                    "clean up the shared test database between runs.");
            }

            string configPath = System.IO.Path.Combine(ProcessHelper.AppDataPath, "dataengine.config.json");
            DataEngineConfig config = DataEngineConfig.Load(configPath);
            MyMoneyTestConnectionResolver.ValidateMatchesAdminConnection(adminConnectionString, config);

            DataEngineCredentialStore credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            DataEngineCredential testCredential = credentialStore.GetCredential("MyMoneyTest");
            string testConnectionString = MyMoneyTestConnectionResolver.BuildConnectionString(config, testCredential);

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
    }
}
