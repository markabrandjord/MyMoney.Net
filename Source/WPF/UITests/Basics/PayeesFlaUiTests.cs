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
    public class PayeesFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("PayeesFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        // Selects the fixture's one "AMC THEATRES 1234" transaction (BasicsFixtureBuilder.cs)
        // in the "Basics Checking" register and right-clicks it to open the context menu, then
        // invokes Rename Payee (TransactionsView.xaml's menuItemRenamePayee, requires a selected
        // non-readonly transaction per CanExecute_RenamePayee). The dialog is a genuine modal
        // Window (unlike Categories' inline tree-edit) with a real title bar - AutomationIds
        // confirmed by reading RenamePayeeDialog.xaml/.xaml.cs and a live tree dump:
        // textBox1 (From), comboBox1/PART_EditableTextBox (To), checkBoxUseRegex, checkBoxAuto,
        // okButton, cancelButton, and the title bar's "Close" button.
        private static Window OpenRenamePayeeDialogForAmcTransaction(Window mainWindow)
        {
            OpenFixtureDatabase.SelectAccount(mainWindow, "Basics Checking");

            AutomationElement amcRow = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem).And(
                    cf.ByName("Transaction: AMC THEATRES 1234 on 1/5/2026 for -32.5"))),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(amcRow, Is.Not.Null, "Expected the AMC THEATRES 1234 transaction row.");
            amcRow.Click();
            Wait.UntilInputIsProcessed();
            amcRow.RightClick();
            Wait.UntilInputIsProcessed();

            AutomationElement renameItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("menuItemRenamePayee")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(renameItem, Is.Not.Null, "'Rename Payee' context menu item not found.");
            renameItem.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            Window dialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(dialog, Is.Not.Null, "Rename Payee dialog did not appear.");
            Assert.That(dialog.Title, Is.EqualTo("Rename Payee"));
            return dialog;
        }

        private static void SetRenameToText(Window dialog, string newName)
        {
            // comboBox1's "To" field starts pre-filled with the same payee name (RenamePayeeDialog.
            // ShowDialogRenamePayee(payee) passes renameToThisPayee = fromPayee), so it needs
            // clearing (Ctrl+A) before typing, same as the Categories rename box gotcha.
            AutomationElement toBox = dialog.FindFirstDescendant(cf => cf.ByAutomationId("PART_EditableTextBox"));
            Assert.That(toBox, Is.Not.Null, "'To' combo box edit control not found.");
            toBox.Click();
            Wait.UntilInputIsProcessed();
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            Keyboard.Type(newName);
            Wait.UntilInputIsProcessed();
        }

        private static AutomationElement FindTransactionRow(Window mainWindow, string payeeSubstring)
        {
            return Retry.WhileNull(() =>
            {
                foreach (var row in mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem)))
                {
                    if (row.Name != null && row.Name.StartsWith("Transaction:", StringComparison.Ordinal) && row.Name.Contains(payeeSubstring))
                    {
                        return row;
                    }
                }
                return null;
            }, TimeSpan.FromSeconds(5)).Result;
        }

        [Test]
        public void RenamePayeeWithAutoRename_CreatesAliasAndRenamesTransaction()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            Window dialog = OpenRenamePayeeDialogForAmcTransaction(mainWindow);

            SetRenameToText(dialog, "AMC Theatres");

            AutomationElement autoCheckBox = dialog.FindFirstDescendant(cf => cf.ByAutomationId("checkBoxAuto"));
            Assert.That(autoCheckBox, Is.Not.Null, "Auto-Rename checkbox not found.");
            autoCheckBox.Patterns.Toggle.Pattern.Toggle();
            Wait.UntilInputIsProcessed();

            AutomationElement okButton = dialog.FindFirstDescendant(cf => cf.ByAutomationId("okButton"));
            Assert.That(okButton, Is.Not.Null, "OK button not found.");
            okButton.Patterns.Invoke.Pattern.Invoke();

            // OnOkButton_Click shows a "no matching transactions" warning if FindAliasMatches
            // comes back empty before closing - shouldn't happen here (the AMC transaction is a
            // real, current match), but assert the dialog actually closed rather than assume it,
            // so a real regression here fails loudly instead of the later assertions just finding
            // stale state.
            Assert.That(Retry.WhileTrue(() => mainWindow.ModalWindows.Length > 0, TimeSpan.FromSeconds(5)).Success, Is.True,
                "Rename Payee dialog should have closed after OK.");

            // Expected result is source-derived: BasicsFixtureBuilder.cs already seeds an Alias
            // with Pattern "AMC THEATRES 1234" (mapped to the AMC payee) for the Payees/Aliases
            // regex-consolidation scenario - so this rename (same pattern, Auto-Rename checked)
            // re-points that EXISTING alias to the new "AMC Theatres" payee rather than adding a
            // new one (confirmed by reading OnOkButton_Click: it calls Aliases.FindAlias(pattern)
            // first and only AddAlias's when none is found).
            AutomationElement renamedRow = FindTransactionRow(mainWindow, "AMC Theatres");
            Assert.That(renamedRow, Is.Not.Null, "Expected the transaction's payee to now show 'AMC Theatres'.");

            AutomationElement viewMenu = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuView")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(viewMenu, Is.Not.Null, "View menu not found.");
            viewMenu.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();
            AutomationElement aliasesItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuViewOnlineAliases")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(aliasesItem, Is.Not.Null, "'Aliases' menu item not found under View.");
            aliasesItem.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            AutomationElement aliasGrid = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AliasDataGrid")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(aliasGrid, Is.Not.Null, "AliasDataGrid not found after navigating to View | Aliases.");

            AutomationElement amcAliasPayeeCell = Retry.WhileNull(
                () => aliasGrid.FindFirstDescendant(cf => cf.ByName("AMC Theatres")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(amcAliasPayeeCell, Is.Not.Null, "Expected the 'AMC THEATRES 1234' alias's Payee column to now show 'AMC Theatres'.");
        }

        [Test]
        public void CancelRenamePayeeDialog_LeavesPayeeUnchanged()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            Window dialog = OpenRenamePayeeDialogForAmcTransaction(mainWindow);

            SetRenameToText(dialog, "Should Not Apply");

            AutomationElement cancelButton = dialog.FindFirstDescendant(cf => cf.ByAutomationId("cancelButton"));
            Assert.That(cancelButton, Is.Not.Null, "Cancel button not found.");
            cancelButton.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            Assert.That(Retry.WhileTrue(() => mainWindow.ModalWindows.Length > 0, TimeSpan.FromSeconds(5)).Success, Is.True,
                "Rename Payee dialog should have closed after Cancel.");

            AutomationElement unchangedRow = FindTransactionRow(mainWindow, "AMC THEATRES 1234");
            Assert.That(unchangedRow, Is.Not.Null, "Payee should be unchanged after Cancel.");
            Assert.That(FindTransactionRow(mainWindow, "Should Not Apply"), Is.Null);
        }

        [Test]
        public void TitleBarCloseOnRenamePayeeDialog_LeavesPayeeUnchanged()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            Window dialog = OpenRenamePayeeDialogForAmcTransaction(mainWindow);

            SetRenameToText(dialog, "Should Not Apply Either");

            // RenamePayeeDialog is a genuine top-level modal Window (WindowStyle="ToolWindow"),
            // unlike Categories' inline tree-edit which has no title bar at all - close it via
            // its title bar's standard "Close" button instead of the dialog's own Cancel button,
            // per the dialog-lifecycle convention's second check.
            AutomationElement closeButton = dialog.FindFirstDescendant(cf => cf.ByAutomationId("Close"));
            Assert.That(closeButton, Is.Not.Null, "Title bar Close button not found.");
            closeButton.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            Assert.That(Retry.WhileTrue(() => mainWindow.ModalWindows.Length > 0, TimeSpan.FromSeconds(5)).Success, Is.True,
                "Rename Payee dialog should have closed after title-bar Close.");

            AutomationElement unchangedRow = FindTransactionRow(mainWindow, "AMC THEATRES 1234");
            Assert.That(unchangedRow, Is.Not.Null, "Payee should be unchanged after title-bar Close.");
            Assert.That(FindTransactionRow(mainWindow, "Should Not Apply Either"), Is.Null);
        }
    }
}
