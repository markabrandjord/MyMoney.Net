using NUnit.Framework;

namespace Walkabout.UITests.Shell;

[TestFixture]
public class ShellLaunchSmokeTests
{
    private ShellSession session;

    [TearDown]
    public void TearDown()
    {
        try
        {
            ShellTestApp.AssertNoCrashOccurred(this.session);
        }
        finally
        {
            this.session?.Dispose();
            this.session = null;
        }
    }

    [Test]
    public void Shell_LaunchesAndShowsMainWindow()
    {
        this.session = ShellTestApp.Launch();

        Assert.That(this.session.MainWindow.Title, Is.EqualTo("MyMoney"));
    }
}
