using System;
using MyMoney.TestKit.Contracts;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Data.Sqlite.TestTier
{
    /// <summary>
    /// The ONE place an IMoneyStoreTestControl is obtained, which is where the TestDatabase guard
    /// goes (Task 21).
    ///
    /// Spec section 2.6.4's reasoning, which is worth having at the call site: ResetSchema reads as
    /// dangerous and gets respect; ClearTable(TableRef.Payees) reads as housekeeping. So the check
    /// belongs at acquisition - the factory refuses to hand back an instance at all - rather than
    /// repeated in twenty methods where the twenty-first will be forgotten.
    /// </summary>
    public static class SqliteTestControlFactory
    {
        public static IMoneyStoreTestControl Acquire(SqliteMoneyStoreProvisioner provisioner)
        {
            if (provisioner == null)
            {
                throw new ArgumentNullException(nameof(provisioner));
            }

            TestDatabaseGuard.Require(provisioner.Identity, "Test control (schema, data and row reset)");
            return new SqliteMoneyStoreTestControl(provisioner);
        }
    }
}
