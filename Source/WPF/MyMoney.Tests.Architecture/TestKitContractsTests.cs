using System;
using System.Linq;
using System.Reflection;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0. MyMoney.TestKit.Contracts exists to be ABSENT from the production dependency
    /// graph: production code cannot name IMoneyStoreTestControl, so it cannot implement or call
    /// it, and a copy-pasted implementation would not compile - spec sections 1 and 2.5 layer 4.
    /// The sweep over every production assembly lands in Task 22; this pins what is true the
    /// moment the assembly exists.
    /// </summary>
    [TestFixture]
    public class TestKitContractsTests
    {
        [Test]
        public void TestKitContracts_ReferencesOnlyMyMoneyBusiness()
        {
            var projectReferences = typeof(IMoneyStoreTestControl).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("MyMoney", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.That(projectReferences, Is.EqualTo(new[] { "MyMoney.Business" }));
        }

        [Test]
        public void MyMoneyBusiness_DoesNotReferenceTestKitContracts()
        {
            Assert.That(
                typeof(IMoneyStore).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.Contains("TestKit"),
                "MyMoney.Business referenced the test-tier contracts. The whole point of that "
                + "assembly is that production cannot name the type - spec section 2.5 layer 4.");
        }

        [Test]
        public void TableRef_CannotBeConstructedForAnUnmappedRootType()
        {
            Assert.Throws<ArgumentException>(() => TableRef.Of<Payee>());
        }

        [Test]
        public void TableRef_OfAccount_EqualsTheNamedOne()
        {
            Assert.That(TableRef.Of<Account>(), Is.EqualTo(TableRef.Accounts));
            Assert.That(TableRef.Accounts.Name, Is.EqualTo("Accounts"));
        }

        [Test]
        public void ClearOptions_DefaultResetsIdentityAndVerifiesForeignKeys()
        {
            // A record STRUCT's default value would silently give false here, on the exact option
            // spec 2.6.4 calls "the cross-engine trap". ClearOptions is a class for that reason.
            Assert.That(ClearOptions.Default.ResetIdentity, Is.True);
            Assert.That(ClearOptions.Default.VerifyForeignKeysAfter, Is.True);
        }
    }
}
