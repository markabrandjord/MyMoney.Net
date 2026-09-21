using System.Linq;
using NUnit.Framework;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    /// <summary>
    /// Slice 2b. The test that would actually have caught issue #34, generalised from indexes to
    /// every schema object. It runs on every build from here on - spec section 1.5, S-4 point 2.
    /// </summary>
    [TestFixture]
    public class SchemaEqualityTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory(string name) =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                name, SqliteConnectionFactory.InMemoryDataSource, true));

        [Test]
        public void ADatabaseBuiltFreshAtN_IsIdenticalToOneBuiltAtNMinusOneThenUpgraded()
        {
            int n = SchemaStepCatalog.LatestVersion;

            using (var fresh = OpenInMemory("fresh"))
            using (var upgraded = OpenInMemory("upgraded"))
            {
                fresh.ApplyTo(n);

                upgraded.ApplyTo(n - 1);
                upgraded.ApplyTo(n);

                SchemaSnapshot a = SchemaSnapshot.Capture(fresh.Connection);
                SchemaSnapshot b = SchemaSnapshot.Capture(upgraded.Connection);

                var differences = SchemaSnapshot.Diff(a, b);

                Assert.That(differences, Is.Empty,
                    "Fresh-at-N and upgraded-to-N disagree: "
                    + string.Join("; ", differences.Select(d => $"{d.ObjectKind} {d.ObjectName} expected '{d.Expected}' actual '{d.Actual}'")));
            }
        }

        [Test]
        public void EveryIntermediateVersionUpgradesToTheSameSchemaAsAFreshBuild()
        {
            // Not just N-1: a step that is only correct when applied straight after its immediate
            // predecessor is a bug the single-hop version would miss.
            int n = SchemaStepCatalog.LatestVersion;

            for (int start = 0; start < n; start++)
            {
                using (var fresh = OpenInMemory("fresh"))
                using (var upgraded = OpenInMemory("upgraded"))
                {
                    fresh.ApplyTo(n);
                    upgraded.ApplyTo(start);
                    upgraded.ApplyTo(n);

                    Assert.That(
                        SchemaSnapshot.Diff(
                            SchemaSnapshot.Capture(fresh.Connection),
                            SchemaSnapshot.Capture(upgraded.Connection)),
                        Is.Empty,
                        $"Upgrading from version {start} to {n} did not produce a fresh-at-{n} schema.");
                }
            }
        }

        [Test]
        public void TheCatalogContainsAnIndexAddedToAnAlreadyCreatedTable()
        {
            // The issue #34 shape, present on purpose. If a future refactor removes it, the two
            // tests above become much weaker without anything going red - so this pins it.
            using (var p = OpenInMemory("shape"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(SchemaSnapshot.Capture(p.Connection).Facts,
                    Has.Some.StartsWith("index:Accounts.IX_Accounts_Name|"));
            }

            SchemaStep indexStep = SchemaStepCatalog.All.Single(s => s.Name.Contains("accounts_name_index"));
            SchemaStep tableStep = SchemaStepCatalog.All.Single(s => s.Name.Contains("create_accounts"));

            Assert.That(indexStep.Version, Is.GreaterThan(tableStep.Version),
                "The index must be added by a LATER step than the one creating its table - that is "
                + "the case issue #34's CreateOrUpdateTable silently skipped.");
        }
    }
}
