# Issue #23: SQL Server Stored-Proc CRUD for Remaining Entities — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the issue #22 stored-proc CRUD pattern (`MyMoneyUser`/`MyMoneyTest` SQL Server roles get CRUD only through `dbo.*_Insert/_Update/_Delete/_SelectAll` stored procedures, never direct table grants) to the six `[TableMapping]` entities #22 deliberately left out: `OnlineAccounts`, `AccountAliases`, `TransactionExtras`, `RentBuildings`, `RentUnits`, `LoanPayments`.

**Architecture:** Same shape as issue #22, entity by entity: one `*_AccessProcs.sql` script per entity (SelectAll/Insert/Update/Delete procs + `GRANT EXECUTE` to `MyMoneyUser`/`MyMoneyTest`), one `public override` `ReadXxx`/`UpdateXxx` pair per entity on `SqlServerStoredProcDatabase` (in `Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs`), wired into that class's `Load()` override in FK-safe order, registered in `BootstrapRunner`'s deployment list, and proven with a smoke test in `SqlServerStoredProcDatabaseTests.cs`. A one-time prerequisite (Task 1) promotes the six entities' base-class `ReadXxx`/`UpdateXxx` methods in `SqlDatabase.cs` to `public virtual` — unlike every issue #22 entity, these six are currently non-virtual (two are `private`), so `SqlServerStoredProcDatabase` cannot `override` them until that's fixed.

**Tech Stack:** C# / .NET 10.0, `Microsoft.Data.SqlClient`, NUnit, T-SQL (SQL Server stored procedures).

**Spec:** No separate spec doc — the issue #22 design doc (`docs\superpowers\plans\2026-09-15-sql-server-user-tier-crud.md`) and its shipped implementation (`Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs`, merged as PR #29 / commit `f1ee913`) is the pattern this plan mechanically extends. GitHub issue #23 is the tracking issue.

## Global Constraints

