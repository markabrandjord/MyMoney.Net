using System;

namespace MyMoney.Shell.Services;

public interface INavigationService
{
    RouteId? CurrentRoute { get; }
    void NavigateTo(RouteId route);
    event EventHandler<RouteId>? Navigated;
}
