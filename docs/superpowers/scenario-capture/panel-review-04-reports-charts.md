# Panel Review 04 — `reports-charts` (D-16 … D-24)

One expert-panel session over the whole `reports-charts` cluster of the
[Redesign Decision Register](redesign-decision-register.md) — all nine decisions,
`D-16` through `D-24`. Traceability runs back through each register entry to a Phase 4 or
Phase 5 open question in [`end-user-scenarios.md`](end-user-scenarios.md); the panel read
those catalog sections, and in several cases went back to the source, before opining.

| | Decision | Recommendation | Needs your decision |
|---|---|---|---|
| D-16 | How is a report or chart scoped, and is there still a shared range dialog? | One inline scope model, no modal; category filtering as a declared per-surface capability | **Partly** — the category-filter half |
| D-17 | What is a shareable report, and is HTML it? | Paginated print/PDF as the share format; HTML export retired unless made self-contained | **Partly** — whether HTML survives |
| D-18 | Does every report export, and to what? | Export becomes a framework guarantee, moved to the report's command surface | No |
| D-19 | What should a mixed-currency total say? | Always normalise to a stated display currency; never silently drop a subtotal | No — but blocked on D-4 |
| D-20 | Should generated category colours be persisted? | Stable-hash into a curated, theme-aware palette; persist only explicit user choices | No |
| D-21 | Does the net-worth picture include what the user owes? | Assets-only composition chart + separate liabilities and net figures | **Yes** |
| D-22 | As at the start or the end of the chosen day? | Close-of-day (inclusive) everywhere, stated in words, enforced by one range type | No |
| D-23 | How are report options hosted, and should a report remember them? | Declarative parameter model in a long-lived host; parameters *are* the view state | No |
| D-24 | One vocabulary for "a period" | Three named types — recurrence, granularity, span — refusing to merge the first two | No |

