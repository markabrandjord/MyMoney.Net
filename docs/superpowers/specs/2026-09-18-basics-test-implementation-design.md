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
   `FlaUI.Core`/`FlaUI.UIA3`, NUnit, the shared Basics fixture (below), `DatabaseRegistry`
   registration/cleanup, `[TearDown]` cleanup.
4. **Run the FlaUI test live**, with the user watching, using real `Keyboard.Type` +
   deliberate pauses + `SetForeground` (not instant `SetValue`) per the established
   watching-live convention. This is the only step that needs an interactive desktop session.
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

### Shared fixture

One checked-in FlaUI fixture, `Source/WPF/UITests/Fixtures/BasicsFixture.mmdb`, seeded once
with enough data to cover most Basics scenarios in a single file (mirrors
`PayeeSelectionTests.cs`'s existing single-fixture pattern):

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

Business-layer tests do **not** use this fixture — they build their own minimal in-memory
`MockDatabase`/`MyMoney` state per the existing convention, one setup per test class.

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

## Testing/tooling notes

- Business-layer tests: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter
  "Name=TestMethodName"` or a class-level filter — never the whole-solution `dotnet test
  Source/WPF/MyMoney.sln` (pulls in FlaUI `UITests`/`ScenarioTest`, hangs unattended).
- FlaUI tests: run only in a live interactive session with the user watching; never dispatched
  to an unattended/background agent.
- No new mocking framework, no new test-support abstractions — reuse what Task 10 and the
  original migration already established.

## Open questions / future work

- The exact enforcement point for "can't delete a transaction whose transfer partner is
  reconciled" and "can't move a Reconciled transaction" hasn't been traced to a specific line
  yet (flagged in the tracking doc as "not fully verified") — resolving this is part of writing
  the Splits & Transfers business-layer tests, not a blocker to starting.
- Attachments' `AttachmentManager` extraction is deferred to a follow-up issue, filed once this
  section reaches that subsection (not filed speculatively now).
