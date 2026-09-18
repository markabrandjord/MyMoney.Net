using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UnitTests
{
    public class DatabaseRegistryTests
    {
        private string tempFile;

        [SetUp]
        public void Setup()
        {
            this.tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(this.tempFile)) { File.Delete(this.tempFile); }
        }

        [Test]
        public void MissingFile_ReturnsEmptyRegistry()
        {
            var registry = DatabaseRegistry.Load(this.tempFile);
            Assert.That(registry.Servers, Is.Empty);
            Assert.That(registry.Databases, Is.Empty);
        }

        [Test]
        public void SaveThenLoad_RoundTripsServersAndDatabases()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Servers["Redmond"] = new DatabaseServerEntry
            {
                Comment = "Home NAS",
                MyMoneyAdmin = new DatabaseCredential { UserId = "MyMoneyAdmin", Password = "adminpw" },
                MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = "userpw" },
                MyMoneyTest = new DatabaseCredential { UserId = "MyMoneyTest", Password = "testpw" }
            };
            registry.Databases["My Real Money"] = new DatabaseEntry
            {
                Engine = DataEngineType.SqlServer, Server = "Redmond", Catalog = "MyMoney",
                TestDatabase = false, LastUsedUtc = new DateTime(2026, 9, 17, 20, 0, 0, DateTimeKind.Utc),
                Comment = "The real one"
            };
            registry.Databases["Personal.mmdb"] = new DatabaseEntry
            {
                Engine = DataEngineType.Sqlite, Path = @"C:\Users\x\MyMoney\personal.mmdb",
                TestDatabase = false, LastUsedUtc = null, Comment = null
            };
            registry.Save();

            var reloaded = DatabaseRegistry.Load(this.tempFile);

            Assert.That(reloaded.Servers["Redmond"].Comment, Is.EqualTo("Home NAS"));
            Assert.That(reloaded.Servers["Redmond"].MyMoneyAdmin.Password, Is.EqualTo("adminpw"));
            Assert.That(reloaded.Databases["My Real Money"].Server, Is.EqualTo("Redmond"));
            Assert.That(reloaded.Databases["My Real Money"].Catalog, Is.EqualTo("MyMoney"));
            Assert.That(reloaded.Databases["My Real Money"].TestDatabase, Is.False);
            Assert.That(reloaded.Databases["Personal.mmdb"].Path, Is.EqualTo(@"C:\Users\x\MyMoney\personal.mmdb"));
        }

        [Test]
        public void Save_WritesTestDatabaseFieldWithExactCasing()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Databases["x"] = new DatabaseEntry { Engine = DataEngineType.Sqlite, Path = "x.mmdb", TestDatabase = true };
            registry.Save();

            string json = File.ReadAllText(this.tempFile);
            Assert.That(json, Does.Contain("\"TestDatabase\": true"));
            Assert.That(json, Does.Not.Contain("\"testDatabase\""));
        }

        [Test]
        public void Save_WritesEngineAsReadableStringNotRawInt()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Databases["x"] = new DatabaseEntry { Engine = DataEngineType.SqlServer, Server = "Redmond", Catalog = "MyMoney" };
            registry.Save();

            string json = File.ReadAllText(this.tempFile);
            Assert.That(json, Does.Contain("\"engine\": \"SqlServer\""));
            Assert.That(json, Does.Not.Contain("\"engine\": 1"));

            var reloaded = DatabaseRegistry.Load(this.tempFile);
            Assert.That(reloaded.Databases["x"].Engine, Is.EqualTo(DataEngineType.SqlServer));
        }

        [Test]
        public void GetDefaultPath_EndsWithDataEngineConfigJson()
        {
            Assert.That(DatabaseRegistry.GetDefaultPath(), Does.EndWith(Path.Combine("MyMoney", "dataengine.config.json")));
        }

        [Test]
        public void BuildConnectionString_ForSqlServerEntry_UsesRequestedRoleCredential()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Servers["Redmond"] = new DatabaseServerEntry
            {
                MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = "secret" }
            };
            var entry = new DatabaseEntry { Engine = DataEngineType.SqlServer, Server = "Redmond", Catalog = "MyMoney" };

            string connectionString = registry.BuildConnectionString(entry, DatabaseRole.User);

            Assert.That(connectionString, Does.Contain("User ID=MyMoneyUser").IgnoreCase);
            Assert.That(connectionString, Does.Contain("Initial Catalog=MyMoney").IgnoreCase);
        }

        [Test]
        public void BuildConnectionString_UnknownServer_ThrowsWithClearMessage()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            var entry = new DatabaseEntry { Engine = DataEngineType.SqlServer, Server = "Nowhere", Catalog = "MyMoney" };

            var ex = Assert.Throws<InvalidOperationException>(() => registry.BuildConnectionString(entry, DatabaseRole.User));
            Assert.That(ex.Message, Does.Contain("Nowhere"));
        }

        [Test]
        public void SqlServerConnectionFactory_Connect_BuildsDatabaseWithOverrideAndSyntheticPath()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Servers["Redmond"] = new DatabaseServerEntry
            {
                MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = "secret" }
            };
            registry.Databases["My Real Money"] = new DatabaseEntry
            {
                Engine = DataEngineType.SqlServer, Server = "Redmond", Catalog = "MyMoney"
            };

            var database = SqlServerConnectionFactory.Connect(registry, "My Real Money", uiCallback: null);

            Assert.That(database.ConnectionStringOverride, Does.Contain("Initial Catalog=MyMoney").IgnoreCase);
            Assert.That(database.DatabasePath, Does.EndWith(Path.Combine("SqlServer", "MyMoney.sqlserver")));
        }

        [Test]
        public void SqlServerConnectionFactory_Connect_UnknownDisplayName_Throws()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            Assert.Throws<InvalidOperationException>(() => SqlServerConnectionFactory.Connect(registry, "nope", null));
        }
    }
}
