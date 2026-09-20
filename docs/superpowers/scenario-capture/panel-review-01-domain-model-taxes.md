# Panel Review 01 — `domain-model` and `taxes`

One expert-panel session over eight entries from the
[Redesign Decision Register](redesign-decision-register.md):

| | |
|---|---|
| `domain-model` | **D-1** envelope budgeting · **D-2** view state on domain objects · **D-3** budgeting grain · **D-4** the currency model |
| `taxes` | **D-28** sales with no known cost basis · **D-29** tax-year vintage · **D-30** filing-status approximations · **D-31** characterization tests that ratify defects |

The panel: **Architect**, **Developer** (C#/.NET/WPF, WPF-UI/Fluent), **Test Engineer**,
**Operations Engineer**, **UI/UX Expert**, **Data Engine Expert** (SQLite/SQL Server),
**Adversarial Expert**.

Each section gives a plain-language explanation of what is actually at stake, two or three
genuinely different design directions with their pros, cons and second-order effects, the
panel's recommendation, and an honest flag for whether the call is the panel's to make or
the product owner's.

Before opining the panel re-read the referenced catalog sections *and* the code behind
them. Several findings below are new — the register and catalog describe the symptom, and
reading the source turned up a sharper or larger version of it. Those are marked
**Panel finding** so they can be traced back.

---

## D-1 — Revive category funding, or leave envelope budgeting out of the model?

**Register:** D-1 · **From:** Phase 1 OQ1 · **Cluster:** `domain-model`

### ELI10

Imagine you get your pocket money and, before you spend any of it, you put some in a jar
labelled "sweets", some in a jar labelled "presents", and some in a jar labelled "saving
up for a bike". When you want sweets, you look in the sweets jar. If it's empty, you can't
have sweets — even though there's money in the bike jar. That's called *envelope*
budgeting, and lots of people run their money that way because it's the only way they can
actually stop themselves overspending.

This program doesn't do that. What it does instead is: at the end of the month it looks at
everything you spent, sorts it into piles, and tells you "you meant to spend £40 on sweets
and you actually spent £62." That's useful, but it's a *school report* — it tells you off
afterwards. The jars stop you beforehand.

Deep inside the program there's a leftover clue that somebody once meant to build the
jars: a hidden kind of account called a "category fund", described in the code as "a
pseudo account for managing category budgets." It doesn't do anything. It's a signpost to
a road that was never built.

So the question is: does the new version build the jars, and if so, are the jars real
places money sits, or just labels on a piece of paper next to your real accounts?

**Panel finding.** The clue is bigger than "an unused enum value". The old mobile port of
this app (`Source/Xamarin`) still carries an `IsCategoryFund` property and a display name
for it, and the current WPF app has **three separate places that explicitly exclude this
account type** — the account-creation dropdown, the CSV export of account balances, and
the account summary report. Something once created these; the current app is carefully
filtering out accounts it expects might exist in an old file. That is a deliberately
abandoned feature, not a typo.

### Alternatives

#### A. Leave it out. Budgeting stays a comparison of planned against actual

Delete the concept. Keep the numeric hole in the stored account-type values so old files
still read correctly, but remove the three exclusion guards and the mobile port's
property. Budgeting means "a target per category per period, compared to what happened."

- **Pros.** Zero new persisted concepts. The redesign's domain model gets smaller, not
  larger. No new mental model for the user to learn. It is also the honest reflection of
  what the product does today, which means no migration story and no half-built feature.
- **Cons.** Gives up on the one budgeting style that changes behaviour rather than
  reporting on it. For a fork whose whole premise is replacing Quicken, this is a feature
  Quicken users may be arriving with muscle memory for.
- **Unintended consequences.** Removing the three exclusion guards is not free: an old
  file that *does* contain a type-9 account would, after the guards go, suddenly start
  showing a strange account in the account list, the CSV export and the net-worth summary.
  The guards must be replaced by a load-time migration that either converts or hides such
  accounts, not simply deleted. It also quietly forecloses "sinking funds" / savings goals,
  which are the same machinery wearing a friendlier name and are much more commonly wanted
  than strict envelopes.

#### B. Build envelopes as a *virtual allocation ledger*, not as accounts

Envelopes are a separate, lightweight record: `(category, period, allocated amount, source
account)`. They never hold money. Real account balances are untouched. The app derives
"remaining in this envelope" as `allocated − actual spend in that category this period`.

- **Pros.** The domain model gains one small, self-contained concept instead of mutating
  the account model. Net worth, reconciliation, transfers and every report stay exactly as
  they are, because no real money moved. Easy to switch off — an envelope with no
  allocations is invisible. Testable in isolation with no UI.
- **Cons.** Purists will note it is not "real" envelope budgeting: you cannot see the money
  physically sitting in the jar in your account list. Rollover rules (does last month's
  leftover carry forward?) have to be designed and are the part people argue about.
- **Unintended consequences.** Because allocations are derived rather than transacted,
  there is no audit trail of *when* you moved £20 from the sweets envelope to the presents
  envelope unless the design adds one deliberately — and users of envelope systems very
  much expect that trail. Also, "spend in that category this period" has to agree with what
  the category reports say, which drags in the fiscal-year setting, the tax-date override,
  transfers and split lines; every disagreement becomes an envelope bug rather than a
  report bug.

#### C. Resurrect it literally — funds are real pseudo-accounts, funding is a transfer

What the original design appears to have intended: a category fund is an account, and
putting money in the sweets envelope is a transfer from your current account into the
sweets fund account.

- **Pros.** It reuses machinery that already exists and is well tested — transfers,
  balances, the register. The audit trail comes free, because every funding move is a
  transaction. Conceptually crisp: everything is an account, everything is a transfer.
- **Cons.** Every account-shaped surface in the app now has to know these aren't real
  accounts: net worth would double-count, the account list gets cluttered, reconciliation
  is meaningless for them, currency handling is undefined, and the tax report would have
  to exclude them. That is exactly the set of exclusion guards the current code already
  carries — three of them, hand-written, in three unrelated files. That pattern only grows.
- **Unintended consequences.** The transfers are real rows in the transactions table, so
  every query that says "show me all my spending" now needs to filter them out, and the
  duplicate-detection, import and merge paths all inherit a new category of thing they can
  get wrong. It also permanently couples envelope budgeting to the transfer machinery, so
  a future change to transfers (already a delicate area — see the register's transfer
  entries) has envelope budgeting hanging off it.

### Panel discussion

**Architect.** C is how you get thirty exclusion guards. The existing three are the
prototype of that failure mode and they are already spread across unrelated files. If
envelopes ship, they ship as their own concept (B), not as a special case of account.

**Developer.** B is a few hundred lines and one new stored record. C is a refactor of
everything that iterates accounts. A is free. The cost ordering here is stark enough that
it should dominate unless envelopes are a headline feature.

**UI/UX Expert.** Worth naming the middle case the register doesn't: most people asking for
"envelopes" actually want **savings goals / sinking funds** — "I'm putting £50 a month
aside for the car service." That is B with a friendlier name and no strictness, and it
lands far better in a modern Fluent-style app (a progress bar per goal) than a rigid
envelope system does. Strict envelopes are a lifestyle; goals are a feature.

**Operations Engineer.** Whatever is chosen, the migration for an old file containing a
type-9 account must be written deliberately. Today it is handled by three accidental
guards; if those go away without a replacement, the failure is silent and visible in the
net-worth figure, which is the worst place for a silent failure.

**Data Engine Expert.** The numeric hole must stay reserved forever regardless of the
decision — the values above it (loan, credit line, education) are positional. On B, the
allocation record is a small, append-friendly table with a natural composite key; it
introduces no concurrency hazard and no schema risk. On C, the pseudo-accounts live in the
accounts table and every `WHERE` clause in the product becomes potentially wrong by
omission. From a storage point of view B is uneventful and C is a long tail of query bugs.

**Test Engineer.** B is testable headlessly: allocations in, remaining out, no UI. C needs
integration tests across transfers, net worth and every report, and its failure mode is
"a number is slightly wrong somewhere", which is the hardest kind of regression to catch.

**Adversarial Expert.** The honest question nobody has asked: does *this* user want
envelopes? This is a one-person fork migrating a Quicken history. Quicken Classic doesn't
do envelope budgeting either, so nobody is arriving with that muscle memory, and the
register itself notes the *entire* existing budgeting feature is unreachable from the UI
(see D-3) without anyone apparently missing it. Building B on spec would be building a
feature to fill a gap in an enum. The panel should say A out loud and let the owner
override it, not hedge toward B because B is architecturally tidier.

### Recommendation

**Alternative A, with the migration written properly — and C ruled out regardless.**

Two conclusions the panel is unanimous on and confident about:

1. **C is wrong** whatever the product decides. Modelling budget envelopes as accounts
   means every account-shaped query in the app needs an exclusion clause; the codebase has
   already started down that road and has three hand-written guards to show for it.
2. **The exclusion guards must not simply be deleted.** Replace them with an explicit
   load-time migration for the account type, so that removing the feature is a deliberate,
   tested act rather than three edits in three files.

On whether envelopes ship at all, the panel splits and defers. The Adversarial Expert's
point carried the room: there is no evidence of demand, and D-3 shows the budgeting
feature that *does* exist has been dead in the UI without anyone noticing. If the owner
does want jar-style budgeting, **Alternative B** is the shape — and the UI/UX Expert's
reframing to *savings goals* is probably the version that actually gets used.

### Needs your decision?

**Yes — for the product question only.** "Does the redesign offer envelope budgeting or
savings goals?" is pure product taste and the panel is guessing. The two technical
conclusions above (never as accounts; migrate the type deliberately) stand regardless and
need no decision.

---

## D-2 — Should domain objects carry view state at all?

**Register:** D-2 · **From:** Phase 1 OQ4 · **Cluster:** `domain-model`

### ELI10

Think of the program's information as a big filing cabinet full of index cards — one card
per payment, one per category, one per share you own. The cards are what gets saved to
disk. They're the real, permanent record.

Now, when you're looking at those cards on screen, the screen needs to remember some
temporary things: which card you're halfway through renaming, which folder you've clicked
open, which card you're dragging a file onto right now. None of that is part of your
financial history. It's just what the screen is doing this second.

In this program, those temporary screen notes are **written onto the index cards
themselves**. There's a "somebody is renaming me right now" mark on the category card, and
an "I'm open" mark on the share card.

That sounds harmless, and mostly it is. But here's what it actually causes:

**Panel finding — clicking a triangle makes the program think you have unsaved work.**
When you expand a share to see the details underneath, the program writes "I'm open" onto
the card, and — because it writes it through the *same door* it uses for genuine edits —
the card gets stamped "changed". The program then believes your file has unsaved changes.
Close it and it asks "do you want to save?" You didn't change anything. You clicked a
triangle. The same goes for starting to rename a category and pressing Escape.

Worse, the program's change-tracker gets confused even by the marks that were *supposed*
to be harmless: dragging a file over a payment row (just hovering — not dropping) is
enough to make the file look dirty, because of how the tracker files the notification
before deciding to ignore it. Its "what changed?" summary will show nothing, while the
title bar and the save prompt insist something did.

So the decision is: do the temporary screen notes go on the permanent cards (fast, simple,
and this is the bug you get), or do they live somewhere else (cleaner, and the cost is a
whole extra layer of translation between the cards and the screen)?

**The evidence, in code terms.** `Category.IsEditing` and `Security.IsExpanded` call
`OnChanged(...)`, which sets `this.change = ChangeType.Changed` and fires a change event —
the persistent-edit path. `Transaction`'s flags (`TransactionDropTarget`,
`AttachmentDropTarget`, `IsCutting`) are better behaved: they live in a separate
non-persisted bitfield and fire `ChangeType.None`. But `ChangeTracker.OnMoneyChanged`
creates the per-type `ChangeList` *before* the switch that discards `None` and
`TransientChanged`, and then sets `IsDirty = true` on `this.changes.Count > 0` — so even
the well-behaved transient flags mark the document dirty. `ChangeCount` stays 0, so the
change-summary grid and the dirty flag disagree.

