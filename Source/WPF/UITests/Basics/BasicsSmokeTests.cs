using FlaUI.Core.AutomationElements;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    [TestFixture]
    public class BasicsSmokeTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("BasicsSmokeTest Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        [Test]
        public void OpeningFreshScratchDatabase_ShowsSeededDataInWindowTitle()
        {
            Window mainWindow = BasicsAppSession.MainWindow;

            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);

            Assert.That(mainWindow.Title, Does.Contain(this.db.RegisteredName).IgnoreCase);
        }
    }
}
