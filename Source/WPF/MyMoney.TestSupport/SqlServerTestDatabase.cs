using System;
using Microsoft.Data.SqlClient;

namespace Walkabout.TestSupport
{
    /// <summary>
    /// Shared test-database cleanup for every SQL Server-backed test fixture that runs against the
    /// shared, persistent "MyMoney" database on Redmond (unlike SqliteDatabaseContractTests' fresh-
    /// temp-file-per-test, there's no "fresh database per test" here - see
    /// docs/superpowers/specs/2026-09-15-sql-server-user-tier-crud-design.md's "Verification"
    /// section). Extracted out of SqlServerDatabaseContractTests so SqlServerStoredProcDatabaseTests
    /// (Source/WPF/UnitTests) can share it too, instead of having no cleanup at all - see the
    /// short-term-fix note in the WI tracking the real fix (a tiered-security-appropriate
    /// test-support stored proc, not this admin-connection raw DELETE).
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
        /// Unconditionally deletes every row from every table in the target database, using the
        /// MyMoneyAdmin connection (direct table access - MyMoneyUser/MyMoneyTest are EXECUTE-only,
        /// which is exactly the tier the tests calling this method are trying to prove out, so
        /// admin access is needed for cleanup regardless). Fails fast (does not skip) if
        /// MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1 or the admin connection string aren't set - see
        /// DestructiveWipeAckEnvVar's own callers for why this is an explicit, unskippable
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

            using (var connection = new SqlConnection(adminConnectionString))
            {
                connection.Open();
                foreach (string table in TablesToWipe)
                {
                    using (var command = new SqlCommand($"DELETE FROM dbo.[{table}];", connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
