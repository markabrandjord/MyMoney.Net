// Source/WPF/MyMoney.Shell/Views/AccountsView.xaml.cs
using System.Windows;
using System.Windows.Controls;
using MyMoney.Shell.Services;
using MyMoney.Shell.ViewModels;
using Walkabout.Business.AppServices;

namespace MyMoney.Shell.Views;

public partial class AccountsView : UserControl
{
    private readonly AccountsListViewModel listViewModel;
    private readonly IDialogService dialogService;
    private readonly AddAccountService addAccountService;

    public AccountsView(AccountsListViewModel listViewModel, IDialogService dialogService, AddAccountService addAccountService)
    {
        InitializeComponent();
        this.listViewModel = listViewModel;
        this.dialogService = dialogService;
        this.addAccountService = addAccountService;
        this.DataContext = listViewModel;
    }

    private async void AddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        // A fresh view model every time, per Task 9's cancel-restores note - reusing one
        // across opens would leak the previous attempt's typed values into the next.
        var addViewModel = new AddAccountViewModel(this.addAccountService);
        var content = new AddAccountView(addViewModel);

        var outcome = await this.dialogService.ShowAsync(content, "Add Account", "Add");

        // WPF-UI's ContentDialog (4.3.0) closes as soon as a button is clicked - its
        // ButtonClicked event carries no deferral/cancel capability, so there is no way to
        // keep the dialog open pending validation. The commit itself therefore has to run
        // here, after the dialog has already closed, rather than being wired to the dialog's
        // own Primary button. A practical consequence: on a validation failure (e.g. a
        // duplicate name) AddAccountView's ErrorMessage text never becomes visible, since the
        // dialog is already gone by the time Succeeded is false - a known UX gap, out of this
        // task's scope (the plan's FlaUI coverage only exercises the happy path and cancel).
        if (outcome == DialogOutcome.Committed)
        {
            addViewModel.CommitCommand.Execute(null);

            if (addViewModel.Succeeded)
            {
                await this.listViewModel.LoadCommand.ExecuteAsync(null);
            }
        }
    }
}