- Every new stored procedure file follows the exact issue #22 convention: `CREATE OR ALTER PROCEDURE`, `SET NOCOUNT ON;`, one `GO` per batch, header comment naming the table, and `GRANT EXECUTE` to both `MyMoneyUser` and `MyMoneyTest` for all four procs at the end of the file.
- Every new C# override method matches the ADO.NET style already used throughout `SqlServerStoredProcDatabase.cs`: raw `SqlConnection`/`SqlCommand` with `CommandType.StoredProcedure`, inline `reader.IsDBNull(i) ? null : reader.GetX(i)` null checks (no `ReadDbString`/`ReadInt32` helpers — this file doesn't use them), and the shared private `ExecuteProc(connection, procName, params (string, object)[] parameters)` helper for Insert/Update/Delete calls.
- String columns whose `[ColumnMapping]` has no explicit `SqlType` and `MaxLength < 50` are stored as `nchar(N)` by `LazyCreateTables()`/`Mapping.cs` and therefore come back space-padded — the C# read code must `.TrimEnd()` those columns (a real bug class fixed during issue #22's review round; see Task 2 and Task 3 below for which columns need it). Columns with `MaxLength >= 50` are `nvarchar(N)` and need no trimming. Stored-proc parameter declarations always use `NVARCHAR(N)` regardless of the underlying column's `nchar`/`nvarchar` split (matches the existing `Accounts_AccessProcs.sql` convention) — SQL Server converts implicitly.
- Stored-proc parameter widths must match the `[ColumnMapping(MaxLength = N)]` value in `Money.cs`/`Money_Loans.cs` exactly (a mismatch here was one of the issue #22 whole-branch review findings).
- `SqlServerStoredProcDatabaseTests.cs` tests use `Assert.Ignore` (via `GetConnectionStringOrSkip()`) when `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` is unset — never make them fail in that case. `SqlServerDatabaseContractTests.cs` deliberately does the opposite (fails hard) — do not touch that fixture's skip behavior.
- Test row IDs use the `999xxx` range already established by the existing smoke tests, one unused number per new entity (999003–999007 are free; 999001/999002 are already used by Payee/Account/Alias tests).

---

## Task 1: Promote the six entities' base-class Read/Update methods to `public virtual`

**Files:**
- Modify: `Source\WPF\MyMoney.Data\SqlDatabase.cs:1316` (`ReadOnlineAccounts`), `:1357` (`UpdateOnlineAccounts`), `:1534` (`ReadAccountAliases`), `:1564` (`ReadTransactionExtras`), `:1740` (`UpdateAccountAliases`), `:1814` (`UpdateTransactionExtras`), `:1913` (`ReadRentBuildings`), `:1959` (`UpdateRentBuildings`), `:2113` (`ReadRentUnits`), `:2136` (`UpdateRentUnits`), `:2216` (`ReadLoanPayments`), `:2249` (`UpdateLoanPayments`)

**Interfaces:**
- Produces: twelve `public virtual` methods on `SqlServerDatabase` (via its base `SqlDatabase`) that Tasks 2–6 will `override` in `SqlServerStoredProcDatabase`. Signatures (unchanged from today, only the modifier changes):
  - `public virtual void ReadOnlineAccounts(OnlineAccounts onlineAccounts, MyMoney money)`
  - `public virtual void UpdateOnlineAccounts(OnlineAccounts accounts)`
  - `public virtual void ReadAccountAliases(AccountAliases accountAliases, MyMoney money)` (was `private`)
  - `public virtual void ReadTransactionExtras(TransactionExtras extras, MyMoney money)` (was `private`)
  - `public virtual void UpdateAccountAliases(AccountAliases accountAliases)` (was `private`)
  - `public virtual void UpdateTransactionExtras(TransactionExtras extras)` (was `private`)
  - `public virtual void ReadRentBuildings(RentBuildings collection, MyMoney money)`
  - `public virtual void UpdateRentBuildings(RentBuildings buildings)`
  - `public virtual void ReadRentUnits(RentUnits collection, MyMoney money)`
  - `public virtual void UpdateRentUnits(RentUnits units)`
  - `public virtual void ReadLoanPayments(LoanPayments collection, MyMoney money)`
  - `public virtual void UpdateLoanPayments(LoanPayments loans)`

- [ ] **Step 1: Change all twelve method signatures**

In `Source\WPF\MyMoney.Data\SqlDatabase.cs`, make these exact replacements (each `old_string` is unique in the file):

```
old: public void ReadOnlineAccounts(OnlineAccounts onlineAccounts, MyMoney money)
new: public virtual void ReadOnlineAccounts(OnlineAccounts onlineAccounts, MyMoney money)

old: public void UpdateOnlineAccounts(OnlineAccounts accounts)
new: public virtual void UpdateOnlineAccounts(OnlineAccounts accounts)

old: private void ReadAccountAliases(AccountAliases accountAliases, MyMoney money)
new: public virtual void ReadAccountAliases(AccountAliases accountAliases, MyMoney money)

old: private void ReadTransactionExtras(TransactionExtras extras, MyMoney money)
new: public virtual void ReadTransactionExtras(TransactionExtras extras, MyMoney money)

old: private void UpdateAccountAliases(AccountAliases accountAliases)
new: public virtual void UpdateAccountAliases(AccountAliases accountAliases)

old: private void UpdateTransactionExtras(TransactionExtras extras)
new: public virtual void UpdateTransactionExtras(TransactionExtras extras)

old: public void ReadRentBuildings(RentBuildings collection, MyMoney money)
new: public virtual void ReadRentBuildings(RentBuildings collection, MyMoney money)

old: public void UpdateRentBuildings(RentBuildings buildings)
new: public virtual void UpdateRentBuildings(RentBuildings buildings)

old: public void ReadRentUnits(RentUnits collection, MyMoney money)
new: public virtual void ReadRentUnits(RentUnits collection, MyMoney money)

old: public void UpdateRentUnits(RentUnits units)
new: public virtual void UpdateRentUnits(RentUnits units)

old: public void ReadLoanPayments(LoanPayments collection, MyMoney money)
new: public virtual void ReadLoanPayments(LoanPayments collection, MyMoney money)

old: public void UpdateLoanPayments(LoanPayments loans)
new: public virtual void UpdateLoanPayments(LoanPayments loans)
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: Build succeeds with 0 errors. (`private` → `public` on previously-private methods cannot break any existing caller since they were already only called via `this.` from within the same class.)

- [ ] **Step 3: Run the existing test suite to confirm zero regressions**

Run: `dotnet test Source/WPF/MyMoney.sln`
Expected: Same pass/fail/skip counts as before this change (this task changes no behavior, only accessibility/virtuality).

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlDatabase.cs
git commit -m "Promote issue #23 entities' base Read/Update methods to public virtual

Prerequisite for SqlServerStoredProcDatabase to override CRUD for
OnlineAccounts, AccountAliases, TransactionExtras, RentBuildings,
RentUnits, and LoanPayments the same way it already does for the
issue #22 entities.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 2: OnlineAccounts stored-proc CRUD

**Files:**
- Create: `Source\WPF\MyMoney.Data\SqlScripts\Access\OnlineAccounts_AccessProcs.sql`
- Modify: `Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs` (add overrides, update `Load()`)
- Modify: `Source\WPF\MyMoneyAdmin\BootstrapRunner.cs:103` (register the new proc file)
- Modify: `Source\WPF\UnitTests\SqlServerStoredProcDatabaseTests.cs` (add smoke test)

**Interfaces:**
- Consumes: `public virtual void ReadOnlineAccounts(OnlineAccounts, MyMoney)` / `UpdateOnlineAccounts(OnlineAccounts)` from Task 1; shared `private static void ExecuteProc(SqlConnection, string, params (string, object)[])` already in `SqlServerStoredProcDatabase.cs`; `OnlineAccounts.AddOnlineAccount(int id)` and `OnlineAccounts.FindOnlineAccount(string name)` from `Money.cs`.
- Produces: `dbo.OnlineAccounts_SelectAll/_Insert/_Update/_Delete` stored procs; `SqlServerStoredProcDatabase.ReadOnlineAccounts`/`UpdateOnlineAccounts` overrides; fixes the pre-existing bug where `Account.OnlineAccount` was silently always `null` after `SqlServerStoredProcDatabase.Load()` (because `OnlineAccounts` was never populated before `ReadAccounts` ran `money.OnlineAccounts.FindOnlineAccountAt(...)`).

- [ ] **Step 1: Create the access-procs SQL file**

Column order/widths come from `OnlineAccount`'s `[ColumnMapping]` attributes (`Source\WPF\MyMoney.Business\Money.cs:3466-3829`) and the base class's existing `SELECT` list (`SqlDatabase.cs:1319`).

Write `Source\WPF\MyMoney.Data\SqlScripts\Access\OnlineAccounts_AccessProcs.sql`:

```sql
-- OnlineAccounts_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the OnlineAccounts table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId, BrokerId,
           OfxVersion, LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken,
           AccessKey, UserKey, UserKeyExpireDate
    FROM dbo.OnlineAccounts ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_Insert
    @Id INT, @Name NVARCHAR(80), @Institution NVARCHAR(80), @OFX NVARCHAR(255), @FID NVARCHAR(50),
    @UserId NVARCHAR(20), @Password NVARCHAR(50), @BankId NVARCHAR(50), @BranchId NVARCHAR(50),
    @BrokerId NVARCHAR(50), @OfxVersion NVARCHAR(10), @LogoUrl NVARCHAR(1000), @AppId NVARCHAR(10),
    @AppVersion NVARCHAR(10), @ClientUid NVARCHAR(36), @UserCred1 NVARCHAR(200), @UserCred2 NVARCHAR(200),
    @AuthToken NVARCHAR(200), @AccessKey NVARCHAR(36), @UserKey NVARCHAR(64), @UserKeyExpireDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.OnlineAccounts (Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId,
        BrokerId, OfxVersion, LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken,
        AccessKey, UserKey, UserKeyExpireDate)
    VALUES (@Id, @Name, @Institution, @OFX, @FID, @UserId, @Password, @BankId, @BranchId, @BrokerId,
        @OfxVersion, @LogoUrl, @AppId, @AppVersion, @ClientUid, @UserCred1, @UserCred2, @AuthToken,
        @AccessKey, @UserKey, @UserKeyExpireDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_Update
    @Id INT, @Name NVARCHAR(80), @Institution NVARCHAR(80), @OFX NVARCHAR(255), @FID NVARCHAR(50),
    @UserId NVARCHAR(20), @Password NVARCHAR(50), @BankId NVARCHAR(50), @BranchId NVARCHAR(50),
    @BrokerId NVARCHAR(50), @OfxVersion NVARCHAR(10), @LogoUrl NVARCHAR(1000), @AppId NVARCHAR(10),
    @AppVersion NVARCHAR(10), @ClientUid NVARCHAR(36), @UserCred1 NVARCHAR(200), @UserCred2 NVARCHAR(200),
    @AuthToken NVARCHAR(200), @AccessKey NVARCHAR(36), @UserKey NVARCHAR(64), @UserKeyExpireDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OnlineAccounts SET
        Name = @Name, Institution = @Institution, OFX = @OFX, FID = @FID, UserId = @UserId,
        Password = @Password, BankId = @BankId, BranchId = @BranchId, BrokerId = @BrokerId,
        OfxVersion = @OfxVersion, LogoUrl = @LogoUrl, AppId = @AppId, AppVersion = @AppVersion,
        ClientUid = @ClientUid, UserCred1 = @UserCred1, UserCred2 = @UserCred2, AuthToken = @AuthToken,
        AccessKey = @AccessKey, UserKey = @UserKey, UserKeyExpireDate = @UserKeyExpireDate
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.OnlineAccounts WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.OnlineAccounts_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.OnlineAccounts_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.OnlineAccounts_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.OnlineAccounts_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.OnlineAccounts_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 2: Add the C# overrides to `SqlServerStoredProcDatabase.cs`**

Insert these two methods right after `GetConnectionString` (i.e. immediately before the `Load()` override), matching the existing file's style:

```csharp
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

```

- [ ] **Step 3: Wire `ReadOnlineAccounts` into `Load()`**

`ReadOnlineAccounts` must run before `ReadAccounts` (which resolves `Account.OnlineAccount` via `money.OnlineAccounts.FindOnlineAccountAt(...)` — today this always resolves to `null` because `OnlineAccounts` is never populated). Edit the `Load()` override:

```
old:
                this.ReadPayees(money.Payees, money);
                this.ReadAliases(money.Aliases, money);
                this.ReadCategories(money.Categories, money);
                this.ReadAccounts(money.Accounts, money);
new:
                this.ReadOnlineAccounts(money.OnlineAccounts, money);
                this.ReadPayees(money.Payees, money);
                this.ReadAliases(money.Aliases, money);
                this.ReadCategories(money.Categories, money);
                this.ReadAccounts(money.Accounts, money);
```

Also update the class doc comment (top of file) and the `Load()` doc comment to stop saying OnlineAccounts is out of scope — but leave that edit for Task 7 (final cleanup), since Tasks 3–6 still need to update the same comment block and doing it once at the end avoids repeated edits to the same lines.

- [ ] **Step 4: Add the smoke test**

Append to `Source\WPF\UnitTests\SqlServerStoredProcDatabaseTests.cs`, inside the `SqlServerStoredProcDatabaseTests` class, after `InsertUpdateDeleteInvestment_RoundTripsThroughStoredProcedures`:

```csharp
        [Test]
        public void InsertUpdateDeleteOnlineAccount_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var onlineAccount = money.OnlineAccounts.AddOnlineAccount(999003);
            onlineAccount.Name = "SqlServerStoredProcDatabaseTests OnlineAccount";
            onlineAccount.Institution = "Test Bank";
            onlineAccount.FID = "12345";
            onlineAccount.OnInserted();

            db.UpdateOnlineAccounts(money.OnlineAccounts);

            var reloaded = new MyMoney();
            db.ReadOnlineAccounts(reloaded.OnlineAccounts, reloaded);
            var found = reloaded.OnlineAccounts.FindOnlineAccount("SqlServerStoredProcDatabaseTests OnlineAccount");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Institution, Is.EqualTo("Test Bank"));
            Assert.That(found.FID, Is.EqualTo("12345"));

            found.Institution = "Updated Bank";
            db.UpdateOnlineAccounts(reloaded.OnlineAccounts);

            var afterUpdate = new MyMoney();
            db.ReadOnlineAccounts(afterUpdate.OnlineAccounts, afterUpdate);
            var updated = afterUpdate.OnlineAccounts.FindOnlineAccount("SqlServerStoredProcDatabaseTests OnlineAccount");
            Assert.That(updated.Institution, Is.EqualTo("Updated Bank"));

            updated.OnDelete();
            var toDelete = new OnlineAccounts(afterUpdate);
            toDelete.Add(updated);
            db.UpdateOnlineAccounts(toDelete);

            var afterDelete = new MyMoney();
            db.ReadOnlineAccounts(afterDelete.OnlineAccounts, afterDelete);
            Assert.That(afterDelete.OnlineAccounts.FindOnlineAccount("SqlServerStoredProcDatabaseTests OnlineAccount"), Is.Null);
        }
```

- [ ] **Step 5: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors.

- [ ] **Step 6: Run the new test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=InsertUpdateDeleteOnlineAccount_RoundTripsThroughStoredProcedures"`
Expected: `Ignored` (1 skipped, 0 failed) if `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` is unset locally; `Passed` (1 passed, 0 failed) if a real SQL Server connection string is set. Either outcome is acceptable — 0 failures is the bar.

- [ ] **Step 7: Register the proc file in `BootstrapRunner`**

```
old: string[] accessProcs = { "Accounts_AccessProcs.sql", "Categories_AccessProcs.sql", "Currencies_AccessProcs.sql", "Securities_AccessProcs.sql", "StockSplits_AccessProcs.sql", "Aliases_AccessProcs.sql", "Transactions_AccessProcs.sql", "Splits_AccessProcs.sql", "Investments_AccessProcs.sql" };
new: string[] accessProcs = { "Accounts_AccessProcs.sql", "Categories_AccessProcs.sql", "Currencies_AccessProcs.sql", "Securities_AccessProcs.sql", "StockSplits_AccessProcs.sql", "Aliases_AccessProcs.sql", "Transactions_AccessProcs.sql", "Splits_AccessProcs.sql", "Investments_AccessProcs.sql", "OnlineAccounts_AccessProcs.sql" };
```

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/OnlineAccounts_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoneyAdmin/BootstrapRunner.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add OnlineAccounts stored-proc CRUD for MyMoneyUser (issue #23)

Also fixes a live bug: SqlServerStoredProcDatabase.Load() never
populated OnlineAccounts before reading Accounts, so
Account.OnlineAccount silently resolved to null for every account.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 3: AccountAliases stored-proc CRUD

**Files:**
- Create: `Source\WPF\MyMoney.Data\SqlScripts\Access\AccountAliases_AccessProcs.sql`
- Modify: `Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs`
- Modify: `Source\WPF\MyMoneyAdmin\BootstrapRunner.cs`
- Modify: `Source\WPF\UnitTests\SqlServerStoredProcDatabaseTests.cs`

**Interfaces:**
- Consumes: `ReadAccountAliases`/`UpdateAccountAliases` virtuals from Task 1; `AccountAliases.AddAlias(int id)` and `AccountAliases.FindAlias(string pattern)` (`Money.cs:4197`).
- Produces: `dbo.AccountAliases_SelectAll/_Insert/_Update/_Delete`; `SqlServerStoredProcDatabase.ReadAccountAliases`/`UpdateAccountAliases` overrides.

- [ ] **Step 1: Create the access-procs SQL file**

Columns from `AccountAlias` (`Money.cs:5398-5471`) and base `SELECT` (`SqlDatabase.cs:1536`): `Id, Pattern, AccountId, Flags`. `AccountId` here is the external account-number string (`AccountAlias.AccountId`, `MaxLength=20`), matched against `Account.AccountId` — not the int primary key.

Write `Source\WPF\MyMoney.Data\SqlScripts\Access\AccountAliases_AccessProcs.sql`:

```sql
-- AccountAliases_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the AccountAliases table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Pattern, AccountId, Flags FROM dbo.AccountAliases ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_Insert
    @Id INT, @Pattern NVARCHAR(255), @AccountId NVARCHAR(20), @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.AccountAliases (Id, Pattern, AccountId, Flags) VALUES (@Id, @Pattern, @AccountId, @Flags);
END
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_Update
    @Id INT, @Pattern NVARCHAR(255), @AccountId NVARCHAR(20), @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.AccountAliases SET Pattern = @Pattern, AccountId = @AccountId, Flags = @Flags WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.AccountAliases WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.AccountAliases_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.AccountAliases_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.AccountAliases_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.AccountAliases_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.AccountAliases_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.AccountAliases_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.AccountAliases_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.AccountAliases_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 2: Add the C# overrides**

Insert into `SqlServerStoredProcDatabase.cs`, after the `UpdateOnlineAccounts` method added in Task 2:

```csharp
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

```

- [ ] **Step 3: Wire `ReadAccountAliases` into `Load()`**

```
old:
                this.ReadOnlineAccounts(money.OnlineAccounts, money);
                this.ReadPayees(money.Payees, money);
                this.ReadAliases(money.Aliases, money);
                this.ReadCategories(money.Categories, money);
new:
                this.ReadOnlineAccounts(money.OnlineAccounts, money);
                this.ReadPayees(money.Payees, money);
                this.ReadAliases(money.Aliases, money);
                this.ReadAccountAliases(money.AccountAliases, money);
                this.ReadCategories(money.Categories, money);
```

- [ ] **Step 4: Add the smoke test**

Append to `SqlServerStoredProcDatabaseTests.cs`, after the `InsertUpdateDeleteOnlineAccount_RoundTripsThroughStoredProcedures` test added in Task 2:

```csharp
        [Test]
        public void InsertUpdateDeleteAccountAlias_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var alias = money.AccountAliases.AddAlias(999004);
            alias.Pattern = "SqlServerStoredProcDatabaseTests AccountAlias Pattern";
            alias.AccountId = "ACCT12345";
            alias.AliasType = AliasType.None;
            alias.OnInserted();

            db.UpdateAccountAliases(money.AccountAliases);

            var reloaded = new MyMoney();
            db.ReadAccountAliases(reloaded.AccountAliases, reloaded);
            var found = reloaded.AccountAliases.FindAlias("SqlServerStoredProcDatabaseTests AccountAlias Pattern");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.AccountId, Is.EqualTo("ACCT12345"));

            found.AliasType = AliasType.Regex;
            db.UpdateAccountAliases(reloaded.AccountAliases);

            var afterUpdate = new MyMoney();
            db.ReadAccountAliases(afterUpdate.AccountAliases, afterUpdate);
            var updated = afterUpdate.AccountAliases.FindAlias("SqlServerStoredProcDatabaseTests AccountAlias Pattern");
            Assert.That(updated.AliasType, Is.EqualTo(AliasType.Regex));

            updated.OnDelete();
            var toDelete = new AccountAliases(afterUpdate);
            toDelete.Add(updated);
            db.UpdateAccountAliases(toDelete);

            var afterDelete = new MyMoney();
            db.ReadAccountAliases(afterDelete.AccountAliases, afterDelete);
            Assert.That(afterDelete.AccountAliases.FindAlias("SqlServerStoredProcDatabaseTests AccountAlias Pattern"), Is.Null);
        }
```

- [ ] **Step 5: Build**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.

- [ ] **Step 6: Run the new test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=InsertUpdateDeleteAccountAlias_RoundTripsThroughStoredProcedures"`
Expected: `Ignored` or `Passed`, 0 failed.

- [ ] **Step 7: Register the proc file in `BootstrapRunner`**

```
old: "Investments_AccessProcs.sql", "OnlineAccounts_AccessProcs.sql" };
new: "Investments_AccessProcs.sql", "OnlineAccounts_AccessProcs.sql", "AccountAliases_AccessProcs.sql" };
```

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/AccountAliases_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoneyAdmin/BootstrapRunner.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add AccountAliases stored-proc CRUD for MyMoneyUser (issue #23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 4: TransactionExtras stored-proc CRUD

**Files:**
- Create: `Source\WPF\MyMoney.Data\SqlScripts\Access\TransactionExtras_AccessProcs.sql`
- Modify: `Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs`
- Modify: `Source\WPF\MyMoneyAdmin\BootstrapRunner.cs`
- Modify: `Source\WPF\UnitTests\SqlServerStoredProcDatabaseTests.cs`

**Interfaces:**
- Consumes: `ReadTransactionExtras`/`UpdateTransactionExtras` virtuals from Task 1; `TransactionExtras.AddExtra(int id)` and `TransactionExtras.FindByTransaction(long transactionId)` (`Money.cs:5606`, `:5646`).
- Produces: `dbo.TransactionExtras_SelectAll/_Insert/_Update/_Delete`; `SqlServerStoredProcDatabase.ReadTransactionExtras`/`UpdateTransactionExtras` overrides.

- [ ] **Step 1: Create the access-procs SQL file**

Columns from `TransactionExtra` (`Money.cs:5478-5553`) and base `SELECT` (`SqlDatabase.cs:1566`): `Id, Transaction, TaxYear, TaxDate`. `Transaction` is a raw `bigint` FK to `Transaction.Id` (no object resolution at read time).

Write `Source\WPF\MyMoney.Data\SqlScripts\Access\TransactionExtras_AccessProcs.sql`:

```sql
-- TransactionExtras_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the TransactionExtras table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Transaction], [TaxYear], [TaxDate] FROM dbo.TransactionExtras ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_Insert
    @Id INT, @Transaction BIGINT, @TaxYear INT, @TaxDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.TransactionExtras ([Id], [Transaction], [TaxYear], [TaxDate]) VALUES (@Id, @Transaction, @TaxYear, @TaxDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_Update
    @Id INT, @Transaction BIGINT, @TaxYear INT, @TaxDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TransactionExtras SET [Transaction] = @Transaction, [TaxYear] = @TaxYear, [TaxDate] = @TaxDate WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.TransactionExtras WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.TransactionExtras_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.TransactionExtras_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.TransactionExtras_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.TransactionExtras_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.TransactionExtras_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.TransactionExtras_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.TransactionExtras_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.TransactionExtras_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 2: Add the C# overrides**

Insert into `SqlServerStoredProcDatabase.cs`, after `UpdateAccountAliases` from Task 3:

```csharp
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

```

- [ ] **Step 3: Wire `ReadTransactionExtras` into `Load()`**

`TransactionExtra.Transaction` is a raw `long`, not resolved to a `Transaction` object at read time, so this has no ordering dependency on `ReadTransactions` — but for readability it's placed right after it:

```
old:
                this.ReadStockSplits(money.StockSplits, money);
                this.ReadTransactions(money.Transactions, money);
            }
new:
                this.ReadStockSplits(money.StockSplits, money);
                this.ReadTransactions(money.Transactions, money);
                this.ReadTransactionExtras(money.TransactionExtras, money);
            }
