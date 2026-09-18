using NUnit.Framework;
using Walkabout.Taxes;

namespace Walkabout.Tests
{
    [TestFixture]
    public class TaxCategoryTests
    {
        [Test]
        public void ParseInt_ValidNumber_ReturnsParsedValue()
        {
            Assert.That(TaxCategory.ParseInt("42"), Is.EqualTo(42));
        }

        [Test]
        public void ParseInt_InvalidNumber_ReturnsZero()
        {
            Assert.That(TaxCategory.ParseInt("not-a-number"), Is.EqualTo(0));
        }

        [Test]
        public void ParseSign_KnownCodes_ReturnsExpectedSign()
        {
            Assert.That(TaxCategory.ParseSign("E"), Is.EqualTo(-1));
            Assert.That(TaxCategory.ParseSign("I"), Is.EqualTo(1));
        }

        [Test]
        public void ParseSign_UnknownCode_ReturnsZero()
        {
            Assert.That(TaxCategory.ParseSign("garbage"), Is.EqualTo(0));
        }

        [Test]
        public void ParseSortOrder_UnknownCode_ReturnsNone()
        {
            Assert.That(TaxCategory.ParseSortOrder("not-a-real-code"), Is.EqualTo(SortOrder.None));
        }

        [Test]
        public void Parse_WrongTokenCount_ReturnsNull()
        {
            // A well-formed line has 8 whitespace/quote-delimited tokens; this has 2.
            Assert.That(TaxCategory.Parse("1 2"), Is.Null);
        }
    }
}
