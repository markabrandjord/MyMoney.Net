using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace MyMoney.Shell.ViewModels;

public sealed partial class EditAccountViewModel : ObservableObject
{
    private readonly EditAccountService editAccountService;
    private readonly long accountId;

    public EditAccountViewModel(EditAccountService editAccountService, long accountId,
        string initialName, AccountType initialType, string initialCurrency)
    {
        this.editAccountService = editAccountService;
        this.accountId = accountId;
        this.name = initialName;
        this.type = initialType;
        this.currency = initialCurrency;
    }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private AccountType type;

    [ObservableProperty]
    private string currency;

    [ObservableProperty]
    private string? errorMessage;

    public bool Succeeded { get; private set; }

    [RelayCommand]
    private void Commit()
    {
        try
        {
            this.editAccountService.EditAccount(this.accountId, this.Name, this.Type, this.Currency);
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
