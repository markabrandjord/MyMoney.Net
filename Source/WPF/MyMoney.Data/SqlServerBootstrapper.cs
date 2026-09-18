using System;
using System.IO;
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
                DeployScript(adminBuilder.ConnectionString, Path.Combine(this.sqlScriptsRoot, "Bootstrap", "MyMoney_CreateCatalog.sql"));
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
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (string batch in SplitBatches(script))
                {
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
