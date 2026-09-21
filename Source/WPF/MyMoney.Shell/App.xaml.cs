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

        new MainWindow(themeService, statusService, navigationService, dialogService).Show();
    }
}
