// Source/WPF/MyMoney.Shell/Views/AccountsView.xaml.cs
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MyMoney.Shell.Controls;
using MyMoney.Shell.Resources;
using MyMoney.Shell.Search;
using MyMoney.Shell.Services;
using MyMoney.Shell.ViewModels;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace MyMoney.Shell.Views;

public partial class AccountsView : UserControl, ISearchableView
{
    private readonly AccountsListViewModel listViewModel;
    private readonly IDialogService dialogService;
    private readonly IStatusService statusService;
    private readonly AddAccountService addAccountService;
    private readonly EditAccountService editAccountService;
    private readonly DeleteAccountService deleteAccountService;

    public AccountsView(AccountsListViewModel listViewModel, IDialogService dialogService,
        IStatusService statusService, AddAccountService addAccountService,
        EditAccountService editAccountService, DeleteAccountService deleteAccountService)
    {
        InitializeComponent();
        this.listViewModel = listViewModel;
        this.dialogService = dialogService;
        this.statusService = statusService;
        this.addAccountService = addAccountService;
        this.editAccountService = editAccountService;
        this.deleteAccountService = deleteAccountService;
        this.DataContext = listViewModel;

        // Reusable across every simple-table screen (see GridViewSorter's own doc comment) -
        // Transactions will need its own design once that screen exists.
        GridViewSorter.Attach(this.AccountsListView, defaultColumn: "Name");
    }

