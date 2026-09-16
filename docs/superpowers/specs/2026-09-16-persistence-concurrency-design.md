# Transactional, Concurrency-Safe Persistence for MyMoney

## Background

Issue #27 (found during #22's final review) discovered that `SqlServerStoredProcDatabase`'s
`Save()` path is not atomic: every `UpdateXxx` override opens its own `SqlConnection` and never
enlists in the ambient transaction `SqlDatabase.Save(MyMoney)` begins, so a partial failure mid-save
leaves whatever succeeded committed, with no rollback, and in-memory dirty-tracking state diverging
from the database. Investigating further surfaced that `SqliteDatabase` has the same root problem in
a different guise: `Save()`'s `this.transaction = tran as SqlTransaction` cast always fails for a
`SQLiteTransaction`, so the private transaction field is silently `null`; Sqlite's CRUD only "works"
today because `System.Data.SQLite` auto-enlists commands into whichever transaction is active on
their connection when the command's own `Transaction` property is unset — an undocumented reliance
on one ADO provider's leniency, not a guarantee this codebase asserts anywhere.

Pulling on that thread reframed the problem twice:

1. **Atomicity alone is not the goal.** The actual goal is letting more than one writer — a second
   household member on another laptop, or an increasingly autonomous AI agent (e.g. downloading and
   reconciling bank transactions) — safely read and write the same backing data at the same time as
   an interactive user, without silent data loss. Atomicity of a single `Save()` call is a
   prerequisite for that, not a substitute for it.
2. **The existing "load everything once, hold it, flush whatever's dirty on demand" architecture is
   the actual obstacle**, independent of which database engine is underneath. Every write path in the
   app — manual register entry, QIF/OFX/CSV import, reconciliation, category/payee/security merge,
   recategorize — funnels through exactly one production entry point
   (`Money.Save(IDatabase)` → `database.Save(this)`), triggered only by a handful of UI actions
   (explicit Save, file switch, a "pending changes" flyout the user has to notice and click). There
   is no autosave timer anywhere in the codebase. This means an arbitrary, unbounded amount of unrelated
   dirty state can accumulate before ever reaching the database, which is both a durability risk and
   the reason two writers' changes are likely to collide: the longer an edit sits dirty in memory, the
   larger the window for someone else to have touched the same row.

This document specs the redesign that follows from both reframings.

## Goals

Three layers, all in scope for this effort:

- **Layer 0 — Atomicity.** Every write to the database, regardless of how large or small, is
  all-or-nothing on every supported flavor.
- **Layer 1 — Conflict detection.** Two writers touching the same row is *detected*, not silently
  resolved by last-write-wins.
- **Layer 2 — Staleness/refresh.** A long-lived session has a way to learn that data changed
  underneath it, instead of only finding out on restart.

Two supported backends going forward:

- **SQLite** — in-process, single machine. May reasonably extend to "this machine's interactive
  session + a locally-running agent process," but is explicitly **not** a supported path for two
  separate machines (SQLite's locking model assumes a local filesystem with correctly-implemented
  lock primitives; network filesystems/cloud-sync folders are documented as unreliable for this,
  independent of any application-level fix).
- **SQL Server** — the only supported backend for anything crossing a machine boundary (a second
  laptop, or a remote/cloud-hosted agent).

## Non-goals

- Live, real-time collaborative UI sync (e.g., seeing a second user's cursor, or changes appearing
  without any user action). Layer 2 is a refresh mechanism, not a live-sync one — see Approach below.
- Reviving `XmlStore` or `SqlCeDatabase` as supported live backing stores. Both are explicitly
  **out of scope, revisit later**: `XmlStore` is architecturally incompatible with frequent small
  commits (no way to update one row without rewriting the whole file) and `SqlCeDatabase` shares
  `SqliteDatabase`'s broken transaction-cast pattern with no plan reviewed here to fix it.
  `CsvStore` is unaffected by any of this — it's a write-only, one-way export
  (`CsvStore.Load()` throws `NotImplementedException`), never a live backing store, and needs no
  changes.
