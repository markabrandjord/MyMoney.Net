# Redesign Decision Register

Triage of every open question raised by the nine-phase scenario-capture audit
(`end-user-scenarios.md`, same folder) into the three things they can actually be:
a **decision** the redesign has to make, a **bug** that is already understood and
tracked, or **housekeeping** that only affects the catalog's own internal consistency.

This exists so the design panel works from a short, sorted list instead of re-reading
6,950 lines of catalog and re-deciding what is worth arguing about.

## Counts

| | |
|---|---|
| Open questions processed | **193** — 8 (P1), 9 (P2), 15 (P3), 30 (P4), 32 (P5), 24 (P6), 25 (P7), 26 (P8), 24 (P9) |
| **DECISION** | **59 open questions**, consolidated into **53 register entries** (six pairs describe one decision each and were merged — see below) |
| **BUG-ONLY** | **114** — 96 already tracked in issues #52–#58, 18 not yet filed |
| **HOUSEKEEPING** | **20** — catalog-construction and phase-boundary calls |

59 + 114 + 20 = 193, matching the catalog's own closing count.

**Merged decisions** (one decision, two open questions): D-52 (P5 OQ6 + P8 OQ20, printing),
D-50 (P2 OQ5 + P8 OQ16, backup/restore), D-29 (P7 OQ10 + P7 OQ13, tax-data vintage),
D-37 (P8 OQ24 + P9 OQ22, is SQL Server a shipped configuration), D-34 (P9 OQ2 + P9 OQ23,
concurrency scope), D-39 (P9 OQ9 + P9 OQ10, schema upgrade).

## How items were sorted

- **DECISION** — a panel debating alternatives would produce genuinely different
  recommendations, and the answer changes what gets built. Where the underlying code is
  also plainly broken, the defect is cross-referenced to its issue; the *decision* is what
  the fixed code should do, which the issue does not answer.
- **BUG-ONLY** — one correct answer, no alternatives to weigh. Full detail already lives
  in #52–#58; the compact list below exists to prove the triage happened. 18 items are
  real defects that no issue covers yet and are marked `untracked`.
- **HOUSEKEEPING** — "which phase should own describing X", "I classified Y as shell
  rather than view content". Matters to the catalog, not to the redesign.

Borderline calls were resolved toward DECISION, per the brief, with the reason stated in
the entry.

## Cluster tags

`domain-model` (4) · `navigation-ui` (3) · `views-editing` (5) · `dialogs-forms` (3) ·
`reports-charts` (9) · `import-export` (3) · `taxes` (4) · `data-engine` (8) ·
`security` (4) · `attachments-documents` (3) · `rental-property` (2) · `cross-cutting` (5)

---

# The Decision Register

## `domain-model`

### D-1 — Revive category funding, or leave envelope budgeting out of the model?
**From:** Phase 1 OQ1 · **Cluster:** `domain-model`

`AccountType.CategoryFund` (value 9) exists in the enum and is explicitly commented as
unused, retained only so existing databases don't shift their stored values. Nothing in
the product creates or consumes one, so the catalog gives it no scenario. What it points
at, though, is a real capability the product does not have: funding a category from an
account — envelope budgeting. The decision is whether the redesign wants envelope-style
budgeting as a first-class concept (in which case this is a design exercise from scratch,
not a resurrection — there is no behaviour to restore, only an enum value) or whether
budgeting stays purely a per-period comparison of actuals against a budgeted amount. This
is entangled with D-3: if budgeting moves to the transaction level, envelope funding
becomes substantially more plausible.

### D-2 — Should domain objects carry view state at all?
**From:** Phase 1 OQ4 · **Cluster:** `domain-model`

`Transaction` carries `TransactionDropTarget`, `AttachmentDropTarget` and `IsCutting`;
`Security` carries `IsExpanded`; `Category` carries `IsEditing`. These are drag-drop and
inline-editing states of a particular WPF grid, living on the persisted domain types and
travelling with them everywhere the model goes. The catalog deliberately gave them no
domain scenarios and deferred them to Phase 3. The redesign has to decide whether the new
domain model stays "one object graph that both persistence and the UI bind to directly"
— which is what makes these fields exist, and is why the model is so cheap to bind — or
whether view state moves into view models, at the cost of a mapping layer over a ~15k-line
object graph. This is the single most structural choice in this cluster and it constrains
several others (D-8's view-state restoration, D-5's status model).

### D-3 — Is budgeting a transaction-level concept or a split-level one?
**From:** Phase 1 OQ5 · **Cluster:** `domain-model`

