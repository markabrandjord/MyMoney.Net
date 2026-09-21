using System;
using System.Linq;
using Walkabout.Data;

namespace Walkabout.Business.AppServices
{
    /// <summary>
    /// Same shape as AddAccountService/EditAccountService. No duplicate-name concern; the
    /// retry loop exists purely because DeleteRoot is version-checked like every other write.
    /// </summary>
    public sealed class DeleteAccountService
    {
        private readonly IMoneyStore store;
        private readonly IMoneyQuery query;

        public DeleteAccountService(IMoneyStore store, IMoneyQuery query)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public const int MaxAttempts = 3;

        public void DeleteAccount(long id)
        {
            ConcurrencyConflictException lastConflict = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                Account account = this.store.LoadAccounts().FirstOrDefault(a => a.Id == id)
                    ?? throw new AccountNotFoundException(id);

                account.OnDelete();

                try
                {
                    this.store.DeleteRoot(account);
                    return;
                }
                catch (ConcurrencyConflictException conflict)
                {
                    lastConflict = conflict;
                }
            }

            throw new ConcurrencyRetryExhaustedException(MaxAttempts, lastConflict);
        }
    }
}