**Panel members.** Architect · Developer (C#/.NET, WPF, WPF-UI/Fluent) · Test Engineer ·
Operations Engineer · UI/UX Expert · Data Engine Expert (SQLite/SQL Server) ·
Adversarial Expert. Opt-outs are stated explicitly per decision rather than left silent.

**Three findings from source that changed the panel's answers**, recorded up front because
they are not in the register and two of them invert the obvious reading:

1. **D-20.** The "colour derived from the category name" that P5-CHART-4 promises is
   `ColorAndBrushGenerator.GenerateNamedColor`, whose whole body is
   `name.GetHashCode()` split into RGB bytes (`Utilities/Colors.cs:39-48`). On .NET Core
   and later — this app is .NET 10 — `string.GetHashCode()` is **randomised per process**
   and cannot be turned off. So the generated colours are *not* stable across restarts
   today. The only reason P5-CHART-4 appears to hold is the very side effect the register
   objects to: the colour gets written into the user's file the first time a chart draws.
   Remove the persistence naively and category colours reshuffle on every launch.
   Separately, `NetWorthReport.GetRandomColor` and `PortfolioReport.GetRandomColor` are
   literally `rand.Next(80, 200)` per component, per render — those two reports' swatches
   and pie slices have no stability at all, and P5-CHART-4 is simply false there.
2. **D-22.** The portfolio report's *computation* is already end-of-day inclusive.
   `MyMoney.GetTransactionsGroupedBySecurity` breaks on `t.Date > toDate` and
   `GetCashBalanceNormalized` breaks on `t.Date > date`; transaction dates are date-only,
   so a trade dated 31 December **is** counted when the report date is 31 December. The
   heading is right; only the `ReportDate.AddDays(-1)` sub-heading lies. That makes the
   "which is it" question much cheaper to settle than the register implies.
3. **D-18 / D-19.** Two escape hatches already exist and are unused. `CsvReportWriter`
   implements the same `IReportWriter` every report already writes through, and
   `PortfolioReport.Export` is one line — `Generate(new CsvReportWriter(writer))` — so
   "every report can export" is nearly free. And `AccountSummaryReport`'s mixed-currency
   collapse can only fire when the user has *not* chosen a normalisation currency; picking
   one in the options panel already converts everything and makes `various` unreachable
   (`AccountSummaryReport.cs:219-247`).

---

## D-16 — How is a report or chart scoped, and is there still a shared range dialog?

**From:** Phase 4 OQ16 (catalog P4-REPORT-1/2/3, P3-PANEL-1) · **Register:** D-16

### ELI10

When you want the program to show you something about your money, you have to tell it
*which bit* — usually which stretch of dates, and sometimes which kinds of spending.

Right now the program asks that question in two completely different ways depending on
what you're looking at. If you're reading a page of numbers, you change the dates in a
strip down the left-hand side of the window and the page redraws as you go. If you're
looking at the little picture under your list of transactions, you pick "custom range"
from its menu and a separate pop-up window opens on top of everything. That pop-up is
titled "Report" in its own code, because it was built for reports years ago — but no
report uses it any more. The picture's menu quietly renames it to "Graph Range" as it
opens it, which is the software equivalent of covering a sign with tape.

Half of that pop-up is a ghost. There's a tick-list in it where you'd choose which
spending categories to include. It is switched off *and* never filled in — two separate
things both have to change before anybody could ever see it, and neither ever does. So
somebody once intended "show me only groceries and fuel" and the intention is still
sitting there, unfinished, where nobody can use it.

Two things have to be decided. Should picking "which bit" work the same way whether you're
looking at numbers or pictures? And should you actually be able to narrow a report down to
the categories you care about — a thing people ask for constantly and this program has
never delivered?

### Alternatives

**A. One scope model, always inline, no separate window.**
Define a single "what am I looking at" object — a date span, a granularity, and optionally
a set of accounts and categories — and render it inline wherever it applies: the side
panel for a report, a Fluent flyout anchored to the chart's own toolbar for a chart. The
`ReportRangeDialog` window is deleted outright. Each surface declares which parts of the
scope it supports, so a chart that has no notion of granularity never renders that row.

*Pros.* One mental model and one control set for the user; live preview everywhere
(changing the date redraws behind you, which is what the report panel already does and the
modal deliberately prevents); deletes a whole window, a `CheckItem` class and two dead
properties; the scope object becomes the natural unit to hand to D-23's parameter model
and to persist for D-8's "put me back where I was".

*Cons.* Charts currently live in a cramped strip beneath the register; a flyout there is a
layout problem, not just a code problem. "Everything inline" also means the chart's scope
controls compete for space with the register itself.

*Unintended consequences.* Losing the modal loses the implicit OK/Cancel — a user who
drags a date to 1970 by accident has no "cancel" any more, so the surface needs an undo or
a reset-to-default affordance it doesn't have today. Declaring capabilities per surface is
a small contract that will be under pressure to grow (does a net-worth report support a
category filter? no — and somebody will file that as a bug). And an inline scope strip
that is *always* visible changes the default look of the chart area for every existing
user, which is a perceptible regression to anyone who liked the space.

**B. Keep two hosts, unify the underlying model and the control template.**
Same scope object as A, but accept that a report configures itself in a panel and a chart
configures itself in a pop-up. One WPF-UI templated control renders the scope; it is
hosted in the side panel in one case and in a small owned window in the other.

*Pros.* Cheapest credible change — the chart's existing "open a window" gesture survives,
so no layout work and no muscle-memory break; the duplicated date-picking code still
collapses to one implementation; the window can be made to behave (owner, theming,
keyboard) under D-15's dialog contract in one place.

*Cons.* Keeps a modal in a product that is otherwise moving toward live, inline settings;
two hosts means two sets of visual bugs and two sets of UI tests; the user still learns
two gestures for one idea.

*Unintended consequences.* A "unified model, two hosts" arrangement is exactly what
produced today's mess — the pop-up *was* the shared surface once. Without a rule about
which host wins, the next feature gets added to whichever one its author was looking at,
and the drift restarts. It also quietly preserves the pop-up as the place where a rarely
used option can be parked, which is how the dead category list got there.

**C. Scope *is* a filter — route everything through the existing query mechanism.**
The register already has a quick filter and an advanced search (`QueryRow`, `Filter<T>`).
Make "scope" a saved filter object: reports and charts both take a filter, date span
included, and the same UI that builds a search builds a report's scope. Category filtering
comes free because the query layer already supports a category condition.

*Pros.* One filtering concept across the whole product instead of two; named/saved scopes
become possible ("my 2025 tax view"); category and payee filtering arrive without new
design; a report's scope is expressible as text, which helps support and testing.

*Cons.* The query model is transaction-shaped. A net-worth report is not a transaction
query — it is a balance evaluation — and forcing it through a filter object either
produces a filter with meaningless fields or a second kind of filter, which is two
concepts again. The advanced-search UI is also much heavier than "pick two dates".

*Unintended consequences.* Pushing scope into the query layer moves real work onto the
data engine for large books: a category-filtered five-year cash-flow report becomes a
query the storage layer should answer, and today every report walks the in-memory object
graph instead. That is arguably where it *should* go, but it couples this decision to
D-35's engine-capability floor — an engine that "cannot query" (XML, CSV, per B-105/B-106)
would then be unable to run a scoped report at all.

### Panel

- **Architect** — A. The scope object is the thing that makes D-23 and D-8 tractable; if
  the panel invents it here, three other decisions get cheaper. C is the intellectually
  tidier answer and the panel should not take it, because it binds a presentation concern
  to the storage-capability portfolio before that portfolio is settled.
- **Developer** — A is not much dearer than B. The scope control is one templated
  `UserControl` either way; the only extra in A is the chart-side flyout, which WPF-UI
  gives you (`ui:Flyout` / `Popup` with the app's theme resources) rather than a `Window`
  needing D-15's whole owner/centring/theme contract.
- **Test Engineer** — strongly A. A modal window in a FlaUI suite is a shared-session
  hazard: a leftover `ReportRangeDialog` corrupts every subsequent test in the fixture,
  which is the exact failure mode `docs/dev/flaui-basics-test-notes.md` already documents.
  Inline scope is assertable without window hunting, and a scope object that round-trips
  through serialisation is unit-testable with no UI at all.
- **Operations Engineer** — no data migration in any option; deleting the dialog cannot
  break an existing file. One note: if scope becomes persisted view state (A or B), it
  needs a version-tolerant reader, or an old saved scope crashes a new build.
- **UI/UX Expert** — A, and the category filter belongs as a *chip/pill* row ("Groceries
  ×  Fuel ×  + Add") rather than a tick-list of the whole category tree. A tick-list of a
  three-level category tree with "all ticked by default" is the design that made the
  original feature unusable enough to abandon. Also: the scope strip must show its current
  state in words when collapsed ("Last 5 years · by year · all categories"), or users will
  not know a filter is on — silently-filtered reports are how people make bad decisions.
- **Data Engine Expert** — mostly out of lane, one point: under A and B nothing reaches
  storage differently, so no engine consequence. Under C it does, and that is a reason to
  prefer A now and revisit C once D-35 has named the surviving engines.
- **Adversarial Expert** — the simpler answer nobody has said out loud: the chart's custom
  range is used *rarely*. The chart already has Year-to-date, Show all, Next, Previous,
  Zoom in and Zoom out (P5-TREND-6), which is how people actually move a chart through
  time. Deleting the dialog and adding nothing at all is a defensible option. The panel
  should not build a shared scope surface purely to preserve a feature whose only current
  user is an unlabelled menu item. **Accepted in part:** the recommendation keeps custom
  range, but the panel records that it is a low-value feature and should not be allowed to
  drive the design of the scope model. Second point: "category filtering is what users ask
  for" is an assertion, not evidence — this is a single-user fork; the owner should say
  whether *they* want it before anyone builds a chip-based category picker.

### Recommendation

**Alternative A — one scope model, rendered inline, no modal window** — with per-surface
capability declaration so a surface renders only the parts of scope it honours. Delete
`ReportRangeDialog`, `CheckItem`, `EnableCategoriesSelection` and the unused `Categories`
setter rather than repairing them.

On the second half — **category filtering — the panel does not make the call.** It is a
genuinely new capability, not a repair, and its value depends entirely on how the owner
uses the product. What the panel *does* recommend is that the scope model be shaped so
that adding a category/account filter later is additive (a declared capability plus a
renderer), so the decision can be deferred without being foreclosed.

The argument that won: every other decision in this cluster wants the same object. D-23
needs a serialisable parameter set, D-8 needs restorable view state, D-24 needs one place
where "a span" and "a granularity" are named. Building the scope object here pays for
itself three times over; whether it also carries a category list is a separate, cheap,
later question.

### Needs your decision

**Partly.** The mechanism (one inline scope model, no modal) — panel confident, no
escalation. **Category filtering of reports and charts — needs your decision:** is
"show me only these categories" something you want, or is drilling in from a chart slice
(P5-PIE-4) already how you do that?

---

## D-17 — What is a shareable report, and is HTML it?

**From:** Phase 5 OQ3 (catalog P5-VIEW-6) · **Register:** D-17 · **Trades against:** D-52

### ELI10

When a report is on screen you can ask the program to save it as a web page, so you can
email it to your accountant or keep a copy. What lands on disk is not really a copy of
what you were looking at.

Three things go wrong. The file doesn't contain its own styling — it asks the internet for
it, from a website the file names. Open it on a plane, or in five years when that website
has moved on, and it looks like a wall of unformatted typing. Every chart and every little
colour square is simply missing: the part of the program that writes those to the file is
an empty box that does nothing. And on the reports that have expandable detail — cash
flow, the W-2, the portfolio — the detail lines come out shifted one column to the left,
so the numbers sit under the wrong headings.

On top of that, for four of the reports the program closes the file *before* it has
finished writing it, because those reports fetch share prices while they write and nobody
waits for them. That last one is a plain bug and is already filed. But the rest adds up to
a question worth asking properly: what *should* "give me a copy I can send someone"
produce? A web page? A PDF? Or should the answer be "print it", which this program
currently cannot do at all?

### Alternatives

**A. Self-contained HTML.**
Rewrite the HTML writer so the file stands alone: styles inlined in a `<style>` block
instead of a CDN link, charts rendered to SVG or to a base64 PNG data URI, colour swatches
as inline elements, the expandable groups emitted as `<details>`/`<summary>` (which works
in every browser with no script), and the column alignment corrected so the expander cell
exists.

*Pros.* Opens anywhere, forever, with nothing installed; editable and re-flowable by the
recipient; small; `<details>` gives the recipient the same expand/collapse the on-screen
report has, which no print format can. Keeps the existing export gesture, so nobody has to
relearn anything.

*Cons.* Rendering a WPF chart into an exported file means rasterising a live visual tree
(`RenderTargetBitmap`) or writing a second, headless chart renderer that emits SVG. Both
are real work, and the first only works on the UI thread with the element realised — which
sits badly with reports that generate asynchronously. It is also the format an accountant
is *least* likely to want as an attachment.

*Unintended consequences.* Rasterised charts are fixed-resolution and do not print well,
so "self-contained HTML" quietly becomes "a document that looks worse the more seriously
you treat it". Inlining a full Bootstrap-equivalent stylesheet adds tens of kilobytes to
every export; hand-rolling a minimal one instead means the exported report stops matching
the app's own look the moment the app is restyled — a second visual language to maintain,
which is precisely the drift this whole redesign exists to stop.

**B. Paginated print / PDF as *the* share format.**
Build the print pipeline D-52 describes, and make "save as PDF" and "print" the same
operation. Reports are already `FlowDocument`s hosted in a document viewer, so they are
already paginated — `FlowDocument` implements `IDocumentPaginatorSource`, and
`PrintDialog.PrintDocument(paginator, …)` prints it. A user choosing "Microsoft Print to
PDF" gets a PDF with no extra code at all; a proper PDF writer can come later.

*Pros.* The cheapest first step by a distance — wiring the existing document to the
existing WPF print stack is days, not weeks, and it answers D-52 at the same time. Charts
come out because they are real elements in the document, so `WriteElement` stops being a
no-op *for free*. It is the format the recipient of a personal-finance report actually
expects. Page headers, page numbers and "generated on" belong to a printed document
naturally, and P5-VIEW-13 already writes the last of those.

*Cons.* A PDF is not machine-readable, so it does not replace CSV (D-18) — the product
needs both. Pagination introduces problems the scrolling viewer never had: a table that
splits mid-group, a chart that lands half on each page, column widths that were computed
for screen width. Some real layout work follows the cheap first step.

*Unintended consequences.* Once printing exists, everything else in the product is
expected to print — the register, a statement, a reconciliation. D-52 already says there
is no shared printing layer; adding one for reports creates the expectation without the
capability, and "why can't I print my transaction list" becomes the next issue. Also, a
print path exercises the report writers in a way nothing does today (page-break decisions
need real column widths), which is likely to surface latent layout bugs such as B-34's
unclosed cell that the forgiving on-screen writer currently absorbs.

**C. Retire document export; share by clipboard, spreadsheet and the OS.**
Keep the report on screen. Keep rich-text copy (P5-VIEW-4 already puts a formatted table
on the clipboard). Guarantee CSV for every report (D-18). Delete HTML export. If someone
needs a document, they paste into a document, or the OS's own print-to-PDF handles it once
D-52 lands.

*Pros.* Least code, least surface, no new format to maintain; removes a feature that
currently produces broken files, which is a net honesty gain; the rich clipboard is
genuinely good and badly under-advertised.

*Cons.* "Email this to my accountant" becomes a multi-step manual chore. Removing a
feature users may be using — even a broken one — is a visible subtraction. Without D-52,
option C leaves the product with *no* way to produce a document at all.

*Unintended consequences.* Deleting HTML export deletes `HtmlDocumentReportWriter`, and
with it the only implementation that proves `IReportWriter` is a real abstraction rather
than a `FlowDocument` API with extra steps. The contract quietly degrades to "whatever the
screen writer does", and the next output format is harder to add, not easier.

### Panel

- **Architect** — B, and note what it does to `IReportWriter`: the print path does not need
  a new writer at all, it reuses the flow-document one. That is a signal the abstraction is
  right and the HTML writer was always the odd one out (it is the only writer with a
  `CanExpandCollapse => false` and two silently-empty members).
- **Developer** — B's first increment is genuinely small: a `Print` command on the report
  view, `PrintDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, …)`.
  The honest caveat is that `FlowDocument` pagination respects `KeepTogether`/
  `BreakPageBefore` only if the writers set them, and today they set neither — so the first
  printed cash-flow report will split a group across a page break and look amateur. Budget
  for that, not for the plumbing.
- **Test Engineer** — B is the hardest to assert and the panel should say so. A printed
  page is verified by eye or by image comparison; neither fits `dotnet test`. What *is*
  testable is the paginator: given a report, assert page count, assert no group's header is
  the last element on a page, assert every page carries the trailer. That requires the
  paginator to be reachable without a printer, which it is. A is more testable (an HTML
  file is an XML document you can assert against, and B-35's "you closed too many tags"
  exception is a free structural check) — that is A's one real advantage.
- **Operations Engineer** — B has an operational wrinkle nobody has mentioned: printing
  touches the print spooler and printer drivers, which is the least predictable part of any
  Windows desktop app, and a headless/CI machine has no printers. Whatever ships must not
  throw when `PrintDialog` finds no print queue. Against that, B ships nothing to disk that
  can rot, whereas A ships files whose appearance depends on a third-party CDN's continued
  existence — which is a supportability liability with an indefinite tail.
- **UI/UX Expert** — B. The mental model "a report is a document" is right, and every
  document in the user's life prints. The current export also fails the most basic test of
  a share feature: the thing you send does not look like the thing you saw. B is the only
  option where it does. On A: `<details>` is a nice touch but nobody opens an emailed HTML
  file; they open the PDF.
- **Data Engine Expert** — nothing to add here. No storage involvement in any option.
- **Adversarial Expert** — two challenges. First, the panel is about to recommend building
  a print pipeline off the back of an *export* decision, which is scope creep dressed as
  economy; D-52 is a separate register entry for a reason and the owner may not want
  printing at all. Second, and more pointed: is anyone actually sending these reports to an
  accountant? This is a one-person fork evaluating a Quicken replacement. If the answer is
  "no, I read them on screen and the tax numbers go out via the `.txf` file (P5-TAX-7)",
  then the whole decision is C — delete the broken feature, write nothing, and spend the
  effort on D-18's CSV guarantee, which serves the same need in a format that actually
  round-trips. **The panel takes this seriously** and reflects it in the escalation below.

### Recommendation

**Alternative B — printing and PDF are the share story; the HTML writer is retired rather
than rewritten** — on the strength of one fact: the reports are already paginated
`FlowDocument`s, so the first useful increment is wiring an existing document to an
existing print stack, and it brings charts along for free because they are real elements
in that document. Every other route requires *building* a renderer that HTML export has
never had.

The panel is explicit that this folds D-52 into D-17 and says so rather than pretending
otherwise: answering "what does a shareable report look like" with "a printed page" means
committing to D-52's build-from-nothing print layer, starting with reports only.

The panel did **not** reach full agreement. The Test Engineer's objection stands
unresolved — B is the least verifiable of the three options in an automated suite, and the
panel is trading testability for user value with its eyes open. The Adversarial Expert's
challenge is not resolved either, because it cannot be resolved by the panel: it is a
question about how the owner actually uses reports.

Regardless of which option wins, one thing is not a decision and should just be done:
B-30, the export that closes the file before the report has finished writing to it, and
B-35, the tax report's unbalanced table. If HTML export survives even one more release,
those two make it produce truncated files today.

### Needs your decision

**Partly.** The panel is confident that *HTML export in its current form is not the answer*
and should not be repaired incrementally. It is **not** confident which replacement you
want, because that depends on something only you know: **do you actually hand these
reports to anyone?** If yes → B (print/PDF). If no, and the `.txf` export plus CSV covers
your real hand-off → C (delete it), and D-52 can stay unanswered.

**Panel flags: would benefit from the recipient's expertise** — an accountant or tax
preparer's view of what format they actually want to receive would settle this in one
sentence, and none of the seven roles can speak for them.

---

## D-18 — Does every report export, and to what?

**From:** Phase 5 OQ4 (catalog P5-VIEW-7, P3-PANEL-2) · **Register:** D-18

### ELI10

Nine reports. Two of them have a button that saves the numbers as a spreadsheet file. The
other seven have no button — and no explanation. There's nothing telling you that the
portfolio can be exported but the tax report can't; the button is just absent, and an
absent button looks the same as a feature you haven't found yet.

Underneath, those seven reports don't merely lack a button — they are each wired to fail
if anyone ever asked. The shared "export me" instruction in the base code says "I don't
know how"; three reports go further and say it louder. Nothing crashes today only because
the button that would ask is never shown. One of them is a single character away from
being asked: the net-worth report subscribes to the export button with a minus sign where
every other report uses a plus, which is the *only* reason its crash-on-export never
happens.

So: should every report be exportable, or should exporting be something a report opts
into — and if it opts out, should it say so?

### Alternatives

**A. Export is a guarantee of the report framework.**
Move the export implementation into the base class: `Export(filename)` runs the report's
own `Generate` against a `CsvReportWriter`. Every report inherits it, every report gets the
button, the three explicit `throw` overrides are deleted, and the cash-flow report's
hand-written CSV is deleted in favour of the shared path.

*Pros.* Close to free — `PortfolioReport.Export` already *is* this one line, so the
mechanism is proven. Makes "a report" a stricter, more useful contract: anything that can
write itself to the screen writer can write itself to any writer. Retires B-33 (the
cash-flow CSV that doesn't match the screen) by deleting the code that causes it, rather
than by fixing it. Gives the test suite a uniform hook: generate every report against a CSV
writer and compare to a golden file — nine characterization tests from one helper.

*Cons.* Some reports produce a CSV that is not very useful as a spreadsheet. The retirement
plan is mostly charts; the unaccepted-transactions report is a list that already exists as
a register view. "Every report exports" guarantees a file, not a *useful* file.

*Unintended consequences.* Making export universal makes the writer contract load-bearing,
which surfaces the latent writer bugs the screen writer currently hides — B-34's cell that
is opened and never closed, B-35's unbalanced table — because a CSV writer counts cells and
a forgiving `FlowDocument` writer does not. That is good (they are real bugs) but it means
"turn on export everywhere" lands with a tail of writer fixes attached, and should be
planned as such rather than as a one-line change.

**B. Opt-in, but declared and explained.**
Give `IReport` a capability declaration — the interface already has the precedent, since
`IReportWriter` carries `CanExpandCollapse`. The export affordance is always present but
disabled, with a tooltip saying why ("The retirement plan is a projection; export the
underlying holdings from the portfolio report instead"). Reports that cannot export say so.

*Pros.* Honest about what is and isn't supported without promising a useless file; the
disabled-with-reason pattern is a Fluent convention users read correctly; lets the redesign
ship export on the reports where it matters without doing writer work on all nine.

*Cons.* Somebody has to write nine justifications, and "this report doesn't export because
nobody implemented it" is not a justification you can show a user — so in practice the
capability flag becomes a polite name for an unfinished feature. A permanently disabled
button is also a small, permanent piece of visual noise.

*Unintended consequences.* A capability flag invites more capability flags
(`CanPrint`, `CanDrillDown`, `CanFilterByCategory`), and each one is a branch in the shell.
Two or three is a contract; eight is a matrix nobody tests. The panel should be wary of
opening this door for a reason as small as CSV.

**C. Export leaves the report entirely and becomes a shell command.**
"Export…" joins Copy, Find and (per D-17) Print on the report's own command surface —
context menu and keyboard, not the options panel. Format is chosen in the save dialog:
CSV, and later PDF/HTML. Always available, on every report, for the same reason Copy is.

*Pros.* Puts the affordance where the user looks for it. Today's placement — a button in
the *settings* panel, beside the date pickers — is why P3-PANEL-2 had to be catalogued as
a panel feature rather than a report feature; export is not a setting. Also removes the
event-plumbing that D-23 objects to (`ReportExport` is one of the events every report
re-subscribes on every generation).

*Cons.* On its own it decides nothing about *whether* a given report can produce a file; it
is a placement answer, not a contract answer, and needs A or B underneath it.

*Unintended consequences.* Moving export off the panel removes one of the panel's few
justifications for existing as a *panel* rather than a parameter strip — which quietly
strengthens D-23's case for Alternative 3 there. That is a second-order effect the panel
considers welcome, but it is a real coupling.

### Panel

- **Architect** — A with C's placement. A is the only option that makes the writer
  abstraction mean something; today `IReportWriter` has three implementations and only one
  of them is exercised by all nine reports, which is why the other two rot. Universal export
  makes the CSV writer a permanent second consumer, and second consumers are what keep
  abstractions honest.
- **Developer** — A is a handful of lines plus a tail of writer fixes. Concretely: base
  `Export` becomes the `Task.Run(...).GetAwaiter().GetResult()` shape that
  `PortfolioReport.Export` already uses (with the comment already written on it explaining
  the deadlock it avoids), the three throwing overrides go, and `CashFlowReport.Export` and
  `GenerateCsvGroup` go. Net deletion.
- **Test Engineer** — this is the decision the panel should take purely on testability. A
  gives nine golden-file tests for one fixture and one helper, and those tests are the first
  thing in this whole cluster that would have caught B-33 (exported cash flow ≠ screen cash
  flow) automatically. Under B, the reports that opt out stay untested forever. Strong A.
- **Operations Engineer** — no migration, no data risk, nothing persisted. One note for C:
  if export becomes a shell command, the file dialog and the "open the file afterwards"
  behaviour need one implementation, not the current two (`InternetExplorer.OpenUrl` in the
  report base versus `NativeMethods.ShellExecute` in the charts — B-46's neighbour).
- **UI/UX Expert** — C, emphatically, and then A underneath it. An export button living
  inside a settings panel is a discoverability failure, not a styling one. And B's
  disabled-button-with-tooltip is the right pattern for a capability the product genuinely
  doesn't have — but a CSV of any tabular report is not that, so B here would be dressing
  up an omission as a design.
- **Data Engine Expert** — nothing to add. CSV writing does not touch storage.
- **Adversarial Expert** — the useful challenge is not A-versus-B, it is: **is CSV even the
  right target?** Every one of these exports exists so the numbers can be continued in a
  spreadsheet, and CSV loses the grouping, the indentation, the number formats and the
  currency. `.xlsx` via a small library would preserve all of it and is not much harder for
  a table-shaped writer. The panel should at least record that it chose CSV by inheritance
  rather than on merit. Second, smaller point: B-44 (the `-=` subscription hiding a
  `NotImplementedException`) is a bug, already filed; do not let this decision's writeup
  imply the panel fixed it.

### Recommendation

**Alternative A for the contract, Alternative C for the placement.** Export becomes a
guarantee of the report framework — implemented once in the base class by running the
report against the CSV writer — and the affordance moves out of the options panel onto the
report's own command surface alongside Copy, Find and Print, always enabled.

The reasoning that won was the Test Engineer's: A is the only option under which all nine
reports acquire an automatic, comparable, regression-catching output, and this cluster's
bug list (B-33, B-34, B-35, B-40) is dominated by exactly the class of defect a golden-file
comparison catches on the first run. The Architect's point reinforced it — a second real
consumer of `IReportWriter` is what stops the interface decaying into a `FlowDocument`
wrapper.

The panel records the Adversarial Expert's CSV-versus-XLSX point as **not settled and not
escalated**: CSV is the recommendation because it exists and works, but if a spreadsheet
format is ever revisited, this is where the note lives.

This decision is close to "just fix the bugs", and the panel says so plainly: B-33, B-34,
B-44 and B-46 all live here. The part that is genuinely a decision — and worth the
writeup — is *export is a contract, not a per-report favour*, and *it is a document
command, not a setting*.

### Needs your decision

**No.** The panel is confident. Low risk, net code deletion, no data or migration impact.

---

## D-19 — What should a mixed-currency total say?

**From:** Phase 5 OQ8 (catalog P5-ACCT-2, P5-VIEW-12) · **Register:** D-19 ·
**Coupled to:** D-4

### ELI10

The account summary lists every account you have with what it's worth, grouped by kind —
all your current accounts together, all your savings together, and so on — and then adds
everything up at the bottom.

If two accounts in the same group are in different currencies, the program hits a problem
it doesn't know how to explain, so it does something worse than explaining: it quietly
makes that whole group count as **zero** towards the total. Not "excluded", not "we can't
add dollars to euros" — zero, with no mark on the page. And because of how it keeps track,
a single odd account anywhere in your file can wipe out the grand total for *everything*.

The really annoying part is that the program can already do the right thing. There is a
box in the settings panel where you choose a currency, and when you use it every figure is
converted and the problem cannot happen. The silent-zero only happens when you haven't
told it which currency you want — that is, by default.

So the question is: when your money is in more than one currency, what should a total
actually say?

### Alternatives

**A. There is always a display currency; totals are always converted, and always say so.**
The report never runs without a normalisation currency — it defaults to the file's
configured display currency. Every total is converted at a known rate, and the trailer
states the rate and its as-of date (P5-VIEW-12 already does this when a currency is
chosen). The "various" state ceases to exist. If a rate is missing or stale beyond a
threshold, the report says *that*, in place, rather than converting silently.

*Pros.* The user gets the number they came for. One rule, applied identically in every
report, every chart and the register's own totals. It uses machinery that already exists
and is already correct — this is mostly deleting the `various` branch and changing a
default. Makes the currency an explicit, visible property of the report rather than a
hidden precondition.

*Cons.* A converted total is an estimate presented as a figure, and users will act on it.
It is entirely dependent on D-4: today every rate is anchored to USD by a hard-coded
service and nothing states the convention `Currency.Ratio` satisfies, so "converted at a
known rate" is a promise the model cannot currently keep for a non-USD user.

*Unintended consequences.* Converting by default means a historical report is either
converted at *today's* rate (wrong, but consistent) or at the rate on the report date
(right, but the product stores no rate history — only a single current `Ratio` per
currency). The honest version of A therefore implies either a rate-history table or a
permanent caveat in the trailer. That is a storage decision hiding inside a formatting
decision, and it belongs to D-4.

**B. Never add across currencies — subtotal per currency, side by side.**
A group containing dollars and euros produces two subtotal lines, not one. The grand total
becomes a small set of per-currency totals. An *optional* converted line can be offered
below it, explicitly labelled as an approximation.

*Pros.* Never wrong. Never implies precision it doesn't have. Matches how a multi-currency
accounting system presents the same problem, and is the position a careful user would take
themselves.

*Cons.* Most users have one currency and would now be reading a report shaped around a
case they don't have — unless the shape changes only when it must, which means two layouts
to design and test. It also refuses to answer the question the report exists to answer
("what am I worth?") for exactly the users who most need it answered.

*Unintended consequences.* It breaks the picture, not just the table. A pie chart cannot
show slices in two currencies; the net-worth pie (D-21) and the account-summary chart would
need a currency choice anyway — so B does not actually escape needing A's conversion, it
just relocates it to the chart and leaves the table inconsistent with its own illustration.

**C. One total, with an explicit exclusion note.**
Keep today's behaviour of not adding across currencies, but say it: "Total (excludes 2
accounts in EUR — see below)", with those accounts listed.

*Pros.* The smallest possible change that stops the lying — arguably not a decision at all
but a bug fix, and the panel notes that. Zero dependency on D-4 or on rate quality.

*Cons.* Leaves the user holding two numbers and a subtraction problem. Does nothing for
the chart. And the "total" it shows is a total of an arbitrary subset, which is a number
that shouldn't really be presented as a total at all.

*Unintended consequences.* An exclusion note is a soft failure that users learn to scroll
past. Over time it becomes the permanent state for any user with one foreign account, and
the report is permanently, quietly partial — which is the current defect with a label on
it rather than the current defect fixed.

### Panel

- **Architect** — A, and the important structural point is that *no report should implement
  this*. There should be one "total a set of monetary amounts" primitive that takes a
  display currency and returns a value plus a provenance record (which rate, as of when,
  how many amounts were converted). Every report, every chart and the register total call
  it. The reason this bug exists at all is that `AccountSummaryReport` owns a `commonSymbol`
  *field* and reasons about currency itself.
- **Developer** — A is small: the `various` flag and the `normalizedTotal = 0` line go, the
  currency picker's default changes from unset to the file's display currency, and the
  `symbol` branch collapses. The real work is the provenance/trailer wording and the
  stale-rate check, neither of which exists.
- **Test Engineer** — A is the most testable: a two-currency fixture asserting one converted
  total and one trailer line. B needs a second expected layout per report. C is untestable
  in any meaningful sense because "excludes 2 accounts" is a message, not a figure. Also
  worth a regression test that a single foreign account cannot affect *any* other group's
  subtotal — that cross-contamination via a shared field is the nastiest part of the current
  bug and would not be caught by a per-group test.
- **Operations Engineer** — under A, existing users' reports change numbers on upgrade: a
  group that showed zero starts showing a figure. That is a *good* change, but it is a
  silent numerical change and should be called out in the release notes, not discovered.
  Also: A makes the report depend on the exchange-rate service being reachable, which is a
  new runtime dependency for a report that previously needed none — it must degrade to
  "rate unavailable, showing per-currency subtotals" rather than failing.
- **UI/UX Expert** — A for the total, with B's per-currency rows available as a disclosure
  underneath ("$12,340 · converted — show original currencies"). The currency the report is
  expressed in must be visible in the heading, not only in the trailer at the bottom, or a
  converted figure can be read as a native one. And the rate's as-of date belongs next to
  the figure, not in a footnote — a converted total carries a timestamp the way a stock
  price does.
- **Data Engine Expert** — engaging here, this is my lane. The model stores one `Ratio` per
  currency and nothing else: no history, no as-of timestamp, no source. Converting a
  *historical* balance at that single current ratio is defensible only if it is labelled as
  such. If the product wants historically-correct conversion it needs a rate-history table
  (date, from, to, rate, source), which is a schema addition with a migration and a fetch
  strategy — genuinely out of scope for this decision, but it is what "convert at the rate
  on the report date" actually costs. Recommendation from this seat: implement A against the
  current single ratio, store an as-of timestamp on `Currency` so the trailer can be honest,
  and treat rate history as a separate, later change. Note also that `Currency.Ratio == 0`
  is already checked for defensively in the report — a zero ratio must be treated as "no
  rate", not as a conversion.
- **Adversarial Expert** — the panel is building a multi-currency reporting policy for a
  single-user fork that is being evaluated as a **Quicken Classic replacement for one
  person's data**. If that person's file is entirely in one currency, every option here is
  equivalent and the correct answer is "delete the `various` branch, always show the total,
  move on". The panel should not design a currency subsystem on speculation. **Accepted:**
  reflected below — the recommendation's *first* increment is exactly that deletion, and
  the rest is sequenced behind D-4.

### Recommendation

**Alternative A — there is always a display currency, totals are always converted, and the
report always says what it converted and when** — with B's per-currency breakdown available
as a disclosure rather than as the default shape.

One rule the panel states as non-negotiable and *not* a design choice: **a total may never
silently omit part of itself.** Whatever shape wins, `normalizedTotal = 0` on a mixed group
is a defect, and the cross-group contamination through the shared `commonSymbol` field is a
worse one. Those are fixes, not decisions, and they should not wait for D-4.

The sequencing the panel recommends, which absorbs the Adversarial Expert's objection:

1. Delete the `various`/zero behaviour and the shared field; default the report's currency
   to the file's display currency. (Small, independent, fixes the visible harm.)
2. Move totalling into one shared primitive with provenance, used by every report and
   chart. (Architect's point; prerequisite for doing this once rather than nine times.)
3. Decide rate vintage — current-rate-with-caveat versus rate history — **as part of D-4**,
   not here.

**This recommendation is conditional on D-4.** If D-4 resolves that the product is
dollar-anchored with conversion at display time, A is straightforwardly right. If D-4
resolves toward per-transaction captured rates, the totalling primitive's inputs change and
this should be revisited. The panel's position is that D-4 and D-19 should be answered in
the same sitting, and the register already says so.

### Needs your decision

**No** on the shape — the panel is confident. **But blocked on D-4:** do not implement step
3 before D-4 is answered, and treat step 1 as a bug fix that ships independently.

**Panel flags: would benefit from accounting / financial-reporting expertise** — the
convention for presenting a multi-currency net position (and whether a converted total is
acceptable on a statement at all) is a professional norm none of the seven roles can state
authoritatively.

---

## D-20 — Should generated category colours be persisted?

**From:** Phase 5 OQ20 (catalog P5-CHART-4, P5-PIE-1/2) · **Register:** D-20 ·
**Bug half:** #52

### ELI10

Every category of spending gets a colour on the pie charts, so "Groceries" is the same
colour in the legend as it is in the pie. If you never chose a colour yourself, the program
invents one.

Today it invents one *and quietly writes it into your data file* while it is drawing the
picture. So simply looking at a chart can make the program tell you that you have unsaved
changes — you didn't change anything, you looked at something. That half is plainly wrong
and is already filed as a bug; nobody is arguing about it.

The interesting half is what should happen *instead*, and here the obvious answer turns out
to be wrong. You'd think: don't save it, just work the colour out from the category's name
every time — same name, same colour, forever, nothing stored. That is what the program
looks like it's doing. It isn't. The way it turns a name into a colour uses a number that
modern .NET deliberately **scrambles differently every time the program starts**, for
security reasons. So the colours are only stable because they get saved. Take the saving
away without changing anything else and your pie chart is a different set of colours every
morning.

And it's worse in two reports: the net-worth and portfolio pies pick their colours with a
dice roll on every single redraw. Change the date, get new colours. The promise that "the
same thing is always the same colour" is simply not true there and never has been.

So: how should a category get its colour, and should that colour be part of your data?

### Alternatives

**A. Stable hash into a curated palette; persist nothing that the user didn't choose.**
Replace `name.GetHashCode()` with a deterministic hash that does not vary between
processes (FNV-1a or xxHash over the category's full path), and use it to *index a curated
palette* rather than to make up RGB bytes. The palette is designed once: N distinguishable
hues, contrast-checked, with light and dark variants so charts work in both themes. A
colour the user explicitly picked still lives on the category and still wins.

*Pros.* Stable across sessions and machines with nothing stored. Zero migration, zero
upgrade risk, no dirty-file side effect by construction. The curated palette fixes a
quality problem the hash approach can never fix — `GetHashCode()`-to-RGB produces muddy,
arbitrary, theme-blind colours, and the existing `rand.Next(80, 200)` clamp is evidence
somebody already noticed the raw version looked bad. Makes P5-CHART-4 true *for the first
time* in the net-worth and portfolio reports, which today are pure chance.

*Cons.* The colour is derived from the category's name, so **renaming a category changes
its colour** — and `Category.Name` is the full colon-separated path, so re-parenting
changes it too. A palette of N entries collides above N categories; two adjacent slices can
land on the same hue.

*Unintended consequences.* Users who already have generated colours stored in their file
(which, given the current bug, is most users who have ever opened a chart) will keep seeing
those stored colours, because explicit colours win — so the new palette only shows up for
new categories, and the chart looks like two design eras stitched together, indefinitely.
The panel has to decide whether stored-but-never-chosen colours are honoured or ignored,
and the data does not record which is which. That is the real cost of A and it is not
obvious from the pros and cons.

**B. Assign a colour at category-creation time, as a visible, editable property.**
When a category is created, the product picks the least-used colour from the palette,
stores it, and shows it in the category editor like any other field. Nothing is ever
generated at draw time.

*Pros.* Completely explicit — the colour is a property of the category, visible, editable,
stable under renaming, and never a surprise. Collisions are avoidable because the assigner
can see what's already taken. The chart code gets simpler: read the colour, draw.

*Cons.* Existing categories have no colour, so either they get one on first sight (back to
a write from a read path, the original sin) or the product runs a one-time backfill at
upgrade. A backfill writes to every user's file.

*Unintended consequences.* A backfill is the D-20 complaint relocated, not removed: the
user upgrades, opens their file, and it is dirty. Worse, it is dirty *before they have done
anything*, which reads as data corruption rather than as a side effect. It also permanently
inflates the model — every category now carries a colour whether or not the user cares —
and it makes colour a thing that syncs, merges and conflicts when two files are merged
(`MoneyFileImportDialog`, D-25's territory): two files, same category, different colours,
and now there is a conflict over something cosmetic.

**C. Generate at draw time, persist only on an explicit command.**
Charts show generated colours. A "keep these colours" command writes them to the model as
explicit choices. Otherwise nothing is stored.

*Pros.* Preserves user control without a surprise write; honest about which colours are
chosen and which are invented.

*Cons.* Nobody will find the command, and until they do the underlying generation problem
is unsolved — so C only works *on top of* A's stable generator, at which point the command
adds little. It also introduces a concept ("pinned colours") that has to be explained.

*Unintended consequences.* An opt-in persistence command creates two classes of category
that look identical in the UI, and the difference only shows up later when a rename does or
doesn't change a colour. Invisible state with delayed, confusing effects.

### Panel

- **Architect** — A. The governing principle is that *rendering must not mutate the domain*,
  and the cleanest way to guarantee that is for the renderer to need nothing from the domain
  it might want to write back. A derived colour has that property; B's stored colour does
  not (somebody will add "fill it in if missing"). Also: the colour function belongs in one
  place used by charts, legends, the category tree and the reports — today
  `CategoryData.GetColorFromCategoryName`, `ColorAndBrushGenerator.GenerateNamedColor`,
  `NetWorthReport.GetRandomColor` and `PortfolioReport.GetRandomColor` are four answers to
  one question.
- **Developer** — A is a contained change: one hash function, one palette resource, and the
  deletion of two `GetRandomColor` helpers. The one thing to get right is that the palette
  must be theme-aware — WPF-UI restyles between light and dark, and a hard-coded chart
  palette that looked fine in light mode is unreadable in dark. That means the palette is a
  pair of resource dictionaries with a converter, not a `Color[]` constant. Worth noting the
  existing `ChartLegend` already flips its text between black and white by luminance, so the
  product has half of this instinct already.
- **Test Engineer** — A is the only option that is *testable at all*, and that is close to
  decisive. "Same category, same colour" can be asserted today only within one process, and
  a per-process-randomised hash means the current implementation would pass an in-process
  test and fail the user's actual experience — a test that proves nothing. With a stable
  hash it becomes a real assertion (`Assert.AreEqual(known, ColorFor("Fun:Movies"))`), and
  a palette makes collision behaviour assertable too. Separately: there should be a test
  that generating a chart leaves `MyMoney.Dirty` false, which would have caught the original
  bug and will catch its return.
- **Operations Engineer** — A, decisively, on migration grounds. A changes nothing on disk;
  B writes to every existing file on upgrade. Given this fork's whole purpose is importing a
  real Quicken history, an upgrade step that dirties the file before the user touches
  anything is the worst possible first impression and the hardest thing to explain if it
  goes wrong. Also flagging: whatever is chosen, the *stored* colours that the current bug
  has already written into users' files are now indistinguishable from deliberate choices.
  If the redesign wants a clean slate it needs a one-time "these look auto-generated, clear
  them?" prompt — and the panel should decide that consciously rather than inherit it.
- **UI/UX Expert** — A, and the palette is the point, not the hash. Hash-to-RGB cannot
  produce a good chart: it has no control over hue spacing, saturation or luminance, so
  adjacent slices routinely differ by an amount the eye cannot resolve, and nothing
  guarantees legibility against the legend's background. A curated categorical palette of
  8–12 hues, ordered so that consecutive assignments are maximally distinct, with a defined
  behaviour past the end (repeat with a pattern or a lightness step, not a new random hue),
  is what makes these charts readable. On renaming-changes-colour: acceptable, and arguably
  correct — a renamed category is a different label, and the user is looking at it when it
  happens.
- **Data Engine Expert** — engaging. The concrete storage consequence is small but real:
  `Category.Color` is a persisted string column. Under A it stays and holds only explicit
  choices, so its semantics *change* without its schema changing — a column that used to
  mean "colour, possibly auto" now means "colour the user chose". That is invisible to the
  database and visible to anyone reading old files, so it should be documented. Under B the
  column becomes non-null for every row after a backfill, which is a full-table write on
  upgrade — trivial for SQLite on a personal-scale file, but it is a write, and it competes
  with whatever else the upgrade is doing. No index or query implications either way.
- **Adversarial Expert** — first, credit where due: the finding that `GetHashCode()` is
  per-process randomised means the register's framing ("persisting them is exactly what
  makes P5-CHART-4 hold") is *more* true than it realised — persistence is not a nice-to-
  have, it is currently load-bearing. Anyone implementing "just remove the write" without
  fixing the hash ships a visible regression. Second, the challenge: does the user care?
  Category colours are decoration on two pie charts. If the answer is "not much", the
  cheapest correct answer is A's hash fix alone, no palette work, and the panel should not
  spend a design cycle on a colour system. **Partly accepted:** the recommendation separates
  the two, so the hash fix can ship alone and the palette is an independent improvement.

### Recommendation

**Alternative A — a process-stable hash into a curated, theme-aware palette, persisting
only colours the user explicitly chose.** Two increments that can ship independently:

1. **Correctness.** Replace `GetHashCode()` with a stable hash so generated colours survive
   a restart, and delete the two `GetRandomColor()` helpers so the net-worth and portfolio
   charts stop re-colouring themselves on every redraw. Combined with #52's fix (stop
   writing from the draw path), this is the whole decision's practical content and it is
   small.
2. **Quality.** Replace hash-to-RGB with hash-to-palette-index over a curated, contrast-
   checked, light/dark-aware palette, defined once and used by every chart, legend, swatch
   and category tree in the product.

Explicitly **not** recommended: a creation-time backfill (B), because it converts a
surprise write into an upgrade-time write, which is operationally worse for a product whose
users are mid-migration with irreplaceable data.

One thing the panel deliberately leaves to the owner rather than deciding: the generated
colours already written into existing files by the current bug are now indistinguishable
from deliberate ones. The panel recommends **honouring them** (do not clear anything
automatically, ever) and accepting the mixed look, because the alternative is a destructive
one-time operation on user data to fix a cosmetic inconsistency. That is not a trade the
panel is willing to make on the owner's behalf.

The panel was unanimous. The only live tension was scope — whether the curated palette is
worth doing at all — and splitting it into a second increment resolved it.

### Needs your decision

**No.** The panel is confident. Note that the "write from a draw path" half is issue #52's
bug and has not been re-litigated here.

**Panel flags: would benefit from accessibility expertise** — designing a categorical
palette that remains distinguishable under the common colour-vision deficiencies, in both
light and dark themes, is specialist work, and the UI/UX seat can state the requirement but
not verify the result.

---

## D-21 — Does the net-worth picture include what the user owes?

**From:** Phase 5 OQ22 (catalog P5-NET-1, P5-NET-3, P5-NET-4) · **Register:** D-21

### ELI10

The net-worth report lists everything you own and everything you owe, and adds it up to one
number. Beside the list is a pie chart, which is meant to show what your wealth is made of
— how much is cash, how much is shares, how much is your house.

The code contains a written note saying "we leave debts out of the pie because including
them would be confusing." The very next instruction adds every loan to the pie anyway, and
strips off the minus sign while doing it. So if you have a £300,000 mortgage, the pie shows
a £300,000 slice sitting alongside your savings, as though owing the bank £300,000 were a
kind of wealth. Credit cards are genuinely left out; mortgages are not. The program
contradicts its own written intention in the following line.

There's a smaller oddity in the same picture. Nearly every slice of that pie can be clicked
to see what's inside it. The slices for your house and your loans can't — they just don't
respond, and nothing tells you they're different from the ones that do.

So there's no existing intention to preserve here; somebody has to choose what this picture
is actually showing.

### Alternatives

**A. Assets only, with liabilities and net stated as figures beside it.**
The pie shows the *composition of what you own*, labelled as such ("What your assets are
made of"). Liabilities are a separate, prominent line — "Liabilities: £312,400" — and the
net figure is the headline number above both. Liability detail stays in the table, which is
where it reads properly anyway.

*Pros.* Truthful and immediately understandable. A pie represents parts of a whole, and
assets *are* a whole; assets-and-liabilities are not. Smallest change from today — remove
one call's liability contribution and relabel the chart. Keeps the drill-downs that already
work.

*Cons.* The pie no longer represents the report's headline total, so chart and table
disagree by design and the labelling has to carry that. A heavily-mortgaged user sees a
reassuring pie above a modest net figure, which is a mild mismatch in tone.

*Unintended consequences.* Labelling the chart "assets" quietly reveals that the report's
*groups* are not clean either — "Taxable Assets" currently includes loans owed **to** the
user (`WriteLoanAccountRows(..., false)`), which is an asset, correctly, but sits under a
heading about tax status rather than about ownership. Fixing the chart invites a re-think of
the table's grouping, which is more work than the chart change. Also: a slice-by-slice
composition chart with 10+ slices (this report routinely produces that many) is at the edge
of what a pie can carry, so A preserves a mark that is already straining.

**B. A two-sided comparison — assets against liabilities.**
Replace the pie with opposed stacked bars (or a diverging bar): assets stacked upward,
liabilities stacked downward, net as a marker. Or two stacked bars side by side.

*Pros.* Shows the thing the report is actually about — the *relationship* between what you
own and what you owe — in one mark, honestly signed. Scales to many components far better
than a pie. Accessible: length is a more precisely-read visual channel than angle.

*Cons.* Loses the "what is my wealth mostly made of" read at a glance, which is the pie's
one genuine strength and what P5-NET-3 promises. Needs a new chart type;
`AnimatingBarChart` exists but its `OnDataChanged` throws a bare exception on uneven series
(B-47), and asset and liability stacks are *inherently* uneven — so B collides directly
with a known defect.

*Unintended consequences.* The net-worth drill-down (P5-NET-4, P5-VIEW-9) is currently
driven by clicking a *slice*; a stacked bar segment can carry the same click, but the
existing `OnPieSliceClicked` → `SecurityDrillDown`/`CashBalanceDrillDown` path is written
against pie slices and `ChartDataValue.UserData`. Changing the mark means reworking the
navigation, which is the report's most valuable feature.

**C. A waterfall from gross assets to net worth.**
Start at total assets, step down through each class of liability, land on net worth.

*Pros.* The clearest *explanation* of a net-worth figure that exists — it answers "why is
my net worth this number" rather than "what is it made of". Naturally signed, so the
mortgage problem cannot recur. Reads well when printed (D-17).

*Cons.* No waterfall control exists anywhere in `Charts/`; this is a build, not a
configuration. Degrades badly when one liability dominates — a large mortgage against
modest assets produces a chart that is mostly one enormous downward step. And it answers a
different question from the one P5-NET-3 was written for.

*Unintended consequences.* A waterfall implies an *ordering* of liabilities, and any
ordering is editorial (largest first? mortgage before credit cards?). That editorial choice
becomes a thing users read meaning into. It also does not show asset composition at all, so
the report probably ends up with two charts rather than one — more to render, more to
print, more to lay out.

### Panel

- **Architect** — A. It is the only option whose cost is proportional to the defect. B and C
  are chart-library projects wearing a bug fix's clothes. Record C as the aspiration if a
  charting investment is ever made.
- **Developer** — A is roughly: stop passing liability loans into the pie's data list,
  relabel, and attach an `AccountGroup` drill-down to the asset and loan rows that lack one
  (the `AccountGroup` type and the `OnSelectCashGroup` handler already exist, so this is
  wiring, not design). B requires fixing B-47 first — `AnimatingBarChart` throws
  `new Exception(...)` from a property setter on uneven series, which is exactly what an
  asset/liability pair is. C requires a new control.
- **Test Engineer** — A is assertable through the CSV/golden-file route D-18 opens up: a
  fixture with one mortgage, one credit card and one savings account, asserting that the
  chart's data series contains no negative-magnitude entry presented as positive, and that
  the table's total equals assets minus liabilities. That single test is the whole defect.
  For B and C there is no meaningful automated assertion about a picture; they would be
  verified by eye, which the panel should weigh.
- **Operations Engineer** — nothing persisted, no migration, no upgrade risk in any option.
  Opting out beyond that.
- **UI/UX Expert** — my honest preference is **B**, and I want it recorded as a dissent
  rather than smoothed over. A pie chart of 10+ slices is not readable; people cannot
  compare angles, the legend becomes the actual interface, and the chart is decoration. The
  net-worth report is the single most important picture in this product and it deserves a
  mark that works. That said: the *decision in front of the panel* is "does the picture
  include liabilities", and A answers that correctly and cheaply. I will not block A. What I
  will insist on is the labelling — the chart must be titled for what it shows, not for the
  report it sits in — and on the drill-down consistency, because a chart where some slices
  respond and some silently don't is worse than one where none do.
- **Data Engine Expert** — nothing to add here.
- **Adversarial Expert** — the panel is close to treating "a mortgage appears as positive
  wealth" as a design decision when it is a straightforward bug: the code states its
  intention in a comment and then does the opposite one line later. The *decision* is only
  what replaces the pie, and the honest answer is that nobody on this panel knows whether
  the owner wants a composition chart or a balance chart, because that depends on what they
  look at this report for. Recommending A while calling it a confident recommendation would
  be the panel talking itself into a preference. **Accepted** — see the flag.

### Recommendation

**Alternative A — assets-only composition chart, liabilities and net as separate figures**
— as the panel's lean, *not* as a confident recommendation.

Three things the panel treats as settled regardless of which picture wins, because they are
repairs rather than choices:

- A liability must never contribute a positive magnitude to a chart that represents wealth.
  Today's `Math.Abs(balance)` on liability loans is a defect, whatever replaces the pie.
- Every slice or segment in that chart either has a drill-down or none does. Silent dead
  slices — currently the asset and loan rows — are the worst of both. The `AccountGroup`
  machinery to fix this already exists.
- The chart is labelled for what it shows. "Net worth" above a chart that shows assets is
  how this defect survived in the first place.

The genuine disagreement, recorded rather than resolved: the **UI/UX seat prefers B** (a
two-sided asset/liability comparison) and considers the pie the wrong mark for this report
at any slice count this report produces. The Architect and Developer prefer A on cost
grounds; the Adversarial seat's position is that neither preference is grounded in what the
owner actually uses the report for. The panel did not reach consensus and is not pretending
to.

### Needs your decision

**Yes.** The repairs above should happen regardless. But *which picture* the net-worth
report should show — a composition pie of assets (A), a two-sided assets-versus-liabilities
comparison (B), or a waterfall explaining the net figure (C) — is a product-taste question
that turns on what you use this report for. The panel leans A on cost and honesty; it is
not confident it is what you want.

**Panel flags: would benefit from accounting / financial-reporting expertise** — what a
net-worth statement conventionally presents (and whether showing assets and liabilities in
one visual is standard or unusual) is a professional convention the panel is guessing at.

---

## D-22 — As at the start or the end of the chosen day?

**From:** Phase 5 OQ23 (catalog P5-PORT-4, P5-NET-1, P5-TAX-6) · **Register:** D-22

### ELI10

You can ask the program what your investments were worth on any date you pick. If you ask
about 31 December, the report's big heading says "31 December" and the smaller line right
underneath says "As of 30 December". The same page tells you two different things about the
same question.

The question underneath is a real one that the program has never actually answered. When
you say "as of 31 December", do you mean *before anything happened that day* or *after
everything that happened that day*? If you sold shares on the 31st, one answer shows you
still holding them and the other shows the sale. At the end of a tax year that is the
difference between a gain falling in one year or the next — which is exactly when people
ask this question.

The good news, found by reading the code rather than the report: the actual sums are done
one specific way — everything up to and including that day counts. So the big heading is
right and the small line is wrong; someone typed "take a day off" where nothing needed
taking off. What still has to be decided is what the product *says* it means, and making
sure every other place that takes a date — the net-worth statement, the history chart, the
tax year — means the same thing.

### Alternatives

**A. Close-of-day, inclusive, everywhere — and say so.**
"As at the close of 31 December 2025" includes everything dated that day. One shared date-
range value type is used by every date-scoped query, implemented as a half-open interval
`[start, date + 1 day)` so the intent is in the type rather than in each comparison.
Every surface that takes a date states the convention in its heading.

*Pros.* It is what the code already does, so **nobody's numbers change** — the single most
valuable property any answer here can have. It matches how brokerage and bank statements
talk ("closing balance"), which is the language the user already has for this. A shared
range type makes the convention enforceable rather than remembered, and removes the class
of `>` / `>=` bug entirely.

*Cons.* "Everything up to and including" is subtly wrong if transactions ever carry a time
of day — today they are date-only, so the `t.Date > toDate` comparisons happen to work, but
that is a latent dependency nothing states. A half-open range type fixes that properly and
is slightly more work than fixing the comparisons.

*Unintended consequences.* Standardising the convention forces an audit of places that
already store an *end* date, and some may have been written assuming exclusivity — the
fiscal-year range (`Transactions.GetTaxYearRange`, `DatabaseSettings.FiscalYearStart`) is
the one to check, because if a fiscal year end is stored as "1 January" meaning "up to but
not including", making ranges inclusive double-counts the boundary day. That is a one-off
audit, but it is the kind that is skipped and then bites at a tax-year boundary.

**B. Start-of-day, exclusive — the ledger convention.**
"As at 1 January" means the position brought forward, before that day's activity. "As of 31
December" excludes the 31st.

*Pros.* Matches the accounting notion of an opening balance; makes "1 January" and "31
December close" the same instant, which some users find natural. Arguably makes the current
sub-heading correct rather than wrong.

*Cons.* Changes every existing user's numbers, silently, for any report struck on a date
with activity on it. There is no upside proportionate to that.

*Unintended consequences.* Silent numerical change on upgrade is the single worst failure
mode for a finance product — the user cannot tell a convention change from a data
corruption, and they will not read the release note before they panic. It would also make
the *saved* report state (P5-VIEW-11 persists report dates) mean something different than
it did when it was saved, with no version marker to detect that.

**C. Never show a bare date — always show an explicit qualifier or a range.**
Whatever the convention, the UI never presents a naked date. Headings read "Close of 31
December 2025" or "Period: 1 Jan – 31 Dec 2025"; the date picker's label says what it means;
the trailer restates it.

*Pros.* Removes the ambiguity from the user's experience regardless of the internal answer —
which is the part they actually suffer from. Cheap: it is wording.

*Cons.* Decides nothing on its own. Without A or B underneath, the wording is documenting an
inconsistency rather than a convention.

*Unintended consequences.* None significant — this is additive. Worth noting it also makes
exported and printed reports self-describing (D-17), which matters more than on screen
because a printed page outlives its context.

### Panel

- **Architect** — A plus C, and the structural half is the shared range type. The reason
  this decision exists is that "a date scope" is expressed as a bare `DateTime` compared
  with `>` in five different files. A value type — `DateRange` / `AsAtDate` with an explicit
  inclusive-end semantic — makes the convention a property of the type rather than a
  convention people have to remember, and it is the same object D-16's scope and D-24's span
  want. Three decisions, one type.
- **Developer** — A is almost free given the finding: delete `.AddDays(-1)` from the
  sub-heading and the report is self-consistent. The range type is the real work and it is
  worth doing because it is also D-16's and D-24's. Concretely the comparisons to migrate
  are `GetTransactionsGroupedBySecurity` (`t.Date > toDate`), `GetCashBalanceNormalized`
  (`t.Date > date`), `CostBasisCalculator.ApplySplits` (`next.Date.Date < dateTime.Date`),
  and the trend/history chart bucket boundaries.
- **Test Engineer** — this is the highest-value characterization target in the whole
  cluster, and it is cheap: one fixture with a trade and a deposit dated exactly on the
  boundary, asserted across the portfolio report, the net-worth report, the account summary,
  the tax-year range and the history chart. Five assertions, one fixture, and they lock the
  convention in before any refactor touches it. This is exactly the pattern `CLAUDE.md`
  prescribes — characterize what the code really does (which the panel now knows) rather
  than assume.
- **Operations Engineer** — A, purely because it changes nothing. B is a silent numerical
  change to historical reports on upgrade, and for a product whose users are mid-migration
  from Quicken and are actively comparing numbers against the old tool, that is the one
  category of change that destroys trust irrecoverably. If B were ever chosen it would need
  to be announced, versioned in the saved report state, and ideally offered as a setting —
  which is three pieces of machinery to support a preference nobody asked for.
- **UI/UX Expert** — C is the part the user experiences, and it should not be treated as a
  footnote to A. "As of 31 December" is ambiguous in English and always has been; the fix is
  words, and the words should appear on the date picker itself ("Value as at close of"), not
  only in the heading. Also, the portfolio report's heading and sub-heading should simply be
  one line — two lines about the same date is how the contradiction became possible.
- **Data Engine Expert** — engaging. Two points. First, the half-open interval form
  (`>= start && < end.AddDays(1)`) is also the form that lets a date predicate use an index
  if these queries ever move into SQL; `DATE(x) = y` style comparisons do not. Since D-35 is
  considering what each engine must do, standardising on half-open ranges now costs nothing
  and keeps the SQL door open. Second: transaction dates are stored as dates, but if the
  redesign ever stores a timestamp (for audit or sync ordering, which D-34's concurrency
  work might want), an inclusive-end comparison against a date silently starts excluding
  same-day transactions with a nonzero time. The range type must be explicit about
  truncation, not rely on values happening to be midnight.
- **Adversarial Expert** — agreed on A, one caution: the panel is reasoning from the *code's*
  current behaviour to the *right* answer, which is the "characterisation testing" habit
  doing its job — but it is worth stating that "the code does X" is not an argument that X
  is correct, only that changing it is expensive. If the owner's accountant says a portfolio
  "as of" date conventionally excludes the day, B is right and the migration cost is the
  price. The panel's confidence rests on nobody having a reason to prefer B, not on A being
  demonstrably more correct.

### Recommendation

**Alternative A with Alternative C — close-of-day inclusive everywhere, stated in words at
every point a date is shown, and enforced by a single shared date-range type** rather than
by repeated comparison operators.

The argument that won: the product already behaves this way, consistently, in every
computation the panel inspected. The only thing that disagrees is one sub-heading string.
Choosing the convention the code already implements means zero users see their numbers
change, and the entire decision collapses to (a) deleting one `.AddDays(-1)`, (b) writing
the convention into the headings and the date picker's label, and (c) introducing the range
type that D-16 and D-24 want anyway.

The Test Engineer's boundary fixture should land **before** the range-type refactor, so the
refactor is verified against the behaviour the panel just characterised rather than against
the behaviour someone assumes.

One audit item the panel flags as part of implementing this: confirm that
`Transactions.GetTaxYearRange` and the fiscal-year handling do not already assume an
exclusive end date. If they do, making everything inclusive without checking double-counts a
boundary day in exactly the report — the tax report — where it matters most.

### Needs your decision

**No.** The panel is confident, on the strength of "this is what the code already does and
nobody has a reason to prefer otherwise".

**Panel flags: would benefit from accounting expertise** — whether "as of <date>" on a
portfolio statement conventionally includes that day's activity is a professional norm. The
stakes are low, because the existing behaviour wins on migration cost regardless, but if the
convention is genuinely the other way the product should at least *say* it is doing
something non-standard.

---

## D-23 — How are report options hosted, and should a report remember them?

**From:** Phase 5 OQ29 (catalog P5-VIEW-2, P5-VIEW-11, P3-PANEL-1) · **Register:** D-23 ·
**Folds into:** D-8, B-31

### ELI10

When a report is on screen, controls appear in the panel down the left-hand side so you can
change the date, the year, the currency, whether to group by month or year, and so on. The
report redraws as you change them. That part works and is genuinely nice.

The way it is built is odd in a way that has consequences. Asking the program for that panel
is what *creates* it — there is no separate "show the panel" step, so the act of looking
something up makes a visible thing appear. And the panel is thrown away and rebuilt from
scratch every single time a report is drawn. That's why every report has instructions for
*hiding* the controls it doesn't want but none for showing them: each one hides what doesn't
apply and relies on the next report getting a brand-new panel. It works. It also means every
report has to re-connect all its buttons every time it draws, and the code that does that
appears — word for word, including an odd double-disconnect — in four different reports,
because somebody copied it and nobody could safely simplify it.

There's a related gap. When you close the program and come back, you don't come back to the
report you were reading. The settings are remembered; which report you were on isn't.

So: how should a report ask for its options, and should the program come back to where you
were?

### Alternatives

**A. Reports declare their parameters as data; one long-lived host renders them.**
A report exposes a parameter set — name, type, default, allowed values, display label — and
never touches a panel, never subscribes to an event, never hides a row. A single persistent
options host renders the parameters using WPF-UI controls bound to a view model, and hands
back "regenerate with these values". Getting the host has no side effect; showing it is a
separate, explicit act.

*Pros.* Kills the `GetService`-has-a-side-effect pattern, the `Hide…`-without-`Show…`
asymmetry and the quadruplicated `Unregister(); Unregister(); Register();` in one move.
Because parameters are *data*, they serialise trivially — which is the cheapest credible
route to B-31 and D-8's "come back where you were": the view state becomes "report type +
parameter values", and `PortfolioReport`'s currently-unserialisable `Predicate<Account>`
state becomes a named group identifier instead. Makes a report generatable headlessly from
a parameter dictionary, with no WPF at all.

*Cons.* A declarative schema strains against genuinely bespoke input. The retirement plan
takes roughly fifteen inputs including social-security amounts recorded per claiming age
(P5-PLAN-8) — expressing that as generic parameters is either ugly or impossible, so an
escape hatch is needed, and escape hatches erode schemas.

*Unintended consequences.* Once parameters are data, they become addressable — and the
obvious next requests follow: saved report configurations, a report opened from a link, a
command line. That is a *good* trajectory but it is a trajectory; the panel should
anticipate that "parameters as data" is the first step of a larger feature, not a
refactoring that ends where it starts. Also, headless report generation changes the testing
economics enough that the test suite will grow, which is work even when the work is welcome.

**B. Each report owns and supplies its own options view.**
The report hands the shell a small control plus its view model; the shell hosts whatever it
is given. No shared vocabulary, full per-report freedom.

*Pros.* Handles the retirement plan honestly, because that panel simply *is* a bespoke form.
Simple contract, no schema to design. Each report's options live next to the report's own
code, which is where an author looks.

*Cons.* Nothing shared means nothing stays consistent: two reports will have differently-
styled date pickers within a year, which is precisely the drift this audit documented across
dialogs (D-15) and help topics (B-48). Restoring state goes back to being each report's own
problem, which is how B-31 happened.

*Unintended consequences.* Per-report views make the *shell* simpler and the *product*
harder to change globally — a future restyle, a keyboard-navigation pass or an accessibility
audit then has to visit nine places instead of one. For a redesign whose entire premise is
moving to a consistent Fluent language, that is the wrong direction of travel, even though
it is locally the easiest option.

**C. No side panel — parameters live in a header strip above the report.**
Date, currency, grouping and the export/print commands sit in a toolbar immediately above
the document, as modern web reporting tools do. The left panel returns to being purely
navigational.

*Pros.* The controls sit next to what they change, which is a real discoverability gain —
today a user reading a report has to look at the opposite side of the window to change it,
and the export button is over there too (D-18). Frees the navigation panel. Reads as modern.

*Cons.* Horizontal space is finite: five or six parameters plus a search box plus command
buttons will wrap or overflow at normal window widths, and the retirement plan's fifteen
inputs cannot live in a strip at all. It also collides with the existing search box and the
chart strip.

*Unintended consequences.* Moving parameters into the report's own header makes the report
view taller and the document shorter, which matters most on the reports users scroll
furthest — and it interacts with printing (D-17), because a header strip is chrome that must
*not* print, requiring the print path to distinguish document from chrome. Today that
distinction does not exist because all the chrome is elsewhere.

### Panel

- **Architect** — A, and the decisive property is not tidiness, it is that A makes a report's
  configuration a **value**. Everything the register complains about downstream — state not
  restored (B-31), unserialisable predicates, view state that is `[XmlIgnore]` — is a
  symptom of configuration living as mutable control state scattered across a panel and four
  event subscriptions. Turn it into a value and D-8 becomes a shell-level guarantee instead
  of nine per-surface implementations. A should be scoped as host-agnostic so C becomes a
  later re-skin rather than a rewrite.
- **Developer** — A is the most work of the three up front and the least work afterwards. In
  WPF-UI terms the host is a `ItemsControl` over parameter view models with a `DataTemplate`
  per parameter type (date, enum-choice, currency, bool, number) — a well-trodden pattern,
  maybe five templates to cover every report but the retirement plan. The retirement plan
  takes B's escape hatch and the panel should say so explicitly rather than pretend the
  schema covers it.
- **Test Engineer** — A, strongly, and for a reason beyond this decision: a report that can
  be generated from a parameter dictionary with no `ServiceProvider` and no panel is a
  report that can be tested in `UnitTests` against the CSV writer (D-18) with a fixture
  money file. That turns nine reports from "verified by looking at them" into nine
  golden-file tests. Today `OnSiteChanged` assigning `this.panel` is on the critical path of
  generating a report at all — which is also why B-45's inline date picker is unreachable.
  Breaking that dependency is the single highest-leverage testability change in this cluster.
- **Operations Engineer** — one concern that applies to A and B alike: if parameters become
  the persisted view state, the reader must tolerate unknown and missing parameters, because
  a user will downgrade, or open a file whose saved state came from a newer build. The
  current code is unintentionally robust here only because it swallows every exception
  (`LoadState`/`SaveState` catch everything); a structured parameter model should be
  *deliberately* tolerant — unknown keys ignored, missing keys defaulted — rather than
  inheriting a blanket catch.
- **UI/UX Expert** — C is where this should end up and A is how to get there. Options beside
  the document rather than across the window is the right long-term arrangement, but it
  cannot carry the retirement plan and it competes with the search box today. Building A
  host-agnostically means the move to C later is a template change. Two things to fix
  whatever happens: the panel must show which report it belongs to (today it is an anonymous
  set of controls), and the F1 topic must stop being the reconciliation page (B-48).
- **Data Engine Expert** — nothing to add; report state is a small XML file beside the money
  file and stays that way under all three options.
- **Adversarial Expert** — the hard question: is any of this visible to the user? The panel
  works today. A user cannot see `GetService` having a side effect, and "the panel is rebuilt
  each time" costs nothing they perceive. The only *user-visible* item in this entry is "the
  app doesn't reopen on my report", which is D-8's problem, not this one. So the honest
  framing is that D-23 is an internal-quality decision that buys a user-visible feature
  (restore-where-you-were) as a side effect — and if the owner does not care about that
  feature, the correct answer might be "leave it alone, it works". **Partly accepted:** the
  recommendation is explicit that the justification is (i) testability and (ii) D-8, not
  tidiness, and that if neither is wanted, this decision can be deferred without harm.

### Recommendation

**Alternative A — reports declare their parameters as data, rendered by one long-lived,
host-agnostic options surface** — with **Alternative B's escape hatch for exactly one
report**, the retirement plan, named as such rather than left as a general loophole. Build
the host so that moving to **Alternative C** (a header strip above the document) later is a
template swap, not a redesign.

The reasoning that won was the Test Engineer's and the Architect's, jointly: turning report
configuration into a value simultaneously (a) lets reports be generated and asserted without
any WPF, which is what makes D-18's nine golden-file tests possible, and (b) makes "come
back to the report I was reading" fall out of the same object, which is the only user-visible
thing in this decision and is currently blocked by state that cannot be serialised at all.

The panel accepts the Adversarial framing and states the priority honestly: **this decision
earns its place through D-8 and through testability, not through internal elegance.** If
restore-where-you-were is not wanted and the report suite is not going to be test-covered,
D-23 can be deferred indefinitely with no user-visible cost.

Not re-litigated, because they are already bugs: B-31 (view state that cannot round-trip),
B-45 (unreachable inline date picker), B-48 (wrong help topic on the options panel).

### Needs your decision

**No.** The panel is confident in the recommendation *given* that D-8 resolves toward
"restore-where-you-were is a product guarantee". If D-8 goes the other way, the case for
D-23 rests on testability alone, which the panel still considers sufficient but no longer
urgent.

---

## D-24 — One vocabulary for "a period"

**From:** Phase 5 OQ32 (catalog P5-FLOW-2, P5-HIST-1, P5-TREND-6, P4-REPORT-2) ·
**Register:** D-24

### ELI10

The program has four different lists of things like "daily, weekly, monthly, yearly". They
look like four versions of the same list, and a redesign is tempted to merge them into one.
They are not four versions of the same list, and merging them would lose something.

Read what each one is actually *for*:

- One is "how often does this happen" — you tell the program a bill repeats monthly, and it
  uses that to predict future bills. That's about the world, not about a picture.
- One is "how wide should each bar be" — a chart of your spending by month versus by year.
  That's about how the data is grouped up before it's drawn.
- One is "how far do I jump when I press Next" — the trend graph steps forward a year, or a
  month, at a time. The trend graph borrows the *first* list for this, which is why it has
  odd entries like "never" that make no sense as a step.
- And underneath all of them is "which stretch of dates am I looking at" — a start and an
  end — which is a different kind of thing entirely.

On top of that, the list that looks most like the official one — the one sitting in the
shared report interface — is used by nothing at all. Its only reader is that pop-up window
from D-16, and the one thing that opens that pop-up switches the list off. It exists in name
only.

So the decision isn't "merge four lists". It's "notice that these are three different ideas
and name each one properly".

### Alternatives

**A. Three named types, one per concept.**
- **Recurrence** — `CalendarRange` stays exactly as it is, in the domain, persisted
  unchanged. It is `Category.Frequency` and the recurring-payment detector's vocabulary, and
  its `None`/`Never` members are meaningful *there*.
- **Granularity** — one new enum replaces `ReportInterval`, `HistoryRange` and the cash-flow
  report's two hard-coded strings. Members: Day, Week, Month, Quarter, Year.
- **Span** — a `DateRange` value type with named presets (year to date, last 12 months, this
  fiscal year, all, custom), which is the same type D-16's scope carries and D-22's
  convention lives in.
- **Step** is not a type: stepping means "move by one unit of the current granularity", so
  the trend graph stops borrowing `CalendarRange` and steps by `Granularity`.

*Pros.* Each type means one thing, so nothing is lost. Nothing persisted changes —
`CalendarRange`'s stored integer values are untouched, which removes the only migration risk
in the whole decision. Deletes the stringly-typed cash-flow comparison and the dead
`ReportInterval`. `Span` is shared with D-16, D-22 and D-23, so it pays for itself.

*Cons.* Three types where a newcomer sees one idea, so the naming and the doc comments have
to carry the distinction. `Granularity` will be under pressure to grow (fortnightly?
fiscal quarters?) and every addition makes it look more like `CalendarRange` again, which
will periodically re-tempt somebody to merge them.

*Unintended consequences.* `HistoryRange` is part of persisted chart/view state; replacing it
changes what is written, so the reader needs to tolerate the old values — a small, real
compatibility task that "it's just an enum rename" hides. Also, giving `Granularity` a Week
and a Quarter member that the current charts do not offer creates values the bucketing code
must actually handle, or it throws on data it has never seen — the same shape as B-47.

**B. One unified `Period` type: a unit plus a count.**
`Period(3, Months)` expresses quarterly, `Period(2, Weeks)` expresses fortnightly, and the
same type serves recurrence, granularity and step. Span stays a separate date range.

*Pros.* Genuinely elegant and strictly more expressive than any of the enums — it can
represent "every 10 weeks", which nothing currently can. One concept to learn. Arithmetic
("advance by one period") is a method on the type instead of a `switch` with ten cases, and
`TrendGraph.Step`'s ten-case switch disappears.

*Cons.* `CalendarRange.None` and `CalendarRange.Never` are sentinels, not periods — a
count-and-unit type cannot express "never", so you need a nullable wrapper or a special
value, and the special cases you deleted come back wearing a different hat. And
`BiAnnually` versus `SemiAnnually` in the existing enum are a pre-existing ambiguity that a
numeric type would force somebody to resolve, changing at least one existing user's stored
meaning.

*Unintended consequences.* The decisive one is storage. `Category.Frequency` persists
`CalendarRange` as an integer (0–11). Replacing it with a structured period is a **stored-
value migration on a domain field**, for a benefit that is entirely internal elegance. Every
existing file needs converting, the conversion needs to be reversible for downgrade, and it
lands in the same release as whatever else the schema work is doing. That is a
disproportionate risk for a naming improvement.

**C. Leave the domain alone; unify only the presentation layer.**
Introduce a presentation-level `TimeScaleOption` (display name + bucketing function). Each
surface offers the options that make sense for it; the underlying enums keep their current
jobs and nothing in the domain moves.

*Pros.* Cheapest by far. Zero data risk. Fixes the *user-visible* half — that "by month" in
one place and "Month" in another and "Monthly" in a third should look and read the same —
without touching anything persisted.

*Cons.* Leaves four definitions in the model, so the next audit finds exactly this entry
again. The trend graph keeps stepping by a recurrence enum, which is the actual conceptual
error, just hidden behind a nicer label.

*Unintended consequences.* A presentation type layered over unreconciled domain types tends
to accumulate mapping code in both directions, and mapping code is where the off-by-one and
wrong-enum bugs live. C is cheap now and quietly taxes every future change to any of the
four.

### Panel

- **Architect** — A. The register frames this as "four definitions to reconcile"; the panel's
  reading of the source says it is three concepts with one being borrowed for a fourth job,
  and that reframing *is* the decision. `CalendarRange` lives in `Money.cs` — it is domain
  vocabulary about the world (how often a bill recurs), and a charting concern reached into
  the domain to borrow it. Fixing that direction of dependency is the valuable part; merging
  the enums is the part that looks valuable and isn't.
- **Developer** — A, and the `Span` type is the piece that earns the work. Today "which
  stretch of dates" is expressed as loose `DateTime` pairs plus two booleans
  (`yearToDate`, `showAll`) on the trend graph, and separately as start/end rows in the
  reports panel, and separately again as the report date on the point-in-time reports. One
  value type with named presets collapses all of that and is the object D-16 scopes with,
  D-22 defines the convention on, and D-23 serialises. Four decisions, one type — that is
  the argument.
- **Test Engineer** — A is testable per type: bucket boundaries against `Granularity`,
  preset resolution against `Span` (does "this fiscal year" honour a non-January fiscal
  start?), recurrence detection against `CalendarRange` unchanged. B is testable too but its
  migration is not — you cannot easily test that every historical `Category.Frequency` value
  in a real Quicken-imported file converts correctly, because the panel does not have those
  files. That asymmetry matters more than the elegance difference.
- **Operations Engineer** — A, on the single ground that it touches no stored value. B
  requires migrating a persisted domain field, which for this fork — mid-migration, real
  irreplaceable data, SQLite by default — is a risk with no user-facing payoff. If B is ever
  wanted, it should ride along with a schema change that is happening anyway (D-39's
  territory), never on its own.
- **UI/UX Expert** — the user-visible symptom is inconsistent wording, and all three options
  fix that, so I am not the deciding vote. What I want from whichever wins: the same word in
  the same place everywhere — "Month", not "By Month" here and "Monthly" there — and the
  granularity control rendered identically in the reports panel and on the chart. And the
  presets matter more than the enum: "Year to date", "Last 12 months", "This fiscal year" is
  what people actually pick, and today only the trend graph has anything like them.
- **Data Engine Expert** — engaging, and this is the decisive practical input. Under A
  nothing persisted changes: `CalendarRange`'s integers stay, `Category.Frequency`'s column
  stays. Under B, a domain enum's stored representation changes, which means a migration
  across SQLite, SQL Server, and the XML/CSV stores — and per D-35 several of those cannot
  even do a per-record save, so "migrate every category's frequency" has a different shape on
  each engine. That alone should decide it. The one thing A does touch is `HistoryRange` in
  persisted view state, which is not domain data and can simply be read tolerantly.
- **Adversarial Expert** — no real objection, one reframing: the panel should be clear that
  D-24 is *not* a user-facing decision at all. Nobody has ever complained that the program
  has four period enums. Its value is entirely that D-16, D-22 and D-23 all want the same
  `Span` type, and D-24 is where that type gets named. If those three decisions are deferred,
  D-24 should be deferred with them rather than done on its own — a refactor with no consumer
  is just churn.

### Recommendation

**Alternative A — three named types, explicitly refusing to merge recurrence with
granularity.**

Concretely:

| Concept | Type | What happens to it |
|---|---|---|
| Recurrence — *how often does this happen* | `CalendarRange` | Unchanged, stays in the domain, stored values untouched |
| Granularity — *how wide is a bucket* | one new `Granularity` enum | Replaces `ReportInterval`, `HistoryRange` and the cash-flow report's two hard-coded strings |
| Span — *which stretch of dates* | `DateRange` value type with named presets | New; shared with D-16's scope, D-22's convention and D-23's parameters |
| Step — *how far does Next move* | not a type | Derived: one unit of the current `Granularity`. `TrendGraph` stops borrowing `CalendarRange` |

The reasoning that won: the register asks how to reconcile four definitions, and the answer
is that two of them should never be reconciled. `CalendarRange` answers a question about the
user's life (this bill arrives monthly); `Granularity` answers a question about a picture
(draw me one bar per month). They coincide in their member names and diverge in everything
else, which is exactly the kind of resemblance that produces a bad merge. The Data Engine
seat's point closed it: the merge that looks most elegant (B) is the only one that requires
migrating a persisted domain field, across engines that do not all support per-record
writes.

The panel endorses the Adversarial Expert's sequencing note: **D-24 should be implemented
alongside D-16/D-22/D-23, not before them.** The `Span` type is the shared deliverable; on
its own, this entry is a rename with no consumer.

### Needs your decision

**No.** The panel is confident. No stored data changes, no user-visible behaviour changes,
and the one compatibility task (`HistoryRange` in saved view state) is small and known.

---

## Summary of flags

**Needs your decision**

- **D-21** — which picture the net-worth report should show. The panel leans assets-only
  composition, but genuinely does not know what you use that report for, and the UI/UX seat
  dissents in favour of a two-sided comparison.
- **D-17 (partly)** — whether you actually hand reports to anyone. If yes, build printing
  and PDF; if no, delete HTML export and let CSV plus `.txf` carry it.
- **D-16 (partly)** — whether category filtering of reports and charts is a capability you
  want at all, as distinct from drilling in from a chart slice.
- **D-19 (conditionally)** — not the shape, but the sequencing: step 3 is blocked on D-4.

**Expert opt-outs**

- **Data Engine Expert** opted out entirely on **D-17**, **D-18**, **D-21** and **D-23** —
  no persistence involvement in any alternative. Engaged on D-19 (rate vintage and storage),
  D-20 (the `Category.Color` column's changing semantics, and B's full-table backfill), D-22
  (half-open ranges and index-ability) and D-24 (the decisive migration argument), and gave a
  one-line narrow note on D-16.
- **Operations Engineer** opted out on **D-21** beyond confirming no migration risk.

**Panel flags — additional expertise that would help**

- **Accounting / financial-reporting expertise** — **D-19** (how a multi-currency net
  position is conventionally presented, and whether a converted total is acceptable on a
  statement), **D-21** (what a net-worth statement conventionally shows), **D-22** (whether
  "as of <date>" conventionally includes that day's activity).
- **The recipient's own view** — **D-17**. An accountant or tax preparer saying what format
  they want to receive would settle the whole decision in one sentence.
- **Accessibility expertise** — **D-20**. A categorical palette that stays distinguishable
  under common colour-vision deficiencies, in both light and dark themes, is specialist work;
  the UI/UX seat can state the requirement but not verify the result.
