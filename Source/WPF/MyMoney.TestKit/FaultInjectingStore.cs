using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit
{
    /// <summary>
    /// The one, first-class, documented way a business-layer test deliberately provokes a
    /// ConcurrencyConflictException and asserts on the AppServices retry loop's behaviour - spec
    /// sections 1.6a and 2.4. It supersedes the old ad hoc pattern (save a root, then mutate its
    /// RowVersion to a stale value before saving again), which was a real mechanism but an
    /// incidental side effect of test setup rather than a named capability.
    ///
    /// Revision 6 gave it one more job (spec section 1.6c, Test Engineer note 3): proving the
    /// retry loop lets an ArgumentException from SaveRoot/DeleteRoot's preconditions STRAIGHT
    /// THROUGH rather than treating it as a conflict and retrying a call that can only fail
    /// identically forever. That is what Task 24 uses it for.
    ///
    /// A conflict registered with ThrowConflictFor fires ONCE and then clears, so a test can
    /// assert that the retry succeeds.
    /// </summary>
    public sealed class FaultInjectingStore : IMoneyStore
    {
        private readonly IMoneyStore inner;
        private readonly HashSet<long> conflictingRootIds = new HashSet<long>();
        private readonly Dictionary<int, Exception> scheduled = new Dictionary<int, Exception>();
        private int callNumber;

        public FaultInjectingStore(IMoneyStore inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public StoreIdentity Identity => this.inner.Identity;

        /// <summary>The next write touching this root id conflicts; the one after it does not.</summary>
        public void ThrowConflictFor(long rootId) => this.conflictingRootIds.Add(rootId);

        /// <summary>Throw <paramref name="exception"/> on the Nth write call (1-based).</summary>
        public void ThrowOnCall(int number, Exception exception) => this.scheduled[number] = exception;

        public void Clear()
        {
            this.conflictingRootIds.Clear();
            this.scheduled.Clear();
            this.callNumber = 0;
        }

        public void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.Intercept(new IAggregateRoot[] { root });
            this.inner.SaveRoot(root);
        }

        public void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.Intercept(new IAggregateRoot[] { root });
            this.inner.DeleteRoot(root);
        }

        public void SaveRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            this.Intercept(roots);
            this.inner.SaveRoots(roots);
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            this.Intercept(new IAggregateRoot[] { from, to });
            this.inner.SaveTransfer(from, to);
        }

        public IReadOnlyList<Account> LoadAccounts() => this.inner.LoadAccounts();

        public void Dispose() => this.inner.Dispose();

        private void Intercept(IReadOnlyList<IAggregateRoot> roots)
        {
            this.callNumber++;

            if (this.scheduled.TryGetValue(this.callNumber, out Exception scheduledException))
            {
                this.scheduled.Remove(this.callNumber);
                throw scheduledException;
            }

            foreach (IAggregateRoot root in roots)
            {
                if (this.conflictingRootIds.Remove(root.Id))
                {
                    throw new ConcurrencyConflictException(
                        (PersistentObject)root,
                        ((root as PersistentObject)?.RowVersion ?? 0) + 1,
                        (root as PersistentObject)?.RowVersion ?? 0);
                }
            }
        }
    }
}
