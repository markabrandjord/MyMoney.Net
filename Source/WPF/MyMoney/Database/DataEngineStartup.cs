using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
#if DEBUG
    public static class DataEngineStartup
    {
        public static bool TryAutoLoad(string configPath, out IDatabase database, out MyMoney money)
        {
            database = null;
            money = null;

            DataEngineConfig config = DataEngineConfig.Load(configPath);
            if (config.Engine != DataEngineType.SqlServer || string.IsNullOrEmpty(config.Server) || string.IsNullOrEmpty(config.Database))
            {
                return false;
            }

            if (!DatabaseExists(config.Server, config.Database))
            {
                if (!RunMyMoneyAdmin(config.Server, config.Database))
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
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"DataEngineStartup: {ex.Message}");
                return false;
            }

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = config.Server,
                InitialCatalog = config.Database,
                UserID = userCredential.UserId,
                Password = userCredential.Password
            };

            var sqlServerDatabase = new SqlServerStoredProcDatabase { ConnectionStringOverride = builder.ConnectionString };
            money = sqlServerDatabase.Load(null);
            database = sqlServerDatabase;
            return true;
        }

        private static bool DatabaseExists(string server, string databaseName)
        {
            // We don't have MyMoneyUser's password until bootstrap has run
            // once, so "does the database exist" is answered by "does the
            // credential file bootstrap wrote already exist" -- there's no
            // way to check the server itself without a login to check with.
            return File.Exists(DataEngineCredentialStore.GetDefaultPath());
        }

        private static bool RunMyMoneyAdmin(string server, string databaseName)
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "MyMoneyAdmin.exe");
            if (!File.Exists(exePath))
            {
                Debug.WriteLine($"DataEngineStartup: MyMoneyAdmin.exe not found at {exePath}. Build Source/WPF/MyMoneyAdmin first.");
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
    }
#endif
}
