using System;
using System.IO;
using FlaUI.Core;
using FlaUI.UIA3;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UITests.Basics
{
    /// <summary>
    /// Launches the app once for every FlaUI test in the Walkabout.UITests.Basics namespace
    /// (app launch is the expensive part - several seconds cold start), while each individual
    /// test still gets its own scratch database via OpenFreshDatabase/CleanUpDatabase. See
    /// docs/superpowers/specs/2026-09-18-basics-test-implementation-design.md's "FlaUI test
    /// session lifecycle" section. Scoped to this namespace only - does not affect the
    /// existing Walkabout.UITests.PayeeSelectionTests, which launches its own app per test.
    /// </summary>
    [SetUpFixture]
    public class BasicsAppSession
    {
        internal static Application App { get; private set; }
        internal static UIA3Automation Automation { get; private set; }
        internal static FlaUI.Core.AutomationElements.Window MainWindow { get; private set; }

        [OneTimeSetUp]
        public void LaunchApp()
        {
            App = Application.Launch(LaunchSmokeTests.MyMoneyExePath, "/nosettings");
            Automation = new UIA3Automation();
            MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(10));
        }

        [OneTimeTearDown]
        public void CloseApp()
        {
            try
            {
                App?.Close();
            }
            catch
            {
                // best-effort cleanup; a test failure shouldn't be masked by a teardown exception
            }
            App?.Dispose();
            Automation?.Dispose();
        }
    }

    /// <summary>
    /// Per-test database isolation within the shared BasicsAppSession. Every FlaUI test calls
    /// OpenFreshDatabase in [SetUp] and CleanUpDatabase in [TearDown] - true Fresh Fixture, a
    /// brand-new scratch SQLite file built by BasicsFixtureBuilder every time, never a
    /// checked-in binary.
    /// </summary>
    internal static class BasicsTestSetup
    {
        internal static (string ScratchPath, string RegisteredName) OpenFreshDatabase(string displayName)
        {
            string scratchPath = Path.Combine(Path.GetTempPath(), $"BasicsScratch-{Guid.NewGuid():N}.mmdb");

            MyMoney money = BasicsFixtureBuilder.Build();
            var db = new SqliteDatabase();
            db.DatabasePath = scratchPath;
            db.Create();
            db.Save(money);

            string registryPath = DatabaseRegistry.GetDefaultPath();
            var registry = DatabaseRegistry.Load(registryPath);
            registry.Databases[displayName] = new DatabaseEntry
            {
                Engine = DataEngineType.Sqlite,
                Path = scratchPath,
                TestDatabase = true
            };
            registry.Save();

            return (scratchPath, displayName);
        }

        internal static void CleanUpDatabase(string scratchPath, string registeredName)
        {
            try
            {
                string registryPath = DatabaseRegistry.GetDefaultPath();
                var registry = DatabaseRegistry.Load(registryPath);
                registry.Databases.Remove(registeredName);
                registry.Save();
            }
            catch
            {
                // best-effort - a cleanup failure shouldn't mask the test's actual result
            }

            try
            {
                if (File.Exists(scratchPath))
                {
                    File.Delete(scratchPath);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }
}
