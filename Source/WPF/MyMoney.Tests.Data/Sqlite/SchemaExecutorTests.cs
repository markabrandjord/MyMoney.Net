using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SchemaExecutorTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory()
        {
            return SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "test", SqliteConnectionFactory.InMemoryDataSource, true));
        }

        [Test]
        public void Catalog_IsContiguouslyNumberedFromOne()
        {
            var versions = SchemaStepCatalog.All.Select(s => s.Version).ToList();

            Assert.That(versions, Is.EqualTo(Enumerable.Range(1, versions.Count).ToList()));
            Assert.That(SchemaStepCatalog.LatestVersion, Is.EqualTo(versions.Last()));
        }

        [Test]
        public void Catalog_ChecksumsAreLineEndingIndependent()
        {
            // A CRLF/LF difference between two checkouts must not read as an edited step.
            SchemaStep step = SchemaStepCatalog.All[0];

            Assert.That(step.ChecksumHash, Is.EqualTo(SchemaStep.ComputeChecksum(step.Sql.Replace("\n", "\r\n"))));
        }

        [Test]
        public void CurrentVersion_OnAnEmptyDatabase_IsZero()
        {
            using (var p = OpenInMemory())
            {
                Assert.That(p.CurrentVersion(), Is.EqualTo(0));
            }
        }

        [Test]
        public void ApplyTo_FromZero_AppliesEveryStepAndRecordsEachInTheLedger()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(p.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
                Assert.That(p.AppliedSteps().Select(s => s.Version),
                    Is.EqualTo(SchemaStepCatalog.All.Select(s => s.Version)));
                Assert.That(p.AppliedSteps().All(s => !string.IsNullOrEmpty(s.AppliedBy)), Is.True);
            }
        }

        [Test]
        public void ApplyTo_IsIdempotent_ASecondCallAppliesNothing()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(p.AppliedSteps(), Has.Count.EqualTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void ApplyTo_StopsAtTheRequestedVersion()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(2);

                Assert.That(p.CurrentVersion(), Is.EqualTo(2));
                Assert.That(TableExists(p, "OnlineAccounts"), Is.True);
                Assert.That(TableExists(p, "Accounts"), Is.False);
            }
        }

        [Test]
        public void ApplyTo_CreatesAccountsAsAStrictTableWithA64BitId()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                using (var cmd = new SQLiteCommand(
                    "SELECT strict FROM pragma_table_list WHERE name = 'Accounts';", p.Connection))
                {
                    Assert.That(Convert.ToInt32(cmd.ExecuteScalar()), Is.EqualTo(1));
                }

                using (var cmd = new SQLiteCommand(
                    "SELECT type FROM pragma_table_info('Accounts') WHERE name = 'Id';", p.Connection))
                {
                    Assert.That(Convert.ToString(cmd.ExecuteScalar()), Is.EqualTo("INTEGER"));
                }
            }
        }

        [Test]
        public void ApplyTo_ToALowerVersionThanCurrent_IsANoOpNotADowngrade()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.ApplyTo(1);

                Assert.That(p.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void ApplyTo_AboveTheLatestKnownVersion_Throws()
        {
            using (var p = OpenInMemory())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => p.ApplyTo(SchemaStepCatalog.LatestVersion + 1));
            }
        }

        private static bool TableExists(SqliteMoneyStoreProvisioner p, string name)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n;", p.Connection))
            {
                cmd.Parameters.AddWithValue("@n", name);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
