using System;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests.Shell
{
    /// <summary>
    /// The one place the Shell FlaUI suite knows where MyMoney.Shell.exe lives, how to launch it,
    /// and how to tell whether it crashed while a test was driving it. All four Shell fixtures
    /// used to carry their own copy of the ExePath expression and the launch/get-main-window
    /// boilerplate.
    ///
    /// The crash check is the Shell's half of the lesson AppCrashGuard records for the legacy
    /// Basics suite: a FlaUI test's UI assertions can all pass while the app has died or thrown in
    /// the background, and nothing in the harness notices. MyMoney.Shell's App.xaml.cs now writes
    /// an "APP ERROR: Unhandled app exception" line to its own log file from
    /// DispatcherUnhandledException, synchronously, and this class diffs that file across the
    /// test. It is intentionally simpler than AppCrashGuard: the Shell shows no crash-report
    /// dialog (it does not set e.Handled), so there is no leftover modal to dismiss, and each
    /// Shell test launches its own process rather than sharing a session, so there is no
    /// corrupted-shared-session failure mode to defend against either.
    /// </summary>
    internal static class ShellTestApp
    {
        /// <summary>
        /// MyMoney.Shell's build output, relative to this test assembly's own output directory
        /// (Source\WPF\UITests\bin\Debug\net10.0-windows7.0 -> Source\WPF). UITests.csproj carries
        /// a ProjectReference (ReferenceOutputAssembly=false) to MyMoney.Shell.csproj so that this
        /// file is guaranteed to have been rebuilt before any of these tests run.
        /// </summary>
        internal static string ExePath => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "MyMoney.Shell",
            "bin", "Debug", "net10.0-windows7.0", "MyMoney.Shell.exe"));

        /// <summary>
        /// The Shell's own log file - NOT the legacy app's %TEMP%\MyMoney\Logs. Must stay in step
        /// with MyMoney.Shell.App.LogFilePath, which is why both spell the same name out rather
        /// than one inferring it.
        /// </summary>
        internal static string LogFilePath => Path.Combine(
            Path.GetTempPath(), "MyMoney.Shell", "Logs",
            "MyMoney_" + DateTime.Now.ToString("yyyy-MM-dd") + "_log.txt");

        /// <summary>
        /// Launches MyMoney.Shell.exe, waits for its main window, and records the log baseline the
        /// crash check will diff against. Dispose (or, better, CloseAndAssertNoCrash from a
        /// TearDown) when the test is done.
        /// </summary>
        internal static ShellSession Launch()
        {
            long baseline = File.Exists(LogFilePath) ? new FileInfo(LogFilePath).Length : 0;

            Application app = Application.Launch(ExePath);
            var automation = new UIA3Automation();

            Window mainWindow = Retry.WhileNull(
                () => app.GetMainWindow(automation), TimeSpan.FromSeconds(10)).Result;
            Assert.That(mainWindow, Is.Not.Null, "MyMoney.Shell's main window did not appear within 10s.");

            return new ShellSession(app, automation, mainWindow, baseline);
        }

        /// <summary>
        /// Shuts the session down and fails the test if the app logged an unhandled exception
        /// while it was running. Call it from a [TearDown] (so it runs even when the test body has
        /// already failed), like this:
        /// <code>
        /// [TearDown]
        /// public void TearDown()
        /// {
        ///     try { ShellTestApp.AssertNoCrashOccurred(this.session); }
        ///     finally { this.session?.Dispose(); this.session = null; }
        /// }
        /// </code>
        /// The second Dispose is a deliberate no-op belt-and-braces: this method has already
        /// closed the app, because the log can only be read once the process is done writing to
        /// it. Null is accepted, for a test that failed before it ever launched anything.
        /// </summary>
        internal static void AssertNoCrashOccurred(ShellSession session)
        {
            if (session == null)
            {
                return;
            }

            string newContent = session.CloseAndReadNewLogContent();

            if (newContent.Contains("APP ERROR: Unhandled"))
            {
                Assert.Fail("MyMoney.Shell logged an unhandled exception during this test - see "
                    + LogFilePath + ". New log content:\n" + newContent);
            }
        }
    }

    /// <summary>One launched MyMoney.Shell process and the automation attached to it.</summary>
    internal sealed class ShellSession : IDisposable
    {
        private readonly long logLengthBaseline;
        private bool closed;

        internal ShellSession(Application app, UIA3Automation automation, Window mainWindow, long logLengthBaseline)
        {
            this.App = app;
            this.Automation = automation;
            this.MainWindow = mainWindow;
            this.logLengthBaseline = logLengthBaseline;
        }

        internal Application App { get; }

        internal UIA3Automation Automation { get; }

        internal Window MainWindow { get; }

        /// <summary>
        /// Shuts the app down and returns whatever it appended to its log file since launch. The
        /// close happens FIRST: the crash line is written synchronously at the crash site, but an
        /// app still running could yet write one, and reading afterwards means a crash during
        /// shutdown is caught too.
        /// </summary>
        internal string CloseAndReadNewLogContent()
        {
            this.Dispose();

            string path = ShellTestApp.LogFilePath;
            long currentLength = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (currentLength <= this.logLengthBaseline)
            {
                // Shorter than the baseline means the file rolled over (new day) or was cleared;
                // there is nothing meaningful to compare against.
                return string.Empty;
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(this.logLengthBaseline, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        public void Dispose()
        {
            if (this.closed)
            {
                return;
            }

            this.closed = true;

            // Best effort: the process may already be gone (that is exactly the case the crash
            // check exists for), and a failure to close it must not replace the real reason the
            // test is failing.
            try
            {
                // FlaUI's Close() asks the main window to close, waits, and kills the process if
                // it does not go - so there is no separate wait-for-exit needed here.
                this.App.Close();
            }
            catch (Exception)
            {
            }

            try
            {
                this.App.Dispose();
            }
            catch (Exception)
            {
            }

            this.Automation.Dispose();
        }
    }
}
