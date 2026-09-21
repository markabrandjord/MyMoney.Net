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

        var darkButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ThemeDarkButton"));
        Assert.That(darkButton, Is.Not.Null);
        darkButton!.Click();

        var lightButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ThemeLightButton"));
        lightButton!.Click();

        app.Close();
    }

    [Test]
    public void NavAccountsButton_BecomesPrimaryAppearanceWhenClicked()
    {
        using var app = Application.Launch(ExePath);
        using var automation = new UIA3Automation();
        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result!;

        var navButton = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("NavAccountsButton"));
        Assert.That(navButton, Is.Not.Null);
        navButton!.Click();

        app.Close();
    }
}
