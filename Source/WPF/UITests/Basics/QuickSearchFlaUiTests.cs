using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    [TestFixture]
    public class QuickSearchFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("QuickSearchFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        [Test]
        public void TypingIntoQuickSearch_FiltersTransactionGridToMatchingPayeeOnly()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.SelectAccount(mainWindow, "Basics Checking");

            // Expected result is source-derived: BasicsFixtureBuilder seeds exactly one
            // "Basics Checking" transaction whose payee is "AMC THEATRES 1234" (see
            // BasicsFixtureBuilder.cs), among 5 total (SAFEWAY, AMC, Alaska Airlines split,
            // a transfer, and a Target duplicate pair).
            //
            // The Quick Search box's real AutomationId is "InputFilterText" (falls back from
            // its x:Name in Controls/QuickFilterControl.xaml - confirmed by reading the XAML,
            // not the "QuickFilterTextBox" placeholder id). It filters on TextChanged wired to
            // a KeyUp handler (OnTextBox_KeyUp/OnInputFilterText_TextChanged), so this needs
            // real per-keystroke input (Enter(), which simulates keyboard input) rather than
            // AutomationElement.Patterns.Value.Pattern.SetValue, which wouldn't fire KeyUp.
            AutomationElement quickSearchBox = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("InputFilterText")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(quickSearchBox, Is.Not.Null, "Quick Search box ('InputFilterText') not found.");

            // The control only applies the filter on a literal Enter keypress
            // (QuickFilterControl.xaml.cs's OnTextBox_KeyUp checks e.Key == Key.Enter -
            // TextChanged alone only toggles the clear-filter (X) button's visibility) -
            // confirmed by reading the code-behind after Enter("AMC") alone left all 6 rows
            // visible. AsTextBox().Enter(text) types the text but does not itself send Enter.
            quickSearchBox.AsTextBox().Enter("AMC");
            FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            Wait.UntilInputIsProcessed();

            // The grid always renders one extra "{NewItemPlaceholder}" DataItem (DataGrid's
            // add-new-row affordance) regardless of the quick filter, so real transaction rows
            // are the ones whose Name starts with "Transaction:" (confirmed via a tree dump -
            // e.g. "Transaction: AMC THEATRES 1234 on 1/5/2026 for -32.5").
            AutomationElement[] transactionRows = Retry.WhileEmpty(
                () => mainWindow.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.DataItem))
                    .Where(r => r.Name != null && r.Name.StartsWith("Transaction:", StringComparison.Ordinal))
                    .ToArray(),
                TimeSpan.FromSeconds(5)).Result ?? Array.Empty<AutomationElement>();

            Assert.That(transactionRows.Length, Is.EqualTo(1), "Expected exactly 1 visible transaction row after filtering to 'AMC'.");
            Assert.That(transactionRows[0].Name, Does.Contain("AMC"));
        }
    }
}
