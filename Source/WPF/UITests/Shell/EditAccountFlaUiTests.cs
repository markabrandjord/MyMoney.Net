using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

// Same UIA-pattern-only approach as AddAccountFlaUiTests - see that file's header comment.
[TestFixture]
public class EditAccountFlaUiTests
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

    /// <summary>
    /// A row's actual UIA ControlType is DataItem, not ListItem - confirmed by dumping the
    /// real tree (this ListView's GridView rows register as DataItem, not the ListItem type
    /// AddAccountFlaUiTests' row-name gotcha comment might suggest). Its Name is the full
    /// AccountRowViewModel record's ToString(), not the bare account name, and FlaUI's ByName
    /// does an exact match - so this finds every row and filters in plain C# with Contains
    /// instead of querying UIA for an exact match we can't spell reliably.
    /// </summary>
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
    public void EditingAnAccount_UpdatesTheListAndReportsInTheStatusBar()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");
        SelectAccountRow(mainWindow, "Checking");

        var editButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("EditAccountButton").And(cf.ByControlType(ControlType.Button))),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(editButton.IsEnabled, Is.True, "Selecting a row must enable the Edit button.");
        editButton.Patterns.Invoke.Pattern.Invoke();

        var nameBox = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("EditAccountNameTextBox")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(nameBox.Patterns.Value.Pattern.Value.ValueOrDefault, Is.EqualTo("Checking"),
            "The edit dialog must be pre-populated with the selected account's current name.");
        nameBox.Patterns.Value.Pattern.SetValue("Everyday Checking");

        mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Save")))
            .Patterns.Invoke.Pattern.Invoke();
        Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("EditAccountNameTextBox")),
            TimeSpan.FromSeconds(5));

        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        Assert.That(listView, Is.Not.Null);
        var renamedRow = Retry.WhileNull(
            () => listView.FindFirstDescendant(cf => cf.ByName("Everyday Checking")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(renamedRow, Is.Not.Null, "The renamed account must appear in the list under its new name.");

        // Not "no element named 'Checking' anywhere" - the Type column legitimately still says
        // "Checking" (AccountType.Checking, untouched by this edit), so that text element is a
        // true positive, not a leftover of the old name. What actually proves a rename (rather
        // than a second row having been added) is exactly one DataItem row existing.
        var rows = listView.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
        Assert.That(rows, Has.Length.EqualTo(1), "Editing must rename the existing row, not add a second one.");

        var statusText = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("StatusText"));
        Assert.That(statusText.Name, Is.EqualTo("Account updated: Everyday Checking"));
    }

    [Test]
    public void EditingAnAccountToADuplicateName_ShowsAnErrorAndKeepsTheDialogOpen()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");
        AddAccount(mainWindow, "Savings");
        SelectAccountRow(mainWindow, "Checking");

        mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("EditAccountButton")).Patterns.Invoke.Pattern.Invoke();
        var nameBox = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("EditAccountNameTextBox")),
            TimeSpan.FromSeconds(5)).Result;
        nameBox.Patterns.Value.Pattern.SetValue("Savings");

        mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Save")))
            .Patterns.Invoke.Pattern.Invoke();

        var stillOpenNameBox = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("EditAccountNameTextBox")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(stillOpenNameBox, Is.Not.Null, "A validation failure must re-show the dialog rather than close it.");

        var errorText = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("EditAccountErrorText")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(errorText, Is.Not.Null);
        Assert.That(errorText.Name, Does.Contain("Savings"));
    }
}
