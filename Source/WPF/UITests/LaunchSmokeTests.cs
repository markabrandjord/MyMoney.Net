using System;
using System.IO;
using FlaUI.Core;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests
{
    public class LaunchSmokeTests
    {
        internal static string MyMoneyExePath =>
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "MyMoney", "bin", "Debug", "net10.0-windows7.0", "win-x64", "MyMoney.exe");

        [Test]
        public void LaunchAndClose_MainWindowAppears()
        {
            Assert.That(File.Exists(MyMoneyExePath), Is.True,
                $"MyMoney.exe not found at {MyMoneyExePath}. Build Source/WPF/MyMoney.csproj first.");

            using var app = Application.Launch(MyMoneyExePath);
            using var automation = new UIA3Automation();

            var mainWindow = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

            Assert.That(mainWindow, Is.Not.Null);
            Assert.That(mainWindow.Title, Does.Contain("MyMoney"));

            app.Close();
        }
    }
}
