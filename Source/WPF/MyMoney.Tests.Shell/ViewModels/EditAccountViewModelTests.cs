using MyMoney.Shell.ViewModels;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace MyMoney.Tests.Shell.ViewModels;

[TestFixture]
public class EditAccountViewModelTests
{
    [Test]
    public void Construction_PrePopulatesFieldsFromTheGivenAccount()
    {
        using var fixture = InMemorySqliteStore.Create();
        var editService = new EditAccountService(fixture.Store, fixture.Query);

        var viewModel = new EditAccountViewModel(editService, 1, "Checking", AccountType.Checking, "USD");

        Assert.That(viewModel.Name, Is.EqualTo("Checking"));
        Assert.That(viewModel.Type, Is.EqualTo(AccountType.Checking));
        Assert.That(viewModel.Currency, Is.EqualTo("USD"));
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(viewModel.Succeeded, Is.False);
    }

    [Test]
    public void Commit_WithValidFields_UpdatesTheAccountAndSetsSucceeded()
    {
        using var fixture = InMemorySqliteStore.Create();
        long id = new AddAccountService(fixture.Store, fixture.Query)
            .AddAccount("Checking", AccountType.Checking, "USD").Id;
        var editService = new EditAccountService(fixture.Store, fixture.Query);
        var viewModel = new EditAccountViewModel(editService, id, "Checking", AccountType.Checking, "USD")
        {
            Name = "Everyday Checking",
            Type = AccountType.Savings,
        };

        viewModel.CommitCommand.Execute(null);

        Assert.That(viewModel.Succeeded, Is.True);
        Assert.That(viewModel.ErrorMessage, Is.Null);
        var persisted = fixture.Query.ListAccounts(AccountQuery.All)[0];
        Assert.That(persisted.Name, Is.EqualTo("Everyday Checking"));
        Assert.That(persisted.Type, Is.EqualTo(AccountType.Savings));
    }

    [Test]
    public void Commit_WithDuplicateName_SetsErrorMessageInsteadOfThrowing()
    {
        using var fixture = InMemorySqliteStore.Create();
        var addService = new AddAccountService(fixture.Store, fixture.Query);
        long id = addService.AddAccount("Checking", AccountType.Checking, "USD").Id;
        addService.AddAccount("Savings", AccountType.Savings, "USD");
        var editService = new EditAccountService(fixture.Store, fixture.Query);
        var viewModel = new EditAccountViewModel(editService, id, "Checking", AccountType.Checking, "USD")
        {
            Name = "Savings",
        };

        viewModel.CommitCommand.Execute(null);

        Assert.That(viewModel.Succeeded, Is.False);
        Assert.That(viewModel.ErrorMessage, Does.Contain("Savings"));
    }

    [Test]
    public void Commit_WithBlankName_SetsErrorMessageForValidationError()
    {
        using var fixture = InMemorySqliteStore.Create();
        long id = new AddAccountService(fixture.Store, fixture.Query)
            .AddAccount("Checking", AccountType.Checking, "USD").Id;
        var editService = new EditAccountService(fixture.Store, fixture.Query);
        var viewModel = new EditAccountViewModel(editService, id, "Checking", AccountType.Checking, "USD")
        {
            Name = "   ",
        };

        viewModel.CommitCommand.Execute(null);

        Assert.That(viewModel.Succeeded, Is.False);
        Assert.That(viewModel.ErrorMessage, Is.Not.Null);
    }

    [Test]
    public void Commit_AfterExhaustingRetries_SetsErrorMessageInsteadOfThrowing()
    {
        using var fixture = InMemorySqliteStore.Create();
        long id = new AddAccountService(fixture.Store, fixture.Query)
            .AddAccount("Checking", AccountType.Checking, "USD").Id;
        var faulty = new FaultInjectingStore(fixture.Store);
        for (int i = 0; i < EditAccountService.MaxAttempts; i++)
        {
            faulty.ThrowConflictFor(id);
        }
        var editService = new EditAccountService(faulty, fixture.Query);
        var viewModel = new EditAccountViewModel(editService, id, "Checking", AccountType.Checking, "USD")
        {
            Name = "Renamed",
        };

        Assert.DoesNotThrow(() => viewModel.CommitCommand.Execute(null));

        Assert.That(viewModel.Succeeded, Is.False);
        Assert.That(viewModel.ErrorMessage, Is.Not.Null);
    }
}
