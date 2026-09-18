using System.Data;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Importers;
using Walkabout.Utilities;

namespace Walkabout.Tests
{
    /// <summary>
    /// Covers DatabaseLifecycle, the storage-engine-independent half of MainWindow's
    /// file/database lifecycle moved into MyMoney.Business by Task 7. None of these
    /// tests touch WPF: the UI seam is IBusinessLayerUiCallback, the engine seam is
    /// IDatabaseFactory and the credential-store seam is IDatabasePasswordStore, and
    /// all three are faked here.
    /// </summary>
    [TestFixture]
    public class DatabaseLifecycleTests
    {
        #region Test doubles

        private class FakeDatabaseFactory : IDatabaseFactory
        {
            private readonly IDatabase database;
            private readonly System.Exception failure;

            public FakeDatabaseFactory(IDatabase database)
            {
                this.database = database;
            }

            public FakeDatabaseFactory(System.Exception failure)
            {
                this.failure = failure;
            }

            public DbFlavor RequestedFlavor { get; private set; }
            public DatabaseConnectionInfo RequestedInfo { get; private set; }
            public int CallCount { get; private set; }

            public IDatabase CreateDatabase(DbFlavor flavor, DatabaseConnectionInfo info)
            {
                this.CallCount++;
                this.RequestedFlavor = flavor;
                this.RequestedInfo = info;
                if (this.failure != null)
                {
                    throw this.failure;
                }
                return this.database;
            }
        }

        private class FakePasswordStore : IDatabasePasswordStore
        {
            public int SaveCount { get; private set; }
            public string SavedDatabaseName { get; private set; }
            public string SavedPassword { get; private set; }

            public string LoadPassword(string databaseName) => null;

            public void SavePassword(string databaseName, string password)
            {
                this.SaveCount++;
                this.SavedDatabaseName = databaseName;
                this.SavedPassword = password;
            }
        }

        /// <summary>
        /// Implements the full IBusinessLayerUiCallback surface as of Task 7 (11 methods).
        /// DatabaseLifecycle only ever calls ConfirmWithDetails, which is the only one that
        /// records anything here.
        /// </summary>
        private class FakeBusinessLayerUiCallback : IBusinessLayerUiCallback
        {
            private readonly bool confirmAnswer;

            public FakeBusinessLayerUiCallback(bool confirmAnswer)
            {
                this.confirmAnswer = confirmAnswer;
            }

            public int ConfirmWithDetailsCount { get; private set; }
            public string LastConfirmMessage { get; private set; }
            public string LastConfirmTitle { get; private set; }
            public string LastConfirmDetails { get; private set; }

            public bool ConfirmWithDetails(string message, string title, string details)
            {
                this.ConfirmWithDetailsCount++;
                this.LastConfirmMessage = message;
                this.LastConfirmTitle = title;
                this.LastConfirmDetails = details;
                return this.confirmAnswer;
            }

            public void ShowError(string message, string title) { }
            public bool Confirm(string message, string title) => true;
            public bool ConfirmOkCancel(string message, string title) => true;
            public void ClearOutputLog(string heading) { }
            public void AppendErrorLog(string errorMessages, string logFilePath, bool activate) { }
            public CsvMap PromptForCsvFieldMapping(string[] expectedColumns, System.Collections.Generic.IEnumerable<string> headers, CsvMap existingMap) => null;
            public string PromptSaveFileName(string filter) => null;
            public void OpenExportedFile(string filePath, bool applyXsltTransform) { }
            public void MoveAttachments(Transaction original, Account newAccount) { }
            public Account PickAccount(MyMoney money, Account accountTemplate, string prompt) => null;
        }

        /// <summary>
        /// MockDatabase always reports UpgradeRequired == false and its members are not
        /// virtual, so the upgrade-negotiation paths need a stand-in that can say yes. This
        /// delegates everything real to a MockDatabase so Load/Save still round-trip.
        /// </summary>
        private class UpgradeableDatabase : IDatabase
        {
            private readonly MockDatabase inner = new MockDatabase();

