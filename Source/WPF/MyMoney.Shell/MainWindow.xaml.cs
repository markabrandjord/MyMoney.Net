using System;
using System.Windows;
using System.Windows.Controls;
using MyMoney.Shell.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MyMoney.Shell;

public partial class MainWindow : FluentWindow
{
    private readonly IThemeService themeService;
    private readonly IStatusService statusService;
    private readonly INavigationService navigationService;

    public MainWindow(IThemeService themeService, IStatusService statusService,
        INavigationService navigationService, IDialogService dialogService)
    {
        InitializeComponent();

        this.themeService = themeService;
        this.statusService = statusService;
        this.navigationService = navigationService;

        this.statusService.Changed += this.OnStatusChanged;
        this.RefreshStatus();

        if (dialogService is DialogService concreteDialogService)
        {
            concreteDialogService.SetHost(this.DialogHost);
        }

        this.navigationService.Navigated += (_, route) =>
        {
            this.NavAccountsButton.Appearance = route == RouteId.Accounts
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;
        };
    }

    private void OnStatusChanged(object? sender, EventArgs e) => this.RefreshStatus();

    private void RefreshStatus()
    {
        this.StatusText.Text = this.statusService.CurrentStatus;
        this.ActivityButton.Content = $"Activity ({this.statusService.Activity.Count})";

        this.ActivityListPanel.Children.Clear();
        foreach (var entry in this.statusService.Activity)
        {
            this.ActivityListPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{entry.Timestamp:h:mm tt} — {entry.Message}",
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
    }

    private void ThemeLightButton_Click(object sender, RoutedEventArgs e) => this.themeService.Apply(AppTheme.Light);
    private void ThemeDarkButton_Click(object sender, RoutedEventArgs e) => this.themeService.Apply(AppTheme.Dark);
    private void ThemeSystemButton_Click(object sender, RoutedEventArgs e) => this.themeService.Apply(AppTheme.FollowSystem);

    private void NavAccountsButton_Click(object sender, RoutedEventArgs e) => this.navigationService.NavigateTo(RouteId.Accounts);

    private void ActivityButton_Click(object sender, RoutedEventArgs e) => this.ActivityPopup.IsOpen = !this.ActivityPopup.IsOpen;
}
