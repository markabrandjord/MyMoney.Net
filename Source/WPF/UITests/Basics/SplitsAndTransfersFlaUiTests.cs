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
    public class SplitsAndTransfersFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("SplitsAndTransfersFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        [Test]
        public void F6OnOutOfBalanceSplit_FillsRemainingAmount()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.SelectAccount(mainWindow, "Basics Checking");

            // Expected result is source-derived: BasicsFixtureBuilder.cs seeds exactly one split
            // transaction - "Alaska Airlines", total -100.00, with one existing split of -40.00
            // (Category "Fun") - so NonNullSplits.Unassigned is -60.00.
            AutomationElement splitRow = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem).And(
                    cf.ByName("Transaction: Alaska Airlines on 1/10/2026 for -100"))),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(splitRow, Is.Not.Null, "Expected the Alaska Airlines split transaction row.");

            // The fixture also seeds a near-duplicate TARGET T-1234 pair (for the Merging
            // Duplicate Transactions scenarios), which the app auto-selects/expands its own
            // Merge connector UI for as soon as the grid loads - a physical Click() on the
            // Alaska Airlines row was observed (via screenshot) to NOT move selection away from
            // that duplicate pair. SelectionItem.Pattern.Select() (a real UIA selection, not a
            // mouse-coordinate click) reliably does.
            splitRow.Patterns.SelectionItem.Pattern.Select();
            Wait.UntilInputIsProcessed();
            splitRow.RightClick();
            Wait.UntilInputIsProcessed();

            // TransactionsView.xaml's menuItemSplit (Command="CommandSplits") reveals the inline
            // split-details sub-grid (TheGridForAmountSplit) for the selected transaction.
            AutomationElement splitMenuItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("menuItemSplit")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(splitMenuItem, Is.Not.Null, "'Splits...' context menu item not found.");
            splitMenuItem.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            AutomationElement splitGrid = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TheGridForAmountSplit")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(splitGrid, Is.Not.Null, "Split details grid (TheGridForAmountSplit) not found.");

            // Columns are Payee(0)/Category(1)/Payment(2)/Deposit(3)/Memo(4) (TransactionsView.xaml's
            // myDataGridDetailView). Use the grid's own {NewItemPlaceholder} row (a fresh, empty
            // split with Amount 0) rather than the existing -40.00 split - F6 computes
            // s.Amount + Unassigned, so an empty split gives exactly the unassigned amount (60.00);
            // pressing it on the existing split would instead compute -40 + -60 = abs(-100).
            AutomationElement paymentCell = Retry.WhileNull(
                () => splitGrid.FindFirstDescendant(cf => cf.ByName("Item: {NewItemPlaceholder}, Column Display Index: 2")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(paymentCell, Is.Not.Null, "New split row's Payment cell not found.");
            paymentCell.Click();
            Wait.UntilInputIsProcessed();

            // myTemplateSplitPayment (read-only) is a TextBlock; only myTemplatePaymentEditInThe
            // SplitDetailedView (edit mode) is a TextBox - F6's handler looks for an editable
            // control in the current cell's content, so the cell must actually be in edit mode
            // first (confirmed by reading both DataTemplates).
            paymentCell.DoubleClick();
            Wait.UntilInputIsProcessed();

            // Search from splitGrid, not the pre-edit paymentCell handle - the edit-mode
            // template swap replaces the cell's visual subtree, leaving the old handle stale
            // (same gotcha documented in CLAUDE.md from CurrenciesFlaUiTests.cs).
            AutomationElement editBox = Retry.WhileNull(
                () => splitGrid.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(editBox, Is.Not.Null, "Payment cell's edit-mode TextBox never appeared after double-click.");

            Keyboard.Type(VirtualKeyShort.F6);
            Wait.UntilInputIsProcessed();

            Assert.That(editBox.AsTextBox().Text, Is.EqualTo("60.00"),
                "F6 should fill the empty split's Payment field with the full unassigned amount (60.00).");
        }
    }
}
