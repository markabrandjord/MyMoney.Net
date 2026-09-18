# Issue #27: SqlServerStoredProcDatabase.Save() Atomicity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `SqlServerStoredProcDatabase.Save(MyMoney)` actually atomic — a failure partway through must roll back everything the same `Save()` call already wrote, instead of leaving earlier `UpdateXxx` calls' work permanently committed.

**Architecture:** `SqlServerDatabase.Save(MyMoney)` (`SqlDatabase.cs`) already opens one ambient `SqlConnection`/`SqlTransaction` pair and stores the transaction in a private field before calling the 13 `UpdateXxx` methods virtually. `SqlServerStoredProcDatabase`'s 16 `UpdateXxx` overrides ignore that ambient state entirely — each opens its own throwaway `SqlConnection` and lets every stored-proc call auto-commit independently. The fix exposes the ambient transaction to subclasses, and gives every override a `BeginScope()` helper that reuses the ambient connection+transaction when `Save()` is in progress, or falls back to today's own-connection-per-call behavior when an override is invoked standalone (as the existing unit tests already do, calling e.g. `db.UpdatePayees(...)` directly with no `Save()` wrapper).

**Tech Stack:** C# / .NET 10, ADO.NET (`Microsoft.Data.SqlClient`), NUnit.

**Spec:** GitHub issue #27 (`SqlServerStoredProcDatabase's Save() is not atomic`). No separate design doc — the issue itself lays out the two options and this plan implements option (a): thread the ambient connection+transaction through the overrides.

## Global Constraints

- Must not change behavior for any override called standalone (not via `Save()`) — the existing `SqlServerStoredProcDatabaseTests` suite calls `UpdateXxx` methods directly, with no ambient transaction, and those tests must keep passing unmodified.
- Must not touch `SaveOne`/`SaveTransfer`/`SaveBatch` or the `*_SaveBatch` stored-proc path — that's a separate, already-atomic mechanism (Phase 2c) unrelated to this bug.
- Tests run against real SQL Server via `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`; per user decision this session, running them wipes the shared `Redmond/MyMoney` test database — that's expected and fine (it's a test database, not production data).

---

## Task 1: Add a failing regression test proving Save() is not atomic

**Files:**
- Modify: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs`

**Interfaces:**
- Consumes: `SqlServerStoredProcDatabase.Save(MyMoney)`, `.UpdateCategories(Categories)`, `.ReadOnlineAccounts(OnlineAccounts, MyMoney)` (all pre-existing, unchanged in this task).
- Produces: nothing new for later tasks — this is the RED half of the TDD cycle Task 2's GREEN half depends on.

- [ ] **Step 1: Write the failing test**

Add this test to `SqlServerStoredProcDatabaseTests.cs` (anywhere among the other `[Test]` methods, e.g. right after `InsertUpdateDeleteCategory_RoundTripsThroughStoredProcedures`):

```csharp
[Test]
public void Save_PartialFailureRollsBackAllEntityTypes()
{
    string connectionString = this.GetConnectionStringOrSkip();
    var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

    // Pre-seed a Category row directly (bypasses Save(), commits immediately on its own
    // connection) so inserting a second Category with the same Id later, from inside Save(),
    // collides with a real PRIMARY KEY violation. This proves Save()'s atomicity using a
    // constraint already confirmed to exist (every [TableMapping] table's Id column is declared
    // PRIMARY KEY - see SqlDatabase.cs's GetCreateTableScript) rather than relying on schema
    // details, like foreign keys, that may not be present on every configured database.
    var seed = new MyMoney();
    var seedCategory = new Category(seed.Categories) { Id = 999301, Name = "Issue27PreexistingCategory", Type = CategoryType.Expense };
    seed.Categories.AddCategory(seedCategory);
    seedCategory.OnInserted();
    db.UpdateCategories(seed.Categories);

    var money = new MyMoney();
    var onlineAccount = money.OnlineAccounts.AddOnlineAccount(999300);
    onlineAccount.Name = "Issue27RollbackTest Bank";
    onlineAccount.OnInserted();

    var conflictingCategory = new Category(money.Categories) { Id = 999301, Name = "Issue27ConflictingCategory", Type = CategoryType.Expense };
    money.Categories.AddCategory(conflictingCategory);
    conflictingCategory.OnInserted();

    // Save(MyMoney) calls UpdateOnlineAccounts before UpdateCategories (see SqlDatabase.cs's
    // Save(MyMoney) method), so the OnlineAccount insert below succeeds before the Category
    // insert hits the PRIMARY KEY violation from the pre-seeded row above.
    Assert.Throws<Microsoft.Data.SqlClient.SqlException>(() => db.Save(money));

    var reloaded = new MyMoney();
    db.ReadOnlineAccounts(reloaded.OnlineAccounts, reloaded);
    Assert.That(reloaded.OnlineAccounts.FindOnlineAccountAt(999300), Is.Null,
        "Save() must be atomic: the OnlineAccount insert that succeeded before the later " +
        "Categories PRIMARY KEY violation should have been rolled back along with it.");
}
```

- [ ] **Step 2: Run it against today's (unfixed) code and confirm it fails**

Run:
```
dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=Save_PartialFailureRollsBackAllEntityTypes"
```
Expected: **FAIL** at the final `Assert.That(...FindOnlineAccountAt(999300), Is.Null)` — the OnlineAccount insert commits independently on its own connection today, so it survives the later Category failure. (The `Assert.Throws<SqlException>` line itself should already pass, since the PK violation does occur.) If the test instead errors out earlier (e.g. `Assert.Ignore` because `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` isn't set), set that environment variable to a valid `MyMoneyUser`/`MyMoneyTest` connection string first.

- [ ] **Step 3: Commit**

```bash
git add Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "test: reproduce SqlServerStoredProcDatabase.Save() non-atomicity (#27)"
```

---

## Task 2: Thread the ambient transaction through every UpdateXxx override

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (expose the ambient transaction)
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (all 16 `UpdateXxx` overrides + the two `ExecuteProc`/`ExecutePayeeProc` helpers)

**Interfaces:**
- Consumes: nothing new.
- Produces: `protected SqlTransaction AmbientTransaction { get; }` on `SqlServerDatabase`, for any future subclass that needs the same enlistment pattern.

- [ ] **Step 1: Expose the ambient transaction from the base class**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, immediately after the existing `private SqlTransaction transaction;` field (the one `Save(MyMoney)` assigns to), add:

```csharp
private SqlTransaction transaction;

