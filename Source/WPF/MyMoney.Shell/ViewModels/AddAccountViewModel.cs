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
    }
}