```

- [ ] **Step 4: Add the smoke test**

Append to `SqlServerStoredProcDatabaseTests.cs`, after the `InsertUpdateDeleteAccountAlias_RoundTripsThroughStoredProcedures` test:

```csharp
        [Test]
        public void InsertUpdateDeleteTransactionExtra_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests TransactionExtra Account");
            account.Type = AccountType.Checking;
            account.OnInserted();
            db.UpdateAccounts(money.Accounts);

            var transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2026, 1, 15);
            transaction.Amount = -10.00m;
            money.Transactions.AddTransaction(transaction);
            db.UpdateTransactions(money.Transactions);

            var extra = money.TransactionExtras.AddExtra(999005);
            extra.Transaction = transaction.Id;
            extra.TaxYear = 2026;
            extra.TaxDate = new DateTime(2026, 4, 15);
            extra.OnInserted();
            db.UpdateTransactionExtras(money.TransactionExtras);

            var reloaded = new MyMoney();
            db.ReadTransactionExtras(reloaded.TransactionExtras, reloaded);
            var found = reloaded.TransactionExtras.FindByTransaction(transaction.Id);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.TaxYear, Is.EqualTo(2026));

            found.TaxYear = 2027;
            db.UpdateTransactionExtras(reloaded.TransactionExtras);

            var afterUpdate = new MyMoney();
            db.ReadTransactionExtras(afterUpdate.TransactionExtras, afterUpdate);
            var updated = afterUpdate.TransactionExtras.FindByTransaction(transaction.Id);
            Assert.That(updated.TaxYear, Is.EqualTo(2027));

            updated.OnDelete();
            var toDelete = new TransactionExtras(afterUpdate);
            toDelete.Add(updated);
            db.UpdateTransactionExtras(toDelete);

            var afterDelete = new MyMoney();
            db.ReadTransactionExtras(afterDelete.TransactionExtras, afterDelete);
            Assert.That(afterDelete.TransactionExtras.FindByTransaction(transaction.Id), Is.Null);

            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            db.ReadTransactions(afterDelete.Transactions, afterDelete);
            var cleanupAccount = afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests TransactionExtra Account");
            var cleanupTransaction = afterDelete.Transactions.GetTransactionsFrom(cleanupAccount).FirstOrDefault();
            cleanupTransaction.OnDelete();
            var transactionsToDelete = new Transactions(afterDelete);
            transactionsToDelete.AddTransaction(cleanupTransaction);
            db.UpdateTransactions(transactionsToDelete);

            cleanupAccount.OnDelete();
            var accountsToDelete = new Accounts(afterDelete);
            accountsToDelete.Add(cleanupAccount);
            db.UpdateAccounts(accountsToDelete);
        }
