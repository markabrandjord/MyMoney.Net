using NUnit.Framework;

namespace Walkabout.UITests.Shell;

[TestFixture]
public class ShellChromeFlaUiTests
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
    public void ThemeButtons_SwitchTheme()
    {
        this.session = ShellTestApp.Launch();
        var mainWindow = this.session.MainWindow;

        // Invoke via the UIA Invoke control pattern rather than .Click() (simulated mouse
        // input via SendInput) - SendInput requires a genuinely interactive desktop, which
        // this sandboxed session doesn't have, and throws Win32Exception "Access is denied"
        // (error 5) here. InvokePattern doesn't need an interactive desktop.
        var darkButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ThemeDarkButton"));
        Assert.That(darkButton, Is.Not.Null);
        darkButton.Patterns.Invoke.Pattern.Invoke();

        var lightButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ThemeLightButton"));
        lightButton.Patterns.Invoke.Pattern.Invoke();

        // "Follow system" now hooks the real window with Wpf.Ui SystemThemeWatcher.Watch, and
        // switching away from it calls UnWatch - both touch the live HWND, so both are driven
        // here rather than left to a unit test that can only run headless. The TearDown crash
        // check is what makes this meaningful: an exception out of Watch/UnWatch would be an
        // unhandled dispatcher exception, which these assertions alone would never notice.
        var systemButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ThemeSystemButton"));
        Assert.That(systemButton, Is.Not.Null);
        systemButton.Patterns.Invoke.Pattern.Invoke();

        lightButton.Patterns.Invoke.Pattern.Invoke();
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
        this.session = ShellTestApp.Launch();

        var navButton = this.session.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NavAccountsButton"));
        Assert.That(navButton, Is.Not.Null);
        // Invoke via the UIA Invoke control pattern rather than .Click() (simulated mouse
        // input via SendInput) - SendInput requires a genuinely interactive desktop, which
        // this sandboxed session doesn't have, and throws Win32Exception "Access is denied"
        // (error 5) here. InvokePattern doesn't need an interactive desktop.
        navButton.Patterns.Invoke.Pattern.Invoke();
    }
}
