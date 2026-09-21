using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class ProvisionerBackupTests
    {
        private string dir;

        [SetUp]
        public void SetUp()
        {
            this.dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.dir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(this.dir, true);
            }
            catch (IOException)
            {
            }
        }

        private SqliteMoneyStoreProvisioner OpenFile(string name) =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                name, Path.Combine(this.dir, name + ".mmdb"), true));

        [Test]
        public void Backup_ProducesAFileThatOpensAtTheSameSchemaVersion()
        {
            string backupPath = Path.Combine(this.dir, "backup.mmdb");

            using (var p = OpenFile("books"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.Backup(backupPath);
            }

            Assert.That(File.Exists(backupPath), Is.True);

            using (var restored = SqliteMoneyStoreProvisioner.Open(
                new SqliteProvisioningOptions("backup", backupPath, true)))
            {
                Assert.That(restored.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
                Assert.That(restored.Verify().IsClean, Is.True);
            }
        }

        [Test]
        public void Backup_OverwritesAnExistingDestination()
        {
            // "IMoneyStoreProvisioner.Backup must be able to overwrite" - spec section 3.2.
            // VACUUM INTO refuses an existing file, so the implementation has to clear it first.
            string backupPath = Path.Combine(this.dir, "backup.mmdb");
            File.WriteAllText(backupPath, "stale");

            using (var p = OpenFile("books"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.DoesNotThrow(() => p.Backup(backupPath));
            }

            using (var restored = SqliteMoneyStoreProvisioner.Open(
                new SqliteProvisioningOptions("backup", backupPath, true)))
            {
                Assert.That(restored.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void Backup_OfAnInMemoryDatabase_Works()
        {
            // The T-1 fixture backs a prepared :memory: template up to clone it per test
            // (spec section 2.4), so this path is not hypothetical.
            string backupPath = Path.Combine(this.dir, "from-memory.mmdb");

            using (var p = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "mem", SqliteConnectionFactory.InMemoryDataSource, true)))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.Backup(backupPath);
            }

            Assert.That(File.Exists(backupPath), Is.True);
        }

        [Test]
        public void Delete_RemovesTheFile()
        {
            string path;
            using (var p = OpenFile("doomed"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                path = p.DataSource;
                p.Delete();
            }

            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void Backup_ToAnEmptyPath_Throws()
        {
            using (var p = OpenFile("books"))
            {
                Assert.Throws<ArgumentException>(() => p.Backup("  "));
            }
        }
    }
}
