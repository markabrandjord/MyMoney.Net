# File|New / File|Open: Database Registry & Engine-Aware Dialogs — Design Spec

## Status

Design phase, implements issue #32. Written through an extended interactive
brainstorming conversation (2026-09-17 evening) — the shape below reflects
several real corrections the user made mid-conversation (notably: eliminating
`MyMoneyAdmin.exe` entirely in favor of in-process bootstrap via permanent,
parameterized stored procedures, and scoping the SQLite-side role-enforcement
facade out to issue #41). Treat this document, not the original issue #32
text, as authoritative for anything the two disagree on.

## Context

Two pieces of existing infrastructure this spec builds on and replaces parts
of:

- **The tiered `sa`→`MyMoneyAdmin`→`MyMoneyUser`/`MyMoneyTest` SQL Server
  bootstrap already exists and works** (`docs/superpowers/specs/
  2026-09-14-sql-server-dual-engine-design.md`), but is entirely automatic
  and invisible: `DataEngineStartup.TryAutoLoad` reads one
  `dataengine.config.json` on app startup and, if it says `engine:
  "SqlServer"`, shells out to a separate `MyMoneyAdmin.exe` process
  (`Source/WPF/MyMoneyAdmin`) to bootstrap if needed. The database name is
  hardcoded to the literal string `"MyMoney"` throughout (`DataEngineStartup
  .RequiredDatabaseName`, and every `USE MyMoney;` at the top of every
  `Database/SqlScripts/{Bootstrap,Schema,Access,Test}/*.sql` file). There is
  no UI for any of this — no File|New, no File|Open, no way to have more than
  one configured database.
- **`DataEngineConfig`** (non-secret: engine/server/database, one object) and
  **`DataEngineCredentialStore`** (secret: UID/PW per SQL Server login, in
  `%USERPROFILE%\.secrets\MyMoney\dataengine.credentials.json`, outside the
  repo) are two separate files, deliberately split by sensitivity. The SQL
  Server test harness (`SqlServerStoredProcDatabaseTests`,
  `SqlServerDatabaseContractTests`, via `MyMoney.TestSupport
  \SqlServerTestDatabase.WipeAllTables`) additionally depends on three
  environment variables (`MYMONEY_TEST_SQLSERVER_USER_CONNECTION`,
  `MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`,
  `MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE`) that duplicate/bypass both files.
- **`RecentFilesMenu`** (`Source/WPF/MyMoney/Utilities/RecentFilesMenu.cs`)
  assumes every entry is a filesystem path (`File.Exists()` prunes stale
  entries) and keeps its own separate list, unrelated to either JSON file
  above.
- **`CreateDatabaseDialog`** is the current File|New/Save-As/Restore dialog.
  It is entirely SQLite/XML/BinaryXML-file-path-oriented; it has no concept
  of a database engine choice at all.

This spec replaces all of the above with one JSON registry file, a pair of
engine-aware New-database dialogs, a registry-backed Open dialog, and an
in-process (no separate `.exe`) SQL Server bootstrap mechanism.

## Goals

- One JSON file is the only thing the running app reads to know what
  databases exist and how to connect to them — no environment variables
  anywhere in the startup/connection path.
- `File | New` gets **SQL Server...** and **SQLite...** submenu items,
  replacing today's engine-blind dialog. Each creates a real, immediately
  usable database and registers it.
- `File | Open` shows every registered database by name (not a file
  browser) and connects directly.
- A database can be flagged as a **test database**: physically separate
  from any production database (its own SQLite file, or its own SQL Server
  catalog), free to be wiped/reset without any risk, and invisible in
  Release builds.
- SQL Server bootstrap runs **in-process**, driven by permanent, parameterized
  stored procedures — no separate `MyMoneyAdmin.exe` process, no
  `Process.Start`, no hardcoded `"MyMoney"` database name anywhere.
- The existing SQL Server test harness resolves its target database and
  credentials from the same one JSON file, with no env vars.
- `RecentFilesMenu` becomes a thin, registry-backed view instead of its own
  separate storage.

## Non-Goals (this WI)

- **SQL Server as a Release-available engine.** Stays DEBUG-only for now
  (`#if DEBUG`), same restriction as today, even though the underlying
  mechanism (permanent parameterized stored procs, no exe) is engineered so
  that lifting this restriction later is straightforward. Noted under
  Future Work.
- **Enforcing the `DatabaseRole` contract on `SqliteDatabase`.** This spec
  introduces the `DatabaseRole` enum and uses it for SQL Server credential
  selection (where the SQL Server engine itself enforces the boundary via
  real login permissions). It does **not** implement the parallel
  `RoleGuardedSqliteDatabase` facade needed to make the same contract
  software-enforced for SQLite — see "Handoff to #41" below. Until #41
  lands, the "Test processes only touch test-tier operations" guarantee
  holds for SQL Server by real permission, and for SQLite only by
  convention (which assembly happens to call in), exactly as it does today.
- **Moving test-only operations (`ClearAllTables` etc.) into a dedicated,
  cleanly-typed test-tier business-layer API**, and the remaining 5
  `_Test_Reset` stored procs needed to fully retire
  `SqlServerTestDatabase.WipeAllTables`'s admin-connection `DELETE`
  fallback. See "Handoff to #41."
- **Migration tooling** for the current two-file dev setup. Not needed —
  there is no real data in any database in this environment right now;
  the old files are simply replaced by hand once this lands.
- **A secrets-merge mechanism for an ops-provisioned deployment.** Bootstrap
  in this spec is entirely in-process and self-generates its own
  passwords, writing them directly into the one config file — there is no
  separate secret source to merge from, and nothing a human or "operations"
  process needs to hand-enter. This only becomes relevant once SQL Server
  is Release-available and a real, externally-provisioned production
  database enters the picture (see Future Work).
- **A database-upgrade rehearsal pipeline** (backup → schema/proc
  migration → app binary update → test run) for real, non-test databases.
  Not needed while every database in this environment is `TestDatabase: true`.
  See Future Work.

## Architecture Overview

```
 WPF App
 ───────
 reads ONE file: dataengine.config.json  (DatabaseRegistry)
   │
   ├─ File | New | SQLite...      ──► create file, register entry (engine=Sqlite)
   ├─ File | New | SQL Server...  ──► in-process bootstrap (see below), register entry
   ├─ File | Open                 ──► pick a registered entry, connect
   └─ RecentFilesMenu             ──► thin view over the registry, sorted by lastUsedUtc

 In-process SQL Server bootstrap (replaces MyMoneyAdmin.exe entirely)
 ─────────────────────────────────────────────────────────────────────
 server has no entry in registry's "servers" block?
   └─ prompt for sa ONCE ──► call master.dbo.MyMoney_BootstrapServer
                              (creates MyMoneyAdmin/MyMoneyUser/MyMoneyTest
                               logins + grants MyMoneyAdmin dbcreator)
                              ──► write the 3 generated credentials into
                                  registry's servers[server] block

 creating any new catalog (production or test) on a known server:
   connect as MyMoneyAdmin (from registry) ──► call
   master.dbo.MyMoney_CreateCatalog @CatalogName ──► reconnect with
   InitialCatalog=<new catalog> ──► run every Schema/Access/Test .sql file's
   CREATE OR ALTER statements directly over that connection ──► register
   the new databases[] entry (engine=SqlServer, server, catalog, TestDatabase)
```

## Config File Schema

**One file**, `%LocalAppData%\MyMoney\dataengine.config.json` (same
directory the old `dataengine.config.json` already used), is the only thing
the app reads at runtime. A checked-in template,
`Source/WPF/MyMoney/dataengine.config.template.json`, documents the shape
with placeholder values — it is never read by the app itself, purely a
reference/starting point. The real file lives outside the repo (same as
today), is never committed, and — unlike today — contains real secrets
directly (see Goals: no separate credentials file, no merge step needed for
this WI's in-process, self-generating bootstrap flow).

```json
{
  "servers": {
    "Redmond": {
      "comment": "Home NAS, always-on, SQL Server 2022 Developer edition",
      "myMoneyAdmin": { "userId": "MyMoneyAdmin", "password": "..." },
      "myMoneyUser":  { "userId": "MyMoneyUser",  "password": "..." },
      "myMoneyTest":  { "userId": "MyMoneyTest",  "password": "..." }
    }
  },
  "databases": {
    "My Real Money": {
      "engine": "SqlServer", "server": "Redmond", "catalog": "MyMoney",
      "TestDatabase": false, "lastUsedUtc": "2026-09-17T20:00:00Z",
      "comment": "The real one — don't wipe"
    },
    "SQL Server Test": {
      "engine": "SqlServer", "server": "Redmond", "catalog": "MyMoneyTest",
      "TestDatabase": true, "lastUsedUtc": null,
      "comment": "Wiped freely by SqlServerStoredProcDatabaseTests"
    },
    "Personal.mmdb": {
      "engine": "Sqlite", "path": "C:\\Users\\...\\MyMoney\\personal.mmdb",
      "TestDatabase": false, "lastUsedUtc": "2026-09-17T19:30:00Z",
      "comment": null
    }
  }
}
```

- `servers` holds one credential set (three roles) per SQL Server host,
  shared by every `databases` entry pointing at that host. `sa` is **never**
  stored here — it is prompted for interactively the one time per server it
  is needed (unchanged from the existing accepted behavior).
- `databases` keys are the unique display names shown in `File | Open` /
  `RecentFilesMenu`. Each entry is either SQL Server (`server` + `catalog`)
  or SQLite (`path`). `TestDatabase` and `lastUsedUtc` apply uniformly to both
  engines.
- **`comment`** is an optional, free-text string on both a `servers` entry
  and a `databases` entry (`null`/absent is fine — not every entry needs
  one), purely for making a hand-read config file self-documenting (e.g.
  "the real one, don't wipe" vs. "throwaway, safe to delete"). The app never
  reads or writes it except to round-trip it — no dialog collects it in this
  WI; it's for whoever hand-edits the file later. `DatabaseRegistry`'s
  load/save must preserve it losslessly (round-trip, not drop unknown/
  null values).
- Replaces `DataEngineConfig` and `DataEngineCredentialStore` with one new
  class (working name `DatabaseRegistry`) in `MyMoney.Data` that loads/saves
  this file and exposes typed access to both blocks. Exact class/method
  shapes are an implementation-plan decision, not fixed here.

## `DatabaseRole`

```csharp
public enum DatabaseRole { Admin, User, Test }
```

- For `SqlServerStoredProcDatabase`: the role selects which of the three
  credential entries (`myMoneyAdmin`/`myMoneyUser`/`myMoneyTest`) under the
  target server to connect with. SQL Server's own login permissions are the
  actual enforcement — `Admin` can create catalogs/schema, `User` can only
  call CRUD access procs, `Test` can call CRUD access procs plus test-only
  procs. This is a real, server-enforced boundary today.
- For `SqliteDatabase`: this spec introduces the enum and threads it through
  wherever a role needs to be requested, but does **not** implement any
  enforcement for SQLite — see Non-Goals / Handoff to #41. Passing `Role =
  Test` against a `SqliteDatabase` in this WI is a no-op distinction; #41
  is expected to make it a real, software-enforced one via a
  `RoleGuardedSqliteDatabase` facade.

## File Menu UX

- **`File | New`** — **DEBUG builds** get a submenu with two items, **SQL
  Server...** and **SQLite...**. **Release builds have no submenu at all**:
  `File | New` goes straight to the SQLite creation dialog (the only engine
  Release ever offers, consistent with SQL Server being DEBUG-only per
  Non-Goals). The SQL Server menu item and its dialog are compiled out
  (`#if DEBUG`) in Release, not merely hidden/disabled — same treatment as
  the "Test database:" checkbox below.
  - Each variant opens a focused dialog (replacing `CreateDatabaseDialog`'s
    do-everything design). Both collect a **unique display name** (defaults
    to something sensible — catalog name or file name — but editable;
    becomes the `databases` dictionary key).
  - The **"Test database:" checkbox** (label reads "Test database:" followed
    by the checkbox, not "Is test database") is **DEBUG builds only** (`#if
    DEBUG`, compiled out entirely in Release, not merely hidden) on
    whichever dialog(s) Release still shows — i.e. on Release's direct-to-
    SQLite dialog too, not just on the SQL Server one. Release can never
    create a database with `TestDatabase: true`.
  - **SQL Server...** (DEBUG only) additionally collects server name
    (offering servers already present in the registry) and catalog name,
    then runs the in-process bootstrap described below.
  - **SQLite...** collects a file location exactly as today's dialog does.
  - On success, both add a new `databases` entry (name/engine/TestDatabase/
    lastUsedUtc=now) to the registry.
- **`File | Open`** → a list of registry entries, not a file browser.
  Sorted by `lastUsedUtc` descending. DEBUG builds show every entry;
  Release builds filter out anything with `TestDatabase: true`. Selecting one
  connects directly — opening can never implicitly create a database, since
  every listed entry by definition already exists.
- **`RecentFilesMenu`** loses its own storage entirely. It becomes a view
  over the same registry with the same Release-hides-test-entries filter,
  sorted by `lastUsedUtc`, capped at the existing 10-item limit. Opening a
  database through *any* path (Open dialog, a recent-file click, a
  command-line argument) updates that entry's `lastUsedUtc`.

## SQL Server Bootstrap Mechanism (replaces `MyMoneyAdmin.exe`)

`Source/WPF/MyMoneyAdmin` is retired entirely. Its bootstrap logic (today
split across `BootstrapRunner`/`SaBootstrapConnection` and a CLI entry
point) is ported into `MyMoney.Data` and becomes two permanent, parameterized
stored procedures called directly from app code — no shelled-out process, no
temp-proc-created-and-dropped-in-master pattern, no `USE <database>;` text
substitution:

- **`master.dbo.MyMoney_BootstrapServer`** — permanent proc in `master`,
  callable as `sa`. Takes the three generated passwords as parameters.
  Creates the `MyMoneyAdmin`/`MyMoneyUser`/`MyMoneyTest` logins if they
  don't already exist (idempotent — matches the existing design's
  idempotency guarantee) and grants `MyMoneyAdmin` the `dbcreator` server
  role so it can create catalogs itself afterward. Called **once per
  server, ever** — the app only invokes it when the target server has no
  entry yet in the registry's `servers` block.
- **`master.dbo.MyMoney_CreateCatalog @CatalogName NVARCHAR(128)`** —
  permanent proc in `master`, callable as `MyMoneyAdmin`. Uses dynamic SQL
  internally (`EXEC('CREATE DATABASE ' + QUOTENAME(@CatalogName))`,
  `QUOTENAME` specifically to avoid SQL injection through the parameter) to
  create a new catalog by name, idempotent via a `sys.databases` existence
  check first. This is the "database name is a parameter to the stored
  procedure" mechanism — the same proc creates the production catalog, any
  test catalog, or any future catalog, with no per-catalog script
  variant needed.
- **Schema/Access/Test proc deployment**: once the catalog exists, open a
  connection with `InitialCatalog` set directly to that catalog and execute
  every `CREATE OR ALTER PROCEDURE`/table/index/grant statement from the
  checked-in `.sql` files over that connection via plain ADO.NET (splitting
  each file on `GO` client-side — a standard pattern, since `GO` is a
  client-tool batch separator ADO.NET doesn't understand). Because the
  connection is already scoped to the right catalog, **the `USE <database>;`
  line at the top of every existing script becomes dead weight for our own
  bootstrap code** — the bootstrap parser skips any statement matching
  `^USE\b` rather than executing it. The line can stay in the checked-in
  files for a human running them by hand in SSMS, or be removed; either way
  it stops being load-bearing for anything the app itself does. No other
  content in these scripts references a database name (confirmed by reading
  `Categories_AccessProcs.sql` and others — `GRANT EXECUTE ... TO
  MyMoneyUser` references only fixed login names, never a catalog name), so
  no further script changes are needed beyond this.
- `DataEngineStartup.RequiredDatabaseName`'s hardcoded `"MyMoney"` check is
  removed — any catalog name is now valid.

## Test Harness Changes

`SqlServerStoredProcDatabaseTests` and `SqlServerDatabaseContractTests`
(via `SqlServerTestDatabase.WipeAllTables` and `MyMoneyTestConnectionResolver`)
stop reading `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`,
`MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`, and
`MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE` entirely. Instead:

- They read the one `dataengine.config.json` and find the `databases` entry
  with `engine: "SqlServer"` and `TestDatabase: true` (today, in practice, the
  `"SQL Server Test"` entry pointing at the `MyMoneyTest` catalog on
  `Redmond`).
- `TestDatabase: true` on that entry **is** the acknowledgment that wiping it is
  safe — there is no longer a separate opt-in flag to set. A missing
  `TestDatabase: true` SQL Server entry means the tests skip (`Assert.Ignore`),
  same graceful-skip behavior as today's missing-env-var case.
- The admin-connection `DELETE` fallback for the 5 owned-child tables still
  uses that server's `myMoneyAdmin` credential from the registry's `servers`
  block (not a separate admin connection string) until #41 replaces it with
  proper `_Test_Reset` procs for those tables too.

## Handoff to #41

The following are explicitly **not** built by this WI, and #41 needs to
know about them to preserve the contract this WI establishes:

1. **`RoleGuardedSqliteDatabase` facade** (or equivalent): make the
   `DatabaseRole` enum introduced here actually enforced in software for
   `SqliteDatabase` — role-restricted operations (schema/file creation →
   `Admin` only; test-only operations → `Test` only) should throw if the
   caller's role doesn't permit them, mirroring the real permission
   boundary SQL Server already provides. Until this lands, "test processes
   only touch test-tier operations" holds for SQL Server by real
   permission and for SQLite only by convention.
2. **The remaining 5 `_Test_Reset` stored procedures** (`Splits`,
   `Investments`, `TransactionExtras`, `AccountAliases`, `RentUnits`) so
   `SqlServerTestDatabase.WipeAllTables` can stop using a raw admin-
   connection `DELETE` for those tables and go through `MyMoneyTest`
   exclusively, matching the other 11 tables.
3. **A dedicated, cleanly-typed test-tier business-layer API** (the user's
   own example: a real `ClearAllTables()` method) replacing today's static
   `SqlServerTestDatabase.WipeAllTables()` utility, always connecting as
   `MyMoneyTest`, callable only from the test-tier assembly.
4. **A `LayerBoundaryTests`-style reflection check** proving the shipped
   `MyMoney`/`MyMoney.Data`/`MyMoney.Business` assemblies can never
   reference `MyMoney.TestSupport` (or whatever the test-tier assembly is
   named after #41's work) — today this is true by convention
   (`MyMoney.csproj` simply doesn't reference it), not by an enforced,
   regression-proof guarantee, mirroring the existing WPF-family boundary
   test this codebase already has for a different layer pair.

A comment will be posted on issue #41 itself, pointing at this section,
once this spec is committed.

## Future Work (explicitly out of scope for this WI)

- **SQL Server as a Release-available engine.** This spec's bootstrap
  mechanism (permanent parameterized procs, no exe) is designed so lifting
  the `#if DEBUG` restriction later is mostly a compile-flag change, not a
  redesign — but that flip is not part of this WI.
- **A secrets-merge mechanism for ops-provisioned deployments.** Needed
  once SQL Server is Release-available and a real production database is
  provisioned externally (not through this app's own in-process bootstrap)
  with ops-chosen rather than auto-generated credentials. The general shape
  discussed: a script that reads a secret source and the checked-in config
  template and writes the merged, real config straight to disk in one
  non-interactive step, so no intermediate value is ever displayed/logged.
  Not needed for this WI, where bootstrap generates and writes its own
  passwords in-process.
- **A database-upgrade rehearsal pipeline**: backup, schema/proc migration,
  app binary update, then a test run against the upgraded copy — for real,
  non-test databases. Not needed while every database in this environment
  is `TestDatabase: true`.

## Verification

- Unit tests for `DatabaseRegistry` load/save round-tripping (mirroring
  the existing `DataEngineConfigTests`/`DataEngineCredentialStoreTests`
  coverage, consolidated into the new single class).
- `SqlServerStoredProcDatabaseTests`/`SqlServerDatabaseContractTests`
  re-verified end-to-end against the live Redmond server after the env-var
  removal, using a registry entry instead — confirms no regression in the
  existing 16 + 191 passing tests from tonight's #27 work.
- Manual/FlaUI-style verification of the new File|New (both engines) →
  File|Open round trip, and of `RecentFilesMenu` reflecting registry state
  correctly, including the Release-build test-entry filtering (built with
  a Release configuration, not just `#if DEBUG` inspected statically).
- Live bootstrap verification against Redmond: `MyMoney_BootstrapServer`
  and `MyMoney_CreateCatalog` both idempotent on a second run, and creating
  a genuinely new second catalog (a real `TestDatabase: true` entry distinct from
  the production one) succeeds and is independently wipeable without
  touching the first.
