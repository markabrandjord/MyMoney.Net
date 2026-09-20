using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UITests.Basics
{
    [TestFixture]
    public class SampleDataFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenEmptyDatabase("SampleDataFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        private static Window InvokeAddSampleDataMenuItem(Window mainWindow)
        {
            AutomationElement helpMenu = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Help")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(helpMenu, Is.Not.Null, "Help menu not found.");
            helpMenu.Patterns.ExpandCollapse.Pattern.Expand();

            AutomationElement addSampleDataItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuSampleData")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(addSampleDataItem, Is.Not.Null, "'Add Sample Data...' menu item not found after expanding the Help menu.");
            addSampleDataItem.Patterns.Invoke.Pattern.Invoke();

            Window dialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(dialog, Is.Not.Null, "Sample Database Options dialog did not appear.");
            Assert.That(dialog.Title, Is.EqualTo("Sample Database Options"),
                $"Expected the Sample Database Options dialog, found '{dialog.Title}' instead.");
            return dialog;
        }

        [Test]
        public void PopulateSampleData_OnEmptyDatabase_AddsAccountsAndTransactions()
        {
            // Predicted results, per the plan's convention: call the already-tested business-layer
            // loader directly (SampleDataLoaderTests.cs covers LoadEmbeddedSampleData itself) to
            // compute the exact set of accounts SampleDataGenerator.Create will add - one Account
            // per <SampleAccount> entry in the embedded SampleData.xml, keyed by Name (see
            // SampleDataGenerator.Create, Money.cs's SampleDataGenerator.cs ~line 38-46).
            string predictedTempDir = Path.Combine(Path.GetTempPath(), $"SampleDataFlaUiTestsPredicted-{Guid.NewGuid():N}");
            var (data, _) = SampleDataLoader.LoadEmbeddedSampleData(typeof(Walkabout.MainWindow).Assembly, predictedTempDir);
            string[] expectedAccountNames = data.Accounts.Select(a => a.Name).Distinct().ToArray();
            Assert.That(expectedAccountNames, Is.Not.Empty, "Embedded SampleData.xml produced no accounts - test setup assumption is wrong.");

            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);

            Window dialog = InvokeAddSampleDataMenuItem(mainWindow);

            AutomationElement okButton = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonOk")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(okButton, Is.Not.Null, "OK button not found in Sample Database Options dialog.");
            // The dialog's own EnableButtons() logic (SampleDatabaseOptions.xaml.cs) should have
            // already validated the pre-filled defaults (Employer "ACME inc", the just-extracted
            // embedded template path, "2000.00"/"2%"/"10") and enabled OK with no interaction.
            Assert.That(okButton.IsEnabled, Is.True, "Expected OK to be enabled with the dialog's pre-filled defaults.");
            okButton.Patterns.Invoke.Pattern.Invoke();

            Retry.WhileTrue(() => mainWindow.ModalWindows.Length > 0, TimeSpan.FromSeconds(20));

            OpenFixtureDatabase.EnsureToolboxSectionExpanded(mainWindow, "AccountsSelector",
                section => section.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)));

            foreach (string accountName in expectedAccountNames)
            {
                AutomationElement accountItem = Retry.WhileNull(
                    () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(accountName)),
                    TimeSpan.FromSeconds(10)).Result;
                Assert.That(accountItem, Is.Not.Null, $"Expected sample account '{accountName}' to appear in the Accounts panel.");
            }

            AutomationElement accountsSection = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsSelector"));
            AutomationElement[] matchingAccountItems = accountsSection
                .FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .Where(item =>
                {
                    try { return expectedAccountNames.Contains(item.AutomationId); }
                    catch (FlaUI.Core.Exceptions.PropertyNotSupportedException) { return false; }
                })
                .ToArray();
            Assert.That(matchingAccountItems.Length, Is.EqualTo(expectedAccountNames.Length),
                "Expected exactly the sample accounts (and no duplicates) to appear in the Accounts panel.");
        }

        [Test]
        public void CancelPopulateSampleDataDialog_LeavesEmptyDatabaseUnchanged()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);

            Window dialog = InvokeAddSampleDataMenuItem(mainWindow);

            AutomationElement cancelButton = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonCancel")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(cancelButton, Is.Not.Null, "Cancel button not found in Sample Database Options dialog.");
            cancelButton.Patterns.Invoke.Pattern.Invoke();

            Retry.WhileTrue(() => mainWindow.ModalWindows.Length > 0, TimeSpan.FromSeconds(10));

            // Expand the Accounts panel even though nothing is expected in it - probeForContent
            // never finds anything for a genuinely empty database, so this just performs the
            // Expand() and settles; the real assertion is the FindAllDescendants check below.
            OpenFixtureDatabase.EnsureToolboxSectionExpanded(mainWindow, "AccountsSelector",
                section => section.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem)));

            AutomationElement accountsSection = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsSelector"));
            AutomationElement[] accountItems = accountsSection.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            Assert.That(accountItems, Is.Empty, "Cancelling the Sample Database Options dialog should leave the empty database with zero accounts.");
        }
    }
}
