using System;
using System.IO;

namespace Walkabout.Data
{
    /// <summary>
    /// Everything needed to reach a money database, independent of which storage
    /// engine actually backs it. Mirrors the five parameters MainWindow's
    /// LoadDatabase(server, databaseName, userId, password, backupPath) used to
    /// take before this logic moved into MyMoney.Business (Task 7).
    /// </summary>
    public class DatabaseConnectionInfo
    {
        public string Server { get; set; }
        public string DatabasePath { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }
        public string BackupPath { get; set; }
    }

    /// <summary>
    /// Constructs the concrete IDatabase for a storage engine.
    ///
    /// DatabaseLifecycle owns the *decision* (which engine a given path names, and
    /// what has to happen before the graph can be loaded) but cannot own the
    /// *construction*: every IDatabase implementation - SqliteDatabase, SqlCeDatabase,
    /// SqlServerDatabase, XmlStore, BinaryXmlStore - lives in MyMoney.Data, which
    /// already references MyMoney.Business, so the reverse reference would be
    /// circular. This is the inversion that lets the decision logic live in the
    /// business layer anyway; MyMoney.Data supplies DatabaseFactory, the real
    /// implementation.
    /// </summary>
    public interface IDatabaseFactory
    {
        /// <summary>
        /// Builds a ready-to-use IDatabase for the given engine. Implementations are
        /// responsible for any engine-specific pre-flight (e.g. the SQL CE runtime
        /// check) and for calling IDatabase.Create() on the engines that need their
        /// store materialized before use.
        /// </summary>
        IDatabase CreateDatabase(DbFlavor flavor, DatabaseConnectionInfo info);
    }

    /// <summary>
    /// Persists the password a database was opened with. The real implementation
    /// talks to the Windows Credential Manager (MyMoney.Data's DatabaseSecurity /
    /// Credential), which MyMoney.Business cannot reference - same layering
    /// constraint as IDatabaseFactory above.
    /// </summary>
    public interface IDatabasePasswordStore
    {
        string LoadPassword(string databaseName);

        void SavePassword(string databaseName, string password);
    }

    /// <summary>
    /// Outcome of DatabaseLifecycle.Open: either a database that is ready to Load,
    /// or the one non-exceptional refusal - the user declining a required schema
    /// upgrade, which leaves the existing database untouched and aborts the load.
    /// </summary>
    public class DatabaseOpenResult
    {
        private DatabaseOpenResult(IDatabase database, bool upgradeDeclined)
        {
            this.Database = database;
            this.UpgradeDeclined = upgradeDeclined;
        }

        /// <summary>
        /// The database that was created/connected. Non-null even when the upgrade
        /// was declined (it was already constructed by then), matching what
        /// MainWindow's original inline code left behind in that case.
        /// </summary>
        public IDatabase Database { get; }

        /// <summary>
        /// True when the database needed a schema upgrade and the user said no, so
        /// the caller must abort without loading.
        /// </summary>
        public bool UpgradeDeclined { get; }

        public static DatabaseOpenResult Opened(IDatabase database)
        {
            return new DatabaseOpenResult(database, false);
        }

        public static DatabaseOpenResult Declined(IDatabase database)
        {
            return new DatabaseOpenResult(database, true);
        }
    }

    /// <summary>
    /// The storage-engine-independent half of MainWindow's file/database lifecycle:
    /// which engine a database path names, and everything that has to happen before
    /// IDatabase.Load can be called - materializing the store, negotiating a schema
    /// upgrade with the user, and persisting the password.
    ///
    /// Deliberately stops short of Load itself. MainWindow still calls
    /// IDatabase.Load(this) directly, because the surrounding work is genuinely UI:
    /// it passes itself as the IStatusService, stamps its status-message suppression
    /// window immediately before the load starts, times the load for the status bar,
    /// and marshals the result back onto the UI thread.
    /// </summary>
    public class DatabaseLifecycle
    {
        // Preserved verbatim from MainWindow.LoadDatabase, including the literal "\n"
        // sequences and the 24-space indentation: this is a C# verbatim string, so
        // that is exactly what the message box used to render. It reads like a latent
        // formatting bug, but fixing it would be a user-visible change that has
        // nothing to do with moving this code, so it is left exactly as it was.
        public const string UpgradeConfirmationMessage = @"Your database needs to be upgraded to the latest format;
                        \n
                        click YES to upgrade,
                        \n
                        click NO to leave your database untouched and abort loading";
        public const string UpgradeConfirmationTitle = "Confirm upgrade";
        public const string UpgradeConfirmationDetails = "Upgrade Required";

        private readonly IDatabaseFactory factory;
        private readonly IDatabasePasswordStore passwords;
        private readonly IBusinessLayerUiCallback uiCallback;

        public DatabaseLifecycle(IDatabaseFactory factory, IDatabasePasswordStore passwords, IBusinessLayerUiCallback uiCallback)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
            this.uiCallback = uiCallback ?? throw new ArgumentNullException(nameof(uiCallback));
        }

        /// <summary>
        /// Maps a database path to the storage engine that can open it. Anything that
        /// is not one of the recognized file-based extensions is a SQL Server catalog
        /// name, not a file - that fallback is why this returns DbFlavor.SqlServer
        /// rather than rejecting unknown extensions.
        /// </summary>
        public static DbFlavor ClassifyDatabaseFile(string databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                throw new ArgumentException("A database name or path is required.", nameof(databaseName));
            }

            string ext = Path.GetExtension(databaseName).ToLowerInvariant();
            if (ext == ".xml")
            {
                return DbFlavor.Xml;
            }
            else if (ext == ".bxml")
            {
                return DbFlavor.BinaryXml;
            }
            else if (databaseName.EndsWith(".sdf", StringComparison.OrdinalIgnoreCase))
            {
                return DbFlavor.SqlCE;
            }
            else if (databaseName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || databaseName.EndsWith(".mmdb", StringComparison.OrdinalIgnoreCase))
            {
                return DbFlavor.Sqlite;
            }
            else
            {
                return DbFlavor.SqlServer;
            }
        }

        /// <summary>
        /// Gets a database ready to Load: picks the engine, builds it, negotiates a
        /// schema upgrade if the engine reports one is required, and saves the
        /// password. Engine failures (a missing SQL CE runtime, an unreadable file, a
        /// bad connection) propagate as exceptions for the caller to report, exactly
        /// as they did when this ran inline in MainWindow.
        /// </summary>
        public DatabaseOpenResult Open(DatabaseConnectionInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            DbFlavor flavor = ClassifyDatabaseFile(info.DatabasePath);
            IDatabase database = this.factory.CreateDatabase(flavor, info);

            if (database.UpgradeRequired)
            {
                if (!this.uiCallback.ConfirmWithDetails(UpgradeConfirmationMessage, UpgradeConfirmationTitle, UpgradeConfirmationDetails))
                {
                    return DatabaseOpenResult.Declined(database);
                }

                database.Upgrade();
            }

            this.passwords.SavePassword(database.DatabasePath, info.Password);
            return DatabaseOpenResult.Opened(database);
        }
    }
}