            public bool UpgradeRequired { get; set; }
            public int UpgradeCount { get; private set; }

            public void Upgrade()
            {
                this.UpgradeCount++;
                this.UpgradeRequired = false;
            }

            public string Server => this.inner.Server;
            public string DatabasePath => this.inner.DatabasePath;
            public string UserId => this.inner.UserId;
            public string Password { get => this.inner.Password; set => this.inner.Password = value; }
            public string BackupPath => this.inner.BackupPath;
            public bool SupportsUserLogin => this.inner.SupportsUserLogin;
            public DbFlavor DbFlavor => this.inner.DbFlavor;
            public bool Exists => this.inner.Exists;

            public void Create() => this.inner.Create();
            public MyMoney Load(IStatusService status) => this.inner.Load(status);
            public void Save(MyMoney money) => this.inner.Save(money);
            public void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot => this.inner.SaveOne(root);
            public void SaveTransfer(Transaction from, Transaction to) => this.inner.SaveTransfer(from, to);
            public void SaveBatch(System.Collections.Generic.IEnumerable<PersistentObject> roots) => this.inner.SaveBatch(roots);
            public void Backup(string path) => this.inner.Backup(path);
            public void Delete() => this.inner.Delete();
            public string GetLog() => this.inner.GetLog();
            public DataSet QueryDataSet(string cmd) => this.inner.QueryDataSet(cmd);
            public void Disconnect() => this.inner.Disconnect();
        }

        #endregion

        #region ClassifyDatabaseFile

