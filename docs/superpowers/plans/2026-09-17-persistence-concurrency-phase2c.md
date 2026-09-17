# Persistence Concurrency — Phase 2c (SQL Server SaveOne/SaveBatch/SaveTransfer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `SqlServerStoredProcDatabase` a real `SaveOne`/`SaveBatch`/`SaveTransfer`
implementation covering all 11 `IAggregateRoot` types, so the already-shared
`DatabaseContractTests` suite (currently ~54 failing tests against `SqlServerDatabaseContractTests`)
passes unmodified — closing out full cross-engine parity (`MockDatabase`, `SqliteDatabase`,
`SqlServerStoredProcDatabase` all implementing the same contract identically).

**Architecture:** `SqlServerStoredProcDatabase` is stored-procedure-only (no direct table grants for
`MyMoneyUser`/`MyMoneyTest`), unlike `SqliteDatabase`'s direct parameterized SQL. Each entity gets a
new, additive `<Table>_SaveBatch` stored procedure that takes a whole batch as a Table-Valued
Parameter (TVP) and does the version-checked insert/update/delete as set-based T-SQL inside its own
`BEGIN TRANSACTION`/`COMMIT`/`ROLLBACK`. C# builds one `DataTable` per entity type in the batch and
calls that entity's proc once — no per-row round trips, and for the common single-type-group case,
no explicit client-side `SqlTransaction` at all (the proc owns atomicity server-side). A new,
distinctly-named `Version BIGINT` column (not SQL Server's existing, unused, native `RowVersion
timestamp` column — see Global Constraints) is the optimistic-concurrency mechanism, matching
`SqliteDatabase`'s `Version` column exactly.

**Tech Stack:** .NET 10 (`net10.0-windows7.0`), `Microsoft.Data.SqlClient`, NUnit 4.6.1, SQL Server
(live shared instance at `Redmond`, database `MyMoney`).

**Spec:** `docs/superpowers/specs/2026-09-17-persistence-concurrency-phase2c-design.md` (as amended
by the correction committed at `ca200e3`). Builds on `docs/superpowers/plans/
2026-09-16-persistence-concurrency-phase2b.md` (SQLite's proven pattern) and its entity-extension/
RentBuilding/Transaction follow-ups (merged, no separate plan docs — executed directly from
Phase 2b's established pattern).

## Global Constraints

- Additive only: `Save(MyMoney)`, every existing `UpdateXxx`/`ReadXxx` override, and every existing
  `_Insert`/`_Update`/`_Delete`/`_SelectAll` stored proc are untouched. The new `_SaveBatch` procs and
  the new `Version` column are a parallel, additive surface.
- **The new application-managed concurrency column is named `Version`, not `RowVersion`.** Every one
  of the 14 relevant tables already has an existing, unused `RowVersion timestamp NOT NULL` column
  (SQL Server's native rowversion type, confirmed live against Redmond as `MyMoneyAdmin`) — a
  different name is required to avoid colliding with it. Do not attempt to reuse or repurpose the
  existing `RowVersion` column; leave it alone.
- Live schema reference (queried live against Redmond as `MyMoneyAdmin` — `MyMoneyUser`/`MyMoneyTest`
  cannot see `INFORMATION_SCHEMA.COLUMNS` for tables they have no direct grant on, so always use the
  admin connection for any future live schema check against this database):

  ```
  Accounts: Id int NOT NULL, AccountId nchar(20), OfxAccountId nvarchar(50), Name nvarchar(80) NOT NULL,
    Description nvarchar(255), Type int NOT NULL, OpeningBalance money, Currency nchar(3),
    OnlineAccount int, WebSite nvarchar(512), ReconcileWarning int, LastSync datetime,
    SyncGuid uniqueidentifier, Flags int, LastBalance datetime, CategoryIdForPrincipal int,
    CategoryIdForInterest int, RowVersion timestamp NOT NULL
  Aliases: Id int NOT NULL, Pattern nvarchar(255) NOT NULL, Flags int NOT NULL, Payee int NOT NULL,
    RowVersion timestamp NOT NULL
  Categories: Id int NOT NULL, ParentId int, Name nvarchar(80) NOT NULL, Description nvarchar(255),
    Type int NOT NULL, Color nchar(10), Budget money, Balance money, Frequency int, TaxRefNum int,
    RowVersion timestamp NOT NULL
  Currencies: Id int NOT NULL, Symbol nchar(20) NOT NULL, CultureCode nvarchar(80),
    Name nvarchar(80) NOT NULL, Ratio money, LastRatio money, RowVersion timestamp NOT NULL
  Investments: Id bigint NOT NULL, Security int, UnitPrice money NOT NULL, Units money,
    Commission money, MarkUpDown money, Taxes money, Fees money, Load money,
    InvestmentType int NOT NULL, TradeType int, TaxExempt bit, Withholding money,
    RowVersion timestamp NOT NULL
  LoanPayments: Id int NOT NULL, AccountId int NOT NULL, Date datetime NOT NULL, Principal money,
    Interest money, Memo nvarchar(255), RowVersion timestamp NOT NULL
  OnlineAccounts: Id int NOT NULL, Name nvarchar(80) NOT NULL, Institution nvarchar(80),
    OFX nvarchar(255), OfxVersion nchar(10), FID nvarchar(50), UserId nchar(20),
    Password nvarchar(50), UserCred1 nvarchar(200), UserCred2 nvarchar(200), AuthToken nvarchar(200),
    BankId nvarchar(50), BranchId nvarchar(50), BrokerId nvarchar(50), LogoUrl nvarchar(1000),
    AppId nchar(10), AppVersion nchar(10), ClientUid nchar(36), AccessKey nchar(36),
    UserKey nvarchar(64), UserKeyExpireDate datetime, RowVersion timestamp NOT NULL
  Payees: Id int NOT NULL, Name nvarchar(255) NOT NULL, RowVersion timestamp NOT NULL
  RentBuildings: Id int NOT NULL, Name nvarchar(255) NOT NULL, Address nvarchar(255),
    PurchasedDate datetime, PurchasedPrice money, LandValue money, EstimatedValue money,
    OwnershipName1 nvarchar(255), OwnershipName2 nvarchar(255), OwnershipPercentage1 money,
    OwnershipPercentage2 money, Note nvarchar(255), CategoryForTaxes int, CategoryForIncome int,
    CategoryForInterest int, CategoryForRepairs int, CategoryForMaintenance int,
    CategoryForManagement int, RowVersion timestamp NOT NULL
  RentUnits: Id int NOT NULL, Building int NOT NULL, Name nvarchar(255) NOT NULL, Renter nvarchar(255),
    Note nvarchar(255), RowVersion timestamp NOT NULL
  Securities: Id int NOT NULL, Name nvarchar(80) NOT NULL, Symbol nchar(20) NOT NULL, Price money,
    LastPrice money, CUSPID nchar(20), SECURITYTYPE int, TAXABLE tinyint, PriceDate datetime,
    RowVersion timestamp NOT NULL
  Splits: Transaction bigint NOT NULL, Id int NOT NULL, Category int, Payee int, Amount money NOT NULL,
    Transfer bigint, Memo nvarchar(255), Flags int, BudgetBalanceDate datetime,
    RowVersion timestamp NOT NULL
  StockSplits: Id bigint NOT NULL, Date datetime NOT NULL, Security int, Numerator money NOT NULL,
    Denominator money NOT NULL, RowVersion timestamp NOT NULL
  Transactions: Id bigint NOT NULL, Account int NOT NULL, Date datetime NOT NULL, Status int,
    Payee int, OriginalPayee nvarchar(255), Category int, Memo nvarchar(255), Number nchar(10),
    ReconciledDate datetime, BudgetBalanceDate datetime, Transfer bigint, FITID nchar(40),
    Flags int NOT NULL, Amount money NOT NULL, SalesTax money, TransferSplit int, MergeDate datetime,
    RowVersion timestamp NOT NULL
  ```
- `IAggregateRoot.Id` is `long` on every entity (explicit interface implementation over each
  concrete class's own `int Id`, except `Transaction`/`StockSplit` whose own `Id` is already `long`).
  TVP `Id` columns use the table's real underlying type (`int` or `bigint` matching the schema
  above); C# dictionaries keying by `IAggregateRoot.Id` use `long`.
- Every task must leave `dotnet build Source/WPF/MyMoney.sln` at 0 errors and
  `dotnet test Source/WPF/MyMoney.sln -m:1` at the established baseline (SQL-Server-env-gated
  `MyMoney.TestSupport` failures when the SQL Server env vars aren't set, 1 `ScenarioTest` FlaUI
  flake — verify no *new* failures). Each task also runs the live-SQL-Server-specific command below
  (requires `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`/`MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`/
  `MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1` set — see `docs/dev/index.md`'s "Running the SQL Server
  contract tests"):
  ```
  dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqlServerDatabaseContractTests"
  ```
- The schema migration (Task 1) and every new stored proc/type (every task's SQL file) run once, by
  hand, as `MyMoneyAdmin` against the live shared Redmond database — this is a real, shared resource
  other work depends on, not a disposable test fixture. No app code runs migrations implicitly.

---

### Task 1: Schema migration — add the `Version` column to all 11 `IAggregateRoot` tables

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Migrations/2026-09-17-add-version-column.sql`

**Interfaces:**
- Consumes: nothing — this is pure DDL, run by hand.
- Produces: a `Version BIGINT NOT NULL DEFAULT 1` column on `Categories`, `Currencies`,
  `OnlineAccounts`, `Accounts`, `Payees`, `Aliases`, `Securities`, `StockSplits`, `LoanPayments`,
  `RentBuildings`, `Transactions` — every later task's `_SaveBatch` proc reads/writes this column by
  this exact name.

- [ ] **Step 1: Write the migration script**

```sql
-- 2026-09-17-add-version-column.sql
-- Run once, by hand, as the MyMoneyAdmin login against the MyMoney database.
-- Adds the application-managed optimistic-concurrency column persistence-concurrency Phase 2c
-- needs. Named "Version", not "RowVersion": every one of these tables already has an unused
-- native RowVersion timestamp column (SQL Server's own auto-incrementing binary(8) type), which
-- can't be used for this purpose - it's a single counter shared by the whole database, not a
-- per-row counter, so it can never satisfy "a fresh row's version is 1" (see
-- docs/superpowers/specs/2026-09-17-persistence-concurrency-phase2c-design.md). This column is
-- purely additive: nothing existing reads or writes it.

USE MyMoney;
GO

ALTER TABLE dbo.Categories ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Currencies ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.OnlineAccounts ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Accounts ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Payees ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Aliases ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Securities ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.StockSplits ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.LoanPayments ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.RentBuildings ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Transactions ADD Version BIGINT NOT NULL DEFAULT 1;
GO
```

- [ ] **Step 2: Run it against Redmond as MyMoneyAdmin**

```powershell
$creds = Get-Content "$env:USERPROFILE\.secrets\MyMoney\dataengine.credentials.json" | ConvertFrom-Json
$env:SQLCMDPASSWORD = $creds.MyMoneyAdmin.Password
sqlcmd -S Redmond -d MyMoney -U $creds.MyMoneyAdmin.UserId -C -i "Source/WPF/MyMoney.Data/SqlScripts/Migrations/2026-09-17-add-version-column.sql"
```

Expected: no errors, no output other than SQL Server's own batch-completion messages.

- [ ] **Step 3: Verify the column exists with the right shape**

```powershell
$env:SQLCMDPASSWORD = $creds.MyMoneyAdmin.Password
sqlcmd -S Redmond -d MyMoney -U $creds.MyMoneyAdmin.UserId -C -Q "SELECT TABLE_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME = N'Version' ORDER BY TABLE_NAME"
```

Expected: 11 rows, one per table listed above, each `bigint`, `NO` (not nullable), and a default
expression containing `1`.

- [ ] **Step 4: Commit the migration script**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Migrations/2026-09-17-add-version-column.sql
git commit -m "Add Version column migration for Phase 2c (run manually against Redmond)"
```

Note: this commit records the script for reproducibility (e.g. re-running against a future restored
copy of the database) — it does not re-run the migration; Step 2 already applied it to the live
database directly.

---

### Task 2: `Category` — establishes the shared pattern (TVP, proc, C# dispatch infrastructure)

This is the pattern every later entity task repeats. It's written in full detail; later tasks are
terser because they reuse `ExecuteSaveBatchProc` (defined here) verbatim.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/Categories_AccessProcs.sql` (new TYPE, new proc,
  new grants, appended)
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (new `SaveOne`/`SaveBatch`
  overrides, new shared `ExecuteSaveBatchProc` helper, new `SaveCategoryBatch`/`NewCategoryRowTable`,
  `ReadCategories` extended to hydrate `RowVersion`)
- Test: `Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs` (no new tests added here —
  the shared `DatabaseContractTests` suite already covers `Category`; this task just needs it to pass)

**Interfaces:**
- Consumes: `IAggregateRoot`, `ConcurrencyConflictException(PersistentObject, long, long)` (throws if
  `root` is null internally via `root.GetType()` — always resolve a real root before throwing),
  `PersistentObject.RowVersion`/`IsInserted`/`IsChanged`/`IsDeleted`/`OnUpdated()`,
  `PersistentContainer.RemoveChild(PersistentObject, bool)`.
- Produces: `SqlServerStoredProcDatabase.ExecuteSaveBatchProc(SqlConnection, SqlTransaction,
  string procName, (string ParamName, string TvpTypeName, DataTable Rows)[] tvpParameters,
  Dictionary<long, PersistentObject> rootsById, Action<long, long> applyNewVersion)` — every later
  entity task's `SaveXxxBatch` method calls this by this exact name/signature. `SaveBatch`'s dispatch
  structure (a `Type firstType = list[0].GetType()` check, `if (firstType == typeof(Category)) { ... }
  else { base.SaveBatch(list); return; }`) is what later tasks each add one more `else if` branch to.

- [ ] **Step 1: Confirm the tests are currently failing for the right reason**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqlServerDatabaseContractTests"`
(with the SQL Server env vars set — see Global Constraints)
Expected: FAIL — `SaveOne_NewCategory_PersistsAndSetsRowVersionToOne`,
`SaveOne_UpdateCategoryAfterReload_IncrementsRowVersion`,
`SaveOne_StaleCategoryRowVersion_ThrowsConcurrencyConflictException`,
`SaveOne_DeleteCategory_RemovesRowFromDatabaseAndContainer` all fail with
`System.NotImplementedException : SaveOne is not yet implemented for SqlServer`.

- [ ] **Step 2: Add the TVP type and `_SaveBatch` proc**

Append to `Source/WPF/MyMoney.Data/SqlScripts/Access/Categories_AccessProcs.sql` (after the existing
`GRANT EXECUTE ON dbo.Categories_Delete TO MyMoneyTest;` / `GO` block):

```sql
-- Phase 2c: SaveOne/SaveBatch support. Additive - the procs/grants above are untouched and still
-- serve the old whole-graph Save(MyMoney) path.

IF TYPE_ID(N'dbo.CategorySaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.CategorySaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(80) NULL,
        Description NVARCHAR(255) NULL,
        Type INT NULL,
        ParentId INT NULL,
        Budget MONEY NULL,
        Frequency INT NULL,
        Balance MONEY NULL,
        Color NCHAR(10) NULL,
        TaxRefNum INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_SaveBatch
    @Rows dbo.CategorySaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Categories c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Categories c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Categories (Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum, Version)
    SELECT Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET
        Name = r.Name, Description = r.Description, Type = r.Type, ParentId = r.ParentId, Budget = r.Budget,
        Frequency = r.Frequency, Balance = r.Balance, Color = r.Color, TaxRefNum = r.TaxRefNum, Version = c.Version + 1
    FROM dbo.Categories c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c
    FROM dbo.Categories c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Categories
    WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Categories_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.CategorySaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.CategorySaveBatchRow TO MyMoneyTest;
GO
```

Run it against Redmond as `MyMoneyAdmin`, same pattern as Task 1 Step 2:

```powershell
$creds = Get-Content "$env:USERPROFILE\.secrets\MyMoney\dataengine.credentials.json" | ConvertFrom-Json
$env:SQLCMDPASSWORD = $creds.MyMoneyAdmin.Password
sqlcmd -S Redmond -d MyMoney -U $creds.MyMoneyAdmin.UserId -C -i "Source/WPF/MyMoney.Data/SqlScripts/Access/Categories_AccessProcs.sql"
```

Expected: no errors.

- [ ] **Step 3: Extend `ReadCategories` to hydrate `RowVersion`**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, change the `Categories_SelectAll`
proc's SELECT list (in `Categories_AccessProcs.sql`, the existing `dbo.Categories_SelectAll` proc)
to also return `Version`:

```sql
CREATE OR ALTER PROCEDURE dbo.Categories_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum, Version
    FROM dbo.Categories ORDER BY Id;
END
GO
```

(This replaces the existing `Categories_SelectAll` definition in the same file — purely additive to
its SELECT list, existing callers ignore the extra column, no behavior change for the old path.)
Re-run the same `sqlcmd -i` command from Step 2 to apply this change too.

Then in `SqlServerStoredProcDatabase.cs`, change `ReadCategories`:

```csharp
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
```

to (adds reading column index 10 and setting `c.RowVersion`, before `OnUpdated()` per
`SqliteDatabase.ReadCategories`'s own established ordering):

```csharp
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
```

- [ ] **Step 4: Add the shared `ExecuteSaveBatchProc` helper, `SaveOne`/`SaveBatch`, and
  `NewCategoryRowTable`/`SaveCategoryBatch`**

Add `using System.Data;` and `using System.Collections.Generic;` to the top of
`SqlServerStoredProcDatabase.cs` if not already present (the file's current imports are `using
System;`, `using System.Collections;`, `using System.Data;`, `using System.Data.SqlTypes;`, `using
System.Linq;`, `using Microsoft.Data.SqlClient;`, `using Walkabout.Utilities;` — `System.Data` is
already there; `System.Collections.Generic` is not and must be added for `Dictionary<,>`/`List<>`).

Add these members to the `SqlServerStoredProcDatabase` class (anywhere after the existing
`ExecutePayeeProc` private method, before the closing class brace):

```csharp
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
                        long id = reader.GetInt64(reader.GetOrdinal("Id"));
                        string result = reader.GetString(reader.GetOrdinal("Result"));
                        if (result == "CONFLICT")
                        {
                            long storedVersion = reader.GetInt64(reader.GetOrdinal("StoredVersion"));
                            long callerVersion = reader.GetInt64(reader.GetOrdinal("CallerVersion"));
                            rootsById.TryGetValue(id, out PersistentObject conflictRoot);
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
```

- [ ] **Step 5: Run the SQL Server contract tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqlServerDatabaseContractTests"`
Expected: the 4 `Category`-specific tests listed in Step 1 now PASS. Every other test (still using
the inherited whole-graph `Save`/`Load` path, or still hitting the `NotImplementedException` stub
for other types) is unchanged from Step 1's baseline.

- [ ] **Step 6: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect the established baseline, no new failures.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Categories_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for Category on SqlServerStoredProcDatabase"
```

---

### Task 3: `Currency`

Same shape as Task 2. `Currencies_SelectAll`'s existing column order (confirmed from
`Currencies_AccessProcs.sql`): `Id(0), Symbol(1), Name(2), Ratio(3), LastRatio(4), CultureCode(5)`
— `Version` becomes index 6.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/Currencies_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

**Interfaces:**
- Consumes: `ExecuteSaveBatchProc` (Task 2).
- Produces: `SaveCurrencyBatch`, `NewCurrencyRowTable` — used by Task 13's multi-type dispatcher.

- [ ] **Step 1: Confirm failing tests** — `SaveOne_NewCurrency_PersistsAndSetsRowVersionToOne`,
  `SaveOne_UpdateCurrencyAfterReload_IncrementsRowVersion`,
  `SaveOne_StaleCurrencyRowVersion_ThrowsConcurrencyConflictException`,
  `SaveOne_DeleteCurrency_RemovesRowFromDatabaseAndContainer` fail with `NotImplementedException`.

- [ ] **Step 2: Append to `Currencies_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.CurrencySaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.CurrencySaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Symbol NCHAR(20) NULL,
        Name NVARCHAR(80) NULL,
        Ratio MONEY NULL,
        LastRatio MONEY NULL,
        CultureCode NVARCHAR(80) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_SaveBatch
    @Rows dbo.CurrencySaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Currencies c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Currencies c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Currencies (Id, Symbol, Name, Ratio, LastRatio, CultureCode, Version)
    SELECT Id, Symbol, Name, Ratio, LastRatio, CultureCode, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Symbol = r.Symbol, Name = r.Name, Ratio = r.Ratio, LastRatio = r.LastRatio,
        CultureCode = r.CultureCode, Version = c.Version + 1
    FROM dbo.Currencies c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Currencies c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Currencies WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Currencies_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.CurrencySaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.CurrencySaveBatchRow TO MyMoneyTest;
GO
```

Also change `Currencies_SelectAll`'s SELECT list to `SELECT Id, Symbol, Name, Ratio, LastRatio,
CultureCode, Version FROM dbo.Currencies ORDER BY Id;`. Run the whole file against Redmond as
`MyMoneyAdmin` (same `sqlcmd -i` command as Task 2 Step 2, this file's path instead).

- [ ] **Step 3: Extend `ReadCurrencies`**

Add `s.RowVersion = reader.GetInt64(6);` immediately before `s.OnUpdated();` in
`SqlServerStoredProcDatabase.ReadCurrencies`.

- [ ] **Step 4: Add `NewCurrencyRowTable`/`SaveCurrencyBatch`, extend `SaveBatch`**

```csharp
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
```

In `SaveBatch`, change:

```csharp
                if (firstType == typeof(Category))
                {
                    this.SaveCategoryBatch(list.ConvertAll(r => (Category)r), connection, null, postCommitActions);
                }
                else
                {
                    base.SaveBatch(list);
                    return;
                }
```

to:

```csharp
                if (firstType == typeof(Category))
                {
                    this.SaveCategoryBatch(list.ConvertAll(r => (Category)r), connection, null, postCommitActions);
                }
                else if (firstType == typeof(Currency))
                {
                    this.SaveCurrencyBatch(list.ConvertAll(r => (Currency)r), connection, null, postCommitActions);
                }
                else
                {
                    base.SaveBatch(list);
                    return;
                }
```

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `Currency` tests now pass, nothing else regresses.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Currencies_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for Currency on SqlServerStoredProcDatabase"
```

---

### Task 4: `OnlineAccount`

`OnlineAccounts_SelectAll`'s existing order: `Id(0), Name(1), Institution(2), OFX(3), FID(4),
UserId(5), Password(6), BankId(7), BranchId(8), BrokerId(9), OfxVersion(10), LogoUrl(11), AppId(12),
AppVersion(13), ClientUid(14), UserCred1(15), UserCred2(16), AuthToken(17), AccessKey(18),
UserKey(19), UserKeyExpireDate(20)` — `Version` becomes index 21. Note the C# property is
`i.Ofx` (not `OFX`) even though the column/parameter name is `OFX`/`@OFX`; `i.UserKeyExpireDate`
is nullable and needs `SqlServerDatabase.DBNullableDateTimeParam(...)` when building the row, same
helper `UpdateOnlineAccounts` already uses.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/OnlineAccounts_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — the 4 `OnlineAccount` `SaveOne_*` tests.

- [ ] **Step 2: Append to `OnlineAccounts_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.OnlineAccountSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.OnlineAccountSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(80) NULL,
        Institution NVARCHAR(80) NULL,
        OFX NVARCHAR(255) NULL,
        FID NVARCHAR(50) NULL,
        UserId NCHAR(20) NULL,
        Password NVARCHAR(50) NULL,
        BankId NVARCHAR(50) NULL,
        BranchId NVARCHAR(50) NULL,
        BrokerId NVARCHAR(50) NULL,
        OfxVersion NCHAR(10) NULL,
        LogoUrl NVARCHAR(1000) NULL,
        AppId NCHAR(10) NULL,
        AppVersion NCHAR(10) NULL,
        ClientUid NCHAR(36) NULL,
        UserCred1 NVARCHAR(200) NULL,
        UserCred2 NVARCHAR(200) NULL,
        AuthToken NVARCHAR(200) NULL,
        AccessKey NCHAR(36) NULL,
        UserKey NVARCHAR(64) NULL,
        UserKeyExpireDate DATETIME NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_SaveBatch
    @Rows dbo.OnlineAccountSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.OnlineAccounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.OnlineAccounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.OnlineAccounts (Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId,
        BrokerId, OfxVersion, LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken,
        AccessKey, UserKey, UserKeyExpireDate, Version)
    SELECT Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId, BrokerId, OfxVersion,
        LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken, AccessKey, UserKey,
        UserKeyExpireDate, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET
        Name = r.Name, Institution = r.Institution, OFX = r.OFX, FID = r.FID, UserId = r.UserId,
        Password = r.Password, BankId = r.BankId, BranchId = r.BranchId, BrokerId = r.BrokerId,
        OfxVersion = r.OfxVersion, LogoUrl = r.LogoUrl, AppId = r.AppId, AppVersion = r.AppVersion,
        ClientUid = r.ClientUid, UserCred1 = r.UserCred1, UserCred2 = r.UserCred2, AuthToken = r.AuthToken,
        AccessKey = r.AccessKey, UserKey = r.UserKey, UserKeyExpireDate = r.UserKeyExpireDate,
        Version = c.Version + 1
    FROM dbo.OnlineAccounts c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.OnlineAccounts c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.OnlineAccounts WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.OnlineAccounts_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.OnlineAccountSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.OnlineAccountSaveBatchRow TO MyMoneyTest;
GO
```

Also add `, Version` to `OnlineAccounts_SelectAll`'s SELECT list (keeping every existing column in
place). Run the file against Redmond as `MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadOnlineAccounts`**

Add `i.RowVersion = reader.GetInt64(21);` immediately before `i.OnUpdated();`.

- [ ] **Step 4: Add `NewOnlineAccountRowTable`/`SaveOnlineAccountBatch`, extend `SaveBatch`**

```csharp
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
```

In `SaveBatch`, add `else if (firstType == typeof(OnlineAccount)) { this.SaveOnlineAccountBatch(list.ConvertAll(r => (OnlineAccount)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `OnlineAccount` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/OnlineAccounts_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for OnlineAccount on SqlServerStoredProcDatabase"
```

---

### Task 5: `Account`

`Accounts_SelectAll`'s existing order: `Id(0), AccountId(1), OfxAccountId(2), Name(3), Type(4),
Description(5), OnlineAccount(6), OpeningBalance(7), LastSync(8), LastBalance(9), SyncGuid(10),
Flags(11), Currency(12), WebSite(13), ReconcileWarning(14), CategoryIdForPrincipal(15),
CategoryIdForInterest(16)` — `Version` becomes index 17. Uses the existing
`SqlServerDatabase.DBDateTimeParam`/`DBNullableDateTimeParam`/`DBGuidParam` static helpers
(`UpdateAccounts` already uses them) for `LastSync`/`LastBalance`/`SyncGuid`.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/Accounts_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — the 4 `Account` `SaveOne_*` tests.

- [ ] **Step 2: Append to `Accounts_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.AccountSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.AccountSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        AccountId NVARCHAR(20) NULL,
        OfxAccountId NVARCHAR(50) NULL,
        Name NVARCHAR(80) NULL,
        Type INT NULL,
        Description NVARCHAR(255) NULL,
        OnlineAccount INT NULL,
        OpeningBalance MONEY NULL,
        LastSync DATETIME NULL,
        LastBalance DATETIME NULL,
        SyncGuid UNIQUEIDENTIFIER NULL,
        Flags INT NULL,
        Currency NVARCHAR(3) NULL,
        WebSite NVARCHAR(512) NULL,
        ReconcileWarning INT NULL,
        CategoryIdForPrincipal INT NULL,
        CategoryIdForInterest INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_SaveBatch
    @Rows dbo.AccountSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Accounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Accounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Accounts (Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount,
        OpeningBalance, LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
        CategoryIdForPrincipal, CategoryIdForInterest, Version)
    SELECT Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount, OpeningBalance,
        LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
        CategoryIdForPrincipal, CategoryIdForInterest, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET
        AccountId = r.AccountId, OfxAccountId = r.OfxAccountId, Name = r.Name, Type = r.Type,
        Description = r.Description, OnlineAccount = r.OnlineAccount, OpeningBalance = r.OpeningBalance,
        LastSync = r.LastSync, LastBalance = r.LastBalance, SyncGuid = r.SyncGuid, Flags = r.Flags,
        Currency = r.Currency, WebSite = r.WebSite, ReconcileWarning = r.ReconcileWarning,
        CategoryIdForPrincipal = r.CategoryIdForPrincipal, CategoryIdForInterest = r.CategoryIdForInterest,
        Version = c.Version + 1
    FROM dbo.Accounts c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Accounts c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Accounts WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Accounts_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.AccountSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.AccountSaveBatchRow TO MyMoneyTest;
GO
```

Also add `, Version` to `Accounts_SelectAll`'s SELECT list. Run the file against Redmond as
`MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadAccounts`**

Add `a.RowVersion = reader.GetInt64(17);` immediately before `a.OnUpdated();`.

- [ ] **Step 4: Add `NewAccountRowTable`/`SaveAccountBatch`, extend `SaveBatch`**

```csharp
        private static DataTable NewAccountRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("AccountId", typeof(string));
            table.Columns.Add("OfxAccountId", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Type", typeof(int));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("OnlineAccount", typeof(int));
            table.Columns.Add("OpeningBalance", typeof(decimal));
            table.Columns.Add("LastSync", typeof(DateTime));
            table.Columns.Add("LastBalance", typeof(DateTime));
            table.Columns.Add("SyncGuid", typeof(Guid));
            table.Columns.Add("Flags", typeof(int));
            table.Columns.Add("Currency", typeof(string));
            table.Columns.Add("WebSite", typeof(string));
            table.Columns.Add("ReconcileWarning", typeof(int));
            table.Columns.Add("CategoryIdForPrincipal", typeof(int));
            table.Columns.Add("CategoryIdForInterest", typeof(int));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SaveAccountBatch(List<Account> accounts, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewAccountRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, Account> byId = new Dictionary<long, Account>();
            List<Account> deletedInThisBatch = new List<Account>();

            foreach (Account a in accounts)
            {
                long id = a.Id;
                rootsById[id] = a;
                byId[id] = a;
                long callerRowVersion = a.RowVersion;
                object onlineAccountId = a.OnlineAccount != null ? (object)a.OnlineAccount.Id : DBNull.Value;
                object categoryForPrincipal = a.CategoryForPrincipal != null ? (object)a.CategoryForPrincipal.Id : DBNull.Value;
                object categoryForInterest = a.CategoryForInterest != null ? (object)a.CategoryForInterest.Id : DBNull.Value;
                object lastSync = SqlServerDatabase.DBDateTimeParam(a.LastSync);
                object lastBalance = SqlServerDatabase.DBDateTimeParam(a.LastBalance);
                object syncGuid = SqlServerDatabase.DBGuidParam(a.SyncGuid);

                if (a.IsInserted)
                {
                    rows.Rows.Add("I", a.Id, a.AccountId, a.OfxAccountId, a.Name, (int)a.Type, a.Description,
                        onlineAccountId, a.OpeningBalance, lastSync, lastBalance, syncGuid, (int)a.Flags,
                        a.Currency, a.WebSite, a.ReconcileWarning, categoryForPrincipal, categoryForInterest,
                        DBNull.Value);
                }
                else if (a.IsChanged)
                {
                    rows.Rows.Add("U", a.Id, a.AccountId, a.OfxAccountId, a.Name, (int)a.Type, a.Description,
                        onlineAccountId, a.OpeningBalance, lastSync, lastBalance, syncGuid, (int)a.Flags,
                        a.Currency, a.WebSite, a.ReconcileWarning, categoryForPrincipal, categoryForInterest,
                        callerRowVersion);
                }
                else if (a.IsDeleted)
                {
                    rows.Rows.Add("D", a.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        callerRowVersion);
                    deletedInThisBatch.Add(a);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.Accounts_SaveBatch",
                new[] { ("@Rows", "dbo.AccountSaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    Account a = byId[id];
                    postCommitActions.Add(() =>
                    {
                        a.RowVersion = newVersion;
                        a.OnUpdated();
                    });
                });

            foreach (Account a in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    a.OnUpdated();
                    a.Parent.RemoveChild(a, true);
                });
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(Account)) { this.SaveAccountBatch(list.ConvertAll(r => (Account)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `Account` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Accounts_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for Account on SqlServerStoredProcDatabase"
```

---

### Task 6: `Payee`

`Payees_SelectAll`'s existing order: `Id(0), Name(1)` — `Version` becomes index 2. Simplest entity:
two columns.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/Payees_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — the 4 `Payee` `SaveOne_*` tests.

- [ ] **Step 2: Append to `Payees_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.PayeeSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.PayeeSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(255) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_SaveBatch
    @Rows dbo.PayeeSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Payees c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Payees c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Payees (Id, Name, Version)
    SELECT Id, Name, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Name = r.Name, Version = c.Version + 1
    FROM dbo.Payees c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Payees c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Payees WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Payees_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.PayeeSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.PayeeSaveBatchRow TO MyMoneyTest;
GO
```

Also change `Payees_SelectAll`'s SELECT list to `SELECT Id, Name, Version FROM dbo.Payees ORDER BY
Id;`. Run the file against Redmond as `MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadPayees`**

Add `p.RowVersion = reader.GetInt64(2);` immediately before `p.OnUpdated();`.

- [ ] **Step 4: Add `NewPayeeRowTable`/`SavePayeeBatch`, extend `SaveBatch`**

```csharp
        private static DataTable NewPayeeRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SavePayeeBatch(List<Payee> payees, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewPayeeRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, Payee> byId = new Dictionary<long, Payee>();
            List<Payee> deletedInThisBatch = new List<Payee>();

            foreach (Payee p in payees)
            {
                long id = p.Id;
                rootsById[id] = p;
                byId[id] = p;
                long callerRowVersion = p.RowVersion;

                if (p.IsInserted)
                {
                    rows.Rows.Add("I", p.Id, p.Name, DBNull.Value);
                }
                else if (p.IsChanged)
                {
                    rows.Rows.Add("U", p.Id, p.Name, callerRowVersion);
                }
                else if (p.IsDeleted)
                {
                    rows.Rows.Add("D", p.Id, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(p);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.Payees_SaveBatch",
                new[] { ("@Rows", "dbo.PayeeSaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    Payee p = byId[id];
                    postCommitActions.Add(() =>
                    {
                        p.RowVersion = newVersion;
                        p.OnUpdated();
                    });
                });

            foreach (Payee p in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    p.OnUpdated();
                    p.Parent.RemoveChild(p, true);
                });
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(Payee)) { this.SavePayeeBatch(list.ConvertAll(r => (Payee)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `Payee` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Payees_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for Payee on SqlServerStoredProcDatabase"
```

---

### Task 7: `Alias`

`Aliases_SelectAll`'s existing order: `Id(0), Pattern(1), Payee(2), Flags(3)` — `Version` becomes
index 4. `a.Payee` is a resolved `Payee` object in C# (`a.Payee.Id` for the SQL parameter);
`a.AliasType` casts to `int` for the `Flags` column, same as `UpdateAliases` already does.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/Aliases_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — the 4 `Alias` `SaveOne_*` tests.

- [ ] **Step 2: Append to `Aliases_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.AliasSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.AliasSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Pattern NVARCHAR(255) NULL,
        Payee INT NULL,
        Flags INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_SaveBatch
    @Rows dbo.AliasSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Aliases c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Aliases c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Aliases (Id, Pattern, Payee, Flags, Version)
    SELECT Id, Pattern, Payee, Flags, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Pattern = r.Pattern, Payee = r.Payee, Flags = r.Flags, Version = c.Version + 1
    FROM dbo.Aliases c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Aliases c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Aliases WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Aliases_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.AliasSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.AliasSaveBatchRow TO MyMoneyTest;
GO
```

Also change `Aliases_SelectAll`'s SELECT list to `SELECT Id, Pattern, Payee, Flags, Version FROM
dbo.Aliases ORDER BY Id;`. Run the file against Redmond as `MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadAliases`**

Add `a.RowVersion = reader.GetInt64(4);` immediately before `a.OnUpdated();`.

- [ ] **Step 4: Add `NewAliasRowTable`/`SaveAliasBatch`, extend `SaveBatch`**

```csharp
        private static DataTable NewAliasRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Pattern", typeof(string));
            table.Columns.Add("Payee", typeof(int));
            table.Columns.Add("Flags", typeof(int));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SaveAliasBatch(List<Alias> aliases, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewAliasRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, Alias> byId = new Dictionary<long, Alias>();
            List<Alias> deletedInThisBatch = new List<Alias>();

            foreach (Alias a in aliases)
            {
                long id = a.Id;
                rootsById[id] = a;
                byId[id] = a;
                long callerRowVersion = a.RowVersion;

                if (a.IsInserted)
                {
                    rows.Rows.Add("I", a.Id, a.Pattern, a.Payee.Id, (int)a.AliasType, DBNull.Value);
                }
                else if (a.IsChanged)
                {
                    rows.Rows.Add("U", a.Id, a.Pattern, a.Payee.Id, (int)a.AliasType, callerRowVersion);
                }
                else if (a.IsDeleted)
                {
                    rows.Rows.Add("D", a.Id, DBNull.Value, DBNull.Value, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(a);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.Aliases_SaveBatch",
                new[] { ("@Rows", "dbo.AliasSaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    Alias a = byId[id];
                    postCommitActions.Add(() =>
                    {
                        a.RowVersion = newVersion;
                        a.OnUpdated();
                    });
                });

            foreach (Alias a in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    a.OnUpdated();
                    a.Parent.RemoveChild(a, true);
                });
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(Alias)) { this.SaveAliasBatch(list.ConvertAll(r => (Alias)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `Alias` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Aliases_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for Alias on SqlServerStoredProcDatabase"
```

---

### Task 8: `Security`

`Securities_SelectAll`'s existing order: `Id(0), Name(1), Symbol(2), Price(3), LastPrice(4),
CuspId(5), SecurityType(6), Taxable(7), PriceDate(8)` — `Version` becomes index 9. `s.SecurityType`
casts to `int`, `s.Taxable` (a `YesNo` enum) casts to `byte`, `s.PriceDate` uses
`SqlServerDatabase.DBDateTimeParam`, matching `UpdateSecurities`.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/Securities_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — the 4 `Security` `SaveOne_*` tests.

- [ ] **Step 2: Append to `Securities_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.SecuritySaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.SecuritySaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(80) NULL,
        Symbol NVARCHAR(20) NULL,
        Price MONEY NULL,
        LastPrice MONEY NULL,
        CuspId NVARCHAR(20) NULL,
        SecurityType INT NULL,
        Taxable TINYINT NULL,
        PriceDate DATETIME NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_SaveBatch
    @Rows dbo.SecuritySaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Securities c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Securities c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Securities (Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate, Version)
    SELECT Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Name = r.Name, Symbol = r.Symbol, Price = r.Price, LastPrice = r.LastPrice,
        CuspId = r.CuspId, SecurityType = r.SecurityType, Taxable = r.Taxable, PriceDate = r.PriceDate,
        Version = c.Version + 1
    FROM dbo.Securities c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Securities c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Securities WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Securities_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.SecuritySaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.SecuritySaveBatchRow TO MyMoneyTest;
GO
```

Also add `, Version` to `Securities_SelectAll`'s SELECT list. Run the file against Redmond as
`MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadSecurities`**

Add `s.RowVersion = reader.GetInt64(9);` immediately before `s.OnUpdated();`.

- [ ] **Step 4: Add `NewSecurityRowTable`/`SaveSecurityBatch`, extend `SaveBatch`**

```csharp
        private static DataTable NewSecurityRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Symbol", typeof(string));
            table.Columns.Add("Price", typeof(decimal));
            table.Columns.Add("LastPrice", typeof(decimal));
            table.Columns.Add("CuspId", typeof(string));
            table.Columns.Add("SecurityType", typeof(int));
            table.Columns.Add("Taxable", typeof(byte));
            table.Columns.Add("PriceDate", typeof(DateTime));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SaveSecurityBatch(List<Security> securities, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewSecurityRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, Security> byId = new Dictionary<long, Security>();
            List<Security> deletedInThisBatch = new List<Security>();

            foreach (Security s in securities)
            {
                long id = s.Id;
                rootsById[id] = s;
                byId[id] = s;
                long callerRowVersion = s.RowVersion;
                object priceDate = SqlServerDatabase.DBDateTimeParam(s.PriceDate);

                if (s.IsInserted)
                {
                    rows.Rows.Add("I", s.Id, s.Name, s.Symbol, s.Price, s.LastPrice, s.CuspId,
                        (int)s.SecurityType, (byte)s.Taxable, priceDate, DBNull.Value);
                }
                else if (s.IsChanged)
                {
                    rows.Rows.Add("U", s.Id, s.Name, s.Symbol, s.Price, s.LastPrice, s.CuspId,
                        (int)s.SecurityType, (byte)s.Taxable, priceDate, callerRowVersion);
                }
                else if (s.IsDeleted)
                {
                    rows.Rows.Add("D", s.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(s);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.Securities_SaveBatch",
                new[] { ("@Rows", "dbo.SecuritySaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    Security s = byId[id];
                    postCommitActions.Add(() =>
                    {
                        s.RowVersion = newVersion;
                        s.OnUpdated();
                    });
                });

            foreach (Security s in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    s.OnUpdated();
                    s.Parent.RemoveChild(s, true);
                });
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(Security)) { this.SaveSecurityBatch(list.ConvertAll(r => (Security)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `Security` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Securities_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for Security on SqlServerStoredProcDatabase"
```

---

### Task 9: `StockSplit`

`StockSplits_SelectAll`'s existing order: `Id(0), Date(1), Security(2), Numerator(3),
Denominator(4)` — `Version` becomes index 5. `StockSplit.Id` is natively `long` (unlike the `int`
entities before it, no cast needed anywhere). `s.Security` is a resolved `Security` object
(`s.Security.Id` for the parameter, `DBNull.Value` if null); `s.Date` uses
`SqlServerDatabase.DBDateTimeParam`. Note `UpdateStockSplits`'s existing guard (`if (s.Date ==
DateTime.MinValue) { continue; }`) — carry the same guard into the new batch method, skipping a row
with an unset `Date` entirely rather than sending it to the proc.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/StockSplits_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — the 4 `StockSplit` `SaveOne_*` tests.

- [ ] **Step 2: Append to `StockSplits_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.StockSplitSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.StockSplitSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id BIGINT NOT NULL,
        Date DATETIME NULL,
        Security INT NULL,
        Numerator MONEY NULL,
        Denominator MONEY NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_SaveBatch
    @Rows dbo.StockSplitSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.StockSplits c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.StockSplits c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.StockSplits (Id, Date, Security, Numerator, Denominator, Version)
    SELECT Id, Date, Security, Numerator, Denominator, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Date = r.Date, Security = r.Security, Numerator = r.Numerator,
        Denominator = r.Denominator, Version = c.Version + 1
    FROM dbo.StockSplits c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.StockSplits c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.StockSplits WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.StockSplits_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.StockSplitSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.StockSplitSaveBatchRow TO MyMoneyTest;
GO
```

Also add `, Version` to `StockSplits_SelectAll`'s SELECT list. Run the file against Redmond as
`MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadStockSplits`**

Add `s.RowVersion = reader.GetInt64(5);` immediately before `s.OnUpdated();`.

- [ ] **Step 4: Add `NewStockSplitRowTable`/`SaveStockSplitBatch`, extend `SaveBatch`**

```csharp
        private static DataTable NewStockSplitRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(long));
            table.Columns.Add("Date", typeof(DateTime));
            table.Columns.Add("Security", typeof(int));
            table.Columns.Add("Numerator", typeof(decimal));
            table.Columns.Add("Denominator", typeof(decimal));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SaveStockSplitBatch(List<StockSplit> stockSplits, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewStockSplitRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, StockSplit> byId = new Dictionary<long, StockSplit>();
            List<StockSplit> deletedInThisBatch = new List<StockSplit>();

            foreach (StockSplit s in stockSplits)
            {
                if ((s.IsChanged || s.IsInserted) && s.Date == DateTime.MinValue)
                {
                    continue;
                }

                long id = s.Id;
                rootsById[id] = s;
                byId[id] = s;
                long callerRowVersion = s.RowVersion;
                object securityId = s.Security != null ? (object)s.Security.Id : DBNull.Value;
                object date = SqlServerDatabase.DBDateTimeParam(s.Date);

                if (s.IsInserted)
                {
                    rows.Rows.Add("I", s.Id, date, securityId, s.Numerator, s.Denominator, DBNull.Value);
                }
                else if (s.IsChanged)
                {
                    rows.Rows.Add("U", s.Id, date, securityId, s.Numerator, s.Denominator, callerRowVersion);
                }
                else if (s.IsDeleted)
                {
                    rows.Rows.Add("D", s.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(s);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.StockSplits_SaveBatch",
                new[] { ("@Rows", "dbo.StockSplitSaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    StockSplit s = byId[id];
                    postCommitActions.Add(() =>
                    {
                        s.RowVersion = newVersion;
                        s.OnUpdated();
                    });
                });

            foreach (StockSplit s in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    s.OnUpdated();
                    s.Parent.RemoveChild(s, true);
                });
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(StockSplit)) { this.SaveStockSplitBatch(list.ConvertAll(r => (StockSplit)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `StockSplit` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/StockSplits_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for StockSplit on SqlServerStoredProcDatabase"
```

---

### Task 10: `LoanPayment`

`LoanPayments_SelectAll`'s existing order: `Id(0), AccountId(1), Date(2), Principal(3),
Interest(4), Memo(5)` — `Version` becomes index 6. `x.Date` uses
`SqlServerDatabase.DBDateTimeParam`, matching `UpdateLoanPayments`.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/LoanPayments_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — the 4 `LoanPayment` `SaveOne_*` tests.

- [ ] **Step 2: Append to `LoanPayments_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.LoanPaymentSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.LoanPaymentSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        AccountId INT NULL,
        Date DATETIME NULL,
        Principal MONEY NULL,
        Interest MONEY NULL,
        Memo NVARCHAR(255) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_SaveBatch
    @Rows dbo.LoanPaymentSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.LoanPayments c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.LoanPayments c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.LoanPayments (Id, AccountId, Date, Principal, Interest, Memo, Version)
    SELECT Id, AccountId, Date, Principal, Interest, Memo, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET AccountId = r.AccountId, Date = r.Date, Principal = r.Principal, Interest = r.Interest,
        Memo = r.Memo, Version = c.Version + 1
    FROM dbo.LoanPayments c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.LoanPayments c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.LoanPayments WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.LoanPayments_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.LoanPaymentSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.LoanPaymentSaveBatchRow TO MyMoneyTest;
GO
```

Also add `, Version` to `LoanPayments_SelectAll`'s SELECT list. Run the file against Redmond as
`MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadLoanPayments`**

Add `x.RowVersion = reader.GetInt64(6);` immediately before `x.OnUpdated();` (after `collection.AddLoan(x);`, matching the existing method's ordering).

- [ ] **Step 4: Add `NewLoanPaymentRowTable`/`SaveLoanPaymentBatch`, extend `SaveBatch`**

```csharp
        private static DataTable NewLoanPaymentRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("AccountId", typeof(int));
            table.Columns.Add("Date", typeof(DateTime));
            table.Columns.Add("Principal", typeof(decimal));
            table.Columns.Add("Interest", typeof(decimal));
            table.Columns.Add("Memo", typeof(string));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private void SaveLoanPaymentBatch(List<LoanPayment> loanPayments, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable rows = NewLoanPaymentRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, LoanPayment> byId = new Dictionary<long, LoanPayment>();
            List<LoanPayment> deletedInThisBatch = new List<LoanPayment>();

            foreach (LoanPayment i in loanPayments)
            {
                long id = i.Id;
                rootsById[id] = i;
                byId[id] = i;
                long callerRowVersion = i.RowVersion;
                object date = SqlServerDatabase.DBDateTimeParam(i.Date);

                if (i.IsInserted)
                {
                    rows.Rows.Add("I", i.Id, i.AccountId, date, i.Principal, i.Interest, i.Memo, DBNull.Value);
                }
                else if (i.IsChanged)
                {
                    rows.Rows.Add("U", i.Id, i.AccountId, date, i.Principal, i.Interest, i.Memo, callerRowVersion);
                }
                else if (i.IsDeleted)
                {
                    rows.Rows.Add("D", i.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, callerRowVersion);
                    deletedInThisBatch.Add(i);
                }
            }

            if (rows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.LoanPayments_SaveBatch",
                new[] { ("@Rows", "dbo.LoanPaymentSaveBatchRow", rows) },
                rootsById,
                (id, newVersion) =>
                {
                    LoanPayment i = byId[id];
                    postCommitActions.Add(() =>
                    {
                        i.RowVersion = newVersion;
                        i.OnUpdated();
                    });
                });

            foreach (LoanPayment i in deletedInThisBatch)
            {
                postCommitActions.Add(() =>
                {
                    i.OnUpdated();
                    i.Parent.RemoveChild(i, true);
                });
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(LoanPayment)) { this.SaveLoanPaymentBatch(list.ConvertAll(r => (LoanPayment)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — the 4 `LoanPayment` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/LoanPayments_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for LoanPayment on SqlServerStoredProcDatabase"
```

---

### Task 11: `RentBuilding` (compound — owned `RentUnit`s)

First compound entity. `dbo.RentBuildings_SaveBatch` takes **two** TVPs: `@Buildings` (version-
checked, like every entity so far) and `@Units` (no `ExpectedVersion` — `RentUnit` isn't
`IAggregateRoot`, gated only by its parent building's version, per the design spec's R2). Units are
reached via the sibling global `RentBuildings.Units` container (`((RentBuildings)building.Parent)
.Units`), filtered to `u.Building` matching one of the buildings in this batch — populated and
processed independently of whether the building's own row has a pending change, matching
`SaveOne_RentBuildingWithUnitOnlyEdit_PersistsUnitEvenThoughBuildingItselfIsUnchanged`.
`RentBuildings_SelectAll`'s existing order: `Id(0), Name(1), Address(2), PurchasedDate(3),
PurchasedPrice(4), LandValue(5), EstimatedValue(6), CategoryForIncome(7), CategoryForTaxes(8),
CategoryForInterest(9), CategoryForRepairs(10), CategoryForMaintenance(11),
CategoryForManagement(12), OwnershipName1(13), OwnershipName2(14), OwnershipPercentage1(15),
OwnershipPercentage2(16), Note(17)` — `Version` becomes index 18. `r.CategoryForIncome`/etc. are
plain `int` fields on `RentBuilding` (a `-1` sentinel when unset, per `ReadRentBuildings`), not
resolved `Category` objects — no null-check needed.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/RentBuildings_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — `SaveOne_NewRentBuilding_PersistsAndSetsRowVersionToOne`,
  `SaveOne_UpdateRentBuildingAfterReload_IncrementsRowVersion`,
  `SaveOne_StaleRentBuildingRowVersion_ThrowsConcurrencyConflictException`,
  `SaveOne_DeleteRentBuilding_RemovesRowFromDatabaseAndContainer`,
  `SaveOne_RentBuildingWithNewUnit_PersistsUnitAndSetsItClean`,
  `SaveOne_RentBuildingWithUnitOnlyEdit_PersistsUnitEvenThoughBuildingItselfIsUnchanged`,
  `SaveOne_RentBuildingWithDeletedUnit_RemovesUnitRow`.

- [ ] **Step 2: Append to `RentBuildings_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.RentBuildingSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.RentBuildingSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(255) NULL,
        Address NVARCHAR(255) NULL,
        PurchasedDate DATETIME NULL,
        PurchasedPrice MONEY NULL,
        LandValue MONEY NULL,
        EstimatedValue MONEY NULL,
        CategoryForIncome INT NULL,
        CategoryForTaxes INT NULL,
        CategoryForInterest INT NULL,
        CategoryForRepairs INT NULL,
        CategoryForMaintenance INT NULL,
        CategoryForManagement INT NULL,
        OwnershipName1 NVARCHAR(255) NULL,
        OwnershipName2 NVARCHAR(255) NULL,
        OwnershipPercentage1 MONEY NULL,
        OwnershipPercentage2 MONEY NULL,
        Note NVARCHAR(255) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

IF TYPE_ID(N'dbo.RentUnitRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.RentUnitRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Building INT NOT NULL,
        Name NVARCHAR(255) NULL,
        Renter NVARCHAR(255) NULL,
        Note NVARCHAR(255) NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_SaveBatch
    @Buildings dbo.RentBuildingSaveBatchRow READONLY,
    @Units dbo.RentUnitRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Buildings r
        LEFT JOIN dbo.RentBuildings c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Buildings r LEFT JOIN dbo.RentBuildings c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- Children before parent on delete, matching UpdateTransactions' documented FK-ordering rule.
    DELETE u FROM dbo.RentUnits u JOIN @Units r ON u.Id = r.Id AND u.Building = r.Building WHERE r.[Action] = 'D';

    DELETE c FROM dbo.RentBuildings c JOIN @Buildings r ON c.Id = r.Id WHERE r.[Action] = 'D';

    INSERT INTO dbo.RentBuildings (Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue,
        CategoryForIncome, CategoryForTaxes, CategoryForInterest, CategoryForRepairs, CategoryForMaintenance,
        CategoryForManagement, OwnershipName1, OwnershipName2, OwnershipPercentage1, OwnershipPercentage2, Note, Version)
    SELECT Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue, CategoryForIncome,
        CategoryForTaxes, CategoryForInterest, CategoryForRepairs, CategoryForMaintenance, CategoryForManagement,
        OwnershipName1, OwnershipName2, OwnershipPercentage1, OwnershipPercentage2, Note, 1
    FROM @Buildings WHERE [Action] = 'I';

    UPDATE c SET Name = r.Name, Address = r.Address, PurchasedDate = r.PurchasedDate, PurchasedPrice = r.PurchasedPrice,
        LandValue = r.LandValue, EstimatedValue = r.EstimatedValue, CategoryForIncome = r.CategoryForIncome,
        CategoryForTaxes = r.CategoryForTaxes, CategoryForInterest = r.CategoryForInterest,
        CategoryForRepairs = r.CategoryForRepairs, CategoryForMaintenance = r.CategoryForMaintenance,
        CategoryForManagement = r.CategoryForManagement, OwnershipName1 = r.OwnershipName1,
        OwnershipName2 = r.OwnershipName2, OwnershipPercentage1 = r.OwnershipPercentage1,
        OwnershipPercentage2 = r.OwnershipPercentage2, Note = r.Note, Version = c.Version + 1
    FROM dbo.RentBuildings c JOIN @Buildings r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    INSERT INTO dbo.RentUnits (Id, Building, Name, Renter, Note)
    SELECT Id, Building, Name, Renter, Note FROM @Units WHERE [Action] = 'I';

    UPDATE u SET Name = r.Name, Renter = r.Renter, Note = r.Note
    FROM dbo.RentUnits u JOIN @Units r ON u.Id = r.Id AND u.Building = r.Building
    WHERE r.[Action] = 'U';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.RentBuildings WHERE Id IN (SELECT Id FROM @Buildings WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.RentBuildings_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.RentBuildingSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.RentBuildingSaveBatchRow TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.RentUnitRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.RentUnitRow TO MyMoneyTest;
GO
```

Also add `, Version` to `RentBuildings_SelectAll`'s SELECT list. Run the file against Redmond as
`MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadRentBuildings`**

Add `r.RowVersion = reader.GetInt64(18);` immediately before `r.OnUpdated();` (after the existing
`foreach (var unit in money.Buildings.Units.GetList()...)` block and `collection.AddRentBuilding(r);`
line, matching the method's existing ordering).

- [ ] **Step 4: Add `NewRentBuildingRowTable`/`NewRentUnitRowTable`/`SaveRentBuildingBatch`, extend
  `SaveBatch`**

```csharp
        private static DataTable NewRentBuildingRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Address", typeof(string));
            table.Columns.Add("PurchasedDate", typeof(DateTime));
            table.Columns.Add("PurchasedPrice", typeof(decimal));
            table.Columns.Add("LandValue", typeof(decimal));
            table.Columns.Add("EstimatedValue", typeof(decimal));
            table.Columns.Add("CategoryForIncome", typeof(int));
            table.Columns.Add("CategoryForTaxes", typeof(int));
            table.Columns.Add("CategoryForInterest", typeof(int));
            table.Columns.Add("CategoryForRepairs", typeof(int));
            table.Columns.Add("CategoryForMaintenance", typeof(int));
            table.Columns.Add("CategoryForManagement", typeof(int));
            table.Columns.Add("OwnershipName1", typeof(string));
            table.Columns.Add("OwnershipName2", typeof(string));
            table.Columns.Add("OwnershipPercentage1", typeof(decimal));
            table.Columns.Add("OwnershipPercentage2", typeof(decimal));
            table.Columns.Add("Note", typeof(string));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private static DataTable NewRentUnitRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Building", typeof(int));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Renter", typeof(string));
            table.Columns.Add("Note", typeof(string));
            return table;
        }

        private void SaveRentBuildingBatch(List<RentBuilding> buildings, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable buildingRows = NewRentBuildingRowTable();
            DataTable unitRows = NewRentUnitRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, RentBuilding> byId = new Dictionary<long, RentBuilding>();
            List<RentBuilding> deletedBuildings = new List<RentBuilding>();
            List<RentUnit> deletedUnits = new List<RentUnit>();
            List<RentUnit> savedUnits = new List<RentUnit>();

            HashSet<int> buildingIds = new HashSet<int>();
            RentBuildings buildingsContainer = null;
            foreach (RentBuilding r in buildings)
            {
                buildingIds.Add(r.Id);
                buildingsContainer = (RentBuildings)r.Parent;
            }

            if (buildingsContainer != null && buildingsContainer.Units != null)
            {
                List<RentUnit> unitsSnapshot = new List<RentUnit>();
                foreach (RentUnit u in buildingsContainer.Units)
                {
                    unitsSnapshot.Add(u);
                }

                foreach (RentUnit u in unitsSnapshot)
                {
                    if (!buildingIds.Contains(u.Building))
                    {
                        continue;
                    }

                    if (u.IsInserted)
                    {
                        unitRows.Rows.Add("I", u.Id, u.Building, u.Name, u.Renter, u.Note);
                        savedUnits.Add(u);
                    }
                    else if (u.IsChanged)
                    {
                        unitRows.Rows.Add("U", u.Id, u.Building, u.Name, u.Renter, u.Note);
                        savedUnits.Add(u);
                    }
                    else if (u.IsDeleted)
                    {
                        unitRows.Rows.Add("D", u.Id, u.Building, DBNull.Value, DBNull.Value, DBNull.Value);
                        deletedUnits.Add(u);
                    }
                }
            }

            foreach (RentBuilding r in buildings)
            {
                long id = r.Id;
                rootsById[id] = r;
                byId[id] = r;
                long callerRowVersion = r.RowVersion;
                object purchasedDate = SqlServerDatabase.DBDateTimeParam(r.PurchasedDate);

                if (r.IsInserted)
                {
                    buildingRows.Rows.Add("I", r.Id, r.Name, r.Address, purchasedDate, r.PurchasedPrice,
                        r.LandValue, r.EstimatedValue, r.CategoryForIncome, r.CategoryForTaxes,
                        r.CategoryForInterest, r.CategoryForRepairs, r.CategoryForMaintenance,
                        r.CategoryForManagement, r.OwnershipName1, r.OwnershipName2, r.OwnershipPercentage1,
                        r.OwnershipPercentage2, r.Note, DBNull.Value);
                }
                else if (r.IsChanged)
                {
                    buildingRows.Rows.Add("U", r.Id, r.Name, r.Address, purchasedDate, r.PurchasedPrice,
                        r.LandValue, r.EstimatedValue, r.CategoryForIncome, r.CategoryForTaxes,
                        r.CategoryForInterest, r.CategoryForRepairs, r.CategoryForMaintenance,
                        r.CategoryForManagement, r.OwnershipName1, r.OwnershipName2, r.OwnershipPercentage1,
                        r.OwnershipPercentage2, r.Note, callerRowVersion);
                }
                else if (r.IsDeleted)
                {
                    // 17 DBNull.Value placeholders between Id and ExpectedVersion, matching the
                    // table's 17 nullable data columns (Name through Note) - count carefully if
                    // touching this line, it's easy to drop one.
                    buildingRows.Rows.Add("D", r.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, callerRowVersion);
                    deletedBuildings.Add(r);
                }
            }

            if (buildingRows.Rows.Count == 0 && unitRows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.RentBuildings_SaveBatch",
                new[]
                {
                    ("@Buildings", "dbo.RentBuildingSaveBatchRow", buildingRows),
                    ("@Units", "dbo.RentUnitRow", unitRows)
                },
                rootsById,
                (id, newVersion) =>
                {
                    RentBuilding r = byId[id];
                    postCommitActions.Add(() =>
                    {
                        r.RowVersion = newVersion;
                        r.OnUpdated();
                    });
                });

            foreach (RentBuilding r in deletedBuildings)
            {
                postCommitActions.Add(() =>
                {
                    r.OnUpdated();
                    r.Parent.RemoveChild(r, true);
                });
            }

            foreach (RentUnit u in savedUnits)
            {
                postCommitActions.Add(() => u.OnUpdated());
            }

            foreach (RentUnit u in deletedUnits)
            {
                postCommitActions.Add(() => u.Parent.RemoveChild(u, true));
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(RentBuilding)) { this.SaveRentBuildingBatch(list.ConvertAll(r => (RentBuilding)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — all 7 `RentBuilding` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/RentBuildings_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch for RentBuilding+RentUnit on SqlServerStoredProcDatabase"
```

---

### Task 12: `Transaction` (compound — owned `Splits`/`Investment`, Transfer FK) + `SaveTransfer`

Most complex task. `dbo.Transactions_SaveBatch` takes **three** TVPs: `@Transactions`
(version-checked), `@Splits`, `@Investments` (neither versioned — owned children, gated only by the
parent transaction's version). `Transactions_SelectAll`'s existing order: `Id(0), Number(1), Date(2),
Amount(3), Account(4), Status(5), Memo(6), Payee(7), Category(8), FITID(9), SalesTax(10), Flags(11),
ReconciledDate(12), BudgetBalanceDate(13), MergeDate(14), OriginalPayee(15), Transfer(16),
TransferSplit(17)` — `Version` becomes index 18. `Investment.Id` always equals its owning
`Transaction.Id` (1:1, no separate sequence — confirmed by `Investments_SelectAll`/`ReadTransactions`
both keying investments by the transaction's own `Id`). `Investment` has no `Parent`/`RemoveChild`
(unlike `Split`/`RentUnit`) — it's a plain property on `Transaction`, not a `PersistentContainer`
member; `MockDatabase.MarkOwnedChildrenClean`'s existing code only calls `OnUpdated()` for a
non-deleted investment and does nothing at all for a deleted one — match that exactly, don't invent
a `RemoveChild` call that doesn't exist for this type.

**Transfer's self-referencing FK** (see design doc): both `Transactions.Transfer`/`TransferSplit`
and `Splits.Transfer` are inserted as `NULL` first, then a separate follow-up `UPDATE` sets the real
cross-reference once every row in the batch already exists — safe regardless of whether SQL Server
would have validated a self-referencing FK per-row or per-statement within one multi-row `INSERT`.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Access/Transactions_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`

- [ ] **Step 1: Confirm failing tests** — `SaveOne_NewTransaction_PersistsAndSetsRowVersionToOne`,
  `SaveOne_UpdateTransactionAfterReload_IncrementsRowVersion`,
  `SaveOne_StaleTransactionRowVersion_ThrowsConcurrencyConflictException`,
  `SaveOne_DeleteTransaction_RemovesRowFromDatabaseAndContainer`,
  `SaveOne_TransactionWithNewSplit_PersistsSplitAndSetsItClean`,
  `SaveOne_TransactionWithSplitOnlyEdit_PersistsSplitEvenThoughTransactionItselfIsUnchanged`,
  `SaveOne_DeleteTransactionWithSplits_RemovesSplitsBeforeTransactionRow`,
  `SaveOne_TransactionWithInvestment_PersistsInvestment`,
  `SaveTransfer_CommitsBothTransactionsTogether`, `SaveTransfer_ConflictOnOneSideRollsBackBoth`.

- [ ] **Step 2: Append to `Transactions_AccessProcs.sql`**

```sql
IF TYPE_ID(N'dbo.TransactionSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.TransactionSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id BIGINT NOT NULL,
        Number NVARCHAR(10) NULL,
        Account INT NULL,
        Date DATETIME NULL,
        Amount MONEY NULL,
        Status INT NULL,
        Memo NVARCHAR(255) NULL,
        Payee INT NULL,
        Category INT NULL,
        FITID NVARCHAR(40) NULL,
        SalesTax MONEY NULL,
        Flags INT NULL,
        ReconciledDate DATETIME NULL,
        BudgetBalanceDate DATETIME NULL,
        MergeDate DATETIME NULL,
        OriginalPayee NVARCHAR(255) NULL,
        Transfer BIGINT NULL,
        TransferSplit INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

IF TYPE_ID(N'dbo.SplitRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.SplitRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        [Transaction] BIGINT NOT NULL,
        Amount MONEY NULL,
        Category INT NULL,
        Memo NVARCHAR(255) NULL,
        Transfer BIGINT NULL,
        Payee INT NULL,
        Flags INT NULL,
        BudgetBalanceDate DATETIME NULL
    )');
END
GO

IF TYPE_ID(N'dbo.InvestmentRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.InvestmentRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id BIGINT NOT NULL,
        Security INT NULL,
        UnitPrice MONEY NULL,
        Units MONEY NULL,
        Commission MONEY NULL,
        InvestmentType INT NULL,
        TradeType INT NULL,
        TaxExempt BIT NULL,
        Withholding MONEY NULL,
        MarkUpDown MONEY NULL,
        Taxes MONEY NULL,
        Fees MONEY NULL,
        [Load] MONEY NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_SaveBatch
    @Transactions dbo.TransactionSaveBatchRow READONLY,
    @Splits dbo.SplitRow READONLY,
    @Investments dbo.InvestmentRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Transactions r
        LEFT JOIN dbo.Transactions t ON t.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (t.Id IS NULL OR t.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(t.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Transactions r LEFT JOIN dbo.Transactions t ON t.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (t.Id IS NULL OR t.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- Children before parent on delete.
    DELETE s FROM dbo.Splits s JOIN @Splits r ON s.Id = r.Id AND s.[Transaction] = r.[Transaction] WHERE r.[Action] = 'D';
    DELETE i FROM dbo.Investments i JOIN @Investments r ON i.Id = r.Id WHERE r.[Action] = 'D';
    DELETE t FROM dbo.Transactions t JOIN @Transactions r ON t.Id = r.Id WHERE r.[Action] = 'D';

    -- Parent before children on insert. Transfer/TransferSplit inserted NULL first - see this
    -- task's own note on why, above.
    INSERT INTO dbo.Transactions (Id, Number, Account, Date, Amount, Status, Memo, Payee, Category,
        Transfer, TransferSplit, FITID, SalesTax, Flags, ReconciledDate, BudgetBalanceDate, MergeDate,
        OriginalPayee, Version)
    SELECT Id, Number, Account, Date, Amount, Status, Memo, Payee, Category, NULL, NULL, FITID,
        SalesTax, Flags, ReconciledDate, BudgetBalanceDate, MergeDate, OriginalPayee, 1
    FROM @Transactions WHERE [Action] = 'I';

    UPDATE t SET Transfer = r.Transfer, TransferSplit = r.TransferSplit
    FROM dbo.Transactions t JOIN @Transactions r ON t.Id = r.Id
    WHERE r.[Action] = 'I' AND r.Transfer IS NOT NULL;

    UPDATE t SET
        Number = r.Number, Account = r.Account, Date = r.Date, Amount = r.Amount, Status = r.Status,
        Memo = r.Memo, Payee = r.Payee, Category = r.Category, Transfer = r.Transfer,
        TransferSplit = r.TransferSplit, FITID = r.FITID, SalesTax = r.SalesTax, Flags = r.Flags,
        ReconciledDate = r.ReconciledDate, BudgetBalanceDate = r.BudgetBalanceDate, MergeDate = r.MergeDate,
        OriginalPayee = r.OriginalPayee, Version = t.Version + 1
    FROM dbo.Transactions t JOIN @Transactions r ON t.Id = r.Id
    WHERE r.[Action] = 'U';

    INSERT INTO dbo.Splits (Id, [Transaction], Amount, Category, Memo, Transfer, Payee, Flags, BudgetBalanceDate)
    SELECT Id, [Transaction], Amount, Category, Memo, NULL, Payee, Flags, BudgetBalanceDate
    FROM @Splits WHERE [Action] = 'I';

    UPDATE s SET Transfer = r.Transfer
    FROM dbo.Splits s JOIN @Splits r ON s.Id = r.Id AND s.[Transaction] = r.[Transaction]
    WHERE r.[Action] = 'I' AND r.Transfer IS NOT NULL;

    UPDATE s SET Amount = r.Amount, Category = r.Category, Memo = r.Memo, Transfer = r.Transfer,
        Payee = r.Payee, Flags = r.Flags, BudgetBalanceDate = r.BudgetBalanceDate
    FROM dbo.Splits s JOIN @Splits r ON s.Id = r.Id AND s.[Transaction] = r.[Transaction]
    WHERE r.[Action] = 'U';

    INSERT INTO dbo.Investments (Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType,
        TaxExempt, Withholding, MarkUpDown, Taxes, Fees, [Load])
    SELECT Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType, TaxExempt, Withholding,
        MarkUpDown, Taxes, Fees, [Load]
    FROM @Investments WHERE [Action] = 'I';

    UPDATE i SET Security = r.Security, UnitPrice = r.UnitPrice, Units = r.Units, Commission = r.Commission,
        InvestmentType = r.InvestmentType, TradeType = r.TradeType, TaxExempt = r.TaxExempt,
        Withholding = r.Withholding, MarkUpDown = r.MarkUpDown, Taxes = r.Taxes, Fees = r.Fees, [Load] = r.[Load]
    FROM dbo.Investments i JOIN @Investments r ON i.Id = r.Id
    WHERE r.[Action] = 'U';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Transactions WHERE Id IN (SELECT Id FROM @Transactions WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Transactions_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.TransactionSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.TransactionSaveBatchRow TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.SplitRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.SplitRow TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.InvestmentRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.InvestmentRow TO MyMoneyTest;
GO
```

Also add `, Version` to `Transactions_SelectAll`'s SELECT list. Run the file against Redmond as
`MyMoneyAdmin`.

- [ ] **Step 3: Extend `ReadTransactions`**

Add `t.RowVersion = reader.GetInt64(18);` right after `t.BatchMode = false;` and before
`t.OnUpdated();` in the first `while (reader.Read())` loop (the one reading `Transactions_SelectAll`
directly — not the Splits or Investments loops, which read different result sets and don't touch
`RowVersion`).

- [ ] **Step 4: Add the three `NewXxxRowTable` helpers, `SaveTransactionBatch`, `SaveTransfer`,
  extend `SaveBatch`**

```csharp
        public override void SaveTransfer(Transaction from, Transaction to)
        {
            this.SaveBatch(new PersistentObject[] { from, to });
        }

        private static DataTable NewTransactionRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(long));
            table.Columns.Add("Number", typeof(string));
            table.Columns.Add("Account", typeof(int));
            table.Columns.Add("Date", typeof(DateTime));
            table.Columns.Add("Amount", typeof(decimal));
            table.Columns.Add("Status", typeof(int));
            table.Columns.Add("Memo", typeof(string));
            table.Columns.Add("Payee", typeof(int));
            table.Columns.Add("Category", typeof(int));
            table.Columns.Add("FITID", typeof(string));
            table.Columns.Add("SalesTax", typeof(decimal));
            table.Columns.Add("Flags", typeof(int));
            table.Columns.Add("ReconciledDate", typeof(DateTime));
            table.Columns.Add("BudgetBalanceDate", typeof(DateTime));
            table.Columns.Add("MergeDate", typeof(DateTime));
            table.Columns.Add("OriginalPayee", typeof(string));
            table.Columns.Add("Transfer", typeof(long));
            table.Columns.Add("TransferSplit", typeof(int));
            table.Columns.Add("ExpectedVersion", typeof(long));
            return table;
        }

        private static DataTable NewSplitRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("Transaction", typeof(long));
            table.Columns.Add("Amount", typeof(decimal));
            table.Columns.Add("Category", typeof(int));
            table.Columns.Add("Memo", typeof(string));
            table.Columns.Add("Transfer", typeof(long));
            table.Columns.Add("Payee", typeof(int));
            table.Columns.Add("Flags", typeof(int));
            table.Columns.Add("BudgetBalanceDate", typeof(DateTime));
            return table;
        }

        private static DataTable NewInvestmentRowTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add("Action", typeof(string));
            table.Columns.Add("Id", typeof(long));
            table.Columns.Add("Security", typeof(int));
            table.Columns.Add("UnitPrice", typeof(decimal));
            table.Columns.Add("Units", typeof(decimal));
            table.Columns.Add("Commission", typeof(decimal));
            table.Columns.Add("InvestmentType", typeof(int));
            table.Columns.Add("TradeType", typeof(int));
            table.Columns.Add("TaxExempt", typeof(bool));
            table.Columns.Add("Withholding", typeof(decimal));
            table.Columns.Add("MarkUpDown", typeof(decimal));
            table.Columns.Add("Taxes", typeof(decimal));
            table.Columns.Add("Fees", typeof(decimal));
            table.Columns.Add("Load", typeof(decimal));
            return table;
        }

        private void SaveTransactionBatch(List<Transaction> transactions, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            DataTable transactionRows = NewTransactionRowTable();
            DataTable splitRows = NewSplitRowTable();
            DataTable investmentRows = NewInvestmentRowTable();
            Dictionary<long, PersistentObject> rootsById = new Dictionary<long, PersistentObject>();
            Dictionary<long, Transaction> byId = new Dictionary<long, Transaction>();
            List<Transaction> deletedTransactions = new List<Transaction>();
            List<Split> savedSplits = new List<Split>();
            List<Split> deletedSplits = new List<Split>();
            List<Investment> savedInvestments = new List<Investment>();

            foreach (Transaction t in transactions)
            {
                long id = t.Id;
                rootsById[id] = t;
                byId[id] = t;
                long callerRowVersion = t.RowVersion;
                object account = t.Account != null ? (object)t.Account.Id : DBNull.Value;
                object payee = t.Payee != null ? (object)t.Payee.Id : DBNull.Value;
                object category = t.Category != null ? (object)t.Category.Id : DBNull.Value;
                object transferTarget = t.Transfer != null && t.Transfer.Transaction != null ? (object)t.Transfer.Transaction.Id : DBNull.Value;
                object transferSplit = t.Transfer != null && t.Transfer.Split != null ? (object)t.Transfer.Split.Id : DBNull.Value;
                object date = SqlServerDatabase.DBDateTimeParam(t.Date);
                object reconciledDate = SqlServerDatabase.DBNullableDateTimeParam(t.ReconciledDate);
                object budgetBalanceDate = SqlServerDatabase.DBNullableDateTimeParam(t.BudgetBalanceDate);
                object mergeDate = SqlServerDatabase.DBNullableDateTimeParam(t.MergeDate);

                if (t.IsInserted)
                {
                    transactionRows.Rows.Add("I", t.Id, t.Number, account, date, t.Amount, (int)t.Status,
                        t.Memo, payee, category, t.FITID, t.SalesTax, (int)t.Flags, reconciledDate,
                        budgetBalanceDate, mergeDate, t.OriginalPayee, transferTarget, transferSplit, DBNull.Value);
                }
                else if (t.IsChanged)
                {
                    transactionRows.Rows.Add("U", t.Id, t.Number, account, date, t.Amount, (int)t.Status,
                        t.Memo, payee, category, t.FITID, t.SalesTax, (int)t.Flags, reconciledDate,
                        budgetBalanceDate, mergeDate, t.OriginalPayee, transferTarget, transferSplit, callerRowVersion);
                }
                else if (t.IsDeleted)
                {
                    transactionRows.Rows.Add("D", t.Id, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                        DBNull.Value, callerRowVersion);
                    deletedTransactions.Add(t);
                }

                if (t.Splits != null)
                {
                    List<Split> splitsSnapshot = new List<Split>();
                    foreach (Split s in t.Splits)
                    {
                        splitsSnapshot.Add(s);
                    }

                    foreach (Split s in splitsSnapshot)
                    {
                        object splitCategory = s.Category != null ? (object)s.Category.Id : DBNull.Value;
                        object splitPayee = s.Payee != null ? (object)s.Payee.Id : DBNull.Value;
                        object splitTransfer = s.Transfer != null && s.Transfer.Transaction != null ? (object)s.Transfer.Transaction.Id : DBNull.Value;
                        object splitBudgetBalanceDate = SqlServerDatabase.DBNullableDateTimeParam(s.BudgetBalanceDate);

                        if (s.IsInserted)
                        {
                            splitRows.Rows.Add("I", s.Id, t.Id, s.Amount, splitCategory, s.Memo,
                                splitTransfer, splitPayee, (int)s.Flags, splitBudgetBalanceDate);
                            savedSplits.Add(s);
                        }
                        else if (s.IsChanged)
                        {
                            splitRows.Rows.Add("U", s.Id, t.Id, s.Amount, splitCategory, s.Memo,
                                splitTransfer, splitPayee, (int)s.Flags, splitBudgetBalanceDate);
                            savedSplits.Add(s);
                        }
                        else if (s.IsDeleted)
                        {
                            splitRows.Rows.Add("D", s.Id, t.Id, DBNull.Value, DBNull.Value, DBNull.Value,
                                DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
                            deletedSplits.Add(s);
                        }
                    }
                }

                if (t.Investment != null)
                {
                    Investment i = t.Investment;
                    object security = i.Security != null ? (object)i.Security.Id : DBNull.Value;

                    if (i.IsInserted)
                    {
                        investmentRows.Rows.Add("I", i.Id, security, i.UnitPrice, i.Units, i.Commission,
                            (int)i.Type, (int)i.TradeType, i.TaxExempt, i.Withholding, i.MarkUpDown,
                            i.Taxes, i.Fees, i.Load);
                        savedInvestments.Add(i);
                    }
                    else if (i.IsChanged)
                    {
                        investmentRows.Rows.Add("U", i.Id, security, i.UnitPrice, i.Units, i.Commission,
                            (int)i.Type, (int)i.TradeType, i.TaxExempt, i.Withholding, i.MarkUpDown,
                            i.Taxes, i.Fees, i.Load);
                        savedInvestments.Add(i);
                    }
                    else if (i.IsDeleted)
                    {
                        investmentRows.Rows.Add("D", i.Id, DBNull.Value, DBNull.Value, DBNull.Value,
                            DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                            DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);
                        // No savedInvestments.Add here and no separate "deleted" tracking either -
                        // Investment has no Parent/RemoveChild (unlike Split/RentUnit), and
                        // MockDatabase.MarkOwnedChildrenClean's own established behavior does
                        // nothing at all for a deleted investment. Match that exactly.
                    }
                }
            }

            if (transactionRows.Rows.Count == 0 && splitRows.Rows.Count == 0 && investmentRows.Rows.Count == 0)
            {
                return;
            }

            this.ExecuteSaveBatchProc(connection, transaction, "dbo.Transactions_SaveBatch",
                new[]
                {
                    ("@Transactions", "dbo.TransactionSaveBatchRow", transactionRows),
                    ("@Splits", "dbo.SplitRow", splitRows),
                    ("@Investments", "dbo.InvestmentRow", investmentRows)
                },
                rootsById,
                (id, newVersion) =>
                {
                    Transaction t = byId[id];
                    postCommitActions.Add(() =>
                    {
                        t.RowVersion = newVersion;
                        t.OnUpdated();
                    });
                });

            foreach (Transaction t in deletedTransactions)
            {
                postCommitActions.Add(() =>
                {
                    t.OnUpdated();
                    t.Parent.RemoveChild(t, true);
                });
            }

            foreach (Split s in savedSplits)
            {
                postCommitActions.Add(() => s.OnUpdated());
            }

            foreach (Split s in deletedSplits)
            {
                postCommitActions.Add(() => s.Parent.RemoveChild(s, true));
            }

            foreach (Investment i in savedInvestments)
            {
                postCommitActions.Add(() => i.OnUpdated());
            }
        }
```

In `SaveBatch`, add `else if (firstType == typeof(Transaction)) { this.SaveTransactionBatch(list.ConvertAll(r => (Transaction)r), connection, null, postCommitActions); }` before the final `else`.

- [ ] **Step 5: Run SQL Server contract tests** — all 10 `Transaction`/`SaveTransfer` tests now pass.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Transactions_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git commit -m "Implement real SaveOne/SaveBatch/SaveTransfer for Transaction on SqlServerStoredProcDatabase"
```

At this point every `IAggregateRoot` type has real `SaveOne`/`SaveBatch` support — the full
`DatabaseContractTests` suite (minus the still-open mixed-type case, Task 13) should be green
against `SqlServerDatabaseContractTests`.

---

### Task 13: Genuinely mixed-entity-type `SaveBatch` (ambient transaction, nested-rollback handling)

Every task so far dispatches a batch to exactly one entity's `SaveXxxBatch` method, since every
current real caller (`SaveOne`, `SaveTransfer`, and the same-type batch conflict test) only ever
sends a single type. This task adds real support for a batch spanning more than one entity type
(e.g. one `Category` + one `Account` together) — contractually legal per
`IDatabase.SaveBatch(IEnumerable<PersistentObject>)`'s signature, but untested until now. Each
entity's `_SaveBatch` proc owns its own internal `BEGIN TRANSACTION`/`COMMIT`/`ROLLBACK`; true
atomicity across two separate proc calls requires an ambient `SqlTransaction` opened by C# first, so
each proc's internal `BEGIN TRAN` becomes a *nested* transaction — SQL Server's rule that `ROLLBACK
TRANSACTION` always unwinds to the *outermost* `BEGIN`, regardless of nesting depth, is what makes
this correct: if a later group's proc conflicts and rolls back, it correctly undoes every earlier
group's work too (an inner `COMMIT` just decrements the nesting counter; only the true outer
`Commit()` finalizes anything).

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (rewrite `SaveBatch`, add
  `IsSupportedSaveBatchType`/`DispatchSaveBatchForType`)
- Modify: `Source/WPF/MyMoney.TestSupport/DatabaseContractTests.cs` (new shared test)

**Interfaces:**
- Consumes: every `SaveXxxBatch` method from Tasks 2–12 (all already accept a `SqlTransaction
  transaction` parameter, used as `null` until now).
- Produces: nothing new for later tasks — this is the last SQL Server code change in this plan.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/MyMoney.TestSupport/DatabaseContractTests.cs` (a general-purpose,
non-entity-specific test, alongside `SaveAndReload_ResolvesAccountCategoryAndInvestmentSecurityForwardReferences`):

```csharp
        [Test]
        public void SaveBatch_MixedEntityTypesAcrossOneBatch_CommitsOrRollsBackTogether()
        {
            MyMoney money = new MyMoney();
            Category category = money.Categories.GetOrCreateCategory("MixedBatchCategory", CategoryType.Expense);
            Account account = money.Accounts.AddAccount("MixedBatchAccount");
            this.Database.SaveBatch(new PersistentObject[] { category, account });

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Categories.FindCategory("MixedBatchCategory"), Is.Not.Null);
            Assert.That(reloaded.Accounts.FindAccount("MixedBatchAccount"), Is.Not.Null);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Account staleAccount = readerA.Accounts.FindAccount("MixedBatchAccount");
            staleAccount.Description = "Attempted stale";

            Category freshCategory = readerB.Categories.FindCategory("MixedBatchCategory");
            freshCategory.Description = "Attempted fresh";

            // Someone else updates the account first, so staleAccount's RowVersion is now behind.
            MyMoney otherWriter = this.Database.Load(null);
            Account otherAccount = otherWriter.Accounts.FindAccount("MixedBatchAccount");
            otherAccount.Description = "Changed elsewhere";
            this.Database.SaveOne(otherAccount);

            // freshCategory listed FIRST deliberately - same reasoning as
            // SaveBatch_OneStaleRootAmongMany_RollsBackTransactionAndPreservesInMemoryState: its
            // own type-group's proc call "succeeds" (commits, or nests-and-decrements under an
            // ambient transaction) before staleAccount's type-group conflicts on the next call.
            Assert.Throws<ConcurrencyConflictException>(
                () => this.Database.SaveBatch(new PersistentObject[] { freshCategory, staleAccount }));

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Categories.FindCategory("MixedBatchCategory").Description, Is.Not.EqualTo("Attempted fresh"));
            Assert.That(freshCategory.IsChanged, Is.True);
        }
```

- [ ] **Step 2: Run it against all three engines to confirm the current state**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SaveBatch_MixedEntityTypesAcrossOneBatch"`
Expected: PASS against `MockDatabaseContractTests` and `SqliteDatabaseContractTests` (neither
engine ever special-cased type — `MockDatabase` re-serializes the whole graph regardless of type
mix; `SqliteDatabase` loops every root in one shared `SQLiteTransaction` regardless of type). FAIL
against `SqlServerDatabaseContractTests` (with the SQL Server env vars set) — `SaveBatch`'s current
`Type firstType = list[0].GetType()` guard sends a genuinely mixed-type batch to `base.SaveBatch`,
the `NotImplementedException` stub.

- [ ] **Step 3: Replace `SaveBatch` with the grouped, ambient-transaction-aware version**

Replace the whole `SaveBatch` method (built incrementally across Tasks 2–12) with:

```csharp
        public override void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            List<PersistentObject> list = new List<PersistentObject>(roots);
            if (list.Count == 0)
            {
                return;
            }

            Dictionary<Type, List<PersistentObject>> groupedByType = new Dictionary<Type, List<PersistentObject>>();
            foreach (PersistentObject root in list)
            {
                Type type = root.GetType();
                if (!IsSupportedSaveBatchType(type))
                {
                    base.SaveBatch(list);
                    return;
                }
                if (!groupedByType.TryGetValue(type, out List<PersistentObject> group))
                {
                    group = new List<PersistentObject>();
                    groupedByType[type] = group;
                }
                group.Add(root);
            }

            this.Connect();
            using (SqlConnection connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                List<Action> postCommitActions = new List<Action>();

                if (groupedByType.Count == 1)
                {
                    // Common case: one entity type, no ambient transaction needed - the proc's own
                    // internal BEGIN TRAN/COMMIT/ROLLBACK is already a complete, standalone
                    // transaction.
                    foreach (var group in groupedByType)
                    {
                        this.DispatchSaveBatchForType(group.Key, group.Value, connection, null, postCommitActions);
                    }
                    foreach (Action action in postCommitActions)
                    {
                        action();
                    }
                    return;
                }

                using (SqlTransaction ambientTransaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var group in groupedByType)
                        {
                            this.DispatchSaveBatchForType(group.Key, group.Value, connection, ambientTransaction, postCommitActions);
                        }
                        ambientTransaction.Commit();
                    }
                    catch (Exception originalException)
                    {
                        try
                        {
                            ambientTransaction.Rollback();
                        }
                        catch (InvalidOperationException)
                        {
                            // A _SaveBatch proc's own internal ROLLBACK TRANSACTION on conflict
                            // already unwound the WHOLE ambient transaction (SQL Server's
                            // nested-transaction rule: ROLLBACK always targets the outermost
                            // BEGIN, regardless of nesting depth) - by the time control returns
                            // here, @@TRANCOUNT is already 0 and this SqlTransaction object is
                            // "zombied". That's success, not a new failure: only a genuinely new
                            // rollback failure (any other exception type) should mask/wrap the
                            // original exception, matching SqliteDatabase.SaveBatch's identical
                            // never-hide-the-original-exception rule.
                            _ = originalException;
                        }
                        throw;
                    }
                    foreach (Action action in postCommitActions)
                    {
                        action();
                    }
                }
            }
        }

        private static bool IsSupportedSaveBatchType(Type type)
        {
            return type == typeof(Category) || type == typeof(Currency) || type == typeof(OnlineAccount)
                || type == typeof(Account) || type == typeof(Payee) || type == typeof(Alias)
                || type == typeof(Security) || type == typeof(StockSplit) || type == typeof(LoanPayment)
                || type == typeof(RentBuilding) || type == typeof(Transaction);
        }

        private void DispatchSaveBatchForType(Type type, List<PersistentObject> roots, SqlConnection connection, SqlTransaction transaction, List<Action> postCommitActions)
        {
            if (type == typeof(Category))
            {
                this.SaveCategoryBatch(roots.ConvertAll(r => (Category)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(Currency))
            {
                this.SaveCurrencyBatch(roots.ConvertAll(r => (Currency)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(OnlineAccount))
            {
                this.SaveOnlineAccountBatch(roots.ConvertAll(r => (OnlineAccount)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(Account))
            {
                this.SaveAccountBatch(roots.ConvertAll(r => (Account)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(Payee))
            {
                this.SavePayeeBatch(roots.ConvertAll(r => (Payee)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(Alias))
            {
                this.SaveAliasBatch(roots.ConvertAll(r => (Alias)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(Security))
            {
                this.SaveSecurityBatch(roots.ConvertAll(r => (Security)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(StockSplit))
            {
                this.SaveStockSplitBatch(roots.ConvertAll(r => (StockSplit)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(LoanPayment))
            {
                this.SaveLoanPaymentBatch(roots.ConvertAll(r => (LoanPayment)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(RentBuilding))
            {
                this.SaveRentBuildingBatch(roots.ConvertAll(r => (RentBuilding)r), connection, transaction, postCommitActions);
            }
            else if (type == typeof(Transaction))
            {
                this.SaveTransactionBatch(roots.ConvertAll(r => (Transaction)r), connection, transaction, postCommitActions);
            }
        }
```

- [ ] **Step 4: Run the new test against all three engines**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SaveBatch_MixedEntityTypesAcrossOneBatch"`
Expected: PASS against all three (`MockDatabaseContractTests`, `SqliteDatabaseContractTests`,
`SqlServerDatabaseContractTests`).

- [ ] **Step 5: Run the full SQL Server contract suite**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqlServerDatabaseContractTests"`
Expected: every test passes — this is the first fully-green run of the whole shared suite against
SQL Server.

- [ ] **Step 6: Full build and regression check** — same commands as Task 2 Step 6.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoney.TestSupport/DatabaseContractTests.cs
git commit -m "Support genuinely mixed-entity-type SaveBatch on SqlServerStoredProcDatabase"
```

---

### Task 14: `_Test_Reset` procs for the 10 remaining `IAggregateRoot` tables

**Scope correction found while planning this task**: the design doc's "Test-support stored procs"
section assumed `Payees_Test_Reset` (the one existing example) was already wired into
`SqlServerTestDatabase.WipeAllTables()`. It isn't — grepped the whole codebase and confirmed
`Payees_Test_Reset` is never called from anywhere, and no `MyMoneyTest`-tier connection-string
environment variable exists at all (only `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`, `MyMoneyUser`
tier, and `MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`, `MyMoneyAdmin` tier, are wired up anywhere).
Actually switching `WipeAllTables()` over would mean introducing a brand-new required environment
variable and changing the documented "three environment variables" contract
(`docs/dev/index.md`, `SqlServerDatabaseContractTests`'s own doc comment) — a bigger change than a
ride-along fix warrants this late in an already-large plan. This task does the concrete, additive
half only: define the missing procs, matching the one already-established (if unused) example, so
they exist and are grantable whenever that wiring work is picked up. `WipeAllTables()` itself is
untouched — still functionally correct via the admin connection, just not yet tiered. Recorded
under "What's next" below, not silently dropped.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlScripts/Test/Categories_TestProcs.sql`,
  `Currencies_TestProcs.sql`, `OnlineAccounts_TestProcs.sql`, `Accounts_TestProcs.sql`,
  `Aliases_TestProcs.sql`, `Securities_TestProcs.sql`, `StockSplits_TestProcs.sql`,
  `LoanPayments_TestProcs.sql`, `RentBuildings_TestProcs.sql`, `Transactions_TestProcs.sql` (all
  new files, one per entity, matching `Payees_TestProcs.sql`'s existing granularity)

- [ ] **Step 1: Create the 10 new test-proc files**

Each follows `Payees_TestProcs.sql`'s exact shape. Example (`Categories_TestProcs.sql`):

```sql
-- Categories_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all Categories rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Categories;
END
GO

GRANT EXECUTE ON dbo.Categories_Test_Reset TO MyMoneyTest;
GO
```

Create the other 9 identically, substituting the table name (`Currencies`, `OnlineAccounts`,
`Accounts`, `Aliases`, `Securities`, `StockSplits`, `LoanPayments`, `RentBuildings`,
`Transactions`) in the filename, proc name (`<Table>_Test_Reset`), comment, and `DELETE FROM`
target. Run each against Redmond as `MyMoneyAdmin`, same `sqlcmd -i` pattern as every earlier task.

- [ ] **Step 2: Verify the procs exist and are granted correctly**

```powershell
$creds = Get-Content "$env:USERPROFILE\.secrets\MyMoney\dataengine.credentials.json" | ConvertFrom-Json
$env:SQLCMDPASSWORD = $creds.MyMoneyAdmin.Password
sqlcmd -S Redmond -d MyMoney -U $creds.MyMoneyAdmin.UserId -C -Q "SELECT p.name FROM sys.procedures p WHERE p.name LIKE '%_Test_Reset' ORDER BY p.name"
```

Expected: 11 rows (the new 10 plus the pre-existing `Payees_Test_Reset`).

- [ ] **Step 3: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Test/
git commit -m "Add _Test_Reset procs for the remaining IAggregateRoot tables (not yet wired into WipeAllTables)"
```

---

### Task 15: Full regression check

**Files:** none — verification only.

- [ ] **Step 1: Full solution build**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.

- [ ] **Step 2: Full solution test run without SQL Server env vars**

Run: `dotnet test Source/WPF/MyMoney.sln -m:1`
Expected: matches the established baseline exactly (`MyMoney.TestSupport`'s `SqlServerDatabaseContractTests`
tests fail fast with the `MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE`/connection-string guard messages when
the SQL Server env vars aren't set — same as before this plan started; the 1 `ScenarioTest` FlaUI
flake is pre-existing and unrelated). No new failures anywhere else.

- [ ] **Step 3: Full solution test run WITH SQL Server env vars set**

```powershell
$creds = Get-Content "$env:USERPROFILE\.secrets\MyMoney\dataengine.credentials.json" | ConvertFrom-Json
$env:MYMONEY_TEST_SQLSERVER_USER_CONNECTION = "Data Source=Redmond;Initial Catalog=MyMoney;User ID=$($creds.MyMoneyUser.UserId);Password=$($creds.MyMoneyUser.Password);TrustServerCertificate=True"
$env:MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION = "Data Source=Redmond;Initial Catalog=MyMoney;User ID=$($creds.MyMoneyAdmin.UserId);Password=$($creds.MyMoneyAdmin.Password);TrustServerCertificate=True"
$env:MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE = "1"
dotnet test Source/WPF/MyMoney.sln -m:1
```

Expected: every test passes, including the full `SqlServerDatabaseContractTests` suite (the entire
point of this plan) and `SqlServerStoredProcDatabaseTests` (`Source/WPF/UnitTests`, which shares the
same live database). `-m:1` is required whenever these env vars are set — see `docs/dev/index.md`'s
"Required flag when SQL Server env vars are set" (without it, `MyMoney.TestSupport` and `UnitTests`
race against the same live database and can collide on primary keys).

- [ ] **Step 4: Confirm issue #19's original ask is now fully proven, not just wired up**

Re-read `docs/superpowers/specs/2026-09-17-persistence-concurrency-phase2c-design.md` and this
plan's own header against the actual final state: every `IAggregateRoot` type has a real
`SaveOne`/`SaveBatch` implementation on `SqlServerStoredProcDatabase`; `SaveTransfer` works;
mixed-entity-type batches are atomic; the full shared `DatabaseContractTests` suite passes
identically against `MockDatabase`, `SqliteDatabase`, and `SqlServerStoredProcDatabase`. No further
action needed here beyond confirming — this step is a checkpoint, not new work.

---

## What's next

- **Wire `_Test_Reset` procs into `SqlServerTestDatabase.WipeAllTables()`** (Task 14's deferred
  half): needs a new `MyMoneyTest`-tier connection-string environment variable
  (e.g. `MYMONEY_TEST_SQLSERVER_TEST_CONNECTION`), updates to `docs/dev/index.md`'s documented
  environment-variable contract, and care to preserve `TablesToWipe`'s existing FK-safe delete
  order when mixing tiered-proc calls (for the 11 tables with a `_Test_Reset` proc) with the
  remaining raw admin `DELETE`s (`Splits`, `Investments`, `TransactionExtras`, `AccountAliases`,
  `RentUnits` — none of which are `IAggregateRoot` types this plan added `_SaveBatch` support for).
- R5 (pull-based staleness/refresh) — SQL Server's native Change Tracking
  (`CHANGE_TRACKING_CURRENT_VERSION()`/`CHANGETABLE(CHANGES ...)`) is the recommended mechanism when
  this is picked up, per the design doc.
- A real multi-connection SQLite contention test (still open from Phase 2b's own "what's next").
- The detached-root `InvalidOperationException` path and other untested `SaveBatch` edge cases
  already flagged in prior-phase notes remain deferred, unrelated to this plan's scope.
- Cross-engine parity is now complete for `SaveOne`/`SaveBatch`/`SaveTransfer`: `MockDatabase`,
  `SqliteDatabase`, and `SqlServerStoredProcDatabase` all pass the identical shared contract suite.
  The persistence-concurrency redesign's core arc (Phase 1 → 2a → 2b → entity-extension →
  RentBuilding → Transaction → test consolidation → Phase 2c) is done.
