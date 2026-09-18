using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UnitTests
{
    public class SqlServerBootstrapperTests
    {
        [Test]
        public void SplitBatches_SplitsOnGoAndTrims()
        {
            string script = "SELECT 1;\r\nGO\r\n  SELECT 2;  \r\nGO\r\n";
            var batches = SqlServerBootstrapper.SplitBatches(script);
            Assert.That(batches, Is.EqualTo(new[] { "SELECT 1;", "SELECT 2;" }));
        }

        [Test]
        public void SplitBatches_SkipsUseStatements()
        {
            string script = "USE MyMoney;\r\nGO\r\nSELECT 1;\r\nGO\r\n";
            var batches = SqlServerBootstrapper.SplitBatches(script);
            Assert.That(batches, Is.EqualTo(new[] { "SELECT 1;" }));
        }

        [Test]
        public void SplitBatches_IgnoresEmptyBatches()
        {
            string script = "GO\r\nSELECT 1;\r\nGO\r\n\r\nGO\r\n";
            var batches = SqlServerBootstrapper.SplitBatches(script);
            Assert.That(batches, Is.EqualTo(new[] { "SELECT 1;" }));
        }
    }
}
