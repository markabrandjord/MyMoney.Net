using System;

namespace MyMoney.Shell.Services;

public sealed class NavigationService : INavigationService
{
    public RouteId? CurrentRoute { get; private set; }

    public event EventHandler<RouteId>? Navigated;

    public void NavigateTo(RouteId route)
    {
        this.CurrentRoute = route;
        this.Navigated?.Invoke(this, route);
    }
}
