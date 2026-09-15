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
    }
}
