# Persistence Concurrency — Phase 2b (SQLite Infra + Category Proof-of-Pattern) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `SqliteDatabase` its own real transaction/concurrency infrastructure (WAL mode,
`busy_timeout`, an explicit per-call `SQLiteTransaction`, a row-count-returning SQL-execution path)
and use it to implement `SaveOne`/`SaveBatch` for real for exactly one entity type (`Category`) —
proving the pattern end-to-end (real atomic transaction, real `RowVersion` optimistic-concurrency
conflict detection, real `Load()` round-trip of the version column) before committing to writing the
same shape ten more times for the remaining aggregate-root types.

**Architecture:** Phase 2a proved the *contract* (shapes, exception semantics, atomicity-on-conflict
behavior) against `MockDatabase`, an in-memory double with no real transactions. Phase 2b is the
first *real* storage engine, and SQLite's per-row optimistic-concurrency mechanism works differently
from `MockDatabase`'s: instead of a separate validate-then-mutate pass over an in-memory dictionary,
each `UPDATE`/`DELETE` carries its own `WHERE Id=@Id AND Version=@Expected` clause and is executed
inside one real `SQLiteTransaction` spanning the whole `SaveBatch` call. A zero-rows-affected
`UPDATE`/`DELETE` *is* the conflict signal — no separate check needed — and if it happens partway
through a multi-root batch, the transaction's rollback (triggered from the same `catch` that
converts it into `ConcurrencyConflictException`) undoes everything already written in that call,
giving genuine all-or-nothing atomicity for free from the database itself, not from application-level
two-phase bookkeeping the way `MockDatabase` needed. This also means two of `MockDatabase`'s
[Phase 2a final-review fixes](2026-09-16-persistence-concurrency-phase2a.md) don't need SQLite
analogues: a duplicate root in one batch naturally conflicts on its second write (the first write's
commit-not-yet-happened value only exists at COMMIT time, but within one transaction the *second*
`UPDATE` against the *same* uncommitted row still sees the first `UPDATE`'s effect per SQLite's
read-your-own-writes semantics inside a transaction, so its `WHERE Version=@Expected` — still holding
the caller's original stale value — finds zero rows and conflicts correctly), and there is no
"multi-graph batch" failure mode at all, since each row write targets the real on-disk table by `Id`,
never a whole in-memory graph.

Scope is deliberately narrow: **only `Category`** (the simplest aggregate root — no owned children,
no FK columns) gets a real `SaveOne`/`SaveBatch` implementation in this plan. Every other aggregate
root type still falls through to the inherited Phase 2a stub (`NotImplementedException`) when passed
to `SqliteDatabase.SaveOne`/`SaveBatch`. `SaveTransfer` is untouched (still throws) — it's
`Transaction`-specific and `Transaction` isn't wired yet. Extending the now-proven pattern to the
remaining ~10 entity types, `SaveOne<Transaction>`'s compound Splits/Investment handling,
`SaveOne<RentBuilding>`'s compound RentUnit handling, and `SaveTransfer` is a follow-up plan (not yet
written) — see "What's next".

**Key design facts this plan depends on** (verified against actual code in this session, not
assumed):
- `SqliteDatabase : SqlServerDatabase` (`SqliteDatabase.cs:14`) inherits `Save(MyMoney)` and the
  Phase 2a `SaveOne`/`SaveTransfer`/`SaveBatch` stubs unchanged — it does not override any
  `UpdateXxx`/`ReadXxx` method today.
- `SqlServerDatabase.transaction` (`SqlDatabase.cs:703`, `private SqlTransaction`) is **private** —
  `SqliteDatabase` cannot see it even if it wanted to. SQLite needs its own transaction field; there
  is no shared engine-agnostic transaction machinery to reuse.
- `SqliteDatabase`'s existing `ExecuteNonQuery`/`ExecuteScalar`/`ExecuteReader` overrides
  (`SqliteDatabase.cs:422-556`) build every `SQLiteCommand` with **no transaction parameter at all**
  — `Save(MyMoney)`'s existing SQLite atomicity rests entirely on `System.Data.SQLite`'s undocumented
  connection-level auto-enlist behavior. This plan does not touch those existing methods (additive
  only) — it adds new, separate, explicitly-transacted methods for the new primitives.
- The `Version` column (SQLite's per-flavor name for the optimistic-concurrency column, added in
  Phase 1) is **completely dead in C# code today** — grepped, the only reference anywhere in
  `SqlDatabase.cs`/`SqliteDatabase.cs` is Phase 1's schema-diffing guard that skips it during
  column-drop detection. No `SELECT`, `INSERT`, or `UPDATE` touches it. This plan is starting from a
  clean slate, not extending partial wiring.
- `ExecuteNonQuery` returns `void` everywhere today (base `SqlDatabase.cs:986`/`:1014`, SQLite
  override `SqliteDatabase.cs:484`/`:510`) — the underlying `command.ExecuteNonQuery()` int
  (rows-affected) return is discarded. Detecting a conflict needs that count, so this plan adds new
  methods rather than changing the existing (widely-called) signature.

**Tech Stack:** .NET 10 (`net10.0-windows7.0`), `System.Data.SQLite` (confirmed — not
`Microsoft.Data.Sqlite`), NUnit 4.6.1.

