# SQL Server Dual-Engine Support — Design Spec

## Status

Design phase. This is item #2 of a five-item 3-tier architecture
initiative (see the tracking work item for the full list). Items #1
(business/data layer extraction), #3 (business-layer test-support
library), and #4 (FlaUI test harness) each get their own spec; this
document covers #2 only.

## Context

`SqlServerDatabase` (`Source/WPF/MyMoney/Database/SqlDatabase.cs`) is a
complete, working SQL Server backend behind the app's `IDatabase`
abstraction. It has been present but unreachable from the UI since
[PR #22](https://github.com/MoneyTools/MyMoney.Net/pull/22) (2022),
which removed the `MyMoneyAdmin` helper tool and the UI paths that
exposed SQL Server as a selectable engine, on the stated grounds that
the app had standardized on SQLite. Today's `CreateDatabaseDialog` and
Save-As flow only offer SQLite/XML/BinaryXML.

The motivation for restoring this: the developer wants a debug-time
option to run against a SQL Server instance on the local dev network,
primarily so they can author and run their own `.sql` scripts (e.g. in
SSMS) to validate data directly, in addition to the existing C#
(NUnit) test suite. This is a personal development-workflow
preference, not a shipped end-user feature.

## Goals

- Let a DEBUG build of the WPF app run against either SQLite (today's
  default, unchanged) or a SQL Server instance, selected via
  configuration.
- Restore/repurpose the `MyMoneyAdmin` tool to bootstrap a SQL Server
  database and a tiered set of SQL logins, without requiring Windows
  OS-level elevation (the original tool's `runas` requirement, needed
  only for filesystem ACL changes, no longer applies).
- Enforce the tiered-trust model at the SQL Server permission level
  (not just by C# convention): normal app CRUD only ever happens
  through stored procedures.
- Leave a clean seam (the `MyMoneyTest` login and its procedures) for
  the future business-layer test-support library (item #3) to build
  on.
- Keep Release builds completely unaffected — SQLite only, no new
  dependencies, no reachable code path to SQL Server.

## Non-Goals

- Migrating the existing XML-based `Settings`/`DatabaseSettings`
  persistence to JSON. Out of scope; tracked as a separate future work
  item if pursued.
- The business/data-layer extraction into separate assemblies (item
  #1). This spec's code can be built against today's single-project
  structure and folded into the extracted layers later; it does not
  depend on that extraction happening first.
- The business-layer test-support library itself (item #3) and the
  FlaUI UI-automation harness (item #4). This spec only prepares the
  data-layer seam (`MyMoneyTest` login + procs) those will consume.
- Any production/shipped use of SQL Server. This is DEBUG-only,
  developer-workstation tooling.
- Secure secret management beyond "plain JSON file outside the repo."
  No Windows Credential Manager, no vault, no encryption at rest for
  this iteration — acceptable because this targets a disposable dev
  database on a dev network.
- Password rotation, revocation, or multi-developer credential sharing
  tooling.

## Architecture Overview

```
 WPF App (DEBUG)                    MyMoneyAdmin (revived tool)
 ────────────────                   ───────────────────────────
 reads dataengine.config.json            (invoked when engine=SqlServer
   │                                      and target DB is missing)
   ├─ engine = Sqlite  ──► SqliteDatabase (unchanged, all builds)
   │
   └─ engine = SqlServer (DEBUG only)
        │
        ├─ DB exists ──► SqlServerDatabase connects as MyMoneyUser,
        │                 reads creds from dataengine.credentials.json,
        │                 all CRUD via stored procedure calls
        │
        └─ DB missing ──► shell out to MyMoneyAdmin.exe ──► bootstrap
                                                             (see below)
```

Three SQL Server logins, created during bootstrap, each scoped to a
distinct purpose:

| Login          | Used by                          | Permissions                                                   |
|----------------|-----------------------------------|----------------------------------------------------------------|
| `MyMoneyAdmin` | `MyMoneyAdmin` tool only          | Full DDL rights on the `MyMoney` database (creates/alters tables, indexes, views, procs) |
| `MyMoneyUser`  | The running WPF app (normal use) | `EXECUTE` only, on the CRUD access procedures. No direct table grants. |
| `MyMoneyTest`  | Future test-support library (#3) | `EXECUTE` on access procedures plus additional test-support procedures too risky to grant `MyMoneyUser` (e.g. bulk reset/reseed, internal-state inspection) |

## Configuration

Two JSON files, deliberately separated by sensitivity:

**`dataengine.config.json`** — non-secret, identifies the engine and
target server/database. Location: alongside the app's existing config
(exact path finalized at implementation time; not sensitive, may be
committed or left per-machine).

```json
{
  "engine": "SqlServer",
  "server": "DEVSQL01",
  "database": "MyMoney"
}
```

`"engine"` is either `"Sqlite"` (default if the file or key is absent)
or `"SqlServer"`.

**`%USERPROFILE%\.secrets\MyMoney\dataengine.credentials.json`** —
secret, holds the auto-generated UID/PW for each of the three logins.
Lives entirely outside the repository (not merely gitignored inside
it), so it can never be committed and survives a fresh clone.

```json
{
  "MyMoneyAdmin": { "userId": "MyMoneyAdmin", "password": "<generated>" },
  "MyMoneyUser":  { "userId": "MyMoneyUser",  "password": "<generated>" },
  "MyMoneyTest":  { "userId": "MyMoneyTest",  "password": "<generated>" }
}
```

Passwords are auto-generated by `MyMoneyAdmin` at account-creation time
(strong random values). No human ever types or needs to know them —
each tool/process reads the entry it needs from this file.

### DEBUG-only enforcement

The `"SqlServer"` engine option, the `MyMoneyAdmin`-invocation code
path, and ideally the `Microsoft.Data.SqlClient` reference itself are
compiled only into DEBUG builds (`#if DEBUG` guards at the relevant
call sites, consistent with the existing `#if PerformanceBlocks`
pattern already used elsewhere in this codebase).

If a Release build somehow encounters `"engine": "SqlServer"` in a
config file (e.g. a stray file left over from a DEBUG session), it
silently falls back to SQLite and writes a log entry, rather than
failing. Release should behave as if this feature doesn't exist.

## `sa` Handling

**Revision note (2026-09-14, post-implementation-start):** this section
originally specified that `sa` is never persisted and that `MyMoneyAdmin`
could detect *why* a connection failed (disabled vs. bad password) and
show a targeted "enable sa and retry" prompt. Both of those have changed,
in two separate corrections made during implementation:

- **Disabled-account detection was found to be technically impossible**
  and removed before Task 8 was built: SQL Server does not expose the
  specific reason for a login failure to the connecting client by
  default (this is deliberate server-side security hardening — a
  non-sysadmin remote connection always sees generic error 18456
  regardless of cause). `MyMoneyAdmin` instead shows one generic
  failure message and a plain **Retry/Cancel** prompt on any connection
  failure (see `RetryLoop`/`SaBootstrapConnection`, Tasks 4 and 8 of
  the implementation plan).
- **`sa` persistence was deliberately reversed** at the developer's
  explicit request, for this dev-network prototype specifically: `sa`'s
  username and password are now stored in the same
  `%USERPROFILE%\.secrets\MyMoney\dataengine.credentials.json` file as
  the three app-account credentials, under the key `"sa"`. The
  developer made this call knowingly, after being told the tradeoff:
  storing `sa` widens the credentials file's blast radius from "three
  scoped, purpose-limited accounts" to "full control of the SQL Server
  instance" if that file is ever exposed. Acceptable for a throwaway
  dev-network database; would need revisiting before this pattern is
  used anywhere with real stakes.

Current behavior:

1. The developer manually enables the `sa` login on the target SQL
   Server instance, out of band (SSMS or `sp_configure`).
2. `MyMoneyAdmin` checks the credentials file for a stored `"sa"`
   entry first. If present, it uses that password directly — no
   prompt. If absent, it prompts for the `sa` password interactively
   (this path is still supported, e.g. for a fresh machine with no
   credentials file yet).
3. `MyMoneyAdmin` attempts a SQL-authentication connection as `sa`. On
   failure (any reason — the client cannot distinguish "disabled" from
   "wrong password" from "login doesn't exist"), it shows a generic
   failure message and a Retry/Cancel prompt. Retry re-attempts;
   Cancel aborts the bootstrap.
4. On a successful connection where the password came from an
   interactive prompt (not already in the credentials file),
   `MyMoneyAdmin` saves it to the credentials file under `"sa"` so
   subsequent bootstraps on this machine don't need to re-prompt.
5. The developer is responsible for disabling `sa` again afterward,
   out of band, if they want it disabled between sessions.
   `MyMoneyAdmin` does not manage that lifecycle.

## Bootstrap Sequence

1. `MyMoneyAdmin.exe` runs — either launched directly by the developer,
   or auto-launched by the WPF app when it reads `dataengine.config.json`,
   sees `engine: "SqlServer"`, and finds the target database missing.
2. `sa` authentication succeeds (see above).
3. Connected as `sa`, `MyMoneyAdmin` sends a script that creates a
   **temporary stored procedure in `master`**. Wrapping this logic in
   a procedure (rather than running loose statements) makes it
   independently inspectable/debuggable in SSMS before it's ever run.
   The procedure, when executed, performs:
   - `CREATE DATABASE MyMoney` (skipped if it already exists — see
     idempotency note below).
   - `CREATE LOGIN` for `MyMoneyAdmin`, `MyMoneyUser`, `MyMoneyTest`,
     each with a freshly auto-generated strong password.
   - Any server-level role/permission setup those logins need (e.g.
     `sysadmin`-free, minimal server-level rights — database-level
     permissions are granted in step 7).
4. `sa` executes that procedure.
5. `sa` drops the procedure from `master` immediately after — no
   privileged object is left behind at the server level.
6. `MyMoneyAdmin` writes the three generated UID/PW pairs to
   `%USERPROFILE%\.secrets\MyMoney\dataengine.credentials.json`.
7. `MyMoneyAdmin` reconnects using the new `MyMoneyAdmin` login,
   scoped to the `MyMoney` database, and runs the hand-authored
   schema stored procedures: table/index/view creation, the CRUD
   access procedures (granted `EXECUTE` to `MyMoneyUser`), and the
   test-support procedures (granted `EXECUTE` to `MyMoneyTest`).

**Idempotency:** steps 3–7 must be safe to re-run. Each creation step
checks for existence first (`IF NOT EXISTS` / catalog view checks)
rather than assuming a clean slate, so a bootstrap that fails partway
through (e.g. a crash between steps 5 and 7, leaving the database and
logins created but schema incomplete) can simply be re-run to
completion without manual cleanup.

## SQL Script / Stored Procedure Organization

All schema and access-procedure T-SQL is **hand-authored and checked
into the repository** as `.sql` files — not generated at runtime from
the existing `Mapping.cs` reflection engine. This is a deliberate
divergence from how `SqliteDatabase` builds its schema (via
`SqlServerDatabase.GetCreateTableScript()`, inherited unchanged): the
developer's stated goal is to have real, readable, independently
runnable SQL Server scripts, not strings assembled by C# attributes at
runtime.

Proposed layout (finalized at implementation time):

```
Source/WPF/MyMoney/Database/SqlScripts/
├── Bootstrap/   — the master-db temp procedure (create DB + logins)
├── Schema/      — CREATE TABLE / INDEX / VIEW procedures
├── Access/      — CRUD procedures granted to MyMoneyUser
└── Test/        — test-support procedures granted to MyMoneyTest
```

Any additional one-off validation scripts the developer writes for
their own use (e.g. ad hoc data-integrity queries run manually in
SSMS) are their discretionary choice whether to add to the repository
— not a deliverable of this spec.

Stored procedures are the **only** sanctioned interface for
`MyMoneyUser`. This is enforced at the SQL Server permission level
(no `SELECT`/`INSERT`/`UPDATE`/`DELETE` grants on tables for that
login), not merely by convention in the C# code.

## Runtime Data Layer Changes

`SqlServerDatabase` currently builds and executes direct parameterized
SQL against tables (see `GetConnectionString`, `Create`, and related
methods in `Source/WPF/MyMoney/Database/SqlDatabase.cs`). This must
change: when connected as `MyMoneyUser`, all CRUD operations are
issued as calls to the access stored procedures instead, since the
login has no table-level grants to fall back on. The public `IDatabase`
interface surface does not need to change — this is an internal
implementation change within `SqlServerDatabase`.

`SqliteDatabase` is unaffected. It keeps using the current
reflection-driven, direct-SQL approach via the inherited
`GetCreateTableScript()`. No accounts, no stored procedures, no
behavior change on that path — this spec does not touch the default,
shipped engine at all.

## Startup / Trigger Flow

On app startup (DEBUG builds only):

1. Read `dataengine.config.json`.
2. If `engine` is absent or `"Sqlite"` → existing SQLite behavior,
   completely unchanged.
3. If `engine` is `"SqlServer"`:
   - If the target database exists → connect directly as `MyMoneyUser`,
     reading credentials from `dataengine.credentials.json`.
   - If it does not exist → invoke `MyMoneyAdmin.exe` to run the full
     bootstrap sequence above, then connect as `MyMoneyUser`.

## Error Handling

- `sa` disabled → Yes/No retry dialog (see `sa` Handling above).
- `sa` authentication fails for any other reason → distinct, specific
  error message; bootstrap aborts.
- `dataengine.credentials.json` missing or unreadable when the app
  expects `engine: "SqlServer"` and the database already exists → this
  indicates the credentials file was lost/moved after a successful
  bootstrap (e.g. a new machine, or the file was deleted); the app
  reports a clear error rather than silently falling back to SQLite,
  since silently switching engines could confuse the developer about
  which database they're looking at. It does not attempt to
  re-bootstrap automatically.
- Bootstrap failure partway through → re-run `MyMoneyAdmin`; see
  Idempotency above.

## Sequencing / Dependencies

- Does not block on item #1 (business/data layer extraction). Can be
  built and used against today's single-project structure now, and
  folded into the extracted layers when #1 lands.
- Feeds item #3 (business-layer test-support library): the
  `MyMoneyTest` login and its granted procedures are the seam that
  library will call into. This spec is responsible for that seam
  existing and being usable; it does not implement the C#-side library
  that consumes it.
- Independent of item #4 (FlaUI harness).

## Open Questions / Future Work

- Whether the credentials file's containing folder needs filesystem
  ACL hardening beyond default user-profile permissions. Not required
  for this dev-only prototype; noted for future consideration if this
  moves beyond a single developer's workstation.
- Migrating existing XML `Settings`/`DatabaseSettings` persistence to
  JSON, and unifying it with `dataengine.config.json`'s format —
  explicitly deferred as a separate future work item.
- Credential rotation/revocation tooling — not needed while `sa` stays
  disabled between bootstraps and the three generated passwords are
  effectively fixed for the life of a given dev database.
