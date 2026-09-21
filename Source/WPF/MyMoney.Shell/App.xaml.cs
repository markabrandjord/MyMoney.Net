using System.Windows;

namespace MyMoney.Shell;

public partial class App : Application
{
    // A prior spike on this same redesign shipped an App.xaml with no StartupUri and no
    // OnStartup override - the process ran but no window was ever created, and
    // FlaUI.Core.Application.GetMainWindow polled forever. Do not repeat that mistake.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow().Show();
    }
}
