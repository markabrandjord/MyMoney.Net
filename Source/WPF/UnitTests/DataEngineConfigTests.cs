using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class DataEngineConfigTests
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
            if (File.Exists(this.tempFile))
            {
                File.Delete(this.tempFile);
            }
        }

        [Test]
        public void MissingFile_DefaultsToSqlite()
        {
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.Sqlite));
        }

        [Test]
        public void SqlServerEngine_ParsesAllFields()
        {
            File.WriteAllText(this.tempFile, @"{ ""engine"": ""SqlServer"", ""server"": ""DEVSQL01"", ""database"": ""MyMoney"" }");
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.SqlServer));
            Assert.That(config.Server, Is.EqualTo("DEVSQL01"));
            Assert.That(config.Database, Is.EqualTo("MyMoney"));
        }

        [Test]
        public void UnrecognizedEngineValue_DefaultsToSqlite()
        {
            File.WriteAllText(this.tempFile, @"{ ""engine"": ""Postgres"" }");
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.Sqlite));
        }

        [Test]
        public void EngineKeyMissing_DefaultsToSqlite()
        {
            File.WriteAllText(this.tempFile, @"{ ""server"": ""DEVSQL01"" }");
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.Sqlite));
        }
    }
}
