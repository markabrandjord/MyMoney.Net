# Business-layer subsystem migration: phase 1 (Taxes, StockQuotes, Importers, Ofx-parsing)

## Context

Issue #1 (3-tier architecture epic) is functionally done: `MyMoney.Business`/`MyMoney.Data`
exist as separate class libraries, `LayerBoundaryTests` enforce the WPF-free boundary, and
item #3's two remaining sub-slices (SQL Server contract-test flavor, old #19; UiDispatcher
portability, old #7) are both closed. What's left is formal closure, not open work.

Issue #5 is the follow-on: the WPF UI project (`Source/WPF/MyMoney`, 180 files) is still
~3x the size of `MyMoney.Business` and ~6x `MyMoney.Data`, and several logic-heavy folders —
`Importers/`, `Ofx/`, `Reports/`, `Taxes/`, `Charts/`, `StockQuotes/` — sit inside the UI
project rather than the business layer. #5's own survey (re-verified against the current
tree while writing this spec) found:

| Folder | Files | Lines | WPF-coupled files |
|---|---|---|---|
| Taxes | 5 | 1,189 | 0 |
| StockQuotes | 9 | 4,108 | 3 |
| Importers | 7 | 2,217 | 5 |
| Ofx | 8 | 10,313 | 2 |

Taxes/StockQuotes/Importers/Ofx-parsing are relocations, not redesigns — WPF coupling is
either zero or already sits behind a clean seam (`IOnlineService` for StockQuotes providers,
`Importer.Import(string file)` for Importers, the Ofx parsing/download split). Reports needs
a genuinely new data-layer query/aggregation API (`IDatabase` currently only does whole-graph
`Load()`, not filtered/subtotaled queries) and is explicitly **out of scope for this phase** —
tracked as its own future effort.

`MainWindow.xaml.cs` (5,317 lines) was surveyed separately: most of it is legitimate UI
(command `CanExecute`/`Executed` plumbing, navigation, dialog-opening menu handlers, panel
show/hide) but a real subset is decision logic wearing a UI costume — file/database lifecycle
(`LoadDatabase`, `CreateNewDatabase`, `Save`/`SaveIfDirty`, `SaveAsSqlCe`/`SaveAsSqlite`/
`SaveAsXml`/`SaveAsBinaryXml`, `ExportCsv`) and import dispatch (`ImportQif`, `ImportOfx`,
`ImportXml`, `ImportMoneyFile`, `ImportCsv`). The import-dispatch half is the same seam as
the Importers migration, so it's folded into this phase rather than deferred.

## Goal

Move Taxes, StockQuotes, Importers, Ofx-parsing, and MainWindow's file/database-lifecycle +
import-dispatch logic into `MyMoney.Business`, each with real business-layer tests, so that
this logic can be exercised in the fast business-layer TDD loop instead of requiring the full
WPF app and an interactive desktop.

## Non-goals (this phase)

- **Reports.** Needs a new business-layer query/aggregation API; its own future spec.
- **The rest of MainWindow's orchestration** (navigation, command plumbing, dialog-opening
  handlers, panel show/hide). Legitimately UI; not touched here.
- **Charts.** Mostly genuine UI controls; `ChartData`/`CategoryData`/`RentalData` are
  candidates for a future pass but aren't part of this one.
- **#4's dual-engine parity test and FlaUI UI-level parity test.** Different concern (storage-
  engine correctness, not business-logic-relocation correctness) and dual-engine is SQL-
  Server-flavored, which isn't an upstream concern. Only #4's headless-generator/canary/
  invariant pieces are pulled into this phase, as the regression safety net (see below).
- **Formal closure of #1.** Should happen alongside this work (it's genuinely done), but
  isn't itself a design question.

## Migration pattern (applied per subsystem)

One subsystem per branch/PR, same shape each time — this is the pattern that already worked
for the original layer extraction and the SaveOne/SaveBatch entity-by-entity persistence
work: small, independently reviewable, bisectable if something regresses.

1. Move the subsystem's files from `Source/WPF/MyMoney/<Folder>` into
   `Source/WPF/MyMoney.Business/<Folder>`.
2. For WPF-coupled files in that subsystem, cut the seam: extract the WPF-touching piece
   behind an interface (see "Test subsystem shape" below), leave the interface's real
   implementation wherever it needs to live (business layer if it's just data shaping,
   WPF project if it's genuinely UI), and have the business-layer code depend only on the
   interface.
3. Add `LayerBoundaryTests` coverage confirming the moved files carry zero
   `System.Windows`/`PresentationFramework`/`PresentationCore` references.
4. Add real business-layer tests for the moved logic (see below).
5. Update the WPF project's call sites to reference the new namespace; confirm
   `dotnet build`/`dotnet test` are green.
6. Run the phase-1 regression safety net (canary + invariants) against the moved subsystem
   before merging.

### Order

1. **Taxes** — zero WPF coupling, smallest, proves the pattern end-to-end cheapest.
2. **StockQuotes** — `IOnlineService` seam already exists; migration is mostly moving files
   and mocking that one interface for the 3 WPF-coupled files.
3. **Importers + MainWindow's import dispatch** — same subsystem, same PR: moving
   `Importers/` without also moving the `MainWindow.xaml.cs` methods that call it would leave
   the seam half-migrated.
4. **MainWindow's file/database lifecycle** (`LoadDatabase`/`Save*`/`ExportCsv`) — folded
   into the same PR as #3 (adjacent, small, and reviewing MainWindow's changes once instead
   of twice is worth more than splitting it), as its own commit within that PR so it's still
   independently bisectable.
