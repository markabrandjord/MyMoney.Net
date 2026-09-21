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

### Phase 1 — Shell

Remove `ModernWpfUI`, add `lepoco/wpfui`, and migrate `MainWindow`'s chrome (menu, toolbar,
toolbox accordion, status bar) as one atomic unit. This chrome is visible on every screen
simultaneously, so it cannot be partially migrated without looking visibly broken — running two
theme systems' implicit (type-targeted) styles in the same visible surface produces inconsistent
corner-radii, accent mechanisms, focus visuals, and padding, because whichever resource
dictionary is merged last/closest wins per-control, not per-window.

### Phase 2 — Simple dialogs first

Convert the lower-risk dialogs first, to establish and document the resource-override
pattern/checklist before tackling the higher-risk cases in Phase 3.

### Phase 3 — Transaction/split grid views

The DataGrid-heavy core UX — the highest-risk surface, tackled once the Phase 2 pattern is
proven. Explicitly includes `TransactionConnectorAdorner` as its own line item: it's an
`Adorner`-based overlay whose visuals are largely hand-coded in `.cs`, not XAML, and is easy to
miss in a file-by-file migration checklist for exactly that reason. **Do not theme it** — see
Phase 4.

### Phase 4 — Duplicate-transaction merge redesign

Replace the connector-overlay interaction entirely with a proper comparison dialog (candidate
transactions shown side-by-side or stacked, per #42's own flagged concern), rather than
reskinning it. This is sequenced after Phase 3's grid work is stable, and deliberately supersedes
any Phase 3 work on `TransactionConnectorAdorner`'s visuals — don't spend effort restyling
something this phase deletes. Also serves as a proof-of-concept for a genuinely Fluent-native
dialog built against real domain data, per the UI/UX review's recommendation.

### Deferred beyond this migration

A broader audit of other bespoke interaction patterns elsewhere in the app, and any
shell/navigation-architecture change (see "Scope" above).

## Testing strategy

Matches #42's own originally-stated process: after each phase lands, review and update the
relevant `Source/WPF/UITests/Basics/*` FlaUI tests for whatever UI surface that phase touched,
before moving to the next phase.

**Addition, scoped narrowly:** lightweight per-control screenshot diffing (via
`FlaUI.Core.Capturing.Capture.Screen()`, already used for debugging in this codebase) for Phase
0's spike and Phase 3's grid work specifically — not a full visual-regression pipeline. This
exists because FlaUI's automation-tree queries (`AutomationId`/`ControlType`/pattern presence)
cannot detect a "renders as a big white box"-class failure: a control can be functionally present
and identity-correct while being visually broken, and that is the specific failure mode this
migration is most at risk of. Scope: cropped per-control snapshots of the specific controls being
themed in that phase, generous tolerance thresholds, run at a fixed resolution/DPI — a
supplementary smoke signal, never a hard gate replacing FlaUI's functional checks.

Also worth tracking as its own test dimension once introduced: if `wpfui`'s theme-mode
(Light/Dark/System) switching is adopted, that's a new axis FlaUI coverage should account for on
controls whose automation identity could plausibly differ across themes.

## Design/UX acceptance criteria

These are concrete, checkable criteria for each phase's work, not just narrative goals:

- **Density**: the transaction register's row height/padding must stay dense — do not accept
  `wpfui`'s default spacing on the core financial grid. This needs a deliberate density override;
  `MoneyDataGrid` already being a custom control (not stock `DataGrid`) makes this a natural
  customization point rather than something inherited wholesale from the library.
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
