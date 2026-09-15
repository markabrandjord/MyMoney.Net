using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class DataEnginePasswordGeneratorTests
    {
        [Test]
        public void Generate_DefaultLength_Is24Characters()
        {
            string password = DataEnginePasswordGenerator.Generate();
            Assert.That(password.Length, Is.EqualTo(24));
        }

        [Test]
        public void Generate_ContainsAtLeastOneOfEachRequiredClass()
        {
            string password = DataEnginePasswordGenerator.Generate();
            Assert.That(password.Any(char.IsUpper), Is.True);
            Assert.That(password.Any(char.IsLower), Is.True);
            Assert.That(password.Any(char.IsDigit), Is.True);
            Assert.That(password.Any(c => "!@#$%^&*-_=+".Contains(c)), Is.True);
        }

        [Test]
        public void Generate_TwoCallsProduceDifferentPasswords()
        {
            string a = DataEnginePasswordGenerator.Generate();
            string b = DataEnginePasswordGenerator.Generate();
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Generate_LengthBelowMinimum_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => DataEnginePasswordGenerator.Generate(4));
        }
    }
}
