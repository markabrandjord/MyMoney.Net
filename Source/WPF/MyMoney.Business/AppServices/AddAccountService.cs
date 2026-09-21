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

        public Account AddAccount(string name, AccountType type, string currency)
        {
            ValidateName(name);

            foreach (AccountRow row in this.query.ListAccounts(AccountQuery.All))
            {
                if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DuplicateAccountNameException(name);
                }
            }

            Account account = this.BuildAccount(name, type, currency);
            this.store.SaveRoot(account);
            return account;
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
