using System.Data.SQLite;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SchemaDriftTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory() =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "test", SqliteConnectionFactory.InMemoryDataSource, true));

        [Test]
        public void ApplyTo_RefusesWhenAnAlreadyAppliedStepsChecksumNoLongerMatches()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                // Simulate "someone edited step 2 and re-paved somewhere else": the ledger in this
                // database records a checksum this build's step 2 no longer produces.
                using (var cmd = new SQLiteCommand(
                    "UPDATE __SchemaHistory SET ChecksumHash = 'TAMPERED' WHERE Version = 2;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                var ex = Assert.Throws<SchemaDriftException>(() => p.ApplyTo(SchemaStepCatalog.LatestVersion));

                Assert.That(ex.Message, Does.Contain("2"));
                Assert.That(ex.Message, Does.Contain("fixed by a new step"));
            }
        }

        [Test]
        public void ApplyTo_RefusesWhenTheDatabaseRecordsAStepThisBuildDoesNotCarry()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                using (var cmd = new SQLiteCommand(
                    "INSERT INTO __SchemaHistory VALUES (@v, 'from_the_future', 'X', '2026-01-01 00:00:00.000', 'someone');",
                    p.Connection))
                {
                    cmd.Parameters.AddWithValue("@v", SchemaStepCatalog.LatestVersion + 1);
                    cmd.ExecuteNonQuery();
                }

                Assert.Throws<SchemaDriftException>(() => p.ApplyTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void ApplyTo_OnAnUntamperedDatabase_DoesNotThrow()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.DoesNotThrow(() => p.ApplyTo(SchemaStepCatalog.LatestVersion));
            }
        }
    }
}