### Alternatives

#### A. Keep the single object graph; formalise the transient channel and fix the tracker

Domain objects may carry view state, but only through an explicitly transient path
(`OnTransientChanged`, non-persisted fields), and `ChangeTracker` is fixed to not register
a type at all for `None`/`TransientChanged` events.

- **Pros.** Smallest possible change; fixes the user-visible bug today. Keeps the property
  that makes this codebase cheap to bind to: WPF binds straight to the model and change
  notification is already there. No mapping layer over a ~15k-line object graph.
- **Cons.** It's a discipline, not a constraint — the compiler can't stop the next
  developer adding `IsSelected` with a plain `OnChanged`. The exact mistake that produced
  the current bug is still one keystroke away.
- **Unintended consequences.** Enshrining "view state on the model is fine if you use the
  right method" makes it the house style, so the model keeps accumulating view concerns
  forever. It also means the domain assembly can never be used headlessly without
  carrying UI-shaped members, which matters for the extraction work already flagged in the
  issue tracker.

#### B. Full separation — view models wrap domain objects; the domain becomes persistence-only

Every surface gets view models. Domain types lose every view member and become plain data
plus change tracking.

- **Pros.** The correct answer in the abstract, and the one that makes the domain reusable,
  headlessly testable and free of WPF. Selection, expansion, editing state and drag
  targets become per-view, which is what they actually are — today two grids showing the
  same category share one `IsEditing` flag, which is a latent bug the moment two views are
  open at once.
- **Cons.** Enormous. A mapping layer over a 15k-line object graph with collections that
  already implement two different `IEnumerable<T>` instantiations, plus synchronisation of
  every edit in both directions, plus the performance question on a register with tens of
  thousands of rows. This is a multi-month project on its own.
- **Unintended consequences.** Two-way synchronisation is where this pattern goes wrong:
  the moment a background operation (import, stock-quote refresh, attachment scan) mutates
  the model, every open view model has to be reconciled. The app already has cross-thread
  event-marshaling scars in this area. It would also invalidate a large fraction of the
  existing unit tests, which bind directly to domain objects.

#### C. Domain objects stay bindable, but view state moves to a side table keyed by identity

The domain types carry *no* view members. Each view owns a small state service — a
dictionary keyed by the domain object (a `ConditionalWeakTable` or equivalent) holding
that view's ephemeral flags, exposed to XAML through a lightweight per-row adapter or an
attached-property-style lookup.

- **Pros.** Gets B's key benefit — nothing ephemeral touches the model, so nothing
  ephemeral can ever mark the file dirty, and two views can disagree about what's expanded
  — without B's mapping layer. Data properties still bind straight to the model, so the
  performance and simplicity that make this codebase work are preserved. It is also
  incremental: one flag at a time, with the model's member deleted as each moves.
- **Cons.** A binding seam that isn't the WPF default; needs a small, well-documented
  helper and a convention so it doesn't get reinvented per view. Lifetime has to be right
  or the table leaks (weak keys solve this, but it must be deliberate).
- **Unintended consequences.** Change notification for side-table state has to be wired
  explicitly — the model won't raise it for you — so a flag that's read but never notified
  becomes a "the highlight doesn't update" class of bug that is annoying to diagnose. And
  because it's a seam rather than a wall, a future developer can still put a view flag on
  the model; the difference from A is that there's an obvious, established place *not* to.

### Panel discussion

**Architect.** This is the most structural entry in the cluster and it constrains the
register's view-state-restoration and status-model decisions. B is right and B is not
affordable. C is the version of B you can actually land, and its incrementality is the
point — each flag that moves is independently shippable and independently testable.

**Developer.** Modern WPF makes C comfortable: attached properties and per-row adapters are
idiomatic, and WPF-UI's controls bind the same way. Practically, the three transaction
flags are already nearly there — they're in a separate non-persisted bitfield, they just
fire into a tracker that mishandles them. `Category.IsEditing` and `Security.IsExpanded`
are the two genuinely wrong ones and both are small.

**Test Engineer.** Two things. First, the tracker bug is trivially testable headlessly:
set a transient flag, assert the tracker is not dirty. That test does not exist and should,
whichever alternative wins. Second, B would invalidate a large body of existing tests that
construct and assert on domain objects directly — that cost is usually forgotten when this
choice is made and it is not small here.

**Operations Engineer.** The user-visible consequence is a save prompt for work that
doesn't exist. Users learn to answer "yes" reflexively to a prompt that's usually
spurious — and then answer "yes" the one time it would have been "no". Worse, a spurious
save is a real write to the file; on SQL Server or a synced folder that's real
contention for no reason. This wants fixing in the current codebase, not only in the
redesign.

**Data Engine Expert.** Agreed and stronger: a spurious dirty flag means a spurious save
cycle, which on the SQLite path is a real transaction and on the SQL Server path is a real
round trip. The persistence-concurrency work already done in this fork assumed saves
correspond to edits. It is also worth noting the existing flags are *not* persisted
(`[XmlIgnore]`, no column mapping) — so nothing is written wrongly to disk; the damage is
entirely in the dirty/save signalling, which is good news for migration: fixing this
changes no stored data at all.

**UI/UX Expert.** The save prompt is the visible harm, but there's a second one that only
shows up in a redesign: if the shell ever shows two views of the same data — a category
tree beside a category report, which is a normal modern layout — a single shared "is
expanded" or "is editing" flag makes them fight. Today the app gets away with it because
only one view is ever live. A redesign that adds panes inherits a bug it didn't write.

**Adversarial Expert.** Careful about scope creep. The *whole* observed harm today is one
spurious save prompt, and the fix for that is roughly five lines in `ChangeTracker` plus
switching two properties to the transient path. If the panel returns "restructure the
domain model" as the answer to "sometimes it asks to save when it shouldn't", that is a
bad trade. The honest framing: **A is the fix, C is the architecture.** They are not
alternatives, they are a sequence, and A should ship first and independently so the
benefit isn't hostage to the redesign.

### Recommendation

**A now, C as the redesign's target — explicitly as a sequence, not a choice.**

The Adversarial Expert reframed the question and the panel accepted it. Concretely:

1. **Immediately, in the current codebase (no redesign dependency):** fix
   `ChangeTracker.OnMoneyChanged` to not create a `ChangeList` entry for `ChangeType.None`
   / `TransientChanged`, and move `Category.IsEditing` and `Security.IsExpanded` from
   `OnChanged` to `OnTransientChanged`. Add the headless test the Test Engineer named.
   This removes the spurious save prompt and changes no stored data.
2. **In the redesign:** domain types carry no view members. View state lives in a
   per-view, identity-keyed side table (**C**), moved one flag at a time with the model
   member deleted as each lands.

**B is explicitly rejected** — not because it's wrong but because the cost is
disproportionate to a codebase this size with a team of one, and C captures the benefit
that matters (nothing ephemeral can dirty the model, and two views can disagree).

