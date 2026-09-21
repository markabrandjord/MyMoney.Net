using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

// Same UIA-pattern-only approach as AddAccountFlaUiTests - see that file's header comment.
[TestFixture]
public class DeleteAccountFlaUiTests
{
    private ShellSession session;

    [TearDown]
    public void TearDown()
    {
        try
        {
            ShellTestApp.AssertNoCrashOccurred(this.session);
        }
        finally
        {
            this.session?.Dispose();
            this.session = null;
        }
    }

    private static void OpenAccountsPage(Window mainWindow)
    {
        mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NavAccountsButton"))
            .Patterns.Invoke.Pattern.Invoke();
        Retry.WhileNull(() => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsPage")), TimeSpan.FromSeconds(5));
    }

    private static void AddAccount(Window mainWindow, string name)
    {
        mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountButton")).Patterns.Invoke.Pattern.Invoke();
        var nameBox = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountNameTextBox")),
            TimeSpan.FromSeconds(5)).Result;
        nameBox.Patterns.Value.Pattern.SetValue(name);
        mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")))
            .Patterns.Invoke.Pattern.Invoke();
        Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountNameTextBox")),
            TimeSpan.FromSeconds(5));
    }

    // See EditAccountFlaUiTests.SelectAccountRow for why this uses DataItem (not ListItem,
    // confirmed by dumping the real tree) and filters by Contains in C# rather than an exact
    // UIA ByName match - the row's automation Name is the full AccountRowViewModel ToString().
    private static AutomationElement SelectAccountRow(Window mainWindow, string accountName)
    {
        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        var row = Retry.WhileNull(
            () => listView.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
                .FirstOrDefault(r => r.Name != null && r.Name.Contains(accountName)),
            TimeSpan.FromSeconds(5)).Result;
        row.Patterns.SelectionItem.Pattern.Select();
        return row;
    }

    [Test]
    public void DeletingAnAccount_RemovesItFromTheListAndReportsInTheStatusBar()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");
        AddAccount(mainWindow, "Savings");
        SelectAccountRow(mainWindow, "Checking");

        var deleteButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DeleteAccountButton").And(cf.ByControlType(ControlType.Button))),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(deleteButton.IsEnabled, Is.True, "Selecting a row must enable the Delete button.");
        deleteButton.Patterns.Invoke.Pattern.Invoke();

        var confirmText = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DeleteAccountConfirmText")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(confirmText.Name, Does.Contain("Checking"), "The confirmation must name the account being deleted.");

        mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Delete")))
            .Patterns.Invoke.Pattern.Invoke();
        Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DeleteAccountConfirmText")),
            TimeSpan.FromSeconds(5));

        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        Assert.That(listView, Is.Not.Null);

        // Not "no element named 'Checking' anywhere" - both accounts were added with the
        // default AccountType.Checking, so the surviving "Savings" row's own Type column also
        // legitimately says "Checking". What proves the deletion is exactly one row remaining,
        // and that row being the survivor.
        var rows = listView.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
        Assert.That(rows, Has.Length.EqualTo(1), "Deleting one account must leave exactly the other.");
        Assert.That(rows[0].Name, Does.Contain("Savings"));
        Assert.That(rows[0].Name, Does.Not.Contain("Name = Checking"));

        var statusText = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("StatusText"));
        Assert.That(statusText.Name, Is.EqualTo("Account deleted: Checking"));
    }

    [Test]
    public void CancellingADelete_LeavesTheAccountInPlace()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");
        SelectAccountRow(mainWindow, "Checking");

        mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DeleteAccountButton")).Patterns.Invoke.Pattern.Invoke();
        Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DeleteAccountConfirmText")),
            TimeSpan.FromSeconds(5));

        mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel")))
            .Patterns.Invoke.Pattern.Invoke();
        Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DeleteAccountConfirmText")),
            TimeSpan.FromSeconds(5));

        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        var survivingRow = listView.FindFirstDescendant(cf => cf.ByName("Checking"));
        Assert.That(survivingRow, Is.Not.Null, "Cancelling the delete must leave the account in place.");
    }
}
