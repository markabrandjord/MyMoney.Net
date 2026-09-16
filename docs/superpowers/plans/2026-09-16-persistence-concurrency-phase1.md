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

### Task 4: Derive foreign-key constraints automatically from `ColumnObjectMapping`

> **Pre-flight correction (ruled before dispatch):** the original draft of this task assumed
> issue #24's six named gaps (`Account.CategoryForPrincipal`/`CategoryForInterest`,
> `Split.Category`/`Split.Transaction`, `Transaction.Account`/`Payee`/`Category`) were plain
> `[ColumnMapping(ColumnName = "...")]` int columns that needed new `ForeignKeyTable`/
> `ForeignKeyColumn` arguments hand-added. Verified against the actual code
> (`Money.cs:3129`, `:3145`, `:11049`, `:13780`, `:13797`, and the `Transaction.Account`/`.Payee`
> declarations) that all of them are already declared via the domain model's existing
> `[ColumnObjectMapping(ColumnName = "...", KeyProperty = "Id")]` attribute
> (`Mapping.cs:190-193`, `internal class ColumnObjectMapping : ColumnMapping { public string
> KeyProperty { get; set; } }`) — a distinct, already-present attribute the original draft never
> checked for. Deriving the FK automatically from that existing attribute (via reflection on the
> referenced type's own `[TableMapping]`) fixes all six gaps *and* every other object-reference
> column in the schema in one generic change, with **zero manual edits to `Money.cs`** — safer and
> more complete than hand-flagging six properties by name.

**Files:**
- Modify: `Source/WPF/MyMoney.Business/Mapping.cs:190-193` (`ColumnObjectMapping`), `:215-234`
  (`MappingEngine.ResolveColumnType`)
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (`GetCreateTableScript`, extended further)
- Test: `Source/WPF/UnitTests/SqlMappingTests.cs`

**Interfaces:**
- Consumes: `GetCreateTableScript(TableMapping, DbFlavor)` (Task 3); existing
  `ColumnObjectMapping : ColumnMapping` and `MappingEngine.GetColumnsFromObject`/
  `ResolveColumnType` (`Mapping.cs:190-234`) — `GetColumnsFromObject` already collects
  `ColumnObjectMapping`-attributed properties into `TableMapping.Columns` today (confirmed:
  `MemberInfo.GetCustomAttributes(typeof(ColumnMapping), false)` matches subclass attribute
  instances too), so no change is needed there.
- Produces: `ColumnObjectMapping.ForeignKeyTable` (new property, populated automatically inside
  `ResolveColumnType` — never set by hand on any domain-model property). No later task in this
  phase consumes it further, but Phase 2 (which touches insert ordering for the new atomic
  primitives) must respect the same FK dependency order `Load()` already uses (Categories before
  Accounts before Transactions before Splits), since these FKs now enforce it at the database
  level, not just the C# object model.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/UnitTests/SqlMappingTests.cs`:

```csharp
        [TableMapping(TableName = "MappingTestParentTable")]
        private class FakeParentRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }
        }

        [TableMapping(TableName = "MappingTestChildTable")]
        private class FakeChildRow
        {
            [ColumnMapping(ColumnName = "Id", IsPrimaryKey = true)]
            public int Id { get; set; }

            [ColumnObjectMapping(ColumnName = "ParentId", KeyProperty = "Id")]
            public FakeParentRow Parent { get; set; }
        }

        [Test]
        public void GetCreateTableScript_DerivesForeignKeyFromColumnObjectMapping()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow) };
            string script = SqlDatabase.GetCreateTableScript(mapping, DbFlavor.SqlServer);
            Assert.That(script, Does.Contain("FOREIGN KEY ([ParentId]) REFERENCES [MappingTestParentTable]([Id])"));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateTableScript_DerivesForeignKeyFromColumnObjectMapping"`
Expected: FAIL — `ForeignKeyTable` doesn't exist on `ColumnObjectMapping` yet, and nothing derives it.

- [ ] **Step 3: Add the property and derive it during `ResolveColumnType`**

In `Source/WPF/MyMoney.Business/Mapping.cs`, change:

```csharp
    internal class ColumnObjectMapping : ColumnMapping
    {
        public string KeyProperty { get; set; }
    }
```

to:

```csharp
    internal class ColumnObjectMapping : ColumnMapping
    {
        public string KeyProperty { get; set; }
        public string ForeignKeyTable { get; set; }
    }
```

In `MappingEngine.ResolveColumnType`, find:

```csharp
            if (mapping is ColumnObjectMapping)
            {
                ColumnObjectMapping co = (ColumnObjectMapping)mapping;
                string propName = co.KeyProperty;
                if (string.IsNullOrEmpty(propName))
                {
                    throw new Exception("ColumnObjectMapping must have a valid KeyProperty");
                }
                PropertyInfo pi = propertyType.GetProperty(propName);
                if (pi == null)
                {
                    throw new Exception(string.Format("Could not find KeyProperty named '{0}' on class '{1}'", propName, propertyType.FullName));
                }
                // get the dereferenced type.
                propertyType = pi.PropertyType;

            }
```

and insert the FK-table derivation *before* the `propertyType = pi.PropertyType;` line (while
`propertyType` still refers to the referenced object type, e.g. `Account`, not yet overwritten
with the key property's type):

```csharp
            if (mapping is ColumnObjectMapping)
            {
                ColumnObjectMapping co = (ColumnObjectMapping)mapping;
                string propName = co.KeyProperty;
                if (string.IsNullOrEmpty(propName))
                {
                    throw new Exception("ColumnObjectMapping must have a valid KeyProperty");
                }
                PropertyInfo pi = propertyType.GetProperty(propName);
                if (pi == null)
                {
                    throw new Exception(string.Format("Could not find KeyProperty named '{0}' on class '{1}'", propName, propertyType.FullName));
                }

                // Derive the FK target table from the referenced type's own [TableMapping] --
                // see docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R7 (#24).
                object[] tableAttrs = propertyType.GetCustomAttributes(typeof(TableMapping), false);
                if (tableAttrs != null && tableAttrs.Length > 0)
                {
                    co.ForeignKeyTable = ((TableMapping)tableAttrs[0]).TableName;
                }

                // get the dereferenced type.
                propertyType = pi.PropertyType;

            }
```

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`'s `GetCreateTableScript`, after the version-column
block added in Task 3 and before the closing `sb.AppendLine(")")`:

```csharp
            foreach (ColumnMapping column in mapping.Columns)
            {
                if (column is ColumnObjectMapping co && !string.IsNullOrEmpty(co.ForeignKeyTable))
                {
                    sb.AppendLine(",");
                    sb.Append(string.Format("  FOREIGN KEY ([{0}]) REFERENCES [{1}]([{2}])",
                        column.ColumnName, co.ForeignKeyTable, co.KeyProperty));
                }
            }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateTableScript_DerivesForeignKeyFromColumnObjectMapping"`
Expected: PASS.

- [ ] **Step 5: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green. This change now emits real FK
constraints for every existing `ColumnObjectMapping` column in the whole schema (Accounts,
Transactions, Splits, and others) — pay particular attention to any SQLite-backed test that
creates a fresh sample database. A genuinely orphaned reference (a pre-existing bug, not this
task's fault) would now surface as a real FK-constraint failure at table-creation/insert time
instead of silently succeeding. If one fails, that's a real pre-existing data-integrity bug the
new FK just caught — do not weaken the constraint to make the symptom disappear; report it and
pause this task for a decision rather than silently loosening the FK.

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.Business/Mapping.cs Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/UnitTests/SqlMappingTests.cs
git commit -m "Derive foreign-key constraints from ColumnObjectMapping automatically (closes #24's FK gap)"
```

---

### Task 5: Derive secondary indexes automatically from `ColumnObjectMapping`

> **Pre-flight correction (ruled before dispatch):** same reasoning as Task 4 — issue #24 named
> two specific columns (`Transaction.Account`, `Split.Transaction`) as needing indexes, but the
> original draft's `IsIndexed` flag hand-added to those two properties would miss every other
> object-reference column in the schema for no reason: every `ColumnObjectMapping` column is
> exactly the kind of foreign-key/join column that benefits from an index, and Task 4 already
> gives this task a reliable way to identify all of them generically. Auto-indexing every
> `ColumnObjectMapping` column — not just the two issue #24 named — is strictly more complete,
> equally low-risk (purely additive `CREATE INDEX` statements), and needs no new `IsIndexed`
> property or any manual `Money.cs` edits at all.

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (new method, called from `CreateOrUpdateTable`)
- Modify: `Source/WPF/MyMoney.Data/SqliteDatabase.cs` (`CreateOrUpdateTable`, same wiring)
- Test: `Source/WPF/UnitTests/SqlMappingTests.cs`

**Interfaces:**
- Consumes: `TableMapping`/`ColumnObjectMapping.ForeignKeyTable` (Task 4), `this.ExecuteNonQuery`
  (existing on both `SqlDatabase`/`SqliteDatabase`).
- Produces: `SqlDatabase.GetCreateIndexScripts(TableMapping mapping)` returning
  `IEnumerable<string>` — no later task in this phase consumes it further; Phase 2 doesn't need
  to touch index scripts.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/UnitTests/SqlMappingTests.cs` (reusing `FakeParentRow`/`FakeChildRow` from
Task 4):

```csharp
        [Test]
        public void GetCreateIndexScripts_EmitsIndexForColumnObjectMapping()
        {
            var mapping = new TableMapping { ObjectType = typeof(FakeChildRow) };
            var scripts = SqlDatabase.GetCreateIndexScripts(mapping).ToList();
            Assert.That(scripts, Has.One.Matches<string>(s =>
                s.Contains("CREATE INDEX") && s.Contains("MappingTestChildTable") && s.Contains("ParentId")));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateIndexScripts_EmitsIndexForColumnObjectMapping"`
Expected: FAIL — `GetCreateIndexScripts` doesn't exist yet.

- [ ] **Step 3: Add the emission method**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, add a new method near `GetCreateTableScript`:

```csharp
        internal static IEnumerable<string> GetCreateIndexScripts(TableMapping mapping)
        {
            foreach (ColumnMapping column in mapping.Columns)
            {
                if (column is ColumnObjectMapping)
                {
                    yield return string.Format("CREATE INDEX [IX_{0}_{1}] ON [{0}] ([{1}])",
                        mapping.TableName, column.ColumnName);
                }
            }
        }
```

(Add `using System.Collections.Generic;` at the top of the file if not already present.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~GetCreateIndexScripts_EmitsIndexForColumnObjectMapping"`
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

- [ ] **Step 6: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/MyMoney.Data/SqliteDatabase.cs Source/WPF/UnitTests/SqlMappingTests.cs
git commit -m "Derive secondary indexes from ColumnObjectMapping automatically (closes #24's index gap)"
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
