# Persistence Concurrency — Phase 1 (Test Infra + Schema Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the two test-infrastructure prerequisites (#28, #25) and the schema-level
foundation (rowversion/version columns, missing foreign keys, missing indexes — issue #24) that
every later phase of the persistence-concurrency redesign depends on.

**Architecture:** No behavior changes to `IDatabase`'s write API yet — this phase only makes the
existing generic table-creation mechanism (`ColumnMapping`/`TableMapping` in `MyMoney.Business`,
`GetCreateTableScript` in `MyMoney.Data`) capable of emitting a per-row version column, foreign
keys, and secondary indexes, and adds the object-model storage slot (`PersistentObject.RowVersion`)
those future primitives will read and check. Nothing yet calls or enforces the version check — that
lands in Phase 2, alongside the new `SaveOne`/`SaveTransfer`/`SaveBatch` primitives themselves, since
each primitive's write logic and its conflict check are naturally the same piece of code.

**Tech Stack:** .NET 10 (`net10.0-windows7.0`), `Microsoft.Data.SqlClient`, `System.Data.SQLite`,
NUnit 4.6.1.

**Spec:** `docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md` (R4's schema
groundwork, R6, R7's #24 fold-in)

## Global Constraints

- SQL Server support stays DEBUG-only; Release always uses SQLite. No change to that in this phase.
- The generic `GetCreateTableScript`/`ColumnMapping` mechanism must stay engine-agnostic in the
  parts it already handles (existing column emission) — only the new version/FK/index emission
  is engine-branching, and it branches on the existing `DbFlavor` enum, not a new abstraction.
  New tables (`Money.cs`) require the `[TableMapping]`/`[ColumnMapping]` attribute style already
  used by every other persistent entity — no `Fluent`/code-first-style mapping introduced here.
- This phase adds no new `IDatabase` methods and does not touch `Save(MyMoney)` or any `UpdateXxx`/
  `ReadXxx` override — those are Phase 2+. Nothing in this phase is user-visible.
- Every task must leave `dotnet build Source/WPF/MyMoney.sln` at 0 errors and
  `dotnet test Source/WPF/MyMoney.sln -m:1` green (see Task 2's Global Constraint on why `-m:1`).

---

### Task 1: Make `SqlServerDatabaseContractTests` discoverable (#28)

**Files:**
- Modify: `Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs:31-32`
- Modify: `docs/dev/index.md`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — this task only adds test-discovery metadata, no production code.

- [ ] **Step 1: Add the NUnit category attribute**

In `Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs`, change:

```csharp
    [TestFixture]
    public class SqlServerDatabaseContractTests : DatabaseContractTests
```

to:

```csharp
    [TestFixture]
    [Category("RequiresSqlServer")]
    public class SqlServerDatabaseContractTests : DatabaseContractTests
```

- [ ] **Step 2: Verify the category is honored by an explicit filter**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "TestCategory=RequiresSqlServer" --list-tests`
Expected: lists every test method in `SqlServerDatabaseContractTests` and nothing from
`MockDatabaseContractTests`/`SqliteDatabaseContractTests`/`MockDatabaseSmokeTests` (none of those
carry the category).

- [ ] **Step 3: Document how to run it**

Add a new subsection to `docs/dev/index.md` (near wherever the existing `dotnet test` invocations
are documented) with this exact content:

```markdown
### Running the SQL Server contract tests

`SqlServerDatabaseContractTests` (in `MyMoney.TestSupport`) is tagged
`[Category("RequiresSqlServer")]` and is **not** run by
`dotnet test Source/WPF/UnitTests/UnitTests.csproj` or CI. It requires a real SQL Server instance
and three environment variables (`MYMONEY_TEST_SQLSERVER_USER_CONNECTION`,
`MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`, `MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1`) — it fails
fast, rather than skipping, if any are unset. Run it explicitly:

    dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "TestCategory=RequiresSqlServer"

This is a manual/local verification step, not part of CI — CI has no SQL Server instance to point
it at.
```

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs docs/dev/index.md
git commit -m "Tag SqlServerDatabaseContractTests RequiresSqlServer and document how to run it (closes #28)"
```

---

### Task 2: Stop `dotnet test`'s project parallelism from racing the two SQL-Server test projects (#25)

**Files:**
- Modify: `docs/dev/index.md`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — documentation-only fix, matching the root cause (MSBuild's cross-project
  test-host parallelism, not anything fixable inside a `.runsettings` or NUnit attribute, since the
  collision is between two separate OS processes each starting a fresh `MyMoney()` with the same
  deterministic ID counters against one shared live database).

- [ ] **Step 1: Confirm the documented fix still reproduces/resolves correctly**

Run (requires `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`/`MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`/
`MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1` set):
`dotnet test Source/WPF/MyMoney.sln`
Expected: intermittent PK-violation failures in `UnitTests` are possible (this is the bug, not a
regression you're introducing).

Then run: `dotnet test Source/WPF/MyMoney.sln -m:1`
Expected: fully green, no code changes — `-m:1` forces MSBuild to run one project's test host at a
time, so `MyMoney.TestSupport` and `UnitTests` never write to the shared live SQL Server
concurrently.

- [ ] **Step 2: Document `-m:1` as required whenever SQL Server env vars are set**

Add to `docs/dev/index.md`, immediately after (or near) the existing `dotnet test` command
reference:

```markdown
### Required flag when SQL Server env vars are set

`dotnet test Source/WPF/MyMoney.sln` runs each test project's host as a separate process. When
`MYMONEY_TEST_SQLSERVER_USER_CONNECTION`/`MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION` are set,
`MyMoney.TestSupport` (home of `SqlServerDatabaseContractTests`) and `UnitTests` (home of
`SqlServerStoredProcDatabaseTests`) can run concurrently and race against the same live database —
both start a fresh `MyMoney()` whose ID counters begin from the same deterministic value, so their
first-inserted rows can collide on primary key. Always pass `-m:1` to force serial project
execution when those env vars are set:

    dotnet test Source/WPF/MyMoney.sln -m:1

This is not needed when the SQL Server env vars are unset (those test fixtures skip/no-op) or when
running a single test project directly.
```

- [ ] **Step 3: Commit**

```bash
git add docs/dev/index.md
git commit -m "Document -m:1 as required for dotnet test when SQL Server env vars are set (closes #25)"
```

---

### Task 3: Add `RowVersion`/`Version` column emission to the shared schema generator

**Files:**
- Modify: `Source/WPF/MyMoney.Business/Mapping.cs:23-76` (`TableMapping`)
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs:324-343` (`LazyCreateTables`), `:345-378`
  (`GetCreateTableScript`), `:380-` (`CreateOrUpdateTable`)
- Modify: `Source/WPF/MyMoney.Data/SqliteDatabase.cs:552-` (`CreateOrUpdateTable`), and its second
  call site at `:651`
- Test: `Source/WPF/UnitTests/SqlMappingTests.cs`

**Interfaces:**
- Consumes: `DbFlavor` enum (`MyMoney.Business/IDatabase.cs:12`), existing `TableMapping`/
  `ColumnMapping` (`MyMoney.Business/Mapping.cs`).
- Produces: `SqlDatabase.GetCreateTableScript(TableMapping mapping, DbFlavor flavor)` (signature
  change — both call sites updated in this task) — Task 4/5 (FK/index emission) and Phase 2's
  per-entity read/write wiring both depend on this signature and on the reserved column name
  `RowVersion` (SQL Server) / `Version` (SQLite) existing in every table.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/UnitTests/SqlMappingTests.cs` (create the file if it doesn't exist yet — check
first with `Test-Path` before assuming):

```csharp
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Data.Tests
{
    [TestFixture]
    public class SqlMappingTests
    {
        [TableMapping(TableName = "MappingTestTable")]
        private class FakeRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }
        }

        [Test]
        public void GetCreateTableScript_SqlServer_AppendsRowVersionColumn()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeRow) };
            string script = SqlDatabase.GetCreateTableScript(mapping, DbFlavor.SqlServer);
            Assert.That(script, Does.Contain("[RowVersion] ROWVERSION NOT NULL"));
        }

        [Test]
        public void GetCreateTableScript_Sqlite_AppendsIntegerVersionColumn()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeRow) };
            string script = SqlDatabase.GetCreateTableScript(mapping, DbFlavor.Sqlite);
            Assert.That(script, Does.Contain("[Version] INTEGER NOT NULL DEFAULT 1"));
            Assert.That(script, Does.Not.Contain("ROWVERSION"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlMappingTests"`
Expected: FAIL with a compile error — `GetCreateTableScript` doesn't yet take a `DbFlavor`
parameter, and (confirmed: no `InternalsVisibleTo` exists anywhere in `MyMoney.Data` today) the
test project can't see it yet either. Add, in
`Source/WPF/MyMoney.Data/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnitTests")]
```

Re-run the same command — it should now fail only on the missing `DbFlavor` parameter, not on
visibility.

- [ ] **Step 3: Change `GetCreateTableScript`'s signature and add version-column emission**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, change:

```csharp
        internal static string GetCreateTableScript(TableMapping mapping)
        {
            ...
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("create table [{0}] (", mapping.TableName));
            bool first = true;
            foreach (ColumnMapping column in mapping.Columns)
            {
                // generate SQL...
                if (!first)
                {
                    sb.AppendLine(",");
                }
                column.GetSqlDefinition(sb);

                first = false;
            }
            sb.AppendLine();
            sb.AppendLine(")");
            return sb.ToString();
        }
```

to:

```csharp
        internal static string GetCreateTableScript(TableMapping mapping, DbFlavor flavor)
        {
            ...
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("create table [{0}] (", mapping.TableName));
            bool first = true;
            foreach (ColumnMapping column in mapping.Columns)
            {
                // generate SQL...
                if (!first)
                {
                    sb.AppendLine(",");
                }
                column.GetSqlDefinition(sb);

                first = false;
            }

            // Every table gets an optimistic-concurrency version column, engine-appropriate:
            // SQL Server's ROWVERSION is engine-maintained (no application code ever sets it);
            // SQLite has no equivalent type, so it's a plain application-maintained counter.
            // See docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R4.
            sb.AppendLine(",");
            if (flavor == DbFlavor.SqlServer)
            {
                sb.Append("  [RowVersion] ROWVERSION NOT NULL");
            }
            else
            {
                sb.Append("  [Version] INTEGER NOT NULL DEFAULT 1");
            }

            sb.AppendLine();
            sb.AppendLine(")");
            return sb.ToString();
        }
```

Update both call sites to pass the flavor:

`Source/WPF/MyMoney.Data/SqlDatabase.cs` inside `CreateOrUpdateTable` (the one that calls
`GetCreateTableScript(mapping)` around line 385): change to
`GetCreateTableScript(mapping, this.DbFlavor)`.

`Source/WPF/MyMoney.Data/SqliteDatabase.cs`, both call sites (lines 557 and 651): change
`GetCreateTableScript(mapping)` to `GetCreateTableScript(mapping, DbFlavor.Sqlite)`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlMappingTests"`
Expected: PASS.

- [ ] **Step 5: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green (SQLite-backed tests exercise the new
column via real `LazyCreateTables()` calls; no existing test should reference `RowVersion`/
`Version` yet, since nothing reads/writes it until Phase 2).

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.Business/Mapping.cs Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/MyMoney.Data/SqliteDatabase.cs Source/WPF/MyMoney.Data/Properties/AssemblyInfo.cs Source/WPF/UnitTests/SqlMappingTests.cs
git commit -m "Add per-flavor optimistic-concurrency version column to generated schema"
```

---

### Task 4: Add foreign-key emission and apply it to the gaps issue #24 names

**Files:**
- Modify: `Source/WPF/MyMoney.Business/Mapping.cs:78-183` (`ColumnMapping`)
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (`GetCreateTableScript`, extended further)
- Modify: `Source/WPF/MyMoney.Business/Money.cs` — add `ForeignKeyTable`/`ForeignKeyColumn` to the
  specific columns issue #24 named: `Account.CategoryIdForPrincipal`/`CategoryIdForInterest` →
  `Categories.Id`; `Split.Category` → `Categories.Id`; `Split.Transaction` → `Transactions.Id`;
  `Transaction.Account` → `Accounts.Id`; `Transaction.Payee` → `Payees.Id`; `Transaction.Category`
  → `Categories.Id`.
- Test: `Source/WPF/UnitTests/SqlMappingTests.cs`

**Interfaces:**
- Consumes: `GetCreateTableScript(TableMapping, DbFlavor)` (Task 3).
- Produces: `ColumnMapping.ForeignKeyTable`/`ForeignKeyColumn` properties — no later task in this
  phase consumes them further, but Phase 2 (which touches insert-ordering for the new atomic
  primitives) must respect the same FK dependency order `Load()` already uses (Categories before
  Accounts before Transactions before Splits), since these FKs will now enforce it at the database
  level, not just the C# object model.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/UnitTests/SqlMappingTests.cs`:

```csharp
        private class FakeParentRow
        {
            public int Id { get; set; }
        }

        [TableMapping(TableName = "MappingTestChildTable")]
        private class FakeChildRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }

            [ColumnMapping(ColumnName = "ParentId", ForeignKeyTable = "MappingTestTable", ForeignKeyColumn = "Id")]
            public int ParentId { get; set; }
        }

        [Test]
        public void GetCreateTableScript_EmitsForeignKeyConstraint()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow) };
            string script = SqlDatabase.GetCreateTableScript(mapping, DbFlavor.SqlServer);
            Assert.That(script, Does.Contain("FOREIGN KEY ([ParentId]) REFERENCES [MappingTestTable]([Id])"));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateTableScript_EmitsForeignKeyConstraint"`
Expected: FAIL — `ForeignKeyTable`/`ForeignKeyColumn` don't exist on `ColumnMapping` yet.

- [ ] **Step 3: Add the properties and emission logic**

In `Source/WPF/MyMoney.Business/Mapping.cs`, add to `ColumnMapping`:

```csharp
        public string ForeignKeyTable { get; set; }
        public string ForeignKeyColumn { get; set; }
```

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`'s `GetCreateTableScript`, after the version-column
block added in Task 3 and before the closing `sb.AppendLine(")")`:

```csharp
            foreach (ColumnMapping column in mapping.Columns)
            {
                if (!string.IsNullOrEmpty(column.ForeignKeyTable))
                {
                    sb.AppendLine(",");
                    sb.Append(string.Format("  FOREIGN KEY ([{0}]) REFERENCES [{1}]([{2}])",
                        column.ColumnName, column.ForeignKeyTable, column.ForeignKeyColumn));
                }
            }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateTableScript_EmitsForeignKeyConstraint"`
Expected: PASS.

- [ ] **Step 5: Apply the new attributes to the real gaps issue #24 named**

In `Source/WPF/MyMoney.Business/Money.cs`, update the six columns listed in this task's Files
section. Example for `Account.CategoryIdForPrincipal` (find the existing attribute and extend it —
do not remove `AllowNulls`/existing properties already on it):

```csharp
        [ColumnMapping(ColumnName = "CategoryIdForPrincipal", ForeignKeyTable = "Categories", ForeignKeyColumn = "Id")]
```

Repeat for `CategoryIdForInterest` (→ `Categories`/`Id`), `Split.Category` (→ `Categories`/`Id`),
`Split.Transaction` (→ `Transactions`/`Id`), `Transaction.Account` (→ `Accounts`/`Id`),
`Transaction.Payee` (→ `Payees`/`Id`), `Transaction.Category` (→ `Categories`/`Id`) — locate each
by its existing `[ColumnMapping(ColumnName = "...")]` attribute and add the two new named
arguments alongside whatever's already there.

- [ ] **Step 6: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green. Pay particular attention to any
SQLite-backed test that creates a fresh sample database — a genuinely orphaned reference (a bug, not
this task's fault) would now surface as a real FK-constraint failure at table-creation/insert time
instead of silently succeeding. If one fails, that's a real pre-existing data-integrity bug the new
FK just caught — do not weaken the constraint to make the symptom disappear; report it and pause
this task for a decision rather than silently loosening the FK.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Business/Mapping.cs Source/WPF/MyMoney.Business/Money.cs Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/UnitTests/SqlMappingTests.cs
git commit -m "Add foreign-key emission to generated schema; apply to issue #24's named gaps"
```

---

### Task 5: Add secondary-index emission and apply it to the gaps issue #24 names

**Files:**
- Modify: `Source/WPF/MyMoney.Business/Mapping.cs` (`ColumnMapping`)
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (new method, called from `CreateOrUpdateTable`)
- Modify: `Source/WPF/MyMoney.Business/Money.cs` — flag `Transaction.Account` and `Split.Transaction`
  as indexed (the two issue #24 called out explicitly: "all transactions for this account", "all
  splits for this transaction").
- Test: `Source/WPF/UnitTests/SqlMappingTests.cs`

**Interfaces:**
- Consumes: `TableMapping`/`ColumnMapping` (Task 3/4), `this.ExecuteNonQuery` (existing, both
  `SqlDatabase`/`SqliteDatabase` already have it).
- Produces: `SqlDatabase.GetCreateIndexScripts(TableMapping mapping)` returning
  `IEnumerable<string>` — no later task in this phase consumes it further; Phase 2 doesn't need
  to touch index scripts.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/UnitTests/SqlMappingTests.cs`:

```csharp
        [TableMapping(TableName = "MappingTestIndexedTable")]
        private class FakeIndexedRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }

            [ColumnMapping(ColumnName = "ParentId", IsIndexed = true)]
            public int ParentId { get; set; }
        }

        [Test]
        public void GetCreateIndexScripts_EmitsIndexForFlaggedColumn()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeIndexedRow) };
            var scripts = SqlDatabase.GetCreateIndexScripts(mapping).ToList();
            Assert.That(scripts, Has.One.Matches<string>(s =>
                s.Contains("CREATE INDEX") && s.Contains("MappingTestIndexedTable") && s.Contains("ParentId")));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateIndexScripts_EmitsIndexForFlaggedColumn"`
Expected: FAIL — `IsIndexed` and `GetCreateIndexScripts` don't exist yet.

- [ ] **Step 3: Add the property and the emission method**

In `Source/WPF/MyMoney.Business/Mapping.cs`, add to `ColumnMapping`:

```csharp
        public bool IsIndexed { get; set; }
```

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, add a new method near `GetCreateTableScript`:

```csharp
        internal static IEnumerable<string> GetCreateIndexScripts(TableMapping mapping)
        {
            foreach (ColumnMapping column in mapping.Columns)
            {
                if (column.IsIndexed)
                {
                    yield return string.Format("CREATE INDEX [IX_{0}_{1}] ON [{0}] ([{1}])",
                        mapping.TableName, column.ColumnName);
                }
            }
        }
```

(Add `using System.Collections.Generic;` at the top of the file if not already present.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateIndexScripts_EmitsIndexForFlaggedColumn"`
Expected: PASS.

- [ ] **Step 5: Wire index creation into `CreateOrUpdateTable`'s new-table path**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`'s `CreateOrUpdateTable`, in the branch that creates a
brand-new table (the `if (!this.TableExists(...))` branch), after
`this.ExecuteNonQuery(createTable);`, add:

```csharp
                foreach (string indexScript in GetCreateIndexScripts(mapping))
                {
                    this.ExecuteNonQuery(indexScript);
                }
```

Repeat the identical addition in `Source/WPF/MyMoney.Data/SqliteDatabase.cs`'s
`CreateOrUpdateTable`, in its equivalent new-table branch (around line 557, right after its own
`this.ExecuteNonQuery(createTable);` call).

- [ ] **Step 6: Flag the two real columns issue #24 named**

In `Source/WPF/MyMoney.Business/Money.cs`, add `IsIndexed = true` to the existing
`[ColumnMapping(ColumnName = "Account", ...)]` on `Transaction.Account`, and to the existing
`[ColumnMapping(ColumnName = "Transaction", ...)]` on `Split.Transaction`.

- [ ] **Step 7: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green.

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney.Business/Mapping.cs Source/WPF/MyMoney.Business/Money.cs Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/MyMoney.Data/SqliteDatabase.cs Source/WPF/UnitTests/SqlMappingTests.cs
git commit -m "Add secondary-index emission to generated schema; apply to issue #24's named gaps"
```

---

### Task 6: Add the `RowVersion` storage slot to `PersistentObject`

**Files:**
- Modify: `Source/WPF/MyMoney.Business/Money.cs:389-` (`PersistentObject`)
- Test: `Source/WPF/UnitTests/DataTests.cs` (or wherever `PersistentObject` itself already has
  direct unit coverage — check first; if none exists, add to `SqlMappingTests.cs`)

**Interfaces:**
- Consumes: nothing new.
- Produces: `PersistentObject.RowVersion` (`public long`, get/set) — Phase 2's `SaveOne`/
  `SaveTransfer`/`SaveBatch` implementations populate this on load and check it on write; nothing
  in this phase populates or reads it yet (the column exists in the schema per Task 3, but no
  read/write path touches the property until Phase 2, since that wiring is naturally part of
  building each entity's atomic-write primitive, not a separate pass).

- [ ] **Step 1: Write the failing test**

```csharp
        [Test]
        public void PersistentObject_RowVersion_DefaultsToZeroAndIsSettable()
        {
            var account = new Account();
            Assert.That(account.RowVersion, Is.EqualTo(0));
            account.RowVersion = 42;
            Assert.That(account.RowVersion, Is.EqualTo(42));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~PersistentObject_RowVersion_DefaultsToZeroAndIsSettable"`
Expected: FAIL — `RowVersion` doesn't exist.

- [ ] **Step 3: Add the property**

In `Source/WPF/MyMoney.Business/Money.cs`'s `PersistentObject` class, add:

```csharp
        /// <summary>
        /// Opaque optimistic-concurrency token: SQL Server's native ROWVERSION converted to a
        /// long via BitConverter for storage-agnostic use here, or SQLite's plain integer
        /// counter, depending on which IDatabase loaded this object. Only ever compared for
        /// exact equality (never ordering) when writing back — see
        /// docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R4.
        /// </summary>
        [XmlIgnore]
        public long RowVersion { get; set; }
```

(This property is deliberately excluded from `[DataContract]`/XML serialization — it is
persistence-tier bookkeeping, not domain data, and must not round-trip through `XmlStore`/
`MockDatabase`'s `DataContractSerializer` snapshot. Confirm `XmlIgnore` is already `using`'d at the
top of `Money.cs` — it is, per its existing use elsewhere in the file.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~PersistentObject_RowVersion_DefaultsToZeroAndIsSettable"`
Expected: PASS.

- [ ] **Step 5: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green (confirms `RowVersion` being excluded
from serialization doesn't break any existing `MockDatabase`/`XmlStore` round-trip test).

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.Business/Money.cs Source/WPF/UnitTests/
git commit -m "Add PersistentObject.RowVersion storage slot for future optimistic-concurrency checks"
```

---

## What's next

This plan stops at the schema/property foundation deliberately — it's the part every later phase
depends on, and it's already substantial once you account for the engine-branching in the shared
mapping code. The next plan (Phase 2) builds `IDatabase.SaveOne<T>`/`SaveTransfer`/`SaveBatch` with
real atomicity and the actual `RowVersion` conflict check wired into each entity's write path, for
`MockDatabase` first, then `SqliteDatabase` (WAL mode + `busy_timeout`), then
`SqlServerStoredProcDatabase` (proper `BEGIN/END TRAN` spanning each primitive's stored-proc calls —
closing issue #27). Phase 3 wires the ~20 UI/business call sites (register grid, transfer, QIF/OFX/
CSV import, reconciliation, the three merge dialogs, recategorize, and the remaining single-entity
dialogs) to the new primitives and retires `IDatabase.Save(MyMoney)` (issue #1's remaining #19
slice rides along here, alongside Phase 2's SQL Server work). Phase 4 is Layer 2 (the `UiDispatcher`/
`SynchronizationContext` decision for issue #7, and the pull-based refresh mechanism).
