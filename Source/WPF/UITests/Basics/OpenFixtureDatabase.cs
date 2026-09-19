using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    internal static class OpenFixtureDatabase
    {
        internal static void Open(Window mainWindow, string registeredName)
        {
            AutomationElement fileMenu = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("File")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(fileMenu, Is.Not.Null, "File menu not found.");
            fileMenu.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();

            AutomationElement openItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuFileOpen")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openItem, Is.Not.Null, "'Open...' menu item not found after expanding the File menu.");
            openItem.Patterns.Invoke.Pattern.Invoke();

            Window openDialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openDialog, Is.Not.Null, "Open Database dialog did not appear.");
            Assert.That(openDialog.Title, Is.EqualTo("Open Database"),
                $"Expected the app's registry-backed 'Open Database' dialog, but found a different modal window instead (Name: '{openDialog.Name}').");

            var listBox = openDialog.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.List)).AsListBox();
            Assert.That(listBox.Items.Length, Is.GreaterThan(0), $"Open Database dialog's list is empty - '{registeredName}' was not found.");
            listBox.Select(registeredName);

            AutomationElement openButton = Retry.WhileNull(
                () => openDialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonOk")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openButton, Is.Not.Null, "Open button not found in Open dialog.");
            openButton.Patterns.Invoke.Pattern.Invoke();

            Retry.WhileFalse(
                () => mainWindow.Title.IndexOf(registeredName, StringComparison.OrdinalIgnoreCase) >= 0,
                TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Selects an account in the left Accounts panel so its transaction register renders in
        /// the grid. Opening a database does NOT auto-select an account or show any register -
        /// confirmed by dumping the automation tree after Open() with nothing selected: the
        /// DataGrid has column headers but zero DataItem rows, and the Accounts panel's account
        /// ListItems ("Basics Checking" etc., AutomationId = account name) aren't even in the
        /// tree until its collapsible "AccountsSelector" section (MainWindow.xaml.cs's toolBox)
        /// is expanded via its "HeaderSite" button (a plain Button - Invoke pattern throws
        /// InvalidOperationException, use Click() instead).
        /// The BasicsAppSession app/window is reused across every test in a fixture (one launch
        /// per [SetUpFixture]), so a section expanded by an earlier test stays expanded - this
        /// probes for the account item first before clicking, rather than blindly toggling
        /// (which would re-collapse an already-expanded section).
        /// </summary>
        internal static void SelectAccount(Window mainWindow, string accountName)
        {
            AutomationElement accountItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(accountName)),
                TimeSpan.FromSeconds(1)).Result;

            if (accountItem == null)
            {
                AutomationElement accountsSelector = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsSelector"));
                Assert.That(accountsSelector, Is.Not.Null, "AccountsSelector section not found.");
                AutomationElement headerSite = accountsSelector.FindFirstDescendant(cf => cf.ByAutomationId("HeaderSite"));
                Assert.That(headerSite, Is.Not.Null, "AccountsSelector's HeaderSite (expand/collapse) button not found.");
                headerSite.Click();

                accountItem = Retry.WhileNull(
                    () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(accountName)),
                    TimeSpan.FromSeconds(5)).Result;
            }

            Assert.That(accountItem, Is.Not.Null, $"Account '{accountName}' not found in the Accounts panel.");
            accountItem.Patterns.SelectionItem.Pattern.Select();
            Wait.UntilInputIsProcessed();
        }
    }
}
