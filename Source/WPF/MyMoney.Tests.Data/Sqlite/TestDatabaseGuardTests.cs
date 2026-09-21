using System;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;
using Walkabout.Data.Sqlite.TestTier;

// See SqliteMoneyStoreOpenTests: SqliteConnectionFactory is deliberately duplicated into both
// SQLite assemblies, so a project referencing both must say which copy it means.
using SqliteConnectionFactory = Walkabout.Data.Sqlite.Provisioning.SqliteConnectionFactory;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class TestDatabaseGuardTests
    {
        private static SqliteMoneyStoreProvisioner Open(bool isTestDatabase) =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "My Real Money", SqliteConnectionFactory.InMemoryDataSource, isTestDatabase));

        [Test]
        public void DropAll_OnANonTestDatabase_Refuses()
        {
            using (var p = Open(false))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                var ex = Assert.Throws<TestDatabaseRequiredException>(() => p.DropAll());

                Assert.That(ex.DatabaseDisplayName, Is.EqualTo("My Real Money"));
                Assert.That(ex.Capability, Is.Not.Empty);
                Assert.That(p.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion),
                    "A refusal must leave the schema untouched.");
            }
        }

        [Test]
        public void Delete_OnANonTestDatabase_Refuses()
        {
            using (var p = Open(false))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.Throws<TestDatabaseRequiredException>(() => p.Delete());
            }
        }

        [Test]
        public void AcquiringTestControl_OnANonTestDatabase_Refuses()
        {
            // Spec 2.6.4: the check is at the point the instance is HANDED OUT, once, not repeated
            // in twenty methods where the twenty-first is the one that forgets.
            using (var p = Open(false))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.Throws<TestDatabaseRequiredException>(() => SqliteTestControlFactory.Acquire(p));
            }
        }

        [Test]
        public void OnATestDatabase_EverythingIsAllowed()
        {
            using (var p = Open(true))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.DoesNotThrow(() => SqliteTestControlFactory.Acquire(p));
                Assert.DoesNotThrow(() => p.DropAll());
            }
        }

        [Test]
        public void TheFlagRoundTripsOntoStoreIdentity()
        {
            using (var p = Open(false))
            {
                Assert.That(p.Identity.IsTestDatabase, Is.False);
            }

            using (var p = Open(true))
            {
                Assert.That(p.Identity.IsTestDatabase, Is.True);
            }
        }

        [Test]
        public void TheRefusalMessageIsPlainLanguage()
        {
            // Spec section 7's UI rule: failures reach the user as plain language through the
            // callback port, never as a type name or a raw engine string.
            var identity = new StoreIdentity("My Real Money", DbFlavor.Sqlite, false, 4);

            var ex = Assert.Throws<TestDatabaseRequiredException>(
                () => TestDatabaseGuard.Require(identity, "Add sample data"));

            Assert.That(ex.Message, Does.Contain("My Real Money"));
            Assert.That(ex.Message, Does.Contain("Add sample data"));
            Assert.That(ex.Message, Does.Contain("test database"));
        }
    }
}
