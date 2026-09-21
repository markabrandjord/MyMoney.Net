# Data-layer architecture brainstorm — the rebuild's first foundation slice

**Status: brainstorm output, not a spec.** This is the design-space exploration that issue #5
was explicitly written as the input to. It is deliberately *not* polished into spec form —
it exists to drive an owner review, after which the agreed path becomes a real design spec and
then an implementation plan, per the brainstorming skill's normal process.

**Date:** 2026-09-20 · **Branch:** `rebuild/data-layer-foundation` · **Panel:** the usual 7 roles.

**Revision 2 (2026-09-20, same day).** The owner read round 1 and gave follow-up guidance. This
document is *extended*, not replaced — round 1's structure, candidates, trade-offs and open items
all survive except where today's guidance genuinely changes them. What the owner said, and where
each piece landed:

| Owner guidance | Where it is answered |
|---|---|
| "At this point the system is a test system. We can nuke the DB and re-pave it… if all that work is done in sprocs." | New §1.5 (S-0, S-1); adversarial pushback in §6.7 |
| "When someone creates a new DB, the process is more or less the same on SQLite and SQL Server." | New §1.5's six-step process table, engine-by-engine |
| "The create-DB sproc creates the tables (named columns, indexes, foreign keys, etc.)." | §1.5's step table + ledger design |
| "**The sprocs need to support both the normal upgrade process and the nuke-and-pave process.**" | §1.5 S-1 — **one** mechanism, two entry points; this is the revision's central constraint |
| "Sprocs with parameterized input to implement CRUD functions as transactions, because they are safer." | New §1.6 (made explicit, was implicit) |
| "Sprocs to deal with tabular inputs where multiple records need to be input as a transaction." | New §1.6 (TVP/`json_each` batch pattern, carried forward explicitly) |
| "Use as much of the db functionality in SQLite as possible, so the IDatabase layer makes the two engines appear similar." | New §1.7 — a new design principle, worked out concretely, **including where it does not hold** |
| "Test routines for the Business layers may need a mock database for speed… maybe SQLite without a disk file. The database experts can revisit." | §2.4 — sub-decision **T-1 now has a recommendation**, not a deferral |
| "Eventually when we turn on a production db, we cannot just nuke and pave. We have to have a real upgrade process for the db and the app." | New §1.8 — explicitly deferred, with the three places today's design must not foreclose it; new open item #8 |

Round 1's §4 open-items list is re-scored at the end of §4: today's guidance **resolves one**,
**reframes two**, and **adds one**.

---

## 0. What this design is built on top of (verified, not assumed)

Everything below was checked against the working tree and the merged history, not recalled:

| Fact | Where verified |
|---|---|
| `MyMoney.Business` has **zero** WPF-family assembly references, enforced by a live test | `Source/WPF/UnitTests/LayerBoundaryTests.cs` — 3 tests: Business WPF-free, Data WPF-free, and the two are genuinely distinct assemblies |
| `IDatabase` (with `SaveOne<T>`/`SaveTransfer`/`SaveBatch`), `IAggregateRoot` (`long Id`), `DbFlavor` all live in **`MyMoney.Business`**, and `MyMoney.Data` references `MyMoney.Business` — i.e. the port/adapter direction is already correct | `Source/WPF/MyMoney.Business/IDatabase.cs`, `MyMoney.Data.csproj` |
| All five engines (`SqliteDatabase` 2,720 ln, `SqlDatabase`/`SqlServerStoredProcDatabase` 3,991 + 3,075 ln, `SqlCeDatabase`, `XmlStore`, `CsvStore`) are in **one** `MyMoney.Data.dll`, ~12.9k lines | `Source/WPF/MyMoney.Data/` |
| One shared `DatabaseContractTests` suite (~1,600 ln across 4 partial files) runs against Mock, SQLite and SQL Server | `Source/WPF/MyMoney.TestSupport/DatabaseContractTests*.cs` |
| `MyMoney.TestSupport` is **simultaneously** a library (`MockDatabase`, `BasicsFixtureBuilder`, `SqlServerTestDatabase`) and a test project (`Microsoft.NET.Test.Sdk` + `[TestFixture]` classes) | `MyMoney.TestSupport.csproj` |
| No production project references `MyMoney.TestSupport` today — the one-way dependency **already holds, but nothing enforces it** | `ProjectReference` grep across all `.csproj` |
| `UnitTests.csproj` references **`MyMoney.csproj` (the WPF UI project)** | `Source/WPF/UnitTests/UnitTests.csproj:26` |
| SQL Server tiering is real: `DatabaseRole {Admin, User, Test}`, `MyMoneyAdmin`/`MyMoneyUser`/`MyMoneyTest`, `_Test_Reset` procs deployed **only** into a `testDatabase: true` catalog | `DatabaseRegistry.cs`, `SqlServerBootstrapper.CreateCatalog` |
| **Schema creation today is *not* sproc-driven** — `CreateCatalog` calls `adminDatabase.LazyCreateTables()`, which is reflection-to-DDL C# (`GetCreateTableScript`), then replays `Migrations/*.sql` as raw batch scripts | `SqlServerBootstrapper.cs:145`, `SqlDatabase.cs` |
| SQLite already sets `journal_mode=WAL`, `busy_timeout=5000`, `foreign_keys=ON` | `SqliteDatabase.cs:167-185` |

The `LazyCreateTables` row matters a lot: the owner's constraint *"Admin creates the database, then
adds the stored procedures that themselves create tables, indexes, views"* is **not** what the code
does today. It is a genuine change, not a formalization — the formalization part is the
three-login/role model, which does exist.

### 0.1 Additional facts verified for revision 2

Checked the same way — against the working tree, not recalled — because revision 2's whole subject
is schema management and engine parity, and both were only sketched in round 1:

