using System;
using System.IO;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    public static class SqlServerConnectionFactory
    {
        public static SqlServerStoredProcDatabase Connect(DatabaseRegistry registry, string displayName, IDataLayerUiCallback uiCallback)
        {
            if (!registry.Databases.TryGetValue(displayName, out DatabaseEntry entry) || entry.Engine != DataEngineType.SqlServer)
            {
                throw new InvalidOperationException($"No registered SQL Server database named '{displayName}'.");
            }

            string connectionString = registry.BuildConnectionString(entry, DatabaseRole.User);

            return new SqlServerStoredProcDatabase
            {
                ConnectionStringOverride = connectionString,
                // SqlServerStoredProcDatabase has no on-disk file, but
                // DatabasePath is read throughout MainWindow (OFX log path,
                // caption text, attachment/statement/report directories) --
                // see the identical comment in DataEngineStartup.cs this
                // replaces.
                DatabasePath = Path.Combine(ProcessHelper.AppDataPath, "SqlServer", entry.Catalog + ".sqlserver"),
                UiCallback = uiCallback
            };
        }
    }
}
