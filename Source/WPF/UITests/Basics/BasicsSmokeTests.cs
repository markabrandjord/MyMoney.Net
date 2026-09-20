using System.IO;
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

            // MainWindow.UpdateCaption (MainWindow.xaml.cs) builds the title from the
            // underlying database file's name, not the registry's display name - the
            // registered name is only used to pick the entry in the Open dialog's list.
            Assert.That(mainWindow.Title, Does.Contain(Path.GetFileName(this.db.ScratchPath)).IgnoreCase);
        }
    }
}
