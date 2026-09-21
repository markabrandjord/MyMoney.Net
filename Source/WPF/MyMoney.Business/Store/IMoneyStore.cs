using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>
    /// The identity of an OPEN store handle. Carried on the store rather than passed alongside
    /// it, so a guard reads the flag off the same object the writes go to - spec section 1.9.2.
    /// Every failure mode of IsTestDatabase resolves to false (a missing JSON field, a casing
    /// regression, an unparsed registry), i.e. the guarded capability refuses. There is no way
    /// for the flag to break open.
    /// </summary>
    /// <param name="DisplayName">The registry entry's key, for plain-language messages.</param>
    /// <param name="Engine">Which engine this handle talks to.</param>
    /// <param name="IsTestDatabase">DatabaseEntry.TestDatabase, as of open.</param>
    /// <param name="SchemaVersion">Schema_CurrentVersion() as of open - spec section 1.8.</param>
    public sealed record StoreIdentity(
        string DisplayName,
        DbFlavor Engine,
        bool IsTestDatabase,
        int SchemaVersion);

    /// <summary>
    /// Everyday read/write access to the books. No DDL, no destructive bulk operations, no
    /// whole-graph Load() and no Save(MyMoney) - spec section 1.
    ///
    /// The write surface is EXACTLY four members (spec section 1.6c), pinned by the Tier-0 test
    /// StoreWriteSurface_IsExactlyTheFourNamedMethods. SaveRoot never removes the root's own row;
    /// DeleteRoot is the only way to do that, so "what in this codebase can remove an account?"
    /// has an answer you can grep for.
    ///
    /// LoadAccounts returns AGGREGATE ROOTS carrying RowVersion, which is what makes a later
    /// version-checked write possible. Reads that do not intend to write go through IMoneyQuery
    /// and come back as projections with no RowVersion - spec section 2.7.2 point 3.
    /// </summary>
    public interface IMoneyStore : IDisposable
    {
        StoreIdentity Identity { get; }

        /// <summary>Insert-or-update. Throws ArgumentException on a root marked deleted.</summary>
        void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot;

        /// <summary>Remove. Throws ArgumentException on a root NOT marked deleted.</summary>
        void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot;

        /// <summary>Mixed-state by definition (reconciliation, merges); asserts nothing.</summary>
        void SaveRoots(IReadOnlyList<IAggregateRoot> roots);

        /// <summary>Exactly two peer Transactions that must co-commit.</summary>
        void SaveTransfer(Transaction from, Transaction to);

        IReadOnlyList<Account> LoadAccounts();
    }
}
