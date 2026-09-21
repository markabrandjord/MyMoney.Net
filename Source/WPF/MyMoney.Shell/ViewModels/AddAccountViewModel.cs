using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace MyMoney.Shell.ViewModels;

public sealed partial class AddAccountViewModel : ObservableObject
{
    private readonly AddAccountService addAccountService;

    public AddAccountViewModel(AddAccountService addAccountService)
    {
        this.addAccountService = addAccountService;
    }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private AccountType type = AccountType.Checking;

    [ObservableProperty]
    private string currency = "USD";

    [ObservableProperty]
    private string? errorMessage;

    public bool Succeeded { get; private set; }

    [RelayCommand]
    private void Commit()
    {
        try
        {
            this.addAccountService.AddAccount(this.Name, this.Type, this.Currency);
            this.Succeeded = true;
            this.ErrorMessage = null;
        }
        catch (DuplicateAccountNameException ex)
        {
            this.Succeeded = false;
            this.ErrorMessage = ex.Message;
        }
        catch (ArgumentException ex)
        {
            this.Succeeded = false;
            this.ErrorMessage = ex.Message;
        }
        catch (ConcurrencyRetryExhaustedException ex)
        {
            // Now a live, reachable path since Task 10 gave Commit() a real caller (previously
            // this only mattered in theory - AddAccountService.AddAccount can throw this after
            // MaxAttempts losing writes, and an uncaught exception here would escape the
            // async-void AddAccountButton_Click handler and crash the app).
            this.Succeeded = false;
            this.ErrorMessage = ex.Message;
        }
    }
}
