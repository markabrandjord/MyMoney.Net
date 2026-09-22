using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyMoney.Shell.Resources;
using MyMoney.Shell.Search;
using MyMoney.Shell.Services;
using MyMoney.Shell.ViewModels;
using MyMoney.Shell.Views;
using Walkabout.Business.AppServices;
using Walkabout.Data;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MyMoney.Shell;

public partial class MainWindow : FluentWindow
{
    private readonly IThemeService themeService;
    private readonly IStatusService statusService;
    private readonly INavigationService navigationService;
    private readonly IDialogService dialogService;
    private readonly IMoneyQuery query;
    private readonly AddAccountService addAccountService;
    private readonly EditAccountService editAccountService;
    private readonly DeleteAccountService deleteAccountService;

    public MainWindow(IThemeService themeService, IStatusService statusService,
        INavigationService navigationService, IDialogService dialogService, IMoneyQuery query, IMoneyStore store)
    {
        InitializeComponent();

        this.themeService = themeService;
        this.statusService = statusService;
        this.navigationService = navigationService;
        this.dialogService = dialogService;
        this.query = query;
        this.addAccountService = new AddAccountService(store, query);
        this.editAccountService = new EditAccountService(store, query);
        this.deleteAccountService = new DeleteAccountService(store, query);

        this.statusService.Changed += this.OnStatusChanged;
        this.RefreshStatus();

        if (dialogService is DialogService concreteDialogService)
        {
            concreteDialogService.SetHost(this.DialogHost);
        }

        // Same shape as the DialogService host hand-off above: the window reference that
        // SystemThemeWatcher needs is a UI detail, so it is handed to the concrete service
        // rather than widening IThemeService. Without it, "Follow system" resolves the OS theme
        // once and never notices a later change.
        if (themeService is ThemeService concreteThemeService)
        {
            concreteThemeService.AttachWindow(this);
        }

        this.navigationService.Navigated += (_, route) =>
        {
            this.NavAccountsButton.Appearance = route == RouteId.Accounts
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;

            // D-9's focus/lifetime rule: a search query is scoped to the screen it was typed
            // against, so navigating to a different screen clears it - clear the text (not
            // just the filter) so the box doesn't misleadingly show a stale query for content
            // it was never applied to. This TextChanged fire is harmless: SearchBox.IsEnabled
            // is updated below, after the new content is hosted, so a disabled/not-yet-set
            // search box just no-ops it.
            this.SearchBox.Text = string.Empty;

            if (route == RouteId.Accounts)
            {
                var viewModel = new AccountsListViewModel(this.query);
                this.ContentHost.Content = new AccountsView(viewModel, this.dialogService, this.statusService,
                    this.addAccountService, this.editAccountService, this.deleteAccountService);
                viewModel.LoadCommand.Execute(null);
            }

            // D-9's "nothing to search here" state: disabled, not hidden, when the hosted view
            // doesn't opt in to ISearchableView. Previous/Next also start disabled regardless -
            // the search box was just cleared above, so there is nothing yet to advance
            // through (see the "Next/Previous availability" entry under D-9).
            bool searchable = this.ContentHost.Content is ISearchableView;
            this.SearchBox.IsEnabled = searchable;
            this.SearchPreviousButton.IsEnabled = false;
            this.SearchNextButton.IsEnabled = false;
        };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Unlike Next/Previous, an empty query IS meaningful here - it must actually clear the
        // filter (ApplySearch("", ...) does this and returns true), not just no-op, or
        // backspacing the search box to empty would leave the grid filtered forever.
        if (this.ContentHost.Content is not ISearchableView searchable)
        {
            return;
        }

        string query = this.SearchBox.Text;

        // Next/Previous availability tracks whether a search is actually active (D-9) - enabled
        // only once the query is non-empty, disabled the instant it's cleared by any means.
        bool hasQuery = !string.IsNullOrEmpty(query);
        this.SearchPreviousButton.IsEnabled = hasQuery;
        this.SearchNextButton.IsEnabled = hasQuery;

        if (!searchable.ApplySearch(query, SearchStep.First) && hasQuery)
        {
            this.statusService.ShowStatus(string.Format(Strings.NoMatchesStatusFormat, query));
        }
    }

    private void SearchPreviousButton_Click(object sender, RoutedEventArgs e) =>
        this.RunSearch(SearchStep.Previous);

    private void SearchNextButton_Click(object sender, RoutedEventArgs e) =>
        this.RunSearch(SearchStep.Next);

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                this.SearchBox.Text = string.Empty;
                e.Handled = true;
                break;
            case Key.Enter:
                this.RunSearch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? SearchStep.Previous : SearchStep.Next);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// D-9: routes an explicit Next/Previous request to whichever view is currently hosted, if
    /// it opts in to ISearchableView. A blank query has nothing to advance through, so it's a
    /// no-op here (as opposed to SearchBox_TextChanged, where an empty query must still clear
    /// the filter).
    /// </summary>
    private void RunSearch(SearchStep step)
    {
        if (this.ContentHost.Content is not ISearchableView searchable)
        {
            return;
        }

        string query = this.SearchBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        if (!searchable.ApplySearch(query, step))
        {
            this.statusService.ShowStatus(string.Format(Strings.NoMatchesStatusFormat, query));
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e) => this.RefreshStatus();

    private void RefreshStatus()
    {
        this.StatusText.Text = this.statusService.CurrentStatus;
        this.ActivityButton.Content = string.Format(Strings.ActivityButtonFormat, this.statusService.Activity.Count);

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
