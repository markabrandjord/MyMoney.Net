using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    public class BootstrapRunner
    {
        /// <summary>
        /// The bootstrap SQL script hardcodes the literal database name
        /// "MyMoney" (CREATE DATABASE MyMoney), and every schema/access/test
        /// script hardcodes "USE MyMoney". The databaseName parameter below
        /// is accepted (and threaded through to MyMoneyAdmin's reconnect as
        /// InitialCatalog) but is never substituted into the actual SQL, so
        /// any value other than this exact literal would fail confusingly
        /// mid-bootstrap. Validate up front instead.
        /// </summary>
        public const string RequiredDatabaseName = "MyMoney";

        private readonly string sqlScriptsRoot;

        public BootstrapRunner(string sqlScriptsRoot)
        {
            this.sqlScriptsRoot = sqlScriptsRoot;
        }

        public bool Run(string server, string databaseName)
        {
            if (!string.Equals(databaseName, RequiredDatabaseName, StringComparison.Ordinal))
            {
                Console.WriteLine($"Unsupported database name '{databaseName}'. The bootstrap SQL scripts hardcode the database name '{RequiredDatabaseName}' (CREATE DATABASE {RequiredDatabaseName}, USE {RequiredDatabaseName}, ...), so only '{RequiredDatabaseName}' is supported. Pass '{RequiredDatabaseName}' as the database name.");
                return false;
            }

            if (!SaBootstrapConnection.TryConnect(server, out string saPassword))
            {
                Console.WriteLine("Bootstrap cancelled.");
                return false;
            }

            // Re-bootstrapping an already-set-up database (e.g. retrying
            // after a deployment-step failure) must not rotate passwords
            // that a working credentials file already holds -- the bootstrap
            // script's ALTER LOGIN branch would change the server-side
            // password before the deployment scripts run, and if any of
            // those then fails, the credentials file (only written at the
            // very end, see below) keeps the OLD password while the server
            // now has the NEW one, permanently desyncing the two. Reusing
            // existing passwords when all three are already present makes
            // ALTER LOGIN a true no-op, so a failed re-run stays harmless.
            var existingCredentials = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath()).Load();
            bool haveAllThree = existingCredentials.ContainsKey("MyMoneyAdmin")
                && existingCredentials.ContainsKey("MyMoneyUser")
                && existingCredentials.ContainsKey("MyMoneyTest");

            string adminPassword = haveAllThree ? existingCredentials["MyMoneyAdmin"].Password : DataEnginePasswordGenerator.Generate();
            string userPassword = haveAllThree ? existingCredentials["MyMoneyUser"].Password : DataEnginePasswordGenerator.Generate();
            string testPassword = haveAllThree ? existingCredentials["MyMoneyTest"].Password : DataEnginePasswordGenerator.Generate();

            string bootstrapScript = File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Bootstrap", "CreateDatabaseAndLogins.sql"))
                .Replace("{{AdminPassword}}", adminPassword.Replace("'", "''"))
                .Replace("{{UserPassword}}", userPassword.Replace("'", "''"))
                .Replace("{{TestPassword}}", testPassword.Replace("'", "''"));

            var saBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = "sa",
                Password = saPassword,
                InitialCatalog = "master",
                TrustServerCertificate = true,
                ConnectTimeout = 10
            };

            Console.WriteLine("Creating database and logins as 'sa'...");
            ExecuteBatchScript(saBuilder.ConnectionString, bootstrapScript);

            var adminBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = "MyMoneyAdmin",
                Password = adminPassword,
                InitialCatalog = databaseName,
                TrustServerCertificate = true,
                ConnectTimeout = 10
            };

            Console.WriteLine("Deploying schema and stored procedures as 'MyMoneyAdmin'...");
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Schema", "001_CreatePayeesTable.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Payees_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Test", "Payees_TestProcs.sql")));

            Console.WriteLine("Creating schema for Accounts/Categories/Currencies/Securities/StockSplits/Aliases/Transactions/Splits/Investments as 'MyMoneyAdmin'...");
            var adminDatabase = new SqlServerStoredProcDatabase
            {
                ConnectionStringOverride = adminBuilder.ConnectionString
            };
            adminDatabase.LazyCreateTables();
            adminDatabase.Disconnect();

            Console.WriteLine("Deploying issue #22 access procedures as 'MyMoneyAdmin'...");
            string[] accessProcs = { "Accounts_AccessProcs.sql", "Categories_AccessProcs.sql", "Currencies_AccessProcs.sql", "Securities_AccessProcs.sql", "StockSplits_AccessProcs.sql", "Aliases_AccessProcs.sql", "Transactions_AccessProcs.sql", "Splits_AccessProcs.sql", "Investments_AccessProcs.sql", "OnlineAccounts_AccessProcs.sql", "AccountAliases_AccessProcs.sql", "TransactionExtras_AccessProcs.sql", "LoanPayments_AccessProcs.sql", "RentUnits_AccessProcs.sql", "RentBuildings_AccessProcs.sql" };
            foreach (var procFile in accessProcs)
            {
                string procPath = Path.Combine(this.sqlScriptsRoot, "Access", procFile);
                ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(procPath));
            }

            // Credentials are written only after every deployment step has
            // succeeded -- writing them earlier (e.g. right after the sa-run
            // bootstrap script) would let DataEngineStartup.DatabaseExists
            // see a "MyMoneyUser" entry for a bootstrap that actually failed
            // partway through, permanently blocking retry (see WI #4).
            var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            var credentials = credentialStore.Load();
            credentials["MyMoneyAdmin"] = new DataEngineCredential { UserId = "MyMoneyAdmin", Password = adminPassword };
            credentials["MyMoneyUser"] = new DataEngineCredential { UserId = "MyMoneyUser", Password = userPassword };
            credentials["MyMoneyTest"] = new DataEngineCredential { UserId = "MyMoneyTest", Password = testPassword };
            credentialStore.Save(credentials);
            Console.WriteLine($"Wrote generated credentials to {DataEngineCredentialStore.GetDefaultPath()}");

            Console.WriteLine("Bootstrap complete.");
            return true;
        }

        /// <summary>
        /// Splits a script on GO batch separators (a plain-text convention,
        /// not parsed T-SQL) and executes each batch in turn -- ADO.NET has
        /// no native concept of GO, it's an SSMS/sqlcmd-only directive.
        /// </summary>
        private static void ExecuteBatchScript(string connectionString, string script)
        {
            string[] batches = script.Split(new[] { "\nGO", "\r\nGO" }, StringSplitOptions.RemoveEmptyEntries);
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (string batch in batches)
                {
                    string trimmed = batch.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }
                    using (var command = new SqlCommand(trimmed, connection) { CommandTimeout = 60 })
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
