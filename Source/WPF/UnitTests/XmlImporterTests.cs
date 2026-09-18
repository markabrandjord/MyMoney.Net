using System;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Importers;

namespace Walkabout.Tests
{
    [TestFixture]
    public class XmlImporterTests
    {
        [Test]
        public void Import_UnsupportedExtension_ThrowsNotSupportedException()
        {
            var importer = new XmlImporter(new MyMoney(), null);
            Assert.Throws<NotSupportedException>(() => importer.Import("statement.qfx"));
        }
    }
}
