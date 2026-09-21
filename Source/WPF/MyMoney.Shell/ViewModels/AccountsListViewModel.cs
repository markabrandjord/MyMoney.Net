using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Walkabout.Data;

namespace MyMoney.Shell.ViewModels;

public sealed partial class AccountsListViewModel : ObservableObject
{
    private readonly IMoneyQuery query;

    public AccountsListViewModel(IMoneyQuery query)
    {
        this.query = query;
    }

    public ObservableCollection<AccountRowViewModel> Accounts { get; } = new();

    [RelayCommand]
    private Task Load()
    {
        this.Accounts.Clear();

        foreach (AccountRow row in this.query.ListAccounts(AccountQuery.All))
        {
            this.Accounts.Add(new AccountRowViewModel(row.Id, row.Name, row.Type.ToString(), row.Currency, row.OpeningBalance));
        }

        return Task.CompletedTask;
    }
}
