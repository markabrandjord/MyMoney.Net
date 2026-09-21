# Data-layer architecture brainstorm — the rebuild's first foundation slice

**Status: brainstorm output, not a spec.** This is the design-space exploration that issue #5
was explicitly written as the input to. It is deliberately *not* polished into spec form —
it exists to drive an owner review, after which the agreed path becomes a real design spec and
then an implementation plan, per the brainstorming skill's normal process.

**Date:** 2026-09-20 · **Branch:** `rebuild/data-layer-foundation` · **Panel:** the usual 7 roles.

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

That last row matters a lot: the owner's constraint *"Admin creates the database, then adds the
stored procedures that themselves create tables, indexes, views"* is **not** what the code does
today. It is a genuine change, not a formalization — the formalization part is the
three-login/role model, which does exist.

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
carries the DDL as **embedded `.sql` resources**, applied by a migration runner keyed on
`PRAGMA user_version`. Same versioning semantics, same auditability, same idempotency, and the
same "this code is in the provisioning assembly, not the store assembly" separation. The
reflection-to-DDL path (`GetCreateTableScript`) is deleted on both engines — it's the reason the
two engines' schemas have quietly drifted before.

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

### 2.4 The store doubles — and an open sub-decision worth arguing about

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

**Sub-decision T-1, genuinely open:** should the default business-test double be a hand-written
`MockStore` at all, or should it be **`RecordingStore`/`FaultInjectingStore` wrapped around a real
SQLite in-memory store** (`Data Source=:memory:`)?

- *For the real-SQLite-decorated option:* zero divergence risk (the mock and the engine cannot
  disagree, because there is only one implementation), one fewer thing to maintain, and the
  aggregation query in §1 doesn't need a second LINQ implementation that can silently drift from
  the SQL one.
- *Against:* unknown speed at several-hundred-tests scale, and it couples every business test to
  the schema being current.

The panel's honest position is that this **should be measured in the first slice, not asserted
now**: build both, run 200 synthetic business tests through each, and pick on the number. If
in-memory SQLite lands under ~15 s for 200 tests, the decorated-real-store option is strictly
better and `MockStore` should be deleted rather than maintained. This is written down as a task,
not a conclusion.

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

| # | Deliverable | Proves |
|---|---|---|
| 1 | `IMoneyStore` / `IMoneyStoreProvisioner` / `IMoneyQuery` ports (in `MyMoney.Business`), `MyMoney.TestKit.Contracts` with `IMoneyStoreTestControl` | The tier split exists as types before any engine implements it |
| 2 | `MyMoney.Data.Sqlite.Provisioning` — versioned embedded-SQL migration runner keyed on `PRAGMA user_version`; creates `Accounts` + FK-target tables | Schema-as-versioned-artifact, not reflection-to-DDL |
| 3 | `MyMoney.Data.Sqlite` — `SaveOne<Account>`, `LoadAccounts`, conflict detection | The proven `SaveOne` pattern survives the reshape |
| 4 | `MyMoney.TestKit` — `MockStore`, `RecordingStore`, `FaultInjectingStore`, `StoreContractTests` base with the Account cases | Happy path **and** error path from day one, as the owner asked |
| 5 | `MyMoney.Data.Sqlite.TestTier` — `IMoneyStoreTestControl` (wipe/reset) | The SQLite facade, for real |
| 6 | `MyMoney.Tests.Architecture` — all seven Tier-0 tests from §2.5 | The guarantees are enforced, not asserted |
| 7 | `AddAccountService` in `MyMoney.Business` + its `MockStore`-backed tests incl. the conflict path | The business layer is callable with no UI present |
| 8 | `MyMoney.Data.SqlServer{,.Provisioning,.TestTier}` for the same slice, incl. `Schema_ApplyTo` procs | The tiering maps twice, and the contract suite is genuinely shared |
| 9 | **Measure T-1** (§2.4): 200 synthetic business tests against `MockStore` vs. decorated in-memory SQLite | Decide the default double on evidence |

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
cover `IMoneyQuery` in the same slice the port is introduced — not later.** This is also the
strongest single argument in favor of T-1's decorated-real-SQLite option, which makes the problem
structurally impossible.

### 6.5 The simpler design nobody has proposed

Delete `MockStore`. Run every business test against a real in-memory SQLite store, decorated for
recording and fault injection. Fewer moving parts, zero divergence, one implementation of every
query. The only reason it isn't the recommendation is that nobody has measured whether it fits the
sub-minute budget — which is why §2.4 makes it a measurement, not an opinion.

### 6.6 Seven assemblies is a real cost, not a rounding error

More projects, slower cold builds, more `.csproj` churn in reviews, and a structure that the next
person will try to "simplify." The mitigation is not discipline; it is that the Tier-0 tests fail
loudly when the simplification happens, and that the reasoning is written down where the tests
point to it.

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
   looked at it.

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
- **Schema creation becomes a versioned artifact executed by the Admin tier** — stored procs on
  SQL Server, embedded SQL + `PRAGMA user_version` on SQLite — replacing reflection-to-DDL on both.
- **Test subsystem**: `MyMoney.TestKit` as a true library (not a test project), four test tiers
  with a new Tier-0 architecture band, four layered enforcement mechanisms for the one-way
  dependency, and `RecordingStore`/`FaultInjectingStore` so the error path is testable from day
  one. One measurement (T-1) decides whether the hand-written mock survives at all.
- **Open question resolved**: control logic → business layer's application-services band;
  format parsing/writing → business layer behind `IDataFormat`; engine-native backup/duplication →
  provisioner tier; reporting → three stages (query in the data layer, `ReportModel` in the
  business layer, Markdown/CSV renderers in the business layer's `Reporting` namespace), with
  `IReportWriter` deleted rather than ported.
- **First slice**: provision-then-add-an-Account on SQLite, with the test subsystem and the Tier-0
  boundary tests landing alongside it, then the same slice on SQL Server to prove the tiering maps
  twice.