| Fact | Where verified | Why it matters here |
|---|---|---|
| **There is no schema-version ledger of any kind today.** `SqlServerBootstrapper` replays *every* file in `SqlScripts/Migrations/` in filename order on every fresh-catalog creation, and correctness rests entirely on each script's own hand-written guard (`IF COL_LENGTH(...) IS NULL BEGIN ALTER TABLE ... END`). Nothing records what has been applied. SQLite has no migration replay at all. | `SqlServerBootstrapper.cs:170-177`, `SqlScripts/Migrations/2026-09-17-add-version-column.sql` | This is the root cause behind issue #34, not a coincidence: a per-script hand-written guard only covers what its author remembered to guard. Nobody wrote an index guard, so indexes aren't guarded. §1.5 replaces "trust the guard" with "a ledger plus a drift check." |
| SQL Server's batch write is **one** proc call taking a table-valued parameter (`@Rows dbo.CurrencySaveBatchRow READONLY`), with `SET XACT_ABORT ON; BEGIN TRANSACTION;` and the conflict check inside the proc. | `SqlScripts/Access/*_AccessProcs.sql`, `SqlServerStoredProcDatabase.ExecuteSaveBatchProc` | The owner's "sprocs for tabular input, as one transaction" already exists on this engine. It is the shape §1.6 carries forward. |
| SQLite's batch write is a **C# `switch` loop** over roots, issuing per-root `INSERT`/`UPDATE`/`DELETE` statements inside one `BeginTransaction()`, with `PRAGMA defer_foreign_keys = ON`. | `SqliteDatabase.SaveBatch` (`:1009-1110`), `SaveOneCategory` etc. | Same *guarantees* as SQL Server (atomic, parameterized, version-checked), genuinely *different programming model*. This is precisely the asymmetry the owner's "use more SQLite functionality" guidance is aimed at. §1.7. |
| SQLite's post-commit `RowVersion` is **computed in C#** (`c.RowVersion = callerRowVersion + 1`) rather than read back from the engine, even though the `UPDATE` itself does `Version = Version + 1` server-side. | `SqliteDatabase.cs:1165-1181` | A small but real instance of "logic that could live in the engine lives in C# instead." SQLite's `RETURNING` clause would make it authoritative. §1.7. |
| The bundled SQLite engine is **3.53.4** (`sqlite` 3.53.4 via `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5). | `MyMoney.Data.csproj:14`, NuGet cache | Well past every feature §1.7 proposes using: `RETURNING` (3.35), built-in JSON incl. `json_each` (3.38), `STRICT` tables (3.37), generated columns (3.31), `UPSERT` (3.24). None of §1.7 requires a version bump or a different provider. |
| `SqliteDatabase` holds **one long-lived cached connection** (`this.sqliteConnection`), reopened only if closed. | `SqliteDatabase.Connect()` | Directly relevant to T-1: an in-memory SQLite database lives exactly as long as its connection, and this design already keeps one. The lifetime model an in-memory store needs is the one the code already has. |
| `DatabaseEntry.TestDatabase` (that exact casing, deliberately) already exists in the registry config, and already gates whether destructive `Test/*` procs are deployed into a catalog. | `DatabaseRegistry.cs:59-62`, `SqlServerBootstrapper.cs:185-194` | The "mark test databases in config so production ones are distinguishable" half of the deferred production-upgrade workflow **already exists**. §1.8 builds on it rather than inventing it. |

---

## 1. Candidate approaches for the data layer

All three share a premise the panel treats as settled, because D-35's capability floor already
decided it: **`IDatabase` gets split.** A single interface whose members throw on most
implementations is a union type wearing a contract's clothes. Everything below splits it three
ways along the *privilege* axis the owner named:

```
IMoneyStore            — everyday read/write. No DDL, no destructive bulk operations.
IMoneyStoreProvisioner — create catalog/file, deploy + run schema, upgrade, backup, delete.
IMoneyStoreTestControl — wipe to empty, seed, snapshot/restore. Test tier only.
```

Plus a fourth, non-store contract for what D-35 called *formats*:

```
IDataFormat            — round-trip the whole model to/from a stream. XML export, CSV export.
                         Lives in the business layer, NOT in a data-layer engine assembly.
```

The three candidates differ in **how that tiering is enforced in .NET**, which is the only
question with real trade-offs.

---

### Candidate A — Tier-per-assembly (**recommended**)

Each tier of each engine is its own DLL. Privilege separation is expressed as *"the code isn't
in the deployed bits"*, which is exactly the property SQL Server already gives via
`_Test_Reset` procs only being deployed into a test catalog (P9-GUARD-2).

```
MyMoney.Domain                      entities, IAggregateRoot, ConcurrencyConflictException
MyMoney.Data.Contracts              IMoneyStore, IMoneyStoreProvisioner, IMoneyQuery, DTOs
                                    (could equally stay in MyMoney.Business — see note below)

MyMoney.Data.Sqlite                 IMoneyStore + IMoneyQuery            [always shipped]
MyMoney.Data.Sqlite.Provisioning    IMoneyStoreProvisioner               [shipped; see §1.4]
MyMoney.Data.SqlServer              IMoneyStore + IMoneyQuery            [DEBUG only]
MyMoney.Data.SqlServer.Provisioning IMoneyStoreProvisioner + bootstrapper [DEBUG only]

MyMoney.TestKit.Contracts           IMoneyStoreTestControl, test-only DTOs   [never shipped]
MyMoney.Data.Sqlite.TestTier        IMoneyStoreTestControl impl              [never shipped]
MyMoney.Data.SqlServer.TestTier     IMoneyStoreTestControl impl              [never shipped]
```

**How the SQLite facade gets its teeth.** The destructive capability isn't hidden behind an
`internal` modifier or a runtime flag — the SQL that can empty the database *does not exist in
any assembly a release build contains*. `MyMoney.Data.Sqlite.dll` contains no `DELETE FROM`
without a `WHERE Id=`, no `DROP TABLE`, no `VACUUM INTO` over-the-top-of-an-existing-file. And
critically: **`IMoneyStoreTestControl` itself lives in `MyMoney.TestKit.Contracts`, which no
production assembly references** — so production code cannot even *name the type* it would need
to implement or call. A copy-pasted implementation wouldn't compile.

That is the honest equivalence claim, and §6 states precisely where it falls short of real
SQL Server role separation.

**How the SQL Server tiering maps.** One-to-one, and the .NET assembly boundary lines up with
the login boundary rather than duplicating it:

| .NET | SQL Server |
|---|---|
| `MyMoney.Data.SqlServer` (`IMoneyStore`) | connects as `MyMoneyUser` — `EXECUTE` on `Access/*` procs only, zero table grants |
| `MyMoney.Data.SqlServer.Provisioning` | connects as `MyMoneyAdmin` (`dbcreator`) — calls `master.dbo.MyMoney_CreateCatalog`, deploys `Schema/*` + `Access/*` procs, then **executes** `Schema_Apply` |
| `MyMoney.Data.SqlServer.TestTier` | connects as `MyMoneyTest` — `EXECUTE` on `*_Test_Reset` only, and those procs are only ever deployed into a `testDatabase: true` catalog |
| `sa` | nowhere in .NET as a stored credential — prompted for, used once by the bootstrap entry point, never persisted |

**Schema-creation-through-sprocs (the owner's constraint), concretely.** Replace today's
reflection-to-DDL `LazyCreateTables()` with a versioned proc family:

1. `sa` (prompted, once per server) runs `MyMoney_BootstrapServer.sql` → creates the three
   logins, grants `MyMoneyAdmin` `dbcreator`, deploys `master.dbo.MyMoney_CreateCatalog`.
2. `MyMoneyAdmin` calls `MyMoney_CreateCatalog` → empty catalog exists, `MyMoneyAdmin` is its
   owner.
3. `MyMoneyAdmin` deploys `SqlScripts/Schema/*.sql` — a set of procs (`dbo.Schema_ApplyTo(@TargetVersion)`,
   `dbo.Schema_CurrentVersion()`) that contain the `CREATE TABLE`/`CREATE INDEX`/`CREATE VIEW`
   DDL, each step idempotent and guarded.
4. `MyMoneyAdmin` **executes** `dbo.Schema_ApplyTo(@TargetVersion = N)`. The DDL runs inside the
   database, from a versioned, auditable object — not from string-building C#.
5. `MyMoneyAdmin` deploys `Access/*` procs and `GRANT EXECUTE ... TO MyMoneyUser`.
6. If and only if `testDatabase: true`: deploy `Test/*` procs, `GRANT EXECUTE ... TO MyMoneyTest`.

Upgrade becomes: deploy the newer `Schema_*` proc, call `Schema_ApplyTo(N+1)`. The existing
ad-hoc `Migrations/*.sql`-replayed-by-hand convention folds into step 3/4.

**The SQLite analogue of step 3/4.** SQLite has no server-side procedures, so the equivalent
"the DDL lives in a versioned artifact, not in C# string-building" is: the provisioning assembly
carries the DDL as **embedded `.sql` resources**, applied by a migration runner keyed on a
version ledger. Same versioning semantics, same auditability, same idempotency, and the
same "this code is in the provisioning assembly, not the store assembly" separation. The
reflection-to-DDL path (`GetCreateTableScript`) is deleted on both engines — it's the reason the
two engines' schemas have quietly drifted before.

> **Revision 2 supersedes the four paragraphs above in detail, not in direction.** The owner's
> follow-up added a constraint round 1 did not have — *the same proc family must serve both the
> nuke-and-pave process and the eventual real upgrade process* — and asked for the SQLite side to
> be worked out concretely rather than asserted symmetric. **§1.5 is the authoritative schema-
> management design**; read it in place of the sketch above. The one substantive correction it
> makes: round 1 said SQLite keys its runner on `PRAGMA user_version`. §1.5 replaces that with a
> real `__SchemaHistory` table on both engines, for reasons given there — `user_version` is a
> single integer with no room for a per-step record, and per-step records are exactly what issue
> #34 needs.

**What happens to `SaveOne`/`SaveTransfer`/`SaveBatch`/`IAggregateRoot`:** they move onto
`IMoneyStore` unchanged. They are the one part of the current data layer with a proven,
three-engine, ~60-test shared contract behind them; nothing here justifies reopening them.
Two changes around them:

- **`Save(MyMoney)` does not exist on `IMoneyStore`.** R1 said to retire it; it is still on
  `IDatabase` today. The rebuild is the clean moment.
- **`IAggregateRoot.Id` stays `long`.** Non-negotiable, already load-bearing.

**New: `IMoneyQuery`, the read-side port issue #5's Reports finding requires.** Not `IQueryable`
— the SQL Server tier is stored-proc-only, so an expression-tree provider is literally
impossible there, and an `IQueryable` that silently materializes everything and filters in
memory is worse than no abstraction at all. Instead, a closed, typed request:

```csharp
public sealed record AggregateQuery(
    IReadOnlyList<long> AccountIds,        // empty = all
    DateRange Range,
    TransactionStatusFilter Status,        // reconciled / cleared / uncleared / any
    IReadOnlyList<long> CategoryIds,
    GroupBy Primary,                       // Account | Month | Year | Payee | Category | None
    GroupBy Secondary);

IReadOnlyList<AggregateRow> IMoneyQuery.Aggregate(AggregateQuery q);
IReadOnlyList<TransactionRow> IMoneyQuery.List(ListQuery q);
```

SQLite implements it as parameterized SQL; SQL Server as a `_Query` proc family taking a TVP of
filter values (consistent with the TVP pattern Phase 2c already established); the test double
implements it over the in-memory graph. The shared contract suite grows to cover it — see §6.4
for why that's not optional.

**Pros**
- Enforcement is structural and *provable by inspecting the output directory*, which is the
  only form of "defense in depth" that survives a bad merge.
- Directly satisfies D-37 (SQL Server physically absent from a release) and D-35 (one engine per
  install) with no `#if` in sight — packaging, not preprocessor.
- The release-packaging boundary test ("no `MyMoney.Data.SqlServer*` in a Release publish output")
  is trivial to write and unambiguous when it fails.
- Each engine's store assembly stays small enough to read.

**Cons**
- 7–9 data-layer assemblies where there is 1 today. Real friction: more `.csproj` files, more
  solution noise, slower cold build.
- Store and provisioner for one engine share connection/mapping/type-conversion code, so either a
  shared `MyMoney.Data.Sqlite.Common` assembly appears (more assemblies) or `InternalsVisibleTo`
  appears (a crack — see §6.2).
- Not the shape most .NET developers reach for first; needs the reasoning written down or it gets
  "simplified" back in six months.

---

### Candidate B — One assembly per engine, tiering by gated handle types

Two engine assemblies (`MyMoney.Data.Sqlite`, `MyMoney.Data.SqlServer`), each containing all
three tiers' code, with the tiers expressed as distinct .NET types you can only *obtain* through
a gated factory:

```csharp
public static class SqliteStore
{
    public static IMoneyStore OpenUser(string path);
    public static IMoneyStoreProvisioner OpenProvisioner(string path, ProvisioningGrant grant);
    internal static IMoneyStoreTestControl OpenTest(string path, TestGrant grant);
}
// TestGrant's only public constructor lives in MyMoney.TestKit; SqliteStore has
// [InternalsVisibleTo("MyMoney.Data.Sqlite.TestTier")].
```

**Pros**
- Two assemblies instead of seven; no code duplication, no shared-common-assembly problem.
- Compile-time safety is still real: you cannot construct a `TestGrant` from production code.
- Substantially cheaper to build, and much easier to hold in your head.

**Cons**
- **The destructive code ships.** `MyMoney.Data.Sqlite.dll` in a release build contains the
  method that empties the database; only the capability *token* is withheld. Reflection reaches
  it in four lines. That is materially weaker than SQL Server's "the proc was never installed",
  and calling the two equivalent would be the comforting fiction the Adversarial Expert warned
  about.
- `InternalsVisibleTo` is load-bearing rather than incidental, which makes §6.2's failure mode
  structural rather than accidental.
- D-37's *"the released assembly set should not contain the SQL Server engine at all"* is still
  satisfiable (the whole SqlServer assembly is excluded), but the SQLite test-tier half of the
  same principle is not.

---

### Candidate C — Admin and Test tiers as out-of-process tools

Only `IMoneyStore` is a library the app links. Provisioning and test control are a separate
executable (`MyMoney.DataAdmin.exe`) the installer, the developer, and the test harness invoke.

**Pros**
- Strongest separation available without OS-level ACLs: different process, different credentials,
  different lifetime. It is the closest .NET analogue to what SQL Server actually does.
- Makes provisioning a real operations artifact — scriptable, CI-runnable, loggable — which the
  Operations Engineer wants regardless of which candidate wins.
- Turns "who is allowed to reshape the books" into an answerable question at the process level.

**Cons**
- **Kills the sub-minute TDD loop** if tests provision per test. A fresh SQLite file per test is
  currently sub-millisecond; a process launch is 50–200 ms, × several hundred tests = minutes,
  not seconds. That directly contradicts issue #5's stated cadence requirement.
- More to build before the first vertical slice exists, which contradicts the owner's own
  "data-layer DLLs + test subsystem first" sequencing.
- Process-boundary error reporting (exit codes, stderr parsing) is worse than exceptions.

---

### Recommendation: **A, borrowing one thing from C**

**Take Candidate A.** Tier-per-assembly, because the only enforcement mechanism that survives a
bad merge under deadline pressure is *"the code is not in the build output"*, and because it is
the only candidate where the SQLite facade's guarantee and the SQL Server role model's guarantee
are the same *kind* of guarantee rather than a strong one and a polite one.

**Borrow from C for SQL Server provisioning only.** The `sa`-credentialed server bootstrap is
genuinely a one-time operator action, not something app code should ever perform in-process. Make
it a thin console entry point over `MyMoney.Data.SqlServer.Provisioning` (`dotnet run --project
... bootstrap --server Redmond`). SQLite provisioning stays in-process — creating a file is not a
privileged act in any meaningful sense, and per-test provisioning speed is load-bearing.

**On `MyMoney.Data.Contracts` vs. leaving the ports in `MyMoney.Business`:** the Developer's
position, which the panel accepts, is *don't create the assembly yet*. The port interfaces can
stay in `MyMoney.Business` exactly where `IDatabase` is today — the dependency direction is
already right, and a separate contracts assembly buys nothing until a non-business consumer
appears. **`MyMoney.TestKit.Contracts` is different and must be separate from day one**, because
its entire purpose is to be absent from the production dependency graph.

**Mitigating A's real cost (the shared-code problem):** do *not* create
`MyMoney.Data.Sqlite.Common`, and do *not* use `InternalsVisibleTo`. Instead, accept a small
amount of duplication between store and provisioner (they need different things: the store needs
row mapping, the provisioner needs DDL and version bookkeeping — the genuine overlap is
connection-string construction and open/close, which is ~40 lines). If that assessment turns out
to be wrong once real code exists, revisit — but "we need `InternalsVisibleTo`" is the signal that
the tier split was drawn in the wrong place, not that the rule is too strict.

---

## 1.5 Schema management — one mechanism, two entry points (Data Engine Expert leading)

This section is revision 2's centre of gravity. The owner's two messages contain a constraint that
looks like two requirements and is actually one:

> *"We can 'nuke' the DB, and then re-pave it with new tables, indexes and views if all that work
> is done in sprocs."*
> *"**The sprocs need to support both the normal upgrade process and the 'nuke and pave' process.**"*

Read together, that rules out the obvious implementation — a fast `CreateEverything` proc for
fresh databases plus a separate `Upgrade` path bolted on later. Two code paths means the upgrade
path is the one that's never exercised, and the first time it runs for real is the first time it
runs on data that matters. The design below has **one** mechanism.

### S-0. The phase declaration, written down so it can expire

**Right now, every database this project touches is a test database.** Nuke-and-pave is the
accepted model for this phase: to change the schema, drop everything and re-apply from zero. No
data preservation is promised, because there is no data worth preserving. `DatabaseEntry.TestDatabase`
already exists in the registry config and already means exactly this.

This is an explicit, time-boxed decision, not a permanent property, and §1.8 states what has to
become true before it expires. The panel accepts it — it is the correct trade for this phase, and
pretending otherwise would buy migration ceremony with no data behind it. §6.7 names the one risk
it does create.

### S-1. The unified mechanism

There is exactly one thing that changes a schema: **an ordered list of versioned, individually-
recorded steps, applied by a single executor.** Everything else is an entry point into it.

```
Schema_CurrentVersion()              -> int     highest fully-applied step, 0 on an empty database
Schema_ApplyTo(@TargetVersion)                  apply every unapplied step from current+1..target
Schema_Verify()                      -> rows    introspected actual schema vs. expected; drift report
Schema_DropAll()                                [test tier only] drop every object this family owns
```

| Operation | Is | Not |
|---|---|---|
| **Create a new database** | `Schema_ApplyTo(N)` against an empty catalog — current version is 0, so it applies steps 1..N | a separate `CreateEverything` proc |
| **Nuke and pave** | `Schema_DropAll()` then `Schema_ApplyTo(N)` — which is then *literally indistinguishable* from creating a new database | a separate reset path |
| **Upgrade (deferred, §1.8)** | `Schema_ApplyTo(N)` against a database at version M — applies steps M+1..N | a separate upgrade path |

The three differ only in what `Schema_CurrentVersion()` returns when they start. That is the whole
trick, and it is what makes the owner's constraint satisfiable rather than merely stated: **the
upgrade path is exercised on every single fresh-database creation**, because creating a fresh
database *is* an upgrade, from version 0.

### S-2. The ledger — a table, not `PRAGMA user_version`

Round 1 proposed `PRAGMA user_version` for SQLite. Revision 2 rejects that and uses the same
ledger table on both engines:

```sql
CREATE TABLE __SchemaHistory (
    Version      INTEGER NOT NULL PRIMARY KEY,   -- step number
    StepName     TEXT    NOT NULL,               -- '0007_add_transactions_payee_index'
    ChecksumHash TEXT    NOT NULL,               -- hash of the step's SQL text as applied
    AppliedUtc   TEXT    NOT NULL,
    AppliedBy    TEXT    NOT NULL                -- login/user that ran it
);
```

`PRAGMA user_version` is a single 32-bit integer in the file header. It can say *"this database is
at version 7."* It cannot say *which* steps produced that, when, by whom, or whether the step-7 SQL
in this build is the same step-7 SQL that actually ran. Every one of those is needed by the
deferred production-upgrade workflow (§1.8), and `ChecksumHash` in particular is the only cheap
defence against the failure mode where someone edits an already-applied step's SQL and every
database that already ran it silently disagrees with every database that hasn't. A table costs one
`CREATE TABLE` and removes an entire class of "which build produced this schema?" questions.

`__SchemaHistory` itself is step 0, created by the executor before the ledger exists to record it —
the one genuine bootstrap exception, and it is the same exception on both engines.

### S-3. Steps are forward-only, additive, and own their own idempotency

A step is a numbered unit containing DDL. Rules:

1. **A step is never edited once it has been applied anywhere.** A mistake in step 7 is fixed by
   step 8. `ChecksumHash` enforces this by detection: the executor refuses to proceed if an
   already-recorded step's checksum no longer matches. During the nuke-and-pave phase this rule is
   *cheap to follow and also cheap to break* — nothing stops you editing step 7 and re-paving — so
   the checksum check must be **on from day one**, not added when production appears. Turning it on
   later means turning it on against a corpus of steps nobody was disciplined about.
2. **Every step is idempotent in its own right** (`IF NOT EXISTS`-guarded), *and* the ledger means
   it is never asked to be. Belt and braces, deliberately: the ledger is the mechanism, the guard is
   the safety net for the case where a step half-applied and the transaction didn't cover it (SQL
   Server DDL is transactional; SQLite DDL is transactional; both are fine here — but a step that
   spans a proc redeploy plus DDL is where this bites).
3. **Each step runs inside a transaction, and the ledger row is written in the same transaction.**
   A step either applied and is recorded, or did neither. This is not negotiable and is the reason
   `Schema_ApplyTo` loops step-by-step rather than wrapping the whole range in one transaction —
   a failure at step 9 of 12 must leave a database at version 8, not at "version 0 but actually
   partly at 9."
4. **Everything the schema owns is a step**: tables, *named columns*, indexes, foreign keys, views,
   check constraints, table types (TVPs), and the `Access/*` CRUD procs themselves. The owner listed
   "tables (named columns, indexes, foreign keys, etc.)" — the "etc." is doing real work there and
   §1.6 is where the proc half lands.

### S-4. How this kills issue #34 — and the honest caveat

**[Issue #34](https://github.com/markabrandjord/MyMoney.Net/issues/34)**: `CreateOrUpdateTable`
only runs `GetCreateIndexScripts(mapping)` inside the *"table doesn't exist yet"* branch, so an
index added to an already-existing table's mapping is silently never created — on either engine.
New tables get their indexes, new columns get handled explicitly, new indexes on existing tables
fall in a hole with no error and no warning.

The new mechanism removes the hole **structurally**, because the concept that caused it no longer
exists. There is no "does this table already exist?" branch anywhere. Adding an index is a step:

```sql
-- 0012_add_transactions_payee_date_index.sql
CREATE INDEX IF NOT EXISTS IX_Transactions_PayeeId_Date ON Transactions (PayeeId, [Date]);
```

- A **fresh** database is at version 0, applies steps 1..12, and gets the index because step 12 ran.
- An **existing** database at version 11 applies step 12, and gets the index because step 12 ran.

Same step, same executor, same reason. There is no branch that can skip one case and not the other,
because there is no branch.

**The honest caveat, stated plainly: this converts a silent-wrong-behavior bug into a
discipline requirement.** The mechanism guarantees *"every written step runs everywhere."* It does
not guarantee *"someone wrote the step."* If a developer adds an index to a `[TableMapping]`
attribute (or whatever replaces it) and forgets to write the corresponding step, the index is still
missing — just for a different reason. Today's bug is "the code can't do it"; the new failure mode
is "nobody asked for it." That is strictly better, but it is not nothing, and it is exactly the
place the Adversarial Expert would expect a design document to overclaim.

Three things close that gap, and they are **part of this design, not follow-ups**:

1. **`Schema_Verify()` — a drift check, not a hope.** Introspect the live schema (`sqlite_master`
   + `PRAGMA index_list`/`table_info`/`foreign_key_list` on SQLite; `sys.tables`/`sys.indexes`/
   `sys.foreign_keys`/`INFORMATION_SCHEMA` on SQL Server) and compare against the expected object
   set for the current version. Report every difference. This is the routine issue #34's own
   "Suggested fix shape" section was reaching for, generalized from indexes to everything, and
   moved from "run it during upgrade" to "run it as an assertion."
2. **The schema-equality contract test — the one that would actually have caught #34.** In the
   Tier-2 store-contract suite, per engine:

   > Build database **A** fresh at version N. Build database **B** fresh at version N−1, then
   > `Schema_ApplyTo(N)`. **Introspect both and assert the schemas are identical** — every table,
   > column, type, nullability, index (including its column list and uniqueness), foreign key,
   > and view.

   Today's code fails this test the moment an index is added to an existing table, which is
   precisely issue #34 reproduced as a red test. It runs on every build from slice 2 onward,
   which — per S-1 — is what keeps the never-yet-needed upgrade path honest during a phase where
   nobody upgrades anything.
3. **Deleting the reflection-to-DDL path entirely.** `GetCreateTableScript` /
   `GetCreateIndexScripts` / `CreateOrUpdateTable` / `LazyCreateTables` go away on both engines.
   The bug cannot recur in code that no longer exists, and keeping both mechanisms alive "for
   now" is how the two engines' schemas drifted in the first place.

### S-5. "More or less the same process" on both engines — concretely

The owner asked that creating a database look the same on both engines. It can, at the level of
*process*; it cannot at the level of *mechanism*, and §1.7 is honest about why. Here is the
process, step by step, with what each engine actually does:

| # | Step (identical on both) | SQL Server | SQLite |
|---|---|---|---|
| 1 | **Create the empty database** | `MyMoneyAdmin` calls `master.dbo.MyMoney_CreateCatalog` (already exists) | Create the file; `Connect()` applies `foreign_keys=ON`, `journal_mode=WAL`, `busy_timeout` (already exists) |
| 2 | **Install the schema-management logic into it** | Deploy `SqlScripts/Schema/*.sql` → real procs `dbo.Schema_ApplyTo`, `Schema_CurrentVersion`, `Schema_Verify` | Load the embedded `Schema/*.sql` step resources + the executor from `MyMoney.Data.Sqlite.Provisioning`. **See §1.7 for what "sproc-equivalent" honestly means here** |
| 3 | **Run it to build the schema** | `EXEC dbo.Schema_ApplyTo @TargetVersion = N` | `provisioner.ApplyTo(N)` — same step list, same ledger, same per-step transaction, same order |
| 4 | **Install the CRUD access layer** | Deploy `Access/*.sql` procs; `GRANT EXECUTE ... TO MyMoneyUser` | Steps 1–3 already created the views/triggers §1.7 describes; there are no grants to make (§1.7's honest limit) |
| 5 | **Install the test-tier capability, iff `TestDatabase: true`** | Deploy `Test/*.sql`; `GRANT EXECUTE ... TO MyMoneyTest` | `MyMoney.Data.Sqlite.TestTier` is present in the build or it is not (§1/§6.1) |
| 6 | **Verify** | `EXEC dbo.Schema_Verify` — must report no drift | `provisioner.Verify()` — same comparison, engine-native introspection |

Steps 1, 3, 5 and 6 are genuinely the same operation. Steps 2 and 4 are where the engines diverge,
and the divergence is real: on SQL Server the schema logic is *installed into the database and runs
there*; on SQLite it is *carried by the provisioning assembly and runs in-process against the file*.
The version ledger, the step list, the step SQL (modulo dialect), the ordering, the transaction
boundaries, the verify comparison and the entry-point API are identical. **What is not identical is
where the executor's control flow physically lives, and no amount of design makes that identical,
because SQLite has no server.** Saying otherwise would be exactly the "comforting fiction" §6.1
already warns about for the privilege model.

---

## 1.6 CRUD and batch procs — parameterized and transactional, stated explicitly

The owner asked for parameterized, transactional CRUD sprocs "because they are safer," and for
tabular-input sprocs that write multiple records as one transaction. Both already exist in the
Phase 2 persistence-concurrency work. Round 1 carried them forward *implicitly* ("`SaveOne`/
`SaveTransfer`/`SaveBatch` move onto `IMoneyStore` unchanged"). Revision 2 states them as
requirements, because an implicit carry-forward is a thing a later slice can quietly drop.

**R-CRUD-1 — every store write is parameterized.** No SQL built by string concatenation with a
value in it, on either engine. This is already true of every write path today. *(It is not yet true
of two schema-introspection reads: `SqliteDatabase.cs:218` and `:230` interpolate a table name into
`SELECT ... FROM sqlite_master WHERE tbl_name='" + name + "'`. Those are internal, non-user-supplied
identifiers, so it is not a live injection vector — but both live in code §1.5 deletes, and the
replacement introspection in `Schema_Verify` must not reintroduce the pattern.)*

**R-CRUD-2 — every store write is atomic at the call boundary.** `SaveOne`, `SaveTransfer` and
`SaveBatch` each either apply completely or not at all, including their in-memory side effects.
SQL Server achieves this inside the proc (`SET XACT_ABORT ON; BEGIN TRANSACTION;`); SQLite achieves
it with `BeginTransaction()` plus the `postCommitActions` deferral that keeps `RowVersion`/
`OnUpdated()` from being applied to the object graph until the commit actually succeeded. That
deferral is a genuinely hard-won behavior — a stale `RowVersion` claiming a commit the rollback
undid is a silent-corruption bug — and it is carried forward verbatim.

**R-CRUD-3 — tabular input is one proc call, one transaction.** The established pattern is a
table-valued parameter: `dbo.Currencies_SaveBatch @Rows dbo.CurrencySaveBatchRow READONLY`, one
call per root type, set-based inside the proc, with the two-result-set contract (assigned ids +
new versions) already contract-tested. This carries forward unchanged on SQL Server and is the
**target shape** for SQLite under §1.7 (which currently loops in C#).

**R-CRUD-4 — the conflict contract is engine-independent.** Zero rows affected by a
version-checked `UPDATE`/`DELETE` means `ConcurrencyConflictException` carrying the store's actual
current version. Identical on both engines today; contract-tested; unchanged.

**R-CRUD-5 — the CRUD procs are schema steps (§1.5, S-3 rule 4).** Deploying `Access/*` is not a
separate ritual sitting outside the version ledger. It is how the ordering bug the bootstrapper
comment already records — `CREATE OR ALTER PROCEDURE` eagerly validating column references against
*existing* tables, so `Payees_AccessProcs.sql` failed before the `Version` column migration ran —
stops being a comment explaining a hand-ordered sequence and becomes a step number.

---

## 1.7 New design principle: SQLite should use SQLite (Data Engine Expert)

> **P-SQLITE.** Where SQLite can express a behavior natively, express it in SQLite — not in
> imperative C# around SQLite. The goal is that `IMoneyStore` sits over two engines that *work*
> alike, not two engines that merely *expose* alike.

The owner's reasoning is sound and the panel endorses it, with one caution stated up front and
again in §6.8: **this principle is worth following where it removes a genuine behavioral
difference, and worth resisting where it merely relocates one.** What follows is the concrete
audit the guidance asked for — what SQLite offers that the current code doesn't use, and where the
limits are real.

### 1.7.1 What SQLite actually offers that this codebase is not using

Engine version 3.53.4 ships with all of these (§0.1); none needs a package change.

| SQLite capability | Not used today | What it would buy | Panel verdict |
|---|---|---|---|
| **`RETURNING`** (3.35+) | `SaveOneCategory` computes `c.RowVersion = callerRowVersion + 1` in C# after an `UPDATE` that did `Version = Version + 1` server-side | The *authoritative* post-write version, read from the engine, exactly as SQL Server's `_SaveBatch` procs return theirs. Removes an inference that is correct today only because nothing else can touch the row mid-transaction | **Adopt.** Small, removes a real C#-side simulation of a database fact, and makes both engines report versions the same way |
| **`json_each()` / `json_tree()`** (3.38+, built in) | SQLite loops in C#: one `ExecuteNonQueryInTransaction` per root | The genuine **TVP equivalent**. Pass the whole batch as one JSON parameter, then `INSERT ... SELECT ... FROM json_each(@Rows)` — one parameterized statement, set-based, engine-side, one transaction. This is the single largest structural convergence available between the two engines | **Adopt, measured.** It is the direct answer to R-CRUD-3 on SQLite. Verify with a benchmark: at small batch sizes a prepared-statement C# loop inside one transaction may genuinely beat JSON parsing, and "more native" is not a reason to be slower. Measure, then choose — and if the loop wins, say so here rather than adopting for aesthetics |
| **Views** | No `CREATE VIEW` anywhere | Read-side shapes (`IMoneyQuery`'s aggregates) defined once in SQL rather than assembled in C#, and defined in a *schema step* so they are versioned like everything else | **Adopt.** This is where §1's `IMoneyQuery` should land on SQLite, and it is the closest parity with SQL Server's `_Query` proc family |
| **`INSTEAD OF` triggers on views** | Not used | The closest thing SQLite has to a **stored procedure for writes**: multi-statement write logic that lives in the database, is versioned as a schema step, and is invoked by a plain `INSERT INTO SomeView VALUES (...)`. A store-tier caller issues one statement; the multi-step logic runs engine-side | **Adopt selectively.** Genuinely useful where a write is multi-statement (transfer both-sides, split rebalance). **Do not** use it to hide the version-conflict check — see 1.7.3 |
| **`CHECK` constraints** | Not emitted | Domain invariants enforced by the engine on both engines instead of by C# on one | **Adopt** for invariants that are genuinely schema-level (non-negative where truly non-negative, enum ranges). Resist the urge to push business rules in: a `CHECK` that fires is an opaque error, and §7's UI note about plain-language failures applies |
| **`STRICT` tables** (3.37+) | Not used — SQLite's default dynamic typing silently accepts a string in an `INTEGER` column | Column types that actually mean something, which is *exactly* the kind of SQL-Server-alike behavior this principle is about. It is also the single cheapest way to stop a type mismatch surviving a SQLite test and failing on SQL Server | **Adopt.** Strong recommendation; low cost since the schema is being rebuilt from zero anyway |
| **Generated columns** (3.31+) | Not used | Derived values computed by the engine rather than assigned in C# | **Note, don't adopt yet.** No current need; listing it so the option is known |
| **`UPSERT` (`ON CONFLICT DO UPDATE`)** (3.24+) | Not used | Insert-or-update in one statement | **Note, don't adopt.** The store's write paths are explicitly branch-on-`IsInserted`/`IsChanged`/`IsDeleted` because the *change type* is a domain concept carrying version semantics. Collapsing it into an upsert would lose the conflict check, which is a regression dressed as a simplification |
| **`PRAGMA index_list` / `table_info` / `foreign_key_list`** | `TableExists` scrapes `sqlite_master.sql` text | Structured schema introspection — exactly what `Schema_Verify` (§1.5, S-4) needs, and what issue #34's suggested fix named | **Adopt.** Required by §1.5 regardless |
| **`PRAGMA foreign_key_check`, `integrity_check`, `quick_check`** | Not used | An engine-native consistency assertion usable by the test tier and by the deferred backup/restore verification (§1.8) | **Adopt** in the test tier and provisioner |
| **`ATTACH DATABASE` + `VACUUM INTO`** | `VACUUM INTO` noted as absent-by-design from the store tier | A file-level backup/duplication primitive that is transactionally consistent, unlike `File.Copy` of a live WAL database | **Adopt in the provisioner tier**, where §3.2 already puts engine-native backup — and note it is *better* than the close-and-`File.Copy` round 1 described, because it does not require closing |

### 1.7.2 Where the limits are real — no amount of "use more SQLite" closes these

Stated flatly, because the owner's goal is that the two engines *appear* similar through
`IMoneyStore`, and an abstraction that oversells that is worse than one that documents the seam:

| Limit | Why it is unclosable | What the design does instead |
|---|---|---|
| **No stored procedures.** SQLite has no procedural language at all — no `CREATE PROCEDURE`, no variables, no control flow. | It is a library linked into the process, not a server with a query engine that executes code on your behalf. | Views + `INSTEAD OF` triggers cover *set-shaped* and *multi-statement-write* logic. Everything needing loops or branching stays in the provisioning/store assembly as versioned, embedded SQL plus a thin executor. §1.5's process table names this as step 2's real divergence rather than papering it. |
| **No server-side identities, roles, grants or permissions.** There is no `MyMoneyUser` on SQLite, and no `GRANT EXECUTE`. | There is no server and no authentication boundary. The process that opens the file has the file's OS rights, entirely. | §1's tier-per-assembly model, and §6.1's already-written honest statement of exactly how far that falls short. Revision 2 changes nothing here and adds no new claim. |
| **Triggers cannot be conditionally bypassed by privilege.** | Same reason. | Don't put test-tier-only behavior in a trigger. Test-tier capability stays an assembly-presence question. |
| **No table-valued parameters as a typed, server-declared object.** `json_each` is functionally equivalent but is not a declared type with a schema the engine validates. | SQLite has no user-defined types. | Accept the asymmetry; the contract suite (not the type system) is what proves the two batch paths behave identically. |
| **DDL-in-a-transaction differs in the details.** Both engines wrap DDL in transactions, but SQLite cannot `ALTER TABLE DROP CONSTRAINT`, has limited `ALTER TABLE` generally, and historically needs the 12-step table-rebuild dance for anything structural. | Engine design. | §1.5's steps are written per-dialect anyway. The *ledger, ordering and transaction-per-step semantics* are identical; the SQL inside a step is not, and never claimed to be. During the nuke-and-pave phase this is nearly free — a table rebuild on an empty test database is trivial — which is a genuine, if temporary, argument for getting the schema shape right *now*, while restructuring is cheap. |
| **No server-side scheduled/background work.** | No server. | Nothing in the design depends on it. Listed so nobody proposes it. |

### 1.7.3 One place the panel pushes back on the principle itself

**Do not move the version-conflict check into a SQLite trigger.** It is tempting — a `BEFORE
UPDATE` trigger raising `RAISE(ABORT, ...)` on a version mismatch is more "native" than checking
`rowsAffected == 0` in C#. Resist it:

- The current check is already engine-side (`WHERE Id=@Id AND Version=@ExpectedVersion`); what's in
  C# is only the *interpretation* of zero affected rows. That is not simulation, it's a return-code
  read.
- A trigger would make the two engines' conflict signal genuinely different: an aborted statement
  with a message string on SQLite, versus a rowcount plus a follow-up `SELECT` on SQL Server. That
  is **more** divergence, not less, and the conflict path is the single most contract-tested
  behavior in the data layer.
- It would push a well-tested behavior into a place that is harder to test, to satisfy a principle
  whose purpose is convergence it does not deliver here.

This is the shape of the general caution: P-SQLITE is a tool for removing real asymmetries
(1.7.1's `json_each`, `RETURNING`, views, `STRICT`), not a goal to be maximized.

---

## 1.8 The real production-upgrade workflow — explicitly deferred, explicitly not foreclosed

The owner: *"Eventually when we turn on a production db, we cannot just 'nuke and pave' to upgrade
a db. We have to have a real upgrade process for the db and the app."*

**This document does not design that workflow.** The shape was already agreed conceptually in an
earlier session — backup, data reformatting, stored-procedure modification, app-binary update,
automated test-run verification, with test databases marked in config so they are distinguishable
from production ones — and was explicitly deferred with *"all current databases treated as test
databases until production testing workflow is needed."* That deferral stands. Nothing in the
current slice plan builds it.

What revision 2 **does** owe is the compatibility check the owner asked for: where would today's
design make that future work harder? The panel found three places, and all three are cheap now and
expensive later:

1. **The ledger has to exist from the first schema step, not be retrofitted.** A production upgrade
   needs to answer *"what version is this database, what has been applied to it, and is the step-7
   in this build the step-7 that ran?"* A database created before the ledger existed can never
   answer that — it can only be guessed at by introspection. This is why §1.5 S-2 puts
   `__SchemaHistory` in from slice 2 and why `ChecksumHash` is populated from day one even though
   nothing checks it against a second build yet. **Cost now: one table and a hash. Cost later:
   unanswerable.**
2. **The upgrade path has to be exercised continuously, or it is fiction.** §1.5's whole design —
   one mechanism, fresh-create *is* upgrade-from-0, plus S-4's fresh-vs-upgraded schema-equality
   test — exists to make the never-yet-needed path run on every build. Without it, the first real
   upgrade would be the first execution of code nobody has ever run, against the only data that
   has ever mattered. **This is the single most important compatibility property in the document.**
3. **`Backup` must be in the provisioner contract and contract-tested from the start.** The
   production workflow's first step is a backup, and the Operations Engineer's standing position
   (§3.2) is that backup is the capability floor's weakest plank *today*. A backup routine that
   only appears when production appears is a backup routine that has never been restored from.
   §1.7.1's `VACUUM INTO` finding strengthens this: a transactionally-consistent SQLite backup is
   available and better than close-and-copy, so there's no cost argument for deferring it.

Two further notes, flagged rather than designed:

- **`DatabaseEntry.TestDatabase` is today the only thing standing between a destructive operation
  and a database.** It already gates test-proc deployment on SQL Server. When production databases
  become real, `Schema_DropAll` and the whole test tier must refuse to run against an entry without
  that flag — and the panel's position is that the refusal belongs in the **provisioner contract**
  (fail loudly, always compiled in) rather than only in the test-tier assembly's absence, because
  "the assembly isn't shipped" protects end users but does not protect a developer running a test
  suite against the wrong registry entry. That is a real, currently-live exposure on the owner's
  own machine, not a hypothetical.
- **App-binary/schema compatibility is a two-sided question this document only half-answers.**
  `Schema_ApplyTo` handles "the app is newer than the database." The reverse — an older binary
  opening a database at a *higher* version than it knows — needs a refusal, not a best-effort open.
  Minimum viable shape: the store checks `Schema_CurrentVersion()` at open and fails with a
  plain-language message if it exceeds the version the binary was built for. Cheap to add now,
  awkward to add once databases are out in the world. **Recommend adding it in slice 3**; not
  otherwise part of the deferred workflow.

---

## 2. Test-subsystem architecture (Test Engineer leading)

### 2.1 What's wrong with the current shape

Three things, all fixable:

1. **`MyMoney.TestSupport` is both a library and a test project.** It carries
   `Microsoft.NET.Test.Sdk` + `[TestFixture]` classes *and* is `ProjectReference`d by `UnitTests`
   and `UITests` for `MockDatabase`/`BasicsFixtureBuilder`. Anything that wants the test doubles
   drags the test runner with it, and the ~60 contract tests live in a library rather than a
   suite.
2. **`UnitTests.csproj` references `MyMoney.csproj`** — the WPF UI project. The existing
   test-scoping convention says business-layer tests don't touch the UI; the project graph says
   otherwise. (It's not gratuitous: `TransactionCollection`, the only concrete
   `FilteredObservableCollection<Transaction>`, lives in the UI project — a known gotcha. But
   it means "business test" is currently an honor system.)
3. **The one-way test-tier dependency holds today purely by nobody having broken it.** Nothing
   checks it.

### 2.2 Proposed assembly shape

```
MyMoney.TestKit.Contracts   IMoneyStoreTestControl and test-only DTOs. Library. Never shipped.
MyMoney.TestKit             THE "test business layer" DLL the owner asked for.
                            Fixture builders, scenario/seed DSL, store doubles, fault injection,
                            assertion helpers, the shared store-contract base classes.
                            Library only — NO test-runner packages, NO [TestFixture].
                            References: MyMoney.Business, MyMoney.TestKit.Contracts.
                            Referenced by: test projects only. Zero production references.

MyMoney.Data.*.TestTier     Per-engine IMoneyStoreTestControl implementations.

MyMoney.Tests.Architecture  Boundary/packaging enforcement. Milliseconds. Runs on every build.
MyMoney.Tests.Business      Business-layer TDD suite. Target: several hundred tests, < 60s.
                            May NOT reference the WPF UI project. (New rule; see 2.1 #2.)
MyMoney.Tests.Data          Store-contract suite, one fixture per engine.
UITests                     FlaUI. Unchanged in role; keep AppCrashGuard, BasicsTestSetup.
```

The owner's constraint — *test-tier DLL may call the user-tier DLL; the user-tier DLL has zero
reference to the test-tier DLL* — is satisfied by `MyMoney.TestKit → MyMoney.Business`, with no
arrow back.

### 2.3 Test types and where each runs in the cadence

| Tier | What it proves | Speed | When |
|---|---|---|---|
| **0 — Architecture** | No WPF in Business/Data; no production assembly references `*.TestKit*`/`*.TestTier*`; no production assembly declares `InternalsVisibleTo`; a Release publish contains no `MyMoney.Data.SqlServer*`; no type in `MyMoney.Data.Sqlite` implements `IMoneyStoreTestControl` | < 1 s | Every build |
| **1 — Business unit** | Computed results correct; correct calls made *to* the store; store-signalled errors handled correctly | target < 60 s for several hundred | The TDD loop — run the one new test repeatedly, whole tier at end of pass |
| **2 — Store contract** | Each engine implements `IMoneyStore`/`IMoneyQuery`/`IMoneyStoreProvisioner` identically | SQLite seconds; SQL Server minutes | End of pass (SQLite) / end of cycle (SQL Server) |
| **3 — UI wiring (FlaUI)** | The UI hands input to the business API and displays/handles what comes back | ~4 min | End of development cycle only |

This **keeps** the existing three-layer scoping convention from the Basics spec — UI tests don't
re-verify business logic, business tests don't re-verify persistence mechanics — with two
amendments: Tier 0 is new (the convention had no architecture band), and Tier 1 must lose its
UI-project reference.

### 2.4 The store doubles — and sub-decision T-1 (*resolved in revision 2*)

*(Round 1's position, kept for the reasoning; superseded in its conclusion by T-1 below.)*
**Keep `MockDatabase`'s proven behaviors**, reshaped as `MockStore : IMoneyStore, IMoneyQuery`:
the `DataContractSerializer` round-trip (so tests exercise a real round-trip rather than
object-identity coincidence), the committed-version bookkeeping, and its genuine
`ConcurrencyConflictException` on stale `RowVersion` — that last one is the single most valuable
thing in the current test support, because it makes error-path testing the default rather than an
afterthought. Add two things it doesn't have:

- **`RecordingStore`** — a decorator that records every operation (`SaveOne(Account#3, v2)`,
  `Aggregate(...)`) so a business test can assert *what was asked of the data layer*, which is
  explicitly shape (b) of the business-layer testing responsibility and is currently done by
  inference.
- **`FaultInjectingStore`** — a decorator configured to throw on the Nth call, or to throw
  `ConcurrencyConflictException` for a named root, or to fail mid-`SaveBatch`. This is the
  concrete answer to the owner's *"test processes to drive the happy path **and the error path**
  of the data layer"*.

**Sub-decision T-1:** should the default business-test double be a hand-written `MockStore` at all,
or should it be **`RecordingStore`/`FaultInjectingStore` wrapped around a real SQLite in-memory
store** (`Data Source=:memory:`)?

- *For the real-SQLite-decorated option:* zero divergence risk (the mock and the engine cannot
  disagree, because there is only one implementation), one fewer thing to maintain, and the
  aggregation query in §1 doesn't need a second LINQ implementation that can silently drift from
  the SQL one.
- *Against:* unknown speed at several-hundred-tests scale, and it couples every business test to
  the schema being current.

Round 1 left this as *"a measurement, not an opinion."* The owner has asked for the data-engine
role's actual judgment, noting their own instinct was in-memory SQLite.

#### T-1 — resolved: **in-memory SQLite, decorated. Do not build `MockStore`.**

The panel's recommendation, with the reasoning rather than just the verdict:

1. **The owner's instinct is right, and §1.7 makes it more right than it was this morning.**
   P-SQLITE's whole point is that behavior should live in the engine. A hand-written `MockStore`
   is, by construction, a C# re-implementation of engine behavior — the exact thing the new
   principle says to stop doing. Building a mock while adopting P-SQLITE would be the document
   contradicting itself in two adjacent sections.
2. **§6.4's divergence problem is otherwise unsolvable, not merely hard.** `IMoneyQuery` brings
   aggregation into the port. A mock needs a LINQ-over-memory implementation of every aggregate;
   the engines have SQL ones; and a subtly-wrong subtotal in the mock makes every business-layer
   report test assert against a fiction that looks green. Deleting the second implementation makes
   the bug class structurally impossible rather than test-detectable.
3. **The speed objection is weaker than it looks, for reasons specific to this codebase.**
   `SqliteDatabase` already holds one long-lived cached connection (§0.1) — which is exactly the
   lifetime model `:memory:` requires, since an in-memory database exists only as long as its
   connection. No new lifetime design is needed. An in-memory `Schema_ApplyTo` run is DDL against
   an empty page cache with no fsync and no journal; the realistic per-test cost is *sub-
   millisecond to low-single-digit milliseconds*, against a several-hundred-test budget of 60
   seconds. The plausible failure mode is not "SQLite is slow," it is "schema setup is repeated
   needlessly per test" — which is a fixture-design problem with a known fix (below), not an
   engine problem.
4. **"It couples every business test to the schema being current" is a feature in this phase.**
   During nuke-and-pave (§1.5 S-0), a business test failing because the schema step wasn't written
   is *correct behavior* — it is the same signal §1.5 S-4 says the design needs more of, arriving
   earlier and cheaper.

**How to build it so the speed objection stays hypothetical:**

- One `:memory:` store per test, over one connection, disposed at teardown. No file, no cleanup,
  no cross-test leakage, no `AppCrashGuard`-style residue.
- Apply the schema via **the same `MyMoney.Data.Sqlite.Provisioning` executor production uses**,
  not a test-only shortcut. If that turns out too slow per test, the fix is to build the schema
  once and use SQLite's **backup API / `VACUUM INTO`** to clone a prepared in-memory template per
  test (milliseconds, and it also exercises §1.8's backup plank) — *not* to introduce a parallel
  test-only schema path, which would reintroduce drift by the back door.
- **Known gotcha, from reading `Connect()`:** `PRAGMA journal_mode=WAL` is not applicable to an
  in-memory database (it reports `memory`). The pragma sequence must tolerate that rather than
  treating a non-`wal` result as a failure — a small, concrete thing that will otherwise surface as
  a confusing first-run error in slice 4.
- `RecordingStore` and `FaultInjectingStore` stay exactly as round 1 designed them. They are
  decorators over `IMoneyStore` and do not care what they wrap, which is the property that makes
  this switch cheap. Fault injection in particular gets *better*: a decorator can now inject a
  fault into a store whose non-faulted behavior is the real engine's.

**What slice 9 becomes.** Not an open decision — a **verification gate on a decision already
made**. Run several hundred real business tests against the in-memory store and record the wall
time. The kill criterion, stated in advance so it can't be rationalized afterwards: **if the
business-test tier exceeds 60 s and profiling shows the store (not the tests) is the cause, and
the template-clone optimization above doesn't fix it, only then revisit a hand-written mock** — and
if that happens, the mock must be generated from or contract-tested against the same
`StoreContractTests` suite the engines run, so §6.4's divergence problem is bounded rather than
reopened.

**What this changes elsewhere in the document:** `MockStore` is removed from slice 4's deliverables
(§5) and from §8's summary. `MockDatabase`'s three genuinely valuable behaviors are not lost — the
`DataContractSerializer` round-trip becomes unnecessary (a real store round-trips through SQL,
which is strictly more honest), and committed-version bookkeeping plus a real
`ConcurrencyConflictException` on stale `RowVersion` are things the SQLite store *already does for
real*. Deleting the mock loses nothing it was valued for.

### 2.5 How the one-way dependency is actually enforced — four layers

1. **No `ProjectReference`.** Necessary, insufficient, one merge away from untrue.
2. **Build-time failure.** A `Directory.Build.targets` in the production projects with a target
   that inspects resolved `@(ReferencePath)` and errors on any `*.TestKit*`/`*.TestTier*` match.
   This is the layer that beats deadline pressure, because it fails at `dotnet build`, not at a
   test run someone might skip.
3. **Architecture test** (Tier 0), following the existing, working `LayerBoundaryTests` pattern —
   `assembly.GetReferencedAssemblies()` over the compiled DLL, which is the check that already
   caught a false positive from MSBuild's verbose log and is therefore the one the project already
   trusts.
4. **The type isn't reachable.** `IMoneyStoreTestControl` lives in `MyMoney.TestKit.Contracts`;
   production doesn't reference it; production code cannot name it to implement or call it.

A fifth option — `Microsoft.CodeAnalysis.BannedApiAnalyzers` — is available but redundant given
(2) and (4); mention it only if a specific API (not a whole assembly) ever needs banning.

**New Tier-0 tests to write, concretely:**

```
ProductionAssemblies_DoNotReferenceTestKit          (each of Business, Data.Sqlite, MyMoney UI)
ProductionAssemblies_DeclareNoInternalsVisibleTo
SqliteStoreAssembly_ContainsNoTestControlImplementation
ReleasePublishOutput_ContainsNoSqlServerEngineAssembly
MyMoneyBusiness_HasNoWpfAssemblyReference           (carry forward, unchanged)
MyMoneyData_HasNoWpfAssemblyReference               (carry forward, per-engine now)
TestsBusiness_DoesNotReferenceTheWpfUiProject       (fixes 2.1 #2)
```

The fourth one is the test that makes D-37 real rather than aspirational, and it is the one most
likely to be quietly deleted when it becomes inconvenient — so it should assert against an actual
`dotnet publish -c Release` output directory, not against a `.csproj`.

---

## 3. The open question, resolved: where common control / import-export / reporting-output logic lives

One rule drives all three answers:

> **The data layer owns only what is engine-specific. The business layer owns everything
> computable. The UI layer owns only pixels and gestures.**

### 3.1 Common control logic → business layer, in an explicit application-services band

`MainWindow.xaml.cs`'s 5,209 lines of orchestration (open/close/switch database, upgrade prompt
flow, dirty-state handling, recent files, command routing) decompose into use-case classes in
`MyMoney.Business` — call the band `AppServices`, sitting alongside the existing
`DatabaseLifecycle`, which is already exactly this shape and is the working precedent.

They talk to the UI only through the existing callback ports (`IBusinessLayerUiCallback`,
`IDataLayerUiCallback`). **Explicitly not the data layer:** a store must not know what a "recent
file", a "prompt", or a "pending changes flyout" is. A store that knows about prompts is a store
that can't be tested without a prompt.

*(Whether `MainWindow`'s decomposition is in scope for this effort or a separate one remains
issue #5's own open question — §4 keeps it there.)*

### 3.2 Import/export → split three ways, by what each part actually is

- **Foreign-format parsing and writing** (QIF, OFX/SGML, CSV, XML) → **business layer**
  (`MyMoney.Business.Formats`). Pure `bytes → domain` / `domain → bytes`. Most of this is already
  in `MyMoney.Business` after the subsystem migration; the OFX *download* half keeps its
  `IOnlineService`-style network seam and stays testable by mocking that seam.
- **Whole-file formats that are not stores** (XML export, CSV export, and — per D-35 — anything
  that can't meet the store capability floor) → also business layer, behind
  `IDataFormat { void Write(MyMoney, Stream); MyMoney Read(Stream); }`. **They must not live
  behind `IMoneyStore` and must not live in an engine assembly.** This is what finally kills
  `NotImplementedException`-as-a-capability-signal: you cannot pass a format where a store is
  required, because they are different types in different assemblies.
- **Engine-native duplication/backup/restore** (SQLite close-and-`File.Copy`; SQL Server
  `BACKUP`/`RESTORE`) → **data layer, provisioner tier**, because it is engine-specific by
  definition. Already decided by R1; this just names the tier it lands in. The Operations
  Engineer's standing insistence that *backup is part of the capability floor and is currently its
  weakest plank* attaches here: `IMoneyStoreProvisioner.Backup` must be able to overwrite, and must
  be contract-tested.

### 3.3 Reporting-output logic → three stages, consistent with D-17 and issue #5

This is the part issue #5 identified as needing genuine redesign rather than relocation, and
today's D-17 decision (Markdown intermediate → Microsoft Print to PDF) constrains the answer. The
three stages land in three different places, and that is the whole point:

**Stage 1 — data layer (`IMoneyQuery`).** Filtering and subtotaling happen in SQL. Today
`CashFlowReport.Generate()` calls `GetAllTransactionsByTaxDate()` — it loads *everything* and
filters in memory. The query port from §1 is what replaces that, and it is the only part of
reporting that is engine-specific.

**Stage 2 — business layer (`ReportModel`).** A report becomes
`IReportBuilder.Build(ReportRequest) → ReportModel`, where `ReportModel` is a plain, serializable,
format-agnostic tree: sections, tables, rows, and typed cells carrying a *value* plus *formatting
intent* (currency, percent, negative, emphasis, group-header, expandable-detail) — **never a
`Brush`, a `UIElement`, or a `MouseButtonEventHandler`.** This is issue #5's "format-agnostic data
stream", made concrete. `IReportWriter` and `IReport.Generate(IReportWriter)` are **deleted, not
ported** — every one of `IReportWriter`'s WPF-typed members is the reason `CsvReportWriter` has to
implement methods about brushes.

**Stage 3 — renderers (`ReportModel` → text).** `ReportModel → Markdown` (D-17's intermediate),
`ReportModel → CSV` (D-18's guarantee). Both are pure functions over a plain data structure, so
both are trivially unit-testable with string assertions — which is exactly the resolution D-17's
owner decision offered to the Test Engineer's recorded dissent, and it only works if Stage 2 is
genuinely separate.

**Where Stage 3 lives — a deliberate anti-recommendation.** The tempting answer is a new
`MyMoney.Reporting.Render` assembly. The Developer's objection holds: don't create an assembly
before a second consumer exists. **Put Stage 3 in `MyMoney.Business.Reporting`** (namespace and
folder, same assembly), with the Tier-0 WPF-free boundary test covering it. Split it out only if
and when a non-business consumer appears (a CLI report generator, say). The architectural
property that matters — *the renderer has no UI dependency and is testable by string comparison* —
is provided by the boundary test, not by the assembly count.

**What stays in the UI project:** a Markdown viewer control, and the `PrintDialog` call against
the "Microsoft Print to PDF" driver. That is all. Charts stay in the UI as visual controls;
`ChartData`/`CategoryData`/`RentalData`'s data-shaping moves to Stage 2 alongside the report
builders, since it is the same `query → model` shape.

---

## 4. What genuinely needs the owner's decision

These are not things the panel is dodging — each is a product or appetite question that a
technical panel cannot legitimately answer.

1. **Does the rebuild happen in place, or in a parallel tree?** Replace `MyMoney.Data` and friends
   incrementally with the app building and running throughout, or start `Source/Rebuild/` with the
   current app frozen until the new stack catches up? This is the single biggest sequencing
   decision and it trades "always shippable" against "one clean cut." Everything in §7 assumes
   *in place, incrementally*; say so if that's wrong.

2. **Does the shipped SQLite path still need D-34's file lease?** D-37's downstream effects said
   *"since SQL Server does not ship to end users, C (a file lease) is what the shipped SQLite path
   needs"* — but the persistence-concurrency work has since given SQLite real version-checked,
   conflict-detecting saves, and the owner's stated AI-agent-in-parallel scenario runs on SQLite
   too. Is the lease still wanted (single-writer, simple, honest), or superseded by optimistic
   concurrency? This is a product promise about what two simultaneous writers on one machine are
   told.

3. **Must SQL Server stay at feature parity throughout the rebuild?** Every new store capability
   (starting with `IMoneyQuery`) implemented twice and contract-tested twice, or may the SQL Server
   tier lag and be caught up at milestones? The cost is real, recurring, and paid on every slice.

4. **Does whole-graph `Load()` survive?** The `MyMoney` object graph with its `PersistentObject`
   change tracking assumes "load everything at startup, hold it in memory." The `IMoneyQuery` port
   makes incremental, query-backed loading *possible*. This is the biggest architectural fork in
   the entire rebuild — it decides whether `Money.cs`'s object graph survives as the domain model
   or becomes a view over queries. The panel deliberately designed §1 to work **either way**
   (`LoadAll` stays on `IMoneyStore` for now), but this can't stay undecided for long.

5. **Is `XmlStore` kept as an export format from day one?** D-35 says yes-as-a-format (disaster
   recovery, human-readable, "the only way off the product"). Whether `IDataFormat` and an XML
   implementation are in the *first* slices or deferred is scope, not principle.

6. **Credential storage.** The three SQL Server logins' generated passwords currently sit in
   plaintext in `dataengine.config.json` — a live defect on the owner's own machine, already
   flagged. Does the rebuild move them to Windows Credential Manager / DPAPI now, or is that a
   separate tracked item? Panel recommends now (the bootstrap is being rewritten anyway); the
   appetite call is the owner's.

7. **Is "sample data" a product feature or a test capability?** `SampleDataGenerator` is in
   `MyMoney.Business` and is user-reachable (File ▸ Add Sample Data). If it stays a product
   feature, it must **not** move into `MyMoney.TestKit`, and the first-time-population `SaveBatch`
   path stays in the shipped product. Confirm, because it affects where a surprising amount of
   seeding code lives.

8. **(New, revision 2.) When does the nuke-and-pave phase end, and what is the trigger?**
   §1.5 S-0 accepts nuke-and-pave *for this phase*, and §1.8 keeps the exit affordable — but
   "eventually" is not a criterion. The panel cannot set this one: it is a product decision about
   when the owner's own data stops being disposable. What the panel needs is the **trigger**, not
   the date — e.g. *"the first database I import real Quicken data into and intend to keep."*
   Naming the trigger now matters more than the timing, because §1.8's three compatibility
   properties are cheap to hold continuously and expensive to establish retroactively, and because
   the day the phase ends should be a decision someone makes, not a thing that turns out to have
   already happened.

### 4.1 Re-scoring the round-1 list against today's guidance

Today's guidance touches four of the seven. Stated explicitly, per the revision's brief:

| # | Status after revision 2 |
|---|---|
| 1 — in place vs. parallel tree | **Reframed, and materially de-risked.** The strongest argument for a parallel tree was always *"the existing database format must keep working while the new one is built."* S-0 removes it: there is no data to preserve and no user to keep shippable for. That does not *decide* the question — build-breakage and reviewability arguments survive untouched — but it takes the scariest constraint off the table, and it strengthens §7's assumption of *in place, incrementally*. Still the owner's call. |
| 2 — SQLite file lease | Unchanged. Nothing today bears on it. |
| 3 — SQL Server feature parity throughout | **Sharpened, and partly answered by implication.** "The process should be more or less the same on SQLite and SQL Server" is a parity requirement, but specifically about *schema management*, and it is a stronger claim than round 1's framing: §1.5's step list, ledger and verify semantics must be parity **from slice 2**, not caught up at a milestone, because a ledger that only one engine has is not a ledger. The question the owner still owns is narrower than round 1 posed it: **may `IMoneyQuery` and other read-side capabilities lag on SQL Server while schema management does not?** The panel's recommendation is yes — lag the query surface, never the schema surface. |
| 4 — does whole-graph `Load()` survive | Unchanged, and worth saying why, since it's easy to assume otherwise: nuke-and-pave makes *schema* change cheap; it says nothing about whether the in-memory `MyMoney` object graph remains the domain model. Still the biggest fork in the rebuild, still undecided, and §1 still works either way. |
| 5 — `XmlStore` as a day-one export format | **Arguably resolved, in the direction of "yes, sooner."** Under nuke-and-pave, an XML export is the only thing that lets a developer keep a hand-built scenario across a re-pave. That is a genuine new argument for it being early rather than deferred — but it is a scope call, so it stays on the list with a recommendation attached rather than being ticked off unilaterally. |
| 6 — credential storage | Unchanged. Panel still recommends now. |
| 7 — sample data: product or test? | **Reframed, and now more urgent.** Under nuke-and-pave, "re-populate a freshly paved database with something to look at" is a routine developer action, which makes `SampleDataGenerator` load-bearing for daily work rather than a nice-to-have feature. The panel's recommendation firms up: **keep it a product feature in `MyMoney.Business`**, and let the test tier seed through `MyMoney.TestKit` separately, rather than merging the two — but this needs the owner's confirmation more than it did this morning, not less. |

Net: **one resolved by the panel** (T-1, §2.4 — it was a sub-decision, not on this list, but it was
the largest genuinely-open technical question in round 1), **two reframed** (#1, #3), **two
strengthened with recommendations** (#5, #7), **one added** (#8), and **two untouched** (#2, #4).

---

## 5. Recommended starting point, sanity-checked

The owner's proposed first vertical slice — *"enough to support adding an account to the
database"* — **holds up well.** `Account` is an `IAggregateRoot` with no owned children (unlike
`Transaction` → `Splits`/`Investment`), so it exercises the full insert/update/delete + version
conflict path without dragging aggregate-composition complexity in on day one. Three wrinkles to
flag:

- **You can't add an account before provisioning exists**, so the slice is really
  *provision-then-add*, and that's good — it forces the Admin tier to be real from the first
  commit rather than bolted on.
- **`Account` has FK relationships** to `Currency` and `OnlineAccount` (both nullable). With
  `foreign_keys=ON` (already set), the slice's schema must create those *tables*, even though it
  needn't create any *rows*. Small, concrete, easy to get wrong once.
- **`IAggregateRoot.Id` is `long`.** Don't let a fresh schema narrow it to `INT` because that's
  what the current tables say.

### Proposed slice order

*(Revised for revision 2: slices 2, 4, 7, 8 and 9 changed; 2b, 5b and 10 are new.)*

| # | Deliverable | Proves |
|---|---|---|
| 1 | `IMoneyStore` / `IMoneyStoreProvisioner` / `IMoneyQuery` ports (in `MyMoney.Business`), `MyMoney.TestKit.Contracts` with `IMoneyStoreTestControl` | The tier split exists as types before any engine implements it |
| 2 | `MyMoney.Data.Sqlite.Provisioning` — the §1.5 executor: `__SchemaHistory` ledger, per-step transactions, checksums, `ApplyTo`/`CurrentVersion`/`Verify`; steps creating `Accounts` + FK-target tables as `STRICT` | Schema-as-versioned-artifact, not reflection-to-DDL; and the upgrade mechanism exists before anything needs upgrading |
| **2b** | **The fresh-vs-upgraded schema-equality test** (§1.5 S-4): build at N; build at N−1 then `ApplyTo(N)`; assert introspected schemas identical. Include a step that adds an index to a table created by an earlier step — the issue #34 shape | **Issue #34 cannot recur.** The upgrade path runs on every build from here on |
| 3 | `MyMoney.Data.Sqlite` — `SaveOne<Account>`, `LoadAccounts`, conflict detection, `RETURNING`-read versions (§1.7.1); plus §1.8's open-time version check ("this database is newer than this binary") | The proven `SaveOne` pattern survives the reshape, engine-side |
| 4 | `MyMoney.TestKit` — in-memory-SQLite store fixture (T-1), `RecordingStore`, `FaultInjectingStore`, `StoreContractTests` base with the Account cases | Happy path **and** error path from day one, as the owner asked — over a real engine, not a mock |
| 5 | `MyMoney.Data.Sqlite.TestTier` — `IMoneyStoreTestControl` (wipe/reset) implemented as `Schema_DropAll` + `ApplyTo(N)`, i.e. nuke-and-pave through the §1.5 machinery | The SQLite facade, for real — and the owner's nuke-and-pave, as a first-class operation rather than a script |
| **5b** | `TestDatabase`-flag refusal in the provisioner contract (§1.8): destructive operations fail loudly against an entry not marked as a test database | The only guard that currently exists becomes enforced rather than assumed |
| 6 | `MyMoney.Tests.Architecture` — all seven Tier-0 tests from §2.5 | The guarantees are enforced, not asserted |
| 7 | `AddAccountService` in `MyMoney.Business` + its in-memory-store-backed tests incl. the conflict path | The business layer is callable with no UI present |
| 8 | `MyMoney.Data.SqlServer{,.Provisioning,.TestTier}` for the same slice: real `Schema_ApplyTo`/`Schema_Verify` procs over the same step list and same ledger, plus slice 2b's equality test per engine | The tiering maps twice, the schema mechanism is parity (§4.1 #3), and the contract suite is genuinely shared |
| 9 | **T-1 verification gate** (§2.4): run the real business-test tier against the in-memory store; record wall time against the 60 s budget and the stated kill criterion | The decision already made is confirmed by measurement, not re-opened |
| 10 | **`json_each` batch benchmark** (§1.7.1): SQLite `SaveBatch` as one set-based statement vs. today's C# loop, at realistic batch sizes | P-SQLITE is applied on evidence, not aesthetics — and R-CRUD-3 lands on both engines or is honestly declined on one |

**One honest deviation from the literal instruction.** The owner's guidance says build *"the data
layer DLLs, as well as the app and business layers of the test subsystem first."* The business
half of that is slices 4/7 above. The **app** half is the part to push back on: there is no
rebuilt app layer yet for app-layer tests to test, and the project already has working, hard-won
FlaUI infrastructure (`BasicsTestSetup`, `AppCrashGuard`, the shared-session lifecycle notes in
`docs/dev/flaui-basics-test-notes.md`). Recommendation: **carry that infrastructure forward as-is
and re-point it when there's a rebuilt app to point it at**, rather than building a second UI test
harness against a UI that doesn't exist. Flagged rather than silently skipped.

---

## 6. Adversarial review — where this design is weakest

### 6.1 The SQLite facade is not equivalent to SQL Server role separation, and the design must say so

SQL Server's guarantee is enforced by a *different process*, under a *different identity*, and the
destructive routine is *not installed* on a production catalog. SQLite's file is opened by the
same process that would do the damage, with full OS rights to it, always. No arrangement of .NET
types changes that.

What Candidate A's facade **can** honestly claim:

- The code capable of the destructive operation is **not in the shipped assembly set** (mirrors
  P9-GUARD-2's real property).
- Production code **cannot name the type** needed to request it.
- A build fails, and a test fails, if either becomes untrue.

What it **cannot** claim: that a buggy, compromised, or deliberately-reflective production process
is prevented from destroying the file. The only genuine strengthenings available are Candidate C's
process separation or OS file ACLs, and the panel judges neither proportionate for a
single-user desktop finance app. **State this scope limit in the eventual spec in these words** —
an overstated guarantee is worse than an honest partial one, because it stops people looking.

### 6.2 `InternalsVisibleTo` is the most likely crack

Not a hypothetical: it is the standard .NET answer to "the test needs to reach this," and it
dissolves the whole tier split in one line. Hence the Tier-0 test
`ProductionAssemblies_DeclareNoInternalsVisibleTo`. If a genuine need ever appears, it means the
tier boundary was drawn in the wrong place — move the boundary, don't punch through it.

### 6.3 The deadline shortcut that actually happens

*"I just need `BasicsFixtureBuilder` from TestKit in this one production class."* A test-run-time
failure is easy to ignore when you're mid-task; a `dotnet build` failure is not. That's why §2.5's
mechanism (2) — the build-time `ReferencePath` check — matters more than mechanism (3), even
though (3) is the one that matches the project's existing, trusted pattern. **Both**, not either.

### 6.4 Two implementations of aggregation, silently diverging

`IMoneyQuery` will have a LINQ-over-memory implementation in the test double and a SQL
implementation per engine. If the shared contract suite covers only writes (as it does today) and
not queries, then every business-layer report test is asserting against a fiction, and the first
time anyone notices is when a real report subtotal is wrong. **The contract suite must grow to
cover `IMoneyQuery` in the same slice the port is introduced — not later.** This was also the
strongest single argument in favor of T-1's decorated-real-SQLite option, which makes the problem
structurally impossible — and in revision 2 it is the argument that carried T-1 (§2.4). With no
mock, the "two implementations" in this section's title are now only the two *engines*, which the
shared contract suite was always designed to compare. The requirement above is unchanged and still
not optional: the contract suite must cover `IMoneyQuery` the slice it appears.

### 6.5 The simpler design nobody has proposed — *adopted in revision 2*

Delete `MockStore`. Run every business test against a real in-memory SQLite store, decorated for
recording and fault injection. Fewer moving parts, zero divergence, one implementation of every
query. Round 1 stopped short of recommending it only because nobody had measured whether it fits
the sub-minute budget.

**Revision 2 takes it.** §2.4's T-1 resolution adopts this design and keeps the measurement as a
verification gate with a pre-stated kill criterion, rather than as a precondition. The adversarial
note that survives: *a design section that adopts the thing the adversarial section proposed is
exactly where nobody looks for the flaw.* The flaw, if there is one, is that the in-memory store
makes every business test depend on the schema steps being written and correct — so a broken
schema step now fails hundreds of tests instead of a handful, with a first error message about
SQL rather than about business logic. The panel judges that an acceptable and even desirable
coupling during nuke-and-pave (§2.4 point 4), but it is a real change in failure ergonomics and
whoever is debugging at 11pm should have been told.

### 6.6 Seven assemblies is a real cost, not a rounding error

More projects, slower cold builds, more `.csproj` churn in reviews, and a structure that the next
person will try to "simplify." The mitigation is not discipline; it is that the Tier-0 tests fail
loudly when the simplification happens, and that the reasoning is written down where the tests
point to it.

### 6.7 Nuke-and-pave is accepted — here is the risk it creates anyway

The owner has decided this and the panel agrees with the decision. Naming the risk is not
re-litigating it; it is the reason §1.5 and §1.8 are shaped the way they are.

**The risk is not data loss. It is habit formation.** Nuke-and-pave makes the *incremental* step
the expensive one and the *rewrite* the cheap one, on every schema change, for as long as the phase
lasts. Every day of that phase trains a workflow — "change the table definition, re-pave, move on" —
that is exactly wrong on the day production arrives, and it does so quietly, because nothing ever
fails. Several specific expressions of it:

- **Steps get edited instead of appended.** Rule S-3.1 says a step is immutable once applied. Under
  nuke-and-pave that rule protects nothing you can see, so it is the first rule to erode. This is
  the entire reason `ChecksumHash` must be populated and checked from slice 2 rather than "when it
  matters" — the check is the only thing that makes the rule observable during a phase where
  breaking it is free.
- **Steps don't get written at all.** Why add step 12 for an index when re-paving from a corrected
  step 4 works? Because on the day it isn't free, the corrected step 4 is a lie about what every
  existing database ran. §1.5 S-4's caveat is honest that the mechanism does not prevent this;
  slice 2b's equality test is what makes the omission visible.
- **The "end of the phase" never gets declared** — it just turns out, retrospectively, to have
  happened three months ago on a database somebody started caring about. Hence open item #8: the
  panel wants the *trigger* named, not the date.
- **`Schema_DropAll` exists and works, in a codebase whose only guard is a config flag.** §1.8 and
  slice 5b address this; flagged here because "we're in the test phase" is precisely the reasoning
  that makes a destructive operation feel safe to leave unguarded, and precisely the reasoning that
  stops being true without anyone editing any code.

None of this argues for migration ceremony now. It argues that the three cheap properties in §1.8
are the price of taking the shortcut safely, and that they are cheap *only* while the phase lasts.

### 6.8 "Use more native SQLite" can itself become a divergence source

P-SQLITE (§1.7) is aimed at convergence, and mostly achieves it. But the same principle, applied
without the §1.7.3 brake, produces the opposite: a `CHECK` constraint, a trigger, or a `STRICT`
column that exists on SQLite and has no SQL Server counterpart is a behavioral difference hiding
under one interface — the precise failure the owner is trying to eliminate, arrived at from the
other direction. Two rules follow, and they belong in the eventual spec:

1. **An engine-native behavior adopted on one engine must be matched on the other or explicitly
   declined in writing.** `STRICT` tables have a SQL Server counterpart (typed columns, which it
   has always had), so adopting them converges. An `INSTEAD OF` trigger encoding a write rule has a
   counterpart (a proc), so it converges. A `CHECK` constraint with no SQL Server twin diverges.
2. **The shared contract suite is the arbiter, not the principle.** If a native feature can't be
   shown to produce identical observable behavior through `IMoneyStore` on both engines, it doesn't
   go in — however native it is. §6.4 already makes this argument for `IMoneyQuery`; §1.7 widens
   the surface it has to cover, which is a real, recurring cost of the new principle and should be
   budgeted as one.

Stated as a tension rather than resolved, because it is a genuine one: the owner asked for maximum
use of SQLite's own functionality *in service of* making the two engines look alike, and those two
goals do not point the same direction at every decision. Where they conflict, the panel's
recommendation is that **parity wins over nativeness** — §1.7.3's version-conflict example is the
worked case.

---

## 7. Role notes, opt-outs, and expertise the panel does not have

**UI/UX Expert — largely opting out** (backend-heavy phase), with two contributions worth keeping:

- Provisioning and store failures must reach the user as plain-language messages through the
  existing callback port, not as a raw `SqlException` or `SQLiteException` string. A failed
  bootstrap that says *"Login failed for user 'MyMoneyAdmin'"* is a support ticket; the tiering
  model makes several such failures newly reachable.
- `ReportModel`'s cells must carry formatting *intent*, not presentation. The whole point of the
  eventual Fluent/WPF-UI restyle is that it shouldn't require touching a single report class.

**Expertise the panel does not have — named, not faked:**

1. **Whether "Microsoft Print to PDF" behaves under an unattended automated session.** D-17's
   decision rests on it, and the Operations Engineer already flagged that printing touches the
   spooler and driver stack — the least predictable part of Windows — and that a headless/agent
   machine may have no print queue at all. This needs an empirical spike (does the driver exist,
   does it prompt for an output path, does it throw with no default printer), not a panel
   judgment. Given this project's standing requirement that tests run fully unattended, resolve it
   *before* committing Stage 3 to a print-dependent verification path — the Markdown-string
   assertions are unaffected either way, which is precisely why D-17's intermediate representation
   was the right call.

2. **A hardened-SQL-Server-policy review of "Admin deploys procs that perform DDL."** The Data
   Engine Expert can design the proc family, but whether a real least-privilege server policy
   permits `MyMoneyAdmin` to do it without an effectively-`db_owner` grant is a DBA question. On a
   developer-only engine (D-37) the blast radius is small, so this does not block the design — but
   it should not be written up as a security property until someone who does this for a living has
   looked at it. **Revision 2 raises the stakes slightly**: §1.5 makes proc-executed DDL the *only*
   schema mechanism rather than one of two, so a policy that forbade it would need a fallback this
   document does not currently have. Still not blocking — but worth confirming before slice 8
   rather than after.

4. **(Revision 2.) Whether `INSTEAD OF` triggers on views are a maintainable way to encode
   multi-statement write logic in SQLite at this scale.** §1.7.1 recommends them selectively and
   the mechanism is sound, but the panel has no operational experience of a codebase that leans on
   them heavily — the failure modes (debuggability, error messages, interaction with
   `defer_foreign_keys`, behavior under `RETURNING`) are known to exist and not known in detail.
   Recommendation: adopt for one concrete case first (the transfer both-sides write), evaluate,
   and only then generalize. Explicitly *not* a reason to skip §1.7's other findings, which are
   independent of this one.

3. **Nobody here can speak for the report recipient.** D-17's own panel flagged this and the owner
   resolved the format question; it stays flagged only because §3.3's `ReportModel` shape (which
   cells, which formatting intents) will eventually be judged by whoever receives a report.

---

## 8. Summary of what this document proposes

- **Split `IDatabase` three ways along the privilege axis** (`IMoneyStore` /
  `IMoneyStoreProvisioner` / `IMoneyStoreTestControl`) plus a non-store `IDataFormat`, and make
  each tier of each engine **its own assembly** (Candidate A), so privilege separation is
  "the code isn't in the build output" rather than "the code is politely hidden."
- **Keep `IAggregateRoot`/`SaveOne`/`SaveTransfer`/`SaveBatch` unchanged**; retire
  `Save(MyMoney)`; **add `IMoneyQuery`**, a closed, typed filter/aggregate port (not `IQueryable`),
  because reporting needs SQL-side filtering and subtotaling that doesn't exist today.
- **Schema management becomes one versioned mechanism with several entry points** (§1.5): an
  ordered list of immutable steps, a `__SchemaHistory` ledger with checksums, one per-step
  transaction, and `Schema_ApplyTo`/`CurrentVersion`/`Verify`/`DropAll` — real stored procs on SQL
  Server, embedded SQL plus an in-process executor on SQLite, same step list and same semantics.
  **Creating a database, nuking-and-paving, and the eventual production upgrade are the same
  operation started from different versions**, which is the owner's unifying constraint and the
  reason the upgrade path is exercised on every build. Reflection-to-DDL (`LazyCreateTables`,
  `CreateOrUpdateTable`) is deleted on both engines, which removes **issue #34** structurally —
  backed by a fresh-vs-upgraded schema-equality contract test (slice 2b) that reproduces #34 as a
  red test against today's code.
- **CRUD and batch writes stay parameterized and transactional, stated as requirements**
  (§1.6, R-CRUD-1..5) rather than carried forward implicitly, including the TVP-per-batch pattern
  and the engine-independent conflict contract.
- **New principle P-SQLITE** (§1.7): express behavior in SQLite where SQLite can express it —
  `RETURNING`, `json_each` batches, views, `INSTEAD OF` triggers, `STRICT` tables, structured
  `PRAGMA` introspection, `VACUUM INTO` backups — with an explicit, honest list of what cannot
  close (no procedures, no roles or grants, no typed TVPs) and one case where the panel pushes back
  on the principle itself (don't move the version-conflict check into a trigger).
- **Test subsystem**: `MyMoney.TestKit` as a true library (not a test project), four test tiers
  with a new Tier-0 architecture band, four layered enforcement mechanisms for the one-way
  dependency, and `RecordingStore`/`FaultInjectingStore` so the error path is testable from day
  one. **T-1 resolved (§2.4): the default business-test double is a decorated in-memory SQLite
  store; `MockStore` is not built.**
- **The real production-upgrade workflow is deferred, not foreclosed** (§1.8): named as future
  work, with the three properties today's design must hold continuously because they cannot be
  established retroactively — the ledger from the first step, the upgrade path exercised on every
  build, and a contract-tested backup.
- **Open question resolved**: control logic → business layer's application-services band;
  format parsing/writing → business layer behind `IDataFormat`; engine-native backup/duplication →
  provisioner tier; reporting → three stages (query in the data layer, `ReportModel` in the
  business layer, Markdown/CSV renderers in the business layer's `Reporting` namespace), with
  `IReportWriter` deleted rather than ported.
- **First slice**: provision-then-add-an-Account on SQLite, with the schema ledger, the
  fresh-vs-upgraded equality test, the test subsystem and the Tier-0 boundary tests landing
  alongside it, then the same slice on SQL Server to prove the tiering *and the schema mechanism*
  map twice.
- **Still the owner's to decide**: §4's seven items, re-scored in §4.1 (two reframed by today's
  guidance, two strengthened with recommendations, two untouched) plus a new #8 — *what event ends
  the nuke-and-pave phase?* The panel wants the trigger named, not the date.
