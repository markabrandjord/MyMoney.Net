using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    /// <summary>
    /// Detects a real app crash during a FlaUI test, instead of letting it pass silently. Found
    /// the hard way in Task 15 (SampleDataFlaUiTests): MainWindow.xaml.cs's OnCommandAddSampleData
    /// unconditionally calls this.accountsControl.SelectedAccount = this.myMoney.Accounts.
    /// GetFirstAccount() after SampleDatabase.Create() returns - even when the Sample Database
    /// Options dialog was cancelled (Create() returns early) or otherwise added zero accounts, so
    /// GetFirstAccount() is null. AccountsControl.SelectedAccount's setter (View Selectors/
    /// AccountsControl.xaml.cs ~line 188) then dereferences that null value's .IsDeleted, throwing
    /// a NullReferenceException that App.xaml.cs's OnUnhandledException catches, logs, and reports
    /// via a MessageBoxEx dialog - but every FlaUI assertion in both SampleDataFlaUiTests still
    /// passed (UIA tree reads work fine alongside an unrelated modal, and MessageBoxEx.Show
    /// displays asynchronously via UiDispatcher.BeginInvoke rather than blocking the crash site),
    /// so `dotnet test` reported green while a crashed, dialog-blocked app sat in the background -
    /// only caught because a human happened to be watching the screen. Nothing in the test harness
    /// was watching for this. See CLAUDE.md and GitHub issue filed for the underlying app bug.
    ///
    /// The app's own log file (Utilities/Logger.cs's Log.UpdateFilePath:
    /// %TEMP%\MyMoney\Logs\MyMoney_&lt;yyyy-MM-dd&gt;_log.txt) is the reliable signal - every one
    /// of App.xaml.cs's three unhandled-exception handlers (OnUnhandledException,
    /// OnAppDomainUnhandledException, TaskScheduler_UnobservedTaskException1) writes an "APP
    /// ERROR: Unhandled ..." line there before anything dialog/timing-related happens, so checking
    /// it is immune to which dialog title or async timing race a UI-only check might miss.
    /// </summary>
    internal static class AppCrashGuard
    {
        private static readonly string[] CrashDialogTitles = { "Unhandled Exception", "Crash Report" };

        internal static string LogFilePath =>
            Path.Combine(Path.GetTempPath(), "MyMoney", "Logs", "MyMoney_" + DateTime.Now.ToString("yyyy-MM-dd") + "_log.txt");

        internal static long SnapshotLogLength()
        {
            string path = LogFilePath;
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        /// <summary>
        /// Dismisses any lingering known crash-dialog title (belt-and-suspenders - a dialog left
        /// open would block/corrupt every later test in the shared BasicsAppSession, the same
        /// class of problem documented for CategoriesFlaUiTests' collision dialog), then asserts
        /// the app's log file gained no new "APP ERROR: Unhandled" line since
        /// <paramref name="logLengthBaseline"/> - advancing it to the current length either way, so
        /// a real crash is attributed to the test that caused it rather than re-reported by every
        /// later test in the same run. Call from every FlaUI test's TearDown, after any
        /// test-specific cleanup.
        /// </summary>
        internal static void AssertNoCrashOccurred(Window mainWindow, ref long logLengthBaseline)
        {
            if (mainWindow != null)
            {
                foreach (string title in CrashDialogTitles)
                {
                    Window dialog = mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null;
                    if (dialog != null && dialog.Title == title)
                    {
                        AutomationElement ok = Retry.WhileNull(
                            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("ButtonOK")),
                            TimeSpan.FromSeconds(3)).Result;
                        ok?.Patterns.Invoke.Pattern.Invoke();
                        Wait.UntilInputIsProcessed();
                    }
                }
            }

            string path = LogFilePath;
            long currentLength = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (currentLength <= logLengthBaseline)
            {
                // Shorter than before means the file rolled over (new day) or was cleared -
                // nothing to compare against, just re-baseline.
                logLengthBaseline = currentLength;
                return;
            }

            string newContent;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(logLengthBaseline, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                newContent = reader.ReadToEnd();
            }
            logLengthBaseline = currentLength;

            if (newContent.Contains("APP ERROR: Unhandled"))
            {
                Assert.Fail("The app under test logged an unhandled exception during this test - " +
                    "see %TEMP%\\MyMoney\\Logs. New log content:\n" + newContent);
            }
        }
    }
}
