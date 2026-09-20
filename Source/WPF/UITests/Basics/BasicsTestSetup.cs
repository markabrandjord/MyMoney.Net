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

        /// <summary>
        /// Same as OpenFreshDatabase, but also pre-creates one attachment file on disk for the
        /// fixture's "AMC THEATRES 1234" transaction (BasicsFixtureBuilder.WriteAttachmentFile),
        /// for the Attachments FlaUI tests. Kept separate from OpenFreshDatabase (rather than
        /// adding this to every test's fixture) since only the Attachments tests need the extra
        /// disk I/O, and because it needs the transaction's real (post-AddTransaction) Id, which
        /// OpenFreshDatabase's plain (ScratchPath, RegisteredName) return doesn't expose.
        /// </summary>
        internal static (string ScratchPath, string RegisteredName, long AttachmentTransactionId) OpenFreshDatabaseWithAttachment(string displayName)
        {
            string scratchPath = Path.Combine(Path.GetTempPath(), $"BasicsScratch-{Guid.NewGuid():N}.mmdb");

            MyMoney money = BasicsFixtureBuilder.Build();

            Transaction amcTransaction = null;
            // Plain foreach, not LINQ - Money.cs's collections implement two different
            // IEnumerable<T> instantiations, which makes LINQ extension methods ambiguous
            // (CS0411). See CLAUDE.md.
            foreach (Transaction t in money.Transactions)
            {
                if (t.Payee != null && t.Payee.Name == "AMC THEATRES 1234")
                {
                    amcTransaction = t;
                    break;
                }
            }
            if (amcTransaction == null)
            {
                throw new InvalidOperationException("BasicsFixtureBuilder no longer seeds an 'AMC THEATRES 1234' transaction - update this helper to match.");
            }

            var db = new SqliteDatabase();
            db.DatabasePath = scratchPath;
            db.Create();
            db.Save(money);

            BasicsFixtureBuilder.WriteAttachmentFile(scratchPath, amcTransaction.Account, amcTransaction.Id, "Basics attachment FlaUI test file.");

            string registryPath = DatabaseRegistry.GetDefaultPath();
            var registry = DatabaseRegistry.Load(registryPath);
            registry.Databases[displayName] = new DatabaseEntry
            {
                Engine = DataEngineType.Sqlite,
                Path = scratchPath,
                TestDatabase = true
            };
            registry.Save();

            return (scratchPath, displayName, amcTransaction.Id);
        }

        internal static void CleanUpDatabaseWithAttachment(string scratchPath, string registeredName)
        {
            CleanUpDatabase(scratchPath, registeredName);
            try
            {
                string attachmentDirectory = Path.Combine(
                    Path.GetDirectoryName(scratchPath),
                    Path.GetFileNameWithoutExtension(scratchPath) + ".Attachments");
                if (Directory.Exists(attachmentDirectory))
                {
                    Directory.Delete(attachmentDirectory, recursive: true);
                }
            }
            catch
            {
                // best-effort
            }
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