**Recorded disagreement:** the Architect would prefer B stated as the long-term direction
with C as a staging post, rather than C as the destination. The panel did not resolve
this and it does not need resolving now — C is the next move either way, and whether a
full view-model layer follows is a question best answered after C exists.

### Needs your decision?

**No.** The panel is confident. Step 1 is an uncontroversial bug fix with a user-visible
benefit and no data impact; step 2 is the cheapest design that removes the class of
problem.

---

## D-3 — Is budgeting a transaction-level concept or a split-level one?

**Register:** D-3 · **From:** Phase 1 OQ5 · **Cluster:** `domain-model`

### ELI10

Say you go to the supermarket and spend £80. £50 of that is food, £20 is cleaning stuff,
£10 is a birthday card. The program lets you break that one £80 payment into three lines
like that.

Now, budgeting. The program has a way to tick a line as "yes, I've counted this toward my
budget" — but only on the individual lines, not on the whole £80 payment. And there's a
leftover switch that looks like it was meant to let you tick the whole payment at once.
So the question in the register is: should ticking happen at the payment level, or the
line level?

**Panel finding: the question has a much bigger answer, because the whole budgeting
feature is switched off.** Reading the app rather than the model:

- There is **no way to tick anything as budgeted** from the user interface. The app has a
  handler that decides whether the "budgeted" menu command *could* run — and no handler
  that actually runs it. The command is permanently available and permanently does nothing.
- The **budget report command** and the **show-budget command** are both declared and
  **never attached to any menu, button or keystroke** anywhere in the app.
- The amount you'd budget for a category (**"I plan to spend £40 on sweets"**) is stored in
  the file and is **never read or written by any screen** in the app.
- The advanced search offers a **"Budgeted"** condition, and the code that answers it
  returns "no" for **everything, always** — for whole payments *and* for individual lines.
  So that search box option can never find anything.

So the honest description is: this program has budgeting plumbing in its filing system, and
no budgeting. Nobody appears to have noticed it was missing. Deciding "payment level or
line level?" is deciding the grain of a feature that does not exist and has to be built
from nothing either way.

And there's a third possibility the original question doesn't offer, which the panel thinks
is the real answer: **why is there a tick at all?** The program already knows the date and
the category of every payment. It can work out "you spent £62 on sweets in March" without
you ticking anything. The tick is a leftover from an era when people balanced budgets by
hand, and it's exactly the kind of manual bookkeeping step that goes stale and rots — which
is, demonstrably, what happened here.

### Alternatives

#### A. Rebuild at transaction level — the whole payment is budgeted or not

Add the missing property on the payment, honour the flag bit that already exists, and build
the UI around ticking whole payments.

- **Pros.** Simplest UI: one tick per row in the register. Matches the leftover flag bit,
  so an old file's stored bits mean something.
- **Cons.** Wrong for split payments, which is where budgeting matters most — the £80
  supermarket trip is precisely the case a budget wants to break apart. A whole-payment tick
  forces the £10 birthday card into the groceries budget.
- **Unintended consequences.** Once whole-payment budgeting exists alongside the still-present
  per-line flag, the two disagree and something has to arbitrate. That arbitration rule
  ("a payment counts as budgeted if all its lines are") is the sort of invisible logic that
  produces numbers nobody can explain.

#### B. Rebuild at split level — keep the existing grain, add the missing UI

Build the ticking UI on the line, as the model already assumes, and delete the leftover
whole-payment flag bit.

- **Pros.** Correct grain for split payments. The model already supports it, including the
  balance bookkeeping and the error when you tick a line with no category.
- **Cons.** Tedious in practice — most payments have one line, so the user is ticking a
  single-line "split" to budget an ordinary payment, which is a confusing mental model to
  present. Retains the manual-ticking design that rotted in the first place.
- **Unintended consequences.** The existing code adjusts a running category balance as you
  tick and untick, and comments in it warn that unticking "will screw up the budget
  balance". Reviving that machinery revives a stateful, order-dependent running total that
  can drift out of agreement with the transactions it's supposed to summarise — a whole
  class of "my budget says one thing and my report says another" support problems.

#### C. Drop the tick entirely — budgeting is a plan compared against derived actuals

There is no "budgeted" flag on anything. Budgeting is: a **plan** — an amount per category
per period — and **actuals**, computed on demand from the transactions by date and
category. Nothing is marked; nothing can go stale.

- **Pros.** Nothing to rot. The actuals are always right because they're derived, not
  maintained. It removes a stateful running total, an order-dependent update path, a flag
  bit, a second flag bit and an exception type. The plan is a small, new, self-contained
  record. It is also the design every modern budgeting product uses.
- **Cons.** Loses one genuine capability: deliberately *excluding* a specific payment from
  the budget ("this £900 was the insurance excess, don't let it wreck my car budget").
  That has to come back as an explicit exclusion concept if it's wanted, rather than falling
  out of a tick.
- **Unintended consequences.** Derived actuals must agree exactly with what the category
  reports say, which pulls in the fiscal-year start, the tax-date override, transfers,
  voided and deleted payments, and sheltered accounts. Every one of those is a decision the
  budget now inherits rather than sidesteps — and every disagreement between the budget
  screen and a report becomes a bug. This is real work, just honest work.

### Panel discussion

**Architect.** Given that nothing works today, "preserve the existing grain" carries no
weight — there is no behaviour to preserve. That removes the only argument for B. C is the
design that doesn't need a maintenance ritual, and a one-person product cannot afford
rituals.

**Developer.** C deletes more than it adds: the split flag, the whole-payment flag bit, the
running-balance update, the budget-balance dates on both types, and an exception. Against
that, one new record type and one derived query. That is a rare shape for a feature
decision and it should count.

**Data Engine Expert.** On storage: the columns are `Budget` and `Frequency` on the
category, `BudgetBalanceDate` on both the payment and the line, plus one bit in each of two
flag fields. Under C, the two date columns and both bits become dead and should be dropped
on the next schema version; `Budget` and `Frequency` survive as the plan's amount and
period, though the plan probably wants to become its own table so it can vary by period
rather than being a single figure per category forever. None of this is hard, but it is a
schema change and should ride whatever migration mechanism the data-engine cluster settles
on, not be done ad hoc. Derived actuals are a straightforward aggregate query on a range
and a category — cheap on both SQLite and SQL Server given an index on date.

**Test Engineer.** C is the only one of the three that is easy to test correctly. A derived
figure is a pure function of the data: given these payments, the March sweets actual is
£62. B and A need tests for the *transition* machinery — tick, untick, tick again, tick a
payment with no category, tick after changing the category — which is where the stateful
running total will fail, and where the current code's own comments already warn of trouble.

**UI/UX Expert.** Any design that asks the user to maintain a tick will fail, and this
codebase is the proof: the tick was built, never wired to a button, and nobody noticed for
years. The modern expectation is a budget screen that is simply *correct* when you open it.
The one thing the panel must not lose is the exclusion case — "ignore this one-off" — which
is genuinely useful and should be an explicit, visible per-payment exclusion with a reason,
not a silent flag. That is a feature, not a bookkeeping step, and it reads completely
differently to the user.

**Operations Engineer.** Note the migration: old files can contain set budget bits from
whenever this last worked. Under C those bits become meaningless. They should be dropped
explicitly with a note in the release notes rather than left to be reinterpreted by a
future reader of the schema.

**Adversarial Expert.** Two pushbacks. First, C sounds clean because the panel hasn't
costed "derived actuals must agree with the reports" — and this app's reports disagree with
*each other* today (the register documents the tax report and the export disagreeing on
both sign and amount). C's budget will inherit every one of those ambiguities and will be
blamed for them. Second, and larger: **should budgeting ship at all?** The feature has been
dead for years in a product the owner uses. Building it because the model has a flag for it
is the same mistake as D-1. If the answer is "budgeting is a v2 concern", then the decision
today is only "delete the dead plumbing", which is cheap, safe and unblocks the model
cleanup either way.

### Recommendation

**Alternative C — with the Adversarial Expert's sequencing.**

The grain question as posed (payment vs. line) is moot: there is no working feature at
either grain, so neither is "preserving" anything. The panel recommends:

1. **Delete the dead plumbing** in the redesign regardless of whether budgeting ships: the
   unreachable commands, the search condition that can never match, the per-line tick, the
   two budget-balance dates and the two flag bits. This is unblocked by any product
   decision and makes the domain model meaningfully smaller.
2. **If budgeting ships, it ships as C**: a stored plan (category × period × amount) and
   actuals derived on demand. No flag, no running total, no ritual.
3. **Keep the exclusion case as an explicit feature** — a visible "exclude from budget"
   on a payment with a reason — rather than as a re-purposed tick. This is the one real
   capability the ticking model provided and it should survive in a form the user
   understands.
4. **Before building C's actuals, write down the inclusion rules** — fiscal year, tax-date
   override, transfers, voided/deleted, sheltered accounts — and make the budget screen use
   the *same* code path the category report uses, not a parallel one. The Adversarial
   Expert's first objection is the real risk in C and this is the mitigation.

This is confidently recommended as the *shape*. It is entangled with D-1: if the owner
wants savings goals or envelopes, they sit naturally on C's plan record and very awkwardly
on A or B.

### Needs your decision?

**Partly.** The panel is confident about the shape (C) and about deleting the dead
plumbing — neither needs escalation. **"Does budgeting ship at all, and at what priority?"
is a product call** and the panel is guessing; it should be answered together with D-1,
since they are the same product question at two levels of ambition.

---

## D-4 — What does a currency ratio mean, and can a non-USD user be right?

**Register:** D-4 · **From:** Phase 8 OQ12 · **Cluster:** `domain-model`