    private void AccountsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = this.AccountsListView.SelectedItem is AccountRowViewModel;
        this.EditAccountButton.IsEnabled = hasSelection;
        this.DeleteAccountButton.IsEnabled = hasSelection;
    }

    /// <summary>
    /// ISearchableView per D-9: filters the grid to rows whose Name contains the query
    /// (case-insensitive), then moves the selection per step. All rows in the filtered view
    /// already match by construction, so Next/Previous is a plain wrapped cycle through them -
    /// IncrementalSearch's Matches/FindNext/FindPrevious still do the work, just over the
    /// already-filtered list rather than the full one.
    /// </summary>
    public bool ApplySearch(string query, SearchStep step)
    {
        ICollectionView view = CollectionViewSource.GetDefaultView(this.AccountsListView.ItemsSource);

        if (string.IsNullOrEmpty(query))
        {
            view.Filter = null;
            return true;
        }

        view.Filter = o => o is AccountRowViewModel row && IncrementalSearch.Matches(row.Name, query);

        var visible = view.Cast<AccountRowViewModel>().ToList();
        if (visible.Count == 0)
        {
            this.AccountsListView.SelectedItem = null;
            return false;
        }

        int currentIndex = this.AccountsListView.SelectedItem is AccountRowViewModel current
            ? visible.IndexOf(current)
            : -1;
        var names = visible.Select(a => a.Name).ToList();

        int targetIndex = step switch
        {
            SearchStep.Next => IncrementalSearch.FindNext(names, currentIndex, query),
            SearchStep.Previous => IncrementalSearch.FindPrevious(names, currentIndex, query),
            _ => 0, // First: the filtered set is all matches, so its first row is always index 0.
        };

        if (targetIndex < 0)
        {
            targetIndex = 0;
        }

        var target = visible[targetIndex];
        this.AccountsListView.SelectedItem = target;
        this.AccountsListView.ScrollIntoView(target);
        return true;
    }

    private async void AddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        // A fresh view model per BUTTON CLICK (not per dialog show), per Task 9's
        // cancel-restores note - reusing one across separate "Add Account" clicks would leak
        // a previous attempt's typed values into the next. Within a single click's flow,
        // though, the SAME view model is deliberately reused across a validation-failure
        // retry loop below - that's what carries the typed values and ErrorMessage forward
        // so the user doesn't have to retype everything after a typo.
        var addViewModel = new AddAccountViewModel(this.addAccountService);

        // WPF-UI's ContentDialog (4.3.0) closes as soon as a button is clicked - its
        // ButtonClicked event carries no deferral/cancel capability, so there is no way to
        // keep the dialog open pending validation from inside the dialog itself. Instead, the
        // SHOW is looped here: on a validation failure (Succeeded == false), re-show the
        // dialog with the same view model, so ErrorMessage is now populated and the user sees
        // it on the re-opened dialog, with their previously typed values still intact. A new
        // AddAccountView is constructed on each loop iteration - the previous one still
        // logically belongs to the just-closed ContentDialog, so reparenting it would be the
        // wrong move; the view model is the one thing that should carry over.
        while (true)
        {
            var content = new AddAccountView(addViewModel);
            var outcome = await this.dialogService.ShowAsync(content, Strings.AddAccountDialogTitle, Strings.AddAccountCommitLabel);

            if (outcome != DialogOutcome.Committed)
            {
                return;
            }

            addViewModel.CommitCommand.Execute(null);

            if (addViewModel.Succeeded)
            {
                await this.listViewModel.LoadCommand.ExecuteAsync(null);

                // D-5's status model had no producers at all until this: the status bar read
                // "Ready" and the activity list "(0)" for the whole app's life, however much
                // the user did. Adding an account is the one completed, user-initiated action
                // this slice has, so it is the one that reports. ShowStatus is the transient
                // line; LogActivity is the durable entry behind the Activity button.
                this.statusService.ShowStatus(string.Format(Strings.AccountAddedStatusFormat, addViewModel.Name));
                this.statusService.LogActivity(string.Format(Strings.AccountAddedActivityFormat, addViewModel.Name));
                return;
            }

            // else: loop and re-show with the same (now error-carrying) view model.
        }
    }

    private async void EditAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.AccountsListView.SelectedItem is not AccountRowViewModel selected)
        {
            return;
        }

        // Same fresh-per-click / shared-within-retry-loop shape as AddAccountButton_Click.
        var editViewModel = new EditAccountViewModel(
            this.editAccountService, selected.Id, selected.Name,
            Enum.Parse<AccountType>(selected.Type), selected.Currency);

        while (true)
        {
            var content = new EditAccountView(editViewModel);
            var outcome = await this.dialogService.ShowAsync(content, Strings.EditAccountDialogTitle, Strings.EditAccountCommitLabel);

            if (outcome != DialogOutcome.Committed)
            {
                return;
            }

            editViewModel.CommitCommand.Execute(null);

            if (editViewModel.Succeeded)
            {
                await this.listViewModel.LoadCommand.ExecuteAsync(null);
                this.statusService.ShowStatus(string.Format(Strings.AccountUpdatedStatusFormat, editViewModel.Name));
                this.statusService.LogActivity(string.Format(Strings.AccountUpdatedActivityFormat, editViewModel.Name));
                return;
            }

            // else: loop and re-show with the same (now error-carrying) view model.
        }
    }

    private async void DeleteAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.AccountsListView.SelectedItem is not AccountRowViewModel selected)
        {
            return;
        }

        // No retry-with-typed-values concern here (there is nothing to type), but the same
        // loop-the-show shape applies: a rare concurrency conflict re-shows the confirmation
        // with the error visible rather than silently closing.
        var deleteViewModel = new DeleteAccountViewModel(this.deleteAccountService, selected.Id, selected.Name);

        while (true)
        {
            var content = new DeleteAccountView(deleteViewModel);
            var outcome = await this.dialogService.ShowAsync(content, Strings.DeleteAccountDialogTitle, Strings.DeleteAccountCommitLabel);

            if (outcome != DialogOutcome.Committed)
            {
                return;
            }

            deleteViewModel.CommitCommand.Execute(null);

            if (deleteViewModel.Succeeded)
            {
                await this.listViewModel.LoadCommand.ExecuteAsync(null);
                this.statusService.ShowStatus(string.Format(Strings.AccountDeletedStatusFormat, deleteViewModel.AccountName));
                this.statusService.LogActivity(string.Format(Strings.AccountDeletedActivityFormat, deleteViewModel.AccountName));
                return;
            }

            // else: loop and re-show with the same (now error-carrying) view model.
        }
    }
}
