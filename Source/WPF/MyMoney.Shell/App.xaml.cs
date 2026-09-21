using System.Windows;
using MyMoney.Shell.Services;

namespace MyMoney.Shell;

public partial class App : Application
{
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
