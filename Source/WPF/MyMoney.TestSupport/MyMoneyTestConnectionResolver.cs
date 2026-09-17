using System;
using Microsoft.Data.SqlClient;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    /// <summary>
    /// Builds the MyMoneyTest-tier connection string from the same two JSON config files the real
    /// app already reads (dataengine.config.json for server/database, dataengine.credentials.json
    /// for the MyMoneyTest credential) - no new environment variable, per the user's explicit
    /// preference for JSON-based configuration over env vars. Deliberately scoped to only the new
    /// MyMoneyTest connection; the existing MYMONEY_TEST_SQLSERVER_USER_CONNECTION/
    /// MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION/MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE env vars are left
    /// untouched (a separate, already-tracked work item covers migrating those).
    /// </summary>
    public static class MyMoneyTestConnectionResolver
    {
        public static string BuildConnectionString(DataEngineConfig config, DataEngineCredential credential)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (credential == null)
            {
                throw new ArgumentNullException(nameof(credential));
            }
            if (string.IsNullOrEmpty(config.Server) || string.IsNullOrEmpty(config.Database))
            {
                throw new InvalidOperationException(
                    "dataengine.config.json is missing Server/Database - cannot build the MyMoneyTest connection string.");
            }

            return $"Data Source={config.Server};Initial Catalog={config.Database};User ID={credential.UserId};Password={credential.Password};TrustServerCertificate=True";
        }

        /// <summary>
        /// dataengine.config.json (used to build the new MyMoneyTest connection) and
        /// MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION (used for the tables that still need raw admin
        /// DELETEs) are two independent configuration sources that are supposed to point at the same
        /// database but aren't guaranteed to - WipeAllTables() is destructive, so this fails loudly
        /// rather than risk wiping two different databases in one call.
        /// </summary>
        public static void ValidateMatchesAdminConnection(string adminConnectionString, DataEngineConfig config)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(adminConnectionString);
            if (!string.Equals(builder.DataSource, config.Server, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(builder.InitialCatalog, config.Database, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"dataengine.config.json points at '{config.Server}/{config.Database}' but " +
                    $"{SqlServerTestDatabase.AdminConnectionEnvVar} points at '{builder.DataSource}/{builder.InitialCatalog}' - " +
                    "refusing to wipe tables against two different configured databases. Make sure " +
                    "dataengine.config.json and the admin connection string agree on the same server/database.");
            }
        }
    }
}
