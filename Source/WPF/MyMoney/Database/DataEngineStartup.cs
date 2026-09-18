using System;
using System.Diagnostics;
using System.Linq;
using Walkabout.Utilities;

namespace Walkabout.Data
{
#if DEBUG
    public static class DataEngineStartup
    {
        /// <summary>
        /// Reconnects to whichever registered SQL Server database was used
        /// most recently, if any. Never bootstraps -- if no SQL Server
        /// entry exists yet in the registry, returns false and the caller
        /// falls through to its normal SQLite/File-menu-driven behavior.
        /// Bootstrapping only ever happens via File | New | SQL Server...
        /// (see MainWindow.OnNewSqlServerDatabase).
        /// </summary>
        public static bool TryAutoLoad(string registryPath, out IDatabase database, out MyMoney money, Action<string> log = null)
        {
            database = null;
            money = null;

            DatabaseRegistry registry;
            try
            {
                registry = DatabaseRegistry.Load(registryPath);
            }
            catch (Exception ex)
            {
                LogFailure(log, $"DataEngineStartup: could not read '{registryPath}', falling back to SQLite. {ex.Message}");
                return false;
            }

            var mostRecent = registry.Databases
                .Where(kv => kv.Value.Engine == DataEngineType.SqlServer)
                .OrderByDescending(kv => kv.Value.LastUsedUtc ?? DateTime.MinValue)
                .FirstOrDefault();

            if (mostRecent.Value == null)
            {
                return false;
            }

            try
            {
                var sqlServerDatabase = SqlServerConnectionFactory.Connect(registry, mostRecent.Key, new WpfDataLayerUiCallback());
                money = sqlServerDatabase.Load(null);
                database = sqlServerDatabase;
                return true;
            }
            catch (Exception ex)
            {
                // The server may be unreachable, MyMoneyUser's login may
                // have been revoked, or the database may have been dropped
                // since the registry was written -- fall through to
                // MainWindow's normal SQLite/File-menu-driven behavior
                // instead of crashing app startup.
                LogFailure(log, $"DataEngineStartup: {ex.GetType().FullName}: {ex.Message}");
                database = null;
                money = null;
                return false;
            }
        }

        private static void LogFailure(Action<string> log, string message)
        {
            Debug.WriteLine(message);
            log?.Invoke(message);
        }
    }
#endif
}