### ELI10

If you have money in two countries — some pounds, some dollars — the program has to turn
one into the other before it can tell you what you're worth in total. To do that it keeps
an exchange rate for each currency.

The catch is that a rate is only meaningful if you know which way round it is. "1.27" could
mean a pound is worth $1.27, or it could mean a dollar is worth £1.27. Those give wildly
different answers, and nothing in this program writes down which one it means. The catalog
flagged that nobody could tell from reading the code whether a non-American user's totals
come out right.

The panel worked it out by reading the rate-downloading code and the totalling code
together. The convention is: **every currency's number says how many US dollars one unit of
it is worth.** So the whole system is anchored to the dollar, always, whatever country you
live in. A British user's pounds get turned into dollars and then the dollars get turned
into pounds again to show you the total. Mathematically that works out — the dollar is just
a waypoint.

**But two real problems fell out of reading it.**

**Panel finding 1 — the shortcut is wrong.** Converting via the dollar is two
multiplications, and the program remembers the second one as if it were the whole journey
so it can skip the work next time. Every time after the first, it does only half the
conversion. In plain terms: a British user with a Canadian account would see the right
total the first time the screen draws, and a wrong one every time after — and the wrong
one *looks* plausible, which is the worst kind of wrong. This only affects someone whose
accounts *and* display currency are both non-dollar; for anyone American it never fires.

**Panel finding 2 — a crash waiting for the right file.** The same piece of code uses the
wrong kind of "and" — the kind that always checks both halves even when the first half has
already said "stop". If the display currency isn't in the file, the second half asks a
question of something that isn't there and the program falls over.

There's also a design question underneath all this. Today the program keeps **one** rate
per currency — today's rate. It does not remember what the rate was on the day you made a
payment. So if you bought something in euros three years ago, the program values that
payment at today's euro rate, not the rate you actually got. For someone who genuinely
lives across two currencies, that's not a rounding difference — it's the difference between
your books matching your bank statement and not.

### Alternatives

#### A. Keep the dollar anchor. Write the convention down, fix the two defects, make the source configurable

Declare in one place that a currency's ratio is "US dollars per unit". Fix the conversion
cache to compose both steps. Fix the short-circuit. Stop overwriting the saved service
address in the constructor, and allow the base currency in the request to follow the user's
choice.

- **Pros.** Smallest change that makes a non-US user's totals correct. Touches no stored
  data — the values already mean what they mean. The fixes are a handful of lines each.
