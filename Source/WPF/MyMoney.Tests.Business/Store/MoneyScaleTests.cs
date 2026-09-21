using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Business.Store
{
    [TestFixture]
    public class MoneyScaleTests
    {
        [TestCase("0", 0L)]
        [TestCase("1", 10000L)]
        [TestCase("-1", -10000L)]
        [TestCase("0.0001", 1L)]
        [TestCase("-0.0001", -1L)]
        [TestCase("1234.5678", 12345678L)]
        [TestCase("922337203685477.5807", 9223372036854775807L)]
        [TestCase("-922337203685477.5808", -9223372036854775808L)]
        public void ToStorage_MatchesSqlServerMoneysOwnRepresentation(string value, long expected)
        {
            Assert.That(MoneyScale.ToStorage(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
                Is.EqualTo(expected));
        }

        [TestCase("0")]
        [TestCase("1234.5678")]
        [TestCase("-0.0001")]
        [TestCase("922337203685477.5807")]
        public void RoundTrip_IsLossless(string value)
        {
            decimal d = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

            Assert.That(MoneyScale.FromStorage(MoneyScale.ToStorage(d)), Is.EqualTo(d));
        }

        [Test]
        public void ToStorage_RefusesMoreThanFourDecimalPlaces_RatherThanSilentlyRounding()
        {
            // Silent rounding is how a cent goes missing and nobody can say where. A money value
            // finer than SQL Server's money can express is a caller bug, not a storage detail.
            Assert.Throws<ArgumentOutOfRangeException>(() => MoneyScale.ToStorage(0.00005m));
        }

        [Test]
        public void ToStorage_RefusesAValueOutsideTheRepresentableRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MoneyScale.ToStorage(1000000000000000m));
        }
    }
}