```

- [ ] **Step 5: Build**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.

- [ ] **Step 6: Run the new test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=InsertUpdateDeleteTransactionExtra_RoundTripsThroughStoredProcedures"`
Expected: `Ignored` or `Passed`, 0 failed.

- [ ] **Step 7: Register the proc file in `BootstrapRunner`**

```
old: "OnlineAccounts_AccessProcs.sql", "AccountAliases_AccessProcs.sql" };
new: "OnlineAccounts_AccessProcs.sql", "AccountAliases_AccessProcs.sql", "TransactionExtras_AccessProcs.sql" };
```

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/TransactionExtras_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoneyAdmin/BootstrapRunner.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add TransactionExtras stored-proc CRUD for MyMoneyUser (issue #23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 5: LoanPayments stored-proc CRUD

**Files:**
- Create: `Source\WPF\MyMoney.Data\SqlScripts\Access\LoanPayments_AccessProcs.sql`
- Modify: `Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs`
- Modify: `Source\WPF\MyMoneyAdmin\BootstrapRunner.cs`
- Modify: `Source\WPF\UnitTests\SqlServerStoredProcDatabaseTests.cs`

**Interfaces:**
- Consumes: `ReadLoanPayments`/`UpdateLoanPayments` virtuals from Task 1; `LoanPayments.AddLoan(LoanPayment x)` and `LoanPayments.GetList()` (`Money_Loans.cs:355`, `:397`); `MyMoney.GetOrCreateLoanAccount(Account)` (called for every `Account.Type == AccountType.Loan` after loading, mirroring the base class).
- Produces: `dbo.LoanPayments_SelectAll/_Insert/_Update/_Delete`; `SqlServerStoredProcDatabase.ReadLoanPayments`/`UpdateLoanPayments` overrides.
- **Ordering requirement:** `ReadLoanPayments` must run after `ReadAccounts` — its final step matches `money.Accounts` for `AccountType.Loan` accounts and calls `money.GetOrCreateLoanAccount(a)`. `LoanPayment.AccountId` itself is a raw `int` (no FK object resolution at read time), same as `TransactionExtra.Transaction`.

- [ ] **Step 1: Create the access-procs SQL file**

Columns from `LoanPayment` (`Money_Loans.cs:464-572`) and base `SELECT` (`SqlDatabase.cs:2219`): `Id, AccountId, Date, Principal, Interest, Memo`.

Write `Source\WPF\MyMoney.Data\SqlScripts\Access\LoanPayments_AccessProcs.sql`:

```sql
-- LoanPayments_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the LoanPayments table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, AccountId, Date, Principal, Interest, Memo FROM dbo.LoanPayments ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_Insert
    @Id INT, @AccountId INT, @Date DATETIME, @Principal MONEY, @Interest MONEY, @Memo NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.LoanPayments (Id, AccountId, Date, Principal, Interest, Memo) VALUES (@Id, @AccountId, @Date, @Principal, @Interest, @Memo);
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_Update
    @Id INT, @AccountId INT, @Date DATETIME, @Principal MONEY, @Interest MONEY, @Memo NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.LoanPayments SET AccountId = @AccountId, Date = @Date, Principal = @Principal, Interest = @Interest, Memo = @Memo WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.LoanPayments WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.LoanPayments_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.LoanPayments_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.LoanPayments_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.LoanPayments_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.LoanPayments_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 2: Add the C# overrides**

Insert into `SqlServerStoredProcDatabase.cs`, after `UpdateTransactionExtras` from Task 4:

```csharp
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

```

- [ ] **Step 3: Wire `ReadLoanPayments` into `Load()`**

```
old:
                this.ReadTransactionExtras(money.TransactionExtras, money);
            }
