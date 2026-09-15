using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
#if DEBUG
    public static class DataEngineStartup
    {
        /// <summary>
        /// The bootstrap scripts and every access/test stored procedure
        /// hardcode the literal database name "MyMoney" (CREATE DATABASE
        /// MyMoney, USE MyMoney, ...) -- see
        /// Database/SqlScripts/Bootstrap/CreateDatabaseAndLogins.sql and the
        /// Schema/Access/Test scripts. dataengine.config.json's "database"
        /// value is developer-editable, so we validate it matches before
        /// doing anything, rather than letting a mismatch fail confusingly
        /// mid-bootstrap or mid-connect.
        /// </summary>
        private const string RequiredDatabaseName = "MyMoney";

        /// <summary>
        /// Attempts to auto-load a SQL Server-backed MyMoney per
        /// dataengine.config.json. Returns false (with database/money both
        /// null) whenever the config says SQLite, is missing/invalid, or
        /// SQL Server auto-load fails for any reason -- in every such case
        /// the caller (MainWindow) is expected to fall through to its
        /// normal SQLite/File-menu-driven behavior instead of crashing or
        /// getting stuck.
        /// </summary>
        /// <param name="log">
        /// Optional sink for human-readable failure diagnostics, routed
        /// through the app's real logger by the caller (this class has no
        /// direct access to MainWindow's ILogger instance). Every failure
        /// is also written via Debug.WriteLine regardless, for parity with
        /// this method's original behavior when no logger is supplied.
        /// </param>
        public static bool TryAutoLoad(string configPath, out IDatabase database, out MyMoney money, Action<string> log = null)
        {
            database = null;
            money = null;

            DataEngineConfig config;
            try
            {
                config = DataEngineConfig.Load(configPath);
            }
            catch (Exception ex)
            {
                // A corrupt/malformed dataengine.config.json (bad JSON, I/O
                // error, etc.) must never crash app startup -- degrade to
                // "as if SQLite is configured" instead of propagating.
                LogFailure(log, $"DataEngineStartup: could not read '{configPath}', falling back to SQLite. {ex.Message}");
                return false;
            }

            if (config.Engine != DataEngineType.SqlServer || string.IsNullOrEmpty(config.Server) || string.IsNullOrEmpty(config.Database))
            {
                return false;
            }

            if (!string.Equals(config.Database, RequiredDatabaseName, StringComparison.Ordinal))
            {
                LogFailure(log, $"DataEngineStartup: configured database name '{config.Database}' is not supported -- the SQL Server bootstrap scripts hardcode the name '{RequiredDatabaseName}'. Set \"database\": \"{RequiredDatabaseName}\" in {configPath}.");
                return false;
            }

            if (!DatabaseExists(log))
            {
                if (!RunMyMoneyAdmin(config.Server, config.Database, log))
                {
                    return false;
                }
            }

            var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            DataEngineCredential userCredential;
            try
            {
                userCredential = credentialStore.GetCredential("MyMoneyUser");
            }
            catch (Exception ex)
            {
                // Widened from InvalidOperationException (the only type
                // GetCredential itself throws) to catch a malformed
                // credentials file too (JSON parse errors etc. can surface
                // from credentialStore.Load() before GetCredential gets a
                // chance to throw its own, more specific exception).
                LogFailure(log, $"DataEngineStartup: {ex.Message}");
                return false;
            }

            try
            {
                var builder = new SqlConnectionStringBuilder
                {
                    DataSource = config.Server,
                    InitialCatalog = config.Database,
                    UserID = userCredential.UserId,
                    Password = userCredential.Password,
                    TrustServerCertificate = true
                };

                var sqlServerDatabase = new SqlServerStoredProcDatabase { ConnectionStringOverride = builder.ConnectionString };
                money = sqlServerDatabase.Load(null);
                database = sqlServerDatabase;
                return true;
            }
            catch (Exception ex)
            {
                // The server may be unreachable, MyMoneyUser's login may
                // have been revoked, or the database may have been dropped
                // since the credentials file was written -- any of these
                // throw out of SqlServerStoredProcDatabase.Load(). Per this
                // method's contract, every such failure falls through to
                // MainWindow's normal SQLite/File-menu-driven behavior
                // instead of crashing app startup.
                LogFailure(log, $"DataEngineStartup: {ex.Message}");
                database = null;
                money = null;
                return false;
            }
        }

        /// <summary>
        /// "Does the database exist" is answered by "has a full,
        /// successful bootstrap already run" -- signaled specifically by a
        /// "MyMoneyUser" entry in the credentials file, not by mere file
        /// existence. SaBootstrapConnection writes a "sa" entry the moment
        /// 'sa' connects, before BootstrapRunner has created anything else;
        /// if bootstrap then fails partway through (e.g. the database/login
        /// creation step errors out), the credentials file already exists
        /// with only "sa" in it. Checking for "MyMoneyUser" specifically --
        /// written only at the very end of a successful BootstrapRunner.Run
        /// -- avoids treating that partial state as "already bootstrapped"
        /// forever, which would otherwise permanently block ever retrying.
        /// </summary>
        private static bool DatabaseExists(Action<string> log)
        {
            try
            {
                var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
                var credentials = credentialStore.Load();
                return credentials.ContainsKey("MyMoneyUser");
            }
            catch (Exception ex)
            {
                // A malformed credentials file: treat as "not yet
                // bootstrapped" (RunMyMoneyAdmin/BootstrapRunner is
                // idempotent and will overwrite it with a fresh, valid one)
                // rather than crashing startup.
                LogFailure(log, $"DataEngineStartup: failed to read credentials file, treating database as not yet bootstrapped. {ex.Message}");
                return false;
            }
        }

        private static bool RunMyMoneyAdmin(string server, string databaseName, Action<string> log)
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "MyMoneyAdmin.exe");
            if (!File.Exists(exePath))
            {
                LogFailure(log, $"DataEngineStartup: MyMoneyAdmin.exe not found at {exePath}. Build Source/WPF/MyMoneyAdmin first.");
                return false;
            }

            var startInfo = new ProcessStartInfo(exePath, $"\"{server}\" \"{databaseName}\"")
            {
                UseShellExecute = true
            };

            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }

        private static void LogFailure(Action<string> log, string message)
        {
            // Debug.WriteLine is invisible unless a debugger is attached;
            // route the same message through the caller's real logger too
            // (MainWindow passes one backed by this.log) so failures are
            // visible in a normal run.
            Debug.WriteLine(message);
            log?.Invoke(message);
        }
    }
#endif
}
