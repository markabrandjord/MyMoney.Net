# SQL Server `MyMoneyUser` CRUD Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the `MyMoneyUser` SQL Server login (the tier the shipped app actually connects as) full CRUD access — via stored procedures, matching the existing Payees pattern — to Accounts, Categories, Currencies, Securities, StockSplits, Aliases, Transactions, Splits, and Investment, closing GitHub issue #22.

**Architecture:** `SqlServerStoredProcDatabase` (in `MyMoney.Data`) gets one `ReadXxx`/`UpdateXxx` override pair per entity (three pairs for Transactions/Splits/Investment, mirroring how the generic base class already treats them as three separately-updated tables), each calling a new stored-proc script deployed by `MyMoneyAdmin`'s bootstrap. A shared `ExecuteProc` helper removes per-entity ADO.NET boilerplate. `Load()` is extended to read all 9 entities instead of just Payees. A new `SqlServerDatabaseContractTests` fixture (in `MyMoney.TestSupport`) proves the whole thing works under the `MyMoneyUser` tier using the existing shared `DatabaseContractTests` suite.

**Tech Stack:** .NET 10 (`net10.0-windows7.0`), `Microsoft.Data.SqlClient`, T-SQL stored procedures, NUnit 4.6.1.

**Spec:** `docs/superpowers/specs/2026-09-15-sql-server-user-tier-crud-design.md`

## Global Constraints

- Scope is exactly 9 entities: Accounts, Categories, Currencies, Securities, StockSplits, Aliases, Transactions, Splits, Investment. Everything else (OnlineAccounts, AccountAliases, TransactionExtras, RentBuildings, RentUnits, LoanPayments) is out of scope — tracked separately as issue #23.
- Every new stored-proc script follows `Payees_AccessProcs.sql`'s exact template: header comment naming the file and "Run as the MyMoneyAdmin login against the MyMoney database", `USE MyMoney; GO`, `CREATE OR ALTER PROCEDURE` per operation, `GRANT EXECUTE` to both `MyMoneyUser` and `MyMoneyTest` at the end.
- Transactions, Splits, and Investment are three independent proc sets and three independent `SqlServerStoredProcDatabase` override methods — never one atomic parent+children proc. This matches how `SqlDatabase.UpdateTransactions`/`UpdateSplits`/`UpdateInvestment` already treat them as separately-updated tables.
- All new entity CRUD methods on `SqlServerStoredProcDatabase` call the shared `ExecuteProc(SqlConnection, string procName, params (string Name, object Value)[] parameters)` helper (Task 1) instead of hand-rolling `SqlCommand`/parameter-binding code, and use the same local-`SqlConnection`-per-call style `ExecutePayeeProc` already uses — never the base class's different ambient-connection/`this.transaction` style.
- `SqlServerDatabaseContractTests` (Task 11) connects as `MyMoneyUser` via environment variable `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` (same variable `SqlServerStoredProcDatabaseTests` already uses) and **must fail, not skip/Ignore, if that variable is unset or the connection fails** — this is a deliberate deviation from `SqlServerStoredProcDatabaseTests`'s `Assert.Ignore` pattern; do not "fix" it back.
- Table-cleanup between contract-test runs (Task 11) connects as `MyMoneyAdmin` via a separate new environment variable, `MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION` — used only for cleanup, never as the `IDatabase` under test.
- No change to the tiered-security model itself, to `MyMoneyAdmin`'s bootstrap process beyond what's specified here, to the SQLite/generic `SqlServerDatabase` CRUD paths, or to `SupportsParameterizedUpdate` (deliberately `SqliteDatabase`-only, unrelated to this plan).
- Every new/changed `.sql` script must actually get deployed to the shared SQL Server test instance before its task's tests can pass — each task's steps say exactly how (via `MyMoneyAdmin.exe`, since `BootstrapRunner` re-running is idempotent).

---

### Task 1: Shared `ExecuteProc` helper, visibility promotions, and schema bootstrap for the 9 new tables

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (promote `DBDateTimeParam`, `DBNullableDateTimeParam`, `DBGuidParam` from `private static` to `internal static` — no body changes)
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ExecuteProc` helper, refactor `ExecutePayeeProc` to call it)
- Modify: `Source/WPF/MyMoneyAdmin/BootstrapRunner.cs` (create the 9 new tables' schema via `LazyCreateTables()`, as `MyMoneyAdmin`, before deploying any of this plan's new access-proc scripts)

**Interfaces:**
- Produces: `Walkabout.Data.SqlServerStoredProcDatabase.ExecuteProc(SqlConnection connection, string procName, params (string Name, object Value)[] parameters)` (private static) — every later task's `UpdateXxx` override calls this.
- Produces: `Walkabout.Data.SqlDatabase.DBDateTimeParam(DateTime)`, `DBNullableDateTimeParam(DateTime?)`, `DBGuidParam(SqlGuid)` now `internal static` (unchanged signatures/bodies) — later tasks' `UpdateAccounts`/`UpdateStockSplits`/`UpdateTransactions`/`UpdateSplits` overrides call these directly (same-assembly `internal` access, no inheritance needed).
- Consumes: nothing new — this task is foundational.

- [ ] **Step 1: Run the existing Payees test to confirm today's green baseline**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerStoredProcDatabaseTests"`

Set the `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` environment variable to a working `MyMoneyUser` connection string first (e.g. `Server=<server>;Database=MyMoney;User Id=MyMoneyUser;Password=<password>;TrustServerCertificate=True;`), matching whatever the SQL Server instance from item #2's bootstrap is using.

Expected: `Passed! - Failed: 0, Passed: 1`. This is the baseline that Step 5 must still show after refactoring `ExecutePayeeProc`.

- [ ] **Step 2: Promote the three DB-parameter helpers to `internal static`**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, find:

```csharp
        private static object DBDateTimeParam(DateTime dt)
```

Change to:

```csharp
        internal static object DBDateTimeParam(DateTime dt)
```

Find:

```csharp
        private static object DBNullableDateTimeParam(DateTime? ndt)
```

Change to:

```csharp
        internal static object DBNullableDateTimeParam(DateTime? ndt)
```

Find:

```csharp
        private static object DBGuidParam(SqlGuid guid)
```

Change to:

```csharp
        internal static object DBGuidParam(SqlGuid guid)
```

No other changes to these three methods' bodies.

- [ ] **Step 3: Add the shared `ExecuteProc` helper and refactor `ExecutePayeeProc` to use it**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, add `using System;` and `using System.Data;` to the top of the file if not already present, then replace the existing `ExecutePayeeProc` method:

```csharp
        private static void ExecutePayeeProc(SqlConnection connection, string procName, Payee p)
        {
            using (var command = new SqlCommand(procName, connection) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                command.Parameters.AddWithValue("@Id", p.Id);
                command.Parameters.AddWithValue("@Name", (object)p.Name ?? System.DBNull.Value);
                command.ExecuteNonQuery();
            }
        }
```

with:

```csharp
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
```

- [ ] **Step 4: Add schema creation for the 9 new tables to `BootstrapRunner`**

In `Source/WPF/MyMoneyAdmin/BootstrapRunner.cs`, find:

```csharp
            Console.WriteLine("Deploying schema and stored procedures as 'MyMoneyAdmin'...");
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Schema", "001_CreatePayeesTable.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Payees_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Test", "Payees_TestProcs.sql")));
```

Replace with:

```csharp
            Console.WriteLine("Deploying schema and stored procedures as 'MyMoneyAdmin'...");
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Schema", "001_CreatePayeesTable.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Payees_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Test", "Payees_TestProcs.sql")));

            Console.WriteLine("Creating schema for Accounts/Categories/Currencies/Securities/StockSplits/Aliases/Transactions/Splits/Investments as 'MyMoneyAdmin'...");
            var adminDatabase = new SqlServerDatabase
            {
                Server = server,
                UserId = "MyMoneyAdmin",
                Password = adminPassword,
                DatabaseName = databaseName
            };
            adminDatabase.LazyCreateTables();

            Console.WriteLine("Deploying issue #22 access procedures as 'MyMoneyAdmin'...");
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Accounts_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Categories_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Currencies_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Securities_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "StockSplits_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Aliases_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Transactions_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Splits_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Investments_AccessProcs.sql")));
```

Note: `LazyCreateTables()` is idempotent (it checks `TableExists` per table before creating), and reflects over every `[TableMapping]`-decorated type in `MyMoney.Business` — including the 6 out-of-scope entities from issue #23. Creating their tables here too is harmless (empty, unused tables) and avoids having to hand-maintain a filtered list; issue #23 will add access procs for them later without needing any further schema-creation step.

This step references `Accounts_AccessProcs.sql` through `Investments_AccessProcs.sql`, which don't exist yet — that's expected; Tasks 2-10 create them. `BootstrapRunner` won't compile-fail (these are just string literals evaluated at runtime), but running the bootstrap before those files exist will throw a `FileNotFoundException`. Don't run the bootstrap again until Task 10 is complete.