new:
                this.ReadTransactionExtras(money.TransactionExtras, money);
                this.ReadLoanPayments(money.LoanPayments, money);
            }
```

- [ ] **Step 4: Add the smoke test**

Append to `SqlServerStoredProcDatabaseTests.cs`, after the `InsertUpdateDeleteTransactionExtra_RoundTripsThroughStoredProcedures` test:

```csharp
        [Test]
        public void InsertUpdateDeleteLoanPayment_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests LoanPayment Account");
            account.Type = AccountType.Loan;
            account.OnInserted();
            db.UpdateAccounts(money.Accounts);

            var loanPayment = new LoanPayment(money.LoanPayments)
            {
                Id = 999006,
                AccountId = account.Id,
                Date = new DateTime(2026, 1, 1),
                Principal = 100.00m,
                Interest = 25.00m,
                Memo = "SqlServerStoredProcDatabaseTests LoanPayment"
            };
            money.LoanPayments.AddLoan(loanPayment);
            loanPayment.OnInserted();
            db.UpdateLoanPayments(money.LoanPayments);

            var reloaded = new MyMoney();
            db.ReadAccounts(reloaded.Accounts, reloaded);
            db.ReadLoanPayments(reloaded.LoanPayments, reloaded);
            var found = reloaded.LoanPayments.GetList().FirstOrDefault(x => x.Memo == "SqlServerStoredProcDatabaseTests LoanPayment");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Principal, Is.EqualTo(100.00m));

            found.Principal = 150.00m;
            db.UpdateLoanPayments(reloaded.LoanPayments);

            var afterUpdate = new MyMoney();
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            db.ReadLoanPayments(afterUpdate.LoanPayments, afterUpdate);
            var updated = afterUpdate.LoanPayments.GetList().FirstOrDefault(x => x.Memo == "SqlServerStoredProcDatabaseTests LoanPayment");
            Assert.That(updated.Principal, Is.EqualTo(150.00m));

            updated.OnDelete();
            var toDelete = new LoanPayments(afterUpdate);
            toDelete.Add(updated);
            db.UpdateLoanPayments(toDelete);

            var afterDelete = new MyMoney();
            db.ReadLoanPayments(afterDelete.LoanPayments, afterDelete);
            Assert.That(afterDelete.LoanPayments.GetList().Any(x => x.Memo == "SqlServerStoredProcDatabaseTests LoanPayment"), Is.False);

            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            var cleanupAccount = afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests LoanPayment Account");
            cleanupAccount.OnDelete();
            var accountsToDelete = new Accounts(afterDelete);
            accountsToDelete.Add(cleanupAccount);
            db.UpdateAccounts(accountsToDelete);
        }
