using System;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

[TestFixture]
public class AccountsViewFlaUiTests
{
    private static string ExePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "MyMoney.Shell",
        "bin", "Debug", "net10.0-windows7.0", "MyMoney.Shell.exe"));

    [Test]
    public void ClickingAccountsNav_ShowsAccountsListView()
    {
        using var app = Application.Launch(ExePath);
        using var automation = new UIA3Automation();
        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result!;

        // Patterns.Invoke.Pattern.Invoke(), not .Click() - .Click() drives FlaUI's simulated
        // mouse input (Win32 SendInput), which throws Win32Exception ("Access is denied",
        // NativeErrorCode 5) in this sandboxed/agent session (confirmed reproducing it here).
        // The Invoke UIA pattern goes through the app's automation provider directly and isn't
        // subject to that restriction - prefer pattern operations over simulated input.
        var navButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NavAccountsButton"))!;
        navButton.Patterns.Invoke.Pattern.Invoke();

        var accountsPage = Retry.WhileNull(
            () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AccountsPage")),
            TimeSpan.FromSeconds(5)).Result;

        Assert.That(accountsPage, Is.Not.Null);

        app.Close();
    }
}
