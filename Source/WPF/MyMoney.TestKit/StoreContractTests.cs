using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace MyMoney.TestKit
{
    /// <summary>
    /// The shared, engine-agnostic contract suite. One derived fixture per engine; if the two
    /// disagree, this is where it shows. Spec sections 2.3 tier 2 and 6.4.
    ///
    /// It covers IMoneyQuery as well as IMoneyStore, from its first commit, because a query surface
    /// the suite does not cover is a surface every business-layer test asserts against on trust.
    /// </summary>
    public abstract class StoreContractTests
    {
        protected abstract IMoneyStore Store { get; }

        protected abstract IMoneyQuery Query { get; }

        protected abstract IMoneyStoreProvisioner Provisioner { get; }

        /// <summary>
        /// The store assembly's MaxKnownSchemaVersion. Kept in the store rather than read from the
        /// provisioner's catalog because the store assembly deliberately does not reference the
        /// provisioning one; this assertion is what stops the two silently drifting.
        /// </summary>
        protected abstract int ExpectedStoreMaxSchemaVersion { get; }

        protected Accounts Container { get; } = new Accounts((PersistentObject)null);

        protected Account NewAccount(string name) => new Account(this.Container)
        {
            Id = checked((int)this.Query.NextAccountId()),
            Name = name,
            Type = AccountType.Checking,
            Currency = "USD",
            OpeningBalance = 0m,
        };

        // ---- schema / identity ----

        [Test]
        public void TheStoreAndTheProvisionerAgreeOnTheSchemaVersion()
        {
            Assert.That(this.Store.Identity.SchemaVersion, Is.EqualTo(this.Provisioner.CurrentVersion()));
            Assert.That(this.Provisioner.LatestKnownVersion, Is.EqualTo(this.ExpectedStoreMaxSchemaVersion),
                "The store's MaxKnownSchemaVersion has drifted from the provisioner's step catalog. "
                + "They are separate constants because the two assemblies deliberately do not "
                + "reference each other; this assertion is what keeps them honest.");
        }

        [Test]
        public void AFreshlyProvisionedSchemaVerifiesClean()
        {
            Assert.That(this.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void TheSchemaOwnsNoInsteadOfTrigger()
        {
            // Spec section 2.7.3: INSTEAD OF triggers are demoted to "do not adopt now", kept as a
            // deferred single-candidate spike with a standing "never above IMoneyStore" constraint.
            // If the spike is ever taken, THIS is the test someone has to deliberately amend, which
            // is exactly the conversation that should happen at that moment. Correct under either
            // answer to the views question the spec flagged back to the owner.
            Assert.That(this.InsteadOfTriggerCount(), Is.EqualTo(0));
        }

        protected abstract int InsteadOfTriggerCount();

        // ---- write surface preconditions, per engine (spec 1.6c, Test Engineer note 2) ----

        [Test]
        public void SaveRoot_OnADeletedRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);
            long countBefore = this.Query.ListAccounts(AccountQuery.All).Count;

            a.Name = "Renamed";
            a.OnDelete();

            Assert.Throws<ArgumentException>(() => this.Store.SaveRoot(a));
            Assert.That(this.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(countBefore));
            Assert.That(this.Store.LoadAccounts().Single(x => x.Id == a.Id).Name, Is.EqualTo("Checking"));
        }

        [Test]
        public void DeleteRoot_OnAnInsertedRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Never Saved");

            Assert.Throws<ArgumentException>(() => this.Store.DeleteRoot(a));
            Assert.That(this.Query.ListAccounts(AccountQuery.All), Is.Empty);
        }

        [Test]
        public void DeleteRoot_OnAChangedRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);
            a.Name = "Renamed";

            Assert.Throws<ArgumentException>(() => this.Store.DeleteRoot(a));
            Assert.That(this.Store.LoadAccounts().Single().Name, Is.EqualTo("Checking"));
        }

        [Test]
        public void DeleteRoot_OnACleanRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);

            Assert.Throws<ArgumentException>(() => this.Store.DeleteRoot(a));
            Assert.That(this.Store.LoadAccounts(), Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveRoot_OnACleanRoot_Succeeds()
        {
            // The case a tighter precondition would have broken. Account has no owned children, so
            // here it is a clean no-op; the slice that brings Transaction extends this to "a root
            // at None with a dirty owned child SUCCEEDS and writes the child" - spec 1.6c.
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);

            Assert.DoesNotThrow(() => this.Store.SaveRoot(a));
        }

        [Test]
        public void AfterAConflict_TheSameCallIsStillAdmissible()
        {
            // What makes the business-layer retry loop legal at all - spec 1.6a consequence 2.
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);

            long goodVersion = a.RowVersion;
            a.Name = "Renamed";
            a.RowVersion = goodVersion + 99;

            Assert.Throws<ConcurrencyConflictException>(() => this.Store.SaveRoot(a));
            Assert.That(a.IsChanged, Is.True);

            a.RowVersion = goodVersion;
            Assert.DoesNotThrow(() => this.Store.SaveRoot(a));
        }

        // ---- round trip ----

        [Test]
        public void ARootSurvivesASaveLoadEditSaveCycle()
        {
            Account a = this.NewAccount("Checking");
            a.OpeningBalance = 1234.5678m;
            this.Store.SaveRoot(a);

            Account loaded = this.Store.LoadAccounts().Single();
            Assert.That(loaded.OpeningBalance, Is.EqualTo(1234.5678m));
            Assert.That(loaded.RowVersion, Is.EqualTo(1));

            loaded.OpeningBalance = -0.0001m;
            this.Store.SaveRoot(loaded);

            Assert.That(this.Store.LoadAccounts().Single().OpeningBalance, Is.EqualTo(-0.0001m));
            Assert.That(this.Store.LoadAccounts().Single().RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void DeleteRoot_OnANeverPersistedRoot_IsASilentNoOp()
        {
            // The plan's decision D-2, pinned as a per-engine contract so Plan B's SQL Server
            // implementation has to make the same call.
            Account a = this.NewAccount("Never Saved");
            a.OnDelete();

            Assert.DoesNotThrow(() => this.Store.DeleteRoot(a));
            Assert.That(this.Query.ListAccounts(AccountQuery.All), Is.Empty);
        }

        [Test]
        public void DeleteRoot_TwiceOnAPersistedRoot_ConflictsOnTheSecondCall()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);
            a.OnDelete();
            this.Store.DeleteRoot(a);

            Assert.Throws<ConcurrencyConflictException>(() => this.Store.DeleteRoot(a));
        }

        [Test]
        public void SaveRoot_OnAnIdThatAlreadyExists_ReportsAConcurrencyConflict()
        {
            // The deliberate extension of R-CRUD-4 to the insert race (Task 12). Pinned per engine
            // so Plan B must match it - spec section 6.8 rule 1.
            Account first = this.NewAccount("Checking");
            this.Store.SaveRoot(first);

            Account colliding = new Account(this.Container)
            {
                Id = first.Id, Name = "Collides", Type = AccountType.Cash, OpeningBalance = 0m
            };

            Assert.Throws<ConcurrencyConflictException>(() => this.Store.SaveRoot(colliding));
        }

        // ---- IMoneyQuery ----

        [Test]
        public void ListAccounts_ReturnsProjectionsThatCannotCarryAVersionBack()
        {
            Account a = this.NewAccount("Checking");
            a.OpeningBalance = 42.25m;
            this.Store.SaveRoot(a);

            AccountRow row = this.Query.ListAccounts(AccountQuery.All).Single();

            Assert.That(row.Id, Is.EqualTo(a.Id));
            Assert.That(row.Name, Is.EqualTo("Checking"));
            Assert.That(row.OpeningBalance, Is.EqualTo(42.25m));
            Assert.That(row.IsClosed, Is.False);
            Assert.That(row, Is.Not.InstanceOf<IAggregateRoot>());
        }

        [Test]
        public void ListAccounts_HonoursTheClosedFilter()
        {
            Account open = this.NewAccount("Open");
            this.Store.SaveRoot(open);
            Account closed = this.NewAccount("Closed");
            closed.Flags = AccountFlags.Closed;
            this.Store.SaveRoot(closed);

            Assert.That(this.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(2));
            Assert.That(
                this.Query.ListAccounts(new AccountQuery(Array.Empty<long>(), false)).Select(r => r.Name),
                Is.EqualTo(new[] { "Open" }));
        }

        [Test]
        public void ListAccounts_HonoursTheIdFilter()
        {
            Account one = this.NewAccount("One");
            this.Store.SaveRoot(one);
            Account two = this.NewAccount("Two");
            this.Store.SaveRoot(two);

            Assert.That(
                this.Query.ListAccounts(new AccountQuery(new[] { (long)two.Id }, true)).Select(r => r.Name),
                Is.EqualTo(new[] { "Two" }));
        }

        [Test]
        public void NextAccountId_AdvancesAsAccountsAreAdded()
        {
            long first = this.Query.NextAccountId();
            this.Store.SaveRoot(this.NewAccount("Checking"));

            Assert.That(this.Query.NextAccountId(), Is.GreaterThan(first));
        }
    }
}
