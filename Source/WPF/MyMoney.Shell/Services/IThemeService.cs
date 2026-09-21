using System;

namespace MyMoney.Shell.Services;

public interface IThemeService
{
    AppTheme Current { get; }
    void Apply(AppTheme theme);
    event EventHandler? Changed;
}
