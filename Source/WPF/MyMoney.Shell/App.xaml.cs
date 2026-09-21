using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using MyMoney.Shell.Services;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;
using Walkabout.Utilities;

// SqliteConnectionFactory is deliberately duplicated into both SQLite assemblies (the
// persistence spec's section 1 Recommendation), so a file referencing both has to say which
// copy it means. They are identical apart from the namespace; the provisioner's copy is the one
// that opens the connection everything else here runs over.
using SqliteConnectionFactory = Walkabout.Data.Sqlite.Provisioning.SqliteConnectionFactory;

namespace MyMoney.Shell;

public partial class App : Application
{
    /// <summary>
    /// This app's own log folder, deliberately NOT the legacy app's %TEMP%\MyMoney\Logs.
    /// Walkabout.Utilities.Log appends to its file with File.AppendAllText from a background
    /// channel pump, so two processes sharing one file is a real IO-contention hazard, and the
    /// legacy Basics FlaUI suite's AppCrashGuard greps that file for "APP ERROR: Unhandled" -
    /// a Shell crash landing there would be attributed to whichever legacy test was running.
    /// </summary>
    internal static string LogFolder =>
        Path.Combine(Path.GetTempPath(), "MyMoney.Shell", "Logs");

    /// <summary>
    /// The current day's log file, named exactly the way Log.UpdateFilePath names it, so this
    /// app and the log class agree on one path. The Shell FlaUI crash guard reads this.
    /// </summary>
    internal static string LogFilePath =>
        Path.Combine(LogFolder, "MyMoney_" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "_log.txt");

    private const string DatabaseDisplayName = "MyMoney.Shell in-memory";

    // The in-memory database this shell runs against is a throwaway demo fixture, not a user's
    // data file, and it is flagged as such - preserving exactly what InMemorySqliteStore.Create
    // (isTestDatabase: true) recorded in StoreIdentity before this bootstrap replaced it.
    private const bool IsTestDatabase = true;

    private ILogger? rootLog;
    private Log? appLog;
    private SqliteMoneyStoreProvisioner? provisioner;
    private SqliteMoneyStore? store;

    // A prior spike on this same redesign shipped an App.xaml with no StartupUri and no
    // OnStartup override - the process ran but no window was ever created, and
    // FlaUI.Core.Application.GetMainWindow polled forever. Do not repeat that mistake.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(LogFolder);
        this.rootLog = new Log(LogFolder);
        this.appLog = Log.GetLogger("App");
        this.appLog.Info("Launching MyMoney.Shell");

        // Until this existed, an unexpected exception anywhere in this app was a silent process
        // death with no log entry at all - the same failure class that once let a FlaUI run
        // report "Passed" over a genuinely crashed legacy app (see CLAUDE.md).
        this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;

        var themeService = new ThemeService();
        var statusService = new StatusService();
        var navigationService = new NavigationService();
        var dialogService = new DialogService();

        // This plan's whole scope runs against an in-memory SQLite fixture, deliberately, not
        // as a placeholder. Opening the user's real, registry-selected database file
        // (DatabaseFactory/DatabaseRegistry - see MyMoney.Data/DatabaseFactory.cs, already
        // built) is a separate, already-solved problem that belongs to a follow-up plan, once
        // more than one screen exists and "which database is open" is a real cross-screen
        // concern worth its own design pass rather than a MainWindow constructor detail.
        //
        // The sequence below is MyMoney.TestKit.InMemorySqliteStore.Create's, minus its
        // SqliteTestControlFactory.Acquire(provisioner) call: acquiring the destructive test
        // control is precisely what a shipped assembly must not be able to do, and it was the
        // only reason this app referenced the test tier at all.
        SqliteMoneyQuery query = this.OpenInMemoryDatabase();

        new MainWindow(themeService, statusService, navigationService, dialogService, query, this.store!).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Nothing disposed the store or its connection before - the InMemorySqliteStore fixture
        // was constructed and dropped on the floor. An in-memory SQLite database lives exactly
        // as long as its connection, so the store and the provisioner (which owns that one
        // shared connection) are torn down in that order, matching InMemorySqliteStore.Dispose.
        this.store?.Dispose();
        this.provisioner?.Dispose();

        this.appLog?.Info($"MyMoney.Shell terminating with exit code {e.ApplicationExitCode}");
        this.appLog?.Dispose();
        this.rootLog?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// Provisions a fresh :memory: database to the latest schema version with the same executor
    /// production uses, then opens the store and the query over that one connection. The
    /// provisioner and store are kept in fields so <see cref="OnExit"/> can dispose them.
    /// </summary>
    private SqliteMoneyQuery OpenInMemoryDatabase()
    {
        var opened = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
            DatabaseDisplayName, SqliteConnectionFactory.InMemoryDataSource, IsTestDatabase));
        try
        {
            opened.ApplyTo(SchemaStepCatalog.LatestVersion);

            this.store = SqliteMoneyStore.OpenOver(
                opened.Connection,
                new SqliteStoreOptions(DatabaseDisplayName, SqliteConnectionFactory.InMemoryDataSource, IsTestDatabase));
            this.provisioner = opened;

            return SqliteMoneyQuery.OpenOver(opened.Connection);
        }
        catch
        {
            opened.Dispose();
            throw;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        this.WriteUnhandledExceptionLine(e.Exception);

        // e.Handled is deliberately NOT set. The legacy app suppresses the crash only because it
        // first puts a modal "Unhandled Exception" report in front of the user; there is no such
        // dialog here, so swallowing this would leave a silently broken app running - the exact
        // invisible-crash failure this handler exists to end. Left unhandled, the process dies
        // loudly, after the line above has already reached disk.
    }

    /// <summary>
    /// Writes the crash line SYNCHRONOUSLY, in the same shape Walkabout.Utilities.Log's own
    /// LogEvent.ToString() produces ("&lt;timestamp&gt; APP ERROR: &lt;message&gt;"), which is what the
    /// Shell FlaUI crash guard greps for. Log.Error itself only queues onto a background channel
    /// (Logger.cs's FlushEventsToDisk), and with e.Handled left false the process is gone as soon
    /// as the handler returns - a queued line would very likely never be flushed. Best effort by
    /// design: the app is already crashing, and a failure to log must not replace the real
    /// exception with an IO one.
    /// </summary>
    private void WriteUnhandledExceptionLine(Exception exception)
    {
        try
        {
            string details = exception == null
                ? string.Empty
                : "\n" + exception.GetType() + ": " + exception.Message + "\n" + exception.StackTrace;

            Directory.CreateDirectory(LogFolder);
            File.AppendAllText(
                LogFilePath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture)
                + " APP ERROR: Unhandled app exception" + details + "\n");
        }
        catch (Exception)
        {
        }
    }
}