        [TestCase(@"c:\data\money.xml", DbFlavor.Xml)]
        [TestCase(@"c:\data\MONEY.XML", DbFlavor.Xml)]
        [TestCase(@"c:\data\money.bxml", DbFlavor.BinaryXml)]
        [TestCase(@"c:\data\money.myMoney.sdf", DbFlavor.SqlCE)]
        [TestCase(@"c:\data\money.SDF", DbFlavor.SqlCE)]
        [TestCase(@"c:\data\money.db", DbFlavor.Sqlite)]
        [TestCase(@"c:\data\money.mmdb", DbFlavor.Sqlite)]
        [TestCase(@"c:\data\money.MMDB", DbFlavor.Sqlite)]
        [TestCase("MyMoneyCatalog", DbFlavor.SqlServer)]
        [TestCase(@"c:\data\money.sqlite", DbFlavor.SqlServer)]
        public void ClassifyDatabaseFile_MapsExtensionToEngine(string path, DbFlavor expected)
        {
            Assert.AreEqual(expected, DatabaseLifecycle.ClassifyDatabaseFile(path));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ClassifyDatabaseFile_MissingName_Throws(string path)
        {
            Assert.Throws<System.ArgumentException>(() => DatabaseLifecycle.ClassifyDatabaseFile(path));
        }

        #endregion

        #region Open

        private static DatabaseLifecycle CreateLifecycle(IDatabaseFactory factory, IDatabasePasswordStore passwords, IBusinessLayerUiCallback callback)
        {
            return new DatabaseLifecycle(factory, passwords, callback);
        }

        [Test]
        public void Open_NoUpgradeNeeded_ReturnsLoadableDatabaseAndSavesPassword()
        {
            var mock = new MockDatabase();
            mock.Create();
            var factory = new FakeDatabaseFactory(mock);
            var passwords = new FakePasswordStore();
            var callback = new FakeBusinessLayerUiCallback(true);
            DatabaseLifecycle lifecycle = CreateLifecycle(factory, passwords, callback);

            DatabaseOpenResult result = lifecycle.Open(new DatabaseConnectionInfo()
            {
                Server = "someserver",
                DatabasePath = @"c:\data\money.mmdb",
                UserId = "someuser",
                Password = "s3cret",
                BackupPath = @"c:\backups",
            });

            Assert.IsFalse(result.UpgradeDeclined);
            Assert.AreSame(mock, result.Database);

            // The engine decision, and the whole connection info, reached the factory.
            Assert.AreEqual(DbFlavor.Sqlite, factory.RequestedFlavor);
            Assert.AreEqual("someserver", factory.RequestedInfo.Server);
            Assert.AreEqual("someuser", factory.RequestedInfo.UserId);
            Assert.AreEqual(@"c:\backups", factory.RequestedInfo.BackupPath);

            // Password is persisted against the database's own reported path, not the
            // requested one - that is what MainWindow's original inline code did.
            Assert.AreEqual(1, passwords.SaveCount);
            Assert.AreEqual(mock.DatabasePath, passwords.SavedDatabaseName);
            Assert.AreEqual("s3cret", passwords.SavedPassword);

            // No upgrade prompt when the engine doesn't ask for one.
            Assert.AreEqual(0, callback.ConfirmWithDetailsCount);

            // The returned database really is ready to load.
            MyMoney money = result.Database.Load(null);
            Assert.IsNotNull(money);
            Assert.IsNotNull(money.Accounts);
        }

        [Test]
        public void Open_UpgradeRequiredAndConfirmed_UpgradesThenSavesPassword()
        {
            var database = new UpgradeableDatabase { UpgradeRequired = true };
            database.Create();
            var passwords = new FakePasswordStore();
            var callback = new FakeBusinessLayerUiCallback(true);
            DatabaseLifecycle lifecycle = CreateLifecycle(new FakeDatabaseFactory(database), passwords, callback);

            DatabaseOpenResult result = lifecycle.Open(new DatabaseConnectionInfo()
            {
                DatabasePath = @"c:\data\money.db",
                Password = "pw",
            });

            Assert.IsFalse(result.UpgradeDeclined);
            Assert.AreEqual(1, database.UpgradeCount);
            Assert.AreEqual(1, passwords.SaveCount);
            Assert.AreEqual(1, callback.ConfirmWithDetailsCount);
            Assert.AreEqual(DatabaseLifecycle.UpgradeConfirmationTitle, callback.LastConfirmTitle);
            Assert.AreEqual(DatabaseLifecycle.UpgradeConfirmationDetails, callback.LastConfirmDetails);
            Assert.AreEqual(DatabaseLifecycle.UpgradeConfirmationMessage, callback.LastConfirmMessage);
        }

        [Test]
        public void Open_UpgradeDeclined_LeavesDatabaseUntouched()
        {
            var database = new UpgradeableDatabase { UpgradeRequired = true };
            database.Create();
            var passwords = new FakePasswordStore();
            var callback = new FakeBusinessLayerUiCallback(false);
            DatabaseLifecycle lifecycle = CreateLifecycle(new FakeDatabaseFactory(database), passwords, callback);

            DatabaseOpenResult result = lifecycle.Open(new DatabaseConnectionInfo()
            {
                DatabasePath = @"c:\data\money.db",
                Password = "pw",
            });

            Assert.IsTrue(result.UpgradeDeclined);
            Assert.AreEqual(0, database.UpgradeCount, "declining the prompt must not upgrade the schema");
            Assert.AreEqual(0, passwords.SaveCount, "an aborted load must not rewrite the saved password");
            Assert.IsTrue(database.UpgradeRequired, "the database is left exactly as it was found");
        }

        [Test]
        public void Open_EngineFailure_PropagatesToCaller()
        {
            // MainWindow reports engine failures itself (it logs them and shows the error
            // dialog), so DatabaseLifecycle must not swallow them.
            var boom = new System.InvalidOperationException("engine unavailable");
            DatabaseLifecycle lifecycle = CreateLifecycle(
                new FakeDatabaseFactory(boom), new FakePasswordStore(), new FakeBusinessLayerUiCallback(true));

            var thrown = Assert.Throws<System.InvalidOperationException>(
                () => lifecycle.Open(new DatabaseConnectionInfo() { DatabasePath = @"c:\data\money.db" }));
            Assert.AreEqual("engine unavailable", thrown.Message);
        }

        [Test]
        public void Open_UnusableDatabaseName_ThrowsBeforeTouchingTheEngine()
        {
            var factory = new FakeDatabaseFactory(new MockDatabase());
            var passwords = new FakePasswordStore();
            DatabaseLifecycle lifecycle = CreateLifecycle(factory, passwords, new FakeBusinessLayerUiCallback(true));

            Assert.Throws<System.ArgumentException>(
                () => lifecycle.Open(new DatabaseConnectionInfo() { DatabasePath = "  " }));
            Assert.AreEqual(0, factory.CallCount);
            Assert.AreEqual(0, passwords.SaveCount);
        }

        [Test]
        public void Open_NullConnectionInfo_Throws()
        {
            DatabaseLifecycle lifecycle = CreateLifecycle(
                new FakeDatabaseFactory(new MockDatabase()), new FakePasswordStore(), new FakeBusinessLayerUiCallback(true));

            Assert.Throws<System.ArgumentNullException>(() => lifecycle.Open(null));
        }

        [Test]
        public void Constructor_RejectsMissingDependencies()
        {
            var factory = new FakeDatabaseFactory(new MockDatabase());
            var passwords = new FakePasswordStore();
            var callback = new FakeBusinessLayerUiCallback(true);

            Assert.Throws<System.ArgumentNullException>(() => new DatabaseLifecycle(null, passwords, callback));
            Assert.Throws<System.ArgumentNullException>(() => new DatabaseLifecycle(factory, null, callback));
            Assert.Throws<System.ArgumentNullException>(() => new DatabaseLifecycle(factory, passwords, null));
        }

        #endregion

        #region DatabaseFactory (MyMoney.Data's IDatabaseFactory implementation)

        [Test]
        public void DatabaseFactory_CreatesRealSqliteDatabase_ThatRoundTrips()
        {
            // End-to-end over the real seam implementation: classification in
            // MyMoney.Business picks the engine, MyMoney.Data's DatabaseFactory builds it,
            // and the result is a genuinely usable store. Uses a fake password store so the
            // test never writes to the Windows Credential Manager.
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "DatabaseLifecycleTests_" + System.Guid.NewGuid().ToString("N") + ".mmdb");
            var passwords = new FakePasswordStore();
            var lifecycle = new DatabaseLifecycle(
                new DatabaseFactory(null, null), passwords, new FakeBusinessLayerUiCallback(true));

            try
            {
                DatabaseOpenResult result = lifecycle.Open(new DatabaseConnectionInfo() { DatabasePath = path });

                Assert.IsFalse(result.UpgradeDeclined);
                Assert.AreEqual(DbFlavor.Sqlite, result.Database.DbFlavor);
                Assert.AreEqual(1, passwords.SaveCount);

                MyMoney money = result.Database.Load(null);
                Assert.IsNotNull(money);

                Account a = money.Accounts.AddAccount("Checking");
                a.Type = AccountType.Checking;
                result.Database.Save(money);
                result.Database.Disconnect();

                DatabaseOpenResult reopened = lifecycle.Open(new DatabaseConnectionInfo() { DatabasePath = path });
                MyMoney reloaded = reopened.Database.Load(null);
                Assert.AreEqual(1, reloaded.Accounts.Count);
                Assert.AreEqual("Checking", reloaded.Accounts.GetFirstAccount().Name);
                reopened.Database.Disconnect();
            }
            finally
            {
                try
                {
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }
                }
                catch (System.IO.IOException)
                {
                    // A leftover temp file is not worth failing the test over.
                }
            }
        }

        [Test]
        public void DatabaseFactory_UnsupportedFlavor_Throws()
        {
            var factory = new DatabaseFactory(null, null);
            Assert.Throws<System.NotSupportedException>(
                () => factory.CreateDatabase(DbFlavor.Mock, new DatabaseConnectionInfo() { DatabasePath = "x" }));
        }

        [Test]
        public void DatabaseFactory_NullConnectionInfo_Throws()
        {
            var factory = new DatabaseFactory(null, null);
            Assert.Throws<System.ArgumentNullException>(() => factory.CreateDatabase(DbFlavor.Sqlite, null));
        }

        #endregion
    }
}
