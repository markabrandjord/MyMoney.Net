using System.IO;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class SampleDataLoaderTests
    {
        [Test]
        public void LoadEmbeddedSampleData_ReturnsNonEmptyDataAndQuotes()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "MyMoneyTests", "SampleDataLoaderTests");
            var (data, quotes) = SampleDataLoader.LoadEmbeddedSampleData(typeof(Walkabout.MainWindow).Assembly, tempDir);

            Assert.That(data, Is.Not.Null);
            Assert.That(data.Accounts, Is.Not.Empty);
            Assert.That(data.Payees, Is.Not.Empty);
            Assert.That(data.Securities, Is.Not.Empty);
            Assert.That(quotes, Is.Not.Empty, "expected at least one security's quote history to load");
        }
    }
}
