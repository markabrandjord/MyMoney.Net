using System.Windows;
using MyMoney.Shell.Services;

namespace MyMoney.Shell;

public partial class App : Application
{
    // A prior spike on this same redesign shipped an App.xaml with no StartupUri and no
    // OnStartup override - the process ran but no window was ever created, and
    // FlaUI.Core.Application.GetMainWindow polled forever. Do not repeat that mistake.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        var fixture = MyMoney.TestKit.InMemorySqliteStore.Create(isTestDatabase: true);

        new MainWindow(themeService, statusService, navigationService, dialogService, fixture.Query).Show();
    }
}
