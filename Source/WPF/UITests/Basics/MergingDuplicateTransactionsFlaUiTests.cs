using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    [TestFixture]
    public class MergingDuplicateTransactionsFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("MergingDuplicateTransactionsFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        [Test]
        public void ClickingMergeOnDuplicateConnector_CollapsesToOneTransaction()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.SelectAccount(mainWindow, "Basics Checking");

            // Expected result is source-derived: BasicsFixtureBuilder.cs seeds exactly 2
            // near-duplicate "TARGET T-1234" transactions (same date/amount, different FITID).
            // TransactionsView.ShowPotentialDuplicates (fired off a short DispatcherTimer delay
            // after selection) auto-shows a TransactionConnectorAdorner - a code-created
            // RoundedButton with Content = "Merge" (no explicit AutomationId; its automation
            // Name defaults to that Content string) - once a selected transaction has a nearby
            // FindPotentialDuplicate match. Confirmed via screenshots during Task 11's work that
            // this connector appears automatically (no explicit click-to-select-a-row-first step
            // needed beyond opening the account) because the grid's default selection already
            // lands on one of the two TARGET T-1234 rows.
            int rowsBefore = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
                .Count(r => r.Name != null && r.Name.StartsWith("Transaction: TARGET T-1234", StringComparison.Ordinal));
            Assert.That(rowsBefore, Is.EqualTo(2), "Expected both seeded TARGET T-1234 duplicates before merging.");

            AutomationElement mergeButton = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Merge"))),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(mergeButton, Is.Not.Null, "Expected the duplicate-connector's 'Merge' button to auto-appear.");
            mergeButton.Patterns.Invoke.Pattern.Invoke();
            Wait.UntilInputIsProcessed();

            int rowsAfter = Retry.WhileNull(() =>
            {
                int count = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
                    .Count(r => r.Name != null && r.Name.StartsWith("Transaction: TARGET T-1234", StringComparison.Ordinal));
                return count == 1 ? (int?)count : null;
            }, TimeSpan.FromSeconds(5)).Result ?? -1;

            Assert.That(rowsAfter, Is.EqualTo(1), "Expected exactly 1 TARGET T-1234 row after merging the duplicates.");
        }
    }
}