- [ ] **Step 5: Rebuild and rerun the existing Payees test to confirm no regression**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: `Build succeeded.`, 0 errors.

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerStoredProcDatabaseTests"`
Expected: `Passed! - Failed: 0, Passed: 1` — same result as Step 1, proving the `ExecutePayeeProc` refactor didn't change behavior.

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/MyMoneyAdmin/BootstrapRunner.cs
git commit -m "Add shared ExecuteProc helper and schema bootstrap for issue #22's 9 entities"
```

---

### Task 2: Accounts

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Accounts_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ReadAccounts`, `UpdateAccounts` overrides; extend `Load()`)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteAccount_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1), `DBDateTimeParam`/`DBGuidParam` (Task 1, now `internal`), `ReadDbString`/`ReadInt32` (`Walkabout.Data.SqlDatabase`, already `internal static`).
- Produces: `Walkabout.Data.SqlServerStoredProcDatabase.ReadAccounts(Accounts, MyMoney)`, `UpdateAccounts(Accounts)` (both `public override`) — Task 8 (Transactions) calls `money.Accounts.FindAccountAt(id)` against data this loads.

- [ ] **Step 1: Write the failing test**

In `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs`, add (inside the `SqlServerStoredProcDatabaseTests` class, after the existing `InsertUpdateDeletePayee_RoundTripsThroughStoredProcedures` test):

```csharp
        [Test]
        public void InsertUpdateDeleteAccount_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests Account");
            account.Type = AccountType.Checking;
            account.Description = "Test account";
            account.OpeningBalance = 100.00m;
            account.OnInserted();

            db.UpdateAccounts(money.Accounts);

            var reloaded = new MyMoney();
            db.ReadAccounts(reloaded.Accounts, reloaded);
            var found = reloaded.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Account");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Type, Is.EqualTo(AccountType.Checking));
            Assert.That(found.Description, Is.EqualTo("Test account"));
            Assert.That(found.OpeningBalance, Is.EqualTo(100.00m));

            found.Description = "Updated description";
            db.UpdateAccounts(reloaded.Accounts);

            var afterUpdate = new MyMoney();
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            var updated = afterUpdate.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Account");
            Assert.That(updated.Description, Is.EqualTo("Updated description"));

            updated.OnDelete();
            var toDelete = new Accounts(afterUpdate);
            toDelete.Add(updated);
            db.UpdateAccounts(toDelete);

            var afterDelete = new MyMoney();
            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            Assert.That(afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Account"), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteAccount_RoundTripsThroughStoredProcedures"`
Expected: FAIL — `UpdateAccounts`/`ReadAccounts` aren't overridden yet, so this calls the base `SqlServerDatabase` versions, which hit `Accounts_SelectAll`/etc. procs that don't exist yet (or, if the base generic-SQL path runs instead, a permissions error since `MyMoneyUser` has no direct table grants). Either way: FAIL, not a compile error.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Accounts_AccessProcs.sql`:

```sql
-- Accounts_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Accounts table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount, OpeningBalance,
           LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
           CategoryIdForPrincipal, CategoryIdForInterest
    FROM dbo.Accounts ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Insert
    @Id INT, @AccountId NVARCHAR(20), @OfxAccountId NVARCHAR(50), @Name NVARCHAR(80), @Type INT,
    @Description NVARCHAR(255), @OnlineAccount INT, @OpeningBalance MONEY, @LastSync DATETIME,
    @LastBalance DATETIME, @SyncGuid UNIQUEIDENTIFIER, @Flags INT, @Currency NVARCHAR(10),
    @WebSite NVARCHAR(255), @ReconcileWarning INT, @CategoryIdForPrincipal INT, @CategoryIdForInterest INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Accounts (Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount,
        OpeningBalance, LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
        CategoryIdForPrincipal, CategoryIdForInterest)
    VALUES (@Id, @AccountId, @OfxAccountId, @Name, @Type, @Description, @OnlineAccount, @OpeningBalance,
        @LastSync, @LastBalance, @SyncGuid, @Flags, @Currency, @WebSite, @ReconcileWarning,
        @CategoryIdForPrincipal, @CategoryIdForInterest);
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Update
    @Id INT, @AccountId NVARCHAR(20), @OfxAccountId NVARCHAR(50), @Name NVARCHAR(80), @Type INT,
    @Description NVARCHAR(255), @OnlineAccount INT, @OpeningBalance MONEY, @LastSync DATETIME,
    @LastBalance DATETIME, @SyncGuid UNIQUEIDENTIFIER, @Flags INT, @Currency NVARCHAR(10),
    @WebSite NVARCHAR(255), @ReconcileWarning INT, @CategoryIdForPrincipal INT, @CategoryIdForInterest INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Accounts SET
        AccountId = @AccountId, OfxAccountId = @OfxAccountId, Name = @Name, Type = @Type,
        Description = @Description, OnlineAccount = @OnlineAccount, OpeningBalance = @OpeningBalance,
        LastSync = @LastSync, LastBalance = @LastBalance, SyncGuid = @SyncGuid, Flags = @Flags,
        Currency = @Currency, WebSite = @WebSite, ReconcileWarning = @ReconcileWarning,
        CategoryIdForPrincipal = @CategoryIdForPrincipal, CategoryIdForInterest = @CategoryIdForInterest
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Accounts WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Accounts_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Accounts_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Accounts_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Accounts_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Accounts_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add the `ReadAccounts`/`UpdateAccounts` overrides**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, add after `UpdatePayees`:

```csharp
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
                        ("@OpeningBalance", a.OpeningBalance), ("@LastSync", SqlDatabase.DBDateTimeParam(a.LastSync)),
                        ("@LastBalance", SqlDatabase.DBDateTimeParam(a.LastBalance)), ("@SyncGuid", SqlDatabase.DBGuidParam(a.SyncGuid)),
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
```

Note the `@SyncGuid`/`@LastSync`/`@LastBalance` values use `SqlDatabase.DBGuidParam(...)`/`SqlDatabase.DBDateTimeParam(...)` — fully-qualified since these are `internal static` members of the base class `SqlDatabase`, accessible from this subclass because both are in the `MyMoney.Data` assembly (Task 1 promoted them from `private` to `internal`).

- [ ] **Step 5: Extend `Load()` to also read Accounts**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, find:

```csharp
        public override MyMoney Load(IStatusService status)
        {
            MyMoney money = new MyMoney();
            money.BeginUpdate(this);
            try
            {
                this.ReadPayees(money.Payees, money);
            }
            finally
            {
                money.FlushUpdates();
                money.EndUpdate();
                money.OnLoaded();
            }
            return money;
        }
```

Change to:

```csharp
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
```

(Later tasks add more `this.ReadXxx(...)` lines here, in dependency order: Categories before Accounts would also work since `ReadAccounts` only resolves `CategoryForPrincipal`/`CategoryForInterest` when non-null and `FindCategoryById` returns `null` harmlessly if Categories haven't loaded yet — but to avoid relying on that, Task 3 inserts `ReadCategories` *before* `ReadAccounts` in this method.)

- [ ] **Step 6: Deploy the new proc script and rerun the test**

Run `MyMoneyAdmin.exe` against the existing bootstrapped SQL Server instance to deploy `Accounts_AccessProcs.sql` (this also runs Task 1's `LazyCreateTables()` step, creating the `Accounts` table if it doesn't already exist):

```bash
dotnet run --project Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj -- <server> MyMoney
```

(Use whatever server argument this environment's existing bootstrap already uses — the same one `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` points at.)

Then run: `dotnet build Source/WPF/MyMoney.sln`
Expected: `Build succeeded.`, 0 errors.

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteAccount_RoundTripsThroughStoredProcedures"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Accounts_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Accounts (issue #22)"
```

---

### Task 3: Categories

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Categories_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ReadCategories`, `UpdateCategories`; extend `Load()`)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteCategory_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1).
- Produces: `ReadCategories(Categories, MyMoney)`, `UpdateCategories(Categories)` (`public override`) — Task 2's `ReadAccounts` (`CategoryForPrincipal`/`CategoryForInterest`) and Task 8 (Transactions) both call `money.Categories.FindCategoryById(id)` against data this loads, so `Load()` must call `ReadCategories` before `ReadAccounts`.

- [ ] **Step 1: Write the failing test**

In `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs`, add:

```csharp
        [Test]
        public void InsertUpdateDeleteCategory_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var category = money.Categories.GetOrCreateCategory("SqlServerStoredProcDatabaseTests:Category", CategoryType.Expense);
            category.Description = "Test category";
            category.Budget = 250.00m;
            category.OnInserted();

            db.UpdateCategories(money.Categories);

            var reloaded = new MyMoney();
            db.ReadCategories(reloaded.Categories, reloaded);
            var found = reloaded.Categories.FindCategory("SqlServerStoredProcDatabaseTests:Category");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Description, Is.EqualTo("Test category"));
            Assert.That(found.Budget, Is.EqualTo(250.00m));

            found.Description = "Updated description";
            db.UpdateCategories(reloaded.Categories);

            var afterUpdate = new MyMoney();
            db.ReadCategories(afterUpdate.Categories, afterUpdate);
            var updated = afterUpdate.Categories.FindCategory("SqlServerStoredProcDatabaseTests:Category");
            Assert.That(updated.Description, Is.EqualTo("Updated description"));

            updated.OnDelete();
            var toDelete = new Categories(afterUpdate);
            toDelete.Add(updated);
            db.UpdateCategories(toDelete);

            var afterDelete = new MyMoney();
            db.ReadCategories(afterDelete.Categories, afterDelete);
            Assert.That(afterDelete.Categories.FindCategory("SqlServerStoredProcDatabaseTests:Category"), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteCategory_RoundTripsThroughStoredProcedures"`
Expected: FAIL (no override yet, same failure mode as Task 2 Step 2).

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Categories_AccessProcs.sql`:

```sql
-- Categories_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Categories table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Categories_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum
    FROM dbo.Categories ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Insert
    @Id INT, @Name NVARCHAR(255), @Description NVARCHAR(255), @Type INT, @ParentId INT,
    @Budget MONEY, @Frequency INT, @Balance MONEY, @Color NVARCHAR(20), @TaxRefNum INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Categories (Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum)
    VALUES (@Id, @Name, @Description, @Type, @ParentId, @Budget, @Frequency, @Balance, @Color, @TaxRefNum);
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Update
    @Id INT, @Name NVARCHAR(255), @Description NVARCHAR(255), @Type INT, @ParentId INT,
    @Budget MONEY, @Frequency INT, @Balance MONEY, @Color NVARCHAR(20), @TaxRefNum INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Categories SET
        Name = @Name, Description = @Description, Type = @Type, ParentId = @ParentId, Budget = @Budget,
        Frequency = @Frequency, Balance = @Balance, Color = @Color, TaxRefNum = @TaxRefNum
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Categories WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Categories_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Categories_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add the `ReadCategories`/`UpdateCategories` overrides**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, add:

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
```

- [ ] **Step 5: Extend `Load()` to read Categories before Accounts**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, find:

```csharp
                this.ReadPayees(money.Payees, money);
                this.ReadAccounts(money.Accounts, money);
```

Change to:

```csharp
                this.ReadPayees(money.Payees, money);
                this.ReadCategories(money.Categories, money);
                this.ReadAccounts(money.Accounts, money);
