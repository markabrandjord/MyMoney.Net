# Phase 2c: SQL Server `SaveOne`/`SaveBatch`/`SaveTransfer` design

## Background

Phase 2a (PR #37) established the `IDatabase.SaveOne<T>`/`SaveTransfer`/`SaveBatch` contract,
`IAggregateRoot`, and `ConcurrencyConflictException`, with a conflict-detecting `MockDatabase`
reference implementation. Phase 2b (PR #38) plus its entity-extension (PR #39), RentBuilding
(PR #40), and Transaction/SaveTransfer (PR #43) follow-ups gave `SqliteDatabase` a real
implementation covering every `IAggregateRoot` type. Test consolidation (PR #44) moved the
~60-test `SaveOne`/`SaveBatch`/`SaveTransfer` suite into the shared `DatabaseContractTests` base
class, so `MockDatabaseContractTests`, `SqliteDatabaseContractTests`, and
`SqlServerDatabaseContractTests` all inherit and run the identical suite — closing issue #19.

Running that suite against `SqlServerStoredProcDatabase` (via `SqlServerDatabaseContractTests`)
confirmed live against the shared Redmond test database that it has zero `SaveOne`/`SaveBatch`
implementation at all: it inherits the Phase 2a `NotImplementedException` stub from the shared
`SqlServerDatabase` base class (`SqlDatabase.cs`). The 7 whole-graph `Save`/`Load` tests pass
(that path already works); the other ~54 fail. This is Phase 2c: give
`SqlServerStoredProcDatabase` a real implementation that makes the exact same shared suite pass,
with no changes to the suite itself.

## Why this isn't just "port the SQLite implementation"

`SqlServerStoredProcDatabase` is architecturally unlike `SqliteDatabase` in a way that changes the
whole shape of the work, not just its SQL dialect:

- **Stored-procedure-only access.** Per issue #22/#23's tiered-security design, `MyMoneyUser`/
  `MyMoneyTest` have `EXECUTE` grants only — no direct table access at all. Every read/write in
  `SqlServerStoredProcDatabase` already goes through a named stored procedure
  (`Source/WPF/MyMoney.Data/SqlScripts/Access/*_AccessProcs.sql`); there is no raw parameterized
  SQL anywhere in that class, unlike `SqliteDatabase`'s direct SQL strings. Any new write path has
  to be new stored procedures, not new C#-side SQL text.
- **No version-tracking column exists anywhere in the live schema.** Confirmed live against the
  Redmond database: `SELECT ... FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME LIKE '%ersion%'`
  returns zero rows, on any table. This phase includes a schema migration, not just new procs.
- **SQL Server's native `ROWVERSION`/`TIMESTAMP` type is the wrong mechanism here, correcting this
  design's own earlier proposal.** The original persistence-concurrency design spec (R4) proposed
  SQL Server use its native `ROWVERSION` column — engine-maintained, auto-incrementing
  `binary(8)` — explicitly flagged there as "proposed, not walked through, confirm or adjust in
  review." It doesn't work for this contract: native `ROWVERSION` is a single counter shared by
  the *entire database*, not a per-row counter, so it never resets to 1 for a fresh row. Confirmed
  live: `SELECT @@DBTS` against Redmond returned `0x0000000000000B32` (2866) purely from ordinary
  test activity, on a database with zero `ROWVERSION` columns anywhere yet — the first row ever
  inserted with one would get roughly 2867, not 1. The now-shared contract suite has 11 tests
  (`SaveOne_NewX_PersistsAndSetsRowVersionToOne`, one per aggregate root, already passing
  identically against Mock and SQLite) that assert exactly `RowVersion == 1` for every freshly
  inserted row regardless of what else exists in the database. Native `ROWVERSION` cannot satisfy
  that and native `ROWVERSION`'s usual benefits (atomic `WHERE`-clause compare-and-set, no locking
  overhead) come from the *pattern* (`UPDATE ... WHERE Id=@Id AND Version=@Expected`, checked via
  rowcount), not from the column being engine-maintained — an application-maintained column gets
  identical atomicity and locking behavior. **Decision: an application-maintained `BIGINT Version`
  column, exactly mirroring what `SqliteDatabase` already does**, not SQL Server's native type.
  SQL Server's native Change Tracking feature (`CHANGETABLE`/`CHANGE_TRACKING_CURRENT_VERSION`) was
  also considered and is a good fit for a *different*, not-yet-started problem — R5's pull-based
  staleness/refresh ("what changed since I last looked") — but doesn't give an atomic single-row
  compare-and-set for this problem (its version data lives in system-maintained side tables, not a
  column usable in an `UPDATE`'s `WHERE` clause), so a separate read-then-check-then-write would
  reopen the exact race window `ROWVERSION`/an app column exists to close. Noted under "What's next"
  below for whenever R5 is picked up.

## Design

### Schema migration (prerequisite)

`ALTER TABLE <Table> ADD Version BIGINT NOT NULL DEFAULT 1` for all 11 `IAggregateRoot` tables:
`Categories`, `Currencies`, `OnlineAccounts`, `Accounts`, `Payees`, `Aliases`, `Securities`,
`StockSplits`, `LoanPayments`, `RentBuildings`, `Transactions`. Metadata-only change on modern SQL
Server given a constant default — no full table rewrite. `Splits`, `Investments`, `RentUnits`
(owned children, not `IAggregateRoot`) get no `Version` column of their own, matching the design
spec's R2 and `SqliteDatabase`'s existing behavior: gated only by their parent's version.

Run once, by hand, as `MyMoneyAdmin` against Redmond — same convention every existing
`*_AccessProcs.sql` file already documents ("Run as the MyMoneyAdmin login"), not something app
code runs implicitly. Purely additive: existing procs, the old whole-graph `Save`/`Load` path, and
every other consumer of these tables are unaffected by an extra column with a default.

### TVP-driven batch procs, not per-row proc calls

Rather than one stored-proc call per row (which is what a direct port of `SqliteDatabase`'s
per-root imperative loop would look like), each entity gets **one stored procedure that takes a
whole batch as a Table-Valued Parameter** and does the insert/update/delete/version-check as
set-based T-SQL inside its own transaction:

- One new SQL Server User-Defined Table Type per entity, e.g.:
  ```sql
  CREATE TYPE dbo.CategoryVersionedRow AS TABLE (
      Action CHAR(1) NOT NULL,  -- 'I', 'U', or 'D'
      Id INT NOT NULL,
      Name NVARCHAR(80) NULL, Description NVARCHAR(255) NULL, Type INT NULL, ParentId INT NULL,
      Budget MONEY NULL, Frequency INT NULL, Balance MONEY NULL, Color NVARCHAR(10) NULL,
      TaxRefNum INT NULL,
      ExpectedVersion BIGINT NULL
  );
  ```
- One new proc per entity, e.g. `dbo.Categories_SaveBatch(@Rows dbo.CategoryVersionedRow READONLY)`:
  `SET XACT_ABORT ON`, `BEGIN TRANSACTION`, a single set-based conflict check (`SELECT` every
  `'U'`/`'D'` row whose live `Version` doesn't match `ExpectedVersion`, including rows that no
  longer exist at all — `StoredVersion = -1` sentinel, matching `SqliteDatabase.ThrowConflict`'s
  convention), and if any conflicts: return them and `ROLLBACK`. Otherwise: three set-based
  statements (`INSERT ... SELECT ... WHERE Action='I'`, `UPDATE ... FROM ... JOIN @Rows ... WHERE
  Action='U'`, `DELETE ... FROM ... JOIN @Rows ... WHERE Action='D'`), `COMMIT`, then return the
  new `Version` for every surviving inserted/updated row.
- This is a new, additive proc family (`_SaveBatch` suffix) alongside the untouched existing
  `_Insert`/`_Update`/`_Delete`/`_SelectAll` procs used by the old whole-graph `Save(MyMoney)` path
  — not a replacement or extension of that split. `GRANT EXECUTE ON TYPE::dbo.CategoryVersionedRow
  TO MyMoneyUser` (and `MyMoneyTest`) is required alongside the usual proc `GRANT EXECUTE`.
- Existing `_SelectAll` procs gain the `Version` column in their `SELECT` list in place (purely
  additive — existing callers get an extra column they already ignore); the corresponding `ReadXxx`
  C# methods are extended to hydrate `.RowVersion` from it, the same way `SqliteDatabase`'s
  `ReadCategories` override already does (that override's own comment anticipated needing a
  "`ROWVERSION`'s binary(8)-to-long conversion" for Phase 2c — since Phase 2c uses a plain
  `BIGINT` instead, no such conversion is actually needed; that comment gets corrected as part of
  this phase's implementation).

C# becomes much thinner as a result: `SaveBatch(roots)` groups the incoming roots by concrete
type (every real caller today — `SaveOne`, `SaveTransfer`, and the one same-type batch test —
sends a single type per call). For the common single-type-group case, one `SqlCommand` with one
`SqlDbType.Structured` parameter (a `DataTable` shaped like the TVP), one `ExecuteReader()` call,
no explicit `SqlTransaction` object needed at all — the proc owns atomicity server-side. A
`SqlTransaction` wrapping multiple proc calls is only needed for the rare, currently-untested case
of a single `SaveBatch` call mixing genuinely different entity types (contractually legal per
`IDatabase.SaveBatch(IEnumerable<PersistentObject>)`'s signature, even though nothing exercises it
today — see "New test coverage" below). If a batch contains any type without a `_SaveBatch` proc
yet, the whole call delegates to `base.SaveBatch` (the Phase 2a stub) rather than partially
committing some roots — matching `SqliteDatabase`'s existing guard, mostly dead code once every
entity is covered but kept for forward safety with any future new `IAggregateRoot` type.

### Owned children: Transaction↔Splits/Investment, RentBuilding↔RentUnit

Each parent's batch proc takes additional TVPs for its owned children, populated independently of
the parent's own dirty state (matching `SaveOne_TransactionWithSplitOnlyEdit_
PersistsSplitEvenThoughTransactionItselfIsUnchanged`, which requires processing a changed child
even when the parent has nothing to do):

- `dbo.Transactions_SaveBatch(@Transactions dbo.TransactionVersionedRow READONLY, @Splits
  dbo.SplitRow READONLY, @Investments dbo.InvestmentRow READONLY)`.
- `dbo.RentBuildings_SaveBatch(@Buildings dbo.RentBuildingVersionedRow READONLY, @Units
  dbo.RentUnitRow READONLY)`.
- Child TVP rows carry no `ExpectedVersion` (children aren't `IAggregateRoot`) and get no conflict
  check — written unconditionally alongside the parent's version-gated write.
- Delete ordering: children deleted before the parent row (no `ON DELETE CASCADE` exists on
  `Splits.Transaction`/`Investments.Id`/`RentUnits.Building` today — if it did, `SqliteDatabase`'s
  existing explicit ordering wouldn't matter, so there isn't one), matching `UpdateTransactions`'s
  already-documented FK-ordering rule.

### Transfer's self-referencing FK

`Transaction.Id` is application-assigned (a client-side `nextTransaction` counter), not a SQL
Server `IDENTITY`, so both sides of a new transfer already have their final `Id` values before
either row is inserted. Rather than depend on an unverified assumption about whether SQL Server
validates a self-referencing FK per-row or per-statement within one multi-row `INSERT`, the new
rows are inserted with `Transfer = NULL` first (always FK-valid), then a single follow-up `UPDATE
... FROM @Transactions r WHERE r.Transfer IS NOT NULL` sets the real cross-reference on both rows
once both already exist. No `PRAGMA defer_foreign_keys`-equivalent needed.

### Error handling

The conflict-check `SELECT` inside each `_SaveBatch` proc returns `(Id, StoredVersion,
CallerVersion)` for every conflicting row — including the `StoredVersion = -1` sentinel for a row
deleted entirely by someone else. C# reads that result set (if non-empty) and throws
`ConcurrencyConflictException` for the first conflicting row. Unlike `SqliteDatabase.ThrowConflict`,
no second round-trip is needed to learn the real stored version — the version check already runs
inside the same call, before anything commits.

### New test coverage

The existing shared `DatabaseContractTests` suite needs no changes — it already fully specifies
the required behavior and is already wired to `SqlServerDatabaseContractTests`. One real gap the
TVP design surfaces: a genuinely mixed-entity-type `SaveBatch` call (e.g. one `Category` + one
`Account` together) is contractually legal but has zero coverage today, and is exactly the case
needing the extra client-side transaction across multiple type-specific proc calls. Add this test
to the *shared* base (benefits Mock/SQLite too) — already flagged as a known gap in prior-phase
notes ("mixed insert/update/delete batch" under known deferred items).

### Test-support stored procs

`SqlServerTestDatabase.WipeAllTables()` currently does a raw `DELETE FROM` against every table
using the `MyMoneyAdmin` connection, with its own doc comment flagging this as a known short-term
gap ("a tiered-security-appropriate test-support stored proc, not this admin-connection raw
DELETE"). Only `Payees` has the real fix today (`Payees_Test_Reset`, granted to `MyMoneyTest`
only — `SqlScripts/Test/Payees_TestProcs.sql`). Since this phase already touches every one of
these 11 tables' schema and procs, add the matching `<Table>_Test_Reset` proc for the other 10 and
switch `WipeAllTables()` to call them instead of the raw admin `DELETE` — closing that gap as a
natural ride-along, the same way R4 originally folded in issue #24's FK/index fix.

### Where the new SQL lives

Each entity's existing `*_AccessProcs.sql` file is extended in place (the `ALTER TABLE`, new
`TYPE`, new `_SaveBatch` proc, and its `GRANT`s appended; `CREATE OR ALTER` plus an `IF NOT
EXISTS` guard on the column add keeps it idempotent) — one file stays the single source of truth
for an entity's whole stored-proc surface. New `_Test_Reset` procs go into `SqlScripts/Test/`, one
file per entity, matching `Payees_TestProcs.sql`'s existing granularity.

## Scope

Full parity in one plan: all 11 `IAggregateRoot` types (`Category`, `Currency`, `OnlineAccount`,
`Account`, `Payee`, `Alias`, `Security`, `StockSplit`, `LoanPayment`, `RentBuilding`,
`Transaction`), plus owned-child handling for `Splits`/`Investment`/`RentUnit`, plus
`SaveTransfer`. (Considered scoping to `Category` only first, matching Phase 2b's own original
scoping — explicitly declined in favor of full parity in one plan.)

## What's next (not this phase)

- R5 (pull-based staleness/refresh) — SQL Server's native Change Tracking
  (`CHANGE_TRACKING_CURRENT_VERSION()`/`CHANGETABLE(CHANGES ...)`) is the recommended mechanism
  when this is picked up, better than the original spec's "`SELECT MAX(Version)`" idea, though
  still a different problem from this phase's write-time conflict detection.
- A real multi-connection SQLite contention test (still open from Phase 2b's own "what's next").
- The detached-root `InvalidOperationException` path and other untested `SaveBatch` edge cases
  already flagged in prior-phase notes remain deferred, unrelated to this phase's scope.
