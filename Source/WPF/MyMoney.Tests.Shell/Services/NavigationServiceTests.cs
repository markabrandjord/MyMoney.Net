using MyMoney.Shell.Services;
using NUnit.Framework;

namespace MyMoney.Tests.Shell.Services;

[TestFixture]
public class NavigationServiceTests
{
    [Test]
    public void CurrentRoute_DefaultsToNull()
    {
        var service = new NavigationService();
        Assert.That(service.CurrentRoute, Is.Null);
    }

    [Test]
    public void NavigateTo_SetsCurrentRouteAndRaisesNavigated()
    {
        var service = new NavigationService();
        RouteId? raised = null;
        service.Navigated += (_, route) => raised = route;

        service.NavigateTo(RouteId.Accounts);

        Assert.That(service.CurrentRoute, Is.EqualTo(RouteId.Accounts));
        Assert.That(raised, Is.EqualTo(RouteId.Accounts));
    }

    [Test]
    public void RouteId_EqualityIsByValue()
    {
        // D-8: route ids are deliberately-chosen strings, never typeof(T).FullName - and
        // must compare equal by value so two lookups of "the same" route agree.
        Assert.That(RouteId.Accounts, Is.EqualTo(new RouteId("accounts")));
    }
}
