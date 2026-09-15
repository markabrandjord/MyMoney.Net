# FlaUI Test Harness — Design Spec

## Status

Design phase. This is item #4 of the five-item 3-tier architecture
initiative (see the tracking work item, GitHub issue #1, for the full
list). Items #1 (business/data layer extraction) and #3
(business-layer test-support library) each get their own spec.

## Context

Item #2 (SQL Server dual-engine support) needed to verify, against a
real running instance of the app, that a specific bug fix (a crash when
selecting a payee, caused by a null `DatabasePath` on the new SQL
Server-backed data engine) actually worked. That verification attempt
used raw Windows UI Automation — `System.Windows.Automation` directly,
simulated mouse clicks via `mouse_event`, `PrintWindow` for
screenshots — driven ad hoc via PowerShell scripts written during that
session. It did not work reliably: simulated clicks on the exact right
pixel coordinates didn't register, `InvokePattern.Invoke()` on WPF menu
items didn't reliably trigger the underlying command, and a native
File→Open dialog could not be driven open at all across three different
attempts. The attempt was abandoned rather than continuing to guess at
a fourth approach.

This is also exactly the gap the original 3-tier initiative already
identified: `Source/WPF/ScenarioTest` (a pre-existing model-based
integration test driven by `TestModel.dgml`) uses this same category of
hand-rolled, raw UI Automation (`ScenarioTest/Input.cs`) rather than a
purpose-built automation library. FlaUI wraps the identical underlying
UI Automation API with a higher-level, purpose-built interface —
reliable element finding, retry/wait-for-condition helpers, and
`Click()`/`Invoke()` methods that correctly handle focus and command
routing — specifically designed to solve the class of problem just
encountered.

## Goals

- Prove that FlaUI can reliably drive the WPF app through a real user
  scenario — launch, open a database file via the native File→Open
  dialog, navigate the UI, select an item — where raw UI Automation
  could not.
- Produce a real, committed, reusable test asset (not more scratch
  automation scripts), scoped tightly enough to land quickly.
- Establish the pattern (project structure, package choices,
  DEBUG-only build) that later, broader UI test coverage can build on,
  without committing to that broader scope now.

## Non-Goals

- Replacing or modifying `ScenarioTest`/`TestModel.dgml` in any way.
  This is a separate, independent project; `ScenarioTest` is untouched.
- Building a general-purpose test framework, DSL, or reusable
  page-object abstraction layer on top of FlaUI. This spec is one
  direct, straightforward test. Generalizing patterns can happen once
  there's more than one test to generalize from.
- Testing the SQL Server engine path (item #2). GitHub issue #5
  (intermittent `Microsoft.Data.SqlClient` connectivity specifically
  when hosted inside `MyMoney.exe`) is open and unresolved; running a
  SQL-Server-backed scenario through this harness now would conflate
  "does FlaUI work" with "is the connection flaky today," making
  failures ambiguous to diagnose. Deferred until issue #5 is resolved.
- CI integration or automated build-gate wiring. This is a manually-run
  DEBUG developer tool for now, the same maturity level item #2 started
  at.
- Any production/shipped use. DEBUG-only, developer-workstation
  tooling, consistent with every other item in this initiative.

## Architecture Overview

A new, independent test project drives the built `MyMoney.exe` as an
external process (not in-process — this is how FlaUI works: it
automates the app from the outside, the same way a human user would,
rather than linking against its code):

```
Source/WPF/UITests/
├── UITests.csproj              -- net10.0-windows7.0, DEBUG-only
├── PayeeSelectionTests.cs      -- the one test
└── Fixtures/
    └── PayeeSmokeTest.mmdb     -- checked-in SQLite fixture: one
                                    account, one payee, one transaction
```

- **Packages:** `FlaUI.Core` + `FlaUI.UIA3` (the UI Automation 3
  backend — the modern, actively-maintained one). Exact versions
  pinned at implementation time.
- **Test runner:** NUnit, consistent with the existing
  `UnitTests.csproj` convention. FlaUI itself is test-runner-agnostic —
  it only drives the app; NUnit supplies the `[Test]`/assertion
  structure around it.
- **DEBUG-only:** this project (or its build/execution) is gated the
  same way `MyMoneyAdmin`'s Release exclusion works for item #2 — not
  part of what a Release build produces or needs.
- **No in-process coupling:** `UITests.csproj` does not need a
  `ProjectReference` to `MyMoney.csproj` for anything beyond knowing
  the built exe's output path (a constant/configured path, not a
  compile-time dependency) — it automates the compiled application,
  the same binary a real user runs.

## The First Test

This is the concrete acceptance criterion for this spec — if FlaUI
cannot reliably do this, that needs to be known now, before any
further investment in it:

1. Launch `MyMoney.exe` via `FlaUI.Core.Application.Launch(...)`,
   passing whatever arguments are needed to ensure a clean, predictable
   startup state (no auto-loaded database from a prior session's
   settings).
2. Use FlaUI to drive the **File → Open** menu command, then interact
   with the resulting native common file dialog to select
   `Fixtures/PayeeSmokeTest.mmdb`. This is deliberately the exact
   interaction raw automation could not complete — FlaUI's dialog and
   window-handling support is a purpose-built feature, not an
   afterthought, so this is the first real test of whether it actually
   solves the problem.
3. Assert the database loaded: the main window's title reflects the
   opened file, and/or the Accounts/Payees tree is populated.
4. Expand the Payees section and confirm the fixture's known payee
   (e.g. "Payee Smoke Test") is present.
5. Select that payee row.
6. Assert the app is still responsive after selection (FlaUI can query
   window state / responsiveness), and that no unhandled-exception
   dialog appeared. This step is the one that failed manually multiple
   times during item #2's verification (a null-`DatabasePath` crash in
   `MainWindow.UpdateCaption`, on the SQL Server engine specifically —
   not expected to reproduce here since the fixture is a normal SQLite
   file with a real `DatabasePath`, but the *mechanism of automating
   the click itself* is exactly what's being proven here, independent
   of which engine is behind it).

## Fixture File

`Fixtures/PayeeSmokeTest.mmdb` is a small, checked-in SQLite database —
generated once (the same way item #2's live-testing scratch script did
it: construct a `MyMoney` object with one `Account`, one `Payee`, one
`Transaction`, then `SqliteDatabase.Create()` + `.Save()`) and committed
as a binary fixture, not regenerated per test run. Deterministic, fast,
no dependency on any external state.

## Sequencing / Dependencies

- Independent of item #1 (business/data layer extraction) and item #3
  (business-layer test-support library) — no dependency either
  direction.
- Does not depend on item #2 being fully complete, but its *purpose*
  is partly motivated by item #2's verification needs. Once this
  harness exists and issue #5 (intermittent SQL Server connectivity)
  is resolved separately, a natural follow-up is a second FlaUI test
  covering the SQL-Server-backed payee-selection scenario that
  motivated this work in the first place — not part of this spec's
  deliverable.
- `ScenarioTest` is unaffected and untouched.

## Open Questions / Future Work

- Whether to eventually fold `ScenarioTest`'s scenarios onto FlaUI
  (the original, broader framing of item #4) is explicitly deferred —
  this spec takes the narrower path by design (see Non-Goals).
- CI/build-gate integration, if ever wanted, is future work.
- Whether the fixture file should be regenerated by a script at test
  time instead of checked in as a binary — checked-in was chosen for
  simplicity and determinism; revisit if the fixture needs to grow
  significantly or if binary-diff noise in the repo becomes a problem.
