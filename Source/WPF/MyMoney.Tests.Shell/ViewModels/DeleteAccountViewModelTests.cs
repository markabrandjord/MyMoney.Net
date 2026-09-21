using MyMoney.Shell.ViewModels;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace MyMoney.Tests.Shell.ViewModels;

[TestFixture]
public class DeleteAccountViewModelTests
{
    [Test]
    public void Construction_ExposesTheAccountNameForDisplay()
    {
        using var fixture = InMemorySqliteStore.Create();
        var deleteService = new DeleteAccountService(fixture.Store, fixture.Query);

        var viewModel = new DeleteAccountViewModel(deleteService, 1, "Checking");

        Assert.That(viewModel.AccountName, Is.EqualTo("Checking"));
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(viewModel.Succeeded, Is.False);
    }

    [Test]
    public void Commit_RemovesTheAccountAndSetsSucceeded()
    {
        using var fixture = InMemorySqliteStore.Create();
        long id = new AddAccountService(fixture.Store, fixture.Query)
            .AddAccount("Checking", AccountType.Checking, "USD").Id;
        var deleteService = new DeleteAccountService(fixture.Store, fixture.Query);
        var viewModel = new DeleteAccountViewModel(deleteService, id, "Checking");

        viewModel.CommitCommand.Execute(null);

        Assert.That(viewModel.Succeeded, Is.True);
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(fixture.Query.ListAccounts(AccountQuery.All), Is.Empty);
    }

    [Test]
    public void Commit_AfterExhaustingRetries_SetsErrorMessageInsteadOfThrowing()
    {
        using var fixture = InMemorySqliteStore.Create();
        long id = new AddAccountService(fixture.Store, fixture.Query)
            .AddAccount("Checking", AccountType.Checking, "USD").Id;
        var faulty = new FaultInjectingStore(fixture.Store);
        for (int i = 0; i < DeleteAccountService.MaxAttempts; i++)
        {
            faulty.ThrowConflictFor(id);
        }
        var deleteService = new DeleteAccountService(faulty, fixture.Query);
        var viewModel = new DeleteAccountViewModel(deleteService, id, "Checking");

        Assert.DoesNotThrow(() => viewModel.CommitCommand.Execute(null));

        Assert.That(viewModel.Succeeded, Is.False);
        Assert.That(viewModel.ErrorMessage, Is.Not.Null);
    }
}
