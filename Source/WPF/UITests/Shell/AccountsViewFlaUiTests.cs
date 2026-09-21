using System;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

[TestFixture]
public class AccountsViewFlaUiTests
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

    [Test]
    public void ClickingAccountsNav_ShowsAccountsListView()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        // Patterns.Invoke.Pattern.Invoke(), not .Click() - .Click() drives FlaUI's simulated
        // mouse input (Win32 SendInput), which throws Win32Exception ("Access is denied",
        // NativeErrorCode 5) in this sandboxed/agent session (confirmed reproducing it here).
        // The Invoke UIA pattern goes through the app's automation provider directly and isn't
        // subject to that restriction - prefer pattern operations over simulated input.
        var navButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NavAccountsButton"));
        navButton.Patterns.Invoke.Pattern.Invoke();

        var accountsPage = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsPage")),
            TimeSpan.FromSeconds(5)).Result;

        Assert.That(accountsPage, Is.Not.Null);
    }
}
