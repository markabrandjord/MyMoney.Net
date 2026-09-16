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
    /// </summary>
    [TestFixture]
    public class SqlServerDatabaseContractTests : DatabaseContractTests
    {
        private const string UserConnectionEnvVar = "MYMONEY_TEST_SQLSERVER_USER_CONNECTION";
        private const string AdminConnectionEnvVar = "MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION";

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
            return new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString, DatabasePath = "MyMoney" };
        }

        private static void WipeAllTables()
        {
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