**Spec:** `docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md` (R3's per-flavor
atomicity, R4's `RowVersion` conflict detection — this plan implements both for real, for one
engine). Builds on `docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md` (merged —
`IAggregateRoot`, `ConcurrencyConflictException`, the `IDatabase.SaveOne`/`SaveTransfer`/`SaveBatch`
contract, `PersistentObject.RowVersion`).

## Global Constraints

- Additive only: `Save(MyMoney)`, every existing `UpdateXxx`/`ReadXxx` override, and
  `SqliteDatabase`'s existing `ExecuteNonQuery`/`ExecuteScalar`/`ExecuteReader` overrides are
  untouched. No UI/business call site is rewired — that is Phase 3 (R1).
- This plan touches `Source/WPF/MyMoney.Data/SqliteDatabase.cs` (new members, plus a retrofit of
  `CreateOrUpdateTable`'s existing body — see Task 2 Step 5) and
  `Source/WPF/MyMoney.Data/SqlDatabase.cs` (one new `protected virtual` property, additive, no
  behavior change for `SqlServerDatabase` since nothing reads it there yet, plus
  `SqlServerDatabase.IncrementProgress`'s visibility changing from `private` to `protected` so
  `SqliteDatabase.ReadCategories` can call it). `SqlServerStoredProcDatabase`
  and real SQL Server behavior are Phase 2c's job, not this plan's.
- `SqliteDatabase.SaveOne<T>`/`SaveBatch` only handle `Category` for real in this plan. Every other
  type passed to them must fall through to `base.SaveOne`/`base.SaveBatch` (the inherited Phase 2a
  stub) — do not partially implement any other entity type "while we're in there."
- Every task must leave `dotnet build Source/WPF/MyMoney.sln` at 0 errors and
  `dotnet test Source/WPF/MyMoney.sln -m:1` green (relative to the pre-existing baseline — the 7
  SQL-Server-env-gated `MyMoney.TestSupport` failures and the 1 `ScenarioTest` FlaUI flake are
  expected and unrelated; verify no *new* failures, not a fully clean run).

---

### Task 1: WAL mode + `busy_timeout` on every SQLite connection

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqliteDatabase.cs:131-144` (`Connect()`)
- Test: `Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new — this task only changes connection-open behavior. Task 2 depends on the
  connection already being WAL-mode by the time its new transaction-scoped methods run, but doesn't
  call anything new from this task directly.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs` (it's a concrete, non-abstract
`[TestFixture]` inheriting `DatabaseContractTests` — new `[Test]` methods go directly on it, same
pattern `MockDatabaseContractTests` used in Phase 2a):

```csharp
        [Test]
        public void Connect_EnablesWalModeAndBusyTimeout()
        {
            DataSet journalModeResult = this.Database.QueryDataSet("PRAGMA journal_mode;");
            Assert.That(journalModeResult.Tables[0].Rows[0][0].ToString().ToLowerInvariant(), Is.EqualTo("wal"));

            DataSet busyTimeoutResult = this.Database.QueryDataSet("PRAGMA busy_timeout;");
            Assert.That(Convert.ToInt32(busyTimeoutResult.Tables[0].Rows[0][0]), Is.EqualTo(5000));
        }
```

Add `using System.Data;` to the top of the file (needed for `DataSet`) — the file's current imports
are exactly `using System;`, `using System.IO;`, `using NUnit.Framework;`, `using Walkabout.Data;`
(confirmed by reading it), so `System` (needed for nothing new in this task, already there for
existing code) and `Walkabout.Data`/`NUnit.Framework` are already covered; only `System.Data` is
missing.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~Connect_EnablesWalModeAndBusyTimeout"`
Expected: FAIL — `PRAGMA journal_mode;` on a freshly-created SQLite file defaults to `delete`
(SQLite's own default), not `wal`, and `PRAGMA busy_timeout;` defaults to `0`.

- [ ] **Step 3: Add the pragmas**

In `Source/WPF/MyMoney.Data/SqliteDatabase.cs`, change:

```csharp
        public override DbConnection Connect()
        {
            if (this.sqliteConnection == null || this.sqliteConnection.State != ConnectionState.Open)
            {
                string constr = this.GetConnectionString(true);
                this.sqliteConnection = new SQLiteConnection(constr);
                this.sqliteConnection.Open();
                using (var pragmaCommand = new SQLiteCommand("PRAGMA foreign_keys = ON;", this.sqliteConnection))
                {
                    pragmaCommand.ExecuteNonQuery();
                }
            }
            return this.sqliteConnection;
        }
```

to:

```csharp
        public override DbConnection Connect()
        {
            if (this.sqliteConnection == null || this.sqliteConnection.State != ConnectionState.Open)
            {
                string constr = this.GetConnectionString(true);
                this.sqliteConnection = new SQLiteConnection(constr);
                this.sqliteConnection.Open();
                using (var pragmaCommand = new SQLiteCommand("PRAGMA foreign_keys = ON;", this.sqliteConnection))
                {
                    pragmaCommand.ExecuteNonQuery();
                }
                // WAL mode: readers never block writers, writers never block readers - the
                // opposite of the default rollback-journal mode, which serializes all access.
                // busy_timeout: a second writer retries for up to this many milliseconds instead
                // of failing immediately with SQLITE_BUSY. 5000ms is a starting default (not
                // spec-mandated - a same-machine local agent contending with an interactive user
                // should resolve well within this window; revisit if real contention testing
                // shows it's too short or needlessly long). See
                // docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R3.
                using (var walCommand = new SQLiteCommand("PRAGMA journal_mode=WAL;", this.sqliteConnection))
                {
                    walCommand.ExecuteNonQuery();
                }
                using (var busyTimeoutCommand = new SQLiteCommand("PRAGMA busy_timeout=5000;", this.sqliteConnection))
                {
                    busyTimeoutCommand.ExecuteNonQuery();
                }
            }
            return this.sqliteConnection;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~Connect_EnablesWalModeAndBusyTimeout"`
Expected: PASS.

- [ ] **Step 5: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect the same pre-existing failure counts as
master's baseline (7 `MyMoney.TestSupport` SQL-Server-env-gated, 1 `ScenarioTest` FlaUI flake), no
new failures. WAL mode changes the on-disk file format slightly (a `-wal`/`-shm` sidecar file
appears next to the `.mmdb` file while open) — confirm no existing test asserts on the database
file's exact contents or file count in a way this breaks (none should, per how
`SqliteDatabaseContractTests`'s `TearDown` deletes by path, but verify).

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqliteDatabase.cs Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs
git commit -m "Enable WAL mode and busy_timeout on every SQLite connection"
```

---

### Task 2: Real `SaveOne`/`SaveBatch` for `Category` on `SqliteDatabase`

**Files:**
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs:150-152` (new `VersionColumnName` property;
  `IncrementProgress` visibility changed from `private` to `protected` so `SqliteDatabase.
  ReadCategories` can call it)
- Modify: `Source/WPF/MyMoney.Data/SqliteDatabase.cs` (new field, new `SaveOne`/`SaveBatch`
  overrides, new private helpers, new `ReadCategories` override, and a retrofit of
  `CreateOrUpdateTable`'s existing body to add the `Version` column onto pre-existing tables that
  predate Phase 1 Task 3 — see Step 5)
- Test: `Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs`
- Test: `Source/WPF/UnitTests/SqlMappingTests.cs` (regression coverage for the `CreateOrUpdateTable`
  retrofit)

**Interfaces:**
- Consumes: `IAggregateRoot`, `ConcurrencyConflictException` (Phase 2a); `PersistentObject.RowVersion`
  (Phase 1); `PersistentObject.IsInserted`/`IsChanged`/`IsDeleted`/`OnUpdated()` (existing,
  `Money.cs:479-544`); `PersistentContainer.RemoveChild(PersistentObject, bool)` (existing, abstract
  on `PersistentContainer`, overridden per container type — `Categories`' own override handles the
  physical removal).
- Produces: `SqlServerDatabase.VersionColumnName` (`protected virtual string`, default `"RowVersion"`)
  — Phase 2c can override this identically to how `SqliteDatabase` does here, for SQL Server's native
  `ROWVERSION` column, when it's built. A follow-up plan extending Phase 2b to the remaining entity
  types reuses `SqliteDatabase`'s new `ExecuteNonQueryInTransaction`/`ExecuteScalarInTransaction`
  helpers and the same `SaveBatch` dispatch-by-type structure this task establishes for `Category`.

- [ ] **Step 1: Write the failing tests**

Add to `Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs`:

```csharp
        private static MyMoney BuildOneCategoryMoney(out Category category)
        {
            MyMoney money = new MyMoney();
            category = money.Categories.GetOrCreateCategory("Groceries", CategoryType.Expense);
            return money;
        }

        [Test]
        public void SaveOne_NewCategory_PersistsAndSetsRowVersionToOne()
        {
            BuildOneCategoryMoney(out Category category);

            this.Database.SaveOne(category);

            Assert.That(category.RowVersion, Is.EqualTo(1));
            Assert.That(category.IsInserted, Is.False);
            Assert.That(category.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Category found = reloaded.Categories.FindCategory("Groceries");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateAfterReload_IncrementsRowVersion()
        {
            BuildOneCategoryMoney(out Category category);
            this.Database.SaveOne(category);

            MyMoney reloaded = this.Database.Load(null);
            Category found = reloaded.Categories.FindCategory("Groceries");
            found.Description = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Category foundAgain = reloadedAgain.Categories.FindCategory("Groceries");
            Assert.That(foundAgain.Description, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleRowVersion_ThrowsConcurrencyConflictException()
        {
            BuildOneCategoryMoney(out Category category);
            this.Database.SaveOne(category);

            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Category categoryA = readerA.Categories.FindCategory("Groceries");
            categoryA.Description = "From A";
            this.Database.SaveOne(categoryA);

            Category categoryB = readerB.Categories.FindCategory("Groceries");
            categoryB.Description = "From B";
            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(categoryB));
            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Categories.FindCategory("Groceries").Description, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveOne_Delete_RemovesRowFromDatabaseAndContainer()
        {
            BuildOneCategoryMoney(out Category category);
            this.Database.SaveOne(category);

            MyMoney reloaded = this.Database.Load(null);
            Category toDelete = reloaded.Categories.FindCategory("Groceries");
            reloaded.Categories.RemoveCategory(toDelete);
            Assert.That(toDelete.IsDeleted, Is.True);

            this.Database.SaveOne(toDelete);

            Assert.That(reloaded.Categories.FindCategory("Groceries"), Is.Null);

            MyMoney reloadedAgain = this.Database.Load(null);
            Assert.That(reloadedAgain.Categories.FindCategory("Groceries"), Is.Null);
        }

        [Test]
        public void SaveBatch_OneStaleRootAmongMany_RollsBackTransactionAndPreservesInMemoryState()
        {
            MyMoney money = new MyMoney();
            Category a = money.Categories.GetOrCreateCategory("A", CategoryType.Expense);
            Category b = money.Categories.GetOrCreateCategory("B", CategoryType.Expense);
            this.Database.SaveBatch(new PersistentObject[] { a, b });

            MyMoney reader = this.Database.Load(null);
            Category staleA = reader.Categories.FindCategory("A");
            Category freshB = reader.Categories.FindCategory("B");

            // Someone else updates A first, so staleA's RowVersion (1) is now behind.
            MyMoney otherWriter = this.Database.Load(null);
            Category otherA = otherWriter.Categories.FindCategory("A");
            otherA.Description = "Changed elsewhere";
            this.Database.SaveOne(otherA);

            staleA.Description = "Attempted A";
            freshB.Description = "Attempted B";
            long freshBOriginalRowVersion = freshB.RowVersion;

            // freshB is listed FIRST deliberately: SaveBatch's loop processes roots in order, so
            // freshB's own UPDATE executes and "succeeds" (within the still-open transaction) on
            // iteration 1, and staleA's conflict is only detected and thrown on iteration 2 - by
            // which point freshB's SQL has already run. This is what actually exercises the
            // postCommitActions deferral: if B were listed second (i.e. never reached because A's
            // conflict throws first), this assertion would pass trivially under ANY
            // implementation, buggy or not, since B's code path would never execute at all.
            Assert.Throws<ConcurrencyConflictException>(
                () => this.Database.SaveBatch(new PersistentObject[] { freshB, staleA }));

            // B must NOT have been committed, even though only A conflicted - proves the real
            // SQLiteTransaction actually rolled back both writes, not just A's, including the one
            // that already "succeeded" earlier in the same loop.
            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Categories.FindCategory("B").Description, Is.Not.EqualTo("Attempted B"));

            // B's in-memory state must ALSO be unchanged. This is the regression this test exists
            // for: an earlier draft of SaveOneCategory applied RowVersion/OnUpdated immediately
            // per-root inside the loop, so B's in-memory RowVersion got bumped and its dirty flag
            // cleared as soon as its own UPDATE ran - even though the transaction that "committed"
            // it was rolled back moments later by A's conflict on the very next iteration -
            // postCommitActions exists specifically to prevent this.
            Assert.That(freshB.RowVersion, Is.EqualTo(freshBOriginalRowVersion));
            Assert.That(freshB.IsChanged, Is.True);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqliteDatabaseContractTests"`
Expected: FAIL — `SaveOne` currently throws `NotImplementedException` (the inherited Phase 2a stub)
for every call, since `SqliteDatabase` doesn't override it yet.

- [ ] **Step 3: Add `VersionColumnName` to the base class**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, change:

```csharp
        public virtual bool SupportsParameterizedUpdate { get { return false; } }

        public virtual bool Exists
```

to:

```csharp
        public virtual bool SupportsParameterizedUpdate { get { return false; } }

        /// <summary>
        /// The per-flavor name of the optimistic-concurrency version column Phase 1 added to
        /// every table: SQL Server's native ROWVERSION is named "RowVersion"; SQLite's
        /// application-maintained INTEGER counter is named "Version" (SqliteDatabase overrides
        /// this). See docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R4.
        /// </summary>
        protected virtual string VersionColumnName { get { return "RowVersion"; } }

        public virtual bool Exists
```

- [ ] **Step 4: Add the SQLite transaction field and override `VersionColumnName`**

In `Source/WPF/MyMoney.Data/SqliteDatabase.cs`, change:

```csharp
        private SQLiteConnection sqliteConnection;
```

to:

```csharp
        private SQLiteConnection sqliteConnection;

        // Distinct from SqlServerDatabase's own `transaction` field (SqlDatabase.cs) - that one
        // is private to the base class and SQL-Server-typed, so SQLite needs its own. Only
        // SaveOne/SaveTransfer/SaveBatch's new code path uses this; Save(MyMoney) and every
        // existing UpdateXxx/ReadXxx override are untouched and keep relying on
        // System.Data.SQLite's connection-level auto-enlist behavior, same as today.
        private SQLiteTransaction sqliteTransaction;

        protected override string VersionColumnName { get { return "Version"; } }
```

- [ ] **Step 5: Retrofit the `Version` column in `CreateOrUpdateTable`, then add the
  transaction-scoped SQL-execution helpers and the rest of the new members**

First, a schema-retrofit fix inside `CreateOrUpdateTable`'s existing "table already exists" `else`
branch: `Version`/`RowVersion` is deliberately excluded from `mapping.Columns` (see the column-drop
guard a few lines below, which skips it for the same reason), so the existing `newColumns` loop can
never add it to a database file that predates Phase 1 Task 3 (which added the column only to
`GetCreateTableScript`'s freshly-`CREATE`d tables). Without this, the new `ReadCategories` override
added later in this step (its `SELECT` unconditionally includes the version column) would fail with
"no such column: Version" against any such file. In `Source/WPF/MyMoney.Data/SqliteDatabase.cs`,
inside `CreateOrUpdateTable`'s `else` branch, insert the following between the `if (newColumns.Count
> 0) { ... }` block and the `if (renames.Count > 0) { ... }` block:

```csharp
                        if (actual.FindColumn(this.VersionColumnName) == null)
                        {
                            // Retrofit the optimistic-concurrency version column onto a database
                            // file that predates persistence-concurrency Phase 1 Task 3 (which
                            // added it only to GetCreateTableScript's freshly-CREATEd tables).
                            // The newColumns loop above can never catch this itself: Version/
                            // RowVersion is deliberately excluded from mapping.Columns (Task 4's
                            // guard a few lines below, so it's also never treated as a column to
                            // drop), since it's engine-injected rather than reflected off the
                            // domain model. Without this, an old on-disk file would never get the
                            // column added - harmless until persistence-concurrency Phase 2b's
                            // SaveOne/ReadCategories (SqliteDatabase.ReadCategories) started
                            // unconditionally selecting it, at which point every load of such a
                            // file would fail with "no such column: Version". DEFAULT 1 mirrors
                            // GetCreateTableScript's own literal for freshly created tables, and
                            // SQLite allows a NOT NULL ADD COLUMN precisely because it has one.
                            string addVersionColumn = string.Format(
                                "ALTER TABLE [{0}] ADD COLUMN [{1}] INTEGER NOT NULL DEFAULT 1;",
                                mapping.TableName, this.VersionColumnName);
                            log.AppendLine(addVersionColumn);
                            this.ExecuteScalar(addVersionColumn);
                        }
```

Second, insert before the final closing braces (after the last method in the file,
`CreateOrUpdateTable`'s closing `}` — the file currently ends with):

```csharp
                if (log.Length > 0)
                {
                    this.AppendLog(log.ToString());
                }
            }
        }

    }
}
```

change the above to (this is the state as of this fix wave — see "What's next" for what changed
after this task's own initial review; in particular there is no `sqliteTransaction` field, `SaveBatch`
uses a local `using`-scoped `SQLiteTransaction` instead):

```csharp
                if (log.Length > 0)
                {
                    this.AppendLog(log.ToString());
                }
            }
        }

        /// <summary>
        /// Runs a parameterized non-query inside the given explicit transaction and returns the
        /// affected-row count - unlike the existing ExecuteNonQuery overrides (which never take a
        /// transaction parameter and discard the row count), this is what SaveOne/SaveBatch need
        /// to detect a RowVersion conflict (zero rows affected on an UPDATE/DELETE whose WHERE
        /// clause checked the version). Private: only this class's new SaveOne/SaveBatch path
        /// uses it.
        /// </summary>
        private int ExecuteNonQueryInTransaction(SQLiteTransaction transaction, string cmd, params (string Name, object Value)[] parameters)
        {
            this.AppendLog(cmd);
            using (SQLiteCommand command = new SQLiteCommand(cmd, this.sqliteConnection, transaction))
            {
                foreach (var (name, value) in parameters)
                {
                    DbParameter p = command.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value ?? DBNull.Value;
                    command.Parameters.Add(p);
                }
                return command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Same transaction-explicit pattern as ExecuteNonQueryInTransaction, for the single
        /// follow-up SELECT SaveOneCategory issues after a detected conflict, to report the
        /// store's actual current RowVersion in ConcurrencyConflictException rather than a
        /// sentinel. Only reached on the rare conflict path.
        /// </summary>
        private object ExecuteScalarInTransaction(SQLiteTransaction transaction, string cmd, params (string Name, object Value)[] parameters)
        {
            using (SQLiteCommand command = new SQLiteCommand(cmd, this.sqliteConnection, transaction))
            {
                foreach (var (name, value) in parameters)
                {
                    DbParameter p = command.CreateParameter();
                    p.ParameterName = name;
                    p.Value = value ?? DBNull.Value;
                    command.Parameters.Add(p);
                }
                return command.ExecuteScalar();
            }
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

            // Only Category has a real implementation in this phase. If the batch contains
            // anything else, delegate the WHOLE call to the inherited Phase 2a stub rather than
            // partially committing some roots and throwing on others.
            foreach (PersistentObject root in list)
            {
                if (!(root is Category))
                {
                    base.SaveBatch(list);
                    return;
                }
            }

            this.Connect();
            using (SQLiteTransaction transaction = this.sqliteConnection.BeginTransaction())
            {
                // Every in-memory side effect (RowVersion assignment, OnUpdated, RemoveChild) is
                // deferred into this list and only run AFTER Commit() succeeds. If any later root in
                // this batch conflicts, the catch below rolls back EVERY write this transaction made -
                // including ones whose own SQL statement already "succeeded" earlier in the loop. If
                // this method mutated in-memory state immediately per-root instead, an earlier root's
                // RowVersion/dirty-flag would end up claiming a commit that the rollback undid,
                // violating R3 ("a failure leaves in-memory dirty-tracking state exactly matching
                // what's actually in the database"). See SaveBatch_OneStaleRootAmongMany_
                // RollsBackTransactionAndPreservesInMemoryState below, which fails without this.
                List<Action> postCommitActions = new List<Action>();
                try
                {
                    foreach (PersistentObject root in list)
                    {
                        this.SaveOneCategory((Category)root, transaction, postCommitActions);
                    }
                    transaction.Commit();
                }
                catch (Exception originalException)
                {
                    // A failing Rollback() (plausible under WAL/busy_timeout - Commit() can fail
                    // with SQLITE_BUSY, and rollback of an already-troubled transaction can fail
                    // too) must never silently replace/hide the original exception (e.g. a
                    // ConcurrencyConflictException the caller needs to see), and must never leave
                    // the caller thinking the rollback happened when it didn't.
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception rollbackException)
                    {
                        throw new Exception("Rollback failed after: " + originalException.Message, rollbackException);
                    }
                    throw;
                }

                // Only reached after Commit() succeeded - the catch above always rethrows before
                // falling through here, so postCommitActions never runs against a rolled-back
                // transaction. Runs inside the using block, before the transaction is disposed.
                foreach (Action action in postCommitActions)
                {
                    action();
                }
            }
        }

        /// <summary>
        /// Writes one Category row with a RowVersion-checked WHERE clause on UPDATE/DELETE, and
        /// queues (but does not yet apply) the matching in-memory side effect - see SaveBatch's
        /// comment on postCommitActions for why applying it immediately would be wrong. SQL
        /// mirrors UpdateCategories' existing parameterized branch (SqlDatabase.cs) exactly, plus
        /// the version check/bump - deliberately duplicated rather than shared, since
        /// UpdateCategories serves the whole-graph Save(MyMoney) path (additive-only constraint:
        /// not touched here) and operates on a whole Categories collection, not one root.
        /// </summary>
        private void SaveOneCategory(Category c, SQLiteTransaction transaction, List<Action> postCommitActions)
        {
            long callerRowVersion = c.RowVersion;

            if (c.IsInserted)
            {
                this.ExecuteNonQueryInTransaction(transaction,
                    "INSERT INTO Categories (Id,Name,Description,Type,ParentId,Budget,Frequency,Balance,Color,TaxRefNum) " +
                    "VALUES (@Id,@Name,@Description,@Type,@ParentId,@Budget,@Frequency,@Balance,@Color,@TaxRefNum);",
                    ("@Id", c.Id), ("@Name", c.Name), ("@Description", c.Description), ("@Type", (int)c.Type),
                    ("@ParentId", c.ParentCategory != null ? (object)c.ParentCategory.Id : DBNull.Value), ("@Budget", c.Budget),
                    ("@Frequency", (int)c.Frequency), ("@Balance", c.Balance), ("@Color", c.Color),
                    ("@TaxRefNum", c.TaxRefNum));
                postCommitActions.Add(() =>
                {
                    c.RowVersion = 1; // matches the schema's "Version INTEGER NOT NULL DEFAULT 1"
                    c.OnUpdated();
                });
                return;
            }

            if (c.IsChanged)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "UPDATE Categories SET Name=@Name,Description=@Description,Type=@Type,ParentId=@ParentId,Budget=@Budget," +
                    "Frequency=@Frequency,Balance=@Balance,Color=@Color,TaxRefNum=@TaxRefNum," +
                    this.VersionColumnName + "=" + this.VersionColumnName + "+1 " +
                    "WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Name", c.Name), ("@Description", c.Description), ("@Type", (int)c.Type),
                    ("@ParentId", c.ParentCategory != null ? (object)c.ParentCategory.Id : DBNull.Value), ("@Budget", c.Budget),
                    ("@Frequency", (int)c.Frequency), ("@Balance", c.Balance), ("@Color", c.Color),
                    ("@TaxRefNum", c.TaxRefNum), ("@Id", c.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(c, "Categories", c.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    c.RowVersion = callerRowVersion + 1;
                    c.OnUpdated();
                });
                return;
            }

            if (c.IsDeleted)
            {
                int rowsAffected = this.ExecuteNonQueryInTransaction(transaction,
                    "DELETE FROM Categories WHERE Id=@Id AND " + this.VersionColumnName + "=@ExpectedVersion;",
                    ("@Id", c.Id), ("@ExpectedVersion", callerRowVersion));
                if (rowsAffected == 0)
                {
                    this.ThrowConflict(c, "Categories", c.Id, transaction, callerRowVersion);
                }
                postCommitActions.Add(() =>
                {
                    c.OnUpdated();
                    c.Parent.RemoveChild(c, true);
                });
                return;
            }

            // No pending change - nothing to do.
        }

        /// <summary>
        /// A zero-rows-affected UPDATE/DELETE means a conflict, but not what the store's current
        /// version actually is - one more SELECT (inside the same transaction, so it sees a
        /// consistent view) gets an accurate diagnostic instead of a sentinel. -1 means the row no
        /// longer exists at all (e.g. already deleted by someone else).
        /// </summary>
        private void ThrowConflict(PersistentObject root, string tableName, int id, SQLiteTransaction transaction, long callerRowVersion)
        {
            object result = this.ExecuteScalarInTransaction(transaction,
                "SELECT " + this.VersionColumnName + " FROM " + tableName + " WHERE Id=@Id;", ("@Id", id));
            long storedRowVersion = (result == null || result == DBNull.Value) ? -1 : Convert.ToInt64(result);
            throw new ConcurrencyConflictException(root, storedRowVersion, callerRowVersion);
        }

        /// <summary>
        /// Overrides the inherited (shared-with-SqlServerDatabase) ReadCategories to also read
        /// back the Version column - deliberately a full override, not a shared generic change,
        /// so SQL Server's read path (which will need ROWVERSION's binary(8)-to-long conversion,
        /// not a plain integer read) is untouched until Phase 2c actually needs it.
        /// </summary>
        public override void ReadCategories(Categories categories, MyMoney money)
        {
            categories.Clear();
            IDataReader reader = this.ExecuteReader("SELECT [Id],[Name],[Description],[Type],[ParentId],[Budget],[Frequency],[Balance],[Color],[TaxRefNum],[" + this.VersionColumnName + "] FROM Categories");
            categories.BeginUpdate(false);
            while (reader.Read())
            {
                this.IncrementProgress("Categories");
                int id = reader.GetInt32(0);
                Category c = new Category(categories);
                c.Id = id;
                categories.AddCategory(c);
                c.Name = ReadDbString(reader, 1);
                c.Description = ReadDbString(reader, 2);
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
                c.RowVersion = reader.GetInt64(10);
                c.OnUpdated();
                // one more fix up that will need to be saved (so must come after c.OnUpdated).
                if (c.Type == CategoryType.Reserved)
                {
                    c.Type = CategoryType.Expense;
                }
            }
            categories.EndUpdate();
            categories.FireChangeEvent(categories, categories, null, ChangeType.Reloaded);
            reader.Close();
        }

    }
}
```

(This second block replaces everything from `if (log.Length > 0)` through the file's final `}`/`}` —
the new members go between the end of `CreateOrUpdateTable` and the class/namespace closing braces.)

No new `using` statements are needed beyond what Step 4 already covers: `SqliteDatabase.cs`'s
existing imports already cover everything the new code uses — `System` (for
`Action`/`DBNull`/`Convert`/`Exception`), `System.Collections.Generic` (for `List<T>`),
`System.Data.Common` (for `DbParameter`), `System.Data.SQLite` (for
`SQLiteTransaction`/`SQLiteCommand`) are all already present at the top of the file. `Category`,
`Categories`, `ChangeType`, `PersistentObject`, `IAggregateRoot`, `ConcurrencyConflictException` need
no `using` either — they're in `Walkabout.Data`, the same namespace `SqliteDatabase` itself is
declared in.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~SqliteDatabaseContractTests"`
Expected: PASS — all 5 new tests plus every pre-existing `DatabaseContractTests` test (still
exercising the untouched `Save(MyMoney)`/`Load` path for every type other than the new `SaveOne`
calls).

