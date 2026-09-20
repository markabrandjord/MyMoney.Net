# Panel Review 02 — Views/Editing and Dialogs/Forms

One expert-panel session over eight entries of the
[Redesign Decision Register](redesign-decision-register.md): the five `views-editing`
decisions and the three `dialogs-forms` decisions.

| ID | Title | Cluster |
|---|---|---|
| D-8 | Is "put me back where I was" a product guarantee or a per-surface nicety? | `views-editing` |
| D-9 | Which surfaces are searchable? | `views-editing` |
| D-10 | Ship "show me everything like this", or drop it? | `views-editing` |
| D-11 | Should the categories tree show balances? | `views-editing` |
| D-12 | Should duplicate detection be suppressed while reconciling? | `views-editing` |
| D-13 | What does Cancel guarantee? | `dialogs-forms` |
| D-14 | What commits a form from the keyboard, and may it commit something destructive? | `dialogs-forms` |
| D-15 | One real shared dialog base, and what it must carry | `dialogs-forms` |

The panel: **Architect**, **Developer** (C#/.NET/WPF, WPF-UI/Fluent), **Test Engineer**,
**Operations Engineer**, **UI/UX Expert**, **Data Engine Expert** (SQLite/SQL Server),
**Adversarial Expert**. Members opt out of decisions outside their lane rather than
padding; opt-outs are recorded. Where the panel found the seven roles genuinely
insufficient it says so under *Panel flags*.

Before opining the panel re-read the source code behind each decision rather than working
only from the catalog. Four findings that the catalog and register did not record turned
out to change a recommendation, and are marked **New finding** where they appear. In
summary:

- **`Category.Balance` is never computed by this application.** It is a mapped database
  column read at load (`SqliteDatabase.cs`, `SqlDatabase.cs`,
  `SqlServerStoredProcDatabase.cs`, column 7) and copied in `Categories.ReParent`
  (`Money.cs:7545`). Nothing else in `Source/WPF` ever assigns it. `Account.Balance` *is*
  computed (`Money.cs:10119`); the category equivalent is inert. (→ D-11)
- **`RecategorizeDialog`'s "From category" is decorative.** It is a read-only `TextBox`
  the caller never reads; `TransactionsView.OnRecategorizeAll` rewrites `Category` on
  *every* transaction in the current view regardless of what it was. (→ D-14)
- **`AccountDialog`'s alias box can silently re-home an alias belonging to a different
  account** (`a.AccountId = this.theAccount.AccountId` on Enter), and Cancel does not
  restore it — so one window's Cancel fails to undo a change made to a *different*
  record. (→ D-13)
- **Saved view state is keyed by .NET type name in a per-user settings file**, not
  per-database-file (`Settings.GetViewState(typeof(T))`, `viewStateNodes` keyed by
  `Type`). Renaming a view class silently discards every user's saved state, and state
  containing entity IDs is replayed against whatever money file is opened next. (→ D-8)

---

## D-8 — Is "put me back where I was" a product guarantee or a per-surface nicety?

**From:** Phase 3 OQ2 · **Register entry:** D-8 · **Related:** B-31 (Phase 5), D-2

### ELI10

When you close a program and open it again, you'd like it to look the way you left it —
the same page, the same thing highlighted, scrolled to the same spot. This program tries
to do that, but only two of its pages actually remember anything: the list of money going
in and out of an account, and the list of investments you own. The page that shows a
loan's payment plan *saves* what you were looking at, but when it goes to read it back it
hands over an empty note instead, so nothing is restored. Three other pages don't even
pretend — they hand back nothing in both directions. And the program never remembers
*which page* you were on at all, so it always starts you somewhere else.

Every page has a slot for "remember me" built into it, so it looks from the outside like
this is a promise the program makes. It isn't; it's a promise two pages keep. The thing
being decided is whether the new version makes it a real promise the whole program keeps
automatically — which means the part of the program that moves you between pages has to
own it, so no one can forget — or whether it says honestly "only these particular pages
remember, and the rest start fresh." Getting it wrong in the friendly direction is worse
than it sounds: if the program remembers a filter that was hiding most of your
transactions, you come back and think your money has vanished.

### Alternatives

**A-8.1 — Shell-owned navigation contract.**
Surfaces are addressed by a serialisable *route* (surface id + parameters, e.g. "register,
account 17") held by a navigation service that also owns the journal. The shell persists
the route and, per route, a small typed state bag the surface may contribute to via an
optional interface with a working default. Restore-where-you-were — including *which*
surface — becomes a property of navigation, and every new surface inherits it.

- *Pros.* Cannot decay: a new surface gets it without its author doing anything. Fixes
  B-31 (never reopening on the last report) as a side effect, because "which surface" is
  part of the route rather than something nobody owns. Matches how WPF-UI's
  `NavigationView`/navigation service is meant to be used, so the redesign gets back/
  forward, breadcrumbs and restore from one mechanism. Makes the currently-copy-pasted
  state classes (`CurrenciesViewState` writing `SelectedSecurity`) structurally impossible
  — a route's parameters are typed to the surface.
- *Cons.* Forces every surface to have a serialisable selection identity, which the
  rental and rename-rules surfaces do not currently have. It is real up-front design work
  in the shell before any surface is built, and it is the kind of work that feels like
  overhead until the fifth surface.
- *Unintended consequences.* Routes become a public contract: once a route id is written
  into a user's settings file it is an upgrade compatibility surface, so renaming or
  merging a surface later needs a route alias table — the redesign acquires a small
  migration obligation it does not have today. A route-based shell also makes deep linking
  and "open this in a new window" cheap, which will attract feature requests. And caching:
  today views are singletons kept alive forever (`MainWindow.cacheViews`), which is what
  makes in-session restore free; a route-based shell that creates surfaces on demand gets
  better memory behaviour but must now restore state on *every* navigation, not just at
  startup, which is where the bugs will be.

**A-8.2 — Honest optional capability.**
Drop `ViewState`/`DeserializeViewState` from the interface every surface must implement.
Make it a separate `ISupportsViewState` that a surface opts into. Surfaces that don't
implement it are not lying. Add a test that enumerates every registered surface and
asserts it either implements the interface correctly or appears on an explicit,
commented opt-out list.

- *Pros.* Cheapest by a wide margin. Removes the four "return null / return new
  ViewState()" stubs that make the current interface a lie. The test is the durable part
  and would have caught `LoansView` and `CurrenciesView` the day they were written.
- *Cons.* Does nothing about "which surface was I on", which is the piece users notice
  most and which no surface can own. Keeps restore as per-surface bespoke code, so the
  same class of copy-paste bug (saving a `Security` id from a grid of `Currency` rows) can
  recur in a surface that *does* opt in.
- *Unintended consequences.* An explicit opt-out list is a decay mechanism with better
  manners: items get added to it under deadline and never removed. It also quietly settles
  D-2 in the "surfaces own their own state" direction, because if the shell never needs to
  understand state, nothing pushes view state out of the domain objects.

**A-8.3 — Session-only, fresh on launch.**
Guarantee restore *within* a run of the application only. On launch the product always
opens on a canonical home surface with no filters applied. Nothing is persisted between
runs except genuine preferences.

- *Pros.* The most predictable behaviour there is, and the easiest to explain. Eliminates
  the whole class of "stale saved state describes a world that no longer exists" bugs —
  deleted accounts, IDs from a different money file, state written by an older version.
  Zero persistence format, zero upgrade obligation.
- *Cons.* Users who work in one account every day will resent it, and "remember where I
  was" is table stakes in the products this one is being compared against. It also throws
  away `TransactionViewState`, which today is the *good* implementation and is genuinely
  rich (P3-FIND-6: account, filter, search text, row height, selection, the whole selector
  chain).
- *Unintended consequences.* Forecloses nothing technically, but it sets a product tone —
  "this app starts clean" — that later gets argued with one surface at a time, which is
  exactly how the current inconsistency arose. It would also make the register's own
  restore a regression for existing users, so it is not a neutral choice for an upgrade.

### Panel

**Architect** — A-8.1. The reason this decayed is that restoration was put in the *leaf*
where eight authors each had to remember it, instead of in the *spine* where one
mechanism serves everyone. The register says exactly this and it is right. The fact that
"which surface was I on" is unowned by anyone is the tell: it is a navigation concern, and
there is no navigation layer to own it.

**Developer** — A-8.1 is less work than it sounds *if* the redesign is already adopting a
WPF-UI navigation host, because the journal and the page lifecycle come with it; what has
to be written is the persistence of the route and the typed parameter bag. What is not
free is selection identity for surfaces that don't have one. Note also that today's
in-session restore works by accident — views are cached singletons, so nothing is ever
re-created and nothing is ever restored. A navigation host that creates pages on demand
turns an accidental success into code that has to be right.

**Test Engineer** — This is the strongest argument for A-8.1 and it is a testing one.
Today verifying restore means eight bespoke tests that nobody wrote. With a route contract
it is one parameterised test: for each registered route, navigate to it, mutate its state,
navigate away, navigate back, assert equality; then round-trip the persisted form and
assert equality again. That single test replaces the entire class of defect. Under A-8.2
you get a weaker version of the same test, which is still worth having and should be
written either way.

**Operations Engineer** — Two upgrade hazards nobody has named. First, saved state is
keyed by the .NET **type name** and lives in a **per-user** settings file, not beside the
money file. So (a) renaming or moving a view class silently discards every existing user's
saved state — under A-8.1 the route id must be a stable string chosen deliberately, not
`typeof(T).FullName`; and (b) state containing account and security IDs is replayed
against whatever money file is opened next, so a user with two files gets one file's
selection restored into the other. That is a live defect today and A-8.1 should fix it by
scoping per-route state to the open file. Second, `MainWindow` currently swallows
deserialisation failures with a `Debug.WriteLine` — the fail-safe instinct is right and
must be kept, but it should be observable in the log rather than invisible.

**UI/UX Expert** — Restoring *location* and *selection* is high-value and users expect it.
Restoring a **filter** is different in kind: a restored quick-filter or state filter hides
data, and the user who comes back to a nearly-empty register concludes the product lost
their transactions, not that a filter is on. Today `TransactionViewState` restores the
search text. So whatever is decided, an active filter must be visibly, dismissibly
announced on restore. Restoring scroll position within a report is worth the least and
should be the first thing dropped if it costs anything.

**Data Engine Expert** — Mostly out of lane. One note: restoring by entity ID is a
dictionary lookup, not a query, so there is no load-time cost — but it must tolerate the
ID not existing (deleted account, different file) without an exception and without
silently selecting the wrong row, which is the failure mode when IDs are reused across
files.

**Adversarial Expert** — The panel is treating "remember everything" as obviously good.
It isn't. The single most annoying behaviour a finance app can have is opening on a
filtered view that looks like missing data — and the *best*-implemented surface here,
the register, is precisely the one that restores a search box. So the honest recommendation
is not "promise it everywhere" but "promise *location and selection* everywhere, and treat
restoring anything that hides data as a separate, visible thing." Second point: A-8.3 is
being dismissed too fast. It is the only option with no upgrade obligation and no stale-
state bugs. If the redesign is time-constrained, shipping A-8.3 plus "remember the last
account in the register" covers most of the perceived value for almost nothing.

### Recommendation

**A-8.1 — shell-owned navigation contract**, with three amendments the panel considers
part of the recommendation rather than optional polish:

1. Route ids are stable, deliberately-chosen strings, never `typeof(T).FullName`.
2. Per-route state is scoped to the open money file, not to the user. Preferences stay
   per-user; *selection and filter* state follows the file.
3. Location and selection restore silently; anything that hides data (quick filter, state
   filter, date range) restores only with a visible, one-click-dismissible indicator.

Amendment 3 is the Adversarial Expert's point and it carried — the panel agreed that
"restore" and "restore filters" are different promises and should not be bundled.
A-8.2's coverage test should be built regardless, as the enforcement half of A-8.1.

**Needs your decision:** No — the panel recommends A-8.1 with confidence. The one sub-
question with genuine product taste in it is amendment 3's default (does the quick filter
restore at all?), and the panel's default of "restores, but announced" is a reasonable
answer either way.

---

## D-9 — Which surfaces are searchable?

**From:** Phase 3 OQ4 · **Register entry:** D-9

### ELI10

Nearly every list in this program has a box you can type in to narrow the list down, and
there's one keyboard shortcut that jumps your cursor into that box. The rule the program
tries to follow is "that shortcut puts the cursor in this page's search box, and if a page
has no search box, nothing happens."

The trouble is "nothing happens" looks exactly like "broken." Press the shortcut on the
currencies list and nothing moves, so you assume the shortcut is broken rather than
learning that this particular page just doesn't do that. And it's a bit odd on the
currencies page specifically, because somebody clearly *started* building search there —
the part that decides whether a row matches what you typed is written and working — they
just never put the box on the screen.

What's being decided is whether the new version gives every list a search box
automatically, even when a list is six rows long and searching it is pointless, or whether
search is something each page decides for itself — in which case pages without it need to
*say* so instead of going quiet.

### Alternatives

**A-9.1 — Universal filter affordance provided by the shell.**
Surfaces declare a match predicate over their rows. The shell renders the filter box in a
consistent place for any surface that declares one, owns the keyboard shortcut, and shows
the "filtered: N of M" state. A surface with no predicate gets no box, and the shortcut
reports "nothing to search on this page" in the status surface rather than doing nothing.

- *Pros.* Consistent placement and behaviour on every list, which is the thing users
  actually learn. Kills the stubbed `FocusQuickFilter()` pattern outright. The existing
  row-matching code on `CurrenciesView` is reusable as a predicate, so three of the four
  gaps close for almost nothing. Declaring searchability lets the shell grey the affordance
  instead of going silent.
- *Cons.* A filter box on a six-row currency list is visual noise. Requires each surface to
  expose a predicate even where the answer is trivially "match the name."
- *Unintended consequences.* A shell-owned filter box invites the assumption that it
  searches *everything*, which it does not — and the register's box has a genuinely
  different grammar (a leading `*` widens the search to all accounts, per P3-FIND-2). One
  box in one place with two meanings depending on the page is a discoverability trap that
  will need explicit handling. It also weakly couples the shell to every surface's row
  type, which the redesign should keep to a `Func<TRow,bool>`-shaped seam rather than
  anything richer.

**A-9.2 — Per-surface opt-in, but declared.**
Keep search as a per-surface decision. Change only this: searchability becomes a declared
property the shell can read, so the shortcut and any menu entry are disabled with a reason
on surfaces that don't have it. Stub implementations are deleted rather than left empty.

- *Pros.* Cheapest. Preserves the freedom to leave a tiny list unsearchable. Fixes the
  actual user-visible complaint — silence — without building anything.
- *Cons.* Leaves placement and behaviour to each surface, which is where the current
  inconsistency came from. Does not help `CurrenciesView`, which would still need a box
  added by hand.
- *Unintended consequences.* "Declared" is only as good as the declaration, and a surface
  author who forgets is back to the current state. Needs the same coverage test as D-8 to
  hold, which is most of A-9.1's enforcement cost without A-9.1's benefit.

**A-9.3 — Global find / command palette.**
One search affordance that searches *across* the product — accounts, categories, payees,
securities, transactions — and navigates to what you pick, rather than filtering the
current list. Per-surface filtering stays only where it is a genuine filter (the register).

- *Pros.* The modern Windows answer, and it is what users of other finance tools reach
  for. Solves a real problem the product does not solve at all today: "where is that
  thing" as distinct from "narrow this list."
- *Cons.* Substantially more work than either alternative. Does not actually answer D-9 —
  the per-surface question remains for the register, holdings and aliases lists, which are
  genuine filters.
- *Unintended consequences.* A global find needs a result ranking and, at scale, an index;
  built naively over the in-memory model it will be fine at personal-finance size and
  quietly bad the first time someone opens a twenty-year file. It also competes with the
  advanced query builder for the same mental slot, and the product cannot afford three
  search grammars (quick filter, query builder, global find).

### Panel

**Architect** — A-9.1. The register frames this as "universal or per-surface" but the real
content is *who owns the affordance*. Once the shell owns it, "which surfaces are
searchable" stops being a decision that can be got wrong silently and becomes a property
that is either declared or not.

**Developer** — A-9.1 is small: WPF-UI's `AutoSuggestBox` in a consistent header slot,
surfaces supply `Func<TRow,bool>` plus a placeholder string. The one thing to avoid is
putting the filter box in the title bar, because the title-bar position reads as global
search, which brings us straight to A-9.3's confusion.

**Test Engineer** — The distinction that matters to me is testable declaration. Under
A-9.1 the coverage test is "every surface either supplies a predicate or declares it has
none, and no surface has an empty `FocusQuickFilter`." That test is three lines of
reflection and prevents the exact four defects found. A-9.3 is the hardest to test of the
three because relevance ranking has no obvious oracle.

**Operations Engineer** — Nothing to add; no deployment or data implications.

**UI/UX Expert** — Filtering and finding are different verbs and must not share a control.
A filter changes what is in front of you and has an obvious "clear" state; a find takes
you somewhere. Put the filter in the surface's own header and, if A-9.3 is ever built, put
find in the shell chrome. On the "pointless on six rows" objection: it is not pointless —
a consistent box that is occasionally unnecessary costs far less than a box whose presence
you have to learn page by page.

**Data Engine Expert** — Opting out; these are small in-memory collections. One caveat for
A-9.3 only: a global find over the whole model is fine today but is the first feature that
would want real query support from the storage layer rather than a linear scan.

**Adversarial Expert** — Everyone is answering "which surfaces get a search box" without
asking whether those surfaces exist in the redesign. Three of the four gaps are
`LoansView`, `RentSummaryView` and `RentInputControl` — and `RentInputControl` is a whole
surface nothing in the product can open (Phase 3 OQ1). If the redesign folds loans into
the register and rationalises the rental surfaces, D-9 shrinks to "should the currencies
list be searchable," and the answer to that is "it is a reference table with about thirty
rows; yes, trivially, and it costs nothing because the matching code is already written."
So: pick A-9.1 because it is cheap and correct, but don't spend design time on the general
question. The general question is mostly an artefact of surfaces that shouldn't survive.

### Recommendation

**A-9.1 — a shell-provided filter affordance that surfaces opt into by supplying a
predicate**, placed in the surface header (not the shell chrome), with the shortcut
reporting an honest "nothing to search here" on surfaces that declare no predicate. The
coverage test from A-9.2 is adopted as A-9.1's enforcement.

**A-9.3 is recorded as a separate future capability, not a substitute** — if global find
is ever built it goes in the shell chrome with a visibly different control, and the
product must not end up with three search grammars.

The panel notes the Adversarial Expert's observation that three of the four affected
surfaces may not survive the redesign, and that this decision should therefore not be
allowed to consume design time proportional to its register entry.

**Needs your decision:** No.

---

## D-10 — Ship "show me everything like this", or drop it?

**From:** Phase 3 OQ5 · **Register entry:** D-10

### ELI10

When you're looking at one payment in your list, it would often be handy to say "show me
all the other ones like this" — every time you bought coffee at that shop, say. The
program has a menu command for exactly that, with all the wiring behind it… and the part
that's supposed to actually do the work is empty, and the menu item has been hidden. So
nothing was lost; it just never got built.

There's a catch that makes this less exciting than it sounds: the program *already* has
"show me everything from this shop" and "show me everything in this category" right next
to it, and those work. So if "like this" just means "same shop", the new command would be
a second door to the same room.

Where it would genuinely help is when the shop's name isn't written the same way twice.
Bank downloads are messy — the same coffee shop can arrive as five slightly different
names — so "everything from this shop" misses four of them, and a smarter "things like
this" would catch them all. That's the real question: is this worth building as a
*cleverer* match, or should the empty command just be deleted?

### Alternatives

**A-10.1 — Delete the dead command.**
Remove the command, the binding, the can-execute handler and the commented-out menu item.
The existing "view by payee", "view by category" and "view by account" commands are the
product's answer to this need.

- *Pros.* Free, immediate, and honest — a command that exists but does nothing is worse
  than no command. Forecloses nothing: the capability can be proposed later on its own
  merits rather than inherited as an obligation.
- *Cons.* Leaves unserved the case the existing commands genuinely miss — the same real-
  world payee arriving under several spellings, which is exactly the situation a Quicken
  import creates.
- *Unintended consequences.* None technically. Politically, deleting it removes the
  reminder that the need exists, so the need should be recorded somewhere (this document,
  or an issue) or it will be re-discovered in two years.

**A-10.2 — "Similar" = fuzzy payee family.**
Build it as a matcher: find transactions whose payee belongs to the same *family* as the
selected one, using the alias table and the normalised-payee logic that
`AutoCategorization` already walks across accounts. One click, straight to results.

- *Pros.* Delivers the only part of this that the existing commands cannot. Reuses
  machinery that already exists and is already exercised by auto-categorisation. Directly
  valuable for the imported-data case this fork exists to evaluate.
- *Cons.* "Similar" acquires a quality bar. A fuzzy matcher that returns one wrong row
  makes the whole feature feel unreliable, and tuning thresholds against real data is
  open-ended work. Weeks, not days.
- *Unintended consequences.* It quietly becomes the front door to *payee cleanup* — a user
  who sees five spellings of one shop will immediately want to merge them, which is
  `RenamePayeeDialog`'s job. Two overlapping cleanup paths with different behaviour is a
  worse outcome than either alone, so this alternative drags a second feature into scope.
  It also adds a second opaque matching engine beside `AutoCategorization`'s, which the
  Architect flags as a maintainability cost: two places that decide "these payees are the
  same thing" will drift.

**A-10.3 — "Similar" = a seeded, editable query.**
The command derives a query from the selected row (payee, category, an amount band, a date
window), runs it immediately and shows the results, with the query panel available beside
them pre-filled so the user can loosen or tighten it.

- *Pros.* Cheap — the query builder, the field list and the execution path all exist.
  Deterministic, so no tuning and no quality bar. Teaches the advanced query builder to
  users who would otherwise never open it. Explains itself: the user can see *why* a row
  matched.
- *Cons.* On its own, with payee matched by identity, it is A-10.1 with extra steps —
  it returns the same rows as "view by payee". Its value depends entirely on what the seed
  query contains.
- *Unintended consequences.* It makes the query grammar's existing quirks user-facing.
  `CLAUDE.md` already records that `QueryRow.Matches`'s `GreaterThan` is implemented
  inclusively and that `Field.Payment` only matches when the amount is negative; a seeded
  amount band would expose both. Those become bugs the moment a casual gesture generates
  them, where today only a user who built the query by hand ever sees them.

### Panel

**Architect** — A-10.2 creates a second similarity engine; if "similar" is built it should
*call* the payee-matching logic that already exists rather than growing its own, which
pushes toward A-10.3's shape with A-10.2's matcher inside it.

**Developer** — A-10.3 is a couple of days. A-10.2 is weeks once the tuning and the
inevitable cleanup-UI follow-on are counted. A-10.1 is an afternoon and removes code.

**Test Engineer** — This is where the alternatives really separate. A-10.3 is
deterministic and testable by asserting the generated query rows — a handful of unit tests
against a fixture. A-10.2 needs a labelled corpus and a similarity threshold, which means
either a test suite that encodes today's tuning (and therefore blocks re-tuning) or no
meaningful test at all. Given this codebase's history of features that were built and then
quietly stopped working, an untestable feature is a feature that will rot.

**Operations Engineer** — Nothing to add.

**UI/UX Expert** — A casual right-click gesture should produce a *result*, not a form. So
if this is built, results come first and the query panel is a "refine this" affordance
beside them, not a gate in front of them. Also: whatever the matching rule is, the results
header must state it in words ("47 transactions from payees like *Starbucks*"), or the
user cannot tell a good match set from a bad one.

**Data Engine Expert** — Opting out. In-memory scan over the transaction collection either
way; no storage implications at personal-finance scale.

**Adversarial Expert** — Ask the question the panel is avoiding: would a real user ever
choose this menu item when "view all from this payee" sits one item above it? Only if the
two give different answers. That is the whole decision. If "similar" means exact payee,
delete it — a second door to the same room is a UI defect, not a feature. If it means
fuzzy, then you are committing to a matching-quality problem, a tuning loop and a payee-
cleanup follow-on, for a convenience feature. Neither is obviously right, and which one is
right depends on how messy the owner's actual imported data turns out to be — which is
something the owner can find out and the panel cannot.

### Recommendation

The panel splits the question in two and is unanimous on the first half, deferring on the
second.

1. **Delete the dead command now** (A-10.1). A command with an empty handler and a
   commented-out menu item is not a feature awaiting resurfacing; removing it costs
   nothing and removes a false signal from the codebase.
2. **If the capability is wanted, build it as A-10.3's presentation with A-10.2's
   matching** — results first, payee matched by family (aliases plus normalised payee, via
   the existing auto-categorisation logic rather than a new engine), with the seed query
   shown and editable beside the results and the match rule stated in words.

**Needs your decision: yes** — on whether to build the capability at all. The panel can
recommend the *design* but not the *priority*: the feature's entire value rests on how
badly the imported payee data is fragmented, which only the owner, looking at their own
converted file, can judge. A concrete way to decide it cheaply: after importing the
Quicken data, count distinct payees against the number of real-world merchants. If the
ratio is close to 1, delete and forget; if it is 3:1, this is one of the more valuable
things in the register.

**Panel flags:** would benefit from **information-retrieval / record-linkage expertise**
if A-10.2's matching is pursued — none of the seven roles can say what a good
payee-similarity threshold is, and the panel would be guessing.

---

## D-11 — Should the categories tree show balances?

**From:** Phase 3 OQ9 · **Register entry:** D-11

### ELI10

Down the side of the program there are two panels. One lists your accounts, grouped by
kind, and each group shows a total — how much is in all your bank accounts, and so on.
That's useful and it's correct. The other lists your spending categories as a tree
(Food, Food → Groceries, Food → Restaurants). It shows no totals at all, even though the
program computes something called a total for each one and there is even an empty space in
the layout where a number would clearly have gone. So the two panels look inconsistent
side by side, and it's not obvious whether somebody removed the numbers on purpose or
never finished putting them in.

**New finding — it's neither.** The panel checked, and the number behind the category
total is *never calculated by this program at all*. It is a column that gets loaded from
the file and written back out, and nothing in the whole application ever works it out.
(The account balances, by contrast, really are calculated.) So in any file this program
created, every category total is zero. Showing it would show a wrong number.

There's also a real reason the two panels differ. "How much is in my savings account" has
one answer. "How much did I spend on Food" has no answer until you say *when* — this
month, this year, ever? A total with no timeframe is close to meaningless. So the decision
is: leave the category tree as names only and accept that the two panels look different
because they *are* different, or show a number and be very clear about which timeframe it
covers.

### Alternatives

**A-11.1 — Navigation panels carry names, not figures (for categories).**
The categories tree is navigation: names, hierarchy, selection. Figures live in reports
and budgets where a date range is explicit and visible. The dormant `Balance` concept is
removed from the domain model.

- *Pros.* Honest and cheap. Makes the asymmetry with the accounts panel a deliberate,
  explicable position ("an account balance is a fact; a category total is an answer to a
  question you haven't asked yet") rather than an unfinished job. Removes a denormalised
  field that nothing maintains, which is a genuine source of future confusion — the next
  developer to find `Category.Balance` will reasonably assume it means something.
- *Cons.* Gives up a real glance-value. Users arriving from Quicken expect category
  spending at a glance, and telling them "open a report" is a downgrade in their eyes.
- *Unintended consequences.* Removing the mapped column is a schema change across SQLite,
  SQL Server and the XML/CSV stores, plus the stored-proc database — for a field with no
  value in it. The Data Engine Expert's counsel below is to *leave the column dormant in
  the schema* and remove it only from the domain model, which gets the clarity at no
  migration risk.

**A-11.2 — Live figure in the tree, scoped to whatever the register is currently showing.**
Each category node shows a total computed over the transaction set currently in view, and
the panel header states the scope.

- *Pros.* Maximum information, no new controls, and the number always relates to what the
  user is looking at.
- *Cons.* The numbers change as you navigate, which reads as instability rather than
  responsiveness. Recomputation is an O(transactions) pass on every filter change — the
  panel already has coalesced delayed updates to hang that on, so it is feasible, but it is
  real work per keystroke in the quick filter.
- *Unintended consequences.* It couples the navigation panel to the register's filter
  state — exactly the entanglement D-2 and D-8 are trying to reduce. A navigation panel
  that depends on the content surface's internal state is the hardest kind of coupling to
  unwind later, and it makes the categories panel un-reusable anywhere the register isn't.

**A-11.3 — Figure on the content surface, not in the tree.**
The tree stays names-only. Selecting a category shows its total — and its subtotals — in
the header of the *content* surface, where the date range is already stated and already
adjustable.

- *Pros.* Answers "what did I spend on Movies?" at one click, in the one place on screen
  where the timeframe is unambiguous. No floating context-free numbers. No coupling from
  panel to content. Cheap: it is one header region, computed once per selection.
- *Cons.* Not a *glance* value — you have to select a category to see its figure, so you
  cannot compare five categories at once without five clicks. That comparison is exactly
  what a report does, so the panel considers this an acceptable division, but it is a real
  loss versus A-11.2.
- *Unintended consequences.* It puts pressure on the content surface header to grow, since
  the same logic argues for showing payee totals, account totals and security totals there
  too. That is probably fine and arguably good, but it should be designed as "the selected
  thing's summary" rather than bolted on for categories alone.

### Panel

**Architect** — A-11.1 or A-11.3, not A-11.2. A navigation panel that reads the content
surface's filter state is a coupling the redesign will regret; it is the same shape as the
view-state-on-domain-objects problem in D-2, just one level up.

**Developer** — Note the fingerprint in the XAML: `CategoryBalance`'s data template
defines a second column with `Width="Auto"` and puts nothing in it. A number *was* there
or was intended. But given the finding that the data was never computed, restoring the
`TextBlock` would display zeros — this is not a one-line fix, and anyone who treats it as
one will ship a wrong number.

**Data Engine Expert** — This is my lane and the finding is the decisive fact.
`Category.Balance` is a mapped column read in all three database implementations and
assigned nowhere else in the application. It is a denormalised aggregate inherited from
the file format, not a live value. Two consequences. First, **do not resurrect it** — a
persisted per-category aggregate means invalidating and recomputing on every transaction
insert, edit, delete, re-categorisation and split change, and the setter already propagates
differences up the parent chain, which is a double-counting hazard the moment two paths
update it. Second, if figures are wanted, compute them on demand: the whole model is in
memory already, so a grouped pass over transactions is simpler and has exactly one source
of truth. At personal-finance scale (tens of thousands of rows) neither an in-memory pass
nor a SQL `SUM ... GROUP BY` is a performance question. And on removal: leave the column in
the schema, unused. Dropping a column across three backends plus the XML and CSV stores
buys nothing and risks a migration for a field containing zeros.

**Test Engineer** — A-11.3 is trivially testable (select category, assert header figure
against a fixture). A-11.2 is the hard one, because its correctness depends on the
register's filter state, so every test is a two-surface test. Also worth saying: there is
currently no test anywhere that would notice `Category.Balance` being zero, which is how
a dormant field survives.

**Operations Engineer** — Only the migration point, which the Data Engine Expert covered.
Leaving the column dormant is the right call; a schema change on a user's live financial
file to delete an unused column is pure downside.

**UI/UX Expert** — I want a number *somewhere* and I don't accept "open a report" as the
answer for "what did I spend on groceries." But I concede the timeframe problem: a
context-free total in a tree is not a feature, it is a trap, and users will read it as
"this year" whatever it actually is. A-11.3 gives me the number at one click with the
timeframe visible, which is the honest version of what I want. I'd add one thing to it:
when a category is selected, show the subtotal of its children too, because the tree's
hierarchy is otherwise informationally dead.

**Adversarial Expert** — Two challenges. First, to A-11.1: "the panels are different
because the concepts are different" is a good argument that users will not accept, because
they don't see concepts, they see two panels where one has numbers. If A-11.1 is chosen,
the UI needs to *say* why — even a column header or a tooltip. Second, to everyone:
nobody has asked what a category total should *mean*. Does it include transfers? Are
income categories positive or negative? Does a split count once or per line? Does a
refund reduce the total or add to it? Those are not UI questions and the panel has no one
qualified to answer them. Whatever is shown will be argued with by anyone who does their
own bookkeeping.

### Recommendation

**A-11.3 — figures on the content surface for the selected category (with child subtotals),
not in the navigation tree** — combined with the Data Engine Expert's instruction that the
figure is **computed on demand from transactions**, never read from or written back to the
`Category.Balance` column. The dormant column is removed from the domain model and left
in the schema.

**Genuine unresolved disagreement:** the UI/UX Expert would still prefer a figure visible
in the tree itself (A-11.2's presentation with A-11.3's honesty about scope) and considers
A-11.3 a compromise rather than the best answer; the Architect regards any figure in the
navigation panel as a coupling error. The panel did not resolve this and records both
positions. It *did* reach unanimity on the two things that matter most: no number from the
dormant column, and no number without a visible timeframe.

**Needs your decision:** No, on the recommendation. **Yes**, on the Adversarial Expert's
follow-up: what a category total should include is a bookkeeping question, not a UI one,
and the answer should be settled before any figure is displayed.

**Panel flags:** would benefit from **personal-finance / bookkeeping domain expertise** —
specifically on the treatment of transfers, splits, refunds and income sign conventions in
a category total. None of the seven roles can answer this and the panel would be guessing.

---

## D-12 — Should duplicate detection be suppressed while reconciling?

**From:** Phase 3 OQ15 · **Register entry:** D-12

### ELI10

Sometimes the same payment ends up in your list twice — you downloaded it once and typed
it in once. The program is good about this: when you click on an entry that looks like a
copy of a nearby one, it draws a bracket in the margin joining the two, with a button to
merge them into one, and a way to say "no, these really are two separate things, stop
asking."

But when you're *balancing* — going down your bank statement ticking off each line to make
sure the program agrees with the bank — the program silently switches that off. And it
doesn't tell you it has.

There's a good reason: a real statement often has two identical charges on the same day
(two coffees, two bus fares), and having the program shout "duplicate!" at every one of
them while you're counting would be maddening. But there's an equally good reason the
other way: balancing is exactly when you're looking closely enough to *spot* a real
duplicate, and that's the moment the program goes quiet.

The third possibility nobody has tried: keep noticing, but be calmer about it — point the
pair out without offering the one-click "merge these" button, because merging things
mid-count while numbers are adding up is its own kind of trouble.

### Alternatives

**A-12.1 — Keep detecting during a reconcile, but demote the presentation.**
The pair is still marked, but passively: a quiet marker rather than a bracket with an
action attached. Acting on it opens a side-by-side comparison where merging is an explicit,
confirmed step outside the reconcile flow.

- *Pros.* Serves both truths at once. Nothing is hidden from the user, and nothing invites
  a destructive one-click action in the middle of a count.
- *Cons.* The most work of the three, and the most UI to design.
- *Unintended consequences.* The important one, and it is what changed the panel's mind:
  **merging during a reconcile mutates records the reconcile is counting.** `Merge` prefers
  the *reconciled* side as the survivor (P3-DUP-5), so the row the user just ticked may be
  the one that disappears, and the running statement total shifts under them mid-count.
  Worse, `CLAUDE.md` records that `RemoveTransfer` throws a `MoneyException` when the other
  side is reconciled, and `Merge` throws when both sides are transfers to different
  accounts — so a merge offered during a reconcile can fail with an exception rather than
  a message. Any design that keeps detection during a reconcile must guarantee it never
  offers an action the domain will refuse.

**A-12.2 — Keep the suppression, but make it visible and controllable.**
Detection stays off during a reconcile by default, and the reconcile strip says so, with a
toggle. The orphaned `Settings.DuplicateRange` preference (which exists, is read, and has
no control anywhere in the UI) gets a real control at the same time.

- *Pros.* Cheapest honest fix. Preserves today's behaviour for the users it suits and
  hands the choice to the ones it doesn't. Fixes the actual complaint in the register,
  which is that the behaviour is *invisible*.
- *Cons.* Defers the design question to a setting, and settings are a way of not deciding.
  Most users never change a default, so the default still decides.
- *Unintended consequences.* Exposing `DuplicateRange` is the right thing to do but turns
  an internal tuning knob into a supported contract — users who set it to 90 days will then
  report the performance and false-positive behaviour that comes with it. Also, adding one
  toggle to the reconcile strip starts the reconcile strip down the road of accumulating
  toggles.

**A-12.3 — Remove the special case; improve detection instead.**
Duplicate detection behaves identically everywhere. Reduce the noise at source: never
re-flag a pair the user has marked `NotDuplicate`, and suppress pairs where both entries
are already ticked against the same statement (which is strong evidence the bank really did
charge twice).

- *Pros.* Simplest possible mental model: the feature works the same everywhere, always.
  The noise-reduction rules are genuinely good and are worth doing under *any* alternative.
- *Cons.* Reintroduces precisely the noise the guard exists to prevent, in the one context
  where the user is least tolerant of it. False positives are expensive during a balance
  because the user is in a counting mindset and every interruption costs them their place.
- *Unintended consequences.* Inherits all of A-12.1's merge-during-reconcile hazards
  *without* A-12.1's mitigation, because the one-click merge button comes back with the
  bracket.

### Panel

**Developer** — Worth knowing how the guard actually behaves, because the register
describes it as flatter than it is. The suppression is checked at timer creation
(`lazyShowConnector == null && !IsReconciling`) and the timer nulls itself when it fires,
so it is effectively re-evaluated on every selection change — it works as intended, and
there is no leak. This is a genuine design choice in the code, not an accident.

**Test Engineer** — A-12.3 is trivially testable. A-12.2 is nearly so. A-12.1 has the
largest state space by far — detection × reconcile state × merge outcome × reconciled-side
handling — and is where regressions would hide. That is an argument for A-12.1's *narrowed*
form (detect, but no inline action), which collapses most of that space.

**Operations Engineer** — My concern is data integrity, and it points the same way. A
merge performed mid-reconcile touches records that a reconcile has already committed to a
statement; `StatementManager` state and the transaction's reconciled status can disagree
afterwards. Whatever is chosen, merging must not be reachable in one click from inside a
reconcile.

**UI/UX Expert** — The current behaviour is the worst option available, because silence
without explanation is indistinguishable from the feature being broken. Everything else on
this list is an improvement. Between them: a passive marker during a reconcile is a good
pattern (it respects the user's focus while not hiding information), provided the marker is
genuinely quiet — not a bracket, not a colour change to the row, something at the margin.

**Data Engine Expert** — Opting out; this is presentation and domain logic, not storage.

**Architect** — A-12.1 narrowed is the only option that separates *noticing* from *acting*,
and that separation is the actual content of this decision. Once you see it that way, the
original question ("suppress or not") turns out to have been the wrong question.

**Adversarial Expert** — The panel is fixing this in the wrong place. Duplicates
overwhelmingly *arrive* from imports — downloading the same OFX file twice, or importing
a Quicken export that overlaps an existing download. The moment to catch them is the
import review, before they are in the register at all, not during a reconcile weeks later.
If the redesign has a proper import-review step, reconcile-time detection is a safety net
rather than a primary mechanism, and spending A-12.1's design budget on it is
misallocation. Also, a smaller point: the register frames this as a user-visible design
question, but the two noise-reduction rules in A-12.3 (respect `NotDuplicate`, ignore pairs
ticked on the same statement) are just correct, and would remove most of the reason the
guard exists in the first place.

### Recommendation

**A-12.1, narrowed — detect during a reconcile, but present it passively with no inline
merge action** — combined with **A-12.2's honesty** (the reconcile strip states the
duplicate-flagging state, and `Settings.DuplicateRange` gets a real control) and
**A-12.3's noise-reduction rules**, which the panel considers correct regardless of which
alternative is chosen.

The reasoning that won: the question "suppress or not" contains two questions that have
different answers. *Noticing* should never be suppressed — the user is entitled to the
information, and hiding it invisibly is the worst of the available behaviours. *Acting*
should be suppressed during a reconcile, because merging mutates records the reconcile is
counting, can select the ticked row as the casualty, and can fail with an exception the
dialog has no way to present. Separating them dissolves the disagreement.

The panel accepts the Adversarial Expert's point that import-time duplicate review is the
higher-value place to invest, and recommends that reconcile-time detection be scoped as a
safety net rather than the primary mechanism.

**Needs your decision:** No.

---

## D-13 — What does Cancel guarantee?

**From:** Phase 4 OQ2 · **Register entry:** D-13 · **Defect instances:** #52 · **Related:** D-49

### ELI10

When a window has a Cancel button, everyone expects the same thing: whatever you changed
in that window is thrown away and nothing is different afterwards. Most of this program's
windows do honour that — they work on a copy of your data and only apply it when you press
OK.

But not all of them, and not consistently *within one window*. In the account window, if
you add an extra name the bank uses for the account, that's saved the instant you press
Enter — pressing Cancel afterwards doesn't take it back. Removing one is the same. The
market-data settings window doesn't restore anything on Cancel at all, and there's a
comment in the code saying so out loud. And in the rental property window, the copy it
works on isn't a real copy — the list of rental units inside it is the *same* list, so
editing a tenant's name sticks even if you cancel, while editing the property's name
doesn't. In the same window. Same button.

**New finding:** the account window's alias box can also quietly take a name away from a
*different* account and give it to this one — and cancelling doesn't give it back.

The thing being decided isn't "fix those three bugs" (they're already filed). It's what
rule the fixes should aim at, because the obvious rule — "always work on a copy" — breaks
down for things that aren't owned by one window, like a shared list of names that other
parts of the program can be looking at simultaneously. So the choice is: make the copy
approach work everywhere, build a general undo so Cancel isn't needed, or admit for those
few shared things that the window can't take it back — and stop offering a button that
says it can.

### Alternatives

**A-13.1 — Universal edit-on-copy, commit-on-OK, with real infrastructure behind it.**
One editing-scope mechanism (an explicit change set, or a begin/commit/rollback scope over
the change tracker) rather than 29 hand-written working copies. Cancel means cancel,
everywhere, including child collections.

- *Pros.* Matches what most of the product already does and what every user expects. One
  concept to teach, one mechanism to test. Removes the class of bug where a window's copy
  is shallow and its children are shared.
- *Cons.* Deep copying over a graph whose objects carry identity, parent back-references
  and change-tracking state is precisely where `RentBuilding.ShallowCopy` went wrong, and a
  general mechanism must get collections, `Parent` links and IDs right in every case. Not
  hard, but not free, and it must be *the* mechanism or it is worthless.
- *Unintended consequences.* Three that matter. (1) **Shared global records.** Aliases are
  not owned by the account window; two windows, or a window and a background import, can be
  editing the same table, and a deferred commit means last-OK-wins silently clobbers.
  (2) **Side effects that cannot be deferred.** `AccountDialog` creates a currency on the
  fly when the user picks an unknown one (P4-ACCT-8) and fetches a live exchange rate to
  display it (P4-ACCT-7). Deferring those to OK means the window cannot show the rate it
  is supposed to show. So either some side effects escape the scope — which is how the
  current inconsistency started — or the window's behaviour changes. (3) **The save
  boundary**, covered by the Data Engine Expert below.

**A-13.2 — Live edit plus application-wide undo.**
Dialogs edit live; "Cancel" is replaced by "Close"; an undo stack reverses anything. This
is only available if D-49 is answered "build undo."

- *Pros.* Makes Cancel unnecessary rather than merely honest, and serves the *register*
  too, where editing is already live and there is nothing to undo with. It is the only
  alternative that helps outside dialogs.
- *Cons.* Very large. The domain has no undo support at all (D-49), and adding a command
  journal over a ~15k-line object graph that already has its own `ChangeType` tracking
  means two change-tracking systems that must agree.
- *Unintended consequences.* Undo across a save boundary is either dishonest (the stack
  quietly empties) or requires journaling to disk. And undo cannot reverse effects outside
  the model — files written into the attachments folder, a request already sent to a bank —
  so "everything is undoable" becomes a promise with asterisks, which is the same category
  of problem this decision exists to eliminate.

**A-13.3 — Working copies by default, honest immediacy for shared records.**
Keep edit-on-copy where the window genuinely owns the data. For records that are global and
shared (account aliases, institution/market-data settings), drop the pretence: those fields
apply immediately, are visually marked as doing so, and carry their own local reversal
("Alias added · Undo") rather than relying on the window's Cancel.

- *Pros.* Cheapest, and the only alternative that is actually *correct* about shared state
  rather than papering over it. No button ever lies. Local undo on a single record is a
  fraction of A-13.2's cost.
- *Cons.* Two interaction models inside one window, which needs a strong and consistent
  visual language or it reads as arbitrary. More UI copy to write and get right.
- *Unintended consequences.* It legitimises "applies immediately" as a pattern, and
  authors under deadline will reach for it because it is less work than a working copy.
  Without a rule about *when* it is permitted, this alternative decays into the current
  state with better labels. The rule the panel would set: immediate application is
  permitted only where the record is not owned by the window's subject.

### Panel

**Architect** — The framing that unlocks this is: *a dialog is not a transaction boundary
in this model, and three separate bugs came from pretending it is.* Where the dialog's
subject owns the data, the dialog can be a boundary and should be. Where it doesn't, no
amount of copying makes it one. So A-13.1 as the default and A-13.3 as a named, documented
exception — not as a fallback when copying turns out to be hard.

**Developer** — The mechanism should be one shared editing scope rather than per-dialog
hand-copying. The domain already has `BeginUpdate`/`EndUpdate` bracketing and `ChangeType`
tracking, so a rollback-capable scope is *plausible* — but it is not free and it is not
what those primitives were built for. Whatever is chosen, `MemberwiseClone` must never
again be the copy mechanism for anything holding a collection.

**Test Engineer** — This is the highest-value thing in the whole cluster and it isn't a
convention, it's a test. One generic test: for every dialog, open it, mutate every bound
field (including collections), press Cancel, and assert the serialised model is
byte-identical to before. That single test catches all three filed bugs, catches the new
alias-rehoming finding, and catches the next one. It also *defines* the convention more
precisely than prose can — anything the test allows is the contract. It needs an explicit
allow-list for the A-13.3 exceptions, and that allow-list becomes the documentation.

**Operations Engineer** — Live edits on shared records are also a multi-window and
multi-process hazard, which this codebase has already spent effort on (the
persistence-concurrency work). A-13.3's immediate-apply fields are the ones that need to
be correct under concurrent access, which is an argument for keeping that set as small as
possible and explicitly enumerated.

**UI/UX Expert** — A Cancel button that doesn't cancel is the single most trust-destroying
thing in this folder, worse than a missing feature, because it teaches users not to believe
the product's own labels. The non-negotiable is: **no button may lie.** Beyond that, A-13.3
is acceptable and even good *if* immediacy is visually obvious — the pattern that works is
a list where added items appear with a subtle "just added · undo" affordance, so the
immediacy is self-evident rather than something you have to know.

**Data Engine Expert** — One thing nobody has raised. A rollback scope that reverts
in-memory state does **not** revert anything already written to the database. If any dialog
triggers a save, or a background save fires while a dialog is open, rollback becomes
partial and silent — the worst possible failure for this feature. So A-13.1 must include
the rule that an editing scope defers *all* writes until commit, and that a save cannot
begin while a scope is open (or must snapshot around it). On the shared-record side: for
things like aliases, the right primitive is probably a small, immediately-committed write
with its own reversal — which is A-13.3 — rather than holding a long-lived in-memory
divergence that other readers can't see.

**Adversarial Expert** — Two challenges. First, the deep-copy approach has now broken
twice, both times at exactly the same place: a collection of child records inside the
copied object. The panel should not recommend "do the deep copy properly" as though that
is a decision; it is a hope. What makes it real is the Test Engineer's generic test, and
the panel should say plainly that **the test is the recommendation** and the convention is
just what the test enforces. Second: does anyone actually cancel an account edit? Yes —
but the reason isn't "I changed my mind," it's "I clicked in the wrong field and I don't
know what I've broken." Cancel is a *safety* affordance, which is why a fake one is so
corrosive: the user who presses it believes they have made themselves safe.

### Recommendation

**A-13.1 as the default contract, A-13.3 as an explicitly enumerated exception for records
the window's subject does not own, and the Test Engineer's generic cancel-fidelity test as
the actual enforcement mechanism.**

Concretely:

1. One shared editing-scope mechanism; no more per-dialog hand-rolled copies, and never
   `MemberwiseClone` for anything holding a collection.
2. An editing scope defers all persistence until commit, and no save may run inside an open
   scope (Data Engine Expert's condition — without this, rollback is silently partial).
3. A short, named list of fields that apply immediately because their records are globally
   shared. Those fields carry their own inline reversal and a consistent visual treatment.
   Membership of that list is a design decision, not an implementation convenience.
4. A generic test over every dialog: mutate everything, cancel, assert the model is
   unchanged — with the list from (3) as its only allow-list.

**A-13.2 is rejected as the dialog contract** regardless of how D-49 is answered. Even if
undo is built, "Cancel restores" is a better guarantee inside a modal form than "Close, then
find the undo command," and undo cannot reverse out-of-model side effects.

**Needs your decision:** No — the panel is confident. The one thing worth the owner's
attention is item (3)'s membership: which records count as "globally shared" is a product
call about how the redesign models aliases and institution settings, and the panel's
current list (account aliases, market-data/institution settings) is derived from today's
code rather than from a future model.

---

## D-14 — What commits a form from the keyboard, and may it commit something destructive?

**From:** Phase 4 OQ20 · **Register entry:** D-14 · **Defect instances:** #52

### ELI10

In most programs, pressing Enter in a window means "yes, do it" and pressing Escape means
"no, forget it." This program does that in four different ways across its twenty-nine
windows, and in three of them *two different buttons* both claim to be the one Enter
presses — so what Enter does in those windows is anybody's guess. In one of them, one of
the competing buttons wipes out a saved access key.

The sharpest case is the "Recategorize All" window. It listens for Enter across the whole
window, not just on the OK button — so if you're halfway through typing the name of a
category and you press Enter out of habit, the window takes that as "do it" and the
program immediately rewrites the category on *every single transaction currently on
screen*. There's no "are you sure", no count of how many that is, and no way to undo it.

**New finding:** that window shows a "From category" box, which implies it only changes
transactions that currently have that category. It doesn't. The box is decorative — the
program never reads it — and the operation rewrites everything in view regardless of what
category each item was in.

Picking one keyboard convention is the easy part. The real question is whether the "yes,
do it" key is ever allowed to set off something that big and that permanent, or whether
operations of that size must always be confirmed with a separate, deliberate action.

### Alternatives

**A-14.1 — One mechanism, plus a rule: default buttons may not perform irreversible bulk
operations.**
Standardise on declared default/cancel semantics (no hand-rolled window-level key
interception), enforced automatically. Add a rule that the default button may not be the
trigger for an operation that changes many records irreversibly; such operations need their
own explicit gesture.

- *Pros.* No infrastructure at all, and a rule that is simple to state. Enforcement is
  cheap: a reflection or XAML test asserting that every dialog has exactly one default and
  exactly one cancel button would have caught the three competing-defaults windows *and*
  the merge-category window where both buttons are marked cancel and neither is default.
- *Cons.* "Never on Enter" is blunt. Power users legitimately expect Enter to work, and
  removing it from a bulk operation makes the operation feel broken rather than safe. It
  also needs a definition of "destructive" that authors will argue about.
- *Unintended consequences.* It does nothing for the user who reaches the same destructive
  operation with the mouse, which is most users. If the keyboard is the only path that gets
  guarded, the product has made the rarer path safe and left the common one alone.

**A-14.2 — One mechanism, plus confirmation proportional to blast radius.**
Same standardisation, but the protection is a shared confirmation component that any bulk
operation must pass through, stating the number of records affected and requiring a
distinct confirming action. Enter may commit the *form*; the form's commit opens the
confirmation.

- *Pros.* Guards every path, not just the keyboard. Self-documenting: "this will
  recategorise 1,412 transactions" is information the user does not have today and would
  often act on. Reusable across every bulk operation the redesign adds.
- *Cons.* Confirmation fatigue is real; a confirmation that always appears is one users
  learn to dismiss. The count must be accurate, which means computing the affected set
  before committing — easy here, potentially awkward for other operations.
- *Unintended consequences.* It does not fix the deeper problem with Recategorize All,
  which is that the operation's scope is "whatever the view happens to be showing" — a
  scope the user may not have clearly in mind even when it is stated as a number. A count
  makes the size visible but not the *membership*. This argues for the confirmation naming
  the scope, not just the count.

**A-14.3 — One mechanism, plus reversibility instead of confirmation.**
Standardise the keyboard, then make bulk operations undoable via a narrow, scoped
"undo last bulk change" affordance — much smaller than full application undo, because these
operations are already bracketed by `BeginUpdate`/`EndUpdate` and are homogeneous (set one
field on N records).

- *Pros.* The best user experience by a distance: it converts a safety problem into a
  recovery problem, which is almost always the better trade. No confirmation to fatigue.
  Genuinely cheaper than general undo because the operations are uniform.
- *Cons.* Still real infrastructure, and it must survive a save to be worth anything —
  an undo slot that silently expires when the file is saved is another half-truth of exactly
  the kind this cluster is trying to eliminate.
- *Unintended consequences.* Building scoped undo will make general undo (D-49) look
  cheap, and scope will drift toward it. That may be a good outcome, but it should be a
  chosen one rather than a drift. It also interacts with D-13: if bulk operations become
  reversible but dialog edits do not, the product's reversibility story becomes uneven in a
  new way.

### Panel

**Developer** — Standardise on declared default/cancel; window-level `PreviewKeyDown`
interception is a bug pattern, not a technique. Both windows that do it have a real
underlying problem (a child control that wants Enter for itself), and the correct fix is on
the child — `AcceptsReturn`, or handling the key at the child and marking it handled —
not at the window. Note that `RecategorizeDialog` never sets `e.Handled` and still calls
the base implementation, so its interception stacks with anything else that might handle
the key. In a WPF-UI redesign none of this should be hand-rolled 29 times: `ContentDialog`
gives primary/secondary/close semantics with the keyboard handled once, centrally.

**Test Engineer** — The enforcement is cheap and should be built whichever alternative
wins: a test over every dialog type asserting exactly one default button and exactly one
cancel button, and no window-level key interception. That is a few lines of reflection over
the loaded XAML and it catches B-17, the double-`IsCancel` merge dialog from Phase 4 OQ8,
and the next one. Separately, A-14.2's count is testable (assert the stated count equals
the number of records actually changed), and that test is worth having because a
confirmation that states the wrong number is worse than none.

**UI/UX Expert** — The count is the highest-value element here by a wide margin.
"Recategorize All" is the scariest control in this application and it tells the user
nothing about what it is about to do. A confirmation that says "change the category on
1,412 transactions currently shown in this view — from 14 different categories" converts a
blind action into an informed one. And the "from 14 different categories" part is essential
given the new finding: the window currently *implies* a single source category and does not
honour it.

**Operations Engineer** — Nothing distinctive to add beyond noting that a bulk
recategorisation of thousands of records with no undo and no backup capability (D-50: the
backup command exists and is unreachable) is a genuinely unrecoverable user action today.
The combination is worse than either part.

**Architect** — Agree with the Adversarial Expert below: the keyboard is one of three doors
into the same room. Standardising the keyboard is correct and cheap, but the decision's
real content is about the operation.

**Data Engine Expert** — Opting out; no storage dimension. One observation in passing: the
operation is already bracketed by `BeginUpdate`/`EndUpdate`, so it is a single change batch
— which is exactly the property that would make A-14.3's scoped undo tractable.

**Adversarial Expert** — The panel is about to over-index on keyboard mechanics. The
finding that matters is that Recategorize All rewrites every row in the current view, with
no count, no undo, and a "From category" box that is pure decoration — and the keyboard is
just one of three ways to fire it. Fix the operation. Standardising Enter and Escape is
correct but it is hygiene, not the decision. And a second challenge to A-14.2: a
confirmation dialog is what teams reach for when they don't want to build undo, and users
click through them. If the redesign is serious about this, A-14.3 is the real answer and
A-14.2 is the placeholder.

### Recommendation

**A-14.1 and A-14.2 together**, with A-14.3 recorded as the preferred long-term answer
contingent on D-49:

1. **One mechanism.** Declared default/cancel semantics (Fluent `ContentDialog`
   primary/close in the redesign), no window-level key interception, enforced by an
   automated check that every dialog has exactly one default and exactly one cancel.
2. **A blast-radius rule.** A primary/default button may commit an operation only if its
   effect is either confined to a single record or has been stated to the user in a
   confirmation that names both the **count** and the **scope** of what will change.
3. **Scoped undo for bulk operations (A-14.3) is the panel's preferred end state** and
   should be revisited when D-49 is answered; if undo exists, the confirmation in (2) can
   be relaxed for reversible operations.

On the new finding: "Recategorize All" implying a source category it does not honour is a
correctness problem independent of the keyboard, and the panel recommends confirming it is
covered by #52 — the dialog's prose does say "ALL the transactions in this view", but the
read-only "From category" box directly contradicts that prose and should not exist in a
window that ignores it.

**Needs your decision:** No.

---

## D-15 — One real shared dialog base, and what it must carry

**From:** Phase 4 OQ30 · **Register entry:** D-15 · **Defect instances:** #52 · **Related:** D-51

### ELI10

The program has about twenty-nine pop-up windows. There are five things they should all do:
look like the rest of the program (including in dark mode), open on top of and centred over
the window you came from, not clutter up the taskbar with their own button, respond to
Enter and Escape the same way, and offer help about what you're doing.

Not one of those five is true of all of them. Roughly two dozen pick up the program's
colours; the four newest — the ones for picking and creating database files — don't, so in
dark mode they appear as glaring white system boxes. About half centre themselves over the
window they came from. About half stay out of the taskbar. Four out of twenty-nine offer
help. And Enter/Escape, as covered in the previous decision, is a free-for-all.

There is a shared "base" window that all of them are supposed to build on, but it does
almost nothing — it sets two colours and stops. So every other convention is something each
author had to remember, and the pattern of who remembered what shows the obvious result:
the newest windows follow the fewest conventions, because by then the convention was
folklore rather than code. Someone on this branch has just fixed two more windows that
opened disconnected from the main window — the same gap, found again, one at a time.

So the question isn't whether to have a shared foundation. It's *what kind*: a better
starting point that authors can still wander away from, a starting point plus an automatic
check that fails the build when someone does, or a more radical answer — stop making these
separate windows at all, and render them inside the main window where none of these
properties exist to get wrong.

### Alternatives

**A-15.1 — A rich base class.**
`BaseDialog` grows up: theme, owner, centre-on-owner, keep-out-of-taskbar, keyboard
semantics and an optional help slot, all established in the base. Authors derive from it
and get everything.

- *Pros.* Cheap, immediate, and fixes the large majority. Works on the existing codebase
  today without waiting for the redesign.
- *Cons.* Bypassable — and the evidence that convention alone decays is the four newest
  dialogs, which simply didn't derive from the base at all. A base class cannot fix a
  window that doesn't use it.
- *Unintended consequences.* Setting `Owner` in the base is subtler than it looks: owner
  must be assigned before the window is shown, and the base's best guess
  (`Application.Current.MainWindow`) is *wrong* for a dialog opened from another dialog,
  which should be owned by that dialog. A base that silently guesses wrong replaces a
  visible bug with an invisible one. Similarly, `ShowInTaskbar="False"` applied blindly
  would be wrong for the crash-report window, which may need to be findable when the shell
  is unresponsive.

**A-15.2 — Contract plus enforcement.**
The base class of A-15.1, plus an automated check — a Roslyn analyser or a reflection test
over the assembly — that every `Window`-derived type in the application derives from the
base and satisfies the invariants, with an explicit, commented opt-out list. CI fails
otherwise.

- *Pros.* The only alternative that makes the convention *stick*, which is the actual
  complaint. Directly prevents the "four newest dialogs" failure mode. The opt-out list
  doubles as documentation of the genuine exceptions.
- *Cons.* Maintenance cost, and false positives on legitimate exceptions. The exceptions
  are real: the unhandled-exception reporter must work when the main window is gone or
  broken, so it cannot be owned by it and probably must appear in the taskbar.
- *Unintended consequences.* Opt-out lists decay. Items are added under deadline and never
  removed, so the list needs a review discipline or it becomes the new "convention". The
  test also has to load the WPF assembly and instantiate or parse windows, which is
  awkward but precedented — the existing `UnitTests` project already references the WPF
  project directly.

**A-15.3 — Dialogs become content, not windows.**
A shell-hosted dialog service renders form-shaped dialogs into a Fluent `ContentDialog`
over the main window. There is no `Window` to configure, so owner, centring, taskbar
presence and theme cease to be per-dialog properties at all — they are properties of the
single host.

- *Pros.* Eliminates the entire class of defect structurally rather than policing it.
  Consistent theming, animation, dimming and keyboard handling for free. Enormous
  testability win: dialogs become view models that can be exercised without a UI thread or
  a window, which shrinks the FlaUI surface substantially. Matches modern Fluent
  conventions, which is the redesign's stated direction.
- *Cons.* Twenty-nine rewrites of mechanical but non-trivial work.
- *Unintended consequences.* Four that constrain it. (1) **It cannot be total.** The native
  file and folder pickers and the print dialog are OS windows and stay windows; so must the
  crash reporter, which has to work when the shell doesn't. So A-15.2's enforcement is still
  needed for the residue — A-15.3 shrinks the problem rather than deleting it. (2) **Modal-
  to-window means the user cannot move a dialog aside to read the data behind it**, which
  is a real behaviour change for long-lived windows like the attachment editor. (3)
  **Nested dialogs** (`AccountDialog` opening `OnlineAccountDialog`) are awkward in a
  single-host content-dialog model and need an explicit stacking design. (4) **It breaks
  every existing FlaUI test that finds a window by title** — a quantifiable migration cost
  against the existing Basics suite, not an abstract one.

### Panel

**Architect** — A-15.3 for the majority, A-15.2 governing an explicitly enumerated residue.
The register frames the decision as "base class an author can bypass, or analyser-enforced
contract", and the honest answer is that the *strongest* version of the contract is one
where the properties don't exist to be got wrong. But it cannot cover everything, so the
enforcement question doesn't go away — it just applies to five windows instead of
twenty-nine, which is a list small enough to review by hand.

**Developer** — A `IDialogService.ShowAsync(viewModel)` over WPF-UI's `ContentDialog` is
idiomatic and the migration can be incremental, dialog by dialog, with both models
coexisting. Worth stating plainly: `BaseDialog` today is *two lines* — two
`SetResourceReference` calls. There is no foundation here to build on; whatever is chosen
is new construction.

**Test Engineer** — A-15.3 is the single biggest testability improvement available in this
whole cluster. Dialog logic becomes view models testable in `UnitTests` with no UI thread,
which is where the real defects are (the empty `Apply()`, the shallow copy, the
`files[0]` loop, the attachment `Save` that deletes and doesn't rewrite). Against that:
it invalidates FlaUI tests that locate windows by title, and this repo has an established
Basics suite plus the `AppCrashGuard` work. That cost is real and should be budgeted, not
discovered. Under any alternative I want the A-15.2 check, because it is cheap and it is
the only thing that prevents recurrence.

**Operations Engineer** — No data or migration implications. One behavioural note for
A-15.3: a modal content dialog cannot be dragged off to one side, and some of these windows
are used for minutes at a time while referring to data behind them. That is a downgrade for
those specific windows and an argument for the mixed model.

**UI/UX Expert** — Strongly for A-15.3 for short forms — pick a category, confirm a merge,
enter a year — where a content dialog is better than a window in every respect. Strongly
*against* it for the attachment window and anything the user works in rather than answers.
Those want to be real, resizable, movable windows. So the rule must not be "all dialogs
become content dialogs"; it must be "forms become content dialogs, workspaces stay
windows", and that distinction should be made deliberately per dialog rather than by
default.

**Data Engine Expert** — Opting out entirely; nothing here touches storage.

**Adversarial Expert** — "One real shared base class" is the register's own framing and it
is the *cheap* answer, which is why it is attractive and why it will fail. This codebase
does not have a base-class problem; it has a **decay** problem — the four newest dialogs
ignored a convention that had existed for years. A-15.1 alone will produce exactly the same
document in 2029. So the choice is really between A-15.2 (make it stick on the current
architecture) and A-15.3 (make it structurally impossible for most of them), and that
choice is not a design question — it is a question about how much of this the owner intends
to rewrite. If all 29 are being rewritten anyway as part of a ground-up redesign, A-15.3 is
nearly free and obviously right. If the redesign is incremental, A-15.3 is 29 rewrites
dressed up as a convention fix, and A-15.2 is the honest answer.

**On help (D-51):** the panel agrees the contract should carry a *slot* for a help topic —
a nullable property — but should not *require* one until D-51 decides whether in-product
help exists. Requiring a topic before topics exist produces the current state, where four
windows have one and the reports panel points at the wrong topic entirely (B-48).

### Recommendation

**A-15.3 for form-shaped dialogs, A-15.2's enforcement for the residual real windows**,
with the split made deliberately rather than by default:

1. **Forms become content dialogs** hosted by a shell dialog service: short, answer-a-
   question windows (category, merge, recategorize, pick year, rename payee, confirmations).
   Owner, centring, taskbar and theme stop being per-dialog concerns.
2. **Workspaces stay real windows**: the attachment editor and anything else the user works
   *in* rather than answers, plus the technically-required exceptions (native file and
   print dialogs, the unhandled-exception reporter).
3. **The residue is governed by A-15.2** — a base class plus an automated check that every
   `Window`-derived type conforms, with a short commented opt-out list. The check is worth
   building **now**, on the current codebase, independent of the redesign's timing: it is
   cheap and it would have caught the four unthemed database dialogs and the two
   disconnected-owner windows fixed on this branch.
4. **Help is a nullable slot on the contract, not a requirement**, pending D-51.

**Needs your decision: yes — on sequencing, not direction.** The panel is confident that
A-15.3-plus-A-15.2 is the right end state, but the Adversarial Expert's point stands: the
choice between "do A-15.1/A-15.2 now on the existing dialogs" and "wait and do A-15.3 as
part of the rewrite" depends entirely on whether the redesign will in fact rewrite all 29
dialogs, which is a scope and timing call only the owner can make. The panel's advice in
either case is to build the enforcement check (item 3) immediately, because it is cheap and
it is valuable under both answers.

**Panel flags:** would benefit from **accessibility expertise**. Screen-reader and
keyboard-navigation behaviour differs materially between a real top-level `Window` and an
in-window `ContentDialog` — focus trapping, announcement of the dialog's arrival, and how
assistive technology exposes a modal overlay. None of the seven roles can judge whether
A-15.3 is neutral, better or worse for a user on a screen reader, and for a decision that
converts twenty-odd windows into overlays, that is not a detail.

---

## Summary

| ID | Recommendation | Needs your decision |
|---|---|---|
| D-8 | Shell-owned navigation contract; stable route ids; state scoped per money file; data-hiding filters restore only with a visible indicator | No |
| D-9 | Shell-provided filter affordance that surfaces opt into with a predicate; honest "nothing to search here"; global find is a separate future capability | No |
| D-10 | Delete the dead command now; if the capability is wanted, build it as a seeded editable query over payee *families*, results first | **Yes** — whether to build it at all |
| D-11 | Figure on the content surface for the selected category, computed on demand; never from the dormant `Category.Balance` column; no figure without a visible timeframe | **Yes** — on what a category total should include (bookkeeping semantics) |
| D-12 | Detect during a reconcile but present passively with no inline merge; state the flagging mode; adopt the noise-reduction rules regardless | No |
| D-13 | Edit-on-copy by default; enumerated immediate-apply exceptions for globally shared records; a generic cancel-fidelity test as the real enforcement | No |
| D-14 | One declared keyboard mechanism, enforced automatically; blast-radius rule requiring count *and* scope before a bulk commit; scoped undo as the preferred end state | No |
| D-15 | Forms become Fluent content dialogs; workspaces stay windows; enforcement check over the residue, built now | **Yes** — sequencing only |

**Opt-outs recorded:** the Data Engine Expert opted out of D-9, D-10, D-12, D-14 and D-15,
and contributed only a narrow caveat on D-8; the Operations Engineer opted out of D-9 and
D-10.

**Additional expertise flagged:** information-retrieval / record-linkage (D-10, if fuzzy
payee matching is pursued); personal-finance bookkeeping semantics (D-11); accessibility
(D-15).

**Cross-cluster dependencies this panel relied on:** D-2 (view state on domain objects)
constrains D-8 and D-11; D-49 (undo) constrains D-13 and D-14; D-51 (in-product help)
constrains D-15; D-50 (backup) sharpens D-14, since a bulk recategorisation today is
unrecoverable in a product with no reachable backup command.