5. **Ofx-parsing** — last, since at 10,313 lines it's the largest and (per the survey) the
   parsing half is clean but sits alongside the download half (`OfxDownloadController.cs`),
   which needs the same mock-the-network-boundary treatment as StockQuotes. Doing it last
   means the mocking pattern is already proven twice by the time this one needs it.

## Test subsystem shape

The four subsystems (plus the MainWindow slice) need to serve two distinct testing shapes,
not three — Reports' "computation entangled with rendering" shape is out of scope here:

- **Pure computation** (Taxes, Ofx parsing, the CSV/QIF/XML importers' core `Import(string
  file)` methods): file/bytes in, mutations to a `MyMoney` object graph or a computed value
  out. Test directly against a `MockDatabase`-backed `MyMoney` instance — no mocking needed
  beyond what `MyMoney.TestSupport` already provides from the #1 test-support-library work.
- **External I/O behind an interface** (StockQuotes providers behind `IOnlineService`, Ofx's
  download half behind `OfxDownloadController`, MainWindow's file-lifecycle methods that
  talk to `IDatabase`): test by substituting a fake/mock implementation of the existing
  interface. No new mocking infrastructure needed — `IOnlineService` and `IDatabase` are
  already interfaces; this phase adds test doubles for them where none exist yet, following
  the same shape as the existing `MockDatabase`.

No new "test subsystem" framework is being built in this phase — the shared `IDatabase`
contract-test pattern from #1 (`MyMoney.TestSupport/DatabaseContractTests*.cs`,
`MockDatabase.cs`) already covers the data-layer half, and the two shapes above reuse that
pattern rather than inventing a new one. If a third genuinely different testing shape shows
up during Reports' future phase, that's the point to reconsider whether a more formal test
subsystem is warranted — not before, per YAGNI.

## Regression safety net (from #4)

Before/after each subsystem's migration, run:

- **Headless `SampleDataGenerator` entry point.** `SampleDataGenerator` already lives in
  `MyMoney.Business` (from the #1 layer extraction) and takes no WPF `Application` context —
  but it's currently only invoked from the WPF-hosted `SampleDatabase.Create()` wizard, which
  extracts the embedded `SampleStockQuotes.zip`/`Database/SampleData.xml` resources first.
  Add a headless entry point that does that extraction without the dialog, so the safety net
  can run in a plain unit test.
- **Seed the generator's `Random`.** `SampleDataGenerator` currently uses an unseeded
  `Random`. Add a seeded-constructor overload so a fixed seed produces a deterministic
  object graph.
- **Seed-derived canary.** One account/report total that's deterministically derivable from
  the fixed seed, checked into the test.
- **Property-based invariants.** Splits sum to transaction amount, transfers balance,
  category totals reconcile — checked after each subsystem's migration to confirm the moved
  code didn't change behavior, independent of the canary.

This is deliberately the minimum slice of #4 needed to validate phase 1, not the full issue.

## Roadmap (sketch only — future phases get their own spec)

1. **Phase 1 (this spec):** Taxes → StockQuotes → Importers + MainWindow import/lifecycle →
   Ofx-parsing, each with real business-layer tests, validated by the canary + invariants.
   Formally close #1 alongside this work.
2. **Phase 2 (future):** Reports redesign — new business-layer query/aggregation API
   (filter by account/date-range/reconciliation-status, subtotal by account/month/year/
   payee), separating computation from `IReportWriter` rendering. Needs its own spec; the
   `IDatabase` query surface this requires doesn't exist yet.
3. **Phase 3 (future, maybe):** Remaining MainWindow orchestration, if it ever becomes a
   real blocker rather than a UI concern. Not committed to.
4. **Deferred from #4:** dual-engine parity test, end-to-end FlaUI UI-level parity test —
   pick up independently if/when they're actually needed.

## Testing

- Each subsystem's migration: `dotnet build Source/WPF/MyMoney.sln`,
  `dotnet test Source/WPF/UnitTests/UnitTests.csproj`,
  `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj`, plus the new
  business-layer tests for that subsystem.
- `LayerBoundaryTests` after each move, confirming zero WPF-family references in the moved
  files.
- Canary + property-based invariants run against the full moved set at the end of phase 1
  (not just per-subsystem), to catch any cross-subsystem interaction the per-subsystem tests
  missed.
- FlaUI smoke test (`PayeeSelectionTests` or equivalent) re-run interactively at the end of
  phase 1, same verification step used at the end of the original #1 layer extraction.

## Open questions carried forward (not blocking phase 1)

- Reports' new query/aggregation API shape, and whether it should be reusable by Importers/
  Charts' data-prep classes too — phase 2's question, noted in #5, not re-litigated here.
- Whether MainWindow's remaining orchestration is ever worth untangling — explicitly
  deferred, not scheduled.
