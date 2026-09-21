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
    public void Commit_WithBlankName_SetsErrorMessageForValidationError()
    {
        using var fixture = InMemorySqliteStore.Create();
        var service = new AddAccountService(fixture.Store, fixture.Query);
        var viewModel = new AddAccountViewModel(service)
        {
            Name = "   ", // blank/whitespace name
            Type = AccountType.Checking,
            Currency = "USD",
        };

        viewModel.CommitCommand.Execute(null);

        Assert.That(viewModel.Succeeded, Is.False);
        Assert.That(viewModel.ErrorMessage, Is.Not.Null);
    }

    [Test]
    public void DefaultConstruction_HasCorrectDefaults()
    {
        // Verifies that a freshly constructed AddAccountViewModel has correct initial values.
        // The real cancel-restore guarantee (D-13) — that typed-but-uncommitted state is
        // discarded when a dialog is cancelled — is implemented by Task 10's dialog opening
        // code constructing a NEW AddAccountViewModel on each "Add Account" click, rather
        // than reusing/resetting one in place. That behavior is verified by Task 10's own
        // tests against its dialog-opening code path, not here.
        using var fixture = InMemorySqliteStore.Create();
        var service = new AddAccountService(fixture.Store, fixture.Query);

        var viewModel = new AddAccountViewModel(service);

        Assert.That(viewModel.Name, Is.EqualTo(string.Empty));
        Assert.That(viewModel.Currency, Is.EqualTo("USD"));
        Assert.That(viewModel.ErrorMessage, Is.Null);
        Assert.That(viewModel.Succeeded, Is.False);
    }
}
