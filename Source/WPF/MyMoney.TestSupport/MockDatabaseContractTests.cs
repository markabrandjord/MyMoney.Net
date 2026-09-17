using System;
using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    [TestFixture]
    public class MockDatabaseContractTests : DatabaseContractTests
    {
        protected override IDatabase CreateDatabase()
        {
            return new MockDatabase();
        }

        [Test]
        public void RestoreRowVersions_CoversEveryIAggregateRootType()
        {
            // Safety net: if a new IAggregateRoot type is ever added to Money.cs without a
            // matching entry in MockDatabase.RestoredAggregateRootCollections, that type's
            // RowVersion never gets restored on Load(), causing a permanent
            // ConcurrencyConflictException on every save - with no symptom that points here.
            HashSet<Type> restoredTypes = new HashSet<Type>();
            foreach (var entry in MockDatabase.RestoredAggregateRootCollections)
            {
                restoredTypes.Add(entry.RootType);
            }

            List<Type> allAggregateRootTypes = new List<Type>();
            foreach (Type t in typeof(IAggregateRoot).Assembly.GetTypes())
            {
                if (t.IsClass && !t.IsAbstract && typeof(IAggregateRoot).IsAssignableFrom(t))
                {
                    allAggregateRootTypes.Add(t);
                }
            }

            Assert.That(allAggregateRootTypes, Is.Not.Empty,
                "Sanity check: expected at least one IAggregateRoot implementor in the assembly.");

            foreach (Type t in allAggregateRootTypes)
            {
                Assert.That(restoredTypes.Contains(t), Is.True,
                    t.Name + " implements IAggregateRoot but MockDatabase.RestoredAggregateRootCollections " +
                    "has no entry for it - Load() would never restore its RowVersion.");
            }
        }
    }
}
