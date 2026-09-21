using System;
using System.Linq;
using Walkabout.Data;

namespace Walkabout.Business.AppServices
{
    /// <summary>Raised when the account an edit/delete targets no longer exists.</summary>
    public sealed class AccountNotFoundException : Exception
    {
        public AccountNotFoundException(long accountId)
            : base($"Account {accountId} no longer exists.")
        {
            this.AccountId = accountId;
        }

        public long AccountId { get; }
    }

    /// <summary>
    /// Same shape as AddAccountService: IMoneyQuery for the reads that don't intend to write
    /// (duplicate-name check), IMoneyStore for the write, a business-layer retry loop over
    /// ConcurrencyConflictException and nothing wider. Loads the current Account fresh via
    /// IMoneyStore.LoadAccounts() rather than IMoneyQuery, because a write needs the real
    /// aggregate root with its RowVersion - a projection has none.
    /// </summary>
    public sealed class EditAccountService
    {
        private readonly IMoneyStore store;
        private readonly IMoneyQuery query;

        public EditAccountService(IMoneyStore store, IMoneyQuery query)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public const int MaxAttempts = 3;

        public void EditAccount(long id, string name, AccountType type, string currency)
        {
            ValidateName(name);

            ConcurrencyConflictException lastConflict = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                foreach (AccountRow row in this.query.ListAccounts(AccountQuery.All))
                {
                    if (row.Id != id && string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DuplicateAccountNameException(name);
                    }
                }

                Account account = this.store.LoadAccounts().FirstOrDefault(a => a.Id == id)
                    ?? throw new AccountNotFoundException(id);

                account.Name = name;
                account.Type = type;
                account.Currency = currency;

                try
                {
                    this.store.SaveRoot(account);
                    return;
                }
                catch (ConcurrencyConflictException conflict)
                {
                    lastConflict = conflict;
                }
            }

            throw new ConcurrencyRetryExhaustedException(MaxAttempts, lastConflict);
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An account needs a name.", nameof(name));
            }

            if (name.IndexOfAny(Accounts.InvalidNameChars) >= 0)
            {
                throw new ArgumentException(
                    $"An account name cannot contain any of: {new string(Accounts.InvalidNameChars)}",
                    nameof(name));
            }
        }
    }
}
