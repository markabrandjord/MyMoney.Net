using MyMoney.Shell.Services;
using NUnit.Framework;
using Wpf.Ui.Appearance;

namespace MyMoney.Tests.Shell.Services;

[TestFixture]
public class ThemeServiceTests
{
    [TestCase(SystemTheme.Dark, AppTheme.Dark)]
    [TestCase(SystemTheme.Light, AppTheme.Light)]
    [TestCase(SystemTheme.Glow, AppTheme.Light)] // any non-Dark system theme resolves to Light
    public void ResolveFollowSystem_MapsSystemThemeToAppTheme(SystemTheme systemTheme, AppTheme expected)
    {
        var resolved = ThemeService.ResolveFollowSystem(systemTheme);
        Assert.That(resolved, Is.EqualTo(expected));
    }

    [Test]
    public void Apply_SetsCurrentAndRaisesChanged()
    {
        var service = new ThemeService();
        var raised = false;
        service.Changed += (_, _) => raised = true;

        service.Apply(AppTheme.Dark);

        Assert.That(service.Current, Is.EqualTo(AppTheme.Dark));
        Assert.That(raised, Is.True);
    }

    [Test]
    public void Apply_SameThemeTwice_DoesNotRaiseChangedTwice()
    {
        var service = new ThemeService();
        service.Apply(AppTheme.Dark);
        var raiseCount = 0;
        service.Changed += (_, _) => raiseCount++;

        service.Apply(AppTheme.Dark);

        Assert.That(raiseCount, Is.EqualTo(0));
    }
}
