using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

// Same UIA-pattern-only approach as AddAccountFlaUiTests - see that file's header comment. Enter
// and Shift+Enter are NOT covered here: they need a real key-press, which this sandboxed
// environment's simulated-input restriction blocks (see the skill's input-and-sessions notes) -
// the visible Previous/Next buttons this feature also provides (mirroring a browser's Ctrl+F
// bar) are what's exercised instead, and they drive the exact same code path.
[TestFixture]
public class AccountsSearchFlaUiTests
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

    private static string[] VisibleRowNames(Window mainWindow)
    {
        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        return listView.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
            .Select(row => row.Name)
            .ToArray();
    }

    private static bool AnyRowNamed(Window mainWindow, string accountName) =>
        VisibleRowNames(mainWindow).Any(name => name.Contains($"Name = {accountName},"));

    private static AutomationElement SelectedRow(Window mainWindow)
    {
        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        return listView.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
            .FirstOrDefault(row => row.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault);
    }

    [Test]
    public void NextAndPreviousButtons_AreDisabledUntilASearchIsActive()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");

        var nextButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchNextButton"));
        var previousButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchPreviousButton"));
        Assert.That(nextButton.IsEnabled, Is.False, "Next must start disabled - no search has been run yet.");
        Assert.That(previousButton.IsEnabled, Is.False, "Previous must start disabled - no search has been run yet.");

        var searchBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"));
        searchBox.Patterns.Value.Pattern.SetValue("Checking");
        Retry.WhileFalse(() => nextButton.IsEnabled, TimeSpan.FromSeconds(5));
        Assert.That(nextButton.IsEnabled, Is.True, "Next must enable once a search is active.");
        Assert.That(previousButton.IsEnabled, Is.True, "Previous must enable once a search is active.");

        searchBox.Patterns.Value.Pattern.SetValue("");
        Retry.WhileTrue(() => nextButton.IsEnabled, TimeSpan.FromSeconds(5));
        Assert.That(nextButton.IsEnabled, Is.False, "Next must disable again once the query is cleared.");
        Assert.That(previousButton.IsEnabled, Is.False, "Previous must disable again once the query is cleared.");
    }

    [Test]
    public void TypingAQuery_FiltersOutNonMatchingRowsAndSelectsTheMatch()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");
        AddAccount(mainWindow, "Visa");
        AddAccount(mainWindow, "Savings");

        var searchBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"));
        searchBox.Patterns.Value.Pattern.SetValue("Visa");

        Retry.WhileTrue(() => VisibleRowNames(mainWindow).Length != 1, TimeSpan.FromSeconds(5));

        var rowNames = VisibleRowNames(mainWindow);
        Assert.That(rowNames, Has.Length.EqualTo(1), "Non-matching rows (Checking, Savings) must be hidden, not just skipped over.");
        Assert.That(rowNames[0], Does.Contain("Name = Visa,"));

        var selected = SelectedRow(mainWindow);
        Assert.That(selected, Is.Not.Null, "The one matching row must be selected automatically.");
        Assert.That(selected.Name, Does.Contain("Name = Visa,"));
    }

    [Test]
    public void ClearingTheQuery_RestoresTheFullList()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");
        AddAccount(mainWindow, "Visa");

        var searchBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"));
        searchBox.Patterns.Value.Pattern.SetValue("Visa");
        Retry.WhileTrue(() => VisibleRowNames(mainWindow).Length != 1, TimeSpan.FromSeconds(5));

        searchBox.Patterns.Value.Pattern.SetValue("");
        Retry.WhileTrue(() => VisibleRowNames(mainWindow).Length != 2, TimeSpan.FromSeconds(5));

        Assert.That(VisibleRowNames(mainWindow), Has.Length.EqualTo(2), "Clearing the query must restore every row, not just the last match.");
    }

    [Test]
    public void MultipleMatches_NextAndPreviousCycleThroughTheFilteredRows()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Savings");
        AddAccount(mainWindow, "Vacation Fund");
        AddAccount(mainWindow, "Checking");

        var searchBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"));
        searchBox.Patterns.Value.Pattern.SetValue("a"); // matches Savings and Vacation Fund, not Checking
        Retry.WhileTrue(() => VisibleRowNames(mainWindow).Length != 2, TimeSpan.FromSeconds(5));

        var firstSelected = Retry.WhileNull(() => SelectedRow(mainWindow), TimeSpan.FromSeconds(5)).Result;
        Assert.That(firstSelected, Is.Not.Null);
        Assert.That(firstSelected.Name, Does.Contain("Name = Savings,"), "The first (topmost, sorted) match is selected automatically.");

        var nextButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchNextButton"));
        nextButton.Patterns.Invoke.Pattern.Invoke();
        var secondSelected = Retry.WhileNull(
            () => SelectedRow(mainWindow) is { } row && row.Name.Contains("Vacation Fund") ? row : null,
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(secondSelected, Is.Not.Null, "Next must move to the other visible match, Vacation Fund.");

        // Next again must wrap back to the first match (only two rows are visible).
        nextButton.Patterns.Invoke.Pattern.Invoke();
        var wrapped = Retry.WhileNull(
            () => SelectedRow(mainWindow) is { } row && row.Name.Contains("Savings") ? row : null,
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(wrapped, Is.Not.Null, "Next must wrap back to Savings after the last visible match.");

        var previousButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchPreviousButton"));
        previousButton.Patterns.Invoke.Pattern.Invoke();
        var previous = Retry.WhileNull(
            () => SelectedRow(mainWindow) is { } row && row.Name.Contains("Vacation Fund") ? row : null,
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(previous, Is.Not.Null, "Previous from Savings must wrap back to Vacation Fund.");
    }

    [Test]
    public void NoMatches_ReportsInTheStatusBarAndShowsAnEmptyGrid()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");

        var searchBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"));
        searchBox.Patterns.Value.Pattern.SetValue("Nonexistent");

        var statusText = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("StatusText")) is { } el && el.Name == "No matches for 'Nonexistent'" ? el : null,
            TimeSpan.FromSeconds(5)).Result;
        Assert.That(statusText, Is.Not.Null, "A query with no matches must be reported, not silently ignored.");
        Assert.That(VisibleRowNames(mainWindow), Is.Empty);
    }

    [Test]
    public void NavigatingAwayAndBack_ClearsThePreviousQuery()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Checking");
        AddAccount(mainWindow, "Visa");

        var searchBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SearchBox"));
        searchBox.Patterns.Value.Pattern.SetValue("Visa");
        Retry.WhileTrue(() => VisibleRowNames(mainWindow).Length != 1, TimeSpan.FromSeconds(5));

        // Re-navigating to the same route is the only navigation available in this slice - it
        // still exercises the Navigated handler's clear-on-navigate rule (D-9's focus/lifetime
        // rule), since AccountsListViewModel/AccountsView are freshly reconstructed each time.
        OpenAccountsPage(mainWindow);

        Retry.WhileTrue(() => searchBox.Patterns.Value.Pattern.Value.ValueOrDefault != "", TimeSpan.FromSeconds(5));
        Assert.That(searchBox.Patterns.Value.Pattern.Value.ValueOrDefault, Is.Empty,
            "Navigating clears the search box - a stale query must not persist across navigation.");
        Assert.That(VisibleRowNames(mainWindow), Has.Length.EqualTo(2), "The full list must be restored, not left filtered.");
    }
}
