using System;
using System.Drawing;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    // Regression coverage for the WPF-UI migration's pending-changes Save split button
    // (MainWindow.xaml's "PendingChangeDropDown"). See CLAUDE.md/the SDD ledger for
    // 2026-09-19-ui-modernization-wpfui-migration: SplitButton.Flyout only works with a real
    // ContextMenu, not a Wpf.Ui.Controls.Flyout - an earlier attempt that compiled but left the
    // dropdown silently inert. This test exists specifically because that earlier defect had no
    // automated coverage at all (14/14 Basics tests stayed green while the control was broken).
    [TestFixture]
    public class PendingChangesFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("PendingChangesFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        [Test]
        public void PendingChangeDropDown_OpensClosesAndReopensAfterOutsideDismissal()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);

            AutomationElement splitButton = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PendingChangeDropDown")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(splitButton, Is.Not.Null, "'PendingChangeDropDown' split button not found.");

            var rect = splitButton.BoundingRectangle;
            var togglePoint = new Point((int)(rect.Right - 8), (int)(rect.Y + rect.Height / 2));

            // Open #1: the chevron half of the split button opens a real ContextMenu (see the
            // XAML comment at MainWindow.xaml's PendingChangeDropDown) hosting a StackPanel
            // ("pendingStack") and a "Revert Changes" button. PART_ToggleButton only reacts to
            // real mouse input (PreviewMouseLeftButtonUp with a hit test), not a UIA Invoke/Toggle
            // pattern - see flaui-wpf-testing skill.
            Mouse.Click(togglePoint);
            Wait.UntilInputIsProcessed();

            AutomationElement revertButton = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Revert Changes")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(revertButton, Is.Not.Null, "'Revert Changes' button not found after first chevron click - dropdown did not open.");

            // Dismiss by clicking elsewhere in the main window (light-dismiss ContextMenu).
            var elsewhere = new Point((int)mainWindow.BoundingRectangle.X + 50, (int)mainWindow.BoundingRectangle.Bottom - 50);
            Mouse.Click(elsewhere);
            Wait.UntilInputIsProcessed();

            Retry.WhileTrue(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Revert Changes")) != null,
                TimeSpan.FromSeconds(5));

            // Reopen #2: a real ContextMenu tracks its own open/close state internally, unlike an
            // earlier hand-toggled Flyout.IsOpen approach that desynced after an outside-click
            // dismissal and required two chevron clicks to reopen.
            Mouse.Click(togglePoint);
            Wait.UntilInputIsProcessed();

            AutomationElement revertButtonAgain = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Revert Changes")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(revertButtonAgain, Is.Not.Null,
                "Dropdown did not reopen on second chevron click after an outside-click dismissal.");

            // Leave it closed for the next test in this fixture.
            Mouse.Click(elsewhere);
            Wait.UntilInputIsProcessed();
        }
    }
}
