# Implementing tests from the FlaUI/business-layer scenario catalog, starting with Basics

## Context

`docs/superpowers/specs/2026-09-18-flaui-ui-wiring-test-scenarios.md` (the "tracking doc")
catalogs ~166 end-user scenarios across 6 functional areas (Home, Basics, Accounts, Charts,
Reports, Development) plus an "Undocumented in help file" section, each row naming a
business-layer API (or flagging its absence as a `Business-layer gap`), a FlaUI verification
step, test data, and a `Status` column that is currently `Not started` or `Business-layer gap`
for every single row — no test implementation has happened yet.

This spec covers turning that catalog into running tests, one functional area ("section") at
a time, starting with **Basics** (9 subsections, ~35 scenarios: Categories, Payees & Aliases,
Splits & Transfers, Merging Duplicate Transactions, Currencies & Securities,
Auto-Categorization, Quick Search & Advanced Queries, Attachments, Sample Data). Once Basics
is done and assessed, the same process repeats for Accounts, Charts, and Reports — this spec
also writes down that repeatable process so later sections don't need a fresh design pass.

## Goal

For each Basics subsection: write a business-layer unit test for every scenario whose API is
already reachable without a UI, then a thin FlaUI test proving the real dialog/control wires
user input to that API correctly. Update the tracking doc's `Status` column as each test lands.
At the end of the section, do a short retro on whether the catalog itself held up (accurate
descriptions, no missing gaps, no rows that needed splitting).

## Non-goals (this pass)

- **New extraction work.** Several Basics rows are `Business-layer gap` because the logic
  hasn't been pulled out of the WPF project at all (e.g. `AttachmentManager`,
  `RecentFilesMenu`-adjacent code — though most of those live in Accounts/Undocumented, not
  Basics). Where a Basics row needs a real extraction rather than "just write a test," it is
  **not** done here — file a follow-up issue (same pattern as the original migration's
  deferred items) and leave the row `Business-layer gap` in the doc.
- **Hardware-dependent or otherwise non-automatable scenarios.** Attachments' "Scan and crop a
  receipt" needs real scanner hardware. Mark these `Not automatable in this environment` and
  skip — no mock-the-scanner workaround.
- **Sections other than Basics.** Accounts/Charts/Reports/the Undocumented section are future
  work, using the repeatable process below, each getting its own short scoping pass (not a
  full new spec) before starting.
- **SDD ceremony.** No fresh-implementer-subagent-per-task, no ledger, no ruling log. This is
  incremental test-writing against already-established conventions (existing fakes, existing
  FlaUI patterns), not architecturally risky work — the user reviews each subsection directly
  before moving to the next.

## Test content conventions (isolation + thoroughness)

Two separate concerns, solved separately:

**Isolation (idempotent, order-independent, re-runnable any number of times) — Fresh Fixture,
not Shared Fixture.** The goal, stated precisely: adding a new test case must never change an
existing test's result, and running tests in a different order (or the same test many times in
a row) must never change any test's result. The standard, named answer to this (Meszaros,
*xUnit Test Patterns*: "Fresh Fixture" vs. "Shared Fixture") is that every test builds its own
clean starting state from scratch rather than reusing or incrementally patching a
shared/persisted one — "nuke and pave" per test, not per run.

Business-layer tests already get this for free — every existing test (`ExportersTests.cs`,
etc.) constructs a fresh in-memory `new MyMoney()` per test method, so there's no shared or
persisted state to leave behind; this is Fresh Fixture already in practice, just not named as
such until now.

FlaUI tests need the same property but currently have nothing enforcing it: `MainWindow`
doesn't autosave (confirmed — writes only happen on explicit File\|Save or on a dirty-database
close prompt, `MainWindow.xaml.cs:5011`'s `OnClosing` → `SaveIfDirty()`), so a FlaUI test that
mutates data and then just closes the app risks either blocking on an unanswered save prompt
or, if answered "Yes," permanently drifting a shared fixture file — reintroducing exactly the
order-dependence this section is trying to eliminate. The fix, matching the business-layer
side's Fresh Fixture property instead of just working around the save-prompt risk: **generate
the fixture fresh, in code, at the start of every single test** — see "Shared fixture builder"
below — rather than maintaining and copying a static checked-in binary. Each test gets a
brand-new scratch SQLite file built from identical seed logic every time, registers it in
`DatabaseRegistry`, and deletes it in `[TearDown]` regardless of pass/fail. There is no
shared file for a failed test to leave dirty, and no checked-in binary to drift or to manually
regenerate when the schema changes.

