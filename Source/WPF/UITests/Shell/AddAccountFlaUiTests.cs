using System;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

// Every interaction below goes through a UIA control pattern (Invoke/Value/SelectionItem)
// rather than FlaUI's simulated-input helpers (.Click(), Keyboard.Type). Those simulate mouse
// clicks and keystrokes via Win32 SendInput, which needs a genuinely interactive desktop -
// this session's sandboxed environment doesn't have one, and .Click() was independently
// confirmed to throw Win32Exception "Access is denied" (error 5) here. Pattern operations go
// through the app's own UIA automation provider and don't hit that restriction - see
// ShellChromeFlaUiTests/AccountsViewFlaUiTests for the same approach established in Task 6.
[TestFixture]
public class AddAccountFlaUiTests
{
    private static string ExePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "MyMoney.Shell",
        "bin", "Debug", "net10.0-windows7.0", "MyMoney.Shell.exe"));

    private static void OpenAccountsPage(FlaUI.Core.AutomationElements.Window mainWindow)
    {
        mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NavAccountsButton"))!
            .Patterns.Invoke.Pattern.Invoke();
        Retry.WhileNull(() => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsPage")), TimeSpan.FromSeconds(5));
    }

    private static FlaUI.Core.AutomationElements.AutomationElement OpenAddAccountDialog(FlaUI.Core.AutomationElements.Window mainWindow)
    {
        mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountButton"))!
            .Patterns.Invoke.Pattern.Invoke();
        return Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountNameTextBox")),
            TimeSpan.FromSeconds(5)).Result!;
    }

    [Test]
    public void CancellingAddAccount_LeavesTheListUnchangedAndTheDialogDoesNotRememberTheTypedName()
    {
        using var app = Application.Launch(ExePath);
        using var automation = new UIA3Automation();
        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result!;

        OpenAccountsPage(mainWindow);

        var nameBox = OpenAddAccountDialog(mainWindow);
        // Value pattern instead of Click()+Keyboard.Type() - Wpf.Ui.Controls.TextBox derives
        // directly from System.Windows.Controls.TextBox, so it gets the standard
        // TextBoxAutomationPeer, which implements IValueProvider (ValuePattern).
        nameBox.Patterns.Value.Pattern.SetValue("Should Not Be Added");

        // ContentDialog's Close/Cancel button - findable by its declared CloseButtonText.
        var cancelButton = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel"))),
            TimeSpan.FromSeconds(5)).Result!;
        cancelButton.Patterns.Invoke.Pattern.Invoke();

        // The dialog must actually be gone before we look for the (nonexistent) row - Invoke()
        // returns as soon as the click is dispatched, not once the dialog has closed.
        Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountNameTextBox")),
            TimeSpan.FromSeconds(5));

        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        Assert.That(listView, Is.Not.Null, "Sanity check: the list itself must be found, or the next assertion would pass vacuously.");
        var addedRow = listView!.FindFirstDescendant(cf => cf.ByName("Should Not Be Added"));
        Assert.That(addedRow, Is.Null, "Cancelling must not add the account to the list.");

        // D-13's real cancel-restore guarantee: re-opening the dialog after a cancel must not
        // show the previously typed value - it proves the dialog gets a genuinely fresh
        // AddAccountViewModel per open (Task 9's design), not a reused/reset one. Task 9's own
        // tests only ever checked a freshly-CONSTRUCTED view model's defaults; nothing until
        // now has exercised the actual "open, type, cancel, open again" round trip.
        var reopenedNameBox = OpenAddAccountDialog(mainWindow);
        var reopenedName = reopenedNameBox.Patterns.Value.Pattern.Value.ValueOrDefault;
        Assert.That(reopenedName, Is.Empty, "Re-opening the dialog after a cancel must not carry over the previously typed name.");

        app.Close();
    }

    [Test]
    public void AddingAnAccount_AppearsInTheList()
    {
        using var app = Application.Launch(ExePath);
        using var automation = new UIA3Automation();
        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result!;

        OpenAccountsPage(mainWindow);

        var nameBox = OpenAddAccountDialog(mainWindow);
        nameBox.Patterns.Value.Pattern.SetValue("Vacation Fund");

        var addButton = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
        Assert.That(addButton, Is.Not.Null);
        addButton!.Patterns.Invoke.Pattern.Invoke();

        // Not scoped to ControlType.ListItem: the GridView-backed row's own automation Name is
        // the AccountRowViewModel record's ToString() (e.g. "AccountRowViewModel { Id = ...,
        // Name = Vacation Fund, ... }"), not the bare account name, and FlaUI's ByName does an
        // exact match rather than Contains. The actual exact-match "Vacation Fund" lives on the
        // nested Text element that GridViewColumn's DisplayMemberBinding renders for the Name
        // column - found by dropping the ListItem constraint and letting the descendant search
        // reach that nested element instead. Confirmed empirically: the ListItem-scoped search
        // used in the brief's original code came back null against the real running app.
        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        Assert.That(listView, Is.Not.Null);
        var addedRow = Retry.WhileNull(
            () => listView!.FindFirstDescendant(cf => cf.ByName("Vacation Fund")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(addedRow, Is.Not.Null);

        app.Close();
    }

    // Covers the review-flagged dead-binding bug: Commit()'s validation failure used to set
    // ErrorMessage on a view whose ContentDialog had already closed, so AddAccountErrorText
    // could never actually render, and the most likely first thing a real user tries -
    // clicking "Add" with a blank name - was a silent no-op with no explanation. This is also
    // the most direct proof that Task 9's ArgumentException catch in Commit() (added for
    // exactly this blank/invalid-name case) is observable, not just internally recorded and
    // then discarded.
    [Test]
    public void AddingAnAccountWithABlankName_ShowsAnErrorAndKeepsTheDialogOpen()
    {
        using var app = Application.Launch(ExePath);
        using var automation = new UIA3Automation();
        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result!;

        OpenAccountsPage(mainWindow);

        // Name field starts blank (AddAccountViewModel's default) - go straight to "Add"
        // without typing anything.
        OpenAddAccountDialog(mainWindow);

        var addButton = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
        Assert.That(addButton, Is.Not.Null);
        addButton!.Patterns.Invoke.Pattern.Invoke();

        // The show-loop must have re-shown the SAME dialog rather than silently closing it -
        // the name text box should still be findable.
        var nameBox = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountNameTextBox")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(nameBox, Is.Not.Null, "A validation failure must re-show the dialog rather than close it.");

        // And the error text must now actually be visible with a real message - TextBlock's
        // automation peer (TextBlockAutomationPeer.GetNameCore()) surfaces its Text via the
        // element's Name, the same mechanism the GridView row-name gotcha above relies on.
        var errorText = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountErrorText")),
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(errorText, Is.Not.Null, "The error text element must be findable once ErrorMessage is set.");
        Assert.That(errorText!.Name, Does.Contain("name"), "The error text must actually explain the problem.");

        app.Close();
    }
}
