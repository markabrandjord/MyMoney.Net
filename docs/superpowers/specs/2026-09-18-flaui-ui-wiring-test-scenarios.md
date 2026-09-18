# FlaUI UI-wiring test scenarios

## Purpose and scope — read this before adding a scenario

This document catalogs UI-level test scenarios for the dialogs the
business-layer subsystem migration (issue #1/#5,
`docs/superpowers/specs/2026-09-18-business-layer-subsystem-migration-design.md`)
rewired to call into `IBusinessLayerUiCallback`/`IImportProgressReporter`
instead of doing WPF work directly.

**This is deliberately narrow.** The business-layer logic behind each of
these dialogs — what happens with the data once a business-layer API
receives it, including invalid-input handling — is already covered (or
should be covered; see the "Business-layer coverage" column) by direct
unit tests in `Source/WPF/UnitTests/` that call the API directly, with no
UI involved: `DatabaseLifecycleTests.cs`, `QifImporterTests.cs`,
`XmlImporterTests.cs`, `CsvTransactionImporterTests.cs`,
`CsvImportControllerTests.cs`, `ExportersTests.cs`,
`StockQuoteManagerDownloadLogTests.cs`, `ExchangeRateServiceTests.cs`,
`OfxObjectModelTests.cs`. **Do not duplicate that coverage here.** A
scenario in this document exists only to answer one narrow question: *does
the real WPF dialog, when a person actually interacts with it, correctly
collect the input and correctly call the business-layer API with it* — the
wiring, not the logic behind the wire.

If a scenario's business-layer coverage doesn't exist yet, that's a gap to
close with a direct unit test (cheap, fast, no UI) — not a reason to write
a heavier FlaUI test that re-proves the business logic through the UI.

## Conventions to follow when implementing a scenario

Based on the existing pattern in `Source/WPF/UITests/PayeeSelectionTests.cs`:

- Live in `Source/WPF/UITests/`, use `FlaUI.Core`/`FlaUI.UIA3`, NUnit.
- Use a checked-in fixture database (`Source/WPF/UITests/Fixtures/`) rather
  than building state through the UI first, where practical.
- If the scenario needs a registered database (post-#32, File|Open is a
  `DatabaseRegistry` picker, not a free-text browser), register the
  fixture against the real default registry path in the test and remove
  it in a `finally`/`TearDown` — there's no per-test registry override.
- Clean up the `Application`/`UIA3Automation` in `[TearDown]`, best-effort,
  so a cleanup failure doesn't mask the actual test failure.
- Per this repo's established FlaUI gotchas (see the `flaui-wpf-testing`
  skill): these tests need a real interactive desktop session to run
  correctly — they are not run via plain `dotnet test` in CI or in an
  unattended/background agent session (launching the real app this way
  hangs waiting for input that never arrives — confirmed directly during
  this migration, see the ledger for
  `docs/superpowers/plans/2026-09-18-business-layer-subsystem-migration.md`'s
  Task 1).

## Status legend

- **Not started** — no test exists yet.
- **Business-layer gap** — the underlying business-layer coverage this
  scenario would sit on top of doesn't exist yet either; close that first.
- **Written** — a FlaUI test exists; note the file.
- **Passing** — last confirmed passing in an interactive session, with date.

## Scenarios

