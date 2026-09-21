using System;
using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace Walkabout.Tests.Business.AppServices
{
    /// <summary>
    /// The concurrency path, tested from the very first slice rather than deferred - spec section
    /// 5, slice 7. FaultInjectingStore is the named, first-class way to provoke a conflict on
    /// demand, which is what the owner asked the test business layer for (spec section 2.4).
    /// </summary>
    [TestFixture]
    public class AddAccountRetryTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        [Test]
        public void AddAccount_RecoversFromASingleConflictByRequeryingAndRetrying()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);      // the id the first attempt will allocate
            var service = new AddAccountService(faulty, this.fixture.Query);

            Account created = service.AddAccount("Checking", AccountType.Checking, "USD");

            Assert.That(created.Id, Is.EqualTo(1));
            Assert.That(this.fixture.Store.LoadAccounts(), Has.Count.EqualTo(1));
        }

        [Test]
        public void AddAccount_AfterTooManyConflicts_GivesUpWithAClearException()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            for (int i = 0; i < AddAccountService.MaxAttempts; i++)
            {
                faulty.ThrowConflictFor(1);
            }

            var service = new AddAccountService(faulty, this.fixture.Query);

            var ex = Assert.Throws<ConcurrencyRetryExhaustedException>(
                () => service.AddAccount("Checking", AccountType.Checking, "USD"));

            Assert.That(ex.Attempts, Is.EqualTo(AddAccountService.MaxAttempts));
            Assert.That(ex.InnerException, Is.InstanceOf<ConcurrencyConflictException>());
            Assert.That(this.fixture.Store.LoadAccounts(), Is.Empty);
        }

        [Test]
        public void TheRetryLoop_DoesNotRetryAnArgumentException()
        {
            // Spec sections 1.6a consequence 1 and 1.6c: a precondition failure is a CALLER BUG,
            // unconditionally reproducible. A loop that caught it would retry a call that can only
            // ever fail the same way, turning an immediate, obvious crash into a spin.
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(1, new ArgumentException("root is in the wrong state"));
            var service = new AddAccountService(faulty, this.fixture.Query);

            Assert.Throws<ArgumentException>(() => service.AddAccount("Checking", AccountType.Checking, "USD"));
        }

        [Test]
        public void TheRetryLoop_DoesNotSwallowAnUnrelatedFailure()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(1, new InvalidOperationException("the disk caught fire"));
            var service = new AddAccountService(faulty, this.fixture.Query);

            Assert.Throws<InvalidOperationException>(() => service.AddAccount("Checking", AccountType.Checking, "USD"));
        }

        [Test]
        public void TheRetry_ReQueriesRatherThanReusingTheStaleState()
        {
            // The whole reason the first attempt failed is that its state was stale, so a retry
            // that did not go back to the data layer would fail identically. Here: someone else
            // took id 1 during the conflict, and the retry must pick up id 2.
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);
            var service = new AddAccountService(faulty, this.fixture.Query);

            // The "someone else" - written through the inner store, so the decorator does not see it.
            this.fixture.Store.SaveRoot(new Account(new Accounts((PersistentObject)null))
            {
                Id = 1, Name = "Theirs", Type = AccountType.Cash, OpeningBalance = 0m
            });

            Account created = service.AddAccount("Mine", AccountType.Checking, "USD");

            Assert.That(created.Id, Is.EqualTo(2));
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All).Select(r => r.Name),
                Is.EqualTo(new[] { "Theirs", "Mine" }));
        }

        [Test]
        public void TheRetry_StillRefusesADuplicateNameThatAppearedDuringTheConflict()
        {
            // Re-query means re-validate: a name that became taken between the two attempts must
            // be refused, not written. This is why the retry re-runs the whole use case body
            // rather than just re-issuing the failed SaveRoot.
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);
            var service = new AddAccountService(faulty, this.fixture.Query);

            this.fixture.Store.SaveRoot(new Account(new Accounts((PersistentObject)null))
            {
                Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m
            });

            Assert.Throws<DuplicateAccountNameException>(
                () => service.AddAccount("Checking", AccountType.Checking, "USD"));
        }
    }
}
