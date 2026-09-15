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

            string adminPassword = DataEnginePasswordGenerator.Generate();
            string userPassword = DataEnginePasswordGenerator.Generate();
            string testPassword = DataEnginePasswordGenerator.Generate();

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
