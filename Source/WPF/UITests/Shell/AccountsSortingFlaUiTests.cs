using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

// Same UIA-pattern-only approach as AddAccountFlaUiTests - see that file's header comment.
[TestFixture]
public class AccountsSortingFlaUiTests
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

    // Currency is a plain free-text field (AddAccountView.xaml's CurrencyTextBox), so giving two
    // accounts different currencies here is enough to make a Currency-column sort verifiable
    // without needing Edit at all.
    private static void AddAccount(Window mainWindow, string name, string currency = "USD")
    {
        mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountButton")).Patterns.Invoke.Pattern.Invoke();

        var nameBox = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountNameTextBox")),
            TimeSpan.FromSeconds(5)).Result;
        nameBox.Patterns.Value.Pattern.SetValue(name);

        var currencyBox = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountCurrencyTextBox"));
        currencyBox.Patterns.Value.Pattern.SetValue(currency);

        mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")))
            .Patterns.Invoke.Pattern.Invoke();

        Retry.WhileNotNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddAccountNameTextBox")),
            TimeSpan.FromSeconds(5));
    }

    /// <summary>Row order top-to-bottom, as the account name embedded in each DataItem's Name.</summary>
    private static string[] CurrentRowOrder(Window mainWindow)
    {
        var listView = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsListView"));
        return listView.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem))
            .Select(row => row.Name)
            .ToArray();
    }

    private static int IndexOfAccount(string[] rowOrder, string accountName) =>
        Array.FindIndex(rowOrder, name => name.Contains($"Name = {accountName},"));

    private static bool NameComesBefore(Window mainWindow, string first, string second)
    {
        var rowOrder = CurrentRowOrder(mainWindow);
        int firstIndex = IndexOfAccount(rowOrder, first);
        int secondIndex = IndexOfAccount(rowOrder, second);
        return firstIndex >= 0 && secondIndex >= 0 && firstIndex < secondIndex;
    }

    [Test]
    public void TheAccountsList_DefaultsToNameAscending()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Zebra");
        AddAccount(mainWindow, "Apple");

        Retry.WhileFalse(() => NameComesBefore(mainWindow, "Apple", "Zebra"), TimeSpan.FromSeconds(5));

        Assert.That(NameComesBefore(mainWindow, "Apple", "Zebra"), Is.True,
            "The list must default to Name ascending, regardless of the order accounts were added in.");
    }

    [Test]
    public void ClickingTheNameHeader_TogglesBetweenAscendingAndDescending()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        AddAccount(mainWindow, "Zebra");
        AddAccount(mainWindow, "Apple");

        // Default load is Name ascending (Apple, Zebra) - confirm before relying on it below.
        Retry.WhileFalse(() => NameComesBefore(mainWindow, "Apple", "Zebra"), TimeSpan.FromSeconds(5));

        var nameHeader = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.HeaderItem).And(cf.ByName("Name")));
        Assert.That(nameHeader, Is.Not.Null);

        // One click on the already-ascending-sorted column must flip it to descending.
        nameHeader.Patterns.Invoke.Pattern.Invoke();
        Retry.WhileFalse(() => NameComesBefore(mainWindow, "Zebra", "Apple"), TimeSpan.FromSeconds(5));
        Assert.That(NameComesBefore(mainWindow, "Zebra", "Apple"), Is.True,
            "Clicking the already-sorted column must toggle the direction.");

        // A second click must flip back to ascending.
        nameHeader.Patterns.Invoke.Pattern.Invoke();
        Retry.WhileFalse(() => NameComesBefore(mainWindow, "Apple", "Zebra"), TimeSpan.FromSeconds(5));
        Assert.That(NameComesBefore(mainWindow, "Apple", "Zebra"), Is.True,
            "A second click on the same header must toggle back to ascending.");
    }

    [Test]
    public void ClickingADifferentHeader_SwitchesTheSortColumnAscending()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        OpenAccountsPage(mainWindow);
        // Distinct currencies, deliberately reversed relative to name order, so a Currency sort
        // produces a different row order than the default Name sort - proving the click actually
        // switched the sort column rather than just re-applying the same one.
        AddAccount(mainWindow, "Zebra", currency: "EUR");
        AddAccount(mainWindow, "Apple", currency: "USD");

        // Default load is Name ascending (Apple, Zebra).
        Retry.WhileFalse(() => NameComesBefore(mainWindow, "Apple", "Zebra"), TimeSpan.FromSeconds(5));

        var currencyHeader = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.HeaderItem).And(cf.ByName("Currency")));
        Assert.That(currencyHeader, Is.Not.Null);
        currencyHeader.Patterns.Invoke.Pattern.Invoke();

        // Currency ascending: EUR (Zebra) before USD (Apple) - the reverse of the Name-ascending
        // order confirmed above, so this can only pass if the sort column genuinely switched.
        Retry.WhileFalse(() => NameComesBefore(mainWindow, "Zebra", "Apple"), TimeSpan.FromSeconds(5));
        Assert.That(NameComesBefore(mainWindow, "Zebra", "Apple"), Is.True,
            "Clicking a different header must switch the sort to that column, ascending.");
    }
}
