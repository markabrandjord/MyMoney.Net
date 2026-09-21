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
            var outcome = await this.dialogService.ShowAsync(content, "Add Account", "Add");

            if (outcome != DialogOutcome.Committed)
            {
                return;
            }

            addViewModel.CommitCommand.Execute(null);

            if (addViewModel.Succeeded)
            {
                await this.listViewModel.LoadCommand.ExecuteAsync(null);
                return;
            }

            // else: loop and re-show with the same (now error-carrying) view model.
        }
    }
}
