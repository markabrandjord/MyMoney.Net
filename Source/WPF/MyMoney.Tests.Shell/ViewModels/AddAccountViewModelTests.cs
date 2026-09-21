using MyMoney.Shell.ViewModels;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace MyMoney.Tests.Shell.ViewModels;

[TestFixture]
public class AddAccountViewModelTests
{
    [Test]
    public void Commit_WithValidFields_AddsAccountAndSetsSucceeded()
    {
        using var fixture = InMemorySqliteStore.Create();
        var service = new AddAccountService(fixture.Store, fixture.Query);
        var viewModel = new AddAccountViewModel(service)
        {
            Name = "Vacation Fund",
            Type = AccountType.Brokerage,
            Currency = "USD",
        };

        viewModel.CommitCommand.Execute(null);

        Assert.That(viewModel.Succeeded, Is.True);
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(fixture.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(1));
    }

    [Test]
    public void Commit_WithDuplicateName_SetsErrorMessageInsteadOfThrowing()
    {
        using var fixture = InMemorySqliteStore.Create();
        var service = new AddAccountService(fixture.Store, fixture.Query);
        service.AddAccount("Checking", AccountType.Checking, "USD");

        var viewModel = new AddAccountViewModel(service)
        {
            Name = "Checking",
            Type = AccountType.Checking,
            Currency = "USD",
        };

        viewModel.CommitCommand.Execute(null);

        Assert.That(viewModel.Succeeded, Is.False);
        Assert.That(viewModel.ErrorMessage, Does.Contain("Checking"));
    }

    [Test]
    public void CancelRestores_ResettingFieldsProducesAFreshViewModelState()
    {
        // D-13's contract lives here: "Cancel restores" is implemented as "the dialog that
        // owned this view model is discarded, taking its typed-but-uncommitted state with
        // it" - not as an in-place reset. This test documents that a freshly constructed
        // view model has no residue from a previous, cancelled one; Task 10's dialog code
        // constructs a NEW AddAccountViewModel every time "Add Account" is clicked, rather
        // than reusing one, which is what actually makes cancel-restores true.
        using var fixture = InMemorySqliteStore.Create();
        var service = new AddAccountService(fixture.Store, fixture.Query);

        var viewModel = new AddAccountViewModel(service);

        Assert.That(viewModel.Name, Is.EqualTo(string.Empty));
        Assert.That(viewModel.Currency, Is.EqualTo("USD"));
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(viewModel.Succeeded, Is.False);
    }
}
