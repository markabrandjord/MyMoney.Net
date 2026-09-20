using System;
using System.IO;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// MyMoney.Data's implementation of MyMoney.Business's IDatabaseFactory seam.
    ///
    /// This is the half of MainWindow.LoadDatabase's old extension switch that could
    /// not move into MyMoney.Business with the rest of the decision logic: the
    /// concrete engines all live here, and MyMoney.Business cannot reference this
    /// assembly (MyMoney.Data already references MyMoney.Business). DatabaseLifecycle
    /// decides which DbFlavor a path names; this builds it.
    ///
    /// The securityService/uiCallback dependencies are only ever used by the SQL
    /// Server engine, exactly as in the original inline code - the file-based engines
    /// were never given either one.
    /// </summary>
    public class DatabaseFactory : IDatabaseFactory
    {
        private readonly IDirectorySecurity securityService;
        private readonly IDataLayerUiCallback uiCallback;

        public DatabaseFactory(IDirectorySecurity securityService, IDataLayerUiCallback uiCallback)
        {
            this.securityService = securityService;
            this.uiCallback = uiCallback;
        }

        public IDatabase CreateDatabase(DbFlavor flavor, DatabaseConnectionInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            switch (flavor)
            {
                case DbFlavor.Xml:
                    return new XmlStore(info.DatabasePath, info.Password)
                    {
                        UserId = info.UserId,
                        Password = info.Password,
                        BackupPath = info.BackupPath,
                    };

                case DbFlavor.BinaryXml:
                    return new BinaryXmlStore(info.DatabasePath, info.Password)
                    {
                        UserId = info.UserId,
                        Password = info.Password,
                        BackupPath = info.BackupPath,
                    };

                case DbFlavor.SqlCE:
                    if (!SqlCeDatabase.IsSqlCEInstalled)
                    {
                        throw new Exception("SQL Express does not appear to be installed any more so we can't open your existing database. " +
                            "If you want to open it, then please visit http://www.microsoft.com/download/en/details.aspx?id=184 to install " +
                            "SQL Server Compact Edition 4.0 and try again");
                    }
                    else
                    {
                        SqlCeDatabase sqlCe = new SqlCeDatabase()
                        {
                            DatabasePath = Path.GetFullPath(info.DatabasePath),
                            UserId = info.UserId,
                            Password = info.Password,
                            BackupPath = info.BackupPath
                        };
                        sqlCe.Create();
                        return sqlCe;
                    }

                case DbFlavor.Sqlite:
                    SqliteDatabase sqlite = new SqliteDatabase()
                    {
                        DatabasePath = Path.GetFullPath(info.DatabasePath),
                        UserId = info.UserId,
                        Password = info.Password,
                        BackupPath = info.BackupPath
                    };
                    sqlite.Create();
                    return sqlite;

                case DbFlavor.SqlServer:
                    SqlServerDatabase sqlServer = new SqlServerDatabase()
                    {
                        Server = info.Server,
                        DatabasePath = info.DatabasePath,
                        UserId = info.UserId,
                        Password = info.Password,
                        BackupPath = info.BackupPath,
                        SecurityService = this.securityService,
                        UiCallback = this.uiCallback
                    };
                    sqlServer.Create();
                    return sqlServer;

                default:
                    throw new NotSupportedException(string.Format("There is no storage engine for DbFlavor.{0}.", flavor));
            }
        }
    }
}