- Enabling nullable reference types (issue #18). Unrelated to this effort; empirically checked
  during scoping (`<Nullable>enable</Nullable>` on `MyMoney.Business` alone produced 1,586 warnings)
  and confirmed to be its own multi-week effort, not something to fold in here.
- Designing the UI/UX for a surfaced conflict (a reload prompt, a merge dialog, etc.). This spec's
  job is to make conflicts detectable and throw a typed exception; what the UI does with that is
  future work.
- `Source/Uno`/`Source/Xamarin` thin clients (issue #21) — separate initiative, unaffected.

## Current-state findings

Full detail lives in this session's history; the load-bearing facts:

- **Single write entry point.** No business or UI code calls per-entity methods
  (`UpdateAccounts`, `UpdateTransactions`, etc.) directly — confirmed by grep across
  `Source/WPF` outside test projects. Every write goes through
  `Money.Save(IDatabase)` (`MyMoney.Business/Money.cs:1050`) → `database.Save(this)`.
- **`SqlServerDatabase.Save(MyMoney)`** (`MyMoney.Data/SqlDatabase.cs:595`) is not `virtual`,
  opens one ambient transaction, and calls all 13 `UpdateXxx` methods. Its own `UpdateXxx`
  implementations correctly enlist via `ConnectSqlServer()` + `this.transaction`.
- **`SqlServerStoredProcDatabase`** overrides effectively every `UpdateXxx`/`ReadXxx` to call
  stored procedures, but each override opens a brand-new `SqlConnection` and never touches
  `this.transaction` (issue #27). Stored procs themselves are single-statement, no `BEGIN TRAN`.
  A loop over many rows within one `UpdateXxx` call auto-commits each row independently — not
  atomic even within one entity type's batch.
- **`SqliteDatabase`** overrides `Connect()` only, inherits the non-virtual `Save()`, and its
  `this.transaction = tran as SqlTransaction` cast always fails silently. No `journal_mode` or
  `busy_timeout` pragma is set anywhere — default rollback-journal mode, not tuned for any
  contention at all.
- **No workflow saves incrementally today.** Grepped every importer under `Importers/` for calls
  to `Save` — none exist. A `ChangeTracker.IsDirty` flag drives a "pending changes" UI flyout the
  user must click; there is no autosave timer.
- **Commit-boundary mapping** (from mapping every dialog/view/importer that mutates persistent
  entities):

  | Group | Shape | Examples | Natural commit boundary |
  |---|---|---|---|
  | 1 | One aggregate root | Account/Category/Payee/Security/Currency/Alias/Loan dialogs; register grid row edit; Transaction+its Splits/Investment | Dialog OK / `DataGrid.RowEditEnding` — already fires today, just does no persistence |
  | 2 | Two peer roots, must co-commit | Transfer creation (`TransactionsView.xaml.cs:4292 TransformTwoTransactionIntoTransfer`) | The method that links both `Transaction.Transfer` fields |
  | 3 | Independent, unbounded stream | QIF/OFX/CSV import — each importer creates one `Transaction` per external record, decided independently | Once per imported record, not once for the whole file |
  | 4 | Bounded batch, one logical operation | Reconciliation (`BalanceControl.xaml.cs:608 Done_Click`, UI's own words: "commit this set of reconciled transactions"); `Payee.Merge`/`Security.Merge`/category merge; Recategorize (`TransactionsView.xaml.cs:4024`, already wrapped in `BeginUpdate`/`EndUpdate` — an in-memory-only boundary today) | The method/click that already exists as the "this is one unit" moment |
  | 5 | Not a SQL-transaction problem | Attachments (`AttachmentManager.cs` — filesystem writes, `File.Copy`/`File.Move`); credential dialogs | Out of scope for this spec |

## Requirements

### R1 — Retire `IDatabase.Save(MyMoney)`

No implementation of `IDatabase` keeps a whole-graph `Save(MyMoney money)` method. Every current
caller is replaced:

- Ongoing editing (manual entry, transfers, imports, reconciliation, merges, recategorize) moves to
  R2's new primitives, called at the commit boundaries already identified above — not accumulated
  and flushed later.
- **Database duplication** ("Save As", or any future "save a copy") is a storage-engine-native
  operation, entirely outside `IDatabase`'s write API:
  - SQLite: close the connection, `File.Copy()` the `.db` file to the new path, open the copy as a
    fresh `SqliteDatabase`. Valid because SQLite fully supports file-copy duplication once the
    source connection is closed (no mid-write state to corrupt).
  - SQL Server: `BACKUP DATABASE ... TO DISK` + `RESTORE DATABASE <newname> FROM DISK ...` via
    T-SQL (issued from the admin-tier connection, matching the existing `MyMoneyAdmin` bootstrap
    pattern), then connect to the restored database.
  - `this.myMoney.MarkAllNew()` (currently used to force a full rewrite in `SaveNewDatabase`) is
    deleted along with the code path that needed it.
- **First-time population of a brand-new, empty database** (`SampleDataGenerator.Create()`, or any
  future "generate then persist" flow) has no existing file/database to duplicate from, so it makes
  exactly one call to `SaveBatch` (R2) with every generated object, immediately after generation
  completes. This is the one legitimate remaining "big batch" call in the system, and it's fine
  specifically because the target has no other writers by construction (nothing else could be
  connected to a database that didn't exist a moment ago).
- `OpenDatabase()`'s `SaveIfDirty()` call is deleted. Once every edit commits at its own boundary,
  `myMoney`'s dirty state is always empty by the time the user chooses to open a different database
  — switching files becomes close-old-connection, open-new-connection, reload, with no bulk write at
  all.

### R2 — New `IDatabase` write primitives

Three methods, replacing the retired `Save(MyMoney)`, framed around aggregate roots rather than one
method per entity type:

```csharp
void SaveOne<T>(T root) where T : PersistentObject;
void SaveTransfer(Transaction from, Transaction to);
void SaveBatch(IEnumerable<PersistentObject> roots);
```

- **`SaveOne<T>`** — Group 1's shape. Covers every single-entity dialog/grid-row
  (`Account`, `Category`, `Payee`, `Security`, `Currency`, `Alias`, `Loan`, `RentBuilding`,
  `RentUnit`, `StockSplit`, `OnlineAccount`) and `Transaction` together with its owned `Splits`/
  `Investment` — a `Transaction`'s splits are part of its aggregate, not peers, so `SaveOne<Transaction>`
  is responsible for committing both the transaction row and its splits/investment as one unit.
  Group 3 (QIF/OFX/CSV import) needs no new method: each importer calls `SaveOne<Transaction>` once
  per record inside its existing read loop, instead of accumulating and never saving at all.
  `Split`/`Investment` are not valid `T` for `SaveOne` on their own — they have no independent
  commit boundary, only their owning `Transaction` does — so `SaveOne<T>`'s generic constraint is
  narrowed to aggregate-root types specifically (a small marker interface or explicit type list,
  decided at implementation time), not merely `PersistentObject`, to make this a compile-time error
  rather than a runtime one.
- **`SaveTransfer`** — Group 2's shape, kept as its own named method rather than folded into
  `SaveBatch`, because it's a real, distinct shape (exactly two peer aggregates that must co-commit,
  not an arbitrary N). Maps directly onto `TransformTwoTransactionIntoTransfer`'s existing logic.
- **`SaveBatch`** — Group 4's shape (reconciliation's `Done_Click`, the `Merge` methods,
  `Recategorize`'s `BeginUpdate`/`EndUpdate` loop) and the one legitimate bulk case from R1
  (first-time population of a brand-new database). Heterogeneous `PersistentObject` roots, all
  committed together or not at all.

All three live directly on `IDatabase` (not a separate capability interface), so every
implementation has one uniform contract:

- `SqliteDatabase` and `SqlServerDatabase`/`SqlServerStoredProcDatabase` implement real,
  minimal-duration atomic transactions per call (R3).
- `MockDatabase` implements them as straightforward in-memory mutations (no transaction semantics
  needed for a test double).

### R3 — Atomicity, per flavor

Each of the three primitives above must be genuinely atomic on both supported flavors: everything
the call touches commits together, or none of it does, and a failure leaves in-memory dirty-tracking
state (`IsChanged`/`IsInserted`/`IsDeleted`, cleared today via `OnUpdated()`/`RemoveDeleted()`) exactly
matching what's actually in the database — never cleared for anything that didn't actually commit.

- **SQL Server**: one `SqlConnection`, one `SqlTransaction`, spanning every stored-proc call the
  primitive needs (e.g. `SaveOne<Transaction>` wraps the transaction row's own insert/update/delete
  plus every split row's, in one transaction). This directly closes issue #27 — no more
  connection-per-`UpdateXxx`-call, no more an ambient transaction wrapping nothing.
- **SQLite**: `PRAGMA journal_mode=WAL` (readers never block writers, writers never block readers)
  and a configured `busy_timeout` (so a second writer retries instead of failing immediately on
  `SQLITE_BUSY`) — neither is set anywhere today. One writer at a time is still enforced by SQLite
  itself; short, well-scoped transactions (which R2's per-aggregate/per-batch primitives naturally
  produce, versus today's unbounded whole-graph flush) minimize how long that lock is held, which
  matters more here than under SQL Server since it directly determines how long a second writer
  (e.g., a same-machine local agent) is blocked.
- Because each primitive's scope is now small and enumerable (one aggregate, two transactions, or
  an explicit batch — never "whatever happens to be dirty"), the atomic unit each transaction must
  cover is well-defined at the call site, not an open-ended "everything in the schema."

### R4 — Conflict detection (Layer 1)

*(Proposed design — this specific mechanism was not walked through turn-by-turn in the design
conversation; confirm or adjust in review.)*

Add a version column to every table `SaveOne`/`SaveTransfer`/`SaveBatch` can write, and check it on
every update/delete:

- **SQL Server**: native `ROWVERSION` (`TIMESTAMP`) column — engine-maintained, auto-incrementing
  `binary(8)`, no application logic needed to bump it. Every `_Update`/`_Delete` stored proc gains a
  `WHERE Id = @Id AND RowVersion = @ExpectedRowVersion` clause (add this in the same pass as #24's
  missing FKs/indexes, since both touch schema generation and stored-proc definitions together).
- **SQLite**: no native rowversion type; an application-maintained `INTEGER Version` column,
  incremented by the application (`UPDATE ... SET Version = Version + 1 ... WHERE Id = @Id AND
  Version = @ExpectedVersion`), same zero-rows-affected-means-conflict pattern.
- `PersistentObject` gains a `Version`/`RowVersion` property, populated on load, checked on save.
- When an update/delete affects zero rows where one was expected, the primitive throws a new,
  specific `ConcurrencyConflictException` rather than silently proceeding or swallowing the
  mismatch. Deciding what a caller *does* with that exception (reload, merge UI, retry) is
  explicitly deferred (see Non-goals) — this requirement is only that the conflict is detectable and
  surfaced.
- This item folds in issue #24 (missing FKs/secondary indexes): the version-column work already
  touches every table's schema generation and every `_Update`/`_Delete` proc, so adding the missing
  FKs and indexes (needed for correctness and for the indexed lookups this check relies on) rides
  along in the same pass rather than a separate one.

### R5 — Staleness / refresh (Layer 2)

*(Proposed design — pull-based, not push-based; this specific choice was not discussed turn-by-turn
in the design conversation; confirm or adjust in review.)*

A long-lived session needs a way to learn that rows changed underneath it. Given the actual target
scenarios (a spouse on another laptop via SQL Server; a locally-running agent on the same machine via
SQLite or SQL Server) don't require live, sub-second sync, and given the infrastructure cost of a true
push mechanism (SignalR, `SqlDependency` with its broker-configuration fragility, file-system
watchers) is disproportionate to that need:

- **Pull-based refresh**, triggered by the UI (on window focus, an explicit "Refresh" command, or a
  periodic timer at a deliberately coarse interval — not sub-second) rather than a live push
  notification.
- Reuses R4's version column as the change-marker: a cheap `SELECT MAX(Version)`/`MAX(RowVersion)`
  per table (or one watermark table) answers "has anything changed at all" before doing any real
  work; only tables whose watermark advanced get a real `WHERE Version > @LastSeenVersion` pull.
- New rows/changes pulled this way get merged into the live `MyMoney` graph without disturbing the
  session's own in-flight, uncommitted edits (an edit in progress in the register grid is not yet
  dirty-tracked as "committed" until its own `SaveOne` fires, so a concurrent refresh pulling in
  someone else's rows doesn't collide with it at the object-model level — collision at the *row*
  level, if the same row was independently edited by both sides, is R4's job to catch, not R5's).
- This is the layer issue #7 (`UiDispatcher`'s live `System.Windows.Threading.Dispatcher` dependency
  in `PersistentObject`/`PersistentContainer`'s change-notification plumbing) becomes directly
  relevant: a background-thread-originated refresh pushing changes into a live UI-bound graph is
  exactly the scenario that plumbing has never been stress-tested for. This spec's implementation
  must explicitly decide — not defer again — whether to rewrite `UiDispatcher` onto
  `SynchronizationContext` first (architecturally cleaner, `WindowsBase`-free) or proceed on the
  existing `Dispatcher`-based plumbing with the risk named in writing. Recommendation: rewrite first,
  given R5 is the first real stress test this plumbing will face and a wrong assumption here risks
  silent UI staleness or a threading deadlock in the code that tracks every change to the user's
  real financial data.

### R6 — Test infrastructure prerequisites

Not part of this design, but must land first so this effort's own verification is trustworthy:

- **#28** — tag `SqlServerDatabaseContractTests` with an NUnit category so it's actually discoverable
  via CI/the documented `dotnet test` command, instead of silently never running.
- **#25** — serialize (or otherwise stop racing) `SqlServerStoredProcDatabaseTests` and
  `SqlServerDatabaseContractTests` against the shared live SQL Server, so this effort's new
  atomicity/conflict-detection tests aren't flaky from day one.

### R7 — Folds into issue #1

Closes out issue #1's remaining item #3 slices:

- **#19** (narrowed to "SQL Server as third `IDatabase` contract-test flavor") rides along with this
  effort's own SQL Server contract-test extensions (needed here anyway to verify R3/R4).
- **#7** is resolved by R5's explicit decision (rewrite `UiDispatcher`, or document the risk),
  not deferred again.
- Issue #1 can close once this plan lands.
- **#31** (the larger self-verifying-test-data/seed-derived-canary/end-to-end FlaUI parity scope,
  split out of #19) remains independently scoped and does **not** block this effort or issue #1.

## Testing / verification strategy

- **Atomicity (R3)**: inject a failure partway through a multi-row `SaveBatch`/multi-table
  `SaveOne<Transaction>` call (e.g., a bad row N of M) and assert (a) nothing committed, (b) in-memory
  dirty flags are unchanged so the same call can be safely retried. Run against both flavors.
- **Conflict detection (R4)**: two `IDatabase` handles pointed at the same row, one updates it, the
  other's write attempt on the stale version must throw `ConcurrencyConflictException`, not silently
  succeed.
- **Concurrency under load (SQLite specifically)**: two writers issuing overlapping `SaveOne`/
  `SaveBatch` calls against one WAL-mode file, asserting both eventually succeed (no unbounded
  `SQLITE_BUSY` failures) and no corruption.
- **Contract-test extension**: the shared `IDatabase` contract suite (`MockDatabase`/
  `SqliteDatabase` today) gains the new `SaveOne`/`SaveTransfer`/`SaveBatch` methods, run against all
  three flavors including SQL Server (R7/#19).
- Full solution build + `dotnet test Source/WPF/MyMoney.sln` green (accounting for the pre-existing,
  unrelated `ScenarioTest`/FlaUI flakes already tracked in #26) before and after, per this repo's
  usual verification bar.

## Decisions made while drafting (flag if you disagree)

- R4/R5's concrete mechanisms (rowversion-style optimistic concurrency; pull-based rather than
  push-based refresh) are reasonable, well-justified defaults consistent with everything else agreed
  in the design conversation, but were not walked through explicitly turn-by-turn the way R1–R3 were.
- R5 recommends rewriting `UiDispatcher` onto `SynchronizationContext` (resolving #7) rather than
  proceeding on the existing `Dispatcher` plumbing with documented risk — the conversation identified
  #7 as relevant and in-scope but didn't settle which of its two options to take.