- **Cons.** The dollar anchor is arbitrary and slightly humiliating for a non-US user
  (their books are silently routed through a third country's currency). It also means every
  conversion carries two rounding steps instead of one.
- **Unintended consequences.** Declaring the convention without *enforcing* it leaves the
  next contributor free to store a rate the other way round — and because the arithmetic
  still produces a number, the mistake is invisible. If A is chosen, the ratio should stop
  being a bare decimal property and become a small type that can only be constructed from a
  named direction.

#### B. Anchor to the user's own book currency

The rate stored for each currency is "how many units of *my* currency is one of these
worth." Changing the book currency rebases every stored rate.

- **Pros.** One multiplication, one rounding step, and the numbers in the currencies list
  mean what a user expects them to mean when they look at them. The book currency's own
  rate is trivially 1.
- **Cons.** Changing the book currency becomes a data migration rather than a display
  setting — every stored rate must be rewritten, and the rewrite is lossy through rounding.
  It also means the rate-download service has to be able to fetch relative to an arbitrary
  base, which the current provider is invoked with a hard-coded dollar base.
- **Unintended consequences.** A failure partway through a rebase leaves the file in a state
  where some rates are in the old base and some in the new, with nothing to tell them apart
  — silently, catastrophically wrong totals. That demands a transactional rebase and a
  stored marker of which base the file is currently in, which is more machinery than the
  feature is worth unless multi-currency is a headline capability.

#### C. Historical rates — capture the rate that applied on the transaction's date

Rates become a dated series. Every conversion asks "what was this worth on *that* day?"
Optionally, the rate actually obtained is captured on the transaction itself.

- **Pros.** The only genuinely correct answer for someone living across currencies. Makes
  foreign-exchange gain and loss expressible, which is a real tax concept a
  multi-currency user needs and this product cannot currently represent at all. Historical
  reports stop silently changing when today's rate moves — which, today, they do.
- **Cons.** Substantially more machinery: a new dated table, a fetch strategy for
  backfilling history, a policy for dates with no rate, and a decision about whether
  reports are "as of then" or "as of now" (both are legitimate and users want both).
- **Unintended consequences.** It changes numbers the user has already seen. A net-worth
  chart that was computed at today's rates will move when historical rates land, and the
  user will read that as the app having been wrong before — which it was, but explaining
  that is a support burden. It also makes the currency service a hard dependency for
  correctness rather than a nicety: without backfill, historical conversion silently falls
  back to something.

### Panel discussion

**Data Engine Expert.** The storage answer is the clearest thing here. Today a currency row
holds `Ratio` and `LastRatio` — today's rate and the one before it. That is not history;
it's a two-frame animation. C requires a proper dated table, which is easy on both engines
(clustered on currency + date, point lookup by "latest on or before") but is a schema
change and needs a backfill story. A and B need no schema change at all — B needs a
migration *of values*, which is worse than a schema change because it is lossy and
unverifiable after the fact. If B is ever chosen, the file must record which base it is in,
or a half-finished rebase is undetectable.

**Developer.** The two defects are small and should be fixed regardless of which direction
wins — they are live wrong answers in the current product, not redesign questions. The
cache fix is one line (compose the factors instead of overwriting). The short-circuit is one
character. Both should carry a test.

**Architect.** The structural recommendation is independent of A/B/C: conversion should stop
being a method on the account that reaches into a shared currency list, and become a single
service with one signature — convert this amount from this currency to that currency as of
this date. Under A the date is ignored; under C it isn't. That way the choice between A and
C becomes an implementation detail behind one seam instead of a change rippling through
every caller. The seam is the decision worth making now.

**Test Engineer.** Neither defect is caught by any existing test, and both are easy to pin:
two accounts in two non-dollar currencies, a non-dollar display currency, assert the total
twice and assert it doesn't change between calls. That test would fail today. There is an
existing `ExchangeRateServiceTests.cs`, so the harness exists.

**UI/UX Expert.** Whatever the internal model, the currencies list currently shows the user
a bare number with no indication of direction. "1.27" against "Pound Sterling" is
ambiguous to everyone. The display should read as a sentence — "£1 = $1.27" — and if the
anchor is the dollar while the user's book currency is the pound, the screen should say so
rather than leaving them to infer it. That is a small fix with disproportionate value,
and it is the only thing in this entry a single-currency user would ever notice.

**Operations Engineer.** The service address being overwritten in the constructor means a
saved setting is ignored, so if that provider goes away or changes terms there is no
recovery short of a new build. That is an availability risk independent of the modelling
question, and it is the cheapest thing on this list to fix.

**Adversarial Expert.** The elephant: this fork exists to migrate one American user's
Quicken history. There is a strong chance multi-currency is never exercised at all. Every
alternative here is work on a code path that may have exactly zero users. The proportionate
answer is A's *defect fixes* — because a wrong number is a wrong number and they are a
handful of lines — plus the Architect's seam, and then stop. C is a correct and expensive
answer to a question nobody in this product has asked. If the panel recommends C, it should
be honest that it is recommending a feature, not a fix.

### Recommendation

**Alternative A's fixes now, behind the Architect's conversion seam — with C named as the
shape *if* multi-currency ever becomes a supported product, and B rejected.**

Concretely:

1. **Fix the two defects** in the current codebase, with the test the Test Engineer
   described. They produce wrong totals today.
2. **Declare the convention explicitly** — "ratio = US dollars per one unit" — in one place,
   and make the ratio a type that can only be built from a named direction so it can't be
   stored backwards.
3. **Introduce one conversion service** with an `asOf` parameter that A ignores. This is the
   decision worth making during the redesign, because it is what makes C affordable later
   and unaffordable-to-retrofit if skipped.
4. **Fix the configuration defects** — stop overwriting the saved service address; let the
   requested base currency follow the setting.
5. **Fix the display** so a rate reads as a sentence with both currencies named.

**B is rejected**: it buys one rounding step and a nicer-looking number, and it costs a
lossy, unverifiable, failure-prone value migration every time the user changes their book
currency.

**C is the right answer for a genuine multi-currency product** and is explicitly *not*
recommended for this one, on the Adversarial Expert's grounds.

**Panel flags: would benefit from multi-currency accounting expertise.** Nobody on this
panel can say authoritatively how foreign-exchange gain and loss should be recognised for
tax, and that — not the totalling arithmetic — is the real reason a serious multi-currency
product needs C. If the owner ever wants C, that question should be answered first.

### Needs your decision?

**Partly.** Steps 1–5 are defect fixes and one cheap architectural seam; the panel is
confident and no decision is needed. **"Is multi-currency a supported capability of this
product?"** is a scope question only the owner can answer, and it determines whether C is
ever built. The panel's recommendation is written so that answering "no" costs nothing and
answering "yes" later is affordable.

---

## D-28 — What happens to a sale the product can't find a purchase for?

**Register:** D-28 · **From:** Phase 7 OQ3 · **Cluster:** `taxes` · *Defect instances: #57*

### ELI10

When you sell shares, the tax you owe depends on what you originally paid for them. So the
program works backwards through your records: "you sold 100 shares in March; which 100 did
you buy, and when, and for how much?"

Sometimes it can't find them. The commonest reason is completely ordinary: you moved an
account in from a broker you never tracked here, or you inherited shares from a relative.
The shares are real, the sale is real, but there's no purchase in this program to match
them to.

What the program does then is: it puts that sale in a **holding pen**, in case a purchase
turns up later. If one never does, the sale stays in the pen — and **the tax report never
mentions it at all.** Not as a warning, not as a zero, not as a footnote. It's as though the
sale never happened. The file you export to your tax software is short by that sale.

There is machinery in the program that was clearly meant to handle exactly this: a section
of the tax report titled "Capital Gains with Unknown Cost Basis", an "Errors Found"
section, and a special record type in the export file that exists purely to say "I sold
this and I don't know what it cost." All three are unreachable. The flag they all check is
never set by anything.

**Panel finding — it isn't invisible everywhere.** The portfolio report *does* show these,
under a heading called "Pending Sales", but only when you're looking at all your holdings
rather than one group. So the information exists and the app already knows how to phrase
it — it just never reaches the one report where it matters most. That makes the fix
cheaper than it looks, and the omission more clearly accidental.

Two smaller things sit alongside it: a shortfall of less than one share is discarded without
even reaching the holding pen; and a sale that *is* rescued later can get matched to a
purchase made *after* the sale, which produces a negative holding period and lands in the
short-term pile by accident rather than by rule.

The decision is what *should* happen — and that matters disproportionately here, because
this fork exists to import a financial history that starts in the middle.

### Alternatives

#### A. Report it with a zero cost, clearly flagged

The sale appears in the tax report with a cost of nothing and a visible marker, so the gain
shown is the full sale proceeds — the most tax you could possibly owe.

- **Pros.** Nothing disappears. The figure errs toward over-paying, which is the safe
  direction with a tax authority. The export record type for exactly this case already
  exists in the format and in dead code.
- **Cons.** The number is wrong, often enormously — inherited shares in particular have a
  cost basis stepped up to their value at the date of death, which is usually close to what
  you sold them for, so a zero basis can overstate the gain by the entire proceeds.
- **Unintended consequences.** A visibly alarming number invites the user to "fix" it by
  inventing a purchase transaction, which then pollutes the real holdings record and the
  portfolio report forever. Making a wrong answer prominent without offering the right way
  to correct it pushes users toward the worst correction.

#### B. Let the user supply the missing basis — a first-class "opening lot"

The user records what they know: acquisition date and cost (or "inherited, valued at X on
date Y"). It is stored, remembered, and feeds every future calculation. Sales with no
basis are surfaced as a to-do list.

- **Pros.** It is the only alternative that produces a *correct* answer. It is exactly what
  a migration-from-elsewhere product needs, and Quicken itself has this concept (placeholder
  entries), so an arriving user already understands it. Once supplied, the problem is solved
  permanently rather than re-flagged every year.
- **Cons.** A new persisted concept, a UI to enter it, and a decision about whether an
  opening lot is a special kind of purchase transaction or a separate record. Real work.
- **Unintended consequences.** If the opening lot is modelled as a synthetic purchase
  transaction, it will show up in the register and in spending reports as though money
  moved, which it didn't — the same class of contamination as D-1's Alternative C. If it's a
  separate record, every piece of code that walks purchases has to know about a second
  source. The panel considers the second the lesser evil, but it is not free either way.

#### C. Exclude, but count loudly

Keep excluding the sales from the figures, and put an unmissable banner on the report and
on the export: "3 sales totalling £12,400 are not included — no cost basis is known",
linked to the list.

- **Pros.** Cheapest of the three by a wide margin — the data already exists and the
  portfolio report already renders it. Doesn't put a wrong number anywhere. Honest.
- **Cons.** The user still can't file their taxes from the report; they're told there's a
  hole but not given a way to fill it. It converts a silent failure into a loud dead end.
- **Unintended consequences.** If the export still omits the sales, the exported file
  remains quietly wrong even though the screen is now honest. The banner must gate the
  export too — which means the export needs a "you are exporting an incomplete return"
  confirmation, and that is a conversation the export path does not currently have.

### Panel discussion

**Architect.** These aren't really three alternatives; B subsumes the others. The question
is what happens *before* the user supplies a basis, and the answer is C. A is what should
happen only if the user explicitly says "I know it's wrong, export it anyway."

**Developer.** Cost ordering: C is small (the pending list is already computed and already
rendered elsewhere — it needs to reach the tax report and the export). A is small too and
mostly consists of deleting the dead flag check and setting the flag. B is a genuine
feature: a record, a dialog, a list.

**Test Engineer.** All three are testable headlessly against the cost-basis calculator with
no UI: sell shares that were never bought, assert the sale appears in the right bucket.
That test does not exist. Note also that fixing the flag assignment immediately activates
three pieces of never-executed code — the two report sections and the export record type —
and code that has never run once is not code that works. Whichever alternative wins, those
three paths need tests before they're trusted, not after.

**Operations Engineer.** The migration angle matters more than usual: this fork's *entire
premise* is importing a history that begins mid-life. The first real import will produce a
pile of these. Whatever the answer is, it has to work at scale on day one and be
discoverable from the import summary, not only from a report the user might not open until
April.

**Data Engine Expert.** B adds one small persisted record — security, account, units,
acquisition date, cost, and a basis-type marker for inherited-vs-purchased. No concurrency
concerns, no interaction with the persistence redesign. The only thing to get right is that
it must participate in the same change tracking and save path as everything else, or it
will be the one thing that doesn't survive a crash.

**UI/UX Expert.** Strong preference for B with C as its resting state, and a specific
framing: don't call it an error. The user hasn't done anything wrong — they imported a real
history. Frame it as "These shares came from before your records start. Tell us what they
cost and your tax figures will be complete." A to-do list with a count, sitting where the
user meets it during import, not buried in a tax report in April. And A's zero-basis number
should *never* be shown as though it were a real figure — if it's shown at all it's shown
struck through or greyed with the marker, not as a plain number in a column of real ones.

**Adversarial Expert.** Two challenges. First — is this just the bug from issue #57? Partly
yes: assigning the flag is a genuinely small fix and it lights up three dead code paths.
The panel should not use "there's a design decision here" as cover for not doing the cheap
fix. Second, and more serious: **A is actively dangerous for inherited shares.** Zero basis
on inherited stock isn't conservative, it's wildly wrong, and if the user trusts it they
over-pay by a lot. "Errs toward over-paying so it's safe" is a comfortable assumption that
doesn't survive contact with the actual rules. The panel should not recommend A as a
default under any circumstances.

### Recommendation

**B as the destination, C as the behaviour until the user supplies a basis, A only on
explicit opt-in — and the cheap fix shipped first.**

1. **Ship the small fix now** (it is issue #57 and the panel endorses it without
   re-litigating): mark unmatched sales so they stop vanishing, which activates the tax
   report's unknown-basis section, its errors section, and the export's existing
   unknown-basis record type. **Cover all three with tests before trusting them** — none has
   ever executed.
2. **Default behaviour is C**: the sale is never silently dropped. It appears in the tax
   report as an unknown-basis row, it is counted in a visible banner, and the export path
   asks for confirmation before producing an incomplete file.
3. **Build B in the redesign**: an opening-lot record the user can supply, surfaced from
   the import summary as a friendly to-do rather than from a tax report as an error. This is
   the correct answer and it is directly on this fork's critical path.
4. **A is available only as an explicit user choice** ("I know the basis is unknown, treat
   it as zero and export it anyway") and is never a silent default, per the Adversarial
   Expert's objection.
5. **Fix the two adjacent defects** while in the area: the sub-one-unit shortfall that is
   discarded without reaching the pen, and the later-purchase match that produces a negative
   holding period.

**Panel flags: would benefit from tax-preparer / CPA expertise.** Specifically: what basis
is acceptable for inherited shares (stepped-up basis rules), for shares transferred in from
an untracked broker, and whether the export format's unknown-basis record is still accepted
by current tax software. This ties directly to D-29 and none of the seven roles here can
answer it responsibly.

### Needs your decision?

**No** for steps 1, 2, 4 and 5 — the panel is confident, and "don't silently drop a sale
from a tax report" needs no product taste. **Step 3 (building opening lots) is a
prioritisation call**, not a design one: the panel is confident it's the right feature and
confident it's on this fork's critical path, but when it gets built is the owner's.

---

## D-29 — Which tax year is this product for, and how does the user know?

**Register:** D-29 · **From:** Phase 7 OQ10 + OQ13 · **Cluster:** `taxes` · *Defect instances: #57*

### ELI10

Tax rules change every year. The numbers change, and so do the *forms* — which box on which
page you write a figure in.

This program has three piles of tax information in it, and they're from wildly different
times:

- The list of tax boxes you can assign your spending to — the thing the whole tax feature is
  built on — is from a specification dated **June 2006**, and the boxes it names are from the
  **2004** tax forms. The forms were completely restructured in 2018. Many of the boxes it
  offers you no longer exist. Newer things — health savings accounts, self-employment income
  in its current form — aren't in there at all.
- The federal tax rates file has **no year written in it anywhere.** No date, no source,
  nothing. The numbers look like they're for 2026, but that's a guess from their size.
- The state tax rates file, to its credit, *does* say what it is: it names itself, dates
  itself and cites its source.

And nowhere on any screen does the program tell you which year's rules it's using. There is
no "these figures are estimates" note anywhere in the entire application — the panel
checked. There's no way to update the numbers except installing a new version of the
program, and nothing warns you when they've gone stale.

So you can run a projection in 2030 and get 2026's rules, silently. Or export a file to
your tax software containing box numbers from 2004, and either it's rejected or — worse —
it's accepted and puts your figures in the wrong places.

**One important scoping fact the panel established:** the rate tables (federal and state)
are used by **one thing only** — the retirement projection. They do not touch the tax report
or the export file. The 2004 box catalogue, by contrast, is what the tax report and the
export are entirely built on. Those are very different levels of risk: a projection is an
estimate by nature; an export goes into the software that files your return.

### Alternatives

#### A. Version the data properly and give it an update path

Every data file carries its tax year, its source and its publication date. Every screen that
shows a tax figure shows the vintage. A staleness check warns when the data is older than
the current tax year. The data ships as downloadable/sideloadable packs rather than being
baked into the build.

- **Pros.** Honest and self-describing. The state file already does this, so there's a model
  to copy. The staleness check is cheap and catches the 2030 case. Decoupling data from
  builds means a rules update doesn't need a release.
- **Cons.** An update path is an ongoing commitment: somebody has to produce next year's
  pack, every year, forever. For a one-person fork that is a real and recurring obligation.
  Refreshing the *box catalogue* in particular means re-deriving hundreds of line mappings
  against current forms.
- **Unintended consequences.** Loadable data packs are a supply-chain and integrity concern
  — a file the app reads at startup, from disk, that determines tax figures. It needs at
  minimum validation and ideally signing. Also, the two rate files are already loaded from
  disk beside the executable with no existence check and no error handling, and the
  assembly-location trick they use returns nothing under single-file publishing — so "make
  it loadable" has to fix that first or it becomes a startup crash.

#### B. Retreat the scope: keep the estimates, drop the year-specific export

Keep the tax report as a categorised summary of your own money. Drop the export to tax
software entirely (or reduce it to a plain spreadsheet with no box numbers). Frame
everything the product says about tax as an estimate, prominently.

- **Pros.** Removes the highest-risk thing in the product — a file of 2004 box numbers going
  into software that files a return — and removes the annual obligation to maintain the box
  catalogue. What remains (categorise your spending, see the totals) is genuinely useful,
  genuinely maintainable, and doesn't go stale.
- **Cons.** Takes away a real feature some users value. "Export to TurboTax" is a headline
  capability for a personal-finance product, even if in practice few people use it.
- **Unintended consequences.** It also strands the tax-category assignments users have
  already made against their categories — those still work for the on-screen report, but
  their box numbers become decorative. Worth keeping the assignments and relabelling them
  as user-facing groupings rather than form boxes, otherwise the feature looks arbitrary.

#### C. Disclose only — keep everything exactly as it is, and say so

Add a visible vintage line and a warning at the point of export. Change no data and build
no update mechanism.

- **Pros.** Almost free. Removes the *silent* part of the problem, which is the worst part.
  Makes no promises that can't be kept.
- **Cons.** The export still produces a file that may be rejected or, worse, silently
  mis-mapped by current tax software. A warning the user clicks through does not make the
  file correct.
- **Unintended consequences.** A disclaimer becomes a substitute for fixing things. Once
  "estimate only, not tax advice" is on the screen, every subsequent tax defect has a
  ready-made reason not to fix it. That is a cultural cost, and in a codebase where the tax
  report and the export already disagree on both the sign and the amount of the same figure,
  it is a real risk rather than a theoretical one.

### Panel discussion

**Operations Engineer.** The maintenance commitment is the whole decision. A is correct and
A means somebody produces a data pack every January. This is a fork maintained by one
person. Promising an annual update path that then doesn't happen is worse than not
promising it, because the staleness warning becomes the permanent state of the app and
users learn to ignore it. Also: the two rate files are loaded from disk with no error
handling at all, and one of them is read in a constructor with no try/catch — a missing or
malformed file takes the panel down at startup. That must be fixed before anything else in
this entry, whichever direction wins.

**Architect.** Separate the two concerns cleanly, because they have different risk
profiles and possibly different answers. The **rate tables** feed a projection only — A is
cheap there and the staleness warning is appropriate and honest. The **box catalogue** feeds
a file that goes into tax software — that is where B deserves serious consideration.
Treating them as one decision forces the wrong answer on one of them.

**UI/UX Expert.** Whatever is decided, the vintage belongs *on the figure*, not in an
about-box. "Estimated using 2026 federal rules" under the projection; "Tax categories based
on the 2004 Form 1040 structure" on the tax report. And there is currently no disclaimer
anywhere in the entire application, which for a product that computes tax figures is
surprising on its own. On B: users don't emotionally distinguish "export to tax software"
from "I can do my taxes with this" — removing it is a visible capability loss and should be
a deliberate, stated product decision, not a quiet deletion.

**Test Engineer.** A is testable in a way the others aren't: a test asserts each shipped
data file declares a tax year, and a test asserts the staleness check fires for a year in
the past. Those are cheap regression tests that keep the promise honest. There is also
currently nothing validating the shape of the data at all — the state file contains a
trailing comma that a stricter JSON parser would reject at startup, which is a live hazard
given the codebase is being modernised toward the stricter parser.

**Data Engine Expert.** Opting out on the substance — this is reference data shipped with
the application, not the user's database, and none of it touches the storage engines. One
adjacent note only: if data packs become loadable at runtime (A), they should land in a
known per-user location with an integrity check, not beside the executable where they are
today, because "beside the executable" is not writable in a normal installation and
interacts badly with single-file publishing.

**Developer.** Adding the vintage metadata is an hour. The staleness check is an hour. The
loading fixes are an hour. Refreshing the box catalogue against current forms is *weeks*,
and it's weeks that recur. That asymmetry should drive the answer: do the cheap honest
thing everywhere, and treat the catalogue refresh as the genuinely expensive decision it is.

**Adversarial Expert.** The uncomfortable question: **does the export work at all today?**
The register already records that the per-account export route writes no file header, that
dates are written in the machine's local format, that five of seven record shapes are
unimplemented so ~56 tax lines export as nothing, and that the report and the export
disagree on both sign and amount. Against that background, arguing about whether the box
numbers are from 2004 is arguing about the paint on a car with no engine. If the export is
this broken, B isn't a retreat — it's recognising that the feature doesn't currently exist
in working form, and that rebuilding it correctly is a project nobody has scoped. The
panel should say that plainly.

### Recommendation

**Split the decision. A for the rate tables (confident). B or a scoped rebuild for the
export (owner's call).**

**For the rate tables — Alternative A, and the panel is confident:**

1. Give `FederalTaxes.json` the same self-describing header the state file already has: tax
   year, source, publication date, schema version.
2. Show the vintage on the projection itself, in the UI/UX Expert's form.
3. Add a staleness check that warns when the data's tax year is behind the current one.
4. **Fix the loading first** — existence check, error handling, no unguarded read in a
   constructor, no reliance on the assembly location, and validate the data on load
   (including the trailing comma, which is a live startup hazard under a stricter parser).
5. Add the Test Engineer's two regression tests.

**For the box catalogue and the export — the panel defers, with a clear framing.** The
Adversarial Expert's point is the one that carried: the export is not a working feature
whose data is stale; it is a substantially broken feature whose data is *also* stale.
There are three honest options and the owner must pick:

- **Retire it (B).** Keep the tax report as a categorised summary; drop or downgrade the
  export. Removes the annual obligation and the highest-consequence risk in the product.
- **Rebuild it.** Refresh the catalogue against current forms, implement the missing record
  shapes, fix the header/date/sign/amount defects, and accept an annual maintenance
  commitment. This is a project, not a task.
- **Disclose and freeze (C).** Keep it working as-is with a prominent warning at export.
  The panel is least comfortable with this — it leaves a file that may be silently
  mis-mapped by current tax software — and offers it only for completeness.

**Panel flags: would benefit from tax-software interoperability expertise.** Nobody here
can say whether current consumer tax software still accepts a 2006-vintage exchange format,
how it treats retired box numbers, or how much of the catalogue would actually need
re-deriving. That answer changes the cost of "rebuild it" by an order of magnitude and
should be obtained before the owner chooses.

### Needs your decision?

**Yes, for the export.** Whether the export to tax software survives, gets rebuilt, or is
retired is a product-scope decision with a real recurring maintenance commitment attached,
and the panel would be guessing. **No, for the rate tables** — versioning them and showing
the vintage is cheap, honest and needs no decision.

---

## D-30 — Model every filing status properly, or disclose the approximations?

**Register:** D-30 · **From:** Phase 7 OQ14 · **Cluster:** `taxes`

### ELI10

How much tax you pay depends partly on your household: single, married and doing your taxes
together, married but doing them separately, or a single parent (which has its own, kinder
set of rules).

This program lets you pick which one you are. But for two of the four, it doesn't actually
have the right numbers, so it quietly uses the "single" numbers instead. The code says so
in its own comments — one of them literally asks *"are there income brackets for this?"* and
another just says *"todo"*.

The effect is small if you don't earn much, because the low bands are similar. It gets
badly wrong at the top, where the "married but separate" cut-off is half the joint one
rather than the same as single.

For state taxes it's worse: the state data doesn't have single-parent or married-separately
tables *at all*, for any state. So a single parent gets a correct federal estimate sitting
right next to a state estimate calculated on completely the wrong table — and nothing on
the screen says one of those two numbers is a guess.

**Two things the panel established by reading the code and data:**

**Panel finding 1 — the federal fix is mostly free.** The federal data file *already has* a
married-separately table for investment gains, and the code already uses it. It's only the
ordinary-income brackets and the standard deduction that are missing. Adding those is
editing a data file — a few dozen numbers — not writing code.

**Panel finding 2 — the blast radius is one screen.** All of this only affects the
**retirement projection**. The tax report and the export file don't use any of it. So we're
talking about the accuracy of a long-range forecast, not a number anyone files.

### Alternatives

#### A. Complete the tables

Add the missing federal brackets and deduction; add single-parent and married-separately
tables to all 51 state files.

- **Pros.** Every answer is right. No caveats on screen.
- **Cons.** The state half is 51 hand-maintained files, sourced by hand, and it recurs every
  year. It is a large, permanent, dull commitment for a one-person fork — and stale complete
  tables are arguably worse than acknowledged incomplete ones, because they look
  authoritative.
- **Unintended consequences.** Completing the data doesn't remove the hardcoded fallback
  from the code — the `=> Single` mapping stays there, now unused, waiting to silently catch
  a state whose data is incomplete or a filing status added later. The defect survives the
  data fix.

#### B. Disclose — shrink the promise to what the data supports

Keep the fallback, but make it visible: label every approximated figure, and say which
table was used and why.

- **Pros.** Honest, immediate, and costs nothing to maintain. A projection is an estimate by
  nature, so "your state figure uses the single-filer table because Ohio's data doesn't
  distinguish head of household" is a perfectly respectable thing for a forecasting tool to
  say.
- **Cons.** Two of four filing statuses carry a permanent asterisk. Some users will read
  that as the product being half-finished, which — for state data — it is.
- **Unintended consequences.** If disclosure is written as hardcoded text next to a
  hardcoded fallback, then when the data *is* later improved for one state, the caveat keeps
  appearing for that state until somebody remembers to remove it. Static disclosure rots the
  same way static data does.

#### C. Data-driven capability, with disclosure generated from it

The code stops hardcoding `=> Single`. Instead it asks the data: "do you have a head-of-
household table?" If yes, use it. If no, fall back *and record that a fallback happened*,
which the UI renders automatically. Complete the federal data (cheap); leave state data to
be improved incrementally, state by state, with zero code changes needed as it improves.

- **Pros.** Gets A's accuracy wherever the data exists and B's honesty wherever it doesn't,
  with no duplicated knowledge between code and data. Improving one state's data is a pure
  data edit, and the caveat for that state disappears on its own. Removes a class of
  hardcoded assumption rather than one instance of it.
- **Cons.** Slightly more code than either A or B alone — a capability query and a way to
  carry "this figure was approximated, here's why" out to the UI alongside the number.
- **Unintended consequences.** Once a figure can be accompanied by "how it was computed",
  every other approximation in the tax engine becomes a candidate for the same treatment —
  the bracket-inflation inconsistency, the ignored state capital-gains exclusions, the
  federal gains-stacking error. That is arguably a benefit, but it is scope: the mechanism
  will attract work, and the panel should expect that rather than be surprised by it.

### Panel discussion

**Developer.** C costs marginally more than B and strictly dominates it. The capability
query is a nullable check on the data object; the annotation is one extra field travelling
with the result. The federal data completion should happen regardless — it's a data edit
with a real accuracy gain and no maintenance tail, since federal is one file, not 51.

**Architect.** The general principle is what matters here and it generalises past tax: when
code compensates for missing data, the compensation should be derived from the data's own
shape, not hardcoded. Every hardcoded `=> Single` is a fact about the data written down in
the wrong place, and it goes stale independently of the data it describes. C is that
principle applied.

**UI/UX Expert.** Strongly for C, with a presentation note: the annotation must be a
*property of the figure*, shown inline — a small marker next to the number with a
hover/expand explanation — not a footnote or a general disclaimer at the bottom. Users
reading a projection look at the number; anything not attached to the number is not read.
And the explanation should be specific and blameless: "Ohio's data does not distinguish
head of household, so the single-filer rates were used." That tells them how much to trust
it, which is the actual goal.

**Test Engineer.** C is by far the most testable. A test asserts that for every state and
every filing status, either the correct table was used or the result is flagged as
approximated — which is a single property test over all 51 states and 4 statuses, and it
can never silently regress. With A or B you're testing individual numbers, which is
laborious and doesn't catch the case of a state whose data is silently incomplete.

**Data Engine Expert.** Opting out of the substance — this is reference JSON, not database
storage. One adjacent contribution though, since it bears on data trust: nothing validates
the bracket tables at all. Each bracket carries both a minimum and a maximum, and **no
calculation ever reads the maximum** — the bands are defined purely by the minimum plus an
unchecked assumption that the array is sorted ascending and has no gaps. Fifty-one
hand-maintained files is a lot of places for that assumption to break, and when it breaks
the result is silently wrong tax with no error anywhere. If the data gains capability
markers (C), it should gain a load-time validator at the same time: sorted, gapless, minimum
and maximum consistent. That is cheap and it protects every alternative here.

**Operations Engineer.** Opting out — no deployment, migration or operational dimension
here beyond what D-29 already covers about how these files ship.

**Adversarial Expert.** The thing nobody has said: the filing-status approximation is
**not the biggest error in this projection**. The register's own catalog records that
federal investment-gains rates are applied to the gains alone, ignoring all other income —
so a high earner with a modest gain is estimated at 0% when the real rate is 15%. That is
a far larger distortion than using the single-filer bracket for a married-separately filer,
and it compounds over a thirty-year projection. Fixing the filing-status tables while that
stands produces a *precisely* wrong answer instead of an approximately wrong one, which is
worse, because precision invites trust. If C ships, the annotation mechanism should be used
to say the projection is an estimate with known limitations — and the gains-stacking defect
should be fixed first, on impact grounds, whatever its issue number says.

### Recommendation

**Alternative C, with the federal data completed and the Adversarial Expert's sequencing
honoured.**

1. **Complete the federal data** — add married-separately income brackets and standard
   deduction. Pure data edit, real accuracy gain, no maintenance tail (one file).
2. **Replace every hardcoded fallback with a capability query**, and carry a
   "this figure was approximated, and here's why" annotation out with the result.
3. **Render the annotation inline on the figure**, specific and blameless, per the UI/UX
   Expert.
4. **Add the Test Engineer's property test** across all states and statuses, and the Data
   Engine Expert's load-time table validator (sorted, gapless, min/max consistent).
5. **Improve state data opportunistically** — one state at a time, data-only, with the
   caveat disappearing automatically as it lands. Do not commit to all 51 up front.
6. **Fix the gains-stacking defect first.** Filing-status precision on top of that error
   produces confident wrong answers.

**A is rejected as a commitment** (51 files, annually, for one person) though it is welcome
opportunistically. **B is subsumed by C** and is strictly worse, because its disclosure
doesn't self-update as data improves.

**Panel flags: would benefit from state-tax-data sourcing expertise.** Whether a
maintained, licensable, machine-readable source of state bracket data exists (and at what
cost) would change this calculus completely — if it does, A becomes cheap and recurring
maintenance disappears. Nobody here knows.

### Needs your decision?

**No.** The panel is confident. C is cheaper than A, more honest than B, and self-maintaining
as data improves. The only genuinely optional part is step 5's pace, which is a
"do it when you feel like it" matter rather than a decision.

---

## D-31 — Is a characterization test allowed to ratify a wrong answer?

**Register:** D-31 · **From:** Phase 7 OQ15 · **Cluster:** `taxes`

### ELI10

When you're carefully modernising an old program, there's a golden rule: **write down what
it currently does before you change anything.** You write little automatic checks that say
"when I give it this, it gives me back that." Then when you rewrite the insides, the checks
tell you whether you accidentally changed the answer. The whole point is that you're not
allowed to "fix" things you merely *think* are wrong — because half the time the weird
behaviour turns out to be deliberate, and you've just broken something somebody relied on.

That rule is this project's working discipline and it's a good one.

But here's the awkward case it just produced. There's a place where the program calculates
state tax on investment gains, and if you had a **loss** instead of a gain, it calculates a
**negative tax** — as though the state were going to send you a cheque. No state does that.
It's plainly an arithmetic accident: the same function everywhere else in the tax code has a
line that says "if it's a loss, the tax is zero", and this one is missing it.

Somebody noticed, and — following the rule — wrote a test that says "yes, it returns minus
£3,500, and here's a comment explaining that the guard is missing."

So now there's an automatic check whose job is to make sure the bug keeps working. If
somebody fixes it, the check fails and complains.

The question isn't really about tax. It's: **when is "write down what it does" the right
call, and when is it just nailing a bug in place?** And how do you tell those apart at a
glance six months later?

### Alternatives

#### A. Keep the test; establish a convention for marking defect-ratifying tests

The test stays as a change-detector, but it is unmistakably labelled: a naming convention, a
test category that can be filtered and listed, and a link to the tracking issue.

- **Pros.** Keeps the discipline intact and keeps the change-detection value — if a rewrite
  accidentally alters this number, somebody finds out. The set of ratified defects becomes a
  list you can produce on demand, which is genuinely useful going into a redesign.
- **Cons.** The suite accumulates tests that assert wrong answers, and a green suite stops
  meaning "the code is right". New contributors read a passing test as an endorsement no
  matter what the comment says.
- **Unintended consequences.** Because the test passes, nothing ever forces the question
  again. A ratified defect has no expiry date and no pressure behind it; it quietly becomes
  permanent. Whatever convention is chosen needs something that keeps producing the list —
  otherwise the label is just a nicer gravestone.

#### B. Fix this one and flip the test

Add the missing guard, change the test to assert zero, and keep the old expectation in the
comment as a record.

- **Pros.** A negative tax is not behaviour anyone can depend on. Nothing is stored, nothing
  is exported, no user workflow encodes it, and no existing file contains it — it is a
  transient computed number inside a forecast. The "don't fix what you don't understand"
  rule exists to protect behaviour someone might rely on, and nobody relies on this.
- **Cons.** Sets a precedent that "obviously wrong" is a category a developer may decide
  unilaterally — which is precisely the judgement the discipline was introduced to remove.
  The next case will be less obvious and the precedent will be invoked anyway.
- **Unintended consequences.** Fixing it changes the projection's output for any user with a
  realised loss in a state with a fixed-rate gains tax — a number that will move without
  explanation. Small, but it is the kind of silent output change the characterization
  discipline is designed to surface, so the fix should be noted in release notes rather than
  slipped in.

#### C. The pair pattern — ratify the present, encode the future

Two tests. One asserts current behaviour and is explicitly labelled as ratifying a known
defect. The other asserts the *desired* behaviour and is skipped, referencing the issue.
When the fix lands, delete the first and un-skip the second.

- **Pros.** Captures both facts: what it does now and what it should do. The skipped test is
  a machine-readable statement of intent that sits right next to the code, and the
  changeover is a mechanical, reviewable edit. Skipped tests show up in reports, which
  supplies the ongoing pressure A lacks.
- **Cons.** Two tests per defect. For a codebase with a large number of known defects —
  this one has ~114 — that is a lot of ceremony if applied uniformly.
- **Unintended consequences.** Skipped tests are widely ignored in practice. Without
  somebody actually reading the skip list, C degrades into A with twice the code. It only
  works if the skip list is surfaced somewhere a human sees regularly.

### Panel discussion

**Test Engineer.** The missing rule is what makes a defect worth ratifying, and it isn't
"how obvious is the bug" — that's unresolvable by argument. The workable test is
**dependency**: does anything outside this function encode the wrong answer? Is it written
to a file? Exported? Does a user workflow or a stored setting depend on it? Has a user
learned to work around it? If yes, it's behaviour and you pin it, because changing it has
consequences you can't see from here. If no — it's a transient computed value that exists
only inside one call — then it is an arithmetic defect and you fix it. That rule is
objective, it's checkable, and it generalises well beyond tax. By it, this case is
unambiguous: nothing stores, exports or depends on a negative state capital-gains figure.
It is computed inside a forecast and discarded.

**Architect.** Agreed, and the framing matters. Characterization testing is a tool for
managing *unknown* risk. Once you have read the code and established there is no dependency
and no defensible reading — which is exactly what happened here, and it's documented in the
test's own comment — the uncertainty the tool exists to manage is gone. Continuing to apply
the tool past that point is cargo-culting the process rather than following it.

**Developer.** The fix is one line and it makes this function consistent with the three
sibling functions that all have the guard. That consistency argument is independent of the
testing philosophy: an inconsistent guard across four near-identical functions is a defect
by inspection.

**Adversarial Expert.** Genuine pushback, and the panel should record it rather than wave
it away. The dependency rule is good but its edges are softer than the Test Engineer makes
out — "does a user work around it" is not something you can determine from source, and in a
product with one user it collapses to "ask the owner", which isn't a rule. There is also a
second-order risk: once "no dependency, therefore fix it" is written down, it will be
reached for enthusiastically, and the first time somebody is wrong about dependency it will
be in a place where being wrong is expensive. **Mitigation:** the rule requires the
dependency analysis to be *written into the commit or the test*, so the judgement is
reviewable rather than implicit. That is cheap and it is what turns a slogan into a
practice. With that, no objection to B here.

**Operations Engineer.** One point, otherwise opting out: any behaviour change that alters
a number the user has already seen belongs in release notes, even a correction. Users
notice numbers moving and assume the new one is the broken one.

**UI/UX Expert.** Opting out — internal engineering convention, nothing user-facing beyond
what the Operations Engineer said.

**Data Engine Expert.** Opting out — nothing persisted, nothing stored.

### Recommendation

**Fix this one (B), and adopt the Test Engineer's dependency rule as the general convention,
with A's labelling for the cases the rule says to keep.**

**The convention, stated for reuse:**

> A characterization test pins behaviour that something *depends on*. Before ratifying an
> apparently-wrong answer, establish whether anything outside the function encodes it — is
> it persisted, exported, read by another component, or relied on by a user workflow?
>
> - **Yes, or unknown** → pin it. Name the test so it is unmistakable
>   (`Ratifies_Known_Defect_*`), give it a filterable category, and link the issue.
> - **No** — it is a transient computed value with no defensible reading → **fix it**, and
>   write the dependency analysis into the commit message so the judgement is reviewable.

**For this specific case:** add the missing loss guard, flip the test to assert zero, keep
the original expectation and the reasoning in the test's comment as the historical record,
and note the output change in release notes.

**On the general mechanism:** prefer **A's labelling** over **C's pair pattern** as the
default. C is better in principle but the panel does not believe the skipped half gets read
in practice, and with ~114 known defects the ceremony is disproportionate. C remains the
right choice for a small number of high-value cases where the desired behaviour is worth
encoding precisely — a defect actively being worked on, for instance.

The Adversarial Expert's amendment — that the dependency analysis must be written down, not
merely performed — is part of the recommendation, not a caveat on it. Without it the rule
is a slogan.

### Needs your decision?

**No.** This is an engineering-convention call squarely within the panel's competence, and
the specific fix (a state does not pay you tax on a loss) needs no product taste. The
convention above is offered as ready to adopt.

---

## Appendix — session summary

### Recommendations at a glance

| # | Decision | Panel's recommendation | Needs your decision? |
|---|---|---|---|
| D-1 | Envelope budgeting / category funds | **A** — leave it out, with a deliberate migration; **C (funds-as-accounts) ruled out unconditionally**; if envelopes ever ship, **B** as a virtual allocation ledger (probably framed as savings goals) | **Yes** — product scope only |
| D-2 | View state on domain objects | **A now, C as the target** — fix the change-tracker and move two properties to the transient path immediately; in the redesign, view state moves to an identity-keyed side table. **B (full view-model layer) rejected on cost** | No |
| D-3 | Budgeting grain | **C** — no "budgeted" flag at all; a stored plan plus derived actuals. Delete the dead plumbing regardless. Keep exclusion as an explicit feature | **Partly** — shape is confident; whether budgeting ships is the owner's |
| D-4 | Currency model | **A's fixes behind a dated conversion seam** — fix the conversion cache and the short-circuit, declare the convention, make the source configurable. **B rejected**; **C** named as the shape only if multi-currency becomes real | **Partly** — is multi-currency supported? |
| D-28 | Sales with no cost basis | **B as destination, C as interim, A opt-in only** — ship the flag fix now with tests, never silently drop a sale, build user-supplied opening lots | **Partly** — prioritisation of opening lots |
| D-29 | Tax-year vintage | **Split**: **A** for the rate tables (version, disclose, staleness-check, fix loading) — confident. Export/box catalogue: retire, rebuild or freeze — owner's call | **Yes** — for the export |
| D-30 | Filing-status approximations | **C** — complete federal data, replace hardcoded fallbacks with data-driven capability queries, generate disclosure from them, fix gains-stacking first | No |
| D-31 | Characterization tests ratifying defects | **B for this case + the dependency rule as convention** — fix the negative-tax defect; pin only behaviour something depends on; write the analysis down | No |

### Explicit opt-outs

- **Data Engine Expert** opted out of the substance on **D-29**, **D-30** and **D-31** —
  all three concern reference data shipped with the application or an internal testing
  convention, none touch the user's database. Contributed adjacent notes only (where
  loadable data packs should live; the unvalidated bracket tables).
- **Operations Engineer** opted out on **D-30** (no deployment or migration dimension
  beyond D-29's) and largely on **D-31** (one release-notes point).
- **UI/UX Expert** opted out on **D-31** — internal engineering convention.

### "Would benefit from additional expertise" flags

- **D-4** — multi-currency accounting expertise: how foreign-exchange gain and loss should
  be recognised. This, not the totalling arithmetic, is the real driver for historical rates.
- **D-28** — tax-preparer / CPA expertise: acceptable cost basis for inherited shares
  (stepped-up basis) and for holdings transferred in from an untracked broker.
- **D-29** — tax-software interoperability expertise: whether current consumer tax software
  still accepts a 2006-vintage exchange format, and how much of the catalogue would need
  re-deriving. This changes the cost of "rebuild the export" by an order of magnitude and
  should be answered before the owner chooses.
- **D-30** — state-tax-data sourcing expertise: whether a maintained, machine-readable
  source of state bracket data exists and at what cost. If it does, completing all 51 tables
  stops being a recurring burden and the recommendation changes.

### Recorded disagreement

- **D-2** — the Architect would state a full view-model layer as the long-term direction
  with the side-table approach as a staging post; the rest of the panel treats the side
  table as the destination. Unresolved and deliberately left so: the next move is the same
  either way.

### Cross-decision dependencies noted

- **D-1 and D-3** are the same product question at two levels of ambition and should be
  answered together.
- **D-3's** plan record is the natural home for **D-1's** envelopes or savings goals; the
  tick-based alternatives are a poor foundation for either.
- **D-2's** decision constrains the register's view-state-restoration (D-8) and status-model
  (D-5) entries, which other panels are covering.
- **D-28** and **D-29** meet at the export's unknown-basis record type: whether it is worth
  activating depends on whether the export survives at all.
- **D-30's** annotation mechanism ("this figure was approximated, and here's why") is
  reusable for **D-29's** vintage disclosure and for the other known limitations in the tax
  engine.
