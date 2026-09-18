using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    public class SqlServerBootstrapper
    {
        private readonly string sqlScriptsRoot;

        private static readonly string[] AccessProcFiles =
        {
            "Payees_AccessProcs.sql", "Accounts_AccessProcs.sql", "Categories_AccessProcs.sql",
            "Currencies_AccessProcs.sql", "Securities_AccessProcs.sql", "StockSplits_AccessProcs.sql",
            "Aliases_AccessProcs.sql", "Transactions_AccessProcs.sql", "Splits_AccessProcs.sql",
            "Investments_AccessProcs.sql", "OnlineAccounts_AccessProcs.sql", "AccountAliases_AccessProcs.sql",
            "TransactionExtras_AccessProcs.sql", "LoanPayments_AccessProcs.sql", "RentUnits_AccessProcs.sql",
            "RentBuildings_AccessProcs.sql"
        };

        public SqlServerBootstrapper(string sqlScriptsRoot)
        {
            this.sqlScriptsRoot = sqlScriptsRoot;
        }

        public bool BootstrapServerIfNeeded(DatabaseRegistry registry, string server, ISaCredentialPrompt saPrompt, out string error)
        {
            error = null;
            if (registry.Servers.ContainsKey(server))
            {
                return true;
            }

            if (!saPrompt.TryGetSaPassword(server, out string saPassword))
            {
                error = "Bootstrap cancelled.";
                return false;
            }

            string adminPassword = DataEnginePasswordGenerator.Generate();
            string userPassword = DataEnginePasswordGenerator.Generate();
            string testPassword = DataEnginePasswordGenerator.Generate();

            var saBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = "sa",
                Password = saPassword,
                InitialCatalog = "master",
                TrustServerCertificate = true,
                ConnectTimeout = 10
            };

            try
            {
                // Both permanent procs are deployed here, as 'sa' -- not
                // just MyMoney_BootstrapServer. MyMoneyAdmin's dbcreator
                // server role (granted by MyMoney_BootstrapServer itself)
                // lets it CREATE DATABASE, but NOT create/alter procedures
                // in master -- confirmed live against Redmond: deploying
                // MyMoney_CreateCatalog as MyMoneyAdmin from CreateCatalog()
                // failed with "CREATE PROCEDURE permission denied in
                // database 'master'". Only 'sa' (or a login with master DDL
                // rights) can deploy it, so it happens once here, and
                // CreateCatalog() below only ever calls the already-
                // deployed proc, never redeploys it.
                //
                // Ordering matters: MyMoney_CreateCatalog.sql's own deploy
                // script creates a master-scoped database user for
                // MyMoneyAdmin and grants it EXECUTE -- which requires the
                // MyMoneyAdmin LOGIN to already exist. That login is only
                // created when MyMoney_BootstrapServer is actually EXECUTED
                // (not merely deployed), so that EXEC must happen before
                // MyMoney_CreateCatalog.sql is deployed, confirmed live
                // against Redmond (got "Cannot find the user 'MyMoneyAdmin'"
                // when this ran in the wrong order).
                DeployScript(saBuilder.ConnectionString, Path.Combine(this.sqlScriptsRoot, "Bootstrap", "MyMoney_BootstrapServer.sql"));
                using (var connection = new SqlConnection(saBuilder.ConnectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand("master.dbo.MyMoney_BootstrapServer", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                    {
                        command.Parameters.AddWithValue("@AdminPassword", adminPassword);
                        command.Parameters.AddWithValue("@UserPassword", userPassword);
                        command.Parameters.AddWithValue("@TestPassword", testPassword);
                        command.ExecuteNonQuery();
                    }
                }
                DeployScript(saBuilder.ConnectionString, Path.Combine(this.sqlScriptsRoot, "Bootstrap", "MyMoney_CreateCatalog.sql"));
            }
            catch (Exception ex)
            {
                error = $"Could not bootstrap server '{server}' as 'sa': {ex.Message}";
                return false;
            }

            // sa's password is never persisted (Global Constraints) -- only
            // the three generated role passwords go into the registry.
            registry.Servers[server] = new DatabaseServerEntry
            {
                MyMoneyAdmin = new DatabaseCredential { UserId = "MyMoneyAdmin", Password = adminPassword },
                MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = userPassword },
                MyMoneyTest = new DatabaseCredential { UserId = "MyMoneyTest", Password = testPassword }
            };
            registry.Save();
            return true;
        }

        public bool CreateCatalog(DatabaseRegistry registry, string server, string catalogName, bool testDatabase, out string error)
        {
            error = null;
            if (!registry.Servers.TryGetValue(server, out DatabaseServerEntry serverEntry))
            {
                error = $"Server '{server}' has not been bootstrapped yet.";
                return false;
            }

            var adminBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = serverEntry.MyMoneyAdmin.UserId,
                Password = serverEntry.MyMoneyAdmin.Password,
                InitialCatalog = "master",
                TrustServerCertificate = true,
                ConnectTimeout = 10
            };

            try
            {
                // MyMoney_CreateCatalog is already deployed by
                // BootstrapServerIfNeeded (as 'sa' -- see the comment
                // there); MyMoneyAdmin can call it but cannot redeploy it.
                using (var connection = new SqlConnection(adminBuilder.ConnectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand("master.dbo.MyMoney_CreateCatalog", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                    {
                        command.Parameters.AddWithValue("@CatalogName", catalogName);
                        command.ExecuteNonQuery();
                    }
                }

                var catalogBuilder = new SqlConnectionStringBuilder(adminBuilder.ConnectionString) { InitialCatalog = catalogName };

                var adminDatabase = new SqlServerStoredProcDatabase { ConnectionStringOverride = catalogBuilder.ConnectionString };
                adminDatabase.LazyCreateTables();
                adminDatabase.Disconnect();

                // Migrations must run before Access procs: confirmed live
                // against Redmond that CREATE OR ALTER PROCEDURE validates
                // column references against EXISTING tables eagerly (unlike
                // deferred name resolution for objects that don't exist at
                // all yet) -- deploying Payees_AccessProcs.sql before the
                // Version column migration failed with "Invalid column
                // name 'Version'". Migrations/*.sql were previously "run
                // once, by hand" against the one pre-existing "MyMoney"
                // catalog (see their own header comments) -- this WI is the
                // first automated fresh-catalog creation path, so it must
                // replay every migration to bring a brand-new catalog's
                // schema up to date, not just LazyCreateTables()'s baseline.
                // Each migration file's own T-SQL idempotency guards (e.g.
                // "IF COL_LENGTH(...) IS NULL BEGIN ALTER TABLE ... END")
                // are trusted as-is via the same ExecuteBatchScript used for
                // every other script -- an earlier apparent failure of that
                // guard turned out to be ExecuteBatchScript's now-fixed
                // shared-connection bug (see its comment below), not a
                // T-SQL problem, confirmed live against Redmond.
                string migrationsDir = Path.Combine(this.sqlScriptsRoot, "Migrations");
                if (Directory.Exists(migrationsDir))
                {
                    foreach (string migrationFile in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f))
                    {
                        ExecuteBatchScript(catalogBuilder.ConnectionString, File.ReadAllText(migrationFile));
                    }
                }

                foreach (string procFile in AccessProcFiles)
                {
                    string script = File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", procFile));
                    ExecuteBatchScript(catalogBuilder.ConnectionString, script);
                }

                if (testDatabase)
                {
                    // Test/_Test_Reset procs are destructive-wipe capable --
                    // only ever deployed into a catalog the caller flagged
                    // as a test database (see this task's Safety decision).
                    foreach (string testFile in Directory.GetFiles(Path.Combine(this.sqlScriptsRoot, "Test"), "*.sql"))
                    {
                        ExecuteBatchScript(catalogBuilder.ConnectionString, File.ReadAllText(testFile));
                    }
                }
            }
            catch (Exception ex)
            {
                error = $"Could not create catalog '{catalogName}' on '{server}': {ex.Message}";
                return false;
            }

            return true;
        }

        private static void DeployScript(string connectionString, string scriptPath)
        {
            ExecuteBatchScript(connectionString, File.ReadAllText(scriptPath));
        }

        /// <summary>
        /// Ported from MyMoneyAdmin's BootstrapRunner.ExecuteBatchScript,
        /// plus skipping any batch matching ^USE\b -- the connection is
        /// already scoped to the right catalog via InitialCatalog, so the
        /// checked-in scripts' leading "USE &lt;db&gt;;" line (kept for a human
        /// running them by hand in SSMS) is dead weight for us.
        /// </summary>
        private static void ExecuteBatchScript(string connectionString, string script)
        {
            // A fresh connection per batch, not one connection reused
            // across every batch in the file: confirmed live against
            // Redmond that reusing one connection across multiple
            // CREATE PROCEDURE/GRANT statements in a loop left the target
            // catalog with zero procs afterward -- no exception anywhere,
            // every ExecuteNonQuery "succeeded", but nothing persisted.
            // Isolating each statement to its own connection (matching
            // DeployMigrationAlterStatements' proven-working pattern)
            // resolved it completely -- all 16 Access proc files' worth of
            // CREATE PROCEDURE/GRANT statements now deploy and persist
            // correctly every time. Root cause not fully isolated (a
            // single connection running two statements in isolation did
            // work in testing, so this isn't simply "shared connections
            // never work") -- this is the empirically verified fix, not a
            // guess.
            foreach (string batch in SplitBatches(script))
            {
                using (var connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand(batch, connection) { CommandTimeout = 60 })
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// GO is a client-tool batch separator (SSMS/sqlcmd), not T-SQL --
        /// ADO.NET has no concept of it. Per SSMS's own rule, a GO must
        /// appear alone on its own line (whitespace aside) to count as a
        /// separator; splitting on the substring "\nGO" instead (as a
        /// naive port of this logic once did) misses a GO on the very
        /// first line, since it has no preceding newline to match against.
        /// </summary>
        // \r? before the multiline $ matters: .NET's $ in Multiline mode
        // matches right before \n only, so on \r\n line endings a trailing
        // \r would otherwise sit between "GO" and $, failing the match.
        private static readonly Regex GoSeparator = new Regex(@"^[ \t]*GO[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        internal static string[] SplitBatches(string script)
        {
            string[] rawBatches = GoSeparator.Split(script);
            var batches = new System.Collections.Generic.List<string>();
            foreach (string raw in rawBatches)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0 || Regex.IsMatch(trimmed, @"^USE\b", RegexOptions.IgnoreCase))
                {
                    continue;
                }
                batches.Add(trimmed);
            }
            return batches.ToArray();
        }
    }
}