/// <summary>
/// Exposes the ambient transaction Save(MyMoney) begins, so subclasses whose UpdateXxx
/// overrides open their own ADO.NET calls (e.g. SqlServerStoredProcDatabase) can enlist in it
/// instead of running outside it. Null outside of an in-progress Save() call.
/// </summary>
protected SqlTransaction AmbientTransaction => this.transaction;
```

- [ ] **Step 2: Add the connection-scope helper and update the shared ADO.NET helpers**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, replace the existing `ExecuteProc`/`ExecutePayeeProc` methods:

```csharp
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
```

with:

```csharp
/// <summary>
/// Returns the connection+transaction an UpdateXxx override should use. When Save(MyMoney) is
/// in progress, this.AmbientTransaction is non-null - reuse the same ambient SqlConnection and
/// enlist in that transaction so a later failure rolls back everything Save() already wrote.
/// When called standalone (every existing unit test calls UpdateXxx methods directly, with no
/// Save() wrapper), AmbientTransaction is null - open a fresh connection with no transaction,
/// matching this class's pre-existing standalone behavior (each stored-proc call auto-commits
/// independently). OwnsConnection tells the caller whether it must dispose the connection when
/// done (true only for the standalone, freshly-opened case).
/// </summary>
private (SqlConnection Connection, SqlTransaction Transaction, bool OwnsConnection) BeginScope()
{
    if (this.AmbientTransaction != null)
    {
        return (this.ConnectSqlServer(), this.AmbientTransaction, false);
    }
    var connection = new SqlConnection(this.GetConnectionString(true));
    connection.Open();
    return (connection, null, true);
}

private static void EndScope(SqlConnection connection, bool ownsConnection)
{
    if (ownsConnection)
    {
        connection.Dispose();
    }
}

private static void ExecuteProc(SqlConnection connection, SqlTransaction transaction, string procName, params (string Name, object Value)[] parameters)
{
    using (var command = new SqlCommand(procName, connection) { CommandType = CommandType.StoredProcedure, Transaction = transaction })
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }
}