### File lifecycle (Task 7 — `DatabaseLifecycle`)

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
|---|---|---|---|---|---|
| New database (SQLite) | File \| New \| SQLite (or the non-DEBUG default) | `DatabaseLifecycle.Open` via `MainWindow`'s call site | New `.mmdb` file created and opened, empty | New temp filename | Not started |
| Open database, no upgrade needed | File \| Open, pick a current-schema fixture | `DatabaseLifecycle.Open` | Opens normally, data visible | A current-schema fixture (e.g. reuse `PayeeSmokeTest.mmdb`) | Not started |
| Open database, upgrade required, user accepts | File \| Open an old-schema fixture, click **Yes** on the upgrade prompt | `DatabaseLifecycle.Open` → `IBusinessLayerUiCallback.ConfirmWithDetails` returns `true` → engine `Upgrade()` runs | Database upgrades and opens; password persisted before load (per `DatabaseLifecycleTests`' already-covered ordering) | An old-schema fixture (needs creating/checking in) | Business-layer gap: confirm `DatabaseLifecycleTests` actually has an accept-path case with a *real* pre-upgrade fixture, not just a faked accept; then write this |
| Open database, upgrade required, user declines | Same fixture, click **No** | Same path, `ConfirmWithDetails` returns `false` | Load aborts, no password written, no crash | Same old-schema fixture | Not started (business-layer decline path already covered by `DatabaseLifecycleTests`, per the final review — this scenario only needs to prove the real dialog's No button actually returns `false` to the callback) |
| Save / Save As, each format | File \| Save, then Save As into `.mmdb`/`.xml`/`.bxml`/SQL CE `.sdf` | `MainWindow`'s `Save`/`SaveAs*` methods (not extracted — see the migration's own rationale for why) | File written, reopens with same data | A small fixture with a few transactions | Not started |
| Export to CSV | File \| Export \| CSV | `Exporters.Export`/`ExportPrompt` → `IBusinessLayerUiCallback.PromptSaveFileName`/`OpenExportedFile` | Exported file opens and contains expected rows | A small fixture | Not started (business-layer formatting already covered by `CsvTransactionFormatTests`/`ExportersTests` — this only proves the dialog wiring and that the file actually opens afterward) |

### CSV import (Task 6 — `CsvImportController`/`CsvTransactionImporter`)

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
|---|---|---|---|---|---|
| CSV import, column mapping | File \| Import a CSV without a saved mapping | `IBusinessLayerUiCallback.PromptForCsvFieldMapping` → `CsvImportController.ImportCsv` | Mapping dialog appears pre-populated from headers; after mapping, transactions import correctly | A small CSV with a header row not matching a saved `CsvMap` | Not started (mapping/import logic covered by `CsvTransactionImporterTests`/`CsvImportControllerTests` — this proves the real dialog produces a `CsvMap` that round-trips correctly) |
| CSV import, account picker | Import a CSV without an "Account Number" column | `IBusinessLayerUiCallback.PickAccount` | Account-picker dialog appears; selected account receives the imported transactions | A small CSV with no account-number column | Not started |
| CSV import, malformed file | Import a CSV that fails to parse | `ShowError` (or whichever method now handles this) | Error dialog shown, app doesn't crash, no partial import | A deliberately malformed CSV | Not started |

### Stock quotes (Task 5 — `StockQuoteManager`)

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
|---|---|---|---|---|---|
| Stock quote download error | Trigger a quote download with a bad/expired API key | `IBusinessLayerUiCallback.ClearOutputLog`/`AppendErrorLog` | Output pane shows the error, log-file link (if present) actually opens the log | An intentionally-invalid API key in test settings | Not started |

### OFX download (Task 8 — `Ofx.cs`/`OfxDownloadController`)

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
|---|---|---|---|---|---|
| OFX download, protocol error | Trigger a download against a mock/test server returning a malformed OFX response | `IBusinessLayerUiCallback.ConfirmWithDetails` (CHALLENGERQ/PINCHRQ paths) or the generic error path (Task 8's restored `error.GetType().FullName + ": " + error.Message` + stack trace rendering) | Generated `OfxErrorTemplate.htm` report actually contains the exception type name and stack trace, matching what Task 8's fix restored | A test OFX server/fixture returning a malformed response (needs building — may not exist yet) | Business-layer gap: no test infrastructure exists yet for driving a fake OFX server response; scope that before attempting this UI test |
| MFA challenge | Trigger a download requiring MFA | `MfaChallengeDialog` | Dialog renders challenge phrases from `MfaPhrases.xml` (the embedded-resource lookup Task 8 fixed) correctly | Needs a bank/test account that actually triggers MFA — likely the hardest scenario to set up | Not started, lowest priority (also flagged in the migration's own final review as read-verified-only, no realistic manual step reaches this today) |

## Adding a new scenario

Add a row to the relevant table (or a new table for a new area) with the
same six columns. Keep the "Business-layer coverage" judgment explicit —
if you're not sure whether the underlying API is already unit-tested,
check before assuming a FlaUI test is the right next step.
