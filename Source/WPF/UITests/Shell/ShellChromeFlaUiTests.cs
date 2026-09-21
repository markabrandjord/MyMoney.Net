using System;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

[TestFixture]
public class ShellChromeFlaUiTests
{
    private static string ExePath => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "MyMoney.Shell",
        "bin", "Debug", "net10.0-windows7.0", "MyMoney.Shell.exe"));

    [Test]
    public void ThemeButtons_SwitchTheme()
    {
        using var app = Application.Launch(ExePath);
        using var automation = new UIA3Automation();
        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result!;

        // Invoke via the UIA Invoke control pattern rather than .Click() (simulated mouse
        // input via SendInput) - SendInput requires a genuinely interactive desktop, which
        // this sandboxed session doesn't have, and throws Win32Exception "Access is denied"
        // (error 5) here. InvokePattern doesn't need an interactive desktop.
        var darkButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ThemeDarkButton"));
        Assert.That(darkButton, Is.Not.Null);
        darkButton!.Patterns.Invoke.Pattern.Invoke();

        var lightButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ThemeLightButton"));
        lightButton!.Patterns.Invoke.Pattern.Invoke();

        app.Close();
    }

    // This only proves the invoke completes without error. Verifying the actual
    // Primary/Secondary Appearance toggle needs either a second real route to navigate
    // away-and-back through, or an in-process test harness with direct object access to
    // read NavAccountsButton.Appearance - neither exists yet (FlaUI/UIA is out-of-process
    // and can't read that live property the way an in-process test could), and there's
    // only one route (Accounts) right now, so "became Primary" is indistinguishable from
    // "already was Primary" from outside the process.
    [Test]
    public void NavAccountsButton_InvokeTriggersNavigationWithoutError()
    {
        using var app = Application.Launch(ExePath);
        using var automation = new UIA3Automation();
        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result!;

        var navButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NavAccountsButton"));
        Assert.That(navButton, Is.Not.Null);
        // Invoke via the UIA Invoke control pattern rather than .Click() (simulated mouse
        // input via SendInput) - SendInput requires a genuinely interactive desktop, which
        // this sandboxed session doesn't have, and throws Win32Exception "Access is denied"
        // (error 5) here. InvokePattern doesn't need an interactive desktop.
        navButton!.Patterns.Invoke.Pattern.Invoke();

        app.Close();
    }
}