- [ ] **Step 7: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect the same pre-existing failure counts as
master's baseline, no new failures.

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/MyMoney.Data/SqliteDatabase.cs Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs
git commit -m "Implement real SaveOne/SaveBatch for Category on SqliteDatabase with RowVersion conflict detection"
```

---

## What's next

This plan deliberately stops at one entity type — it proves SQLite's real transaction/conflict-check
machinery works end-to-end (WAL mode, an explicit `SQLiteTransaction`, a row-count-aware execution
path, a real `RowVersion` round-trip through `Load()`) before paying the cost of repeating the same
shape for the other ~10 aggregate roots. The follow-up plan (not yet written) should:

- Extend `SaveBatch`'s dispatch (currently `if (!(root is Category))` falls through to the base
  stub) to the remaining simple entities (`Account`, `OnlineAccount`, `Currency`, `Payee`, `Alias`,
  `Security`, `StockSplit`, `LoanPayment`) — each following the exact pattern
  `SaveOneCategory`/`ReadCategories` establish here, and per the subagent-driven-development skill's
  "batch small same-shape work" guidance, these are good candidates for one batched implementer
  dispatch rather than one per entity, once this plan's pattern is confirmed solid in review.
- Add `SaveOne<Transaction>` (compound: transaction row + owned `Splits` + `Investment`, matching
  `UpdateTransactions`' existing inline shape at `SqlDatabase.cs:3197-3355`) and
  `SaveOne<RentBuilding>` (compound: building row + owned `RentUnit`s, matching `UpdateBuildings`).
  Both need their own task — genuinely more complex than the simple-entity batch.
- Add `SaveTransfer` (two `Transaction`s co-committing in one `SQLiteTransaction`) — needs
  `SaveOne<Transaction>` first.
- Promote the now-proven `SaveOne`/conflict-detection tests into the shared `DatabaseContractTests`
  base class (per Phase 2a's own "What's next" note) so `MockDatabaseContractTests` and
  `SqliteDatabaseContractTests` both inherit and re-run them, instead of each having its own
  near-duplicate copy.
- Add a genuine multi-process/multi-connection SQLite contention test (two `SqliteDatabase`
  instances pointed at the same file, overlapping `SaveOne` calls, asserting both eventually succeed
  via `busy_timeout` retry rather than an immediate `SQLITE_BUSY` failure) — `MockDatabase` has no
  equivalent scenario, so this is SQLite-only coverage the design spec's testing strategy calls for.
- Decide whether `SaveOneCategory`'s pattern of a follow-up `SELECT` on conflict (for an accurate
  `StoredRowVersion` in the exception) is worth generalizing into a small per-entity-agnostic helper
  once 2-3 more entities exist to generalize from, or whether keeping it duplicated per entity (as
  `UpdateXxx` already is) matches this codebase's existing style better.
- **Latent risk found during this plan's own review (Task 2), not yet fixed — pre-existing code,
  untouched by this plan:** `CreateOrUpdateTable`'s `newTable`-rebuild branch (`SqliteDatabase.cs:711-789`)
  copies data into a rebuilt table via `INSERT INTO new_table SELECT <cols> FROM old_table`, where
  `<cols>` comes from `mapping.Columns` — and `Version`/`RowVersion` is deliberately excluded from
  `mapping.Columns` (same exclusion Task 2's ALTER-path retrofit relies on). If a future schema
  change ever forces `newTable = true` on a table that has accumulated real per-row `Version`
  history (once more entity types than just `Category` have real `SaveOne` traffic), the rebuild
  would silently reset every row's version to `DEFAULT 1`, discarding conflict-detection history —
  a real data-integrity gap once it's load-bearing, though not exercised by anything in Phase 2b.
  The follow-up plan should add an explicit carry-forward of the actual `Version`/`RowVersion`
  values in that rebuild path (e.g. include the version column by name in the `INSERT ... SELECT`,
  rather than relying on `mapping.Columns`), plus a regression test forcing a `newTable` rebuild on
  a table with non-default version values.
- **Exception-contract divergence from `MockDatabase`**: `SqliteDatabase.SaveBatch` disagrees with
  Phase 2a's `MockDatabase.SaveBatch` on what exception type fires for: a null root in the batch, a
  non-`IAggregateRoot` root, and a duplicate-inserted root (raw `SQLiteException` for the UNIQUE
  constraint, not a typed exception). Most of these are correct-by-design given a real engine's
  architecture (e.g. a duplicate *changed* root correctly conflicts via the `WHERE` clause with no
  special-casing, unlike `MockDatabase`'s blanket pre-check), but they're still a real API-surface
  inconsistency between the two `IDatabase` implementations. Before promoting the shared tests into
  `DatabaseContractTests`'s base class (already planned above), explicitly decide which side is
  normative for each divergence and reconcile.
- **INSERT-path Id collisions bypass `ConcurrencyConflictException` entirely**: two writers racing
  to insert a `Category` with the same client-side-allocated `Id` will hit a raw `SQLiteException`
  (UNIQUE constraint violation) rather than `ConcurrencyConflictException` — the transaction still
  rolls back correctly (no corruption), but a caller's `catch (ConcurrencyConflictException)` handler
  won't fire for this specific race. Not fixed here — `Id` allocation across concurrent writers is a
  separate, larger, not-yet-designed problem (out of this plan's scope).
- **SQL Server will need the identical `Version`-column retrofit** `CreateOrUpdateTable` got here,
  once Phase 2c wires up `SqlServerStoredProcDatabase`'s equivalent read path — harmless today since
  `SqlServerDatabase.ReadCategories` (the shared base method) doesn't select the column yet, but
  Phase 2c will hit the same bug this fix wave just closed if it's forgotten.

### Post-review fix wave (final whole-branch review)

The final whole-branch review of this plan (Tasks 1 and 2 both already merged individually) found 1
Critical, 8 Important, and 9 Minor issues. A curated subset was fixed in commits `c983fec..53e1ad6`
(on top of `1afe4d3`, Task 2's own last commit), already landed on this branch before Phase 2b was
considered complete:

- **Critical:** `Backup()` now runs `PRAGMA wal_checkpoint(TRUNCATE);` before copying the file (WAL
  mode, enabled by Task 1, meant a raw `File.Copy` could silently produce a backup missing the most
  recent committed transactions); `Delete()` and the static `Restore()` now also clean up the
  `-wal`/`-shm` sidecar files (at the deleted path, and at the restore target before overwriting it,
  respectively).
- **Important:** `SaveBatch`'s `SQLiteTransaction` is now a `using`-scoped local variable (properly
  disposed on every path) instead of an undisposed instance field; a failing `Rollback()` no longer
  masks the original exception (it's wrapped with the rollback failure as an inner exception instead
  of silently replacing it) or leaves the connection in an ambiguous state. The now-unused
  `sqliteTransaction` field was removed entirely.
- **Important:** Added a regression test for the `CreateOrUpdateTable` `Version`-column retrofit
  (`Source/WPF/UnitTests/SqlMappingTests.cs`), and fixed a vacuous assertion in
  `SaveOne_Delete_RemovesRowFromDatabaseAndContainer` (`SqliteDatabaseContractTests.cs`) that was
  checking the wrong `Categories` lookup (name-based `FindCategory`, cleared unconditionally by
  `RemoveCategory` itself) instead of the one that actually depends on `SaveOneCategory`'s deferred
  `RemoveChild(c, true)` postCommit action (id-based `FindCategoryById`).
- **Minor:** `ReadCategories` now interpolates `this.VersionColumnName` instead of hardcoding the
  literal `"Version"`, matching every other new statement in this plan's diff.
- Deliberately deferred (see the fix-wave task's own "explicitly out of scope" list, not repeated
  here): unbounded `AppendLog` growth, the misleading `NotImplementedException` message for
  non-`Category` roots, `PRAGMA journal_mode=WAL`'s discarded return value, a throwing postCommit
  action leaving partial in-memory state, the single-connection nature of the "independent reader"
  tests, several untested code paths, and duplicated magic literals (`5000`, `"wal"`).

Phase 2c (SQL Server stored procs, not yet planned) follows the same shape again for the third
engine, reusing `VersionColumnName`'s override point and needing its own transaction/row-count
machinery analogous to what this plan built for SQLite (SQL Server's is architecturally simpler,
since `SqlServerStoredProcDatabase`'s per-call connection-opening — issue #27 — needs fixing
regardless, at which point wiring in an explicit transaction is the same piece of work).
