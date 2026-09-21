using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Walkabout.Business.AppServices;

namespace MyMoney.Shell.ViewModels;

public sealed partial class DeleteAccountViewModel : ObservableObject
{
    private readonly DeleteAccountService deleteAccountService;
    private readonly long accountId;

    public DeleteAccountViewModel(DeleteAccountService deleteAccountService, long accountId, string accountName)
    {
        this.deleteAccountService = deleteAccountService;
        this.accountId = accountId;
        this.AccountName = accountName;
    }

    public string AccountName { get; }

    [ObservableProperty]
    private string? errorMessage;

    public bool Succeeded { get; private set; }

    [RelayCommand]
    private void Commit()
    {
        try
        {
            this.deleteAccountService.DeleteAccount(this.accountId);
            this.Succeeded = true;
            this.ErrorMessage = null;
        }
        catch (AccountNotFoundException ex)
        {
            this.Succeeded = false;
            this.ErrorMessage = ex.Message;
        }
        catch (ConcurrencyRetryExhaustedException ex)
        {
            this.Succeeded = false;
            this.ErrorMessage = ex.Message;
        }
    }
}
