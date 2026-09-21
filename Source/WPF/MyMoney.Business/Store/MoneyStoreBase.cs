using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>
    /// The four public write methods, written ONCE, over one engine-supplied executor.
    ///
    /// The preconditions live here rather than per engine for the reason spec section 1.6c's
    /// Developer note 2 gives: if each engine carried its own copy, the engines could disagree
    /// about what SaveRoot admits, which is the exact class of divergence the contract suite
    /// exists to police.
    ///
    /// The four methods are deliberately NON-VIRTUAL, and WriteRoots is protected, so nothing
    /// public reaches the executor except through one of the four. InternalsVisibleTo is
    /// explicitly not the mechanism - spec section 6.2.
    ///
    /// The named crack, stated rather than papered over (spec section 1.6c): SaveRoots asserts
    /// nothing, so SaveRoots(new[] { root }) is a one-line unvalidated route to the same
    /// executor. Three things keep it honest - the plural name makes a single-element call
    /// visibly odd at review, StoreWriteSurface_IsExactlyTheFourNamedMethods pins the member
    /// set, and only ONE distinction is guarded so there is exactly one thing to dodge.
    /// </summary>
    public abstract class MoneyStoreBase : IMoneyStore
    {
        public abstract StoreIdentity Identity { get; }

        public abstract IReadOnlyList<Account> LoadAccounts();

        /// <summary>
        /// The one insert/update/delete dispatch. Implementations own the transaction, the
        /// version check and R-CRUD-2's post-commit deferral - all written exactly once per
        /// engine, never once per public method.
        /// </summary>
        protected abstract void WriteRoots(IReadOnlyList<IAggregateRoot> roots);

        public abstract void Dispose();

        public void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            // ArgumentException, deliberately NOT ConcurrencyConflictException: this is a caller
            // bug, unconditionally reproducible, and if the business layer's retry loop caught it
            // the loop would spin forever on a call that can only fail identically. Spec 1.6a.
            if (root.IsDeleted)
            {
                throw new ArgumentException(
                    $"SaveRoot cannot be used on a {typeof(TRoot).Name} that is marked deleted - " +
                    "call DeleteRoot instead. (A root can become deleted behind a caller's back: " +
                    "PersistentObject.OnDelete() is a guardless soft delete.)",
                    nameof(root));
            }

            this.WriteRoots(new IAggregateRoot[] { root });
        }

        public void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (!root.IsDeleted)
            {
                throw new ArgumentException(
                    $"DeleteRoot requires a {typeof(TRoot).Name} that is marked deleted " +
                    "(call OnDelete() first). Deletion is always something a caller deliberately " +
                    "asked for, so this precondition is exact.",
                    nameof(root));
            }

            this.WriteRoots(new IAggregateRoot[] { root });
        }

        public void SaveRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            if (roots == null)
            {
                throw new ArgumentNullException(nameof(roots));
            }

            if (roots.Count == 0)
            {
                return;
            }

            this.WriteRoots(roots);
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            if (from == null)
            {
                throw new ArgumentNullException(nameof(from));
            }

            if (to == null)
            {
                throw new ArgumentNullException(nameof(to));
            }

            this.WriteRoots(new IAggregateRoot[] { from, to });
        }
    }
}
