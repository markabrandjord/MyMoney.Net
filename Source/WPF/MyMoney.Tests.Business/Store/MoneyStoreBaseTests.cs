using System;
using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Business.Store
{
    /// <summary>
    /// Pins the preconditions spec section 1.6c states for the four-method write surface.
    /// These live once, in MoneyStoreBase, so the two engines cannot disagree about what
    /// SaveRoot admits - spec section 1.6c, Developer note 2. The per-engine half (that a
    /// refused call writes nothing) is Tier 2 and lands in Task 17's contract suite.
    /// </summary>
    [TestFixture]
    public class MoneyStoreBaseTests
    {
        /// <summary>Records what reached WriteRoots without touching a database.</summary>
        private sealed class SpyStore : MoneyStoreBase
        {
            public List<IReadOnlyList<IAggregateRoot>> Writes { get; } = new List<IReadOnlyList<IAggregateRoot>>();

            public override StoreIdentity Identity { get; } =
                new StoreIdentity("spy", DbFlavor.Sqlite, true, 0);

            public override IReadOnlyList<Account> LoadAccounts() => Array.Empty<Account>();

            protected override void WriteRoots(IReadOnlyList<IAggregateRoot> roots) => this.Writes.Add(roots);

            public override void Dispose()
            {
            }
        }

        private static Account NewAccount()
        {
            // new Account(container) starts at ChangeType.Inserted - see Money.cs's
            // "private ChangeType change = ChangeType.Inserted;".
            return new Account(new Accounts((PersistentObject)null)) { Id = 1, Name = "Checking" };
        }

        private static Account ChangedAccount()
        {
            Account a = NewAccount();
            a.OnUpdated();          // Inserted -> None
            a.OnChanged("Name");    // None -> Changed
            return a;
        }

        private static Account CleanAccount()
        {
            Account a = NewAccount();
            a.OnUpdated();          // Inserted -> None
            return a;
        }

        private static Account DeletedAccount()
        {
            Account a = NewAccount();
            a.OnDelete();
            return a;
        }

        [Test]
        public void SaveRoot_AdmitsInsertedChangedAndNone_AndCallsWriteRootsOncePerCall()
        {
            var store = new SpyStore();

            store.SaveRoot(NewAccount());
            store.SaveRoot(ChangedAccount());
            store.SaveRoot(CleanAccount());

            Assert.That(store.Writes, Has.Count.EqualTo(3));
            Assert.That(store.Writes[0], Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveRoot_RefusesADeletedRoot_WithArgumentExceptionAndNoWrite()
        {
            var store = new SpyStore();

            Assert.Throws<ArgumentException>(() => store.SaveRoot(DeletedAccount()));
            Assert.That(store.Writes, Is.Empty);
        }

        [Test]
        public void DeleteRoot_AdmitsOnlyADeletedRoot()
        {
            var store = new SpyStore();

            store.DeleteRoot(DeletedAccount());

            Assert.That(store.Writes, Has.Count.EqualTo(1));
        }

        [TestCase("inserted")]
        [TestCase("changed")]
        [TestCase("none")]
        public void DeleteRoot_RefusesANonDeletedRoot_WithArgumentExceptionAndNoWrite(string state)
        {
            var store = new SpyStore();
            Account a = state switch
            {
                "inserted" => NewAccount(),
                "changed" => ChangedAccount(),
                _ => CleanAccount()
            };

            Assert.Throws<ArgumentException>(() => store.DeleteRoot(a));
            Assert.That(store.Writes, Is.Empty);
        }

        [Test]
        public void SaveRoots_AcceptsAMixedStateCollectionAndAssertsNothing()
        {
            var store = new SpyStore();

            store.SaveRoots(new IAggregateRoot[] { NewAccount(), ChangedAccount(), DeletedAccount() });

            Assert.That(store.Writes, Has.Count.EqualTo(1));
            Assert.That(store.Writes[0], Has.Count.EqualTo(3));
        }

        [Test]
        public void SaveRoots_OnAnEmptyCollection_DoesNotReachTheExecutor()
        {
            var store = new SpyStore();

            store.SaveRoots(Array.Empty<IAggregateRoot>());

            Assert.That(store.Writes, Is.Empty);
        }

        [Test]
        public void SaveRoot_OnNull_ThrowsArgumentNullException()
        {
            var store = new SpyStore();

            Assert.Throws<ArgumentNullException>(() => store.SaveRoot<Account>(null));
        }
    }
}
