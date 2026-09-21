using System;
using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace Walkabout.Tests.Business.AppServices
{
    [TestFixture]
    public class AddAccountServiceTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private AddAccountService NewService(IMoneyStore store = null) =>
            new AddAccountService(store ?? this.fixture.Store, this.fixture.Query);

        [Test]
        public void AddAccount_PersistsTheAccountAndReturnsItWithAnIdAndAVersion()
        {
            Account created = this.NewService().AddAccount("Checking", AccountType.Checking, "USD");

            Assert.That(created.Id, Is.GreaterThan(0));
            Assert.That(created.RowVersion, Is.EqualTo(1));

            Account persisted = this.fixture.Store.LoadAccounts().Single();
            Assert.That(persisted.Name, Is.EqualTo("Checking"));
            Assert.That(persisted.Type, Is.EqualTo(AccountType.Checking));
            Assert.That(persisted.Currency, Is.EqualTo("USD"));
        }

        [Test]
        public void AddAccount_RunsWithNoUiPresent()
        {
            // Slice 7's success criterion, asserted rather than assumed: nothing in this call
            // path touches a dispatcher, a dialog or a callback port.
            AddAccountService service = this.NewService();

            Assert.DoesNotThrow(() => service.AddAccount("Headless", AccountType.Cash, "USD"));

            Assert.That(
                typeof(AddAccountService).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.EqualTo("PresentationFramework"));
        }

        [Test]
        public void AddAccount_AllocatesSuccessiveIds()
        {
            AddAccountService service = this.NewService();

            Account first = service.AddAccount("Checking", AccountType.Checking, "USD");
            Account second = service.AddAccount("Savings", AccountType.Savings, "USD");

            Assert.That(second.Id, Is.EqualTo(first.Id + 1));
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(2));
        }

        [Test]
        public void AddAccount_RefusesADuplicateNameCaseInsensitively_AndWritesNothing()
        {
            var recording = new RecordingStore(this.fixture.Store);
            AddAccountService service = this.NewService(recording);
            service.AddAccount("Checking", AccountType.Checking, "USD");
            int writesAfterFirst = recording.WriteCount;

            var ex = Assert.Throws<DuplicateAccountNameException>(
                () => service.AddAccount("CHECKING", AccountType.Savings, "USD"));

            Assert.That(ex.AccountName, Is.EqualTo("CHECKING"));
            Assert.That(recording.WriteCount, Is.EqualTo(writesAfterFirst),
                "The refusal must happen before any write, not after a partial one.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AddAccount_RefusesAnEmptyName(string name)
        {
            Assert.Throws<ArgumentException>(() => this.NewService().AddAccount(name, AccountType.Cash, "USD"));
        }

        [Test]
        public void AddAccount_RefusesANameContainingACharacterTheDomainForbids()
        {
            // Accounts.InvalidNameChars is the domain's own list; the service consults it rather
            // than inventing a second rule.
            Assert.Throws<ArgumentException>(() => this.NewService().AddAccount("Bad:Name", AccountType.Cash, "USD"));
        }

        [Test]
        public void AddAccount_UsesTheOrdinaryWriteSurfaceAndNothingPrivileged()
        {
            var recording = new RecordingStore(this.fixture.Store);

            this.NewService(recording).AddAccount("Checking", AccountType.Checking, "USD");

            Assert.That(recording.Calls, Has.Some.StartsWith("SaveRoot(Account#"));
            Assert.That(recording.Calls, Has.None.Contains("SaveRoots"));
        }
    }
}