private static void ExecutePayeeProc(SqlConnection connection, SqlTransaction transaction, string procName, Payee p)
{
    ExecuteProc(connection, transaction, procName, ("@Id", p.Id), ("@Name", (object)p.Name ?? DBNull.Value));
}
```

- [ ] **Step 3: Migrate the 13 simple-shaped UpdateXxx overrides**

These all follow the identical `using (var connection = new SqlConnection(...)) { connection.Open(); foreach (...) { ExecuteProc(connection, "proc", params); } }` shape. Replace each with the `BeginScope()`/`EndScope()` equivalent below (method bodies otherwise byte-for-byte identical — only the connection setup/teardown and the `ExecuteProc(...)` call sites change to pass `transaction`).

`UpdateOnlineAccounts`:
```csharp
public override void UpdateOnlineAccounts(OnlineAccounts accounts)
{
    if (accounts.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                ExecuteProc(connection, transaction, "dbo.OnlineAccounts_Update", parameters);
            }
            else if (i.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.OnlineAccounts_Insert", parameters);
            }
            else if (i.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.OnlineAccounts_Delete", ("@Id", i.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
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
```

`UpdateAccountAliases`:
```csharp
public override void UpdateAccountAliases(AccountAliases accountAliases)
{
    if (accountAliases.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                    ExecuteProc(connection, transaction, "dbo.AccountAliases_Update", parameters);
                }
                else
                {
                    ExecuteProc(connection, transaction, "dbo.AccountAliases_Insert", parameters);
                }
            }
            else if (a.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.AccountAliases_Delete", ("@Id", a.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (AccountAlias a in accountAliases)
    {
        a.OnUpdated();
    }
    accountAliases.RemoveDeleted();
}
```

`UpdateTransactionExtras`:
```csharp
public override void UpdateTransactionExtras(TransactionExtras extras)
{
    if (extras.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                    ExecuteProc(connection, transaction, "dbo.TransactionExtras_Update", parameters);
                }
                else
                {
                    ExecuteProc(connection, transaction, "dbo.TransactionExtras_Insert", parameters);
                }
            }
            else if (e.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.TransactionExtras_Delete", ("@Id", e.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (TransactionExtra e in extras)
    {
        e.OnUpdated();
    }
    extras.RemoveDeleted();
}
```

`UpdateLoanPayments`:
```csharp
public override void UpdateLoanPayments(LoanPayments loans)
{
    if (loans.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
        foreach (LoanPayment i in loans)
        {
            (string Name, object Value)[] parameters =
            {
                ("@Id", i.Id), ("@AccountId", i.AccountId), ("@Date", SqlServerDatabase.DBDateTimeParam(i.Date)),
                ("@Principal", i.Principal), ("@Interest", i.Interest), ("@Memo", (object)i.Memo ?? DBNull.Value)
            };

            if (i.IsChanged)
            {
                ExecuteProc(connection, transaction, "dbo.LoanPayments_Update", parameters);
            }
            else if (i.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.LoanPayments_Insert", parameters);
            }
            else if (i.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.LoanPayments_Delete", ("@Id", i.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (LoanPayment i in loans)
    {
        i.OnUpdated();
    }
    loans.RemoveDeleted();
}
```

`UpdateRentUnits`:
```csharp
public override void UpdateRentUnits(RentUnits units)
{
    if (units.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
        foreach (RentUnit x in units)
        {
            (string Name, object Value)[] parameters =
            {
                ("@Id", x.Id), ("@Building", x.Building), ("@Name", (object)x.Name ?? DBNull.Value),
                ("@Renter", (object)x.Renter ?? DBNull.Value), ("@Note", (object)x.Note ?? DBNull.Value)
            };

            if (x.IsChanged)
            {
                ExecuteProc(connection, transaction, "dbo.RentUnits_Update", parameters);
            }
            else if (x.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.RentUnits_Insert", parameters);
            }
            else if (x.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.RentUnits_Delete", ("@Id", x.Id), ("@Building", x.Building));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (RentUnit x in units)
    {
        x.OnUpdated();
    }
    units.RemoveDeleted();
}
```

`UpdateRentBuildings`:
```csharp
public override void UpdateRentBuildings(RentBuildings buildings)
{
    if (buildings.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                ExecuteProc(connection, transaction, "dbo.RentBuildings_Update", parameters);
            }
            else if (p.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.RentBuildings_Insert", parameters);
            }
            else if (p.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.RentBuildings_Delete", ("@Id", p.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (RentBuilding p in buildings)
    {
        p.OnUpdated();
    }
    buildings.RemoveDeleted();
}
```

`UpdatePayees` (the one override with a raw `SqlCommand` delete branch alongside `ExecutePayeeProc`):
```csharp
public override void UpdatePayees(Payees payees)
{
    if (payees.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
        foreach (Payee p in payees)
        {
            if (p.IsChanged)
            {
                ExecutePayeeProc(connection, transaction, "dbo.Payees_Update", p);
            }
            else if (p.IsInserted)
            {
                ExecutePayeeProc(connection, transaction, "dbo.Payees_Insert", p);
            }
            else if (p.IsDeleted)
            {
                using (var command = new SqlCommand("dbo.Payees_Delete", connection) { CommandType = System.Data.CommandType.StoredProcedure, Transaction = transaction })
                {
                    command.Parameters.AddWithValue("@Id", p.Id);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
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
```

`UpdateAccounts`:
```csharp
public override void UpdateAccounts(Accounts accounts)
{
    if (accounts.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                ExecuteProc(connection, transaction, "dbo.Accounts_Update", parameters);
            }
            else if (a.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.Accounts_Insert", parameters);
            }
            else if (a.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.Accounts_Delete", ("@Id", a.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
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
```

`UpdateCategories`:
```csharp
public override void UpdateCategories(Categories categories)
{
    if (categories.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                ExecuteProc(connection, transaction, "dbo.Categories_Update", parameters);
            }
            else if (c.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.Categories_Insert", parameters);
            }
            else if (c.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.Categories_Delete", ("@Id", c.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (Category c in categories)
    {
        c.OnUpdated();
    }
    categories.RemoveDeleted();
}
```

`UpdateCurrencies`:
```csharp
public override void UpdateCurrencies(Currencies currencies)
{
    if (currencies.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
        foreach (Currency s in currencies)
        {
            (string Name, object Value)[] parameters =
            {
                ("@Id", s.Id), ("@Symbol", (object)s.Symbol ?? DBNull.Value), ("@Name", (object)s.Name ?? DBNull.Value),
                ("@Ratio", s.Ratio), ("@LastRatio", s.LastRatio), ("@CultureCode", (object)s.CultureCode ?? DBNull.Value)
            };

            if (s.IsChanged)
            {
                ExecuteProc(connection, transaction, "dbo.Currencies_Update", parameters);
            }
            else if (s.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.Currencies_Insert", parameters);
            }
            else if (s.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.Currencies_Delete", ("@Id", s.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (Currency s in currencies)
    {
        s.OnUpdated();
    }
    currencies.RemoveDeleted();
}
```

`UpdateSecurities`:
```csharp
public override void UpdateSecurities(Securities securities)
{
    if (securities.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                ExecuteProc(connection, transaction, "dbo.Securities_Update", parameters);
            }
            else if (s.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.Securities_Insert", parameters);
            }
            else if (s.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.Securities_Delete", ("@Id", s.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (Security s in securities)
    {
        s.OnUpdated();
    }
    securities.RemoveDeleted();
}
```

`UpdateStockSplits`:
```csharp
public override void UpdateStockSplits(StockSplits stockSplits)
{
    if (stockSplits.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                    ExecuteProc(connection, transaction, "dbo.StockSplits_Update", parameters);
                }
                else
                {
                    ExecuteProc(connection, transaction, "dbo.StockSplits_Insert", parameters);
                }
            }
            else if (s.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.StockSplits_Delete", ("@Id", s.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (StockSplit s in stockSplits)
    {
        s.OnUpdated();
    }
    stockSplits.RemoveDeleted();
}
```

`UpdateAliases`:
```csharp
public override void UpdateAliases(Aliases aliases)
{
    if (aliases.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                    ExecuteProc(connection, transaction, "dbo.Aliases_Update", parameters);
                }
                else
                {
                    ExecuteProc(connection, transaction, "dbo.Aliases_Insert", parameters);
                }
            }
            else if (a.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.Aliases_Delete", ("@Id", a.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (Alias a in aliases)
    {
        a.OnUpdated();
    }
    aliases.RemoveDeleted();
}
```

- [ ] **Step 4: Migrate UpdateTransactions, UpdateSplits, UpdateInvestment**

These three are special: `UpdateTransactions` currently opens one `SqlConnection` for the whole method but then calls `connection.Open()`/`connection.Close()` again on every single loop iteration (dead weight now that `BeginScope()` opens it once), and it calls into `UpdateSplits`/`UpdateInvestment` mid-loop, which must transparently join the same ambient scope when `Save()` is running.

`UpdateTransactions`:
```csharp
public override void UpdateTransactions(Transactions transactions)
{
    if (transactions.Count == 0)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
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
                ExecuteProc(connection, transaction, "dbo.Transactions_Update", parameters);
            }
            else if (t.IsInserted)
            {
                if (t.Id == -1)
                {
                    continue;
                }
                ExecuteProc(connection, transaction, "dbo.Transactions_Insert", parameters);
            }
            else if (t.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.Transactions_Delete", ("@Id", t.Id));
            }

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
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (Transaction t in transactions)
    {
        t.OnUpdated();
    }
    transactions.RemoveDeleted();
}
```

`UpdateSplits`:
```csharp
public override void UpdateSplits(Splits splits)
{
    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
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
                ExecuteProc(connection, transaction, "dbo.Splits_Update", parameters);
            }
            else if (s.IsInserted)
            {
                ExecuteProc(connection, transaction, "dbo.Splits_Insert", parameters);
            }
            else if (s.IsDeleted)
            {
                ExecuteProc(connection, transaction, "dbo.Splits_Delete", ("@Id", s.Id), ("@Transaction", s.Transaction.Id));
            }
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }

    foreach (Split s in splits)
    {
        s.OnUpdated();
    }
    splits.RemoveDeleted();
}
```

`UpdateInvestment`:
```csharp
public override void UpdateInvestment(Investment i)
{
    if (i == null)
    {
        return;
    }

    var (connection, transaction, ownsConnection) = this.BeginScope();
    try
    {
        (string Name, object Value)[] parameters =
        {
            ("@Id", i.Id), ("@Security", i.Security == null ? (object)DBNull.Value : i.Security.Id), ("@UnitPrice", i.UnitPrice),
            ("@Units", i.Units), ("@Commission", i.Commission), ("@InvestmentType", (int)i.Type),
            ("@TradeType", (int)i.TradeType), ("@TaxExempt", i.TaxExempt ? 1 : 0), ("@Withholding", i.Withholding),
            ("@MarkUpDown", i.MarkUpDown), ("@Taxes", i.Taxes), ("@Fees", i.Fees), ("@Load", i.Load)
        };

        if (i.IsChanged)
        {
            ExecuteProc(connection, transaction, "dbo.Investments_Update", parameters);
        }
        else if (i.IsInserted)
        {
            ExecuteProc(connection, transaction, "dbo.Investments_Insert", parameters);
        }
        else if (i.IsDeleted)
        {
            ExecuteProc(connection, transaction, "dbo.Investments_Delete", ("@Id", i.Id));
        }
    }
    finally
    {
        EndScope(connection, ownsConnection);
    }
    i.OnUpdated();
}
```

- [ ] **Step 5: Build and run the new regression test — confirm it now passes**

Run:
```
dotnet build Source/WPF/MyMoney.sln
dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=Save_PartialFailureRollsBackAllEntityTypes"
```
Expected: build succeeds, test **PASSES** — the OnlineAccount insert is now rolled back along with the Category PK violation.

- [ ] **Step 6: Run the full existing SQL Server test suites — confirm no regressions**

Run:
```
dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerStoredProcDatabaseTests"
dotnet test Source/WPF/MyMoney.sln
```
Expected: every pre-existing test in `SqlServerStoredProcDatabaseTests` (all of which call `UpdateXxx` methods standalone, with no `Save()` wrapper) still passes unchanged, and the full solution test run shows no new failures. This confirms the standalone/no-ambient-transaction fallback path in `BeginScope()` preserves prior behavior.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "fix: enlist SqlServerStoredProcDatabase's UpdateXxx overrides in Save()'s ambient transaction (#27)"
```

---

## Self-Review Notes

- **Spec coverage:** Issue #27's option (a) — "have each UpdateXxx override accept/reuse the ambient connection+transaction from Save() instead of opening its own" — is fully implemented; all 16 overrides listed in the issue (OnlineAccounts, AccountAliases, TransactionExtras, LoanPayments, RentUnits, RentBuildings, Payees, Accounts, Categories, Currencies, Securities, StockSplits, Aliases, Transactions, Splits, Investment) are covered in Task 2.
- **Standalone-call compatibility:** every override falls back to its pre-existing own-connection, no-transaction behavior when `Save()` isn't in progress, so the ~30 existing direct `db.UpdateXxx(...)` calls in `SqlServerStoredProcDatabaseTests.cs` are unaffected.
- **Out of scope, confirmed:** `SaveOne`/`SaveTransfer`/`SaveBatch` and the `*_SaveBatch` stored procs are a separate, already-atomic mechanism (Phase 2c) and are not touched by this plan.
