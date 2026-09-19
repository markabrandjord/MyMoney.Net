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

            // OnRenameNode_CommitAndStopEditing shows a MessageBoxEx ("Category \"Fun:Videos\"
            // already exist", title "Category") on collision - it's a real MODAL that blocks the
            // whole window. Missing this dismiss step was a real bug found the hard way: the
            // assertions below still pass without it (FindFirstDescendant reads the automation
            // tree fine even while a modal is up), but the dialog then sits open for the rest of
            // the shared app session, silently breaking every later FlaUI test in other fixtures
            // that also uses this same session (confirmed via a screenshot after an unrelated
            // Currencies test started failing for no apparent reason of its own).
            Window collisionDialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(collisionDialog, Is.Not.Null, "Expected the 'Category ... already exist' collision dialog to appear.");
            Assert.That(collisionDialog.Title, Is.EqualTo("Category"));

            AutomationElement stillMovies = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName("Fun:Movies")));
            Assert.That(stillMovies, Is.Not.Null, "'Fun:Movies' node should still exist - the rename-onto-an-existing-name should have been rejected, not silently merged.");
            Assert.That(mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName("Fun:Videos"))).Length, Is.EqualTo(1),
                "Expected exactly 1 'Fun:Videos' node - no duplicate created by the rejected rename.");

            AutomationElement okButton = collisionDialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonOK"));
            Assert.That(okButton, Is.Not.Null, "Collision dialog's OK button not found.");
            okButton.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();
            Assert.That(Retry.WhileTrue(() => mainWindow.ModalWindows.Length > 0, TimeSpan.FromSeconds(5)).Success, Is.True,
                "Collision dialog should be dismissed.");
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
