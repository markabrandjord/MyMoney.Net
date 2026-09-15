using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    [TestFixture]
    public class MockDatabaseSmokeTests
    {
        [Test]
        public void SaveThenLoad_ReturnsADifferentObjectInstance()
        {
            MockDatabase db = new MockDatabase();
            db.Create();
            MyMoney original = new MyMoney();
            original.Accounts.AddAccount("Checking");

            db.Save(original);
            MyMoney reloaded = db.Load(null);

            // Proves this is a real round-trip, not an object-identity
            // passthrough - a passthrough would return the exact same
            // reference, which would defeat the point of the contract suite.
            Assert.That(reloaded, Is.Not.SameAs(original));
            Assert.That(reloaded.Accounts.FindAccount("Checking"), Is.Not.Null);
        }
    }
}
