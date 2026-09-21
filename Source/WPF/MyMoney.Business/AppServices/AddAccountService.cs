using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace Walkabout.Business.AppServices
{
    /// <summary>Raised when an account with this name already exists.</summary>
    public sealed class DuplicateAccountNameException : Exception
    {
        public DuplicateAccountNameException(string accountName)
            : base($"There is already an account called '{accountName}'.")
        {
            this.AccountName = accountName;
        }

        public string AccountName { get; }
    }

    /// <summary>
    /// Raised when a use case gave up after MaxAttempts version conflicts. Distinct from
    /// ConcurrencyConflictException so a caller can tell "this one write lost a race" from "we
    /// kept losing"; the last conflict is carried as InnerException.
    /// </summary>
    public sealed class ConcurrencyRetryExhaustedException : Exception
    {
        public ConcurrencyRetryExhaustedException(int attempts, Exception inner)
            : base($"Gave up after {attempts} attempts - another writer kept changing the same data.",
                   inner)
        {
            this.Attempts = attempts;
        }

        public int Attempts { get; }
    }

    /// <summary>
    /// The AppServices band in MyMoney.Business, alongside the existing DatabaseLifecycle, which
    /// is already exactly this shape and is the working precedent - spec section 3.1.
    ///
    /// It uses BOTH ports, which is the split working as designed: IMoneyQuery for the reads it
    /// does not intend to write from (the duplicate-name check and the id allocation, which come
    /// back as projections), IMoneyStore for the write. Spec section 2.7.2 and the plan's D-1.
    ///
    /// It talks to no UI at all - no dispatcher, no dialog, no callback port - which is slice 7's
    /// success criterion rather than an incidental property.
    ///
    /// The conflict-retry loop is Task 24.
    /// </summary>
    public sealed class AddAccountService
    {
        private readonly IMoneyStore store;
        private readonly IMoneyQuery query;

        public AddAccountService(IMoneyStore store, IMoneyQuery query)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        /// <summary>
        /// How many times a use case reapplies its intent before giving up. Three is a starting
        /// default: the product's stated concurrency goal is ONE human and possibly ONE agent
        /// (spec section 1.6a), so a third consecutive loss means something other than ordinary
        /// contention is happening and spinning longer would hide it.
        /// </summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// The business-layer retry loop spec section 1.6a puts here rather than in the data
        /// layer: the store cannot retry, because reapplying a change requires knowing what the
        /// caller's INTENT was, and intent is a business concept the store must not know about.
        ///
        /// This is NEW logic, not a relocation - no retry loop exists anywhere in the codebase
        /// today; today's behaviour is that the conflict propagates to the UI as a message box.
        ///
        /// It catches ConcurrencyConflictException AND NOTHING WIDER. SaveRoot/DeleteRoot's
        /// precondition failures are ArgumentException - caller bugs, unconditionally
        /// reproducible - and a loop that caught them would retry a call that can only ever fail
        /// the same way, turning an immediate crash into a spin (spec section 1.6a consequence 1).
        ///
        /// It re-runs the WHOLE use case body, not just the failed write, because the reason the
        /// first attempt failed is that its state was stale: the id and the duplicate-name check
        /// both have to be re-derived from current data.
        ///
        /// Retrying is legal at all only because a failed write leaves the root's change state
        /// untouched - R-CRUD-2's postCommitActions deferral, pinned per engine by the contract
        /// suite's AfterAConflict_TheSameCallIsStillAdmissible (spec section 1.6a consequence 2).
        /// </summary>
        public Account AddAccount(string name, AccountType type, string currency)
        {
            ValidateName(name);

            ConcurrencyConflictException lastConflict = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                foreach (AccountRow row in this.query.ListAccounts(AccountQuery.All))
                {
                    if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DuplicateAccountNameException(name);
                    }
                }

                Account account = this.BuildAccount(name, type, currency);

                try
                {
                    this.store.SaveRoot(account);
                    return account;
                }
                catch (ConcurrencyConflictException conflict)
                {
                    lastConflict = conflict;
                }
            }

            throw new ConcurrencyRetryExhaustedException(MaxAttempts, lastConflict);
        }

        /// <summary>
        /// A brand-new, Inserted root with a caller-allocated id. Id allocation stays a caller
        /// concern, exactly as it already is in Accounts.AddAccount's NextAccount counter; with no
        /// ambient graph, IMoneyQuery.NextAccountId is where that counter now lives. A lost race
        /// is handled rather than ignored - the store reports a primary-key collision as
        /// ConcurrencyConflictException, which Task 24's retry loop catches.
        /// </summary>
        private Account BuildAccount(string name, AccountType type, string currency)
        {
            // A detached container so every root has a non-null Parent - CLAUDE.md records that a
            // parentless PersistentObject is a real source of NullReferenceException in
            // business-layer code that walks it.
            var container = new Accounts((PersistentObject)null);

            return new Account(container)
            {
                Id = checked((int)this.query.NextAccountId()),
                Name = name,
                Type = type,
                Currency = currency,
                OpeningBalance = 0m,
            };
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An account needs a name.", nameof(name));
            }

            // The domain's own list, consulted rather than duplicated.
            if (name.IndexOfAny(Accounts.InvalidNameChars) >= 0)
            {
                throw new ArgumentException(
                    $"An account name cannot contain any of: {new string(Accounts.InvalidNameChars)}",
                    nameof(name));
            }
        }
    }
}
