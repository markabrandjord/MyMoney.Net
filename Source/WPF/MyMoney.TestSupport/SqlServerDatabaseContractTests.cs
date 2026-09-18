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
    /// unreachable: unlike SqlServerStoredProcDatabaseTests's skip-when-
    /// unconfigured smoke tests, a broken connection here is a real signal
    /// that MyMoneyUser CRUD coverage has regressed, not an expected
    /// local-dev absence. Do not add Assert.Ignore/skip logic to this
    /// fixture.
    ///
    /// Requires a TestDatabase: true SQL Server entry in the DatabaseRegistry
    /// (see issue #32) -- register one via File | New | SQL Server... with
    /// "Test database:" checked. SqlServerTestDatabase.WipeAllTables()
    /// throws (does not skip) if none is registered, which is what makes
    /// this fixture fail rather than silently pass when unconfigured.
    /// </summary>
    [TestFixture]
    [Category("RequiresSqlServer")]
    public class SqlServerDatabaseContractTests : DatabaseContractTests
    {
        public override void SetUp()
        {
            SqlServerTestDatabase.WipeAllTables();
            base.SetUp();
        }

        protected override IDatabase CreateDatabase()
        {
            string connectionString = SqlServerTestDatabase.GetUserConnectionString();
            string catalogName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            return new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString, DatabasePath = catalogName };
        }
    }
}
