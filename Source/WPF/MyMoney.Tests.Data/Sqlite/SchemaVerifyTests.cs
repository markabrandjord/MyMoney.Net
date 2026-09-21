using System.Data.SQLite;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SchemaVerifyTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory() =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "test", SqliteConnectionFactory.InMemoryDataSource, true));

        [Test]
        public void Verify_OnAFreshlyPavedDatabase_ReportsNoDrift()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                SchemaVerifyResult result = p.Verify();

                Assert.That(result.Version, Is.EqualTo(SchemaStepCatalog.LatestVersion));
                Assert.That(result.IsClean, Is.True,
                    string.Join("; ", result.Differences.Select(d => $"{d.ObjectKind} {d.ObjectName}: expected '{d.Expected}' actual '{d.Actual}'")));
            }
        }

        [Test]
        public void Verify_NoticesAMissingIndex()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                // Verify's expected set includes indexes, which is what issue #34 needed. Captured
                // BEFORE the drop, because IX_Accounts_Name is the only index the schema has, so
                // asserting after the drop would be asserting the opposite of what it says.
                Assert.That(SchemaSnapshot.Capture(p.Connection).Facts.Any(f => f.StartsWith("index:")),
                    Is.True, "Verify must introspect indexes, not just tables.");

                using (var cmd = new SQLiteCommand("DROP INDEX IF EXISTS IX_Accounts_Name;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                SchemaVerifyResult afterDrop = p.Verify();

                Assert.That(afterDrop.IsClean, Is.False, "A dropped index must be reported as drift.");
                Assert.That(afterDrop.Differences.Any(d => d.ObjectName.Contains("IX_Accounts_Name")), Is.True);
            }
        }

        [Test]
        public void Verify_NoticesAnAddedColumn()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                using (var cmd = new SQLiteCommand("ALTER TABLE Accounts ADD COLUMN Rogue TEXT;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                SchemaVerifyResult result = p.Verify();

                Assert.That(result.IsClean, Is.False);
                Assert.That(result.Differences.Any(d => d.ObjectName.Contains("Rogue")), Is.True);
            }
        }

        [Test]
        public void Verify_NoticesADroppedTable()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                using (var cmd = new SQLiteCommand("DROP TABLE Accounts;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                Assert.That(p.Verify().IsClean, Is.False);
            }
        }

        [Test]
        public void Snapshot_RecordsThatAccountsIsStrict()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(SchemaSnapshot.Capture(p.Connection).Facts,
                    Has.Some.EqualTo("table:Accounts|strict=1"));
            }
        }
    }
}
