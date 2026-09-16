using System;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    /// <summary>
    /// Proves the shared IDatabase contract suite (see DatabaseContractTests)
    /// passes against SqlServerStoredProcDatabase connected as MyMoneyUser --
    /// the tier the shipped app actually runs under (see issue #22's design
    /// spec). Deliberately does NOT skip/Ignore when SQL Server is
    /// unreachable: unlike SqlServerStoredProcDatabaseTests's env-var-driven
    /// smoke tests, a broken connection here is a real signal that
    /// MyMoneyUser CRUD coverage has regressed, not an expected local-dev
    /// absence. Do not add Assert.Ignore/skip logic to this fixture.
    ///
    /// Requires three environment variables: MYMONEY_TEST_SQLSERVER_USER_CONNECTION
    /// (MyMoneyUser tier, the database under test), MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION
    /// (MyMoneyAdmin tier, used only to wipe tables between runs), and
    /// MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1 -- an explicit safety
    /// acknowledgement that this fixture unconditionally issues
    /// DELETE FROM against every table in whatever database the admin
    /// connection string points at. The admin connection targets a
    /// database literally named "MyMoney", the same catalog name the
    /// shipped app uses, so a mis-set env var (or pointing this suite at
    /// the wrong server) could otherwise silently destroy real data. This
    /// third env var must be set to exactly "1" or the wipe -- and
    /// therefore every test -- fails fast instead of running.
    /// </summary>
    [TestFixture]
    public class SqlServerDatabaseContractTests : DatabaseContractTests
    {
        private const string UserConnectionEnvVar = "MYMONEY_TEST_SQLSERVER_USER_CONNECTION";
        private const string AdminConnectionEnvVar = "MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION";
        private const string DestructiveWipeAckEnvVar = "MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE";

        private static readonly string[] TablesToWipe =
        {
            "Splits", "Investments", "Transactions", "StockSplits", "Aliases",
            "Accounts", "Securities", "Currencies", "Categories", "Payees"
        };

        public override void SetUp()
        {
            WipeAllTables();
            base.SetUp();
        }

        protected override IDatabase CreateDatabase()
        {
            string connectionString = Environment.GetEnvironmentVariable(UserConnectionEnvVar);
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    $"{UserConnectionEnvVar} must be set to a MyMoneyUser connection string to run SqlServerDatabaseContractTests. " +
                    "This fixture intentionally fails rather than skips when SQL Server is unreachable.");
            }
            string catalogName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            return new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString, DatabasePath = catalogName };
        }

        private static void WipeAllTables()
        {
            string ack = Environment.GetEnvironmentVariable(DestructiveWipeAckEnvVar);
            if (ack != "1")
            {
                throw new InvalidOperationException(
                    $"{DestructiveWipeAckEnvVar}=1 must be set to acknowledge that SqlServerDatabaseContractTests will " +
                    "unconditionally DELETE all rows from every table in the target database before each test. " +
                    "This is a safety guard against accidentally running this fixture against a real, populated database.");
            }

            string adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionEnvVar);
            if (string.IsNullOrEmpty(adminConnectionString))
            {
                throw new InvalidOperationException(
                    $"{AdminConnectionEnvVar} must be set to a MyMoneyAdmin connection string so SqlServerDatabaseContractTests can " +
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
