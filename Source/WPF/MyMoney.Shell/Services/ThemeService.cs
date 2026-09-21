using System;
using Wpf.Ui.Appearance;

namespace MyMoney.Shell.Services;

public sealed class ThemeService : IThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.Light;

    public event EventHandler? Changed;

    public static AppTheme ResolveFollowSystem(SystemTheme systemTheme) =>
        systemTheme == SystemTheme.Dark ? AppTheme.Dark : AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        if (theme == this.Current)
        {
            return;
        }

        this.Current = theme;

        var resolved = theme == AppTheme.FollowSystem
            ? ResolveFollowSystem(ApplicationThemeManager.GetSystemTheme())
            : theme;

        ApplicationThemeManager.Apply(resolved == AppTheme.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light);

        this.Changed?.Invoke(this, EventArgs.Empty);
    }
}