`TransactionFlags.Budgeted` exists as a bit on the flags enum and is deliberately cleared
on import, but `Transaction` exposes no property for it — only `Split.IsBudgeted` is
readable and writable, so in working code budget participation is a property of an
itemised line, never of a whole transaction. The flag bit is either a removed capability
or an unfinished one. A user marking "this whole paycheque is budgeted" and a user marking
"the $40 groceries line of this transaction is budgeted" are different products. The
decision has visible downstream reach: Phase 3's advanced search offers a `Field.Budgeted`
condition (B-8) that the model cannot currently answer for a transaction, and the
budget-report command was never wired up at all (D-50's sibling finding).

### D-4 — What does a currency ratio mean, and can a non-USD user be right?
**From:** Phase 8 OQ12 · **Cluster:** `domain-model`

`ExchangeRateService` always requests `fetch-all?from=USD` and stores `Currency.Ratio =
1 / rate`, so every currency in the file is expressed against the dollar, regardless of
what the user picked as their display currency (`DatabaseSettings.DisplayCurrency`, which
also defaults to `"USD"`). The catalog could not establish from the code whether a
non-US user's totals come out right, because nothing at either end states the convention
`Currency.Ratio` is supposed to satisfy. The decision is what the product's currency model
actually is: dollar-anchored with conversion at display time, home-currency-anchored, or
per-transaction rates captured at entry. Related and probably part of the same answer: the
rate service's address is hard-coded and overwritten in its constructor, so unlike the four
market-data providers it cannot be pointed anywhere else — deliberate single-source, or an
oversight to correct.

## `navigation-ui`

### D-5 — What is the product's model for telling the user what it is doing?
**From:** Phase 2 OQ9 · **Cluster:** `navigation-ui`

Status messages are suppressed for the first three seconds after startup and again for
five seconds after a load completes (`loadTime + 3000`, `skipMessagesUntil`). Those guards
exist because the code issues far more `ShowMessage` calls than a user could read, and the
timers are the blunt instrument that keeps the status area usable. A redesign that reworks
the status bar without knowing this will reintroduce the message storm the guards suppress.
The decision is what replaces it: a transient status line with explicit rate limiting, a
notification/activity centre that keeps history, or progress surfaced only where an
operation is long enough to need it. Small, but it has to be answered before the status
surface is redrawn rather than after.

### D-6 — Is there a compact/density mode, and is it a user setting?
**From:** Phase 8 OQ18 · **Cluster:** `navigation-ui`

`Themes/Compact.xaml` is a complete density style sheet, commented as imported from another
project, that nothing merges and no setting names — `MainWindow` hard-codes only the
`Light.xaml` and `Dark.xaml` paths. Separately, the transaction register has its own
row-height setting, so the product already has a partial, local answer to the same question.
The decision is whether a product-wide density control is a capability the redesign offers
(and therefore a dimension every new surface must honour) or whether density stays a
per-surface affordance and `Compact.xaml` is vendored material to delete. Deciding it before
the visual language is set is much cheaper than retrofitting.

### D-7 — How does theming work when a third theme exists?
**From:** Phase 8 OQ19 · **Cluster:** `navigation-ui`

Theming today is complete only for code that opts into it. `AppTheme.GetThemedBrush` exists
precisely because a brush obtained the normal way is frozen and will not follow a theme
change, so anything drawn in code that skips it keeps its original colour until restart —
`MessageBoxEx.SetImageStyle` does exactly that, deliberately. `UpdateDynamicBrushes` throws
outright if a themed resource turns out not to be a solid colour, and only writes a debug
line when a brush named in code is missing from the incoming theme. The catalog's own
observation is that a redesign introducing a third theme would find these one at a time.
The decision is the theming contract: every colour resolved through dynamic resources with
no code-side brushes at all, a validated token set that fails loudly at build time, or the
current best-effort model with restart-on-theme-change made explicit to the user.

## `views-editing`

### D-8 — Is "put me back where I was" a product guarantee or a per-surface nicety?
**From:** Phase 3 OQ2 · **Cluster:** `views-editing`

`IView.SerializeViewState`/`DeserializeViewState` exists on every surface, and exactly two
implement it properly: the transaction register and the holdings list. The loan schedule
builds a state object and then returns a bare `ViewState` on deserialize, so the loan
account is never restored; the rename-rules, rental-summary and rent-input views return
`null` from both. Phase 5 found the same hole from the reports side — the app never reopens
on the report the user was last reading (B-31). So P3-VIEW-2 describes an intent that holds
for two surfaces out of eight-ish. The decision is whether the redesign promises
restore-where-you-were everywhere (which means it belongs in the shell's navigation
contract, not in each surface's own code, and every new surface inherits it) or whether it
is explicitly a register-and-holdings feature. Promising it in the shell is the only way it
stops decaying, but it forces every surface to have serialisable selection identity.

### D-9 — Which surfaces are searchable?
**From:** Phase 3 OQ4 · **Cluster:** `views-editing`

`CurrenciesView` implements a `QuickFilter` property and its row collection implements
matching — but there is no `QuickFilterControl` in its XAML and `FocusQuickFilter()` is
empty, so the plumbing is there and nothing drives it. `LoansView`, `RentSummaryView` and
`RentInputControl` are in the same position. P3-VIEW-7's claim that a surface which can't
be searched does nothing is technically satisfied. The decision is whether search is a
universal shell affordance that every list gets for free (consistent, discoverable, and
occasionally pointless on a six-row list) or a deliberate per-surface choice. Answering
"universal" also answers what Ctrl+F does on a surface with nothing to find.

### D-10 — Ship "show me everything like this", or drop it?
**From:** Phase 3 OQ5 · **Cluster:** `views-editing`

`TransactionsView` has a `CommandViewSimilarTransactions`, a can-execute handler and a
command binding; the execute handler is empty and the menu item is commented out. It is an
empty shell, not a working feature that lost its entry point, so the redesign is deciding
whether to build it rather than whether to restore it. It matters more than it looks: the
product already has the ingredients (the quick-filter expression language, the advanced
query builder, the payee index that `AutoCategorization` walks across accounts) and
"everything like this one" is one of the most common things a user wants from a register.
The alternative is that the existing search surfaces are considered sufficient and the
command is deleted.

### D-11 — Should the categories tree show balances?
**From:** Phase 3 OQ9 · **Cluster:** `views-editing`

`CategoryBalance` carries a `Balance` that `UpdateRoots`/`UpdateBalance` keep continuously
current — and the data template for it renders only the word "Total". Group headings show
no figure either. Meanwhile the accounts panel *does* show group totals, so the asymmetry
is visible to a user looking at the two panels side by side. Either the display was dropped
deliberately (a category total across all time is arguably meaningless without a date
range) or it was never finished. The decision is whether the navigation panels are
*navigation* — names and selection only — or whether they carry figures; and if they carry
figures, over what period, since that question is what makes a category total hard and an
account balance easy.

### D-12 — Should duplicate detection be suppressed while reconciling?
**From:** Phase 3 OQ15 · **Cluster:** `views-editing`

The duplicate-transaction bracket (P3-DUP-1) is offered only when the user is *not*
reconciling (`!this.IsReconciling`). The catalog's read is that this is probably deliberate
— a real statement legitimately contains repeated identical charges, and flagging them
during a balance would be noise. The counter-argument is equally real: reconciling against
a statement is exactly the moment a user is most likely to *find* a genuine duplicate, and
the product goes quiet precisely then. A panel could go either way, and there is a third
option (keep detecting, present it differently during a reconcile). Worth deciding rather
than inheriting, because the current behaviour is invisible — nothing tells the user
detection has been turned off.

## `dialogs-forms`

### D-13 — What does Cancel guarantee?
**From:** Phase 4 OQ2 · **Cluster:** `dialogs-forms` · *Defect instances: #52*

`AccountDialog` edits everything on a working copy and commits on OK (P4-ACCT-3) — except
aliases, where adding calls `money.AccountAliases.AddAlias` immediately and the close box
calls `container.RemoveAlias` immediately, so cancelling the window undoes neither. Two
more windows have the same shape from different causes: `OnlineServiceDialog`'s `Apply()`
is empty with a comment admitting there is no restore-on-cancel (B-14), and `RentalDialog`'s
unit table edits the same `List<RentUnit>` the real property holds because `ShallowCopy` is
a `MemberwiseClone` (B-20). Those three are filed as bugs; what is *not* decided is the
convention they should be fixed toward. Edit-on-copy-commit-on-OK is one answer and is what
most of the product already does, but it does not survive contact with shared global
records like aliases, which other open windows may also be editing. The alternatives are
live-edit-with-undo (which the product has no infrastructure for — see D-49), or live-edit
with the dialog honestly not offering a Cancel button for those fields.

### D-14 — What commits a form from the keyboard, and may it commit something destructive?
**From:** Phase 4 OQ20 · **Cluster:** `dialogs-forms` · *Defect instances: #52*

Enter and Escape are handled four different ways across 29 dialog classes: `IsDefault`/
`IsCancel` in most, hand-rolled key interception in `RecategorizeDialog` and
`AttachmentDialog`, Enter-swallowing in `AccountDialog`'s alias box so the window doesn't
close, and several windows with competing `IsDefault` buttons (B-17). The sharpest case is
not the inconsistency but `RecategorizeDialog`: its interception is window-wide, so pressing
Enter while still typing in the category box commits the entire recategorize-everything
operation. Picking one mechanism is the easy half. The real decision is whether Enter is
allowed to commit an irreversible bulk operation at all, or whether destructive
confirmations must require an explicit, distinct gesture — which also sets the rule for
every new dialog the redesign adds.

### D-15 — One real shared dialog base, and what it must carry
**From:** Phase 4 OQ30 · **Cluster:** `dialogs-forms` · *Defect instances: #52*

Counted across the 29 dialog classes: the themed base class is used by 24 of the 28 that
aren't `BaseDialog` itself, a help topic by 4, `CenterOwner` by 14 of 24 with XAML,
`ShowInTaskbar="False"` by 13, and Enter/Escape four ways. Each of P4-DLG-1…P4-DLG-5
describes the *majority* convention, not an invariant — a dialog picked at random has a
fair chance of missing two or three. The catalog calls one real shared base carrying all
five the cheapest single improvement available in that folder, and the recently landed
"two dialogs opening disconnected from the app window" fix on this branch is the same class
of gap being found one at a time. The decision is not *whether* but *what it enforces and
how hard*: owner/centring/taskbar/theme/help/keyboard as a base class an author can still
bypass, or as an analyser-enforced contract; and whether "has a help topic" is on that list
at all, which is D-51.

## `reports-charts`

### D-16 — How is a report or chart scoped, and is there still a shared range dialog?
**From:** Phase 4 OQ16 · **Cluster:** `reports-charts`

`ReportRangeDialog` is named for reports and used by none. Its single caller in the whole
product is `TrendGraph.OnSetRange` — the range command on the history chart strip — which
retitles it "Graph Range" and sets `ShowInterval = false`. Reports configure themselves in
the side panel instead (P3-PANEL-1). Half the window is dead in both directions: its
category tick-list is collapsed in XAML, `EnableCategoriesSelection` is never set by
anything, and the `Categories` setter that would populate it is never called — so the list
is simultaneously hidden and empty. Two things need deciding. First, whether report and
chart scoping share one surface at all or stay separate (the window's name, title and dead
half are the residue of a repurposing nobody tidied). Second, whether *category filtering*
of a report is a capability the redesign wants — the intent is written down here and
implemented nowhere, and it is the kind of thing users ask for constantly.

### D-17 — What is a shareable report, and is HTML it?
**From:** Phase 5 OQ3 · **Cluster:** `reports-charts`

An exported report is not a self-contained document. `HtmlDocumentReportWriter`'s `<head>`
links Bootstrap from a CDN, so the file is unstyled offline. `WriteElement` is a no-op, so
every chart and every net-worth colour swatch is simply absent. `WriteHyperlink` emits a
plain `span` with a comment saying there is nowhere to link to. The expandable groups are
no-ops, so the detail rows of the cash-flow, W-2 and portfolio reports come out shifted one
column left. The catalog's judgement is that a redesign wanting a shareable report should
treat this as a rewrite rather than a tweak — which makes the format a real choice: a
self-contained HTML file with inlined styles and rendered charts, PDF, or leaning on
printing (D-52) and dropping HTML export entirely. This is the report a user hands to an
accountant, so "what lands on disk" is the requirement, not the mechanism.

### D-18 — Does every report export, and to what?
**From:** Phase 5 OQ4 · **Cluster:** `reports-charts`

`ShowExportButton` is called by exactly two of the nine reports — cash flow and portfolio.
Every other report either inherits `Report.Export`, which throws `NotImplementedException`,
or overrides it to throw explicitly (`FutureBillsReport`, `UnacceptedReport`, `W2Report`).
The exception is unreachable today only because the button is never shown, and Phase 5's
OQ21 found the net-worth report one character away from making it reachable (B-44). So the
product's position is "two reports export, silently". The decision is whether export is a
guarantee of the report framework (every report can produce a table, so every report can
produce a CSV — which then makes "what a report *is*" a stricter contract) or an opt-in
per report, in which case the reports that don't export should say so rather than simply
lack a button.

### D-19 — What should a mixed-currency total say?
**From:** Phase 5 OQ8 · **Cluster:** `reports-charts`

In `AccountSummaryReport.WriteAccountSummary`, if two accounts of the same kind report
different currency symbols, a local `various` flag is set and the method returns `0`
instead of its real subtotal — so that entire account type silently drops out of the grand
total. Because `commonSymbol` is a field shared across types, one odd account anywhere can
suppress the "All Accounts" row for everything. The catalog's summary is that the report
currently says less than it knows. The redesign has to pick a policy and apply it
everywhere totals appear: convert to the display currency at today's rate and say so, show
per-currency subtotals side by side and refuse to add them, or show the total with an
explicit "excludes N accounts in other currencies" note. This is the same underlying
question as D-4 and should probably be answered with it.

### D-20 — Should generated category colours be persisted?
**From:** Phase 5 OQ20 · **Cluster:** `reports-charts`

`CategoryChart.Tally` assigns `c.Root.Color = cd.Color.ToString()` for any category that
has no colour of its own — a write to the persisted model performed as a side effect of
rendering a chart. The user can therefore be told they have unsaved changes purely because
they looked at a pie chart. The catalog is careful to separate the two halves: doing it
from a draw path is plainly wrong (tracked in #52), but *whether generated colours should
stick* is a genuine design question — persisting them is exactly what makes P5-CHART-4's
"same category, same colour every time" hold across sessions. The alternatives are a
deterministic colour function derived from the category (stable without persisting
anything), or assigning a colour at category-creation time as an explicit, visible property.

### D-21 — Does the net-worth picture include what the user owes?
**From:** Phase 5 OQ22 · **Cluster:** `reports-charts`

The net-worth report's pie chart carries the comment *"liabilities are not included in the
pie chart because that would be confusing"*, and indeed omits credit-card balances — and
then the very next call, `WriteLoanAccountRows(writer, data, color, true)`, adds every
liability loan to the same pie with `Math.Abs(balance)`. A mortgage therefore appears as a
positive slice of what the chart presents as net worth. The code contradicts itself, so
there is no "current intent" to preserve; the panel has to pick. Assets-only with
liabilities shown as a separate figure, a two-sided asset/liability comparison, or a
waterfall from gross assets to net are all defensible and look completely different. Also
in scope for the same answer: asset and loan rows are added with no drill-down attached, so
those slices are the only ones that do nothing when clicked, with no indication of the
difference.

### D-22 — As at the start or the end of the chosen day?
**From:** Phase 5 OQ23 · **Cluster:** `reports-charts`

`PortfolioReport.InternalGenerate` writes its heading with `ReportDate` and then, whenever
that isn't today, a sub-heading reading *"As of "* plus `ReportDate.AddDays(-1)`. The two
lines of the same report disagree by a day. That is a bug in the sense that they cannot
both be right, but the question underneath is one a user genuinely asks and the product has
never answered: does "portfolio as of 31 December" mean holdings and prices at the close of
the 31st, or the state of the book at the start of that day? The answer determines whether
a sale made on the date appears, which is a real difference at a tax-year boundary, and it
needs to be the same answer across the portfolio report, the history chart and the tax
report's fiscal-year range.

### D-23 — How are report options hosted, and should a report remember them?
**From:** Phase 5 OQ29 · **Cluster:** `reports-charts`

`MainWindow.GetService(typeof(ReportsControl))` calls `ShowReportsPanel()`, which disposes
the old panel and constructs a new one — so *asking for the service* is what makes the
panel appear, and every report gets a fresh one. That is why the panel has `Hide…` methods
with no matching `Show…` for the date and currency rows: each report hides what doesn't
apply and relies on the next report getting a clean panel. It works, and it is also the
reason every report must re-register its event handlers on every generation (all of them do
it by unsubscribing twice first — `Unregister(); … Unregister(); Register();` appears
verbatim in four reports). "Getting a service has a visible side effect" is a pattern the
redesign should not inherit, but the replacement is a real choice: one persistent options
surface that reports declaratively describe their parameters to, or per-report option
panels owned by the report and restored with it (which folds into D-8 and B-31).

### D-24 — One vocabulary for "a period"
**From:** Phase 5 OQ32 · **Cluster:** `reports-charts`

There are four overlapping notions of a time period in this one feature area, and the one
that looks like the intended shared vocabulary is the least used. `IReport.cs` defines
`enum ReportInterval { Days, Months, Years }`; no report uses it, and its only consumer is
`ReportRangeDialog`'s interval combo, which that dialog's sole caller collapses (D-16) — so
it is reachable in name only. Meanwhile the cash-flow report buckets by two hard-coded
strings, the history chart has `HistoryRange` (the same three members with different
semantics), and the trend graph has `CalendarRange` (ten members). A redesign that wants
"choose a period" to mean one thing has four definitions to reconcile, and the reconciliation
is not mechanical: the ten-member `CalendarRange` and the three-member `ReportInterval`
encode different user intents (recurrence vs bucketing), and collapsing them loses
something.

## `import-export`

### D-25 — What should "Money File Import" accept?
**From:** Phase 4 OQ25 · **Cluster:** `import-export`

Despite the name and the multi-file selection, `MoneyFileImportDialog.ProcessFile` accepts
only `.db`/`.mmdb` and answers anything else with *"Import only supports sqllite money
files"* (sic) before stopping the whole batch. The product can, elsewhere, read `.xml`,
`.bxml`, `.sdf` and CSV — all of which are "a money file" by any ordinary reading, and at
least two of which are whole-file formats this same dialog's merge semantics would apply to
cleanly. The decision is the feature's scope: one merge entry point that accepts every
whole-file format the storage layer can open (which interacts with D-35's engine
portfolio), or a deliberately narrow SQLite-to-SQLite merge that is named and worded
honestly. The current state is the worst of both — a broad name, a narrow implementation
and a misspelled error.

### D-26 — Is QIF import worth keeping?
**From:** Phase 6 OQ20 · **Cluster:** `import-export`

`QifImporter.ImportQif` is the only route in with no `BeginUpdate`/`EndUpdate` scope and no
rollback, and it `throw`s on the first field code it doesn't recognise, on a malformed
status line, on a split with no amount, and on an illegal self-transfer — so a file that is
90% valid leaves 90% of its transactions committed, no way to tell which, and a message box
naming a line number. Every list churns a row at a time while it runs. It also has no
notion of the `!Account` / `!Type:Cat` / `!Type:Class` sections real exports contain, so a
whole-file Microsoft Money export fails on its first line. The catalog puts the question
plainly: whether QIF import is worth keeping at all is a redesign question. It is directly
load-bearing for *this fork's* reason to exist — importing Quicken data — so the panel
should weigh "rewrite QIF properly, transactionally, with an error report" against "QFX/OFX
and CSV only, and tell Quicken users to export those instead".

### D-27 — What happens when an imported transaction isn't in the account's currency?
**From:** Phase 6 OQ23 · **Cluster:** `import-export` · *Defect instances: #56*

There are two guards for this and neither runs. `OfxRequest.CheckUSD` — called before every
bank, card and investment statement is applied — has its entire body commented out with
`return true;` left behind, so the statement's declared currency is read and discarded.
`ProcessCurrency` has an inverted guard (it only records an error when the set already
contains the symbol, and only adds to the set inside that branch), so the set starts empty
and stays empty forever; it is also wired only into the investment path, never bank or
card. Fixing the guards is a bug (#56). What the fixed guards should *do* is not decided:
refuse the import, book at the account's currency with an error row, convert using the
stored `Currency.Ratio`, or store the transaction in its own currency (which the domain
model's P1-CUR-* scenarios suggest it can represent). For a product that models currencies
as thoroughly as Phase 1 describes, the import side currently knows nothing about them.

## `taxes`

### D-28 — What happens to a sale the product can't find a purchase for?
**From:** Phase 7 OQ3 · **Cluster:** `taxes` · *Defect instances: #57*

`CapitalGainsTaxCalculator` sorts a sale into its `Unknown` list when `sale.Error != null`,
and `SecuritySale.Error` is never assigned anywhere in the codebase. Three consequences
follow: the tax report's "Capital Gains with Unknown Cost Basis" section can never render,
its "Errors Found" section can never render, and `TxfExporter`'s entire refnum-673
unknown-basis branch is dead code. What actually happens is that `SecurityFifoQueue.Sell`
parks the sale in a private `pending` list, recovered only if a later purchase turns up — so
a user who transferred a holding in from an untracked broker, or inherited shares, sells
them and gets a tax report with **no row at all**. Filed as a defect in #57, but the correct
behaviour is a genuine choice with a real user experience attached: flag the row with a
zero basis, prompt the user to supply a basis and remember it, or exclude with a visible
count of what was excluded. This matters disproportionately for this fork, whose whole
premise is importing a history that begins mid-life.

### D-29 — Which tax year is this product for, and how does the user know?
**From:** Phase 7 OQ10 + Phase 7 OQ13 · **Cluster:** `taxes` · *Defect instances: #57*

`TxfSpec.txt` — the catalogue behind the whole of §7.1, every selectable tax line — is
version 041, dated 6/16/06, and every line carries an IRS reference of the form
`2004:1040:21`. Form 1040 was restructured in 2018 into a short form plus Schedules 1–3, so
many line numbers the user picks from no longer exist, some refnums have been retired, and
HSA lines, qualified business income and the 1099-NEC are absent entirely. Separately,
`FederalTaxes.json` is a bare object with no year, no source and no published date (its
figures look 2026-shaped); `StateTaxes.json` at least self-describes. There is no staleness
check, nothing on screen saying which year the rules are from, and no update mechanism short
of shipping a new build. The decision is what the product claims: a tax-year-versioned
data set with a visible vintage and an update path, a deliberately vaguer "estimate only,
not tax advice" framing with the year-specific export dropped, or keeping the current
behaviour and accepting that it exports refnums current software may reject.

### D-30 — Model every filing status properly, or disclose the approximations?
**From:** Phase 7 OQ14 · **Cluster:** `taxes`

`FederalTaxes.GetIncomeTax` maps married-filing-separately to the **Single** brackets and
the **Single** standard deduction, with the code's own comments reading *"are there income
brackets for this?"* and *"todo"* — close at the lower bands and wrong at the top, where the
separate threshold is half the joint one, not the single one. The state data has no
head-of-household or separate tables at all, so `StateData.GetIncomeTax` maps
head-of-household to Single too, noted as *"state data doesn't have this category"*. A
head-of-household user therefore gets a correct federal estimate and a state estimate
computed on the wrong table, and nothing in the UI says so. Two honest answers exist —
complete the tables (51 hand-maintained state files, a real maintenance commitment) or
state the limitation where the figure appears — and they lead to very different products.
Silently approximating is the one option that should be off the table.

### D-31 — Is a characterization test allowed to ratify a wrong answer?
**From:** Phase 7 OQ15 · **Cluster:** `taxes`

`StateData.GetCapitalGainsTax` has no `if (gains < 0) return 0;` guard, unlike
`FederalTaxes.GetCapitalGainsTax` and both `GetIncomeTax` methods, which all have one — so
once `baseGains` exceeds the state exemption, a realised loss produces a *negative* tax, a
refund no state offers. This one is already documented rather than fixed:
`UnitTests/TaxTests.cs` asserts `Is.EqualTo(-3500.0M)` for Washington with a $50,000 loss,
with a comment explaining that the clamp is missing. That makes it a policy question, not
just a fix: this migration's own rule is characterization-testing — capture what the code
does, don't silently "fix" it to match a guess — and here that rule has pinned a defect. The
panel should decide when a characterization test is evidence to preserve and when it is a
bug with a test holding the door open, and what the convention is for marking the
difference (an explicitly named `Ratifies_Known_Defect` test, a linked issue, an
`[Ignore]`). It generalises well beyond tax.

## `data-engine`

### D-32 — Should the user ever choose a storage engine?
**From:** Phase 2 OQ3 · **Cluster:** `data-engine`

"File ▸ New" behaves differently in a debug build: in release it goes straight to creating
the product's default storage, and in debug it grows a submenu offering a choice of engine.
The catalog described the release behaviour on the assumption that a storage engine is not
an end-user concept — which is a defensible product position and also the thing the panel
has to ratify or overturn. If the redesign intends to expose a choice (SQLite file vs
shared SQL Server being the meaningful one for a household), it needs designing as a real
user-facing concept with consequences explained, not inherited from a debug-only menu.
Directly coupled to D-35 (which engines survive) and D-37 (whether SQL Server is a shipped
configuration at all).

### D-33 — Is there a power-user data surface, and what is it?
**From:** Phase 2 OQ7 · **Cluster:** `data-engine`

`Query ▸ Adhoc SQL Query` and `Query ▸ Show Last Update` both open a free-form SQL window
over the user's own financial data. The catalog gave them no scenario on the judgement that
a SQL console is a developer tool rather than an end-user capability — but it is also the
only place the product currently offers anything like "ask an arbitrary question of my
data", and it is reachable from the normal menu in a release build. The adhoc one silently
does nothing unless the books happen to be on a server (B-23), and on the XML and CSV
engines the query text is ignored entirely (B-105). So the decision has two halves: does the
redesign offer a power-user query capability at all, and if so is it SQL over the live
store (with the obvious hazards) or a safe structured query surface built on the existing
`Query.cs` vocabulary that the advanced search already uses.

### D-34 — What does the concurrency guarantee actually cover?
**From:** Phase 9 OQ2 + Phase 9 OQ23 · **Cluster:** `data-engine` · *Defect instances: #58*

The per-record path (`*_SaveBatch`) checks `ExpectedVersion` and writes `Version =
Version + 1`. The per-row procedures the whole-file save uses — `Transactions_Update`,
`Categories_Update`, `Payees_Update` and thirteen others — do neither, so a full save
neither checks nor increments, and P1-WHOLE-4's "warn me if someone else changed this"
simply does not fire for the operation most likely to need it. The SQLite whole-file path
has the same gap for the same reason. Separately, the version column was deliberately added
to eleven aggregate-root tables and deliberately not to `Splits`, `Investments`,
`RentUnits`, `AccountAliases` or `TransactionExtras`, which are saved as part of their
parent — and `RentUnits` in particular is written by `SaveRentUnitsForBuilding` with no
version check of any kind, including after the parent building's `DELETE` has already run.
The defects are filed in #58; the decision is the guarantee's scope and shape — is
optimistic concurrency a property of every write path (so whole-file save must route through
`SaveBatch` or be removed), and is the aggregate-root boundary the right granularity given
`RentUnits` sits outside it. The existing design doc treats the version column as *the*
guarantee, so this is ratifying or amending that document.

### D-35 — Which storage engines does the redesign keep, and what must each do?
**From:** Phase 9 OQ12 · **Cluster:** `data-engine`

`SaveOne`, `SaveTransfer` and `SaveBatch` throw `NotImplementedException` on
`SqlServerDatabase` (and therefore `SqlCeDatabase`), on `XmlStore`/`BinaryXmlStore` (with an
explicit "see the design spec's Non-goals") and on `CsvStore`. Only `SqliteDatabase` and
`SqlServerStoredProcDatabase` implement them. So P9-SAVE-2 describes a real capability that
is a property of the storage choice, and nothing tells the user that picking `.xml` means
every edit rewrites the whole file — with the concurrency consequences of D-34 attached.
Related engine-capability asymmetries are filed as bugs (B-105, B-106: query and log return
nothing on XML/CSV). The decision is the portfolio: which of the five engines survive, and
what the capability floor is for one that does — per-record save, optimistic concurrency,
query, log, backup. An engine that cannot meet the floor is either a *format* (import/export
target) rather than a store, or it goes.

### D-36 — Auto-heal integrity errors, surface them, or keep ignoring them?
**From:** Phase 9 OQ3 · **Cluster:** `data-engine` · *Defect instances: #58*

All three engines' `ReadTransactions` build an `ArrayList` of `DataError` records on every
load — "other side of split transfer not found", "transaction is marked as a transfer, but
other side of transfer was not found", "already have a transfer for this transaction, so
transfer N is a duplicate of transfer M" — and return it. Every production caller discards
the return value; the only code that reads the list is the unit tests. `DataError.Heal
(MyMoney)`, the method whose name says it was meant to repair them, has an empty body and a
`// heal thyself!` comment. So Phase 1's P1-XFER-8 ("have broken transfers found and
reported") is half true: they are found on every load and reported to nobody. The three
answers are genuinely different products — repair silently on load (fast, and hides the fact
that data was wrong), surface a review surface the user acts on (honest, and needs UI that
doesn't exist), or keep collecting and only report on demand. Cheapest of the Phase 9
findings to act on, and it turns a class of silent corruption into something a user can see.

### D-37 — Is SQL Server a shipped configuration or a developer one?
**From:** Phase 8 OQ24 + Phase 9 OQ22 · **Cluster:** `data-engine`

Two independent findings say the same thing. The whole of `DataEngineStartup` — reconnecting
to a server database on startup — is inside `#if DEBUG`, so a release build never
auto-reopens a SQL Server set of books and the user must go through the File menu every
time (P8-STORE-6 describes behaviour most users will never see). And `MyMoney.Data.csproj`
has no content item copying `SqlScripts/` to the output, while `SqlServerBootstrapper` reads
`Bootstrap/`, `Migrations/`, `Access/` and `Test/` off disk at run time — the plan documents
say that copy lives in `MyMoney.csproj` and is DEBUG-only, which if still true means a
release build cannot bootstrap a server or create a catalog at all. Either SQL Server is a
supported configuration, in which case both of these are release blockers and the whole
bootstrap path needs a release test, or it is a development/testing engine and should be
labelled as one — which would substantially simplify D-32 and D-35 and would retire the
server-credential exposures B-100 and B-102 outright.

### D-38 — Which SQL Server engine is the real one?
**From:** Phase 9 OQ4 · **Cluster:** `data-engine`

`DatabaseFactory.CreateDatabase`'s `DbFlavor.SqlServer` case constructs `SqlServerDatabase`
— the raw-SQL engine, which needs direct table rights the `MyMoneyUser` login is
deliberately not granted. The 3,075-line `SqlServerStoredProcDatabase` is constructed in
exactly one production place, `SqlServerConnectionFactory.Connect`, reached only through the
registry-driven path. So the engine that the entire `SqlScripts/Access/` surface, the
bootstrapper and the persistence-concurrency design exist to serve is *not* what you get by
opening a SQL Server database through `DatabaseLifecycle`. Either the factory case is wrong
or `DbFlavor` needs to distinguish the two, and someone has to say which — the answer
determines whether D-34 and the `DBString` escaping defects (B-109, B-110) are live in
production at all, and whether 3,000 lines of carefully written engine are reachable.

### D-39 — How does a schema upgrade work, and what is the user told?
**From:** Phase 9 OQ9 + Phase 9 OQ10 · **Cluster:** `data-engine`

`SqliteDatabase.CreateOrUpdateTable`'s rebuild path creates `NEW_<Table>`, copies rows,
`DROP TABLE`s the original and renames — correctly, inside one transaction. The weak link is
that the column list is derived by `ParseColumnSql`, a hand-written scanner over SQLite's
stored `CREATE TABLE` text, and the only thing standing between a mis-parse and a
`DROP TABLE` of real data is `if (first) throw new Exception("Invalid table definition…")`.
No test coverage is named for it anywhere in that folder. Alongside it,
`IDatabase.UpgradeRequired` is `true` only for `SqlCeDatabase` —
`SqlServerDatabase.UpgradeRequired` returns `false` from both branches of a decorative
try/catch, SQLite and XML return `false` outright, and `SqliteDatabase.Upgrade()` is empty
and marked `// TBD` — so P9-UPGRADE-3's confirmation prompt is dead for four engines out of
five and a reader of `IDatabase` would never guess it. The decision covers both: a real
migration mechanism (versioned scripts with tests, as the SQL Server side already has)
versus the current reflective DDL rebuild, and whether an in-place upgrade is something the
user is told about, asked about, or never sees.

## `security`

### D-40 — One credential window for six jobs, or six windows?
**From:** Phase 4 OQ1 · **Cluster:** `security`

`PasswordWindow` is simultaneously the "set a password on my data" window, the "prove you
know it" window, the online-banking sign-in, the multi-factor questionnaire, the
authentication-token prompt and the change-password flow — each subclass bending it by
adding fields and rewriting its prose at runtime. The catalog captured them as several
scenarios because they are genuinely different user goals with different stakes: one
protects a local file, another hands credentials to a third party over the network. Keeping
them unified is a real position (one audited, careful credential surface, one place to get
masking and clipboard behaviour right) and so is splitting them (a bank sign-in should look
nothing like a local file password, and conflating them trains users to type banking
credentials into whatever window looks familiar). Worth deciding deliberately, because this
is the riskiest window in the product and everything else in this cluster passes through it.

### D-41 — What may a shareable diagnostic log contain?
**From:** Phase 6 OQ13 · **Cluster:** `security` · *Defect instances: #56*

`OfxRequest.SaveLog` blanks nine credential-bearing elements — the hard part done — but
`BANKID`, `ACCTID` and `BROKERID`, and every transaction in the response, are written to
`OfxLogs\` in the clear. P6-ENTRY-5's entire purpose is to get the user to hand that file to
someone else for troubleshooting. The masking gap is in #56; the decision is what the
"Details…" affordance should actually offer. A redacted-by-default log with an explicit
"include account identifiers" opt-in, a structured error report that carries no statement
content at all, and today's full-fidelity log with a clear warning are all workable and
lead to different diagnostics quality. Whatever is chosen, the product should be able to
state what leaves the machine.

### D-42 — Should the product trust a third-party institution directory?
**From:** Phase 6 OQ18 · **Cluster:** `security` · *Defect instances: #56*

Both `OfxHomeProviderList` (`http://www.ofxhome.com/api.php?all=yes`) and
`OfxHomeProviderInfo` are plain HTTP. What comes back includes **the URL the product will
subsequently post the user's banking credentials to**, and it is merged into the user's
local directory and saved. `OfxRequest.SendOfxRequest` does upgrade a bare institution
address to `https://` before posting, which limits the damage, but an entry whose stored URL
already begins `http://` is left alone. Switching to HTTPS is the obvious fix and is in
#56. The decision is bigger: whether a remote, community-maintained directory is allowed to
supply the endpoint that receives credentials at all, or whether the product should pin a
vetted list, require explicit user confirmation before a changed endpoint is used, or drop
directory sync and let users enter their institution's details themselves. The catalog calls
it the most security-relevant thing in the phase and says it deserves a deliberate decision
rather than inheritance.

### D-43 — What does "password-protect my file" promise?
**From:** Phase 8 OQ9 · **Cluster:** `security` · *Defect instances: #52/#56 family*

`Encryption` hard-codes `saltValue = "Money Rocks"` and `initVector = "*B5good!+027XYZ."`,
derives the key with `PasswordDeriveBytes` (PBKDF1) over SHA-1 with `passwordIterations = 2`,
and comments that these "cannot change". Every encrypted file the product has ever written
shares one salt and one IV, and the derivation offers essentially no resistance to
brute-force. A user who password-protects an exported financial file is getting far less
protection than the feature implies. This is not a drop-in fix — changing any of it breaks
every existing file, which is why the comment is there — so the decision is the migration
strategy: a versioned file format that reads old files and writes new ones, a one-time
convert-on-open with a warning, or withdrawing file encryption and pointing at OS-level
options (BitLocker, EFS) instead. Related and cheaper: while a `.bxml` file is being read or
written, the fully decrypted contents of the user's entire financial history exist as a
plain file in `%TEMP%` (B-103).

## `attachments-documents`

### D-44 — Bring scanning back, or delete it?
**From:** Phase 4 OQ22 · **Cluster:** `attachments-documents`

The scan toolbar button, the scanner-selection code and the acquire-image flow are all
commented out in `AttachmentDialog`, while the WIA error-code enumeration and the
error-message translator survive as live, uncalled code — and the dialog's own class comment
still reads "Interaction logic for ScanDialog.xaml". Scanning receipts was clearly the
original point of this window. The intent is written down and mostly implemented, so this is
a cheaper revival than most items in this register, but it is still a scope decision with a
real cost (WIA is a fussy dependency, and phone-camera-to-cloud has largely replaced desktop
scanning for receipts). The alternatives are revive it, replace it with an import-from-folder
/ watch-folder flow, or delete the dead block and the orphaned WIA code with it.

### D-45 — What happens to paperwork when its transaction is deleted?
**From:** Phase 8 OQ13 · **Cluster:** `attachments-documents`

`AttachmentManager.OnMoneyChanged`'s delete branch calls `DeleteAttachments(t)` and carries
the comment *"todo: would be nice to warn the user they are losing them…"* — twice, in two
different handlers. Deleting a transaction that has a receipt filed against it destroys the
receipt immediately and silently. The product otherwise treats deleting a transaction as a
low-stakes, easily made action, so this is a real asymmetry: the cheap action silently
destroys the irreplaceable artefact. The options are warn-and-confirm (which makes a common
action heavier), orphan the file into a holding area with a recoverable window, or keep
attachments until an explicit cleanup — and the answer interacts with D-46, because
attachments live *beside* the data file rather than inside it, so there is currently nothing
to restore them from.

### D-46 — File documents by account name or by account id?
**From:** Phase 8 OQ14 · **Cluster:** `attachments-documents`

Both `AttachmentManager` and `StatementManager` build a folder path from
`NativeMethods.GetValidFileName(account.Name)`. Renames are handled (P8-ATT-6, P8-STMT-5),
but two accounts whose names differ only in characters that get stripped — `"Visa: Joint"`
and `"Visa / Joint"` — resolve to the same folder, and every document in it is then matched
to transactions by id across both accounts. `StatementManager` additionally keys its whole
in-memory index by the raw account name, and the model permits two accounts with the same
name outright. Using the account's identifier would remove the entire class of problem, at
the cost of folders a human can no longer read — which is exactly the trade-off P8-ATT-3
exists to describe, since "your documents are ordinary files you can find in Explorer" is a
deliberate, valuable property of the current design. The third option is readable names plus
a manifest that maps them.

## `rental-property`

### D-47 — Finish per-unit rent entry, or delete it?
**From:** Phase 3 OQ1 · **Cluster:** `rental-property` · *Tracked as #53*

`Views/RentPayementsView.xaml` (class `RentInputControl`) is a complete, working surface for
entering rent received per unit per month, grouped by month with per-month totals — and it
is never constructed anywhere in the product. Rental tracking otherwise has **no way at all**
to record what each tenant actually paid, so this is not a missing convenience, it is the
missing half of the feature. #53 exists to track it and asks this exact question rather than
answering it, which is why it is here: the panel has to decide whether rental property is a
capability the redesign carries forward (in which case this view is a head start and D-48 is
its companion) or a niche feature to retire, taking `RentBuildings`, `RentUnits`, the rental
dialog and the rent-summary view with it.

### D-48 — What is a rental property in the model, and how much of it is editable?
**From:** Phase 4 OQ23 · **Cluster:** `rental-property`

Phase 1's P1-RENT-1 records purchase date, purchase price, land value and current estimated
value on a property. `RentalDialog` offers none of them — so either they are maintained
somewhere else, or they can only arrive by import, or they are vestigial. Before a redesign
assumes the existing window is the complete property editor, someone has to say which of
those fields the product actually intends to carry and what they are *for*: purchase price
and land value are depreciation inputs, which implies a tax capability the product does not
otherwise have, and current estimated value is a net-worth input that nothing currently
updates. Pairs with D-47 — the two together are "is rental property a real feature here".

## `cross-cutting`

### D-49 — Build undo, or remove the menu items?
**From:** Phase 2 OQ1 · **Cluster:** `cross-cutting` · *Defect instances: #52*

Undo and Redo sit in the Edit menu, permanently disabled: `OnCommandCanUndo`/
`OnCommandCanRedo` unconditionally set `CanExecute = false`, the execute handlers are empty,
and the `UndoManager manager` field created for them in the constructor is never used (the
separate `navigator` instance that drives back/forward navigation works fine and is
unrelated). Critically, the *domain* model has no undo support either — nothing in Phase 1
corresponds to it — so this is new work, not resurfacing something that used to work.
Building it means a change-tracking or command-journal design across a ~15k-line object
graph with a persistence layer that already has its own change-tracking notion
(`ChangeType`), which is a significant architectural commitment; removing the menu items is
free and honest. It also sets the answer for D-13: live-edit-with-undo is only an option for
dialogs if undo exists.

### D-50 — Does the product back up the user's data?
**From:** Phase 2 OQ5 + Phase 8 OQ16 · **Cluster:** `cross-cutting` · *Defect instances: #52*

`AppCommands.CommandFileBackup` has a command binding in `MainWindow.xaml` and a complete
implementation (`OnCommandBackup`, with per-storage-format file filters) — and no menu item,
button or keyboard gesture anywhere references it, so backup is dead UI.
`CommandFileRestore` and `CommandReportBudget` are declared and never bound at all. Phase 8
confirmed the other half: `Settings.BackupPath` is a live preference, read, written and
migrated like any other, so the product persistently remembers where backups should go and
offers no way to make one. The stakes are higher than they look, because attachments and
statements live in folders *beside* the financial file (P8-ATT-2, P8-STMT-3), so "there is
no backup" means the irreplaceable documents are unprotected too. The decision: is backup a
product capability (and if so, is it manual, scheduled, versioned, and does it include the
document folders?), or is it explicitly the user's problem and the dead command and
preference come out? If it is restored, Phase 9's OQ16 documents the behaviours being
restored (B-108) — those engines' `Backup` implementations have their own sharp edges.

### D-51 — Does the product ship in-product help?
**From:** Phase 4 OQ19 · **Cluster:** `cross-cutting`

Four windows carry a `HelpKeyword`: `AccountDialog`, `OnlineAccountDialog`,
`AttachmentDialog` and `SampleDatabaseOptions`. The other twenty-five do not, including the
loan, category, rename-payee and online-service windows, which are at least as likely to
need explaining — and the reports options panel points F1 at the reconciliation topic
because the keyword was copy-pasted from `BalanceControl` (B-48). So P4-DLG-2 describes what
exists rather than a guarantee. The decision is whether contextual help is part of the
product (in which case it is a requirement of the shared dialog base in D-15, with a
coverage check, and someone owns writing the topics) or whether the redesign relies on the
UI being self-explanatory and the existing help wiring comes out. Half-covered help that
sometimes opens the wrong topic is worse than neither.

### D-52 — Is printing a capability of this product?
**From:** Phase 5 OQ6 + Phase 8 OQ20 · **Cluster:** `cross-cutting`

The only `Print` command binding in the entire product is in `AttachmentDialog`. There is
no print menu item, button, keyboard shortcut or context-menu entry for a report, a
register, a statement, a balance or a chart; `FlowDocumentView` adds none. (The underlying
WPF document viewer has its own built-in print handling, so Ctrl+P with focus inside the
document may do something, but the product neither advertises nor tests it.) Phase 8's
cross-cutting sweep confirmed from the other side that there is no shared printing layer,
no page setup and no print-specific layout anywhere — this is build-from-nothing, not
wiring-up. For a personal-finance product whose reports are mostly things you hand to an
accountant, that is a conspicuous gap, and it trades directly against D-17: a good
export-to-PDF story may be a better answer than a print pipeline, but they are different
amounts of work and only one of them needs page layout.

### D-53 — How does the product ship, version and refresh its reference data?
**From:** Phase 7 OQ18 · **Cluster:** `cross-cutting`

`FederalTaxes.Load` and `StateTaxes.Load` do `Path.Combine(Path.GetDirectoryName(assembly
.Location), "Taxes", "<file>.json")` and `File.ReadAllText` with no existence check and no
error handling, relying on `CopyToOutputDirectory=Always`. `TxfSpec.txt`, in the same
folder, is an `EmbeddedResource` instead. Three data files, two delivery mechanisms, and the
two that can go missing are the two with no fallback — and `assembly.Location` is empty
under single-file publishing, which would make `Path.Combine` throw. Phase 7's own closing
note (OQ25) points out this is really a "how does this product ship reference data" question
that also covers the institution directory and the stock-quote and split caches. The
decision spans all of them: embedded and versioned with the build (simple, stale between
releases), downloadable with a cache and a visible vintage (current, needs an update service
and a trust story — see D-42), or user-supplied. D-29 depends on the answer.

---

# BUG-ONLY items

Already-understood defects with one correct answer. Full detail lives in the referenced
issue. `untracked` means the audit found it but no issue covers it yet — 18 of these, and
they should be appended to #52 (or the relevant high-priority issue) before the panel
convenes so nothing falls between the catalog and the tracker.

| ID | Open question | Summary | Tracked |
|---|---|---|---|
| B-1 | P1 OQ2 | `CategoryType.Reserved` unused-undeletable; `RecurringExpense` silently rewritten to `Expense` | #52 |
| B-2 | P1 OQ3 | `Account.SyncGuid` has no observable behaviour anywhere | #52 |
| B-3 | P2 OQ2 | `Edit ▸ Cleanup` ships GC.Collect and a full environment-variable dump to end users | #52 |
| B-4 | P3 OQ3 | `CurrenciesView.Caption` returns "Securities"; its ViewState is written against `Security` | #52 |
| B-5 | P3 OQ6 | `ChangeTracker` per-type accessors are dead and would throw `InvalidCastException` | #52 |
| B-6 | P3 OQ7 | Double-clicking an account is unreachable — hit-test result discarded, `item` always null | #52 |
| B-7 | P3 OQ8 | A newly created account/loan is never selected (`Account` assigned to an `AccountViewModel` list) | #52 |
| B-8 | P3 OQ10 | Advanced search offers `Field.Budgeted`, which the model can't answer per transaction (resolution follows D-3) | #52 |
| B-9 | P3 OQ11 | `QueryViewControl.Cut` always overwrites the clipboard with an empty query | #52 |
| B-10 | P4 OQ3 | `LoanDialog.ButtonGoToWebSite` dereferences `WebSite` before its null guard | #52 |
| B-11 | P4 OQ4 | Loan window validates nothing, has no help topic, copies an `OpeningBalance` it can't edit | #52 |
| B-12 | P4 OQ5 | `ShowHideFieldsForAccountType` has no `default:` arm — irrelevant fields stay visible | untracked |
| B-13 | P4 OQ6 | `AuthTokenDialog` computes a fallback label and passes the empty one anyway | #52 |
| B-14 | P4 OQ7 | `OnlineServiceDialog` Cancel keeps the changes — `Apply()` is empty by admission (see D-13) | #52 |
| B-15 | P4 OQ8 | `MergeCategoryDialog`'s OK is `IsCancel="True"`, nothing is `IsDefault`; call sites test the result two ways | #52 |
| B-16 | P4 OQ9 | `RenamePayeeDialog.CheckConflicts` builds a `Regex` from a 50ms timer; malformed pattern throws uncaught | #52 |
| B-17 | P4 OQ10 | Multiple `IsDefault="True"` buttons in three dialogs, one of them destructive (Disable clears an access key) | #52 |
| B-18 | P4 OQ11 | `AttachmentDialog.OnDrop` copies `files[0]`'s content N times with each file's extension | #54 |
| B-19 | P4 OQ12 | `AttachmentDialog.Save` deletes and fails to rewrite non-image attachments; `Copy` empty, `Cut` destroys | #54 |
| B-20 | P4 OQ13 | `RentalDialog` unit edits survive Cancel — `ShallowCopy` is a `MemberwiseClone` sharing the list (see D-13) | #52 |
| B-21 | P4 OQ14 | `MoneyFileImportDialog` files imported attachments against the *source* file's transaction id | #54 |
| B-22 | P4 OQ15 | `ReportRangeDialog.ShowInterval(true)` re-shows the label but not the combo | untracked |
| B-23 | P4 OQ17 | Adhoc SQL Query is always enabled and silently does nothing on SQLite | #52 |
| B-24 | P4 OQ18 | Four newest dialogs derive from `Window`, not `BaseDialog` — white system windows in dark mode | #52 |
| B-25 | P4 OQ21 | `AccountListItem.ToolTipMessage` raises `PropertyChanged` for a property that doesn't exist | #52 |
| B-26 | P4 OQ24 | "Test database" tick-box shown in release builds for SQL Server, `#if !DEBUG`-hidden for SQLite | untracked |
| B-27 | P4 OQ26 | Six of eight `FreeStyleQueryDialog` File-menu items have no handler and no command | #52 |
| B-28 | P4 OQ27 | `SampleDatabaseOptions` defaults to a relative path, so it opens with OK disabled unless the caller fixes it | untracked |
| B-29 | P5 OQ1 | `Payments.ComputeRecurrence` excludes any category that *has* a frequency; outlier removal recomputes unfiltered | #52 |
| B-30 | P5 OQ2 | `GenerateHtmlReport` doesn't await `Generate`; the `using` closes the writer mid-write | #52 |
| B-31 | P5 OQ5 | `ReportViewState` is `[XmlIgnore]` and `DeserializeViewState` returns a bare `ViewState`; portfolio state holds unserializable predicates (evidence for D-8) | #52 |
| B-32 | P5 OQ7 | `AccountSummaryReport` formats with the machine's currency symbol beside the account's real currency code | untracked |
| B-33 | P5 OQ9 | Cash-flow CSV omits the Unknown group and the Total row and flips expense signs — doesn't match the screen | untracked |
| B-34 | P5 OQ10 | `PortfolioReport.WriteSummaryRow` opens a cell it never closes when the third column is null | untracked |
| B-35 | P5 OQ11 | `GenerateCapitalGains` calls `EndTable()` unconditionally — unbalanced document, HTML writer throws | #55 |
| B-36 | P5 OQ12 | `TaxReport.ApplyState` sets the report-type combo from the consolidation flag, index inverted | #55 |
| B-37 | P5 OQ13 | RMD age computed as 73/75 but the distribution table's `RmdAge` is hard-coded to 75 | #55 |
| B-38 | P5 OQ14 | State tax computed on `income` where the adjacent federal line uses `amount` | #55 |
| B-39 | P5 OQ15 | Closed brokerage/money-market accounts counted in the plan — `&&`/`||` precedence, twice | #55 |
| B-40 | P5 OQ16 | `ChartData.Export` indexes every series by `Series[0]`'s count; also leaks an empty temp file per export | #52 |
| B-41 | P5 OQ17 | `ComputeLinearRegression` tests `c == last` twice where `first` was meant | #52 |
| B-42 | P5 OQ18 | Split transfers tallied with the running remainder instead of the line's subtotal, in two must-stay-in-sync methods | #55 |
| B-43 | P5 OQ19 | `Charts/Styles.cs` loads a `Charts/styles.xaml` that doesn't exist; nothing calls it | #52 |
| B-44 | P5 OQ21 | `NetWorthReport.Register()` uses `-=` where every other report uses `+=`, hiding a `NotImplementedException` | #52 |
| B-45 | P5 OQ24 | The portfolio report's inline heading `DatePicker` is unreachable once the report is sited | #52 |
| B-46 | P5 OQ25 | `Report.ExportReportAsCsv` reports failures in a box titled "Error Exporting .txf" | #52 |
| B-47 | P5 OQ26 | `AnimatingBarChart.OnDataChanged` throws a bare `Exception` from a property setter | untracked |
| B-48 | P5 OQ27 | `ReportsControl`'s help keyword is the reconciliation topic; two of nine reports set none | #52 |
| B-49 | P5 OQ28 | `OnCommandReportUnaccepted` constructs a `FlowDocumentReportWriter` and discards it | #52 |
| B-50 | P6 OQ1 | Multi-file import drains its accumulator lists inside the per-file loop — three files import six times | #56 |
| B-51 | P6 OQ2 | `GroupCsvByAccount` adjusts `accountNameIndex` once per row, deleting an innocent column after the first | #56 |
| B-52 | P6 OQ3 | CSV rows with no account number are silently dropped; cancelling the account picker NREs | #56 |
| B-53 | P6 OQ4 | `($1,234.56)` imports as `+1234.56` — accounting negatives become income | #56 |
| B-54 | P6 OQ5 | QIF import asks for an account three times; "does not currently exit" typo; type guessed from the file path containing "Checking" | untracked |
| B-55 | P6 OQ6 | `accountTypeMismatch` computed before the switch that sets `at` — refuses correct merges, permits wrong ones | #56 |
| B-56 | P6 OQ7 | A first-time QIF import marks nothing `Unaccepted`, unlike every other route in | #52 |
| B-57 | P6 OQ8 | `AccountsControl.Paste` always reports failure — `LastAccount` is never assigned on that path | #52 |
| B-58 | P6 OQ9 | The product's own CSV export can't be re-imported: `ToString("C2")`, machine symbol, `ToShortDateString()` | #56 |
| B-59 | P6 OQ10 | `AccountsControl.ExportList` writes balances as `"C3"` with the machine's symbol beside a currency column | untracked |
| B-60 | P6 OQ11 | OFX encoding regex is run against a hard-coded sample string, never the file — always "utf-8" | #56 |
| B-61 | P6 OQ12 | "Save As → .csv" writes the current transaction view's rows, not the file | #56 |
| B-62 | P6 OQ14 | A failed OFX version retry leaves `OfxVersion` flipped; `Signup`'s copy of the same retry restores it | #52 |
| B-63 | P6 OQ15 | An MFA challenge with only machine questions errors out, discarding the answers it just computed | #56 |
| B-64 | P6 OQ16 | MFA timestamp answered with lowercase `hh` — every afternoon answer is twelve hours out | #52 |
| B-65 | P6 OQ17 | DGML account map: no error handling on the write, deleted transactions included, null deref, bare `catch {}` | #52 |
| B-66 | P6 OQ19 | `UpdateCachedProfile` merges two identifier-less institutions; `GetCachedBankList` re-saves on read; reference-equality change detection | #52 |
| B-67 | P6 OQ21 | Dead second CSV importer; `Exporters.SupportXml` never set true; `ExportAccount`'s dedupe field never cleared | #52 |
| B-68 | P7 OQ1 | Federal capital-gains bands walked against gains alone, ignoring ordinary income | #57 |
| B-69 | P7 OQ2 | Long-term test is `Days > 365`, a day out across a leap year | #57 |
| B-70 | P7 OQ4 | The per-account `.txf` route skips `WriteHeader` entirely | #57 |
| B-71 | P7 OQ5 | `.txf` acquisition/sale dates written with `ToShortDateString()` | #57 |
| B-72 | P7 OQ6 | Report and `.txf` disagree in sign (expense flip) and amount (tax-exempt inclusion); `WriteRecordFormat1` skips `Round` | #57 |
| B-73 | P7 OQ7 | `ExportCategories` handles only record formats 1 and 3 — 56 of 376 tax lines export as nothing, no `default:` | #57 |
| B-74 | P7 OQ8 | `GenerateGroups` drops any transaction whose group key (usually the payee) is empty, uncounted | #57 |
| B-75 | P7 OQ9 | `TaxCategory` instances are process-wide statics that `GenerateGroups` writes each run's results onto | #52 |
| B-76 | P7 OQ11 | Three states' `deductionPercentage` never read; two more states spell the key `"deduction"`, matching no property | #57 |
| B-77 | P7 OQ12 | Bracket inflation moves Single/Married/Head income brackets only — not Separate, not capital gains, not the standard deduction | #57 |
| B-78 | P7 OQ16 | The bracket walk loses a cent per band; `Bracket.Max` is never read, so no table ordering is validated | #52 |
| B-79 | P7 OQ17 | `StateTaxes.json` has a trailing comma; `RetirementControl`'s constructor loads it eagerly with no try/catch | #52 |
| B-80 | P7 OQ20 | `RetirementPlan.FindState` falls back to `Data[0]` — Alabama — for any unrecognised state | #52 |
| B-81 | P7 OQ21 | "Tax Excempt" column header; "TuboTax" export tooltip | #52 |
| B-82 | P7 OQ22 | `TaxReport.GetSalesTax` filters on `t.Date` while every other figure uses `t.TaxDate` | #52 |
| B-83 | P7 OQ23 | `W2Report` doesn't exclude tax-deferred/tax-free accounts and rounds every figure to whole dollars | #52 |
| B-84 | P8 OQ1 | `GetUniqueStatementName` tests the bare parameter, not the full path — statements silently overwrite | #54 |
| B-85 | P8 OQ2 | `ImportStatements` passes a foreign relative path; merged statements arrive with no document and no hash | #54 |
| B-86 | P8 OQ3 | `MessageBoxEx.Show` returns `None` immediately when called off the UI thread — the question answers itself | #52 |
| B-87 | P8 OQ4 | `MessageBoxEx` computes a default button and ignores it; Escape always cancels, even OK-only | #52 |
| B-88 | P8 OQ5 | `DownloadLog.OnQuoteAvailable` calls `TryUpdate` on a key nothing ever adds — today's quote never merges | #52 |
| B-89 | P8 OQ6 | `DirectorySetup.AddWritePermission` never calls `SetAccessControl` — nothing is granted | #52 |
| B-90 | P8 OQ7 | `CrashReport.Save` uses `File.OpenWrite` (no truncate); `Load` then throws and deletes both reports | #52 |
| B-91 | P8 OQ8 | `TempFilesManager.RemoveTempFile` removes from the list it is `foreach`-ing — throws whenever it matches | #52 |
| B-92 | P8 OQ10 | `FilterLiteral` judges the first row by a different date rule from every later row | #52 |
| B-93 | P8 OQ11 | Hard-coded `MSFT` / 2025-01-08 debug branch shipped in `GetMissingDataRanges`; silent ten-range cap | #52 |
| B-94 | P8 OQ15 | `Settings.ReadXml`/`WriteXml` throw on any unhandled property type; no try/catch around `Settings.Load` | untracked |
| B-95 | P8 OQ17 | `CalculatorPopup` ignores `OemMinus`; `CalculatorControl.OnKeyDown` is fourteen empty cases | #52 |
| B-96 | P8 OQ21 | `MoneyDataObject` implements every `SetData` overload as `throw new NotImplementedException()` | #52 |
| B-97 | P8 OQ22 | `ChangeInfoReport.HasLatestVersion` returns unconditionally inside the first loop iteration | untracked |
| B-98 | P8 OQ23 | Background failures swallowed — empty `catch {}` in `AttachmentWatcher`, debug-only elsewhere (see D-36) | #52 |
| B-99 | P9 OQ1 | Whole-file save calls `OnUpdated()`/`RemoveDeleted()` before the transaction commits — a failed commit destroys the work | #58 |
| B-100 | P9 OQ5 | Three generated server passwords written in clear to `dataengine.config.json`, `MyMoneyAdmin` holding `dbcreator` | #58 |
| B-101 | P9 OQ6 | A half-failed bootstrap loses the passwords for logins it already created; retry silently rotates them | #58 |
| B-102 | P9 OQ7 | `AddLogin` concatenates user input into `sp_addLogin`/`sp_addsrvrolemember` and grants `sysadmin` | #58 |
| B-103 | P9 OQ8 | `XmlStore` accepts a password it never uses; `.bxml` load leaves decrypted plaintext in `%TEMP%` on failure | #52 |
| B-104 | P9 OQ11 | SQL CE branch says "SQL Express does not appear to be installed" and links the Compact download | #52 |
| B-105 | P9 OQ13 | `XmlStore.QueryDataSet` ignores the query and returns the whole file; `CsvStore` returns empty | #52 |
| B-106 | P9 OQ14 | `XmlStore`/`CsvStore` `GetLog()` return `""`; the stored-proc engine bypasses the logging base entirely | #52 |
| B-107 | P9 OQ15 | `SqlCeDatabase` builds its connection string with SQL Server's builder and ignores `includeDatabase` | untracked |
| B-108 | P9 OQ16 | `Backup` uses `File.Copy` with no overwrite on three engines (delete-first workaround leaves a gap); SQL Server flips recovery model and uses `WITH INIT` | #52 |
| B-109 | P9 OQ17 | `DBString` rewrites CR/LF as the literal two-character sequences `\r` and `\n`, which T-SQL does not interpret | #52 |
| B-110 | P9 OQ18 | `UpdateLoanPayments`' update branch skips `DBString` on `Memo`; same omission in four other updaters; unbracketed database names | #58 |
| B-111 | P9 OQ19 | `XmlStore.Save` never calls `money.OnSaved()` — the file still shows as unsaved; `Load` asymmetries too | #52 |
| B-112 | P9 OQ20 | `DataEnginePasswordGenerator` takes `value % charset.Length` with no rejection sampling | untracked |
| B-113 | P9 OQ21 | `LoadDatabasePassword` signals "no password stored" by throwing; `SaveDatabasePassword` swallows every exception | untracked |
| B-114 | P9 OQ24 | `SqlServerStoredProcDatabase.ReadStockSplits` declared `new` over a `protected virtual` base member | #52 |

**Untracked (18):** B-12, B-22, B-26, B-28, B-32, B-33, B-34, B-47, B-54, B-59, B-94, B-97,
B-107, B-112, B-113 — plus B-84 and B-85 which *are* in #54's body but were listed as
"not yet filed" in the catalog's closing note (they have since been added), and B-103 which
is covered by #52's Phase 9 section rather than #58 where its siblings live.

---

# HOUSEKEEPING items

Catalog-construction and phase-boundary calls. No bearing on what the redesign should do;
listed to show they were read and dismissed rather than missed.

| ID | Open question | Why it's housekeeping |
|---|---|---|
| H-1 | P1 OQ6 | `Transaction.to`/`Split.to` classified as infrastructure supporting P1-XFER-8 rather than a capability of their own — a labelling call |
| H-2 | P1 OQ7 | Stock-quote fetching referenced in Phase 1 but described in Phase 8 — phase ownership |
| H-3 | P1 OQ8 | Attachments appear in Phase 1 only as a flag; the documents themselves are Phase 8 — phase ownership |
| H-4 | P2 OQ4 | The settings flyout catalogued as shell (2.9) rather than deferred to Phase 4 — boundary call, flagged with the exact scenarios to revisit |
| H-5 | P2 OQ6 | Left-navigation panels split between Phase 2 (navigation) and Phase 3 (their own capabilities) — boundary call |
| H-6 | P2 OQ8 | Chart-strip *arrangement* is Phase 2, chart *content* is Phase 5 — boundary call |
| H-7 | P3 OQ12 | "The door, not the room" — modal flows captured as affordances here, contents owned by Phase 4 |
| H-8 | P3 OQ13 | `FlowDocumentView` hosts both the in-register portfolio and every standalone report; its own controls catalogued in Phase 3 in passing |
| H-9 | P3 OQ14 | `GraphGenerators.cs` lives in `Views/` but is owned by Phase 5 — location vs ownership note |
| H-10 | P4 OQ28 | Boundary with Phases 5/6 for the tax-report, report-range and import dialogs — option collection here, contents there |
| H-11 | P4 OQ29 | Modal UI outside `Dialogs/` (`MessageBoxEx`, print dialog, native pickers) left to Phase 8 — scope note |
| H-12 | P5 OQ30 | Where the Phase 7 line was drawn around `TaxReport`/`W2Report` and the tax machinery behind them |
| H-13 | P5 OQ31 | Where the Phase 6 line was drawn — report file-writing captured as report affordances, not export formats |
| H-14 | P6 OQ22 | `.txf` file-writing mechanics assigned to Phase 7 rather than Phase 6, with the reasoning recorded |
| H-15 | P6 OQ24 | Where the Phase 8 line was drawn for attachments and the quote cache (the two defects noted in passing — `DownloadControl.SelectEntry`'s endless timer and `DoSync`'s useless guard — are in #52) |
| H-16 | P7 OQ19 | Crossed namespaces and a filename naming another file's class — a navigation note for readers, not a redesign input |
| H-17 | P7 OQ24 | `MyMoney.Business/CostBasis.cs` claimed by Phase 7 rather than 5 or 8; only the ownership label is in question |
| H-18 | P7 OQ25 | Fiscal-year start, report printing and tax-data delivery handed to Phase 8 — scope note (the delivery half became D-53) |
| H-19 | P8 OQ25 | `MyMoney.Data/` scoped out of Phase 8 and asked to be confirmed; Phase 9 closed it, so the qualifier no longer applies |
| H-20 | P8 OQ26 | `AutoCategorization` and `Query.cs` claimed without new scenarios; notes that cross-account category suggestion should get a scenario of its own — a catalog gap, not a decision |
