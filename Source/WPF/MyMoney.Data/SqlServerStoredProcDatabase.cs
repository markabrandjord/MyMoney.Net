using System;
using System.Data;
using System.Data.SqlTypes;
using Microsoft.Data.SqlClient;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// A SqlServerDatabase variant that performs Payees CRUD exclusively
    /// through stored procedures, matching the grants given to the
    /// MyMoneyUser login (see Database/SqlScripts/Access/Payees_AccessProcs.sql).
    /// Other tables are not yet overridden -- see the "Known Limitation"
    /// section of the plan that introduced this class.
    /// </summary>
    public class SqlServerStoredProcDatabase : SqlServerDatabase
    {
        /// <summary>
        /// Sole reason this exists: tests need to point at an ad hoc dev
        /// connection string without going through DataEngineConfig/
        /// DataEngineCredentialStore. Production callers (Task 10) leave
        /// this null and rely on the inherited Server/DatabasePath/UserId/
        /// Password properties instead.
        /// </summary>
        public string ConnectionStringOverride { get; set; }

        protected override string GetConnectionString(bool includeDatabase)
        {
            if (!string.IsNullOrEmpty(this.ConnectionStringOverride))
            {
                return this.ConnectionStringOverride;
            }
            return base.GetConnectionString(includeDatabase);
        }

        /// <summary>
        /// Overrides the base SqlServerDatabase.Load(), which starts with
        /// LazyCreateTables() (DDL against every [TableMapping] table) and
        /// then reads every table via raw SQL (ReadOnlineAccounts,
        /// ReadCategories, ReadAccounts, ReadTransactions, etc.). MyMoneyUser
        /// only has EXECUTE grants on the four Payees_* access procedures
        /// (see Database/SqlScripts/Access/Payees_AccessProcs.sql) -- it has
        /// no direct table grants at all -- so LazyCreateTables() and every
        /// ReadXxx() other than ReadPayees() would fail with a SQL Server
        /// permissions error. This override reads only Payees, matching the
        /// "Payees-only vertical slice" this class exists to prove out (see
        /// the "Known Limitation" section of the plan that introduced it).
        /// Every other MyMoney collection is left empty, not because the
        /// data doesn't exist, but because reading it isn't wired up yet.
        /// </summary>
        public override MyMoney Load(IStatusService status)
        {
            MyMoney money = new MyMoney();
            money.BeginUpdate(this);
            try
            {
                this.ReadPayees(money.Payees, money);
                this.ReadAccounts(money.Accounts, money);
            }
            finally
            {
                money.FlushUpdates();
                money.EndUpdate();
                money.OnLoaded();
            }
            return money;
        }

        public override void ReadPayees(Payees payees, MyMoney money)
        {
            payees.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Payees_SelectAll", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    payees.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Payee p = payees.AddPayee(id);
                        p.Name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        p.OnUpdated();
                    }
                    payees.EndUpdate();
                }
            }
            payees.FireChangeEvent(payees, payees, null, ChangeType.Reloaded);
        }

        public override void UpdatePayees(Payees payees)
        {
            if (payees.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Payee p in payees)
                {
                    if (p.IsChanged)
                    {
                        ExecutePayeeProc(connection, "dbo.Payees_Update", p);
                    }
                    else if (p.IsInserted)
                    {
                        ExecutePayeeProc(connection, "dbo.Payees_Insert", p);
                    }
                    else if (p.IsDeleted)
                    {
                        using (var command = new SqlCommand("dbo.Payees_Delete", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                        {
                            command.Parameters.AddWithValue("@Id", p.Id);
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }

            foreach (Payee p in payees)
            {
                if (!p.IsDeleted)
                {
                    p.OnUpdated();
                }
            }
            payees.RemoveDeleted();
        }

        public override void ReadAccounts(Accounts accts, MyMoney money)
        {
            accts.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Accounts_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    accts.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Account a = accts.AddAccount(id);
                        a.AccountId = reader.IsDBNull(1) ? null : reader.GetString(1);
                        a.OfxAccountId = reader.IsDBNull(2) ? null : reader.GetString(2);
                        a.Name = reader.IsDBNull(3) ? null : reader.GetString(3);
                        a.Type = (AccountType)reader.GetInt32(4);
                        a.Description = reader.IsDBNull(5) ? null : reader.GetString(5);
                        if (!reader.IsDBNull(6))
                        {
                            a.OnlineAccount = money.OnlineAccounts.FindOnlineAccountAt(reader.GetInt32(6));
                        }
                        a.OpeningBalance = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7);
                        if (!reader.IsDBNull(8))
                        {
                            a.LastSync = reader.GetDateTime(8);
                        }
                        if (!reader.IsDBNull(9))
                        {
                            a.LastBalance = reader.GetDateTime(9);
                        }
                        if (!reader.IsDBNull(10))
                        {
                            a.SyncGuid = new SqlGuid(reader.GetGuid(10));
                        }
                        if (!reader.IsDBNull(11))
                        {
                            a.Flags = (AccountFlags)reader.GetInt32(11);
                        }
                        a.Currency = reader.IsDBNull(12) ? null : reader.GetString(12);
                        a.WebSite = reader.IsDBNull(13) ? null : reader.GetString(13);
                        if (!reader.IsDBNull(14))
                        {
                            a.ReconcileWarning = reader.GetInt32(14);
                        }
                        if (!reader.IsDBNull(15))
                        {
                            a.CategoryForPrincipal = money.Categories.FindCategoryById(reader.GetInt32(15));
                        }
                        if (!reader.IsDBNull(16))
                        {
                            a.CategoryForInterest = money.Categories.FindCategoryById(reader.GetInt32(16));
                        }
                        a.OnUpdated();
                    }
                    accts.EndUpdate();
                }
            }
            accts.FireChangeEvent(accts, accts, null, ChangeType.Reloaded);
        }

        public override void UpdateAccounts(Accounts accounts)
        {
            if (accounts.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Account a in accounts)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", a.Id), ("@AccountId", (object)a.AccountId ?? DBNull.Value),
                        ("@OfxAccountId", (object)a.OfxAccountId ?? DBNull.Value), ("@Name", (object)a.Name ?? DBNull.Value),
                        ("@Type", (int)a.Type), ("@Description", (object)a.Description ?? DBNull.Value),
                        ("@OnlineAccount", a.OnlineAccount != null ? a.OnlineAccount.Id : -1),
                        ("@OpeningBalance", a.OpeningBalance), ("@LastSync", SqlServerDatabase.DBDateTimeParam(a.LastSync)),
                        ("@LastBalance", SqlServerDatabase.DBDateTimeParam(a.LastBalance)), ("@SyncGuid", SqlServerDatabase.DBGuidParam(a.SyncGuid)),
                        ("@Flags", (int)a.Flags), ("@Currency", (object)a.Currency ?? DBNull.Value),
                        ("@WebSite", (object)a.WebSite ?? DBNull.Value), ("@ReconcileWarning", a.ReconcileWarning),
                        ("@CategoryIdForPrincipal", a.CategoryForPrincipal == null ? -1 : a.CategoryForPrincipal.Id),
                        ("@CategoryIdForInterest", a.CategoryForInterest == null ? -1 : a.CategoryForInterest.Id)
                    };

                    if (a.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.Accounts_Update", parameters);
                    }
                    else if (a.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.Accounts_Insert", parameters);
                    }
                    else if (a.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.Accounts_Delete", ("@Id", a.Id));
                    }
                }
            }

            foreach (Account a in accounts)
            {
                if (!a.IsDeleted)
                {
                    a.OnUpdated();
                }
            }
            accounts.RemoveDeleted();
        }

        /// <summary>
        /// Shared ADO.NET helper for every entity's stored-proc CRUD calls
        /// (see the design spec for issue #22). Keeps each entity's
        /// Read/Update override thin: build the parameter list, call this.
        /// </summary>
        private static void ExecuteProc(SqlConnection connection, string procName, params (string Name, object Value)[] parameters)
        {
            using (var command = new SqlCommand(procName, connection) { CommandType = CommandType.StoredProcedure })
            {
                foreach (var (name, value) in parameters)
                {
                    command.Parameters.AddWithValue(name, value ?? DBNull.Value);
                }
                command.ExecuteNonQuery();
            }
        }

        private static void ExecutePayeeProc(SqlConnection connection, string procName, Payee p)
        {
            ExecuteProc(connection, procName, ("@Id", p.Id), ("@Name", (object)p.Name ?? DBNull.Value));
        }
    }
}
