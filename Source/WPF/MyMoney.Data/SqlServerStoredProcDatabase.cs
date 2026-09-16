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
                this.ReadCategories(money.Categories, money);
                this.ReadAccounts(money.Accounts, money);
                this.ReadCurrencies(money.Currencies, money);
                this.ReadSecurities(money.Securities, money);
                this.ReadStockSplits(money.StockSplits, money);
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
                            c.Color = reader.GetString(8);
                        }
                        if (!reader.IsDBNull(9))
                        {
                            c.TaxRefNum = reader.GetInt32(9);
                        }
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

        public void ReadStockSplits(StockSplits splits, MyMoney money)
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
                        s.Security = money.Securities.FindSecurityAt(reader.GetInt32(2));
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
                    if (s.Security == null || s.Date == DateTime.MinValue)
                    {
                        continue;
                    }

                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", s.Id), ("@Date", SqlServerDatabase.DBDateTimeParam(s.Date)), ("@Security", s.Security.Id),
                        ("@Numerator", s.Numerator), ("@Denominator", s.Denominator)
                    };

                    if (s.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.StockSplits_Update", parameters);
                    }
                    else if (s.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.StockSplits_Insert", parameters);
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