**Thoroughness (any test that exercises an entity's persistent lifecycle).** Where a scenario
adds, updates, or deletes a record, structure the test as a full round trip, not a single
assertion:

1. Query first — confirm the starting count is what's expected (0, or a known N from seeded
   fixture/setup data).
2. Add the record(s) — with a **valid** input, confirm success and re-query to confirm the
   count increased by exactly the expected amount.
3. Add/update with an **invalid** input (where the API has real validation to test) — confirm
   the operation is rejected (exception, or whatever failure signal the API actually uses) and
   that the record count/state is unchanged afterward. Skip this step for rows the tracking
   doc already flags as having no validation seam (e.g. `Currency`'s silent-fallback case,
   account-number truncation) — those get a test asserting the *actual* (non-rejecting)
   behavior instead, so the test documents reality rather than an aspirational contract.
4. Update the record(s) — confirm the update took (re-query and check the changed field), not
   just that no exception was thrown.
5. Query with a filter/condition designed against known seeded data to return an **exact**
   count (e.g. "exactly 3 categories under this parent," "exactly 1 payee matching this
   alias") — not a loose `Is.Not.Empty`/`Is.GreaterThan(0)` check. Exact counts catch a query
   silently over- or under-matching in a way a non-empty check would miss.
6. Delete the record(s) — re-query and confirm the count returns to 0 (or the pre-test
   baseline), proving delete actually removed data rather than just not throwing.

This is the shape for any test touching persistent entities (Categories, Payees, Aliases,
Splits, Currencies, etc.) in both business-layer tests (within one fresh in-memory `MyMoney`)
and FlaUI tests (against one scratch fixture copy). Read-only/pure-computation scenarios
(`AutoCategorization`, `QuickFilterParser`) don't need this shape — they already fit the
existing input-in/value-out unit test pattern.

**Code-path-driven design for transformation/generation APIs.** The CRUD-lifecycle shape
above fits entity persistence (Categories, Payees, Splits, ...). It doesn't fit APIs whose job
is producing an artifact or running a pipeline — exporters (XML/CSV/PDF/TXF), importers, Ofx
parsing/download. For these, before writing tests:

1. Read the API's actual implementation and enumerate its distinct code paths: one per output
   format/success branch, one per validation/error condition, one per malformed-input handling
   branch. The tracking doc's scenario rows are the starting point, not the full list — reading
   the source usually surfaces branches no scenario description mentions (e.g. an exporter's
   empty-input special case, or a specific `OfxErrorCode` dispatch branch).
2. Design one test per enumerated branch, with input data deliberately shaped to hit that exact
   branch — not input reused loosely across multiple "similar" tests.
3. Assert precisely: for a success branch, that the produced artifact is actually well-formed
   for that path (valid XML shape, correct TXF record format, expected byte content) — not just
   "no exception was thrown." For an error branch, assert the specific error/exception
   type/error code expected, not a generic `Assert.Throws<Exception>`.
4. Since `coverlet.collector` is already referenced in `UnitTests.csproj`, spot-check with
   `dotnet test Source/WPF/UnitTests/UnitTests.csproj --collect:"XPlat Code Coverage"` after a
   batch of tests for one API — not a hard gate, but a cheap way to catch a branch that was
   *designed* to be hit but whose test data didn't actually reach it.

**FlaUI test session lifecycle (shared app instance, per-test data isolation).** Launching the
WPF app is the expensive part (several seconds cold start); opening/closing a database within
an already-running app is fast. So the app itself is launched once per section, not once per
test, while each test still gets its own scratch fixture copy:

- All of Basics' FlaUI test classes live in a new namespace (e.g. `Walkabout.UITests.Basics`),
  separate from the existing `Walkabout.UITests.PayeeSelectionTests` so this doesn't touch or
  risk that file.
- A `[SetUpFixture]` class in that namespace does `[OneTimeSetUp]` (launch the app once with
  `/nosettings`, create the `UIA3Automation`, get the main window — before any test in the
  namespace runs) and `[OneTimeTearDown]` (close the app, dispose automation — once, after
  every test in the namespace has finished).
- Each individual `[Test]`'s own `[SetUp]`/`[TearDown]` handles per-test isolation within that
  shared session: `[SetUp]` calls `BasicsFixtureBuilder.Build()`, saves it to a fresh scratch
  path, registers it, opens it via File\|Open against the already-running app; `[TearDown]`
  closes the database (answering "don't save" to any prompt) and deletes the scratch file +
  registry entry, so the next test starts from a clean "no database open" state without
  relaunching the process.

This generalizes past Basics without redesign — a later section's FlaUI test classes either
join the same `[SetUpFixture]`-managed session or get their own namespace-scoped one, following
the same shape.

**Deferred, not part of this pass:** a randomized/property-based approach (random sequence of
add/update/delete/query operations against a dynamically-tracked shadow model of "what the
state should be," ending in a full reset) is a legitimate but materially heavier technique —
a model-based stress-test harness, not a scenario test. Logged under Open Questions below as a
possible future addition, not built into these ~35 scenario tests.

## Process (applies to every subsection, in every section)

1. **Business-layer tests first.** For each scenario in the subsection whose row names a real,
   already-reachable API: write a unit test in `Source/WPF/UnitTests/`, following the existing
   pattern (`MyMoney.TestSupport`'s `MockDatabase`, the `FakeBusinessLayerUiCallback`/
   `FakeImportProgressReporter` fakes already defined in `QifImporterTests.cs`/
   `DatabaseLifecycleTests.cs` where a callback is needed). No new mocking infrastructure.
2. **Run business-layer tests.** `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter
   "..."` — safe to run unattended. Confirm green before moving on.
3. **FlaUI test second**, only for scenarios whose business-layer half is now covered (or
   never had one — e.g. a pure UI toggle). Follow `PayeeSelectionTests.cs`'s conventions:
   `FlaUI.Core`/`FlaUI.UIA3`, NUnit, a fresh scratch database built by `BasicsFixtureBuilder`
   per test (see "Test content conventions" below — never a checked-in binary), `DatabaseRegistry`
   registration/cleanup, `[TearDown]` cleanup.
4. **Run the FlaUI test.** Fully automated, self-driving — the test code performs every
   interaction (`Keyboard.Type`, clicks, dialog dismissal) itself, no human present or
   watching. This requires a real interactive Windows desktop session to execute against
   (a Windows UI Automation constraint, not a design choice); it cannot run from this
   background-job environment (confirmed by the earlier whole-solution-test-run hang). Once
   written, running it is a single unattended `dotnet test Source/WPF/UITests/UITests.csproj
   --filter "..."` invocation on a machine with an interactive session — see "FlaUI execution
   environment" below for how that invocation actually happens.
5. **Update the tracking doc's Status column** for every scenario touched in this subsection —
   `Written` with the test file name, or `Passing` with today's date once the live FlaUI run
   confirms it, or `Business-layer gap (deferred — issue #NN)` / `Not automatable in this
   environment` for anything skipped.
6. **Move to the next subsection.**

At the end of all subsections in a section: a short written retro (in chat, not a new doc) —
did any row's description turn out wrong once actually tested, did new gaps surface that
weren't visible from static reading, does the doc need re-splitting anywhere. Decide the next
section from there.

## Basics: subsection breakdown and fixture plan

### Shared fixture builder

Not a checked-in binary — a shared, plain C# builder method (e.g.
`BasicsFixtureBuilder.Build()`) living in `Source/WPF/MyMoney.TestSupport/` (WPF-free,
already referenced by `UnitTests`; `UITests.csproj` needs one new `ProjectReference` to it,
which it doesn't have today) that constructs a `MyMoney` graph purely via business-layer API
calls — the same shape as the existing `Source/WPF/UITests/FixtureGenerator.cs`
(`money.Payees.AddPayee`, `money.Accounts.AddAccount`, etc.), generalized from a one-off
`[Explicit]` regeneration utility into a function called fresh on every test run. This is the
Test Data Builder / Object Mother pattern (Meszaros): one place that knows how to construct
known-valid domain objects, reused everywhere instead of each test hand-rolling setup, and
reused *across both test layers* so "similar strategy, similar results" holds literally:

- **Business-layer tests** call `BasicsFixtureBuilder.Build()` (or a narrower per-subsection
  builder method where a test only needs a slice of it) to get an in-memory `MyMoney` directly
  — no file I/O, matching the existing convention.
- **FlaUI tests** call the same builder, then `SqliteDatabase.Save()` the result to a brand-new
  scratch temp path at the start of every test (see the isolation discussion above) — nuke and
  pave via code, not via copying a file.

The builder seeds enough data to cover most Basics scenarios in one pass:

- 3+ categories including a parent/child pair (`Fun:Movies`, `Fun:Videos`) and one category
  with existing transactions, for the Categories subsection's rename/delete/merge scenarios.
- 2+ payees with messy raw names suitable for rename/alias scenarios, plus one existing
  `Alias` row and one narrow-alias-subsumption setup (2-3 narrow aliases for the same payee,
  for the regex-consolidation scenario).
- A transaction with an out-of-balance split, and a linked transfer pair (one side reconciled,
  one not), for Splits & Transfers.
- Two near-duplicate transactions (same amount/date, different FID) for Merging Duplicate
  Transactions.
- A non-USD currency row and a security with stock-split history, for Currencies & Securities
  (cost-basis-dependent scenarios reuse `CostBasisTests`' fixtures directly instead —
  business-layer only, no FlaUI needed there per the tracking doc).
- Payee history at varying amounts/dates for Auto-Categorization's suggestion scenarios
  (business-layer only — `AutoCategorization` is WPF-free, no FlaUI test needed for the
  algorithm itself, only if a dialog visibly surfaces the suggestion).
- A mix of matching/non-matching transactions for Quick Search literal/boolean scenarios
  (business-layer only — `QuickFilterParser` is WPF-free; a FlaUI test only for confirming the
  Quick Search box itself filters the grid).
- One attachment on an existing transaction, for the attachment-delete scenario (add-via-
  drag/drop and scan/crop are skipped per non-goals above).

Individual business-layer tests are free to use only the slice of `BasicsFixtureBuilder`'s
output they need, or build even narrower ad hoc `MyMoney` state directly (as
`ExportersTests.cs`, etc. already do) — the shared builder is there for reuse where it helps,
not a mandate to route every test through the full Basics graph when a smaller setup is
clearer.

FlaUI tests never touch a checked-in fixture file at all — each test calls the builder and
saves a brand-new scratch SQLite file at the start of the test, registers it in
`DatabaseRegistry`, and deletes the scratch file + registry entry in `[TearDown]` (see "Test
content conventions" above).

### Subsections, in implementation order

1. **Auto-Categorization** — business-layer only (`AutoCategorization.AutoCategoryMatch`,
   `KNearestNeighbor<T>`), zero FlaUI needed per the tracking doc (pure logic feeding a text
   field, no distinct dialog). Cheapest possible first subsection to prove the process.
2. **Quick Search & Advanced Queries** — mostly business-layer (`QuickFilterParser`,
   `MyMoney.ExecuteQuery`); one thin FlaUI test that types into the Quick Search box and
   confirms the grid filters.
3. **Categories** — business-layer gaps: `GetOrCreateCategory` sub-category path, `OnDelete`,
   `ReCategorize`. FlaUI: rename-collision rejection, delete-with-redirect, drag/drop
   move/merge.
4. **Currencies & Securities** — business-layer: `AddCurrency`/`GetCultureForCurrency`
   (including the invalid-code fallback case), `RemoveCurrency`. FlaUI: add/remove a currency
   row. Stock-split/cost-basis rows are already covered by `CostBasisTests` — skip re-testing.
5. **Payees & Aliases** — business-layer: `ApplyAlias`/`FindAliasMatches`, `AddAlias`,
   `FindSubsumedAliases` (the regex-consolidation case — flagged in the tracking doc as
   entirely unverified today, worth prioritizing). FlaUI: rename dialog, regex consolidation
   dialog's conflict warning.
6. **Splits & Transfers** — business-layer: `Splits.Unassigned`/`HasUnassigned`,
   `MyMoney.Transfer`/`Rebalance`, `RemoveTransfer` (both allowed and reconciled-blocked
   cases — the blocked case's enforcement point needs tracing first, per the tracking doc's
   "not fully verified" note). FlaUI: F6 balance, transfer-by-typing, F12 link.
7. **Merging Duplicate Transactions** — business-layer: `FindPotentialDuplicate`,
   `Transaction.Merge` (field-preserving merge, attachment carry-over). FlaUI: merge button,
   not-a-duplicate dismissal.
8. **Attachments** — business-layer: none available without extraction (`AttachmentManager`
   isn't in `MyMoney.Business`) — file a follow-up issue for the extraction, same as the
   original migration's deferred items. FlaUI: attachment-delete only, against the one
   pre-seeded attachment in the fixture. Add/drag-drop and scan/crop: skipped per non-goals.
9. **Sample Data** — business-layer already covered (`SampleDataRegressionTests`,
   `SampleDataLoaderTests` from the original migration) — only new work is a thin FlaUI test
   for the Help-menu dialog wiring (`SampleDatabaseOptions`).

This order runs cheapest/highest-confidence first (pure-logic subsections with no FlaUI at
all) and ends on the two subsections most likely to surface real gaps (Attachments' missing
extraction, Sample Data's thin remaining wiring).

## Repeatable process for later sections (Accounts, Charts, Reports)

Each future section gets, before starting:

1. A quick scoping pass identifying its subsections' implementation order (cheapest/most
   pure-logic first, same heuristic as above) and any shared-fixture needs — a few sentences
   in chat, not a new design doc, unless a section's shape turns out to need one (e.g. Reports'
   `IReportWriter`-interleaved architecture may need its own short design discussion once
   reached, since none of its scenarios currently name a decoupled API to test at all).
2. Confirmation that the lightweight (non-SDD) process from this spec still fits — if a later
   section's scope turns out to need real extraction work as a prerequisite (e.g. Reports
   needs a new query/aggregation API per the original migration's own non-goals), that's a
   signal to stop and brainstorm that extraction separately before resuming test-writing.

Otherwise, the process itself (business-layer test → run → FlaUI test → run live → update doc
Status → next subsection → end-of-section retro) carries over unchanged.

## FlaUI execution environment

FlaUI tests are fully automated — no human clicks, types, or watches during a run — but
Windows UI Automation requires a real interactive desktop session to attach to, which this
background-job environment doesn't have (confirmed by the earlier whole-solution `dotnet test`
hang: the app opened a dialog with no interactive session able to display or dismiss it). This
is a Windows-API-level constraint, not something solvable by writing the tests differently.

Practical consequence: I can write and hand off the FlaUI test files, but I cannot invoke
`dotnet test Source/WPF/UITests/UITests.csproj` myself from this session and see it run to
completion. Someone/something with access to a real interactive Windows session needs to run
that command — once — and it completes fully unattended (no interaction needed during the
run itself, just like any other `dotnet test` invocation). Options for *what* triggers that
run, to be settled before Basics' first FlaUI test is written (not a blocker to the
business-layer half of each subsection):

1. You run the command yourself on your own machine, whenever convenient — not "watching a
   test," just starting a normal test run and reading the result afterward, same as any other
   test suite.
2. A scheduled task on your machine (auto-logon, unlocked session) runs it on a cadence,
   fully hands-off after initial setup.
3. Reinstating a self-hosted CI runner scoped safely for this — bigger infrastructure decision
   than this pass warrants, given the project deliberately moved off self-hosted CI for the
   public-repo safety reasons noted earlier in this project's history; not proposed here.

## Testing/tooling notes

- Business-layer tests: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter
  "Name=TestMethodName"` or a class-level filter — never the whole-solution `dotnet test
  Source/WPF/MyMoney.sln` (pulls in FlaUI `UITests`/`ScenarioTest`, hangs unattended).
- FlaUI tests: fully self-driving, no human interaction during the run — see "FlaUI execution
  environment" above for where that run actually happens.
- No new mocking framework, no new test-support abstractions — reuse what Task 10 and the
  original migration already established.

## Test input data for importers/parsers (synthetic vs. real-world samples)

Doesn't block Basics (Importers/CSV/OFX live in Accounts, per the tracking doc's section
breakdown) but is a general strategy decision worth settling now, before Accounts' CSV
import/OFX download subsections need it, and it directly extends the "code-path-driven design"
convention above — for an importer/parser, the "input shaped to hit a branch" *is* a file.

**Synthetic/controlled data — the default, already this codebase's convention.**
`CsvTransactionImporterTests.cs` already builds input via a `CreateCsv(params string[][] rows)`
helper; `OfxObjectModelTests.cs` already builds OFX content as inline `const string` XML
literals. Both give exactly-known expected results by construction — the same principle as
`BasicsFixtureBuilder`, applied to file-shaped input instead of a `MyMoney` graph. This stays
the primary approach for branch-coverage tests: one minimal, purpose-built input per code path,
not a large realistic-looking file that happens to also exercise that path.

**Real-world sample files — a secondary, smaller set of realism/robustness tests.** Bank-
generated OFX/QFX/CSV files have institution-specific quirks (odd encodings, nonstandard
fields, malformed-but-tolerated responses) that hand-crafted synthetic data won't think to
include. If used:

- **Never check in a real, unmodified export.** Any real file must be fully anonymized first —
  account numbers, names, balances, dates shifted/scrubbed — same discipline already applied to
  this project's SQL Server credentials. Prefer sourcing already-synthetic sample files (the
  OFX spec itself ships example files) over starting from a real personal export at all.
- Store anonymized samples as checked-in fixtures (e.g.
  `Source/WPF/UnitTests/Fixtures/RealWorldSamples/*.ofx`/`.csv`), read via `File.ReadAllText`,
  clearly separated from the synthetic branch-coverage tests.
- Assert more loosely than the synthetic tests do — "parses without throwing," "produces the
  expected transaction count," not exact byte-for-byte output — since a real-world file's exact
  shape isn't something the test author fully controls, only observes.
- If you have real exports you'd like used this way, they need your own review/scrub before
  handing them over — not something to source or anonymize on your behalf without you looking
  at the content first.

This is a small supplementary set of tests, not a replacement for the synthetic/branch-coverage
approach, and gets scoped in detail when Accounts' CSV import/OFX subsections are reached.

## Open questions / future work

- The exact enforcement point for "can't delete a transaction whose transfer partner is
  reconciled" and "can't move a Reconciled transaction" hasn't been traced to a specific line
  yet (flagged in the tracking doc as "not fully verified") — resolving this is part of writing
  the Splits & Transfers business-layer tests, not a blocker to starting.
- Attachments' `AttachmentManager` extraction is deferred to a follow-up issue, filed once this
  section reaches that subsection (not filed speculatively now).
- A randomized/property-based test harness (random add/update/delete/query sequences against a
  dynamically-tracked shadow model of expected state, ending in a full reset to a known state)
  was considered as a way to stress-test persistence beyond fixed scenarios. Legitimate future
  idea, but a materially heavier effort than scenario testing — not part of this pass. Worth
  revisiting once several sections' worth of scenario tests exist and a stress-test layer on
  top of them would add real value beyond what the scenario tests already catch.