```

- [ ] **Step 5: Build**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.

- [ ] **Step 6: Run the new test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=InsertUpdateDeleteLoanPayment_RoundTripsThroughStoredProcedures"`
Expected: `Ignored` or `Passed`, 0 failed.

- [ ] **Step 7: Register the proc file in `BootstrapRunner`**

```
old: "AccountAliases_AccessProcs.sql", "TransactionExtras_AccessProcs.sql" };
new: "AccountAliases_AccessProcs.sql", "TransactionExtras_AccessProcs.sql", "LoanPayments_AccessProcs.sql" };
```

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/LoanPayments_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoneyAdmin/BootstrapRunner.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add LoanPayments stored-proc CRUD for MyMoneyUser (issue #23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 6: RentBuildings + RentUnits stored-proc CRUD

**Files:**
- Create: `Source\WPF\MyMoney.Data\SqlScripts\Access\RentUnits_AccessProcs.sql`
- Create: `Source\WPF\MyMoney.Data\SqlScripts\Access\RentBuildings_AccessProcs.sql`
- Modify: `Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs` (also needs a new `using System.Linq;`)
- Modify: `Source\WPF\MyMoneyAdmin\BootstrapRunner.cs`
- Modify: `Source\WPF\UnitTests\SqlServerStoredProcDatabaseTests.cs`

**Interfaces:**
- Consumes: `ReadRentUnits`/`UpdateRentUnits`/`ReadRentBuildings`/`UpdateRentBuildings` virtuals from Task 1; `RentUnits.AddRentUnit(RentUnit x)` and `.Get(int id)` (`Money.cs:7004`, `:7036`); `RentBuildings.AddRentBuilding(RentBuilding r)` and `.FindByName(string name)` (`Money.cs:6544`, `:6569`); `RentBuildings.Units` property.
- Produces: `dbo.RentUnits_SelectAll/_Insert/_Update/_Delete`, `dbo.RentBuildings_SelectAll/_Insert/_Update/_Delete`; `SqlServerStoredProcDatabase.ReadRentUnits`/`UpdateRentUnits`/`ReadRentBuildings`/`UpdateRentBuildings` overrides.
- **Combined into one task** because `RentBuilding.Units` is populated by matching `RentUnit.Building == RentBuilding.Id` at read time — `ReadRentBuildings` calls `this.ReadRentUnits(collection.Units, money)` as its first line, exactly like the base class.

- [ ] **Step 1: Create the `RentUnits` access-procs SQL file**

Columns from `RentUnit` (`Money.cs:6837-6929`) and base `SELECT` (`SqlDatabase.cs:2116`): `Id, Building, Name, Renter, Note`. `Id` is the primary key; `Building` is a plain FK int. The base class's `Update`/`Delete` procs filter on `Id AND Building` together (defensive double-key match) — mirror that exactly.

Write `Source\WPF\MyMoney.Data\SqlScripts\Access\RentUnits_AccessProcs.sql`:

```sql
-- RentUnits_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the RentUnits table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Building, Name, Renter, Note FROM dbo.RentUnits ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_Insert
    @Id INT, @Building INT, @Name NVARCHAR(255), @Renter NVARCHAR(255), @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.RentUnits (Id, Building, Name, Renter, Note) VALUES (@Id, @Building, @Name, @Renter, @Note);
END
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_Update
    @Id INT, @Building INT, @Name NVARCHAR(255), @Renter NVARCHAR(255), @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RentUnits SET Name = @Name, Renter = @Renter, Note = @Note WHERE Id = @Id AND Building = @Building;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_Delete
    @Id INT, @Building INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.RentUnits WHERE Id = @Id AND Building = @Building;
END
GO

GRANT EXECUTE ON dbo.RentUnits_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentUnits_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentUnits_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentUnits_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.RentUnits_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentUnits_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentUnits_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentUnits_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 2: Create the `RentBuildings` access-procs SQL file**

Columns from `RentBuilding` (`Money.cs:6021-6335`) and base `SELECT` (`SqlDatabase.cs:1919-1920`): `Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue, CategoryForIncome, CategoryForTaxes, CategoryForInterest, CategoryForRepairs, CategoryForMaintenance, CategoryForManagement, OwnershipName1, OwnershipName2, OwnershipPercentage1, OwnershipPercentage2, Note`.

Write `Source\WPF\MyMoney.Data\SqlScripts\Access\RentBuildings_AccessProcs.sql`:

```sql
-- RentBuildings_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the RentBuildings table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue,
           CategoryForIncome, CategoryForTaxes, CategoryForInterest, CategoryForRepairs,
           CategoryForMaintenance, CategoryForManagement, OwnershipName1, OwnershipName2,
           OwnershipPercentage1, OwnershipPercentage2, Note
    FROM dbo.RentBuildings ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_Insert
    @Id INT, @Name NVARCHAR(255), @Address NVARCHAR(255), @PurchasedDate DATETIME, @PurchasedPrice MONEY,
    @LandValue MONEY, @EstimatedValue MONEY, @CategoryForIncome INT, @CategoryForTaxes INT,
    @CategoryForInterest INT, @CategoryForRepairs INT, @CategoryForMaintenance INT, @CategoryForManagement INT,
    @OwnershipName1 NVARCHAR(255), @OwnershipName2 NVARCHAR(255), @OwnershipPercentage1 MONEY,
    @OwnershipPercentage2 MONEY, @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.RentBuildings (Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue,
        CategoryForIncome, CategoryForTaxes, CategoryForInterest, CategoryForRepairs, CategoryForMaintenance,
        CategoryForManagement, OwnershipName1, OwnershipName2, OwnershipPercentage1, OwnershipPercentage2, Note)
    VALUES (@Id, @Name, @Address, @PurchasedDate, @PurchasedPrice, @LandValue, @EstimatedValue,
        @CategoryForIncome, @CategoryForTaxes, @CategoryForInterest, @CategoryForRepairs, @CategoryForMaintenance,
        @CategoryForManagement, @OwnershipName1, @OwnershipName2, @OwnershipPercentage1, @OwnershipPercentage2, @Note);
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_Update
    @Id INT, @Name NVARCHAR(255), @Address NVARCHAR(255), @PurchasedDate DATETIME, @PurchasedPrice MONEY,
    @LandValue MONEY, @EstimatedValue MONEY, @CategoryForIncome INT, @CategoryForTaxes INT,
    @CategoryForInterest INT, @CategoryForRepairs INT, @CategoryForMaintenance INT, @CategoryForManagement INT,
    @OwnershipName1 NVARCHAR(255), @OwnershipName2 NVARCHAR(255), @OwnershipPercentage1 MONEY,
    @OwnershipPercentage2 MONEY, @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RentBuildings SET
        Name = @Name, Address = @Address, PurchasedDate = @PurchasedDate, PurchasedPrice = @PurchasedPrice,
        LandValue = @LandValue, EstimatedValue = @EstimatedValue, CategoryForIncome = @CategoryForIncome,
        CategoryForTaxes = @CategoryForTaxes, CategoryForInterest = @CategoryForInterest,
        CategoryForRepairs = @CategoryForRepairs, CategoryForMaintenance = @CategoryForMaintenance,
        CategoryForManagement = @CategoryForManagement, OwnershipName1 = @OwnershipName1,
        OwnershipName2 = @OwnershipName2, OwnershipPercentage1 = @OwnershipPercentage1,
        OwnershipPercentage2 = @OwnershipPercentage2, Note = @Note
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.RentBuildings WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.RentBuildings_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.RentBuildings_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentBuildings_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentBuildings_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentBuildings_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 3: Add `using System.Linq;` to `SqlServerStoredProcDatabase.cs`**

`ReadRentBuildings` (Step 4 below) needs `.Where`/`.OrderBy` on `RentUnits.GetList()`, and this file currently has no `System.Linq` import:

```
old:
using System;
using System.Collections;
using System.Data;
using System.Data.SqlTypes;
using Microsoft.Data.SqlClient;
using Walkabout.Utilities;
new:
using System;
using System.Collections;
using System.Data;
using System.Data.SqlTypes;
using System.Linq;
using Microsoft.Data.SqlClient;
using Walkabout.Utilities;
```

- [ ] **Step 4: Add the C# overrides**

Insert into `SqlServerStoredProcDatabase.cs`, after `UpdateLoanPayments` from Task 5:

```csharp
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

```

- [ ] **Step 5: Wire `ReadRentBuildings` into `Load()`**

This must be the last read (it internally reads `RentUnits` first, then matches units to buildings using `money.Buildings.Units`, matching the base class's own convention):

```
old:
                this.ReadLoanPayments(money.LoanPayments, money);
            }
new:
                this.ReadLoanPayments(money.LoanPayments, money);
                this.ReadRentBuildings(money.Buildings, money);
            }
```

- [ ] **Step 6: Add the smoke test**

Append to `SqlServerStoredProcDatabaseTests.cs`, after the `InsertUpdateDeleteLoanPayment_RoundTripsThroughStoredProcedures` test:

```csharp
        [Test]
        public void InsertUpdateDeleteRentBuildingAndUnit_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var building = new RentBuilding(money.Buildings)
            {
                Id = -1,
                Name = "SqlServerStoredProcDatabaseTests Building",
                Address = "123 Test St",
                PurchasedDate = new DateTime(2020, 1, 1),
                PurchasedPrice = 200000m,
                LandValue = 50000m,
                EstimatedValue = 250000m
            };
            money.Buildings.AddRentBuilding(building);
            building.OnInserted();

            var unit = new RentUnit(money.Buildings.Units) { Id = 999007, Building = building.Id, Name = "Unit A", Renter = "Test Renter" };
            money.Buildings.Units.AddRentUnit(unit);
            unit.OnInserted();

            db.UpdateRentUnits(money.Buildings.Units);
            db.UpdateRentBuildings(money.Buildings);

            var reloaded = new MyMoney();
            db.ReadRentBuildings(reloaded.Buildings, reloaded);
            var foundBuilding = reloaded.Buildings.FindByName("SqlServerStoredProcDatabaseTests Building");
            Assert.That(foundBuilding, Is.Not.Null);
            Assert.That(foundBuilding.Address, Is.EqualTo("123 Test St"));
            Assert.That(foundBuilding.Units.Count, Is.EqualTo(1));
            var foundUnit = reloaded.Buildings.Units.Get(999007);
            Assert.That(foundUnit, Is.Not.Null);
            Assert.That(foundUnit.Name, Is.EqualTo("Unit A"));

            foundBuilding.Address = "456 Updated Ave";
            foundUnit.Renter = "Updated Renter";
            db.UpdateRentUnits(reloaded.Buildings.Units);
            db.UpdateRentBuildings(reloaded.Buildings);

            var afterUpdate = new MyMoney();
            db.ReadRentBuildings(afterUpdate.Buildings, afterUpdate);
            var updatedBuilding = afterUpdate.Buildings.FindByName("SqlServerStoredProcDatabaseTests Building");
            Assert.That(updatedBuilding.Address, Is.EqualTo("456 Updated Ave"));
            var updatedUnit = afterUpdate.Buildings.Units.Get(999007);
            Assert.That(updatedUnit.Renter, Is.EqualTo("Updated Renter"));

            updatedUnit.OnDelete();
            var unitsToDelete = new RentUnits(afterUpdate);
            unitsToDelete.Add(updatedUnit);
            db.UpdateRentUnits(unitsToDelete);

            updatedBuilding.OnDelete();
            var buildingsToDelete = new RentBuildings(afterUpdate);
            buildingsToDelete.AddRentBuilding(updatedBuilding);
            db.UpdateRentBuildings(buildingsToDelete);

            var afterDelete = new MyMoney();
            db.ReadRentBuildings(afterDelete.Buildings, afterDelete);
            Assert.That(afterDelete.Buildings.FindByName("SqlServerStoredProcDatabaseTests Building"), Is.Null);
            Assert.That(afterDelete.Buildings.Units.Get(999007), Is.Null);
        }
```

- [ ] **Step 7: Build**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.

- [ ] **Step 8: Run the new test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=InsertUpdateDeleteRentBuildingAndUnit_RoundTripsThroughStoredProcedures"`
Expected: `Ignored` or `Passed`, 0 failed.

- [ ] **Step 9: Register the proc files in `BootstrapRunner`**

```
old: "TransactionExtras_AccessProcs.sql", "LoanPayments_AccessProcs.sql" };
new: "TransactionExtras_AccessProcs.sql", "LoanPayments_AccessProcs.sql", "RentUnits_AccessProcs.sql", "RentBuildings_AccessProcs.sql" };
```

- [ ] **Step 10: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/RentUnits_AccessProcs.sql Source/WPF/MyMoney.Data/SqlScripts/Access/RentBuildings_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoneyAdmin/BootstrapRunner.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add RentBuildings/RentUnits stored-proc CRUD for MyMoneyUser (issue #23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Task 7: Final cleanup — doc comments, BootstrapRunner log message, contract-test wipe list, full verification

**Files:**
- Modify: `Source\WPF\MyMoney.Data\SqlServerStoredProcDatabase.cs` (class doc comment, `Load()` doc comment)
- Modify: `Source\WPF\MyMoneyAdmin\BootstrapRunner.cs` (log message wording only)
- Modify: `Source\WPF\MyMoney.TestSupport\SqlServerDatabaseContractTests.cs` (`TablesToWipe`)

**Interfaces:**
- Consumes: nothing new — this task only updates comments/docs and the shared test fixture's cleanup list, using entities and methods added in Tasks 2–6.

- [ ] **Step 1: Update the class doc comment**

```
old:
    /// <summary>
    /// A SqlServerDatabase variant that performs CRUD exclusively through
    /// stored procedures, matching the grants given to the MyMoneyUser
    /// login (see Database/SqlScripts/Access/*_AccessProcs.sql). Covers
    /// Payees, Accounts, Categories, Currencies, Securities, StockSplits,
    /// Aliases, Transactions, Splits, and Investment -- the entities
    /// scoped to issue #22. OnlineAccounts, AccountAliases,
    /// TransactionExtras, RentBuildings, RentUnits, and LoanPayments are
    /// tracked separately as issue #23.
    /// </summary>
new:
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
```

- [ ] **Step 2: Update the `Load()` doc comment**

```
old:
        /// <summary>
        /// Overrides the base SqlServerDatabase.Load(), which starts with
        /// LazyCreateTables() (DDL against every [TableMapping] table) and
        /// then reads every table via raw SQL. MyMoneyUser has no direct
        /// table grants at all -- only EXECUTE on the *_AccessProcs.sql
        /// stored procedures -- so LazyCreateTables() and the base class's
        /// generic ReadXxx() methods would fail with a SQL Server
        /// permissions error. This override reads every entity in scope
        /// for issue #22 (Payees, Aliases, Categories, Accounts,
        /// Currencies, Securities, StockSplits, Transactions -- with
        /// Splits and Investment read inline inside ReadTransactions) via
        /// their dedicated stored procedures, in FK-safe dependency
        /// order. OnlineAccounts and the other issue #23 entities are
        /// still left empty.
        /// </summary>
new:
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
```

- [ ] **Step 3: Update the `BootstrapRunner` log message**

```
old: Console.WriteLine("Deploying issue #22 access procedures as 'MyMoneyAdmin'...");
new: Console.WriteLine("Deploying issue #22/#23 access procedures as 'MyMoneyAdmin'...");
```

Also update the preceding schema-creation message for accuracy:

```
old: Console.WriteLine("Creating schema for Accounts/Categories/Currencies/Securities/StockSplits/Aliases/Transactions/Splits/Investments as 'MyMoneyAdmin'...");
new: Console.WriteLine("Creating schema for every [TableMapping] table as 'MyMoneyAdmin'...");
```

- [ ] **Step 4: Add the six new tables to `SqlServerDatabaseContractTests.TablesToWipe`**

Order matches the existing "children before parents" convention (e.g. `Splits` before `Transactions`, `Transactions` before `Accounts`):

```
old:
        private static readonly string[] TablesToWipe =
        {
            "Splits", "Investments", "Transactions", "StockSplits", "Aliases",
            "Accounts", "Securities", "Currencies", "Categories", "Payees"
        };
new:
        private static readonly string[] TablesToWipe =
        {
            "Splits", "Investments", "Transactions", "TransactionExtras", "StockSplits", "Aliases",
            "AccountAliases", "LoanPayments", "RentUnits", "RentBuildings",
            "Accounts", "OnlineAccounts", "Securities", "Currencies", "Categories", "Payees"
        };
```

- [ ] **Step 5: Build**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Source/WPF/MyMoney.sln`
Expected: All tests that passed before this plan still pass; all six new `SqlServerStoredProcDatabaseTests` smoke tests are `Ignored` (no `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` set) or `Passed` (if set) — 0 new failures. If a real SQL Server is available, additionally run with `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`, `MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`, and `MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1` set so `SqlServerDatabaseContractTests` also exercises the updated wipe list end to end.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoneyAdmin/BootstrapRunner.cs Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs
git commit -m "Finish issue #23: update docs, BootstrapRunner log text, and contract-test wipe list

All six previously-deferred entities (OnlineAccounts, AccountAliases,
TransactionExtras, RentBuildings, RentUnits, LoanPayments) now have
full MyMoneyUser stored-proc CRUD coverage, matching issue #22's
pattern. MyMoneyUser now has CRUD parity with MyMoneyAdmin across the
entire schema.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Post-plan: whole-branch review

Issue #22 ended with a whole-branch review pass (7 findings, all fixed before merge — see `docs/superpowers/plans/2026-09-15-sql-server-user-tier-crud.md` and CLAUDE.md-adjacent memory). After Task 7, request a similar review (via `superpowers:requesting-code-review` or `/code-review`) covering the full diff before opening a PR against `MoneyTools/MyMoney.Net:master`, paying particular attention to:
- Parameter width/type agreement between each `*_AccessProcs.sql` and its `[ColumnMapping]` source (the exact bug class issue #22 found four instances of).
- Whether any other nchar-backed short string column needs `.TrimEnd()` beyond the ones identified in Tasks 2–3.
- Whether `LazyCreateTables()` actually produces schemas matching these procs' assumptions (only verifiable against a real SQL Server instance).
