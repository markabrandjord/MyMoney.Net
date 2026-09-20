# UI/UX modernization: migrate to WPF-UI (lepoco), design spec

Tracks: [markabrandjord/MyMoney.Net#42](https://github.com/markabrandjord/MyMoney.Net/issues/42)

## Purpose

Bring the app's UI/UX up to the current Windows 11 / Fluent Design standard, replacing the
already-dead `ModernWpfUI` dependency, via a multi-step migration where each step's FlaUI test
coverage is reviewed and updated to match what that step actually changed — not a big-bang
rewrite followed by a big-bang test fix-up.

## Background

The app already partially uses `ModernWpfUI` 0.9.7-preview.2 (a third-party Fluent-style WPF
library) in 22 files, including the shared `Themes/generic.xaml` that other controls inherit
from — not a cosmetic add-on confined to leaf screens. That dependency's 0.9.x line is confirmed
**frozen and unsupported**, with no further releases planned, so moving off it is mandatory
regardless of which direction this migration takes.

Per this repo's own `CLAUDE.md`, this checkout is `markabrandjord`'s fork, being evaluated as a
Quicken-replacement feasibility study. That uncertainty was explicitly raised and weighed before
committing to this effort (see "Decision to proceed now" below) — the requester's judgment,
recorded here, is that the migration will succeed with enough work, and the UI modernization
effort is worth starting now rather than waiting for that question to fully resolve.

## Options considered

A six-perspective expert panel (architect, developer, tester, UI/UX designer, deployment/
packaging expert, and a deliberately adversarial risk officer/"pessimist") independently
evaluated four candidate directions:

1. **`lepoco/wpfui`** ("WPF-UI") — actively-maintained third-party Fluent library, used in
   production in Microsoft PowerToys, independently styled (owns its whole styling stack rather
   than depending on WPF's own still-maturing built-in theme). Provides genuine Fluent
   *shell* patterns (NavigationView, Mica/acrylic backdrops, title-bar customization), not just
   control-level restyling.
2. **`pipeline-foundation/ModernWpf` 1.x** — a community fork of the abandoned Kinnara/ModernWpf
   project, currently release-candidate status (`1.0.0-rc.1`), built on top of .NET 10's own
   official `PresentationFramework.Fluent` theme.
3. **.NET 10's built-in Fluent theme only** (`PresentationFramework.Fluent.dll`,
   `Window.ThemeMode`) — first-party, ships with the runtime, zero new NuGet dependency. Real,
   confirmed limitation: it is not loaded as a true system theme architecturally — any
   custom-styled control must be individually reworked to inherit it correctly, or it renders
   broken (a documented real-world case: a custom-styled button rendered as "a big white box").
   Provides no app-shell/navigation-pattern support at all.
4. **Native WinUI 3 / Windows App SDK port** — not source-compatible with WPF; would be closer to
   a rewrite than a migration for this DataGrid/custom-control-heavy app.

### Rejected: native WinUI 3 port

Unanimous across the panel. Beyond not being source-compatible (forcing a rewrite of all 69
XAML files' worth of custom controls, the `MoneyDataGrid`, and `TransactionConnectorAdorner`),
it also forces a full deployment-pipeline replacement: the app ships via ClickOnce today, and
ClickOnce cannot publish a WinUI3/Windows App SDK app — MSIX or a self-contained multi-hundred-MB
payload would be required, with a different signing/update model entirely, no incremental path
from the current pipeline. The rewrite cost and the deployment-re-platform cost compound rather
than sharing any infrastructure. Ruled out.

### Rejected: `pipeline-foundation/ModernWpf` 1.0.0-rc.1

Deprioritized by most of the panel. Adopting a pre-1.0 fork of the exact library-lineage
(`ModernWpfUI`) already confirmed dead in this repo repeats the same risk pattern one layer
removed, for no clear benefit over option 1 that isn't also available more safely elsewhere.
Ruled out as the primary path.

### Considered and set aside: .NET 10's built-in Fluent theme (option 3)

Genuinely attractive on paper (first-party, zero dependency, lowest long-run maintenance risk),
and the panel split on whether to spike it first before committing to option 1. Set aside as the
*primary* direction for this migration because:

- It provides no app-shell/navigation pattern support, so it doesn't move the "feels like a
  modern Windows 11 app" needle the way option 1 does (per the UI/UX review).
- Its confirmed "not a true system theme" limitation means the migration cost is dominated by
  per-control rework regardless — and that cost is the *least* predictable of the options (the
  panel's own estimates for affected file/control counts already grew once during review, before
  any implementation was attempted), which is the wrong risk profile to accept as the default
  path.
- `lepoco/wpfui`'s independent styling stack and production track record (PowerToys) make it the
  safer near-term bet per the architect's review, without foreclosing a later move to the
  first-party theme once it matures — that move will cost real per-control rework whenever it
  happens, for either starting point, so there's no "buy now, save later" argument for starting
  with option 3 instead.

### Selected: `lepoco/wpfui`

## Rejected assumption: "a thin app layer makes this cheap to change later"

The requester's original hope was that keeping the app layer thin would make switching between
these options cheap in the future. The architect and developer reviews independently reached the
same conclusion: **this does not hold for a styling/theming dependency.** Unlike a
data-access or business-logic dependency (where an interface genuinely isolates callers from an
implementation), a styling library's coupling point is the XAML resource system itself —
`{StaticResource}`/`{DynamicResource}` lookups, `xmlns:ui=` namespace usage, and control
templates referenced directly throughout markup across dozens of files. There is no facade to
insert between a `<ui:NavigationView>` element and its rendering. A thin resource-key
indirection layer (remapping semantic brush names to whichever library's underlying resource) is
worth doing and reduces *color*-level switching cost at the margins, but it does nothing for the
dominant cost driver: every custom control's template has to be individually reconciled with
whichever library owns Fluent styling, and that cost is roughly fixed regardless of how "thin"
the application code above it is. This should not be treated as a reason to prefer one option
over another going forward.

## Scope

**In scope:**

- Replace `ModernWpfUI` with `lepoco/wpfui` across the app.
- Fix the duplicate-transaction-merge UI's interaction pattern (the connector-overlay described
  in #42's own comment) as part of this effort — not deferred to a separate initiative.

**Out of scope (explicitly deferred, not decided against):**

- Native WinUI 3 port — rejected above, not merely deferred; would need to be reopened as its own
  decision if ever reconsidered.
- The RC `pipeline-foundation/ModernWpf` fork — rejected above.
- A broader shell/navigation-architecture redesign (e.g. a `NavigationView`-style persistent side
  rail replacing the current menu-bar/toolbar/docked-tree layout). The UI/UX review's own
  assessment is that this is real, valuable modernization work, but large enough to need its own
  separate justification and design pass — not bundled into this migration.
- A broader audit of other bespoke/non-standard interaction patterns elsewhere in the app beyond
  the duplicate-transaction dialog (which is in scope because it was already specifically flagged
  in #42). Worth a follow-up work item once this migration lands.
- Confirming whether this fork can actually publish via ClickOnce at all today — the deployment
  review found the existing ClickOnce signing certificate and web-hosting URL may belong to the
  upstream maintainer, not this fork. This is orthogonal to the library choice and doesn't block
  this design, but should be verified before this work is considered meaningfully "shippable,"
  not just buildable.

## Phased plan

### Phase 0 — Spike: de-risk the worst case first

Before touching any real screen, install `lepoco/wpfui` and prove it against `MoneyDataGrid`'s
edit-mode template swap specifically (one grid, one column) — this is the single
highest-uncertainty, highest-blast-radius pattern in the app (custom cell templates that swap a
read-only `TextBlock` for an editable `ComboBox`/`TextBox` on `BeginEdit`, already a documented
source of real bugs in this codebase's FlaUI test history independent of any theming change).
If this spike reveals a dead end that can't be reasonably resolved with style overrides, stop and
reconsider before Phase 1 — cheaper to find out on one column than after 20 dialogs are
converted.

### Phase 0 Finding

**Result: PASS** ✓

The spike test (`Source/WPF/UnitTests/WpfUiGridEditSpikeTests.cs`) confirmed that WPF-UI's theme
resources are **fully compatible** with `MoneyDataGrid`'s edit-mode template-swap pattern:

- Themed `ComboBox` rendered successfully when swapped into `DataGridCell` edit mode
- Rendered dimensions verified: `ActualWidth > 0` and `ActualHeight > 0`
- No "big white box" visual failure occurred

**Plan inaccuracies found and corrected during Task 3 implementation:**

1. The plan's verbatim spike-test code referenced `window.SpikeGrid` for direct field access, which
   fails at compile time across assemblies (the auto-generated XAML field is marked `internal`).
   Corrected via `FindName("SpikeGrid")` to retrieve the field at runtime.

2. The spike test requires `using System.Windows.Controls.Primitives;` for the `DataGridCellsPresenter`
   type — this was missing from the plan's verbatim code snippet and was added during implementation.

Both corrections were verified by the task's reviewer. They are minor plan documentation inaccuracies,
not functional issues, and do not affect the spike result.

**Conclusion:** Phase 1 can proceed as planned. No escalation or design reconsideration needed.

### Phase 1 — Shell

Remove `ModernWpfUI`, add `lepoco/wpfui`, and migrate `MainWindow`'s chrome (menu, toolbar,
toolbox accordion, status bar) as one atomic unit. This chrome is visible on every screen
simultaneously, so it cannot be partially migrated without looking visibly broken — running two
theme systems' implicit (type-targeted) styles in the same visible surface produces inconsistent
corner-radii, accent mechanisms, focus visuals, and padding, because whichever resource
dictionary is merged last/closest wins per-control, not per-window.

### Phase 1 Finding

**Result: shipped, with corrections below.** "Remove `ModernWpfUI`" (this section's opening line)
did **not** happen — implementation found that removing it broke 8 files not yet migrated
(`AppBarButton`/`CommandBar`/`TextBoxHelper`/`DropDownButton` usages) plus `Dark.xaml`/`Light.xaml`
(below), so `App.xaml` instead merges both libraries under separate `ui:`/`mwpf:` prefixes, with
WPF-UI merged last so its implicit styles win on every overlapping key. `ModernWpfUI`'s removal is
still outstanding, deferred to whichever future plan finishes migrating those 8+2 files.

**Phase 1 restyled more than `MainWindow`'s chrome — the whole app, silently.** Found during the
final whole-branch review (2026-09-20), not anticipated when Task 5 was planned: WPF-UI's
`ControlsDictionary` and `ModernWpfUI`'s own resources share **52 resource key names** (e.g.
`DefaultButtonStyle`, `DefaultTextBoxStyle`, `DefaultComboBoxStyle`, `DefaultDataGridStyle`,
`DefaultDataGridCellStyle`, `DefaultListBoxItemStyle`, `DefaultTreeViewItemStyle`), referenced via
`BasedOn=`/`Style=` at **54 sites across roughly 20 XAML files** app-wide (`Themes/generic.xaml`,
`Views/QueryViewControl.xaml`, `Views/Controls/BalanceControl.xaml`, `Dialogs/RenamePayeeDialog.xaml`,
`Dialogs/CsvImportDialog.xaml`, `Dialogs/ReportRangeDialog.xaml`, `Views/Controls/AccountsControl.xaml`,
`Views/Controls/PayeesControl.xaml`, `Dialogs/CategoryDialog.xaml`, and others — not an exhaustive
list). Because WPF-UI is merged last, every one of those sites has been deriving from WPF-UI's
version of that key since Task 5 landed (`5d7f30dc`), not `ModernWpfUI`'s — Task 5's own
self-review incorrectly assumed the app was "functionally unchanged from a user's perspective."
Every `TargetType` matches between the two libraries (both target stock `System.Windows.*` types),
so this is a **visual/behavioral risk, not a crash risk** — nothing has thrown at load time — but
it means the true scope of what Phase 1 already touched is the whole app's stock-control styling,
not just `MainWindow`'s chrome as originally scoped. A short manual smoke pass across a handful of
dialogs (the ones listed above are a reasonable starting set) is recommended before/alongside
whatever phase is next, specifically to catch visual regressions this silent restyle may have
introduced outside `MainWindow`.

**Dark theme required an explicit second call, and initially didn't get one.** `App.xaml`'s
`ui:ThemesDictionary` is pinned to `Theme="Light"`; WPF-UI's own theming needs
`Wpf.Ui.Appearance.ApplicationThemeManager.Apply(...)` called explicitly to switch — it does not
follow `ModernWpf.ThemeManager`. Found and fixed 2026-09-20 (final review, before merge):
`MainWindow.xaml.cs`'s `OnThemeChanged` now calls both theme managers. Before this fix, toggling
to Dark (Ctrl+L, a first-class shipped feature) left every WPF-UI-styled stock control rendering
in Light colors against a dark app background — confirmed via live toggle before and after the
fix. The Basics FlaUI suite runs under the default Light theme and would not have caught this
either way; it's a manual-verification-only risk until/unless a FlaUI check for theme-applied
brush values is added.

**`SplitButton.Flyout` only accepts a real `System.Windows.Controls.ContextMenu`, not a
`Wpf.Ui.Controls.Flyout`.** Confirmed via reflection on the installed `Wpf.Ui.dll` and via
`SplitButton`'s real source (both the 4.3.0 tag and the unreleased `main` branch, checked
2026-09-20): `SplitButton.Flyout` is typed `object` — so assigning a `Flyout` to it compiles — but
its internal click handler only wires up and opens the popup when the assigned value is a
`ContextMenu`; anything else is silently inert (no error, no popup). The pending-changes Save
control (`MainWindow.xaml`'s `PendingChangeDropDown`) now uses a real `ContextMenu` hosting its
existing summary/Revert-Changes content — see the inline XAML comment at that control for the
full history (an intermediate two-Button-plus-hand-toggled-Flyout workaround was tried first,
fixed the "doesn't open" bug but lost the native split-button visual and desynced after an
outside-click dismissal; superseded by the `ContextMenu` approach, which a throwaway spike
confirmed avoids both problems). Any future `SplitButton` usage in this app should go straight to
`ContextMenu`, not rediscover this.

**`Themes/Dark.xaml` and `Themes/Light.xaml` also depend on `ModernWpfUI`** — via their own
file-local `xmlns:m="http://schemas.modernwpf.com/2019"` prefix and `m:DynamicColor` usage, found
during Task 6 (not caught by this spec's original `xmlns:ui=`-only file inventory). Neither file
is merged by `App.xaml` (only `generic.xaml` is) and neither was touched by this plan. Add both to
the file list for whichever future plan finishes removing `ModernWpfUI` — the true remaining count
is at least 8 (Scope, above) + these 2.

**Not done, deferred by design:** adopting `FluentWindow`/`ApplicationThemeManager.IsThemeAware`
for `MainWindow` itself (the direct replacements for `ui:WindowHelper.UseModernWindowStyle` and
`ui:ThemeManager.IsThemeAware`, removed without replacement in Task 6) — `MainWindow` still derives
from plain `Window`. Worth its own follow-up task rather than folding into a future phase's
existing scope, since it's a `MainWindow`-root-level change, not a per-control one.

### Phase 2 — Simple dialogs first

Convert the lower-risk dialogs first, to establish and document the resource-override
pattern/checklist before tackling the higher-risk cases in Phase 3.

### Phase 2 Planning Notes (forward-looking expert panel, 2026-09-20)

Before any Phase 2 implementation started, a two-panelist review (a WPF-UI/lepoco developer
expert and a Fluent UI/UX expert, working independently from the same real material: live
screenshots, the 24-file `Dialogs/` directory, and this app's own user docs) evaluated the actual
scope. Both panelists' full reports are the authority here; this is a synthesis.

**Real scope is smaller than "24 dialogs," but there's a shared landmine.** Only 5 dialog XAMLs
reference `ModernWpf` directly (`AttachmentDialog`, `CategoryDialog`, `MoneyFileImportDialog`,
`OnlineAccountDialog`, `PasswordWindow`) — the other 19 mostly need re-verification, not rework.
But `Dialogs/BaseDialog.cs` (every dialog's shared base) references 4 resource keys
(`SystemControlPageBackgroundChromeLowBrush`, `SystemControlPageTextBaseHighBrush`,
`SystemControlPageBackgroundChromeMediumLowBrush`, `DefaultListViewItemStyle`) confirmed present
**only** in `ModernWpf.dll`, never in `Wpf.Ui.dll` — verified by scanning both assemblies. These
resolve fine today because `ModernWpfUI` is still referenced, but the moment it's ever fully
removed, every dialog silently loses its background (no compile error, no exception — worst in
Dark mode). **Phase 2's actual first task**: define app-owned semantic aliases for these 4 keys in
`generic.xaml`, mapped to WPF-UI brushes, and repoint `BaseDialog` to them — before touching any
individual dialog.

**Sequencing correction**: start with `RenamePayeeDialog`, not `CsvImportDialog`. Despite its
name, `CsvImportDialog` is not a wizard (87 lines, a single field-mapping `ListView`) — it's not
the hard case this plan assumed. `RenamePayeeDialog` is the better first conversion: it's already
screenshotted as reference material, it's in the shared 52-key blast radius, and its
label-right/field-left layout is the template most of the other form-style dialogs copy.

**Bucket the 24 by actual fit**, not a uniform treatment:
- Fits `ContentDialog` cleanly (modal, single-task, one primary action) per the UX panelist:
  `OpenDatabaseDialog`, `NewSqliteDatabaseDialog`, `NewSqlServerDatabaseDialog`, `AddLoginDialog`,
  `PickDateDialog`, `MergeCategoryDialog`, `RecategorizeDialog`, `PasswordWindow`,
  `RenamePayeeDialog`, `MoneyFileImportDialog`, and others in that shape.
- Property-sheet forms — keep as resizable restyled `Window`s, do not force into `ContentDialog`:
  `AccountDialog`, `CategoryDialog`, `LoanDialog`, `ReportRangeDialog`, `FreeStyleQueryDialog`,
  `AttachmentDialog` (a document viewer — needs to stay resizable/non-modal-feeling).
- No strong Fluent prescription, need app-specific treatment: `CsvImportDialog` (a mapping table —
  add live preview of parsed rows, don't force a wizard shape it doesn't have), `OnlineAccountDialog`
  (a genuinely two-stage flow currently showing both stages at once — this one actually deserves a
  real stepper), `RentalDialog` (the only `TabControl` dialog — keep tabs, just restyle).
- **However**: the WPF developer panelist's counter-finding is that `ContentDialog` requires a
  `ContentDialogHost` in the visual tree and `MainWindow` is still a plain `Window` (`FluentWindow`
  adoption was explicitly deferred — see the Phase 1 Finding above). Recommendation: **stay with
  plain `Window`s, restyled**, for all of Phase 2. Revisit `ContentDialog` only if/when `FluentWindow`
  adoption is separately taken on — don't take on both changes in the same phase.

### Phase 3 — Transaction/split grid views

The DataGrid-heavy core UX — the highest-risk surface, tackled once the Phase 2 pattern is
proven. Explicitly includes `TransactionConnectorAdorner` as its own line item: it's an
`Adorner`-based overlay whose visuals are largely hand-coded in `.cs`, not XAML, and is easy to
miss in a file-by-file migration checklist for exactly that reason. **Do not theme it** — see
Phase 4.

### Phase 3 Planning Notes (forward-looking expert panel, 2026-09-20)

**The Phase 0 spike is not sufficient confidence for the rest of the grid — and one edit surface
has already silently migrated, unplanned.** Verified by byte-scanning both assemblies:
`DefaultDatePickerStyle` exists in **both** `Wpf.Ui.dll` 4.3.0 and `ModernWpf.dll`. Since
`Controls/MoneyDatePicker.cs:26` does `SetResourceReference(StyleProperty,
"DefaultDatePickerStyle")`, and Phase 1 merged WPF-UI last, **every date cell in every transaction
grid has been rendering WPF-UI's `DatePicker` template since Phase 1 landed** — never verified,
not tracked as a Phase 1 or Phase 3 task. This is the same silent-shared-key pattern documented in
the Phase 1 Finding above, just discovered in a different control this time.

That silent change has a concrete, plausible failure mode worth spiking **before** any further
Phase 3 markup work: `MoneyDataGrid.GetCellEditor` (`Controls/MoneyDataGrid.cs:821-853`) hit-tests
the active cell editor and tries `FindAncestor<TextBox>` *before* `FindAncestor<DatePicker>`.
WPF-UI's `DatePicker` template contains a `DatePickerTextBox` (a `TextBox` subclass) — so the hit
test can resolve the editor as a plain `TextBox`, silently routing `GetUncommittedColumnText`/
`SetColumnValue` (lines 890-961) down the wrong branch. Compiles fine; wrong at runtime, exactly
the Phase 1 failure pattern. Three concrete spikes recommended before Phase 3 markup starts:

1. **Date-cell editor identity** — assert the concrete runtime type `GetCellEditor` returns for a
   date column, not just "an editor was found."
2. **`TransactionAmountColumn`'s validation-error path** (`Views/TransactionsView.xaml.cs:7238-7291`)
   — its bespoke `TransactionAmountControl` has an inner `TextBox` that does inherit WPF-UI's
   style; spike a bad-value commit (lines 7275-7290) to check the error/focus visuals still work.
3. **Row/cell asymmetry** — `MoneyDataGrid : DataGrid` is a *derived* type, so WPF-UI's implicit
   `DataGrid` style does **not** reach it, but `DataGridRow`/`DataGridCell`/`DataGridColumnHeader`
   are stock types and *do* get WPF-UI's style (confirmed: `Themes/generic.xaml:53` already
   re-bases the header on `DefaultDataGridColumnHeaderStyle`). Headers/rows are already
   WPF-UI-styled; the grid shell and cell templates (`MyDataGridStyle`,
   `Views/TransactionsView.xaml:473,533+`) are not. Set row density explicitly on `MyDataGridStyle`
   — don't assume it's inherited correctly from either library.

**Design-intent stance from the UX panelist, informing the density criterion above**: adopt
Fluent's *materials* here (dark mode, accent-aware selection that preserves the existing category
color swatches, a deliberately *thickened* focus ring beyond Fluent's thin default, since this is
cell-level keyboard editing), but explicitly *reject* Fluent's default *metrics* — WPF-UI's stock
row height/padding would roughly halve visible rows on this reconciliation-focused screen, a
direct regression against this spec's own density criterion. Freeze a density override on
`MoneyDataGrid` **before** touching any cell templates, so later template work isn't authored
against the wrong row heights. Corner radius, Mica/acrylic backgrounds, and reveal-on-hover
animation are called out as actively harmful on a 6000-row dense ledger and should be refused, not
just left as defaults.

### Phase 4 — Duplicate-transaction merge redesign

Replace the connector-overlay interaction entirely with a proper comparison dialog (candidate
transactions shown side-by-side or stacked, per #42's own flagged concern), rather than
reskinning it. This is sequenced after Phase 3's grid work is stable, and deliberately supersedes
any Phase 3 work on `TransactionConnectorAdorner`'s visuals — don't spend effort restyling
something this phase deletes. Also serves as a proof-of-concept for a genuinely Fluent-native
dialog built against real domain data, per the UI/UX review's recommendation.

### Phase 4 Planning Notes (forward-looking expert panel, 2026-09-20)

**Both panelists independently converged on the same fix from different angles, and in the
process found a real, pre-existing bug unrelated to this migration.** `TransactionsView.xaml.cs`'s
`Merge()` is called from two places with different confirmation behavior:
`TransactionConnectorAdorner`'s "Merge" button (line ~3691) passes `promptForConfirmation: false`;
the drag-and-drop merge path (line ~1120) passes `true` for the identical operation. The visually
*lightest* affordance on screen — a small floating button — is the one that deletes a transaction
with **zero confirmation**, silently picks which side survives via an invisible heuristic, and
never shows the user what's discarded. `docs/Basics/Merging.md` describes this feature as safe
("tries to preserve any information"); the code doesn't match that promise. **Recommend filing
this as its own tracked issue, independent of the migration** — it's a real data-safety gap, not
a styling concern, and shouldn't wait on Phase 4's sequencing.

**Design direction (converged)**: keep `TransactionConnectorAdorner` as the lightweight
*discovery* affordance — both panelists agree its cost is low (5 brush keys, already documented,
already working) and its job (surface "these might be duplicates") is correctly lightweight.
Don't replace it with `InfoBar` (a banner control, wrong shape for a between-rows overlay) or
`Flyout` (the same WPF-UI type whose `SplitButton` coupling already caused a real Phase 1 bug —
avoid re-introducing that dependency here). **Replace only the action**: wire the Merge button to
open the comparison dialog this phase already planned, and let *that* dialog do the actual
confirming — two columns, differing fields highlighted, the surviving row's reason stated ("Keeping
this one — it's reconciled"), replacing the generic Yes/No prompt entirely. Keep "not a duplicate"
(the small dismiss) as an equal-weight, clearly-labeled secondary action, not a 12px `(x)` — per
`docs/Basics/Merging.md`, that choice persists and deserves comparable visual weight to accepting.
`CreateConnectorGeometry`/`ArrangeOverride` (the adorner's actual positioning logic) stay
untouched — this phase changes what happens after the click, not how the connector is drawn.

### Deferred beyond this migration

A broader audit of other bespoke interaction patterns elsewhere in the app, and any
shell/navigation-architecture change (see "Scope" above).

## Testing strategy

Matches #42's own originally-stated process: after each phase lands, review and update the
relevant `Source/WPF/UITests/Basics/*` FlaUI tests for whatever UI surface that phase touched,
before moving to the next phase. **Phase 1's actual experience shows this process, as stated, is
not sufficient**: the 15-test Basics FlaUI suite stayed 100% green throughout Phase 1 while its
owner, watching the live app, independently found four real bugs the suite never caught (a
truncated menu label, an inert `SplitButton`, a blank title bar plus a wrong-colored dialog under
Dark theme, and an `Expander` animation racing the app's own layout code) — all four purely
visual/timing failures, invisible to FlaUI's dominant idiom of asserting on the automation tree
(element existence, `Name`, control type, pattern state). A forward-looking QA/test-strategy
panel review (2026-09-20) proposed a concrete fix for Phase 2-4, superseding the screenshot-diffing
idea below:

**Self-referential visual invariants, not golden-image baselines.** Golden-image diffing is a
maintenance sink (frame variance, DPI, theme) and was already scoped as "supplementary" — replace
it with invariants that need no stored baseline at all, added as `VisualGuard.cs`
(`Source/WPF/UITests/Basics/`, same static-helper idiom as the existing `AppCrashGuard.cs`, built
on `FlaUI.Core.Capturing.Capture`):

- `AssertNotClipped(element)` — crops the element's `BoundingRectangle`; fails if non-background
  pixels touch the last 1-2px of any edge. Catches the Phase 1 menu-truncation class of bug, is
  theme-independent, needs no golden image.
- `AssertNotBlank(element)` — fails if a crop has ≤1 distinct color, or if mean luminance doesn't
  match the currently-active theme (dark theme ⇒ luminance below a fixed threshold). Catches the
  Phase 1 blank-title-bar/wrong-dialog-background class of bug directly.
- `AssertSettledWithin(element, budget)` — captures a crop at the moment of interaction and again
  after `budget` (e.g. 400ms); fails if they differ, meaning the UI was still repainting past the
  expected settle time. Catches the Phase 1 `Expander`-animation-race class of bug with no
  baseline at all.

Wire all three into `BasicsTestSetup.CleanUpDatabase` (the universal `[TearDown]`) the same way
`AppCrashGuard` was retrofitted, so every existing and future Basics test gets this checking for
free rather than requiring each test author to remember to call it. Add a second, `[Category("Theme:Dark")]`
pass of the suite (toggle Ctrl+L at session start) so `AssertNotBlank` actually exercises both
themes, not just the default Light one Phase 1's suite ran under exclusively.

**An "adversarial fast pass" for animated controls.** Bug #4 (the `Expander` race) was
specifically *timing-dependent* — invisible at the test suite's own careful, waited interaction
pace, worse under faster, more realistic clicking. Add a reusable `Adversarial.Hammer(element,
clicks, gapMs)` helper (rapid-clicks a control with deliberately-short gaps, using real
`Mouse.Click`/`SendInput` per this repo's `flaui-wpf-testing` skill, so mark these
`[Category("Interactive")]` rather than folding them into the headless-safe pattern-based suite)
followed by `VisualGuard.AssertSettledWithin`. Register one AutomationId per animated WPF-UI
control (`Expander`, `SplitButton`, `ComboBox`, etc.) introduced in Phase 2-4 via
`[TestCaseSource]`, so new controls opt in by registering a string, not writing a new test.

**Coverage that must exist *before* Phase 3 and Phase 4 start, not just after.** Per
`docs/Basics/Merging.md`, the real duplicate-merge feature includes drag-and-drop merge, a
dismiss ("not a duplicate") that persists, and field-preserving merge semantics — none of which
`MergingDuplicateTransactionsFlaUiTests.cs` currently covers (it only asserts the connector
appears and the row count drops to 1 after clicking Merge). Since Phase 4 *deletes* this UI, write
the missing coverage first as the behavior contract the replacement dialog must also satisfy —
merge-semantics belongs in `UnitTests` against `Transaction.Merge` directly (fast, survives the
redesign), the interaction-level behaviors in FlaUI. Similarly, `TransactionsView.xaml` (929
lines) has essentially no grid coverage beyond quick-search filtering today — before Phase 3
starts, record the column header set/order, row height in pixels as a literal number (the density
criterion below is unenforceable without this), the one-line/Show-All-Splits view toggles, inline
edit commit, and sort-by-column, so Phase 3 has something concrete to diff against.

**Process fix**: run the suite — now including the `VisualGuard`/adversarial checks and the
dark-theme pass — after every *task*, not just every phase. A task isn't done until
`dotnet test --filter "Category=Basics|Category=Visual"` passes under both themes. This is a
cadence change as well as a content change; Phase 1's per-phase cadence with functional-only
checks is what let four real bugs accumulate to the end of a phase undetected.

**Static complement, from the WPF-UI developer panel's cross-cutting recommendation**: a
resource-key provenance unit test (`Source/WPF/UnitTests`, STA) that loads `ui:ControlsDictionary`,
`mwpf:XamlControlsResources`, and `generic.xaml` independently, enumerates their keys, and
cross-references every `StaticResource`/`DynamicResource`/`SetResourceReference` literal used
across the app's XAML/C#. Fails if any referenced key is supplied by **neither** library (would
have caught `BaseDialog`'s ModernWpf-only keys — see the Phase 2 Planning Notes above, a class of
bug invisible until the day `ModernWpfUI` is finally removed) and snapshots the keys supplied by
**both** libraries, failing if that snapshot changes unreviewed (the 52-key silent-restyle class
of bug from the Phase 1 Finding, including the `DefaultDatePickerStyle` instance found in the
Phase 3 Planning Notes above). This runs before the app ever launches, catching two of the three
pitfall classes statically rather than live. Pair it with a hard rule for the third class (an API
that compiles but is inert or has unrelated side effects, e.g. `SplitButton.Flyout`,
`ApplicationThemeManager.Apply`): read the actual WPF-UI source for any new `Wpf.Ui.*` API before
its first use in this codebase, not just its public doc comments.

Also worth tracking as its own test dimension once introduced: if `wpfui`'s theme-mode
(Light/Dark/System) switching is adopted, that's a new axis FlaUI coverage should account for on
controls whose automation identity could plausibly differ across themes.

## Design/UX acceptance criteria

**Non-goal, added 2026-09-20 per the forward-looking UX panel review**: "make MyMoney look like a
modern Windows 11 app" is explicitly *not* the destination, and should not be treated as one by
default. Phase 1's own experience is the argument — the library's aesthetic won by default
everywhere it wasn't deliberately overridden (the 52-key silent restyle documented in the Phase 1
Finding above), and that drift is the wrong direction for a dense financial ledger this app's
users work in daily. The actual destination: identical-or-higher information density versus
today, plus real dark mode, checked contrast on the red/green figures, a focus ring at least as
visible as the current classic WPF one, keyboard parity, and one consistent control vocabulary —
Fluent's *quality-of-life* layer, deliberately decoupled from its *visual* defaults.

These are concrete, checkable criteria for each phase's work, not just narrative goals:

- **Density**: the transaction register's row height/padding must stay dense — do not accept
  `wpfui`'s default spacing on the core financial grid. This needs a deliberate density override;
  `MoneyDataGrid` already being a custom control (not stock `DataGrid`) makes this a natural
  customization point rather than something inherited wholesale from the library. **Checkable
  gate**: a Phase 3 change that reduces the number of transaction rows visible at 1080p versus the
  pre-migration baseline is a regression, full stop, regardless of how the change looks.
- **Contrast**: verify actual rendered contrast on any acrylic/Mica surfaces introduced,
  especially near the red/green gain-loss figures already used for financial data (which has its
  own colorblindness consideration independent of this migration).
- **Motion**: any new motion (reveal effects, transitions) must respect the OS "Show animations"
  setting (`SystemParameters.ClientAreaAnimation`) rather than assuming the library handles this
  correctly by default.
- **Focus visibility**: Fluent's lighter/thinner focus-ring styling must not regress
  keyboard-focus visibility versus the current classic WPF focus rectangle — verify explicitly,
  don't assume.
- **Text scaling**: verify `MoneyDataGrid` and other custom controls still degrade gracefully at
  150%/200% Windows text scaling after re-templating.

## Decision to proceed now

The panel's risk officer explicitly argued for deferring this entire effort until the Quicken
feasibility question resolves, on the grounds that none of this work has value independent of
the fork surviving, and that the 14-test FlaUI suite would give false confidence (green tests,
undetected visual regressions) rather than a real safety net. This was weighed and the requester
made an informed decision to proceed now, judging the Quicken replacement likely to succeed with
continued effort. Recorded here for context, not as an unresolved open question.

## Next step

Hand off to the `writing-plans` skill to produce a step-by-step implementation plan for Phases
0-4 above.
