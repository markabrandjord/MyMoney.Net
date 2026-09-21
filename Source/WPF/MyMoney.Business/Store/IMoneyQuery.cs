using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>
    /// Read-model marker, no members. Every row type IMoneyQuery returns implements it, and no
    /// implementer implements IAggregateRoot - which is what makes store.SaveRoot(row) fail to
    /// COMPILE rather than fail at runtime. Spec section 2.7.2.
    /// </summary>
    public interface IProjection
    {
    }

    /// <summary>
    /// A read-only view of one account. Deliberately carries NO RowVersion: to write, you re-read
    /// the root through IMoneyStore.LoadAccounts, which hands you the authoritative version. The
    /// round trip is possible; it just has to be explicit, and the explicitness is the feature.
    /// Spec section 2.7.2 point 3.
    /// </summary>
    public sealed record AccountRow(
        long Id,
        string Name,
        AccountType Type,
        string Currency,
        decimal OpeningBalance,
        bool IsClosed) : IProjection;

    /// <summary>
    /// A closed, typed filter - NOT IQueryable. The SQL Server tier is stored-proc-only, so an
    /// expression-tree provider is literally impossible there, and an IQueryable that silently
    /// materializes everything and filters in memory is worse than no abstraction. Spec section 1.
    /// </summary>
    /// <param name="AccountIds">Empty means all accounts.</param>
    public sealed record AccountQuery(IReadOnlyList<long> AccountIds, bool IncludeClosed)
    {
        public static AccountQuery All { get; } = new AccountQuery(Array.Empty<long>(), true);
    }

    /// <summary>
    /// The application's read path for everything that does not intend to write. Filtering and
    /// subtotaling happen in SQL, not in memory - spec section 3.3 stage 1.
    ///
    /// Plan A declares only what Plan A implements. Aggregate(AggregateQuery) from spec section 1
    /// arrives with the slice that brings Transaction; declaring it now would put a member on this
    /// port that no engine implements, which is precisely the
    /// NotImplementedException-as-a-capability-signal that splitting IDatabase was meant to kill.
    /// </summary>
    public interface IMoneyQuery
    {
        IReadOnlyList<AccountRow> ListAccounts(AccountQuery query);

        /// <summary>
        /// The next unused Account id. Id allocation stays a caller concern (it already is -
        /// Accounts.AddAccount's NextAccount counter); with no ambient graph this is where a
        /// caller gets it. A lost race is real and is handled: the store turns a primary-key
        /// collision on insert into ConcurrencyConflictException, so section 1.6a's retry loop
        /// covers it. See Task 12.
        /// </summary>
        long NextAccountId();
    }
}
