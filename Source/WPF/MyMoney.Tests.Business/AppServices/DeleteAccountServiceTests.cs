using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace Walkabout.Tests.Business.AppServices
{
    [TestFixture]
    public class DeleteAccountServiceTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private DeleteAccountService NewService(IMoneyStore store = null) =>
            new DeleteAccountService(store ?? this.fixture.Store, this.fixture.Query);

        private long SeedAccount(string name) =>
            new AddAccountService(this.fixture.Store, this.fixture.Query).AddAccount(name, AccountType.Checking, "USD").Id;

        [Test]
        public void DeleteAccount_RemovesIt()
        {
            long id = this.SeedAccount("Checking");
            this.SeedAccount("Savings");

            this.NewService().DeleteAccount(id);

            Assert.That(this.fixture.Store.LoadAccounts().Select(a => a.Name), Is.EqualTo(new[] { "Savings" }));
        }

        [Test]
        public void DeleteAccount_ThrowsIfTheAccountNoLongerExists()
        {
            Assert.Throws<AccountNotFoundException>(() => this.NewService().DeleteAccount(999));
        }

        [Test]
        public void DeleteAccount_RecoversFromASingleConflictByRequeryingAndRetrying()
        {
            long id = this.SeedAccount("Checking");
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(id);
            var service = new DeleteAccountService(faulty, this.fixture.Query);

            service.DeleteAccount(id);

            Assert.That(this.fixture.Store.LoadAccounts(), Is.Empty);
        }

        [Test]
        public void DeleteAccount_AfterTooManyConflicts_GivesUpWithAClearException()
        {
            long id = this.SeedAccount("Checking");
            var faulty = new FaultInjectingStore(this.fixture.Store);
            for (int i = 0; i < DeleteAccountService.MaxAttempts; i++)
            {
                faulty.ThrowConflictFor(id);
            }
            var service = new DeleteAccountService(faulty, this.fixture.Query);

            var ex = Assert.Throws<ConcurrencyRetryExhaustedException>(() => service.DeleteAccount(id));

            Assert.That(ex.Attempts, Is.EqualTo(DeleteAccountService.MaxAttempts));
            Assert.That(this.fixture.Store.LoadAccounts(), Has.Count.EqualTo(1),
                "A retry-exhausted delete must leave the original data untouched.");
        }

        [Test]
        public void TheRetryLoop_DoesNotRetryAnArgumentException()
        {
            long id = this.SeedAccount("Checking");
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(1, new System.ArgumentException("root is in the wrong state"));
            var service = new DeleteAccountService(faulty, this.fixture.Query);

            Assert.Throws<System.ArgumentException>(() => service.DeleteAccount(id));
        }
    }
}
