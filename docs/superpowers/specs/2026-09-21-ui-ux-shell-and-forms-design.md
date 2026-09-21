# UI/UX Redesign — Shell, Navigation, Views & Dialogs

## Goal

Rebuild the WPF UI on top of the new business/data layer (`rebuild/data-layer-foundation`
— `IMoneyStore`/`IMoneyQuery`/`IMoneyStoreProvisioner`, `MyMoney.Business`, SQLite engine,
`Account` aggregate so far) using WPF-UI as the control library and real MVVM, rather than
continuing to patch the existing code-behind-heavy views (`AccountsControl.xaml.cs`,
`TransactionsView.xaml.cs`, none of them MVVM) onto Fluent controls one at a time. That
patching approach is already in progress (issue #42, PR #51) and has produced real,
documented friction — Accordion sizing regressions, `SplitButton`/`Flyout` API mismatches,
silent DatePicker migration, broken reports discovered only by manual click-through. The
owner's assessment: continuing it means "spend the rest of our lives trying to make that
work, and in the end it will be non-standard." This spec covers the shell, navigation,
status model, theming, and the views/dialogs patterns that surface has to support — not
individual screens, which get their own pass as each business-layer aggregate lands.

## Relationship to prior work — read before anything else in this doc

This spec sits downstream of two efforts that happened independently and are folded
together here for the first time:

1. **`rebuild/data-layer-foundation`** — Plan A, complete. Provides the ports this UI talks
   to. Only `Account` exists end-to-end; no `Transaction`, `Payee`, `Category`, etc. yet.
2. **`docs/scenario-capture`** — the nine-phase end-user scenario audit (811 scenarios) and
   its 53-decision redesign register, reviewed by six 7-role expert panels. This spec is
   built from the register's `navigation-ui` (D-5–D-7), `views-editing` (D-8–D-12) and
   `dialogs-forms` (D-13–D-15) clusters — 11 decisions, all now resolved by the owner (see
   below). The register lives on that branch, not yet merged to `master`.

**Resolved 2026-09-21:** this redesign runs *alongside* the already-in-progress incremental
WPF-UI migration (issue #42, PR #51, Phase 0+1 merged), not in place of it, and is scoped to
Accounts for now — the only aggregate Plan A's business layer implements. #42 keeps
modernizing the rest of the existing app's controls; this spec's screens are new,
MVVM-based, and coexist with both the legacy app and #42's work rather than replacing
either. See "Open items" item 1.

## Validation: a throwaway demo, not shipped code

Before writing this up, a ~30-minute, deliberately disposable WPF-UI prototype
(`UiDesignDemo` — FluentWindow shell, hand-rolled nav pane, Light/Dark theming, an
Accounts list, a Categories list with an on-demand total, an "Add Account" overlay dialog,
a two-channel status bar) was built and driven live via a small FlaUI console driver so the
owner could see the shape of D-5, D-7, D-8, D-11, D-13, D-14 and D-15 before they were
written up as a spec. The owner confirmed the demo looked right. Both live in Claude's
scratchpad directory, not the repository — **this code is not a starting point for the real
implementation**; it skipped MVVM, real data, and half the ports on purpose to stay inside
its time budget. Its only job was answering "does this feel right," and it did. (One
tooling note for whoever writes FlaUI tests against the real app: the driver's
`mainWindow` `AutomationElement` reference went stale after a live theme switch —
re-acquire the main window from `Application.GetMainWindow` after any theme change rather
than trusting a cached reference across one.)

## Ratified decisions (register D-5 through D-15)

Full panel reasoning lives in `docs/superpowers/scenario-capture/panel-review-02-views-dialogs.md`
and `panel-review-03-navigation-crosscutting.md` on `docs/scenario-capture`. Summary:

**Shell (D-5, D-6, D-7) — panel-recommended, owner did not need to intervene:**
- **Status model**: two channels. An ephemeral one-line/Snackbar for foreground results
  (minimum dwell, no wall-clock suppression, background chatter never uses this channel).
  A persistent activity/problems flyout, backed by the existing log, for everything
  background/long-running/failed. `loadTime+3000` and `skipMessagesUntil` are deleted, not
  ported.
- **Density**: no app-wide compact mode. Delete `Themes/Compact.xaml`. Keep per-surface
  density (`OneLineView`/Ctrl+T) where it already earns it. Measure rows-visible-per-screen
  against the current build during migration so Fluent's roomier row metrics don't silently
  shrink the grid — fix at the grid, deliberately, if they do.
- **Theming**: WPF-UI's token set as the base (not bespoke Light/Dark dictionaries). Light /
  Dark / **Follow system**, live, no restart. Fix the two exception-swallowing bugs in the
  current theme-switch path. Keep a code-side color resolver for charts/print. Convention
  (not a mechanism) against literal colors outside the palette.

**Views & editing (D-8–D-12):**
- **Navigation** (D-8): shell owns a real navigation contract. Route ids are stable,
  deliberately-chosen strings — never `typeof(T).FullName`. State is scoped to the open
  money file, not the user (preferences stay per-user). Location/selection restore
  silently; anything that *hides data* (a quick filter, a state filter) restores only with
  a visible, one-click-dismissible indicator — "restore" and "restore filters" are
  different promises.
- **Search** (D-9): one shell-provided filter affordance, surfaces opt in via a predicate,
  header-hosted (not shell chrome), honest "nothing to search here" where unsupported.
  Global find, if ever built, is a visibly different control — not a substitute.
- **"Show me everything like this"** (D-10) — **owner decision, 2026-09-21**: delete the
  dead command now. Defer build/no-build on the real feature (seeded, editable payee-family
  search) until after the Quicken import — decide with the panel's own cheap test: count
  distinct payees against real-world merchants; near 1:1, forget it; near 3:1, build it.
- **Categories tree** (D-11) — **owner decision, 2026-09-21**: no balance in the tree.
  Figures live on the content surface for the selected category (with child subtotals),
  computed on demand, never from the dormant `Category.Balance` column, never without a
  visible timeframe. **Category totals follow standard accounting semantics**: transfers
  are excluded by construction (not assigned a spending/income category at all — tagged by
  linked account instead), splits allocate to their own child categories, refunds net
  against the original category. The owner's underlying need — visibility into what moved
  between accounts via transfer — is real but is a *different*, not-yet-scoped capability
  (account/transfer activity view), not a category-total change.
- **Reconcile vs. duplicate detection** (D-12): keep noticing, suppress *acting* — flag
  passively during reconcile, no inline merge button while reconciling; import-time
  duplicate review remains the higher-value investment.

**Dialogs & forms (D-13–D-15):**
- **Cancel contract** (D-13): one shared editing-scope mechanism (never `MemberwiseClone`),
  defers persistence until commit, no save runs inside an open scope. A short, explicit,
  named allow-list of fields that apply immediately because their records are globally
  shared (today's list: account aliases, market-data/institution settings — derived from
  current code, revisit once the new model exists). Enforced by a generic "mutate
  everything, cancel, assert unchanged" test.
- **Keyboard commit** (D-14): one declared default/cancel mechanism (Fluent `ContentDialog`
  primary/close), enforced automatically — every dialog has exactly one default and one
  cancel. A primary/default button may commit only if scoped to one record, or the
  confirmation states both count and scope. Scoped undo (contingent on D-49, still open) is
  the preferred long-term answer for bulk operations.
- **Dialog base** (D-15) — **owner decision, 2026-09-21**: form-shaped dialogs become
  shell-hosted Fluent content dialogs (owner/centering/theme stop being per-dialog
  concerns); true workspaces (attachment editor, native file/print dialogs, the crash
  reporter) stay real windows. **Sequencing**: build the automated "every `Window`-derived
  type conforms to the base, with a short commented opt-out list" check now, against the
  current 29 dialogs. Do not retrofit those 29 dialogs onto the new base ahead of time —
  each converts when it's rewritten as part of this redesign.

## Proposed technical architecture

- **Control library**: WPF-UI 4.3.0 (`Wpf.Ui.Controls.FluentWindow` root, `ThemesDictionary`
  + `ControlsDictionary` resources, `ApplicationThemeManager`/`SystemThemeWatcher` for D-7).
  Same package/version already adopted by the in-progress migration (issue #42) — not a
  second choice competing with it.
- **MVVM**: `CommunityToolkit.Mvvm` (resolved 2026-09-21 — see "Open items" item 2),
  source-generator based (`[ObservableProperty]`/`[RelayCommand]`), no forced DI container
  or navigation framework.
- **Data access**: view models call `MyMoney.Business` services (e.g. `AddAccountService`
  from Plan A), never `IMoneyStore`/`IMoneyQuery` directly and never the legacy
  `Database/Money.cs` object graph. A screen whose aggregate doesn't exist yet in the
  business layer doesn't get rebuilt yet — see "Non-goals."
- **Shell**: a navigation host implementing D-8's contract (stable route ids, file-scoped
  per-route state), a status host implementing D-5's two channels, header search per D-9,
  theme switching per D-7.
- **Dialogs**: a shared dialog service per D-15 — form-shaped dialogs go through it as
  Fluent content dialogs; the D-15 enforcement check (build now, against the *current* 29
  windows) is independent of this redesign's timing and should be picked up as its own
  small piece of work regardless of sequencing.
- **Testing**: carry forward the existing FlaUI infrastructure (`BasicsTestSetup`,
  `AppCrashGuard`, `docs/dev/flaui-basics-test-notes.md`) and re-point it at the rebuilt
  screens as they land, rather than building a second UI test harness — this was already
  the right call when Plan A's spec flagged it (deferred then because no rebuilt app
  existed yet to point FlaUI at; that condition starts lifting with this spec's first
  screen).

## Open items — need an explicit decision before `writing-plans`

Per this session's own "deviation-check" agreement: nothing below gets silently assumed
into an implementation plan. Each needs a yes/no or a pick from you.

1. ~~Does this redesign replace the in-progress incremental WPF-UI migration (#42, PR #51),
   or run alongside it?~~ **Resolved 2026-09-21 — runs alongside, scoped to Accounts for
   now.** Option (b): this redesign is scoped to business-layer-backed screens only — today
   that means Accounts, the only aggregate Plan A implemented. The rest of the app keeps
   getting the incremental Fluent-control treatment via #42/PR #51 until each area has a
   business-layer slice of its own and is rebuilt in turn. No retirement of the legacy app
   is scheduled by this decision; it narrows this spec's immediate build target to one
   screen.
2. ~~MVVM toolkit: confirm `CommunityToolkit.Mvvm`, or specify something else.~~ **Resolved
   2026-09-21 — `CommunityToolkit.Mvvm`.** Microsoft-maintained, source-generator based
   (`[ObservableProperty]`/`[RelayCommand]`), no forced DI container or navigation
   framework, matches WPF-UI's own sample/gallery patterns. Rejected: hand-rolled (would
   recreate the non-standard-plumbing problem this redesign exists to escape), Prism
   (module/region machinery this single-app project doesn't need, and a licensing-risk
   history worth avoiding for a hobby project), ReactiveUI (steep learning curve and
   reactive-chain risk disproportionate to mostly-simple CRUD forms).
3. **Project structure**: **follows directly from (1).** A new UI project (name TBD — not
   `MyMoney.csproj`, which Plan A's rules leave untouched), hosting only the Accounts screen,
   referencing `MyMoney.Business`. Coexists indefinitely with the legacy app and with #42's
   incremental migration — not a staged replacement of either. Project name still open.
4. **D-2** (`domain-model` cluster, still open in the register): "should domain objects
   carry view state at all?" — flagged by the panel as a direct constraint on D-8
   (navigation state) and D-11 (category totals). Worth resolving before those two get
   implemented, not just designed. Still open.
5. **Accessibility gap**, flagged by the panel itself on D-6, D-7 and D-15: no accessibility
   expertise reviewed the density-control removal, the theming/High-Contrast story, or the
   Window→`ContentDialog` conversion's screen-reader/focus-trapping behavior. Real gap, not
   a formality — worth deciding whether to get real accessibility input before or during
   implementation. Still open.

## Non-goals

- Rebuilding any screen whose aggregate doesn't exist in `MyMoney.Business` yet
  (`Transaction`, `Payee`, `Category`, `Split`, `RentBuilding`, ...). Each screen's rebuild
  is scoped to when its business-layer slice lands, matching Plan A's own sequencing.
- Reports/Charts (register cluster `reports-charts`, D-16–D-24) — separate cluster, mostly
  unresolved, out of scope here.
- Retrofitting the 29 existing dialogs onto the new base ahead of their own rewrite — D-15
  explicitly rejected this.
- The `UiDesignDemo` scratch prototype becoming real code. It was a spike; nothing in it is
  reused as-is.
