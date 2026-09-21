using System;
using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace Walkabout.Tests.Business.AppServices
{
    [TestFixture]
    public class EditAccountServiceTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private EditAccountService NewService(IMoneyStore store = null) =>
            new EditAccountService(store ?? this.fixture.Store, this.fixture.Query);

        private long SeedAccount(string name, AccountType type = AccountType.Checking, string currency = "USD") =>
            new AddAccountService(this.fixture.Store, this.fixture.Query).AddAccount(name, type, currency).Id;

        [Test]
        public void EditAccount_UpdatesNameTypeAndCurrency_AndPersists()
        {
            long id = this.SeedAccount("Checking");

            this.NewService().EditAccount(id, "Everyday Checking", AccountType.Savings, "EUR");

            Account persisted = this.fixture.Store.LoadAccounts().Single();
            Assert.That(persisted.Name, Is.EqualTo("Everyday Checking"));
            Assert.That(persisted.Type, Is.EqualTo(AccountType.Savings));
            Assert.That(persisted.Currency, Is.EqualTo("EUR"));
        }

        [Test]
        public void EditAccount_RenamingToItsOwnCurrentName_IsAllowed()
        {
            long id = this.SeedAccount("Checking");

            Assert.DoesNotThrow(() => this.NewService().EditAccount(id, "Checking", AccountType.Checking, "USD"));
        }

        [Test]
        public void EditAccount_RefusesRenamingToAnotherAccountsName_CaseInsensitively()
        {
            long id = this.SeedAccount("Checking");
            this.SeedAccount("Savings");

            var ex = Assert.Throws<DuplicateAccountNameException>(
                () => this.NewService().EditAccount(id, "SAVINGS", AccountType.Checking, "USD"));

            Assert.That(ex.AccountName, Is.EqualTo("SAVINGS"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void EditAccount_RefusesAnEmptyName(string name)
        {
            long id = this.SeedAccount("Checking");

            Assert.Throws<ArgumentException>(() => this.NewService().EditAccount(id, name, AccountType.Checking, "USD"));
        }

        [Test]
        public void EditAccount_RefusesANameContainingACharacterTheDomainForbids()
        {
            long id = this.SeedAccount("Checking");

            Assert.Throws<ArgumentException>(
                () => this.NewService().EditAccount(id, "Bad:Name", AccountType.Checking, "USD"));
        }

        [Test]
        public void EditAccount_ThrowsIfTheAccountNoLongerExists()
        {
            Assert.Throws<AccountNotFoundException>(
                () => this.NewService().EditAccount(999, "Ghost", AccountType.Checking, "USD"));
        }

        [Test]
        public void EditAccount_RecoversFromASingleConflictByRequeryingAndRetrying()
        {
            long id = this.SeedAccount("Checking");
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(id);
            var service = new EditAccountService(faulty, this.fixture.Query);

            service.EditAccount(id, "Renamed", AccountType.Checking, "USD");

            Assert.That(this.fixture.Store.LoadAccounts().Single().Name, Is.EqualTo("Renamed"));
        }

        [Test]
        public void EditAccount_AfterTooManyConflicts_GivesUpWithAClearException()
        {
            long id = this.SeedAccount("Checking");
            var faulty = new FaultInjectingStore(this.fixture.Store);
            for (int i = 0; i < EditAccountService.MaxAttempts; i++)
            {
                faulty.ThrowConflictFor(id);
            }
            var service = new EditAccountService(faulty, this.fixture.Query);

            var ex = Assert.Throws<ConcurrencyRetryExhaustedException>(
                () => service.EditAccount(id, "Renamed", AccountType.Checking, "USD"));

            Assert.That(ex.Attempts, Is.EqualTo(EditAccountService.MaxAttempts));
            Assert.That(this.fixture.Store.LoadAccounts().Single().Name, Is.EqualTo("Checking"),
                "A retry-exhausted edit must leave the original data untouched.");
        }

        [Test]
        public void TheRetryLoop_DoesNotRetryAnArgumentException()
        {
            long id = this.SeedAccount("Checking");
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(1, new ArgumentException("root is in the wrong state"));
            var service = new EditAccountService(faulty, this.fixture.Query);

            Assert.Throws<ArgumentException>(() => service.EditAccount(id, "Renamed", AccountType.Checking, "USD"));
        }
    }
}
