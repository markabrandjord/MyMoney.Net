using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    public class BootstrapRunner
    {
        private readonly string sqlScriptsRoot;

        public BootstrapRunner(string sqlScriptsRoot)
        {
            this.sqlScriptsRoot = sqlScriptsRoot;
        }

        public bool Run(string server, string databaseName)
        {
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
                ConnectTimeout = 10
            };

            Console.WriteLine("Creating database and logins as 'sa'...");
            ExecuteBatchScript(saBuilder.ConnectionString, bootstrapScript);

            var credentials = new Dictionary<string, DataEngineCredential>
            {
                ["MyMoneyAdmin"] = new DataEngineCredential { UserId = "MyMoneyAdmin", Password = adminPassword },
                ["MyMoneyUser"] = new DataEngineCredential { UserId = "MyMoneyUser", Password = userPassword },
                ["MyMoneyTest"] = new DataEngineCredential { UserId = "MyMoneyTest", Password = testPassword }
            };
            var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            credentialStore.Save(credentials);
            Console.WriteLine($"Wrote generated credentials to {DataEngineCredentialStore.GetDefaultPath()}");

            var adminBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = "MyMoneyAdmin",
                Password = adminPassword,
                InitialCatalog = databaseName,
                ConnectTimeout = 10
            };

            Console.WriteLine("Deploying schema and stored procedures as 'MyMoneyAdmin'...");
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Schema", "001_CreatePayeesTable.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Payees_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Test", "Payees_TestProcs.sql")));

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
