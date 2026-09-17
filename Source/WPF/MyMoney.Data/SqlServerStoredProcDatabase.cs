using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using System.Linq;
using Microsoft.Data.SqlClient;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// A SqlServerDatabase variant that performs CRUD exclusively through
    /// stored procedures, matching the grants given to the MyMoneyUser
    /// login (see Database/SqlScripts/Access/*_AccessProcs.sql). Covers
    /// every [TableMapping] entity: Payees, Accounts, Categories,
    /// Currencies, Securities, StockSplits, Aliases, Transactions,
    /// Splits, and Investment (issue #22), plus OnlineAccounts,
    /// AccountAliases, TransactionExtras, RentBuildings, RentUnits, and
    /// LoanPayments (issue #23).
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

        public override void ReadOnlineAccounts(OnlineAccounts onlineAccounts, MyMoney money)
        {
            onlineAccounts.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.OnlineAccounts_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    onlineAccounts.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        OnlineAccount i = onlineAccounts.AddOnlineAccount(id);
                        i.Name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        i.Institution = reader.IsDBNull(2) ? null : reader.GetString(2);
                        i.Ofx = reader.IsDBNull(3) ? null : reader.GetString(3);
                        i.FID = reader.IsDBNull(4) ? null : reader.GetString(4);
                        i.UserId = reader.IsDBNull(5) ? null : reader.GetString(5).TrimEnd();
                        i.Password = reader.IsDBNull(6) ? null : reader.GetString(6);
                        i.BankId = reader.IsDBNull(7) ? null : reader.GetString(7);
                        i.BranchId = reader.IsDBNull(8) ? null : reader.GetString(8);
                        i.BrokerId = reader.IsDBNull(9) ? null : reader.GetString(9);
                        i.OfxVersion = reader.IsDBNull(10) ? null : reader.GetString(10).TrimEnd();
                        i.LogoUrl = reader.IsDBNull(11) ? null : reader.GetString(11);
                        i.AppId = reader.IsDBNull(12) ? null : reader.GetString(12).TrimEnd();
                        i.AppVersion = reader.IsDBNull(13) ? null : reader.GetString(13).TrimEnd();
                        i.ClientUid = reader.IsDBNull(14) ? null : reader.GetString(14).TrimEnd();
                        i.UserCred1 = reader.IsDBNull(15) ? null : reader.GetString(15);
                        i.UserCred2 = reader.IsDBNull(16) ? null : reader.GetString(16);
                        i.AuthToken = reader.IsDBNull(17) ? null : reader.GetString(17);
                        i.AccessKey = reader.IsDBNull(18) ? null : reader.GetString(18).TrimEnd();
                        i.UserKey = reader.IsDBNull(19) ? null : reader.GetString(19);
                        if (!reader.IsDBNull(20))
                        {
                            i.UserKeyExpireDate = reader.GetDateTime(20);
                        }
                        i.RowVersion = reader.GetInt64(21);
                        i.OnUpdated();
                    }
                    onlineAccounts.EndUpdate();
                }
            }
            onlineAccounts.FireChangeEvent(onlineAccounts, onlineAccounts, null, ChangeType.Reloaded);
        }

        public override void UpdateOnlineAccounts(OnlineAccounts accounts)
        {
            if (accounts.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (OnlineAccount i in accounts)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", i.Id), ("@Name", (object)i.Name ?? DBNull.Value), ("@Institution", (object)i.Institution ?? DBNull.Value),
                        ("@OFX", (object)i.Ofx ?? DBNull.Value), ("@FID", (object)i.FID ?? DBNull.Value),
                        ("@UserId", (object)i.UserId ?? DBNull.Value), ("@Password", (object)i.Password ?? DBNull.Value),
                        ("@BankId", (object)i.BankId ?? DBNull.Value), ("@BranchId", (object)i.BranchId ?? DBNull.Value),
                        ("@BrokerId", (object)i.BrokerId ?? DBNull.Value), ("@OfxVersion", (object)i.OfxVersion ?? DBNull.Value),
                        ("@LogoUrl", (object)i.LogoUrl ?? DBNull.Value), ("@AppId", (object)i.AppId ?? DBNull.Value),
                        ("@AppVersion", (object)i.AppVersion ?? DBNull.Value), ("@ClientUid", (object)i.ClientUid ?? DBNull.Value),
                        ("@UserCred1", (object)i.UserCred1 ?? DBNull.Value), ("@UserCred2", (object)i.UserCred2 ?? DBNull.Value),
                        ("@AuthToken", (object)i.AuthToken ?? DBNull.Value), ("@AccessKey", (object)i.AccessKey ?? DBNull.Value),
                        ("@UserKey", (object)i.UserKey ?? DBNull.Value),
                        ("@UserKeyExpireDate", SqlServerDatabase.DBNullableDateTimeParam(i.UserKeyExpireDate))
                    };

                    if (i.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.OnlineAccounts_Update", parameters);
                    }
                    else if (i.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.OnlineAccounts_Insert", parameters);
                    }
                    else if (i.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.OnlineAccounts_Delete", ("@Id", i.Id));
                    }
                }
            }

            foreach (OnlineAccount i in accounts)
            {
                if (!i.IsDeleted)
                {
                    i.OnUpdated();
                }
            }
            accounts.RemoveDeleted();
        }

        public override void ReadAccountAliases(AccountAliases accountAliases, MyMoney money)
        {
            accountAliases.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.AccountAliases_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    accountAliases.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        AccountAlias a = accountAliases.AddAlias(id);
                        a.Pattern = reader.IsDBNull(1) ? null : reader.GetString(1);
                        a.AccountId = reader.IsDBNull(2) ? null : reader.GetString(2).TrimEnd();
                        if (!reader.IsDBNull(3))
                        {
                            a.AliasType = (AliasType)reader.GetInt32(3);
                        }
                        a.OnUpdated();
                    }
                    accountAliases.EndUpdate();
                }
            }
            accountAliases.FireChangeEvent(accountAliases, accountAliases, null, ChangeType.Reloaded);
        }

        public override void UpdateAccountAliases(AccountAliases accountAliases)
        {
            if (accountAliases.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (AccountAlias a in accountAliases)
                {
                    if (a.IsChanged || a.IsInserted)
                    {
                        (string Name, object Value)[] parameters =
                        {
                            ("@Id", a.Id), ("@Pattern", (object)a.Pattern ?? DBNull.Value),
                            ("@AccountId", (object)a.AccountId ?? DBNull.Value), ("@Flags", (int)a.AliasType)
                        };

                        if (a.IsChanged)
                        {
                            ExecuteProc(connection, "dbo.AccountAliases_Update", parameters);
                        }
                        else
                        {
                            ExecuteProc(connection, "dbo.AccountAliases_Insert", parameters);
                        }
                    }
                    else if (a.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.AccountAliases_Delete", ("@Id", a.Id));
                    }
                }
            }

            foreach (AccountAlias a in accountAliases)
            {
                a.OnUpdated();
            }
            accountAliases.RemoveDeleted();
        }

        public override void ReadTransactionExtras(TransactionExtras extras, MyMoney money)
        {
            extras.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.TransactionExtras_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    extras.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        TransactionExtra a = extras.AddExtra(id);
                        a.Transaction = reader.GetInt64(1);
                        a.TaxYear = reader.GetInt32(2);
                        if (!reader.IsDBNull(3))
                        {
                            DateTime taxDate = reader.GetDateTime(3);
                            if (taxDate.Year > 1)
                            {
                                a.TaxDate = taxDate;
                            }
                        }
                        a.OnUpdated();
                    }
                    extras.EndUpdate();
                }
            }
            extras.FireChangeEvent(extras, extras, null, ChangeType.Reloaded);
        }

        public override void UpdateTransactionExtras(TransactionExtras extras)
        {
            if (extras.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (TransactionExtra e in extras)
                {
                    if (e.IsChanged || e.IsInserted)
                    {
                        (string Name, object Value)[] parameters =
                        {
                            ("@Id", e.Id), ("@Transaction", e.Transaction), ("@TaxYear", e.TaxYear),
                            ("@TaxDate", SqlServerDatabase.DBNullableDateTimeParam(e.TaxDate))
                        };

                        if (e.IsChanged)
                        {
                            ExecuteProc(connection, "dbo.TransactionExtras_Update", parameters);
                        }
                        else
                        {
                            ExecuteProc(connection, "dbo.TransactionExtras_Insert", parameters);
                        }
                    }
                    else if (e.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.TransactionExtras_Delete", ("@Id", e.Id));
                    }
                }
            }

            foreach (TransactionExtra e in extras)
            {
                e.OnUpdated();
            }
            extras.RemoveDeleted();
        }

        public override void ReadLoanPayments(LoanPayments collection, MyMoney money)
        {
            collection.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.LoanPayments_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    collection.BeginUpdate(false);
                    while (reader.Read())
                    {
                        LoanPayment x = new LoanPayment(collection);
                        x.BatchMode = true;
                        x.Id = reader.GetInt32(0);
                        x.AccountId = reader.GetInt32(1);
                        if (!reader.IsDBNull(2))
                        {
                            x.Date = reader.GetDateTime(2);
                        }
                        x.Principal = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                        x.Interest = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
                        x.Memo = reader.IsDBNull(5) ? null : reader.GetString(5);
                        x.BatchMode = false;
                        collection.AddLoan(x);
                        x.OnUpdated();
                    }
                    collection.EndUpdate();
                }
            }
            collection.FireChangeEvent(collection, collection, null, ChangeType.Reloaded);

            foreach (Account a in money.Accounts)
            {
                if (a.Type == AccountType.Loan)
                {
                    money.GetOrCreateLoanAccount(a);
                }
            }
        }

        public override void UpdateLoanPayments(LoanPayments loans)
        {
            if (loans.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (LoanPayment i in loans)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", i.Id), ("@AccountId", i.AccountId), ("@Date", SqlServerDatabase.DBDateTimeParam(i.Date)),
                        ("@Principal", i.Principal), ("@Interest", i.Interest), ("@Memo", (object)i.Memo ?? DBNull.Value)
                    };

                    if (i.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.LoanPayments_Update", parameters);
                    }
                    else if (i.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.LoanPayments_Insert", parameters);
                    }
                    else if (i.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.LoanPayments_Delete", ("@Id", i.Id));
                    }
                }
            }

            foreach (LoanPayment i in loans)
            {
                i.OnUpdated();
            }
            loans.RemoveDeleted();
        }

        public override void ReadRentUnits(RentUnits collection, MyMoney money)
        {
            collection.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.RentUnits_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    collection.BeginUpdate(false);
                    while (reader.Read())
                    {
                        RentUnit r = new RentUnit(collection);
                        r.Id = reader.GetInt32(0);
                        r.Building = reader.GetInt32(1);
                        r.Name = reader.IsDBNull(2) ? null : reader.GetString(2);
                        r.Renter = reader.IsDBNull(3) ? null : reader.GetString(3);
                        r.Note = reader.IsDBNull(4) ? null : reader.GetString(4);
                        collection.AddRentUnit(r);
                        r.OnUpdated();
                    }
                    collection.EndUpdate();
                }
            }
        }

        public override void UpdateRentUnits(RentUnits units)
        {
            if (units.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (RentUnit x in units)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", x.Id), ("@Building", x.Building), ("@Name", (object)x.Name ?? DBNull.Value),
                        ("@Renter", (object)x.Renter ?? DBNull.Value), ("@Note", (object)x.Note ?? DBNull.Value)
                    };

                    if (x.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.RentUnits_Update", parameters);
                    }
                    else if (x.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.RentUnits_Insert", parameters);
                    }
                    else if (x.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.RentUnits_Delete", ("@Id", x.Id), ("@Building", x.Building));
                    }
                }
            }

            foreach (RentUnit x in units)
            {
                x.OnUpdated();
            }
            units.RemoveDeleted();
        }

        public override void ReadRentBuildings(RentBuildings collection, MyMoney money)
        {
            this.ReadRentUnits(collection.Units, money);

            collection.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.RentBuildings_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    collection.BeginUpdate(false);
                    while (reader.Read())
                    {
                        RentBuilding r = new RentBuilding(collection);
                        r.Id = reader.GetInt32(0);
                        r.Name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        r.Address = reader.IsDBNull(2) ? null : reader.GetString(2);
                        if (!reader.IsDBNull(3))
                        {
                            r.PurchasedDate = reader.GetDateTime(3);
                        }
                        r.PurchasedPrice = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
                        r.LandValue = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5);
                        r.EstimatedValue = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6);
                        r.CategoryForIncome = reader.IsDBNull(7) ? -1 : reader.GetInt32(7);
                        r.CategoryForTaxes = reader.IsDBNull(8) ? -1 : reader.GetInt32(8);
                        r.CategoryForInterest = reader.IsDBNull(9) ? -1 : reader.GetInt32(9);
                        r.CategoryForRepairs = reader.IsDBNull(10) ? -1 : reader.GetInt32(10);
                        r.CategoryForMaintenance = reader.IsDBNull(11) ? -1 : reader.GetInt32(11);
                        r.CategoryForManagement = reader.IsDBNull(12) ? -1 : reader.GetInt32(12);
                        r.OwnershipName1 = reader.IsDBNull(13) ? null : reader.GetString(13);
                        r.OwnershipName2 = reader.IsDBNull(14) ? null : reader.GetString(14);
                        r.OwnershipPercentage1 = reader.IsDBNull(15) ? 0 : reader.GetDecimal(15);
                        r.OwnershipPercentage2 = reader.IsDBNull(16) ? 0 : reader.GetDecimal(16);
                        r.Note = reader.IsDBNull(17) ? null : reader.GetString(17);

                        foreach (var unit in money.Buildings.Units.GetList().Where(x => x.Building == r.Id).OrderBy(x => x.Id))
                        {
                            r.Units.Add(unit);
                        }

                        collection.AddRentBuilding(r);
                        r.OnUpdated();
                    }
                    collection.EndUpdate();
                }
            }
        }

        public override void UpdateRentBuildings(RentBuildings buildings)
        {
            if (buildings.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (RentBuilding p in buildings)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", p.Id), ("@Name", (object)p.Name ?? DBNull.Value), ("@Address", (object)p.Address ?? DBNull.Value),
                        ("@PurchasedDate", SqlServerDatabase.DBDateTimeParam(p.PurchasedDate)), ("@PurchasedPrice", p.PurchasedPrice),
                        ("@LandValue", p.LandValue), ("@EstimatedValue", p.EstimatedValue),
                        ("@CategoryForIncome", p.CategoryForIncome), ("@CategoryForTaxes", p.CategoryForTaxes),
                        ("@CategoryForInterest", p.CategoryForInterest), ("@CategoryForRepairs", p.CategoryForRepairs),
                        ("@CategoryForMaintenance", p.CategoryForMaintenance), ("@CategoryForManagement", p.CategoryForManagement),
                        ("@OwnershipName1", (object)p.OwnershipName1 ?? DBNull.Value), ("@OwnershipName2", (object)p.OwnershipName2 ?? DBNull.Value),
                        ("@OwnershipPercentage1", p.OwnershipPercentage1), ("@OwnershipPercentage2", p.OwnershipPercentage2),
                        ("@Note", (object)p.Note ?? DBNull.Value)
                    };

                    if (p.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.RentBuildings_Update", parameters);
                    }
                    else if (p.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.RentBuildings_Insert", parameters);
                    }
                    else if (p.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.RentBuildings_Delete", ("@Id", p.Id));
                    }
                }
            }

            foreach (RentBuilding p in buildings)
            {
                p.OnUpdated();
            }
            buildings.RemoveDeleted();
        }

        /// <summary>
        /// Overrides the base SqlServerDatabase.Load(), which starts with
        /// LazyCreateTables() (DDL against every [TableMapping] table) and
        /// then reads every table via raw SQL. MyMoneyUser has no direct
        /// table grants at all -- only EXECUTE on the *_AccessProcs.sql
        /// stored procedures -- so LazyCreateTables() and the base class's
        /// generic ReadXxx() methods would fail with a SQL Server
        /// permissions error. This override reads every [TableMapping]
        /// entity via its dedicated stored procedures, in FK-safe
        /// dependency order: OnlineAccounts before Accounts (Account.
        /// OnlineAccount is resolved by lookup), Accounts before
        /// LoanPayments (which matches loan accounts by iterating
        /// money.Accounts), and RentUnits before RentBuildings (handled
        /// internally by ReadRentBuildings, matching issue #22's
        /// Splits/Investment-inside-ReadTransactions pattern).
        /// </summary>
        public override MyMoney Load(IStatusService status)
        {
            MyMoney money = new MyMoney();
            money.BeginUpdate(this);
            try
            {
                this.ReadOnlineAccounts(money.OnlineAccounts, money);
                this.ReadPayees(money.Payees, money);
                this.ReadAliases(money.Aliases, money);
                this.ReadAccountAliases(money.AccountAliases, money);
                this.ReadCategories(money.Categories, money);
                this.ReadAccounts(money.Accounts, money);
                this.ReadCurrencies(money.Currencies, money);
                this.ReadSecurities(money.Securities, money);
                this.ReadStockSplits(money.StockSplits, money);
                this.ReadTransactions(money.Transactions, money);
                this.ReadTransactionExtras(money.TransactionExtras, money);
                this.ReadLoanPayments(money.LoanPayments, money);
                this.ReadRentBuildings(money.Buildings, money);
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
                        a.AccountId = reader.IsDBNull(1) ? null : reader.GetString(1).TrimEnd();
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
                        a.Currency = reader.IsDBNull(12) ? null : reader.GetString(12).TrimEnd();
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
                        ("@OnlineAccount", a.OnlineAccount != null ? (object)a.OnlineAccount.Id : DBNull.Value),
                        ("@OpeningBalance", a.OpeningBalance), ("@LastSync", SqlServerDatabase.DBDateTimeParam(a.LastSync)),
                        ("@LastBalance", SqlServerDatabase.DBDateTimeParam(a.LastBalance)), ("@SyncGuid", SqlServerDatabase.DBGuidParam(a.SyncGuid)),
                        ("@Flags", (int)a.Flags), ("@Currency", (object)a.Currency ?? DBNull.Value),
                        ("@WebSite", (object)a.WebSite ?? DBNull.Value), ("@ReconcileWarning", a.ReconcileWarning),
                        ("@CategoryIdForPrincipal", a.CategoryForPrincipal == null ? (object)DBNull.Value : a.CategoryForPrincipal.Id),
                        ("@CategoryIdForInterest", a.CategoryForInterest == null ? (object)DBNull.Value : a.CategoryForInterest.Id)
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

        public override void ReadCategories(Categories categories, MyMoney money)
        {
            categories.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Categories_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    categories.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Category c = new Category(categories);
                        c.Id = id;
                        categories.AddCategory(c);
                        c.Name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        c.Description = reader.IsDBNull(2) ? null : reader.GetString(2);
                        if (!reader.IsDBNull(3))
                        {
                            c.Type = (CategoryType)reader.GetInt32(3);
                        }
                        if (!reader.IsDBNull(4))
                        {
                            c.ParentId = reader.GetInt32(4);
                        }
                        if (!reader.IsDBNull(5))
                        {
                            c.Budget = reader.GetDecimal(5);
                        }
                        if (!reader.IsDBNull(6))
                        {
                            c.Frequency = (CalendarRange)reader.GetInt32(6);
                        }
                        if (!reader.IsDBNull(7))
                        {
                            c.Balance = reader.GetDecimal(7);
                        }
                        if (!reader.IsDBNull(8))
                        {
                            c.Color = reader.GetString(8).TrimEnd();
                        }
                        if (!reader.IsDBNull(9))
                        {
                            c.TaxRefNum = reader.GetInt32(9);
                        }
                        c.RowVersion = reader.GetInt64(10);
                        c.OnUpdated();
                        if (c.Type == CategoryType.Reserved)
                        {
                            c.Type = CategoryType.Expense;
                        }
                    }
                    categories.EndUpdate();
                }
            }
            categories.FireChangeEvent(categories, categories, null, ChangeType.Reloaded);
        }

        public override void UpdateCategories(Categories categories)
        {
            if (categories.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Category c in categories)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", c.Id), ("@Name", (object)c.Name ?? DBNull.Value),
                        ("@Description", (object)c.Description ?? DBNull.Value), ("@Type", (int)c.Type),
                        ("@ParentId", c.ParentCategory != null ? c.ParentCategory.Id : -1), ("@Budget", c.Budget),
                        ("@Frequency", (int)c.Frequency), ("@Balance", c.Balance),
                        ("@Color", (object)c.Color ?? DBNull.Value), ("@TaxRefNum", c.TaxRefNum)
                    };

                    if (c.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.Categories_Update", parameters);
                    }
                    else if (c.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.Categories_Insert", parameters);
                    }
                    else if (c.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.Categories_Delete", ("@Id", c.Id));
                    }
                }
            }

            foreach (Category c in categories)
            {
                c.OnUpdated();
            }
            categories.RemoveDeleted();
        }

        public override void ReadCurrencies(Currencies currencies, MyMoney money)
        {
            currencies.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Currencies_SelectAll", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    currencies.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Currency s = currencies.AddCurrency(id);
                        s.Symbol = reader.IsDBNull(1) ? null : reader.GetString(1).TrimEnd();
                        s.Name = reader.IsDBNull(2) ? null : reader.GetString(2);
                        if (!reader.IsDBNull(3))
                        {
                            s.Ratio = reader.GetDecimal(3);
                        }
                        if (!reader.IsDBNull(4))
                        {
                            s.LastRatio = reader.GetDecimal(4);
                        }
                        s.CultureCode = reader.IsDBNull(5) ? "en-US" : reader.GetString(5);
                        s.RowVersion = reader.GetInt64(6);
                        s.OnUpdated();
                    }
                    currencies.EndUpdate();
                }
            }
            currencies.FireChangeEvent(currencies, currencies, null, ChangeType.Reloaded);
        }

        public override void UpdateCurrencies(Currencies currencies)
        {
            if (currencies.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Currency s in currencies)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", s.Id), ("@Symbol", (object)s.Symbol ?? DBNull.Value), ("@Name", (object)s.Name ?? DBNull.Value),
                        ("@Ratio", s.Ratio), ("@LastRatio", s.LastRatio), ("@CultureCode", (object)s.CultureCode ?? DBNull.Value)
                    };

                    if (s.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.Currencies_Update", parameters);
                    }
                    else if (s.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.Currencies_Insert", parameters);
                    }
                    else if (s.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.Currencies_Delete", ("@Id", s.Id));
                    }
                }
            }

            foreach (Currency s in currencies)
            {
                s.OnUpdated();
            }
            currencies.RemoveDeleted();
        }

        public override void ReadSecurities(Securities securities, MyMoney money)
        {
            securities.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Securities_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    securities.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Security s = securities.AddSecurity(id);
                        s.Name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        s.Symbol = reader.IsDBNull(2) ? null : reader.GetString(2).TrimEnd();
                        s.Price = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                        if (!reader.IsDBNull(4))
                        {
                            s.LastPrice = reader.GetDecimal(4);
                        }
                        s.CuspId = reader.IsDBNull(5) ? null : reader.GetString(5).TrimEnd();
                        if (!reader.IsDBNull(6))
                        {
                            s.SecurityType = (SecurityType)reader.GetInt32(6);
                        }
                        if (!reader.IsDBNull(7))
                        {
                            s.Taxable = (YesNo)reader.GetByte(7);
                        }
                        if (!reader.IsDBNull(8))
                        {
                            s.PriceDate = reader.GetDateTime(8);
                        }
                        s.OnUpdated();
                    }
                    securities.EndUpdate();
                }
            }
            securities.FireChangeEvent(securities, securities, null, ChangeType.Reloaded);
        }

        public override void UpdateSecurities(Securities securities)
        {
            if (securities.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Security s in securities)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", s.Id), ("@Name", (object)s.Name ?? DBNull.Value), ("@Symbol", (object)s.Symbol ?? DBNull.Value),
                        ("@Price", s.Price), ("@LastPrice", s.LastPrice), ("@CuspId", (object)s.CuspId ?? DBNull.Value),
                        ("@SecurityType", (int)s.SecurityType), ("@Taxable", (byte)s.Taxable),
                        ("@PriceDate", SqlServerDatabase.DBDateTimeParam(s.PriceDate))
                    };

                    if (s.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.Securities_Update", parameters);
                    }
                    else if (s.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.Securities_Insert", parameters);
                    }
                    else if (s.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.Securities_Delete", ("@Id", s.Id));
                    }
                }
            }

            foreach (Security s in securities)
            {
                s.OnUpdated();
            }
            securities.RemoveDeleted();
        }

        public new void ReadStockSplits(StockSplits splits, MyMoney money)
        {
            splits.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.StockSplits_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    splits.BeginUpdate(false);
                    while (reader.Read())
                    {
                        long id = reader.GetInt64(0);
                        StockSplit s = splits.AddStockSplit(id);
                        if (!reader.IsDBNull(1))
                        {
                            s.Date = reader.GetDateTime(1);
                        }
                        s.Security = reader.IsDBNull(2) ? null : money.Securities.FindSecurityAt(reader.GetInt32(2));
                        s.Numerator = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                        s.Denominator = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
                        s.OnUpdated();
                    }
                    splits.EndUpdate();
                }
            }
            splits.FireChangeEvent(splits, splits, null, ChangeType.Reloaded);
        }

        public override void UpdateStockSplits(StockSplits stockSplits)
        {
            if (stockSplits.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (StockSplit s in stockSplits)
                {
                    if (s.IsChanged || s.IsInserted)
                    {
                        if (s.Date == DateTime.MinValue)
                        {
                            continue;
                        }

                        (string Name, object Value)[] parameters =
                        {
                            ("@Id", s.Id), ("@Date", SqlServerDatabase.DBDateTimeParam(s.Date)), ("@Security", s.Security == null ? (object)DBNull.Value : s.Security.Id),
                            ("@Numerator", s.Numerator), ("@Denominator", s.Denominator)
                        };

                        if (s.IsChanged)
                        {
                            ExecuteProc(connection, "dbo.StockSplits_Update", parameters);
                        }
                        else
                        {
                            ExecuteProc(connection, "dbo.StockSplits_Insert", parameters);
                        }
                    }
                    else if (s.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.StockSplits_Delete", ("@Id", s.Id));
                    }
                }
            }

            foreach (StockSplit s in stockSplits)
            {
                s.OnUpdated();
            }
            stockSplits.RemoveDeleted();
        }

        public override void ReadAliases(Aliases aliases, MyMoney money)
        {
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Aliases_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    aliases.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Alias a = aliases.AddAlias(id);
                        a.Pattern = reader.IsDBNull(1) ? null : reader.GetString(1);
                        int payeeId = reader.GetInt32(2);
                        a.Payee = money.Payees.FindPayeeAt(payeeId);
                        if (!reader.IsDBNull(3))
                        {
                            a.AliasType = (AliasType)reader.GetInt32(3);
                        }
                        a.OnUpdated();
                    }
                    aliases.EndUpdate();
                }
            }
            aliases.FireChangeEvent(aliases, aliases, null, ChangeType.Reloaded);
        }

        public override void UpdateAliases(Aliases aliases)
        {
            if (aliases.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Alias a in aliases)
                {
                    if (a.IsChanged || a.IsInserted)
                    {
                        (string Name, object Value)[] parameters =
                        {
                            ("@Id", a.Id), ("@Pattern", (object)a.Pattern ?? DBNull.Value), ("@Payee", a.Payee.Id),
                            ("@Flags", (int)a.AliasType)
                        };

                        if (a.IsChanged)
                        {
                            ExecuteProc(connection, "dbo.Aliases_Update", parameters);
                        }
                        else
                        {
                            ExecuteProc(connection, "dbo.Aliases_Insert", parameters);
                        }
                    }
                    else if (a.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.Aliases_Delete", ("@Id", a.Id));
                    }
                }
            }

            foreach (Alias a in aliases)
            {
                a.OnUpdated();
            }
            aliases.RemoveDeleted();
        }

        public override ArrayList ReadTransactions(Transactions transactions, MyMoney money)
        {
            transactions.Clear();
            ArrayList errors = new ArrayList();

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Transactions_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    transactions.BeginUpdate(false);
                    while (reader.Read())
                    {
                        long id = reader.GetInt64(0);
                        Transaction t = transactions.AddTransaction(id);
                        t.BatchMode = true;
                        t.Number = reader.IsDBNull(1) ? null : reader.GetString(1).TrimEnd();
                        t.Account = money.Accounts.FindAccountAt(reader.GetInt32(4));
                        if (!reader.IsDBNull(2))
                        {
                            t.Date = reader.GetDateTime(2);
                        }
                        t.Amount = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                        t.Status = (TransactionStatus)reader.GetInt32(5);
                        t.Memo = reader.IsDBNull(6) ? null : reader.GetString(6);
                        if (!reader.IsDBNull(7))
                        {
                            t.Payee = money.Payees.FindPayeeAt(reader.GetInt32(7));
                        }
                        if (!reader.IsDBNull(8))
                        {
                            t.Category = money.Categories.FindCategoryById(reader.GetInt32(8));
                        }
                        t.FITID = reader.IsDBNull(9) ? null : reader.GetString(9).TrimEnd();
                        if (!reader.IsDBNull(10))
                        {
                            t.SalesTax = reader.GetDecimal(10);
                        }
                        if (!reader.IsDBNull(11))
                        {
                            t.Flags = (TransactionFlags)reader.GetInt32(11);
                        }
                        if (!reader.IsDBNull(12))
                        {
                            t.ReconciledDate = reader.GetDateTime(12);
                        }
                        if (!reader.IsDBNull(13))
                        {
                            t.BudgetBalanceDate = reader.GetDateTime(13);
                        }
                        if (!reader.IsDBNull(14))
                        {
                            t.MergeDate = reader.GetDateTime(14);
                        }
                        if (!reader.IsDBNull(15))
                        {
                            t.OriginalPayee = reader.GetString(15);
                        }
                        t.BatchMode = false;
                        t.OnUpdated();
                    }
                    transactions.EndUpdate();
                }

                using (var command = new SqlCommand("dbo.Splits_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        long transactionId = reader.GetInt64(1);
                        Transaction t = transactions.FindTransactionById(transactionId);
                        if (t == null)
                        {
                            continue;
                        }

                        if (!t.IsSplit)
                        {
                            t.Splits = new Splits(t, t);
                        }
                        Split s = t.Splits.AddSplit(id);
                        s.BatchMode = true;
                        t.Splits.BeginUpdate(false);
                        s.Amount = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                        if (!reader.IsDBNull(3))
                        {
                            s.Category = money.Categories.FindCategoryById(reader.GetInt32(3));
                        }
                        s.Memo = reader.IsDBNull(4) ? null : reader.GetString(4);
                        if (!reader.IsDBNull(5))
                        {
                            long tid = reader.GetInt64(5);
                            if (tid != -1)
                            {
                                Transaction u = transactions.FindTransactionById(tid);
                                if (u == null)
                                {
                                    errors.Add(new DataError(transactionId, id, "Other side of split transfer not found"));
                                }
                                else
                                {
                                    if (u.Transfer != null && (u.Transfer.Transaction != t || u.Transfer.Split != s))
                                    {
                                        errors.Add(new DataError(transactionId, id, "Duplicate transfer found"));
                                    }
                                    s.Transfer = new Transfer(tid, t, s, u);
                                }
                            }
                        }
                        if (!reader.IsDBNull(6))
                        {
                            s.Payee = money.Payees.FindPayeeAt(reader.GetInt32(6));
                        }
                        if (!reader.IsDBNull(7))
                        {
                            s.Flags = (SplitFlags)reader.GetInt32(7);
                        }
                        if (!reader.IsDBNull(8))
                        {
                            s.BudgetBalanceDate = reader.GetDateTime(8);
                        }
                        t.Splits.EndUpdate();
                        s.BatchMode = false;
                        s.OnUpdated();
                        t.OnUpdated();
                    }
                }

                // Resolve transfers (mirrors SqlServerDatabase.ReadTransactions's third pass).
                using (var command = new SqlCommand("dbo.Transactions_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long id = reader.GetInt64(0);
                        long transferTarget = reader.IsDBNull(16) ? -1 : reader.GetInt64(16);
                        if (transferTarget == -1)
                        {
                            continue;
                        }

                        Transaction t = transactions.FindTransactionById(id);
                        System.Diagnostics.Debug.Assert(t != null); // since we just loaded it above.
                        Transaction u = transactions.FindTransactionById(transferTarget);
                        if (u == null)
                        {
                            errors.Add(new DataError(id, "Transaction is marked as a transfer, but other side of transfer was not found"));
                        }
                        if (t != null && u != null)
                        {
                            int sid = reader.IsDBNull(17) ? -1 : reader.GetInt32(17);
                            if (sid == -1)
                            {
                                if (u.Transfer != null)
                                {
                                    if (u.Transfer.Transaction != t)
                                    {
                                        // already have a transfer for this transaction!
                                        errors.Add(new DataError(id, string.Format("Already have a transfer for this transaction, so transfer {0} is a duplicate of transfer {1}", id, u.Transfer.Id)));
                                    }
                                }
                                t.Transfer = new Transfer(id, t, u);
                            }
                            else
                            {
                                Split s = u.FindSplit(sid);
                                if (s == null)
                                {
                                    errors.Add(new DataError(id, sid, "Transaction contains a split marked as a transfer, but other side of transfer was not found"));
                                }
                                else
                                {
                                    if (t.Transfer != null)
                                    {
                                        // already have a transfer for this split!
                                        errors.Add(new DataError(id, string.Format("Already have a transfer for this split, so {0} is a duplicate of {1}", id, t.Transfer.Id)));
                                    }
                                    t.Transfer = new Transfer(id, t, u, s);
                                }
                            }
                            t.OnUpdated();
                        }
                    }
                }

                using (var command = new SqlCommand("dbo.Investments_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long id = reader.GetInt64(0);
                        Transaction t = transactions.FindTransactionById(id);
                        if (t == null)
                        {
                            continue;
                        }

                        Investment i = t.GetOrCreateInvestment();
                        i.Security = reader.IsDBNull(1) ? null : money.Securities.FindSecurityAt(reader.GetInt32(1));
                        i.UnitPrice = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                        i.Units = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                        i.Commission = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4);
                        i.Type = (InvestmentType)reader.GetInt32(5);
                        if (!reader.IsDBNull(6))
                        {
                            i.TradeType = (InvestmentTradeType)reader.GetInt32(6);
                        }
                        if (!reader.IsDBNull(7))
                        {
                            i.TaxExempt = reader.GetBoolean(7);
                        }
                        if (!reader.IsDBNull(8))
                        {
                            i.Withholding = reader.GetDecimal(8);
                        }
                        if (!reader.IsDBNull(9))
                        {
                            i.MarkUpDown = reader.GetDecimal(9);
                        }
                        if (!reader.IsDBNull(10))
                        {
                            i.Taxes = reader.GetDecimal(10);
                        }
                        if (!reader.IsDBNull(11))
                        {
                            i.Fees = reader.GetDecimal(11);
                        }
                        if (!reader.IsDBNull(12))
                        {
                            i.Load = reader.GetDecimal(12);
                        }
                        i.OnUpdated();
                        t.OnUpdated();
                    }
                }
            }

            // recompute state of Payee objects
            foreach (Transaction t in transactions)
            {
                t.BatchMode = true;

                Payee p = t.Payee;
                if (p != null)
                {
                    // setup initial counts
                    if (t.Category == null && t.Transfer == null && !t.IsSplit)
                    {
                        p.UncategorizedTransactions++;
                        p.OnUpdated();
                    }
                    if ((t.Flags & TransactionFlags.Unaccepted) != 0)
                    {
                        p.UnacceptedTransactions++;
                        p.OnUpdated();
                    }
                }

                t.BatchMode = false;
            }

            transactions.FireChangeEvent(transactions, transactions, null, ChangeType.Reloaded);
            return errors;
        }

        public override void UpdateTransactions(Transactions transactions)
        {
            if (transactions.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                foreach (Transaction t in transactions)
                {
                    if (t.Account == null)
                    {
                        continue;
                    }

                    // Splits/Investment carry a FK back to this Transaction. On delete, the child
                    // rows must go first or SQL Server rejects the parent DELETE with them still
                    // pointing at it (FK_Splits_Transaction) - unlike insert/update, where the
                    // parent must exist first, so this ordering only flips for the delete case.
                    if (t.IsDeleted)
                    {
                        if (t.Splits != null)
                        {
                            this.UpdateSplits(t.Splits);
                        }
                        if (t.Investment != null)
                        {
                            this.UpdateInvestment(t.Investment);
                        }
                    }

                    connection.Open();
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", t.Id), ("@Number", (object)t.Number ?? DBNull.Value), ("@Account", t.Account.Id),
                        ("@Date", SqlServerDatabase.DBDateTimeParam(t.Date)), ("@Amount", t.Amount), ("@Status", (int)t.Status),
                        ("@Memo", (object)t.Memo ?? DBNull.Value), ("@Payee", t.Payee != null ? (object)t.Payee.Id : DBNull.Value),
                        ("@Category", t.Category != null ? (object)t.Category.Id : DBNull.Value),
                        ("@Transfer", t.Transfer != null && t.Transfer.Transaction != null ? t.Transfer.Transaction.Id : -1),
                        ("@TransferSplit", t.Transfer != null && t.Transfer.Split != null ? t.Transfer.Split.Id : -1),
                        ("@FITID", (object)t.FITID ?? DBNull.Value), ("@SalesTax", t.SalesTax), ("@Flags", (int)t.Flags),
                        ("@ReconciledDate", SqlServerDatabase.DBNullableDateTimeParam(t.ReconciledDate)),
                        ("@BudgetBalanceDate", SqlServerDatabase.DBNullableDateTimeParam(t.BudgetBalanceDate)),
                        ("@MergeDate", SqlServerDatabase.DBNullableDateTimeParam(t.MergeDate)),
                        ("@OriginalPayee", (object)t.OriginalPayee ?? DBNull.Value)
                    };

                    if (t.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.Transactions_Update", parameters);
                    }
                    else if (t.IsInserted)
                    {
                        if (t.Id == -1)
                        {
                            connection.Close();
                            continue;
                        }
                        ExecuteProc(connection, "dbo.Transactions_Insert", parameters);
                    }
                    else if (t.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.Transactions_Delete", ("@Id", t.Id));
                    }
                    connection.Close();

                    if (!t.IsDeleted)
                    {
                        if (t.Splits != null)
                        {
                            this.UpdateSplits(t.Splits);
                        }
                        if (t.Investment != null)
                        {
                            this.UpdateInvestment(t.Investment);
                        }
                    }
                }
            }

            foreach (Transaction t in transactions)
            {
                t.OnUpdated();
            }
            transactions.RemoveDeleted();
        }

        public override void UpdateSplits(Splits splits)
        {
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Split s in splits)
                {
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", s.Id), ("@Transaction", s.Transaction.Id), ("@Amount", s.Amount),
                        ("@Category", s.Category != null ? (object)s.Category.Id : DBNull.Value), ("@Memo", (object)s.Memo ?? DBNull.Value),
                        ("@Transfer", s.Transfer != null && s.Transfer.Transaction != null ? s.Transfer.Transaction.Id : -1),
                        ("@Payee", s.Payee != null ? (object)s.Payee.Id : DBNull.Value), ("@Flags", (int)s.Flags),
                        ("@BudgetBalanceDate", SqlServerDatabase.DBNullableDateTimeParam(s.BudgetBalanceDate))
                    };

                    if (s.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.Splits_Update", parameters);
                    }
                    else if (s.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.Splits_Insert", parameters);
                    }
                    else if (s.IsDeleted)
                    {
                        ExecuteProc(connection, "dbo.Splits_Delete", ("@Id", s.Id), ("@Transaction", s.Transaction.Id));
                    }
                }
            }

            foreach (Split s in splits)
            {
                s.OnUpdated();
            }
            splits.RemoveDeleted();
        }

        public override void UpdateInvestment(Investment i)
        {
            if (i == null)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                (string Name, object Value)[] parameters =
                {
                    ("@Id", i.Id), ("@Security", i.Security == null ? (object)DBNull.Value : i.Security.Id), ("@UnitPrice", i.UnitPrice),
                    ("@Units", i.Units), ("@Commission", i.Commission), ("@InvestmentType", (int)i.Type),
                    ("@TradeType", (int)i.TradeType), ("@TaxExempt", i.TaxExempt ? 1 : 0), ("@Withholding", i.Withholding),
                    ("@MarkUpDown", i.MarkUpDown), ("@Taxes", i.Taxes), ("@Fees", i.Fees), ("@Load", i.Load)
                };

                if (i.IsChanged)
                {
                    ExecuteProc(connection, "dbo.Investments_Update", parameters);
                }
                else if (i.IsInserted)
                {
                    ExecuteProc(connection, "dbo.Investments_Insert", parameters);
                }
                else if (i.IsDeleted)
                {
                    ExecuteProc(connection, "dbo.Investments_Delete", ("@Id", i.Id));
                }
            }
            i.OnUpdated();
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

        public override void SaveOne<T>(T root)
        {
            this.SaveBatch(new PersistentObject[] { root });
        }

        public override void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            List<PersistentObject> list = new List<PersistentObject>(roots);
            if (list.Count == 0)
            {
                return;
            }

            // Every root in the batch must be the SAME type - a batch mixing types (e.g. one
            // Category and one Currency) falls through to the inherited stub until a later task
            // adds real cross-type atomicity (each entity's _SaveBatch proc owns its own
            // transaction; mixing types safely needs an ambient ombudsman transaction the proc's
            // internal BEGIN TRAN can nest inside - not needed for any currently-real caller).
            Type firstType = list[0].GetType();
            foreach (PersistentObject root in list)
            {
                if (root.GetType() != firstType)
                {
                    base.SaveBatch(list);
                    return;
                }
            }

            this.Connect();
            using (SqlConnection connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                List<Action> postCommitActions = new List<Action>();
                if (firstType == typeof(Category))
                {
                    this.SaveCategoryBatch(list.ConvertAll(r => (Category)r), connection, null, postCommitActions);
                }
                else if (firstType == typeof(Currency))
                {
                    this.SaveCurrencyBatch(list.ConvertAll(r => (Currency)r), connection, null, postCommitActions);
                }
                else if (firstType == typeof(OnlineAccount))
                {
                    this.SaveOnlineAccountBatch(list.ConvertAll(r => (OnlineAccount)r), connection, null, postCommitActions);
                }
                else
                {
                    base.SaveBatch(list);
                    return;
                }
                foreach (Action action in postCommitActions)
                {
                    action();
                }
            }
        }

        /// <summary>
        /// Executes one of the new *_SaveBatch stored procs (Categories_SaveBatch,
        /// Currencies_SaveBatch, etc.) and applies the shared two-result-set contract every one of
        /// them follows: a row with Result='CONFLICT' means the proc's own conflict check found a
        /// stale or missing row and already rolled back its own transaction server-side before
        /// returning - this throws ConcurrencyConflictException for that row's Id (looked up in
        /// rootsById, since the exception needs the actual PersistentObject, not just its Id).
        /// Every other returned row is Result='OK' with that row's new Version, applied via
        /// applyNewVersion. transaction may be null (the common single-type-group case, where the
        /// proc's own internal BEGIN TRAN/COMMIT/ROLLBACK is a complete, standalone transaction) or
        /// a real ambient SqlTransaction (a later task's multi-type case, where the proc's internal
        /// BEGIN TRAN nests inside it).
        /// </summary>
        private void ExecuteSaveBatchProc(
            SqlConnection connection,
            SqlTransaction transaction,
            string procName,
            (string ParamName, string TvpTypeName, DataTable Rows)[] tvpParameters,
            Dictionary<long, PersistentObject> rootsById,
            Action<long, long> applyNewVersion)
        {
            using (SqlCommand command = new SqlCommand(procName, connection) { CommandType = CommandType.StoredProcedure })
            {
                if (transaction != null)
                {
                    command.Transaction = transaction;
                }
                foreach (var (paramName, tvpTypeName, rows) in tvpParameters)
                {
                    SqlParameter p = command.Parameters.AddWithValue(paramName, rows);
                    p.SqlDbType = SqlDbType.Structured;
                    p.TypeName = tvpTypeName;
                }
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Id is INT for some entities (e.g. Category) and BIGINT for others (e.g.
                        // Transaction) - Convert.ToInt64 handles either underlying SQL type,
                        // whereas SqlDataReader.GetInt64 throws InvalidCastException on an INT
                        // column.
                        long id = Convert.ToInt64(reader.GetValue(reader.GetOrdinal("Id")));
                        string result = reader.GetString(reader.GetOrdinal("Result"));
                        if (result == "CONFLICT")
                        {
                            long storedVersion = reader.GetInt64(reader.GetOrdinal("StoredVersion"));
                            long callerVersion = reader.GetInt64(reader.GetOrdinal("CallerVersion"));
                            if (!rootsById.TryGetValue(id, out PersistentObject conflictRoot))
                            {
                                // ConcurrencyConflictException's constructor calls
                                // root.GetType() unconditionally, so a null root would surface as
                                // a raw NullReferenceException instead of the intended, well-typed
                                // exception. rootsById is supposed to contain every id that could
                                // come back from the proc's result set (it's built from the same
                                // batch that was sent), so a miss here means the caller broke that
                                // invariant - fail fast with a clear diagnostic naming the proc and
                                // the id, rather than let it degrade into an NRE.
                                throw new InvalidOperationException(string.Format(
                                    "{0} reported a CONFLICT for Id {1}, but no matching root was found in rootsById. " +
                                    "The caller must populate rootsById with every root in the batch before calling ExecuteSaveBatchProc.",
                                    procName, id));
                            }
                            throw new ConcurrencyConflictException(conflictRoot, storedVersion, callerVersion);
                        }
                        long newVersion = reader.GetInt64(reader.GetOrdinal("NewVersion"));
                        applyNewVersion(id, newVersion);
                    }
                }
            }
        }

        private static DataTable NewCategoryRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("ParentId", typeof(int));
            table.Columns.Add("Budget", typeof(decimal));
            table.Columns.Add("Frequency", typeof(int));
            table.Columns.Add("Balance", typeof(decimal));
            table.Columns.Add("Color", typeof(string));
            table.Columns.Add("TaxRefNum", typeof(int));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        /// <summary>
        /// Writes a batch of Category rows via dbo.Categories_SaveBatch. Deletes get no row back
        /// in the proc's result set (nothing to report a NewVersion for), so their postCommit
        /// action is queued directly here, right after ExecuteSaveBatchProc returns without
        /// throwing - which only happens once the whole batch (inserts, updates, AND deletes) has
        /// already committed inside the proc's own transaction.
        /// </summary>
        private void SaveCategoryBatch(List<Category> categories, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewCategoryRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, Category> byId = new Dictionary<long, Category>();
            List<Category> deletedInThisBatch = new List<Category>();

            foreach (Category c in categories)
            {
                long id = c.Id;
                rootsById[id] = c;
                byId[id] = c;
                long callerRowVersion = c.RowVersion;

                if (c.IsInserted)
                {
                    rows.Rows.Add("I", c.Id, c.Name, c.Description, (int)c.Type,
                        c.ParentCategory != null ? (object)c.ParentCategory.Id : DBNull.Value,
                        c.Budget, (int)c.Frequency, c.Balance, (object)c.Color ?? DBNull.Value,
                        c.TaxRefNum, DBNull.Value);
                }
                else if (c.IsChanged)
                {
                    rows.Rows.Add("U", c.Id, c.Name, c.Description, (int)c.Type,
                        c.ParentCategory != null ? (object)c.ParentCategory.Id : DBNull.Value,
                        c.Budget, (int)c.Frequency, c.Balance, (object)c.Color ?? DBNull.Value,
                        c.TaxRefNum, callerRowVersion);
                }
                else if (c.IsDeleted)
                {
                    rows.Rows.Add("D", c.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(c);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.Categories_SaveBatch",
                new[] { ("@Rows", "dbo.CategorySaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    Category c = byId[id];
                    postCommitActions.Add(() =>
                    {
                        c.RowVersion = newVersion;
                        c.OnUpdated();
                    });
                });

            foreach (Category c in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    c.OnUpdated();
                    c.Parent.RemoveChild(c, true);
                });
            }
        }

        private static DataTable NewCurrencyRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Symbol", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Ratio", typeof(decimal));
            table.Columns.Add("LastRatio", typeof(decimal));
            table.Columns.Add("CultureCode", typeof(string));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SaveCurrencyBatch(List<Currency> currencies, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewCurrencyRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, Currency> byId = new Dictionary<long, Currency>();
            List<Currency> deletedInThisBatch = new List<Currency>();

            foreach (Currency c in currencies)
            {
                long id = c.Id;
                rootsById[id] = c;
                byId[id] = c;
                long callerRowVersion = c.RowVersion;

                if (c.IsInserted)
                {
                    rows.Rows.Add("I", c.Id, c.Symbol, c.Name, c.Ratio, c.LastRatio, c.CultureCode, DBNull.Value);
                }
                else if (c.IsChanged)
                {
                    rows.Rows.Add("U", c.Id, c.Symbol, c.Name, c.Ratio, c.LastRatio, c.CultureCode, callerRowVersion);
                }
                else if (c.IsDeleted)
                {
                    rows.Rows.Add("D", c.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(c);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.Currencies_SaveBatch",
                new[] { ("@Rows", "dbo.CurrencySaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    Currency c = byId[id];
                    postCommitActions.Add(() =>
                    {
                        c.RowVersion = newVersion;
                        c.OnUpdated();
                    });
                });

            foreach (Currency c in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    c.OnUpdated();
                    c.Parent.RemoveChild(c, true);
                });
            }
        }

        private static DataTable NewOnlineAccountRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Institution", typeof(string));
            table.Columns.Add("OFX", typeof(string));
            table.Columns.Add("FID", typeof(string));
            table.Columns.Add("UserId", typeof(string));
            table.Columns.Add("Password", typeof(string));
            table.Columns.Add("BankId", typeof(string));
            table.Columns.Add("BranchId", typeof(string));
            table.Columns.Add("BrokerId", typeof(string));
            table.Columns.Add("OfxVersion", typeof(string));
            table.Columns.Add("LogoUrl", typeof(string));
            table.Columns.Add("AppId", typeof(string));
            table.Columns.Add("AppVersion", typeof(string));
            table.Columns.Add("ClientUid", typeof(string));
            table.Columns.Add("UserCred1", typeof(string));
            table.Columns.Add("UserCred2", typeof(string));
            table.Columns.Add("AuthToken", typeof(string));
            table.Columns.Add("AccessKey", typeof(string));
            table.Columns.Add("UserKey", typeof(string));
            table.Columns.Add("UserKeyExpireDate", typeof(DateTime));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SaveOnlineAccountBatch(List<OnlineAccount> accounts, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewOnlineAccountRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, OnlineAccount> byId = new Dictionary<long, OnlineAccount>();
            List<OnlineAccount> deletedInThisBatch = new List<OnlineAccount>();

            foreach (OnlineAccount i in accounts)
            {
                long id = i.Id;
                rootsById[id] = i;
                byId[id] = i;
                long callerRowVersion = i.RowVersion;
                object expireDate = SqlServerDatabase.DBNullableDateTimeParam(i.UserKeyExpireDate);

                if (i.IsInserted)
                {
                    rows.Rows.Add("I", i.Id, i.Name, i.Institution, i.Ofx, i.FID, i.UserId, i.Password,
                        i.BankId, i.BranchId, i.BrokerId, i.OfxVersion, i.LogoUrl, i.AppId, i.AppVersion,
                        i.ClientUid, i.UserCred1, i.UserCred2, i.AuthToken, i.AccessKey, i.UserKey,
                        expireDate, DBNull.Value);
                }
                else if (i.IsChanged)
                {
                    rows.Rows.Add("U", i.Id, i.Name, i.Institution, i.Ofx, i.FID, i.UserId, i.Password,
                        i.BankId, i.BranchId, i.BrokerId, i.OfxVersion, i.LogoUrl, i.AppId, i.AppVersion,
                        i.ClientUid, i.UserCred1, i.UserCred2, i.AuthToken, i.AccessKey, i.UserKey,
                        expireDate, callerRowVersion);
                }
                else if (i.IsDeleted)
                {
                    rows.Rows.Add("D", i.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(i);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.OnlineAccounts_SaveBatch",
                new[] { ("@Rows", "dbo.OnlineAccountSaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    OnlineAccount i = byId[id];
                    postCommitActions.Add(() =>
                    {
                        i.RowVersion = newVersion;
                        i.OnUpdated();
                    });
                });

            foreach (OnlineAccount i in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    i.OnUpdated();
                    i.Parent.RemoveChild(i, true);
                });
            }
        }
    }
}
