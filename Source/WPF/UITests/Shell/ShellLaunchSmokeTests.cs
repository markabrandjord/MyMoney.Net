using System;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests.Shell;

[TestFixture]
public class ShellLaunchSmokeTests
{
    private static string ExePath =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "MyMoney.Shell",
            "bin", "Debug", "net10.0-windows7.0", "MyMoney.Shell.exe");

    [Test]
    public void Shell_LaunchesAndShowsMainWindow()
    {
        using var app = Application.Launch(Path.GetFullPath(ExePath));
        using var automation = new UIA3Automation();

        var mainWindow = Retry.WhileNull(() => app.GetMainWindow(automation),
            TimeSpan.FromSeconds(10)).Result;

        Assert.That(mainWindow, Is.Not.Null, "Main window did not appear within 10s.");
        Assert.That(mainWindow.Title, Is.EqualTo("MyMoney"));

        app.Close();
    }
}
