using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    [TestFixture]
    public class CategoriesFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("CategoriesFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        // The Categories TreeView is hierarchical and starts fully collapsed below the
        // top-level category nodes (confirmed via a tree dump: "Fun" has a nested
        // "ExpandCollapseChevron" button and no visible children until expanded). A TreeItem's
        // automation Name is the full Category.Name (e.g. "Fun:Movies", via Category.ToString())
        // - NOT the leaf Label ("Movies") shown by the item's TextBlock, which only shows up as
        // a separate nested Text element's own Name. So child lookups use the full dotted name.
        private static AutomationElement ExpandParentAndFindChild(Window mainWindow, string parentName, string childName)
        {
            AutomationElement parentNode = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName(parentName))),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(parentNode, Is.Not.Null, $"Expected '{parentName}' category node in the Categories panel.");
            parentNode.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();

            return Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName(childName))),
                TimeSpan.FromSeconds(5)).Result;
        }

        [Test]
        public void RenamingCategoryToExistingName_IsRejected()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.ExpandCategoriesPanel(mainWindow);

            // Expected result is source-derived: BasicsFixtureBuilder seeds both "Fun:Movies"
            // and "Fun:Videos" under the same parent, so attempting to rename Movies to
            // "Videos" must not create a second same-named sibling.
            AutomationElement moviesNode = ExpandParentAndFindChild(mainWindow, "Fun", "Fun:Movies");
            Assert.That(moviesNode, Is.Not.Null, "Expected 'Fun:Movies' category node under 'Fun'.");

            moviesNode.Click();
            Keyboard.Type(VirtualKeyShort.F2);
            Wait.UntilInputIsProcessed();
            // OnTextEditorForRenaming_Loaded (CategoriesControl.xaml.cs) sets CaretIndex to the
            // end of the existing text, not select-all - typing without first selecting
            // everything appends instead of replacing (confirmed: an earlier attempt without
            // this produced a real "MoviesVideos" category, not a rename attempt at all).
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Keyboard.Type("Videos");
            Keyboard.Type(VirtualKeyShort.ENTER);
            Wait.UntilInputIsProcessed();

            AutomationElement stillMovies = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName("Fun:Movies")));
            Assert.That(stillMovies, Is.Not.Null, "'Fun:Movies' node should still exist - the rename-onto-an-existing-name should have been rejected, not silently merged.");
            Assert.That(mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName("Fun:Videos"))).Length, Is.EqualTo(1),
                "Expected exactly 1 'Fun:Videos' node - no duplicate created by the rejected rename.");
        }

        [Test]
        public void CancelOnCategoryDialog_LeavesCategoryUnchanged()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.ExpandCategoriesPanel(mainWindow);

            AutomationElement moviesNode = ExpandParentAndFindChild(mainWindow, "Fun", "Fun:Movies");
            Assert.That(moviesNode, Is.Not.Null, "Expected 'Fun:Movies' category node under 'Fun'.");

            int categoryCountBefore = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem)).Length;

            moviesNode.Click();
            Keyboard.Type(VirtualKeyShort.F2);
            Wait.UntilInputIsProcessed();
            Keyboard.Type("Should Not Apply");
            Keyboard.Type(VirtualKeyShort.ESCAPE);
            Wait.UntilInputIsProcessed();

            AutomationElement stillMovies = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName("Fun:Movies")));
            Assert.That(stillMovies, Is.Not.Null, "Escape/Cancel during rename must leave the category name unchanged.");
            int categoryCountAfter = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem)).Length;
            Assert.That(categoryCountAfter, Is.EqualTo(categoryCountBefore), "Cancel must not change the category count.");
        }
    }
}
