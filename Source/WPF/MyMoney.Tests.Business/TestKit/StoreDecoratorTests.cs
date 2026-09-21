using System;
using System.Collections.Generic;
using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Business.TestKit
{
    [TestFixture]
    public class StoreDecoratorTests
    {
        private InMemorySqliteStore fixture;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.fixture = InMemorySqliteStore.Create();
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private Account New(int id, string name) => new Account(this.container)
        {
            Id = id, Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 0m
        };

        [Test]
        public void InMemorySqliteStore_IsProvisionedAndUsable()
        {
            this.fixture.Store.SaveRoot(this.New(1, "Checking"));

            Assert.That(this.fixture.Store.LoadAccounts().Single().Name, Is.EqualTo("Checking"));
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(1));
            Assert.That(this.fixture.Store.Identity.SchemaVersion, Is.GreaterThan(0));
        }

        [Test]
        public void InMemorySqliteStore_InstancesAreIsolatedFromEachOther()
        {
            this.fixture.Store.SaveRoot(this.New(1, "Checking"));

            using (InMemorySqliteStore other = InMemorySqliteStore.Create())
            {
                Assert.That(other.Store.LoadAccounts(), Is.Empty);
            }
        }

        [Test]
        public void RecordingStore_RecordsWhatWasAskedOfTheDataLayer()
        {
            var recording = new RecordingStore(this.fixture.Store);

            recording.SaveRoot(this.New(1, "Checking"));
            Account deleted = this.New(2, "Doomed");
            recording.SaveRoot(deleted);
            deleted.OnDelete();
            recording.DeleteRoot(deleted);

            Assert.That(recording.Calls, Is.EqualTo(new[]
            {
                "SaveRoot(Account#1)", "SaveRoot(Account#2)", "DeleteRoot(Account#2)"
            }));
            Assert.That(recording.WriteCount, Is.EqualTo(3));
        }

        [Test]
        public void RecordingStore_OnARefusedCall_RecordsNoWrite()
        {
            // The assertion spec section 1.9.7 says is the one that actually matters: proving a
            // guard ran BEFORE any write, not merely that it threw.
            var recording = new RecordingStore(this.fixture.Store);
            Account a = this.New(1, "Checking");
            a.OnDelete();

            Assert.Throws<ArgumentException>(() => recording.SaveRoot(a));

            Assert.That(recording.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void FaultInjectingStore_ThrowsAConflictForANamedRootOnDemand()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);

            Account a = this.New(1, "Checking");

            Assert.Throws<ConcurrencyConflictException>(() => faulty.SaveRoot(a));
            Assert.That(this.fixture.Store.LoadAccounts(), Is.Empty);
        }

        [Test]
        public void FaultInjectingStore_ConflictsOnceThenLetsTheRetrySucceed()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);

            Account a = this.New(1, "Checking");
            Assert.Throws<ConcurrencyConflictException>(() => faulty.SaveRoot(a));

            Assert.DoesNotThrow(() => faulty.SaveRoot(a));
            Assert.That(this.fixture.Store.LoadAccounts(), Has.Count.EqualTo(1));
        }

        [Test]
        public void FaultInjectingStore_CanThrowAnArbitraryExceptionOnTheNthCall()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(2, new InvalidOperationException("boom"));

            faulty.SaveRoot(this.New(1, "One"));

            Assert.Throws<InvalidOperationException>(() => faulty.SaveRoot(this.New(2, "Two")));
        }

        [Test]
        public void Decorators_ForwardIdentityWithoutAlteringIt()
        {
            var recording = new RecordingStore(this.fixture.Store);
            var faulty = new FaultInjectingStore(recording);

            Assert.That(faulty.Identity, Is.EqualTo(this.fixture.Store.Identity));
        }
    }
}
