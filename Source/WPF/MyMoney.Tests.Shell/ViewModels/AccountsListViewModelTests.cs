using System.Threading.Tasks;
using MyMoney.Shell.ViewModels;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace MyMoney.Tests.Shell.ViewModels;

[TestFixture]
public class AccountsListViewModelTests
{
    [Test]
    public async Task Load_PopulatesAccountsFromTheQueryPort()
    {
        using var fixture = InMemorySqliteStore.Create();
        var addAccountService = new AddAccountService(fixture.Store, fixture.Query);
        addAccountService.AddAccount("Checking", AccountType.Checking, "USD");
        addAccountService.AddAccount("Brokerage", AccountType.Brokerage, "USD");

        var viewModel = new AccountsListViewModel(fixture.Query);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(viewModel.Accounts, Has.Count.EqualTo(2));
        Assert.That(viewModel.Accounts[0].Name, Is.EqualTo("Checking"));
        Assert.That(viewModel.Accounts[1].Name, Is.EqualTo("Brokerage"));
    }

    [Test]
    public async Task Load_OnEmptyStore_ProducesEmptyList()
    {
        using var fixture = InMemorySqliteStore.Create();

        var viewModel = new AccountsListViewModel(fixture.Query);
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.That(viewModel.Accounts, Is.Empty);
    }
}