```

- [ ] **Step 6: Deploy and rerun**

Run `MyMoneyAdmin.exe` as in Task 2 Step 6 to deploy `Categories_AccessProcs.sql`.

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteCategory_RoundTripsThroughStoredProcedures"` — expect `Passed! - Failed: 0, Passed: 1`.
Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerStoredProcDatabaseTests"` — expect all tests in the fixture (Payees + Accounts + Categories) still pass, confirming `Load()`'s new ordering didn't break anything.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Categories_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Categories (issue #22)"
```

---

### Task 4: Currencies

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Currencies_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ReadCurrencies`, `UpdateCurrencies`; extend `Load()`)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteCurrency_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1).
- Produces: `ReadCurrencies(Currencies, MyMoney)`, `UpdateCurrencies(Currencies)` (`public override`) — nothing later in this plan depends on Currencies directly, but issue #19's rich dataset will.

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void InsertUpdateDeleteCurrency_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var currency = money.Currencies.AddCurrency(0);
            currency.Symbol = "ZZT";
            currency.Name = "SqlServerStoredProcDatabaseTests Currency";
            currency.Ratio = 1.5m;
            currency.CultureCode = "en-US";
            currency.OnInserted();

            db.UpdateCurrencies(money.Currencies);

            var reloaded = new MyMoney();
            db.ReadCurrencies(reloaded.Currencies, reloaded);
            var found = reloaded.Currencies.FindCurrency("ZZT");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Name, Is.EqualTo("SqlServerStoredProcDatabaseTests Currency"));
            Assert.That(found.Ratio, Is.EqualTo(1.5m));

            found.Ratio = 2.0m;
            db.UpdateCurrencies(reloaded.Currencies);

            var afterUpdate = new MyMoney();
            db.ReadCurrencies(afterUpdate.Currencies, afterUpdate);
            var updated = afterUpdate.Currencies.FindCurrency("ZZT");
            Assert.That(updated.Ratio, Is.EqualTo(2.0m));

            updated.OnDelete();
            var toDelete = new Currencies(afterUpdate);
            toDelete.Add(updated);
            db.UpdateCurrencies(toDelete);

            var afterDelete = new MyMoney();
            db.ReadCurrencies(afterDelete.Currencies, afterDelete);
            Assert.That(afterDelete.Currencies.FindCurrency("ZZT"), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteCurrency_RoundTripsThroughStoredProcedures"`
Expected: FAIL.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Currencies_AccessProcs.sql`:

```sql
-- Currencies_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Currencies table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Symbol, Name, Ratio, LastRatio, CultureCode FROM dbo.Currencies ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Insert
    @Id INT, @Symbol NVARCHAR(10), @Name NVARCHAR(80), @Ratio DECIMAL(18,6), @LastRatio DECIMAL(18,6), @CultureCode NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Currencies (Id, Symbol, Name, Ratio, LastRatio, CultureCode)
    VALUES (@Id, @Symbol, @Name, @Ratio, @LastRatio, @CultureCode);
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Update
    @Id INT, @Symbol NVARCHAR(10), @Name NVARCHAR(80), @Ratio DECIMAL(18,6), @LastRatio DECIMAL(18,6), @CultureCode NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Currencies SET Symbol = @Symbol, Name = @Name, Ratio = @Ratio, LastRatio = @LastRatio, CultureCode = @CultureCode
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Currencies WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Currencies_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Currencies_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Currencies_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Currencies_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Currencies_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add the `ReadCurrencies`/`UpdateCurrencies` overrides**

```csharp
        public override void ReadCurrencies(Currencies currencies, MyMoney money)
        {
            currencies.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Currencies_SelectAll", connection) { CommandType = CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    currencies.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Currency s = currencies.AddCurrency(id);
                        s.Symbol = reader.IsDBNull(1) ? null : reader.GetString(1);
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
```

- [ ] **Step 5: Extend `Load()`**

Add `this.ReadCurrencies(money.Currencies, money);` after the `ReadCategories` line (order relative to Accounts/Categories doesn't matter — Currencies has no FK dependency on either).

- [ ] **Step 6: Deploy and rerun**

Deploy `Currencies_AccessProcs.sql` via `MyMoneyAdmin.exe`. Build, then run:
`dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerStoredProcDatabaseTests"` — expect all tests in the fixture so far still pass.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Currencies_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Currencies (issue #22)"
```

---

### Task 5: Securities

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Securities_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ReadSecurities`, `UpdateSecurities`; extend `Load()`)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteSecurity_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1).
- Produces: `ReadSecurities(Securities, MyMoney)`, `UpdateSecurities(Securities)` (`public override`) — Task 6 (StockSplits) and Task 10 (Investment) both need a Security to exist first, via `money.Securities.FindSecurityAt(id)`, so `Load()` must call `ReadSecurities` before anything that resolves a `Security` reference.

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void InsertUpdateDeleteSecurity_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var security = money.Securities.AddSecurity(0);
            security.Name = "SqlServerStoredProcDatabaseTests Security";
            security.Symbol = "ZZS";
            security.Price = 42.50m;
            security.SecurityType = SecurityType.Equity;
            security.OnInserted();

            db.UpdateSecurities(money.Securities);

            var reloaded = new MyMoney();
            db.ReadSecurities(reloaded.Securities, reloaded);
            var found = reloaded.Securities.FindSecurity("SqlServerStoredProcDatabaseTests Security", false);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Name, Is.EqualTo("SqlServerStoredProcDatabaseTests Security"));
            Assert.That(found.Price, Is.EqualTo(42.50m));

            found.Price = 50.00m;
            db.UpdateSecurities(reloaded.Securities);

            var afterUpdate = new MyMoney();
            db.ReadSecurities(afterUpdate.Securities, afterUpdate);
            var updated = afterUpdate.Securities.FindSecurity("SqlServerStoredProcDatabaseTests Security", false);
            Assert.That(updated.Price, Is.EqualTo(50.00m));

            updated.OnDelete();
            var toDelete = new Securities(afterUpdate);
            toDelete.Add(updated);
            db.UpdateSecurities(toDelete);

            var afterDelete = new MyMoney();
            db.ReadSecurities(afterDelete.Securities, afterDelete);
            Assert.That(afterDelete.Securities.FindSecurity("SqlServerStoredProcDatabaseTests Security", false), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteSecurity_RoundTripsThroughStoredProcedures"`
Expected: FAIL.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Securities_AccessProcs.sql`:

```sql
-- Securities_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Securities table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Securities_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate FROM dbo.Securities ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_Insert
    @Id INT, @Name NVARCHAR(80), @Symbol NVARCHAR(20), @Price MONEY, @LastPrice MONEY,
    @CuspId NVARCHAR(20), @SecurityType INT, @Taxable TINYINT, @PriceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Securities (Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate)
    VALUES (@Id, @Name, @Symbol, @Price, @LastPrice, @CuspId, @SecurityType, @Taxable, @PriceDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_Update
    @Id INT, @Name NVARCHAR(80), @Symbol NVARCHAR(20), @Price MONEY, @LastPrice MONEY,
    @CuspId NVARCHAR(20), @SecurityType INT, @Taxable TINYINT, @PriceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Securities SET
        Name = @Name, Symbol = @Symbol, Price = @Price, LastPrice = @LastPrice, CuspId = @CuspId,
        SecurityType = @SecurityType, Taxable = @Taxable, PriceDate = @PriceDate
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Securities WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Securities_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Securities_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Securities_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Securities_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Securities_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add the `ReadSecurities`/`UpdateSecurities` overrides**

```csharp
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
                        s.Symbol = reader.IsDBNull(2) ? null : reader.GetString(2);
                        s.Price = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);
                        if (!reader.IsDBNull(4))
                        {
                            s.LastPrice = reader.GetDecimal(4);
                        }
                        s.CuspId = reader.IsDBNull(5) ? null : reader.GetString(5);
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
                        ("@PriceDate", SqlDatabase.DBDateTimeParam(s.PriceDate))
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
```

- [ ] **Step 5: Extend `Load()`**

Add `this.ReadSecurities(money.Securities, money);` after `ReadCurrencies` and before `ReadAccounts` (Securities has no dependency on Accounts, but Task 6/10 need Securities loaded before Transactions/StockSplits resolve `Security` references, so keep it early).

- [ ] **Step 6: Deploy and rerun**

Deploy `Securities_AccessProcs.sql` via `MyMoneyAdmin.exe`. Build, then run the full `SqlServerStoredProcDatabaseTests` fixture — expect all tests so far still pass.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Securities_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Securities (issue #22)"
```

---

### Task 6: StockSplits

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/StockSplits_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ReadStockSplits`, `UpdateStockSplits`; extend `Load()`)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteStockSplit_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1), `ReadSecurities`/`UpdateSecurities` (Task 5, to set up the Security a StockSplit references).
- Produces: `ReadStockSplits(StockSplits, MyMoney)`, `UpdateStockSplits(StockSplits)` (`public override`).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void InsertUpdateDeleteStockSplit_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var security = money.Securities.AddSecurity(0);
            security.Name = "SqlServerStoredProcDatabaseTests StockSplit Security";
            security.Symbol = "ZZP";
            security.OnInserted();
            db.UpdateSecurities(money.Securities);

            var split = money.StockSplits.AddStockSplit(0);
            split.Date = new DateTime(2026, 1, 1);
            split.Security = security;
            split.Numerator = 2;
            split.Denominator = 1;
            split.OnInserted();
            db.UpdateStockSplits(money.StockSplits);

            var reloaded = new MyMoney();
            db.ReadSecurities(reloaded.Securities, reloaded);
            db.ReadStockSplits(reloaded.StockSplits, reloaded);
            var reloadedSecurity = reloaded.Securities.FindSecurity("SqlServerStoredProcDatabaseTests StockSplit Security", false);
            var found = reloaded.StockSplits.FindStockSplitByDate(reloadedSecurity, split.Date);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Numerator, Is.EqualTo(2));
            Assert.That(found.Denominator, Is.EqualTo(1));

            found.Numerator = 3;
            db.UpdateStockSplits(reloaded.StockSplits);

            var afterUpdate = new MyMoney();
            db.ReadSecurities(afterUpdate.Securities, afterUpdate);
            db.ReadStockSplits(afterUpdate.StockSplits, afterUpdate);
            var afterUpdateSecurity = afterUpdate.Securities.FindSecurity("SqlServerStoredProcDatabaseTests StockSplit Security", false);
            var updated = afterUpdate.StockSplits.FindStockSplitByDate(afterUpdateSecurity, split.Date);
            Assert.That(updated.Numerator, Is.EqualTo(3));

            updated.OnDelete();
            var toDelete = new StockSplits(afterUpdate);
            toDelete.Add(updated);
            db.UpdateStockSplits(toDelete);

            afterUpdateSecurity.OnDelete();
            var securitiesToDelete = new Securities(afterUpdate);
            securitiesToDelete.Add(afterUpdateSecurity);
            db.UpdateSecurities(securitiesToDelete);

            var afterDelete = new MyMoney();
            db.ReadSecurities(afterDelete.Securities, afterDelete);
            db.ReadStockSplits(afterDelete.StockSplits, afterDelete);
            Assert.That(afterDelete.Securities.FindSecurity("SqlServerStoredProcDatabaseTests StockSplit Security", false), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteStockSplit_RoundTripsThroughStoredProcedures"`
Expected: FAIL.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/StockSplits_AccessProcs.sql`:

```sql
-- StockSplits_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the StockSplits table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Date, Security, Numerator, Denominator FROM dbo.StockSplits ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_Insert
    @Id BIGINT, @Date DATETIME, @Security INT, @Numerator DECIMAL(18,6), @Denominator DECIMAL(18,6)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.StockSplits (Id, Date, Security, Numerator, Denominator)
    VALUES (@Id, @Date, @Security, @Numerator, @Denominator);
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_Update
    @Id BIGINT, @Date DATETIME, @Security INT, @Numerator DECIMAL(18,6), @Denominator DECIMAL(18,6)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.StockSplits SET Date = @Date, Security = @Security, Numerator = @Numerator, Denominator = @Denominator
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_Delete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.StockSplits WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.StockSplits_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.StockSplits_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.StockSplits_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.StockSplits_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.StockSplits_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add the `ReadStockSplits`/`UpdateStockSplits` overrides**

Note: the base `SqlDatabase.ReadStockSplits` is `private`, so this is a *new* `public` method on `SqlServerStoredProcDatabase`, not an `override` (there's nothing virtual to override — `IDatabase`'s `Load()` is what calls it, not a public base method). Same for `UpdateStockSplits`, which *is* `public` on the base class, so this one genuinely is `override`.

```csharp
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
                        ("@Id", s.Id), ("@Date", SqlDatabase.DBDateTimeParam(s.Date)), ("@Security", s.Security.Id),
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
```

- [ ] **Step 5: Extend `Load()`**

Add `this.ReadStockSplits(money.StockSplits, money);` after `ReadSecurities` (StockSplits needs Securities loaded first to resolve `s.Security`).

- [ ] **Step 6: Deploy and rerun**

Deploy `StockSplits_AccessProcs.sql` via `MyMoneyAdmin.exe`. Build, then run the full `SqlServerStoredProcDatabaseTests` fixture — expect all tests so far still pass.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/StockSplits_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for StockSplits (issue #22)"
```

---

### Task 7: Aliases

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Aliases_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ReadAliases`, `UpdateAliases`; extend `Load()`)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteAlias_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1), `ReadPayees`/`UpdatePayees` (pre-existing, to set up the Payee an Alias references).
- Produces: `ReadAliases(Aliases, MyMoney)`, `UpdateAliases(Aliases)` (`public override`).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void InsertUpdateDeleteAlias_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var payee = money.Payees.AddPayee(999002);
            payee.Name = "SqlServerStoredProcDatabaseTests Alias Payee";
            payee.OnInserted();
            db.UpdatePayees(money.Payees);

            var alias = money.Aliases.AddAlias(0);
            alias.Pattern = "SqlServerStoredProcDatabaseTests Alias Pattern";
            alias.Payee = payee;
            alias.AliasType = AliasType.None;
            alias.OnInserted();
            db.UpdateAliases(money.Aliases);

            var reloaded = new MyMoney();
            db.ReadPayees(reloaded.Payees, reloaded);
            db.ReadAliases(reloaded.Aliases, reloaded);
            var found = reloaded.Aliases.FindAlias("SqlServerStoredProcDatabaseTests Alias Pattern");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Payee.Name, Is.EqualTo("SqlServerStoredProcDatabaseTests Alias Payee"));

            found.AliasType = AliasType.Regex;
            db.UpdateAliases(reloaded.Aliases);

            var afterUpdate = new MyMoney();
            db.ReadPayees(afterUpdate.Payees, afterUpdate);
            db.ReadAliases(afterUpdate.Aliases, afterUpdate);
            var updated = afterUpdate.Aliases.FindAlias("SqlServerStoredProcDatabaseTests Alias Pattern");
            Assert.That(updated.AliasType, Is.EqualTo(AliasType.Regex));

            updated.OnDelete();
            var toDelete = new Aliases(afterUpdate);
            toDelete.Add(updated);
            db.UpdateAliases(toDelete);

            var payeeToDelete = afterUpdate.Payees.FindPayee("SqlServerStoredProcDatabaseTests Alias Payee", false);
            payeeToDelete.OnDelete();
            var payeesToDelete = new Payees(afterUpdate);
            payeesToDelete.Add(payeeToDelete);
            db.UpdatePayees(payeesToDelete);

            var afterDelete = new MyMoney();
            db.ReadAliases(afterDelete.Aliases, afterDelete);
            Assert.That(afterDelete.Aliases.FindAlias("SqlServerStoredProcDatabaseTests Alias Pattern"), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteAlias_RoundTripsThroughStoredProcedures"`
Expected: FAIL.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Aliases_AccessProcs.sql`:

```sql
-- Aliases_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Aliases table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Pattern, Payee, Flags FROM dbo.Aliases ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_Insert
    @Id INT, @Pattern NVARCHAR(255), @Payee INT, @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Aliases (Id, Pattern, Payee, Flags) VALUES (@Id, @Pattern, @Payee, @Flags);
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_Update
    @Id INT, @Pattern NVARCHAR(255), @Payee INT, @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Aliases SET Pattern = @Pattern, Payee = @Payee, Flags = @Flags WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Aliases WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Aliases_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Aliases_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Aliases_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Aliases_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Aliases_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add the `ReadAliases`/`UpdateAliases` overrides**

```csharp
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
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", a.Id), ("@Pattern", (object)a.Pattern ?? DBNull.Value), ("@Payee", a.Payee.Id),
                        ("@Flags", (int)a.AliasType)
                    };

                    if (a.IsChanged)
                    {
                        ExecuteProc(connection, "dbo.Aliases_Update", parameters);
                    }
                    else if (a.IsInserted)
                    {
                        ExecuteProc(connection, "dbo.Aliases_Insert", parameters);
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
```

- [ ] **Step 5: Extend `Load()`**

Add `this.ReadAliases(money.Aliases, money);` after `ReadPayees` (Aliases needs Payees loaded first to resolve `a.Payee`).

- [ ] **Step 6: Deploy and rerun**

Deploy `Aliases_AccessProcs.sql` via `MyMoneyAdmin.exe`. Build, then run the full `SqlServerStoredProcDatabaseTests` fixture — expect all tests so far still pass.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Aliases_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Aliases (issue #22)"
```

---

### Task 8: Transactions

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Transactions_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add `ReadTransactions`, `UpdateTransactions`; extend `Load()`)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteTransaction_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1), `ReadAccounts`/`UpdateAccounts` (Task 2, an Account is required), `ReadPayees`/`ReadCategories` (to optionally resolve Payee/Category references).
- Produces: `ReadTransactions(Transactions, MyMoney)` (`public override` — note the base signature returns `ArrayList` for error reporting; this override keeps the same signature so `Load()`'s call site is identical to the base class's usage pattern), `UpdateTransactions(Transactions)` (`public override`). This override does **not** yet read/write Splits or Investment rows — those are Tasks 9 and 10, which extend this same method (the base class's `ReadTransactions` already combines all three reads into one method, and `UpdateTransactions` already calls `UpdateSplits`/`UpdateInvestment` per-transaction; Tasks 9-10 add exactly those calls here).

Transfer resolution (a transaction whose `Transfer`/`TransferSplit` columns point at another transaction) is faithfully reproduced from the base class's `ReadTransactions`, including the "other side of transfer not found" / "duplicate transfer" error accumulation — this task's test only exercises the non-transfer path, but the code below implements transfer resolution because Task 9's tests and issue #19's later parity test will exercise it.

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void InsertUpdateDeleteTransaction_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests Transaction Account");
            account.Type = AccountType.Checking;
            account.OnInserted();
            db.UpdateAccounts(money.Accounts);

            var transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2026, 1, 15);
            transaction.Amount = -42.50m;
            transaction.Memo = "SqlServerStoredProcDatabaseTests Transaction";
            money.Transactions.AddTransaction(transaction);
            db.UpdateTransactions(money.Transactions);

            var reloaded = new MyMoney();
            db.ReadAccounts(reloaded.Accounts, reloaded);
            db.ReadTransactions(reloaded.Transactions, reloaded);
            var reloadedAccount = reloaded.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Transaction Account");
            var found = reloaded.Transactions.GetTransactionsFrom(reloadedAccount)
                .FirstOrDefault(t => t.Memo == "SqlServerStoredProcDatabaseTests Transaction");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Amount, Is.EqualTo(-42.50m));

            found.Amount = -50.00m;
            db.UpdateTransactions(reloaded.Transactions);

            var afterUpdate = new MyMoney();
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            db.ReadTransactions(afterUpdate.Transactions, afterUpdate);
            var afterUpdateAccount = afterUpdate.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Transaction Account");
            var updated = afterUpdate.Transactions.GetTransactionsFrom(afterUpdateAccount)
                .FirstOrDefault(t => t.Memo == "SqlServerStoredProcDatabaseTests Transaction");
            Assert.That(updated.Amount, Is.EqualTo(-50.00m));

            updated.OnDelete();
            var toDelete = new Transactions(afterUpdate);
            toDelete.AddTransaction(updated);
            db.UpdateTransactions(toDelete);

            afterUpdateAccount.OnDelete();
            var accountsToDelete = new Accounts(afterUpdate);
            accountsToDelete.Add(afterUpdateAccount);
            db.UpdateAccounts(accountsToDelete);

            var afterDelete = new MyMoney();
            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            Assert.That(afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Transaction Account"), Is.Null);
        }
```

Add `using System.Linq;` to the top of `SqlServerStoredProcDatabaseTests.cs` for `.FirstOrDefault`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteTransaction_RoundTripsThroughStoredProcedures"`
Expected: FAIL.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Transactions_AccessProcs.sql`:

```sql
-- Transactions_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Transactions table -- neither has any direct table grants.
-- Splits and Investments are separate tables with their own access-proc
-- scripts (Splits_AccessProcs.sql, Investments_AccessProcs.sql) -- see the
-- design spec for issue #22 for why these stay three independent proc sets.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Number, Date, Amount, Account, Status, Memo, Payee, Category, FITID, SalesTax, Flags,
           ReconciledDate, BudgetBalanceDate, MergeDate, OriginalPayee, Transfer, TransferSplit
    FROM dbo.Transactions ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_Insert
    @Id BIGINT, @Number NVARCHAR(20), @Account INT, @Date DATETIME, @Amount MONEY, @Status INT,
    @Memo NVARCHAR(255), @Payee INT, @Category INT, @Transfer BIGINT, @TransferSplit INT,
    @FITID NVARCHAR(50), @SalesTax MONEY, @Flags INT, @ReconciledDate DATETIME,
    @BudgetBalanceDate DATETIME, @MergeDate DATETIME, @OriginalPayee NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Transactions (Id, Number, Account, Date, Amount, Status, Memo, Payee, Category,
        Transfer, TransferSplit, FITID, SalesTax, Flags, ReconciledDate, BudgetBalanceDate, MergeDate, OriginalPayee)
    VALUES (@Id, @Number, @Account, @Date, @Amount, @Status, @Memo, @Payee, @Category, @Transfer,
        @TransferSplit, @FITID, @SalesTax, @Flags, @ReconciledDate, @BudgetBalanceDate, @MergeDate, @OriginalPayee);
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_Update
    @Id BIGINT, @Number NVARCHAR(20), @Account INT, @Date DATETIME, @Amount MONEY, @Status INT,
    @Memo NVARCHAR(255), @Payee INT, @Category INT, @Transfer BIGINT, @TransferSplit INT,
    @FITID NVARCHAR(50), @SalesTax MONEY, @Flags INT, @ReconciledDate DATETIME,
    @BudgetBalanceDate DATETIME, @MergeDate DATETIME, @OriginalPayee NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Transactions SET
        Number = @Number, Account = @Account, Date = @Date, Amount = @Amount, Status = @Status,
        Memo = @Memo, Payee = @Payee, Category = @Category, Transfer = @Transfer, TransferSplit = @TransferSplit,
        FITID = @FITID, SalesTax = @SalesTax, Flags = @Flags, ReconciledDate = @ReconciledDate,
        BudgetBalanceDate = @BudgetBalanceDate, MergeDate = @MergeDate, OriginalPayee = @OriginalPayee
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_Delete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Transactions WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Transactions_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Transactions_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Transactions_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Transactions_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Transactions_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add the `ReadTransactions`/`UpdateTransactions` overrides**

```csharp
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
                        t.Number = reader.IsDBNull(1) ? null : reader.GetString(1);
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
                        t.FITID = reader.IsDBNull(9) ? null : reader.GetString(9);
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

                // Resolve transfers (mirrors SqlDatabase.ReadTransactions's third pass).
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
                        Transaction u = transactions.FindTransactionById(transferTarget);
                        if (u == null)
                        {
                            errors.Add(new DataError(id, "Transaction is marked as a transfer, but other side of transfer was not found"));
                            continue;
                        }

                        int sid = reader.IsDBNull(17) ? -1 : reader.GetInt32(17);
                        if (sid == -1)
                        {
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
                                t.Transfer = new Transfer(id, t, u, s);
                            }
                        }
                        t.OnUpdated();
                    }
                }
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

                    connection.Open();
                    (string Name, object Value)[] parameters =
                    {
                        ("@Id", t.Id), ("@Number", (object)t.Number ?? DBNull.Value), ("@Account", t.Account.Id),
                        ("@Date", SqlDatabase.DBDateTimeParam(t.Date)), ("@Amount", t.Amount), ("@Status", (int)t.Status),
                        ("@Memo", (object)t.Memo ?? DBNull.Value), ("@Payee", t.Payee != null ? t.Payee.Id : -1),
                        ("@Category", t.Category != null ? t.Category.Id : -1),
                        ("@Transfer", t.Transfer != null && t.Transfer.Transaction != null ? t.Transfer.Transaction.Id : -1),
                        ("@TransferSplit", t.Transfer != null && t.Transfer.Split != null ? t.Transfer.Split.Id : -1),
                        ("@FITID", (object)t.FITID ?? DBNull.Value), ("@SalesTax", t.SalesTax), ("@Flags", (int)t.Flags),
                        ("@ReconciledDate", SqlDatabase.DBNullableDateTimeParam(t.ReconciledDate)),
                        ("@BudgetBalanceDate", SqlDatabase.DBNullableDateTimeParam(t.BudgetBalanceDate)),
                        ("@MergeDate", SqlDatabase.DBNullableDateTimeParam(t.MergeDate)),
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

            foreach (Transaction t in transactions)
            {
                t.OnUpdated();
            }
            transactions.RemoveDeleted();
        }
```

Note: `connection.Open()`/`connection.Close()` are called per-transaction inside the loop here (rather than once outside, as the simpler entities do) because this method also calls `this.UpdateSplits(...)`/`this.UpdateInvestment(...)` (Tasks 9-10), which open their own connections — keeping one shared connection open across those nested calls would work too, but per-transaction open/close keeps this method's connection lifetime obviously scoped and matches the "no atomic parent+children" requirement (each table's update is independently connected).

Add `using System.Collections;` to the top of `SqlServerStoredProcDatabase.cs` for `ArrayList`.

- [ ] **Step 5: Extend `Load()`**

Find:

```csharp
                this.ReadPayees(money.Payees, money);
                this.ReadCategories(money.Categories, money);
                this.ReadCurrencies(money.Currencies, money);
                this.ReadSecurities(money.Securities, money);
                this.ReadStockSplits(money.StockSplits, money);
                this.ReadAliases(money.Aliases, money);
                this.ReadAccounts(money.Accounts, money);
```

(exact ordering per Tasks 2-7 above) and add, after `ReadAccounts`:

```csharp
                this.ReadTransactions(money.Transactions, money);
```

- [ ] **Step 6: Deploy and rerun**

Deploy `Transactions_AccessProcs.sql` via `MyMoneyAdmin.exe`. Build, then run the full `SqlServerStoredProcDatabaseTests` fixture — expect all tests so far still pass.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Transactions_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Transactions (issue #22)"
```

---

### Task 9: Splits

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Splits_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add split-reading to `ReadTransactions`; add `UpdateSplits` override)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteSplit_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1), `ReadTransactions`/`UpdateTransactions` (Task 8, which this task extends).
- Produces: `UpdateSplits(Splits)` (`public override`) — called from `UpdateTransactions` (Task 8's code already has the `this.UpdateSplits(t.Splits)` call site).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void InsertUpdateDeleteSplit_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests Split Account");
            account.Type = AccountType.Checking;
            account.OnInserted();
            db.UpdateAccounts(money.Accounts);

            var category = money.Categories.GetOrCreateCategory("SqlServerStoredProcDatabaseTests:SplitCategory", CategoryType.Expense);
            category.OnInserted();
            db.UpdateCategories(money.Categories);

            var transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2026, 1, 15);
            transaction.Amount = -100.00m;
            money.Transactions.AddTransaction(transaction);
            transaction.Splits = new Splits(money.Transactions, transaction);
            var split = transaction.Splits.AddSplit(0);
            split.Amount = -100.00m;
            split.Category = category;
            split.Memo = "SqlServerStoredProcDatabaseTests Split";
            transaction.OnInserted();
            db.UpdateTransactions(money.Transactions);

            var reloaded = new MyMoney();
            db.ReadCategories(reloaded.Categories, reloaded);
            db.ReadAccounts(reloaded.Accounts, reloaded);
            db.ReadTransactions(reloaded.Transactions, reloaded);
            var reloadedAccount = reloaded.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Split Account");
            var foundTransaction = reloaded.Transactions.GetTransactionsFrom(reloadedAccount).First();
            Assert.That(foundTransaction.IsSplit, Is.True);
            Assert.That(foundTransaction.Splits.Count, Is.EqualTo(1));
            Assert.That(foundTransaction.Splits[0].Memo, Is.EqualTo("SqlServerStoredProcDatabaseTests Split"));

            foundTransaction.Splits[0].Memo = "Updated split memo";
            db.UpdateTransactions(reloaded.Transactions);

            var afterUpdate = new MyMoney();
            db.ReadCategories(afterUpdate.Categories, afterUpdate);
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            db.ReadTransactions(afterUpdate.Transactions, afterUpdate);
            var afterUpdateAccount = afterUpdate.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Split Account");
            var updatedTransaction = afterUpdate.Transactions.GetTransactionsFrom(afterUpdateAccount).First();
            Assert.That(updatedTransaction.Splits[0].Memo, Is.EqualTo("Updated split memo"));

            updatedTransaction.OnDelete();
            var toDelete = new Transactions(afterUpdate);
            toDelete.AddTransaction(updatedTransaction);
            db.UpdateTransactions(toDelete);

            afterUpdateAccount.OnDelete();
            var accountsToDelete = new Accounts(afterUpdate);
            accountsToDelete.Add(afterUpdateAccount);
            db.UpdateAccounts(accountsToDelete);

            var categoryToDelete = afterUpdate.Categories.FindCategory("SqlServerStoredProcDatabaseTests:SplitCategory");
            categoryToDelete.OnDelete();
            var categoriesToDelete = new Categories(afterUpdate);
            categoriesToDelete.Add(categoryToDelete);
            db.UpdateCategories(categoriesToDelete);

            var afterDelete = new MyMoney();
            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            Assert.That(afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Split Account"), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteSplit_RoundTripsThroughStoredProcedures"`
Expected: FAIL — `UpdateSplits`/the split-reading code don't exist on `SqlServerStoredProcDatabase` yet, so the base class's versions run and hit the same `MyMoneyUser`-has-no-table-grants failure.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Splits_AccessProcs.sql`:

```sql
-- Splits_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Splits table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Splits_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, [Transaction], Amount, Category, Memo, Transfer, Payee, Flags, BudgetBalanceDate
    FROM dbo.Splits ORDER BY [Transaction], Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Splits_Insert
    @Id INT, @Transaction BIGINT, @Amount MONEY, @Category INT, @Memo NVARCHAR(255),
    @Transfer BIGINT, @Payee INT, @Flags INT, @BudgetBalanceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Splits (Id, [Transaction], Amount, Category, Memo, Transfer, Payee, Flags, BudgetBalanceDate)
    VALUES (@Id, @Transaction, @Amount, @Category, @Memo, @Transfer, @Payee, @Flags, @BudgetBalanceDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.Splits_Update
    @Id INT, @Transaction BIGINT, @Amount MONEY, @Category INT, @Memo NVARCHAR(255),
    @Transfer BIGINT, @Payee INT, @Flags INT, @BudgetBalanceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Splits SET
        Amount = @Amount, Category = @Category, Memo = @Memo, Transfer = @Transfer, Payee = @Payee,
        Flags = @Flags, BudgetBalanceDate = @BudgetBalanceDate
    WHERE Id = @Id AND [Transaction] = @Transaction;
END
GO

CREATE OR ALTER PROCEDURE dbo.Splits_Delete
    @Id INT, @Transaction BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Splits WHERE Id = @Id AND [Transaction] = @Transaction;
END
GO

GRANT EXECUTE ON dbo.Splits_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Splits_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Splits_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Splits_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Splits_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Splits_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Splits_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Splits_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add split-reading to `ReadTransactions` and add the `UpdateSplits` override**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, find the `ReadTransactions` override added in Task 8, and change:

```csharp
                    transactions.EndUpdate();
                }

                // Resolve transfers (mirrors SqlDatabase.ReadTransactions's third pass).
```

to:

```csharp
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

                // Resolve transfers (mirrors SqlDatabase.ReadTransactions's third pass).
```

(Split-side transfer resolution — column 5, `Transfer` — is intentionally left unhandled here, matching the base class's own `ReadTransactions`, which also only resolves transfers recorded on `Transactions.Transfer`/`TransferSplit`, not `Splits.Transfer`, when populating the in-memory `Transfer` object graph on load; both leave the raw column round-trippable via `UpdateSplits` below without reconstructing a `Transfer` object for split-level transfers on read.)

Then add the `UpdateSplits` override (after `UpdateTransactions`):

```csharp
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
                        ("@Category", s.Category != null ? s.Category.Id : -1), ("@Memo", (object)s.Memo ?? DBNull.Value),
                        ("@Transfer", s.Transfer != null && s.Transfer.Transaction != null ? s.Transfer.Transaction.Id : -1),
                        ("@Payee", s.Payee != null ? s.Payee.Id : -1), ("@Flags", (int)s.Flags),
                        ("@BudgetBalanceDate", SqlDatabase.DBNullableDateTimeParam(s.BudgetBalanceDate))
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
```

- [ ] **Step 5: Deploy and rerun**

Deploy `Splits_AccessProcs.sql` via `MyMoneyAdmin.exe`. Build, then run the full `SqlServerStoredProcDatabaseTests` fixture — expect all tests so far still pass.

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Splits_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Splits (issue #22)"
```

---

### Task 10: Investment

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Access/Investments_AccessProcs.sql`
- Modify: `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs` (add investment-reading to `ReadTransactions`; add `UpdateInvestment` override)
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` (add `InsertUpdateDeleteInvestment_RoundTripsThroughStoredProcedures`)

**Interfaces:**
- Consumes: `ExecuteProc` (Task 1), `ReadTransactions`/`UpdateTransactions` (Task 8, which this task extends), `ReadSecurities`/`UpdateSecurities` (Task 5, an Investment requires a Security).
- Produces: `UpdateInvestment(Investment)` (`public override`) — called from `UpdateTransactions` (Task 8's code already has the `this.UpdateInvestment(t.Investment)` call site). This is the last task before Task 11's verification.

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void InsertUpdateDeleteInvestment_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var account = money.Accounts.AddAccount("SqlServerStoredProcDatabaseTests Investment Account");
            account.Type = AccountType.Brokerage;
            account.OnInserted();
            db.UpdateAccounts(money.Accounts);

            var security = money.Securities.AddSecurity(0);
            security.Name = "SqlServerStoredProcDatabaseTests Investment Security";
            security.Symbol = "ZZI";
            security.OnInserted();
            db.UpdateSecurities(money.Securities);

            var transaction = money.Transactions.NewTransaction(account);
            transaction.Date = new DateTime(2026, 1, 15);
            transaction.Amount = -1000.00m;
            money.Transactions.AddTransaction(transaction);
            var investment = transaction.GetOrCreateInvestment();
            investment.Security = security;
            investment.UnitPrice = 100.00m;
            investment.Units = 10;
            investment.Type = InvestmentType.Buy;
            transaction.OnInserted();
            db.UpdateTransactions(money.Transactions);

            var reloaded = new MyMoney();
            db.ReadSecurities(reloaded.Securities, reloaded);
            db.ReadAccounts(reloaded.Accounts, reloaded);
            db.ReadTransactions(reloaded.Transactions, reloaded);
            var reloadedAccount = reloaded.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Investment Account");
            var foundTransaction = reloaded.Transactions.GetTransactionsFrom(reloadedAccount).First();
            Assert.That(foundTransaction.Investment, Is.Not.Null);
            Assert.That(foundTransaction.Investment.UnitPrice, Is.EqualTo(100.00m));
            Assert.That(foundTransaction.Investment.Units, Is.EqualTo(10));

            foundTransaction.Investment.Units = 20;
            db.UpdateTransactions(reloaded.Transactions);

            var afterUpdate = new MyMoney();
            db.ReadSecurities(afterUpdate.Securities, afterUpdate);
            db.ReadAccounts(afterUpdate.Accounts, afterUpdate);
            db.ReadTransactions(afterUpdate.Transactions, afterUpdate);
            var afterUpdateAccount = afterUpdate.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Investment Account");
            var updatedTransaction = afterUpdate.Transactions.GetTransactionsFrom(afterUpdateAccount).First();
            Assert.That(updatedTransaction.Investment.Units, Is.EqualTo(20));

            updatedTransaction.OnDelete();
            var toDelete = new Transactions(afterUpdate);
            toDelete.AddTransaction(updatedTransaction);
            db.UpdateTransactions(toDelete);

            afterUpdateAccount.OnDelete();
            var accountsToDelete = new Accounts(afterUpdate);
            accountsToDelete.Add(afterUpdateAccount);
            db.UpdateAccounts(accountsToDelete);

            var securityToDelete = afterUpdate.Securities.FindSecurity("SqlServerStoredProcDatabaseTests Investment Security", false);
            securityToDelete.OnDelete();
            var securitiesToDelete = new Securities(afterUpdate);
            securitiesToDelete.Add(securityToDelete);
            db.UpdateSecurities(securitiesToDelete);

            var afterDelete = new MyMoney();
            db.ReadAccounts(afterDelete.Accounts, afterDelete);
            Assert.That(afterDelete.Accounts.FindAccount("SqlServerStoredProcDatabaseTests Investment Account"), Is.Null);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~InsertUpdateDeleteInvestment_RoundTripsThroughStoredProcedures"`
Expected: FAIL.

- [ ] **Step 3: Create the stored-proc script**

Create `Source/WPF/MyMoney.Data/SqlScripts/Access/Investments_AccessProcs.sql`:

```sql
-- Investments_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Investments table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Investments_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType, TaxExempt,
           Withholding, MarkUpDown, Taxes, Fees, [Load]
    FROM dbo.Investments ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Investments_Insert
    @Id BIGINT, @Security INT, @UnitPrice MONEY, @Units DECIMAL(18,4), @Commission MONEY,
    @InvestmentType INT, @TradeType INT, @TaxExempt BIT, @Withholding MONEY, @MarkUpDown MONEY,
    @Taxes MONEY, @Fees MONEY, @Load MONEY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Investments (Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType,
        TaxExempt, Withholding, MarkUpDown, Taxes, Fees, [Load])
    VALUES (@Id, @Security, @UnitPrice, @Units, @Commission, @InvestmentType, @TradeType, @TaxExempt,
        @Withholding, @MarkUpDown, @Taxes, @Fees, @Load);
END
GO

CREATE OR ALTER PROCEDURE dbo.Investments_Update
    @Id BIGINT, @Security INT, @UnitPrice MONEY, @Units DECIMAL(18,4), @Commission MONEY,
    @InvestmentType INT, @TradeType INT, @TaxExempt BIT, @Withholding MONEY, @MarkUpDown MONEY,
    @Taxes MONEY, @Fees MONEY, @Load MONEY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Investments SET
        Security = @Security, UnitPrice = @UnitPrice, Units = @Units, Commission = @Commission,
        InvestmentType = @InvestmentType, TradeType = @TradeType, TaxExempt = @TaxExempt,
        Withholding = @Withholding, MarkUpDown = @MarkUpDown, Taxes = @Taxes, Fees = @Fees, [Load] = @Load
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Investments_Delete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Investments WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Investments_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Investments_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Investments_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Investments_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Investments_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Investments_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Investments_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Investments_Delete TO MyMoneyTest;
GO
```

- [ ] **Step 4: Add investment-reading to `ReadTransactions` and add the `UpdateInvestment` override**

In `Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs`, find the end of the `ReadTransactions` override (the closing of the transfer-resolution `using (var connection ...)` block, right before `transactions.FireChangeEvent(...)`), and add a fourth read pass right after the transfer-resolution block but still inside the outer `using (var connection = ...)`:

```csharp
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
                        i.Security = money.Securities.FindSecurityAt(reader.GetInt32(1));
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
```

Then add the `UpdateInvestment` override (after `UpdateSplits`):

```csharp
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
                    ("@Id", i.Id), ("@Security", i.Security == null ? -1 : i.Security.Id), ("@UnitPrice", i.UnitPrice),
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
```

- [ ] **Step 5: Deploy and rerun**

Deploy `Investments_AccessProcs.sql` via `MyMoneyAdmin.exe` — this is the last of the 9 new scripts, so from this point on `BootstrapRunner.Run()` (Task 1 Step 4's full change) can be run end-to-end without a `FileNotFoundException`.

Build, then run the full `SqlServerStoredProcDatabaseTests` fixture — expect all tests (Payees + the 9 new ones across Tasks 2-10) to pass.

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Access/Investments_AccessProcs.sql Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add MyMoneyUser stored-proc CRUD coverage for Investment (issue #22)"
```

---

### Task 11: `SqlServerDatabaseContractTests` (the shared contract-test flavor) + close out issues

**Files:**
- Create: `Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs`
- Test: (the file above *is* the test — no separate test file)

**Interfaces:**
- Consumes: `DatabaseContractTests` (abstract base, `Source/WPF/MyMoney.TestSupport/DatabaseContractTests.cs`), `SqlServerStoredProcDatabase` (all overrides from Tasks 1-10).
- Produces: nothing further downstream — this is the plan's final proof step.

This is the plan's own verification that issue #22 is actually done: the *existing*, unmodified shared contract suite (`Load_BeforeAnySave_ReturnsEmptyGraph`, `Create_ThenCheckExists_ReturnsTrue`, `SaveAndReload_PreservesAccountAndTransactionData`, `Insert_ThenReload_ItemAppearsWithCleanState`, `Update_ThenReload_ChangePersisted`, `Delete_ThenReload_ItemIsGone`) passes against `SqlServerStoredProcDatabase` connected as `MyMoneyUser` — proving Accounts, Categories, Payees, and Transactions all work under the real tier the app uses, using nothing but the CRUD paths Tasks 2-10 built.

- [ ] **Step 1: Write the failing test (the new fixture itself)**

Create `Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs`:

```csharp
using System;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    /// <summary>
    /// Proves the shared IDatabase contract suite (see DatabaseContractTests)
    /// passes against SqlServerStoredProcDatabase connected as MyMoneyUser --
    /// the tier the shipped app actually runs under (see issue #22's design
    /// spec). Deliberately does NOT skip/Ignore when SQL Server is
    /// unreachable: unlike SqlServerStoredProcDatabaseTests's env-var-driven
    /// smoke tests, a broken connection here is a real signal that
    /// MyMoneyUser CRUD coverage has regressed, not an expected local-dev
    /// absence. Do not add Assert.Ignore/skip logic to this fixture.
    /// </summary>
    [TestFixture]
    public class SqlServerDatabaseContractTests : DatabaseContractTests
    {
        private const string UserConnectionEnvVar = "MYMONEY_TEST_SQLSERVER_USER_CONNECTION";
        private const string AdminConnectionEnvVar = "MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION";

        private static readonly string[] TablesToWipe =
        {
            "Splits", "Investments", "Transactions", "StockSplits", "Aliases",
            "Accounts", "Securities", "Currencies", "Categories", "Payees"
        };

        public override void SetUp()
        {
            WipeAllTables();
            base.SetUp();
        }

        protected override IDatabase CreateDatabase()
        {
            string connectionString = Environment.GetEnvironmentVariable(UserConnectionEnvVar);
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    $"{UserConnectionEnvVar} must be set to a MyMoneyUser connection string to run SqlServerDatabaseContractTests. " +
                    "This fixture intentionally fails rather than skips when SQL Server is unreachable.");
            }
            return new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };
        }

        private static void WipeAllTables()
        {
            string adminConnectionString = Environment.GetEnvironmentVariable(AdminConnectionEnvVar);
            if (string.IsNullOrEmpty(adminConnectionString))
            {
                throw new InvalidOperationException(
                    $"{AdminConnectionEnvVar} must be set to a MyMoneyAdmin connection string so SqlServerDatabaseContractTests can " +
                    "clean up the shared test database between runs.");
            }

            using (var connection = new SqlConnection(adminConnectionString))
            {
                connection.Open();
                foreach (string table in TablesToWipe)
                {
                    using (var command = new SqlCommand($"DELETE FROM dbo.[{table}];", connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
```

Tables are deleted in dependency order (children before parents: `Splits`/`Investments` before `Transactions`, `Transactions` before `Accounts`, `StockSplits`/`Aliases` before their referenced `Securities`/`Payees`, etc.) so foreign-key constraints (if any exist in the schema `LazyCreateTables()` generates) don't reject the deletes.

- [ ] **Step 2: Run test to verify it fails appropriately**

First, without setting `MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION` or `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`:

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqlServerDatabaseContractTests"`
Expected: every test in the fixture FAILS with the `InvalidOperationException` message from `WipeAllTables()` (not skipped/inconclusive) — this proves the "fail, don't skip" requirement actually holds before moving on.

Then set both environment variables to real `MyMoneyAdmin`/`MyMoneyUser` connection strings and rerun:

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqlServerDatabaseContractTests"`
Expected: FAILS differently now — either compiles and runs but fails on real assertions if Tasks 1-10 have a bug, or (if Tasks 1-10 are solid, which they should be at this point) this step actually PASSES already, since Task 11 doesn't add any new production code, only a test fixture exercising what's already built. If it passes here, that's fine — proceed to Step 3 as a formality (there's no "implementation" left to write for this task; Step 3 becomes a no-op confirmation rather than new code).

- [ ] **Step 3: No implementation step needed**

Unlike every previous task, there is no production code to write here — Task 11 only adds a test fixture. If Step 2's second run already passed, that confirms Tasks 1-10 are complete and correct. If it failed on a real assertion (not the `InvalidOperationException` guard), go back to whichever entity's task the failure implicates and fix it there, not in this file.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Source/WPF/MyMoney.sln`
Expected: `MyMoney.TestSupport` now shows the previous 13 tests plus 6 more from `SqlServerDatabaseContractTests` (19 total), all passing. `UnitTests` (32 passed/1 skipped) and `UITests` (2 passed) unchanged. `ScenarioTest`'s pre-existing, unrelated failure (see CLAUDE.md's gotchas section) is expected and not a regression.

- [ ] **Step 5: Update and close GitHub issues**

Update issue #1 (`gh issue comment 1 --body "..."` or via the GitHub UI): note that issue #22 is complete, so item #3's "third contract-test flavor" reference is now satisfied by `SqlServerDatabaseContractTests`.

Close issue #22 (`gh issue close 22 --comment "..."`) summarizing: all 9 entities now have `MyMoneyUser`-tier stored-proc CRUD coverage; `SqlServerDatabaseContractTests` proves the shared contract suite passes under that tier; issue #19's rich-dataset parity test can now run through `SqlServerStoredProcDatabase` + `MyMoneyUser` instead of the `MyMoneyAdmin` workaround originally being considered.

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs
git commit -m "Add SqlServerDatabaseContractTests proving MyMoneyUser CRUD coverage (closes #22)"
```

---

## Self-Review Notes

- **Spec coverage:** Section 1 (procs mirroring Payees template, three independent Transactions/Splits/Investment proc sets) → Tasks 2-10. Section 2 (shared `ExecuteProc` helper) → Task 1, consumed by every later task. Section 3 (`Load()` extended to all 9 entities) → one step per task, in FK-safe order (Categories/Currencies/Securities before Accounts before Transactions before Splits/Investment; Payees before Aliases). Section 4 (`SqlServerDatabaseContractTests`, admin-tier cleanup, fail-don't-skip) → Task 11. Out-of-scope items (issue #23's 6 entities, tiered-security model changes, `SupportsParameterizedUpdate`) are untouched by every task above.
- **Placeholder scan:** every task has complete, entity-specific SQL and C# derived from the actual `SqlDatabase.cs` generic methods (verified against `ReadAccounts`/`UpdateAccounts`, `ReadCategories`/`UpdateCategories`, `ReadCurrencies`/`UpdateCurrencies`, `ReadSecurities`/`UpdateSecurities`, `ReadStockSplits`/`UpdateStockSplits`, `ReadAliases`/`UpdateAliases`, `ReadTransactions`/`UpdateTransactions`/`UpdateSplits`/`UpdateInvestment`/`ReadInvestments` line-by-line); no task says "similar to Task N" instead of showing code.
- **Type/signature consistency:** `ExecuteProc(SqlConnection, string, params (string, object)[])` (Task 1) is called with the same signature in Tasks 2-10. `SqlDatabase.DBDateTimeParam`/`DBNullableDateTimeParam`/`DBGuidParam` are referenced as `internal static` (Task 1's promotion) consistently from Task 2 onward, always fully-qualified as `SqlDatabase.XxxParam(...)` since they're called from a subclass in the same assembly, not via inheritance. `ReadTransactions` keeps the base class's `ArrayList` return type across Tasks 8-10. `Load()`'s accumulated `this.ReadXxx(...)` call sequence is spelled out in full in Task 8 Step 5 to avoid any ambiguity about final ordering.
