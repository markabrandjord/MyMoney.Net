using System;
using System.Windows;
using Wpf.Ui.Appearance;

namespace MyMoney.Shell.Services;

public sealed class ThemeService : IThemeService
{
    /// <summary>
    /// The window SystemThemeWatcher hooks to receive WM_SETTINGCHANGE/WM_THEMECHANGED. Null
    /// until <see cref="AttachWindow"/> is called, which is the headless case every unit test
    /// runs in - watching is simply skipped there, so Apply stays callable without a UI.
    /// </summary>
    private Window? window;

    private bool watching;

    public AppTheme Current { get; private set; } = AppTheme.Light;

    public event EventHandler? Changed;

    public static AppTheme ResolveFollowSystem(SystemTheme systemTheme) =>
        systemTheme == SystemTheme.Dark ? AppTheme.Dark : AppTheme.Light;

    /// <summary>
    /// Gives the service the window to hook for live OS theme changes. Not on IThemeService:
    /// the interface stays UI-free, and MainWindow does the attach through a concrete-type
    /// check, the same shape DialogService.SetHost is wired with.
    /// </summary>
    public void AttachWindow(Window window)
    {
        this.window = window ?? throw new ArgumentNullException(nameof(window));

        // Attaching after the app has already been put into "Follow system" must start watching;
        // today MainWindow attaches during construction, when Current is still the Light
        // default, but this keeps the two orderings equivalent rather than order-dependent.
        if (this.Current == AppTheme.FollowSystem)
        {
            this.StartWatching();
        }
    }

    public void Apply(AppTheme theme)
    {
        if (theme == this.Current)
        {
            return;
        }

        this.Current = theme;

        // Leaving "Follow system" must actually stop the watcher, or the next OS theme change
        // would silently overwrite the explicit Light/Dark choice the user just made. Unhook
        // before applying, so nothing re-resolves the system theme underneath the new one.
        if (theme != AppTheme.FollowSystem)
        {
            this.StopWatching();
        }

        var resolved = theme == AppTheme.FollowSystem
            ? ResolveFollowSystem(ApplicationThemeManager.GetSystemTheme())
            : theme;

        ApplicationThemeManager.Apply(resolved == AppTheme.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light);

        // D-7's "Follow system" means follow it from now on, not resolve it once. Without this,
        // Apply(FollowSystem) read the OS theme at that instant and never updated again.
        if (theme == AppTheme.FollowSystem)
        {
            this.StartWatching();
        }

        this.Changed?.Invoke(this, EventArgs.Empty);
    }

    private void StartWatching()
    {
        if (this.watching || this.window == null)
        {
            return;
        }

        // Wpf.Ui.Appearance.SystemThemeWatcher.Watch(Window, WindowBackdropType, bool) - the
        // backdrop argument defaults to Mica, which is what MainWindow already declares
        // (WindowBackdropType="Mica"), so the default is left alone deliberately.
        SystemThemeWatcher.Watch(this.window);
        this.watching = true;
    }

    private void StopWatching()
    {
        if (!this.watching || this.window == null)
        {
            return;
        }

        SystemThemeWatcher.UnWatch(this.window);
        this.watching = false;
    }
}
