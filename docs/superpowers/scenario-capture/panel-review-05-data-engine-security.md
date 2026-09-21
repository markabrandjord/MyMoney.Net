# Panel Review 05 — `data-engine` and `security`

One expert-panel session over **twelve** decisions from the Redesign Decision Register:
the whole of the `data-engine` cluster (**D-32**–**D-39**) and the whole of the `security`
cluster (**D-40**–**D-43**).

| ID | Title | Cluster |
|---|---|---|
| D-32 | Should the user ever choose a storage engine? | `data-engine` |
| D-33 | Is there a power-user data surface, and what is it? | `data-engine` |
| D-34 | What does the concurrency guarantee actually cover? | `data-engine` |
| D-35 | Which storage engines does the redesign keep, and what must each do? | `data-engine` |
| D-36 | Auto-heal integrity errors, surface them, or keep ignoring them? | `data-engine` |
| D-37 | Is SQL Server a shipped configuration or a developer one? | `data-engine` |
| D-38 | Which SQL Server engine is the real one? | `data-engine` |
| D-39 | How does a schema upgrade work, and what is the user told? | `data-engine` |
| D-40 | One credential window for six jobs, or six windows? | `security` |
| D-41 | What may a shareable diagnostic log contain? | `security` |
| D-42 | Should the product trust a third-party institution directory? | `security` |
| D-43 | What does "password-protect my file" promise? | `security` |

**The panel:** Architect · Developer (C#/.NET/WPF-UI) · Test Engineer · Operations Engineer ·
UI/UX Expert · Data Engine Expert (SQLite + SQL Server) · Adversarial Expert.

**Scope note.** Issue #58 was read for context. Where a decision's honest answer is "that is
just the bug, fix it," the panel says so and moves on rather than re-litigating. Plain bugs
are referenced, not re-argued.

---

## Findings the panel established before arguing

The panel re-read the code behind these decisions rather than working from the register
alone. Four things came back different from the register's framing, and they change the
arguments below. They are recorded here once so each decision can reference them.

**F1 — Every engine except `XmlStore` and `CsvStore` inherits from `SqlServerDatabase`.**
`SqliteDatabase`, `SqlCeDatabase` and `SqlServerStoredProcDatabase` are all subclasses of
`SqlServerDatabase` (`SqlDatabase.cs:66`). This single naming/inheritance decision is the
root cause of several separate register entries, and it makes `is`/`as` tests against the
base class accidentally true for the default local store.

**F2 — The ad-hoc SQL console is live for ordinary SQLite users, and it can write.**
The register (following catalog B-23) says `Query ▸ Adhoc SQL Query` "silently does nothing
unless the books happen to be on a server." That is **not what the code does**.
`MainWindow.OnCommandAdhocQuery` guards with `this.database as SqlServerDatabase`, and by
F1 that cast **succeeds for `SqliteDatabase`** — the default local format. `SqliteDatabase`
overrides `QueryDataSet` with a real implementation. The console passes the typed text
straight to a data adapter, so `DELETE FROM Transactions` executes against the user's live
books; because the statement returns no `Results` table the dialog then shows nothing,
giving no indication anything happened, while the in-memory model still holds the deleted
rows. This is a live data-loss hazard on the default configuration, not a server-only
curiosity. It materially raises the stakes of D-33.

**F3 — `DbFlavor.SqlServer` is the fall-through case for any unrecognised file name.**
`DatabaseLifecycle`'s extension switch (`DatabaseLifecycle.cs:163`) ends with
`else { return DbFlavor.SqlServer; }`. A path that is not `.xml`, `.bxml`, `.sdf`, `.db` or
`.mmdb` is treated as a *SQL Server catalog name*. So the raw-SQL `SqlServerDatabase` engine
is reachable by accident, not only by deliberate configuration. This matters to D-37 and D-38.

**F4 — The repository already states the D-37 answer, in a build file.**
`MyMoney.csproj` copies `SqlScripts/**/*.sql` under `Condition="'$(Configuration)' == 'Debug'"`,
with the comment: *"Release never ships them; SQL Server stays a DEBUG-only engine."* The two
findings that produced D-37 (debug-only `DataEngineStartup`, no release script copy) are
therefore **consistent and deliberate**, not two independent oversights. D-37 is a ratification,
not an investigation.

Two smaller corrections, used below: `SqlServerDatabase.Save(MyMoney)` is `public void`, not
virtual — so **SQLite inherits the whole-file save path verbatim**, including both defects in
#58 (D-34). And `SqliteDatabase.GetTableSchema` does `sql.Replace(",", "\r\n")` before parsing,
which would split a `decimal(8,12)` type across two lines; it is harmless today only because
no column in the model declares a precision (D-39).

---

## D-32 — Should the user ever choose a storage engine?

**From:** Phase 2 OQ3 · **Cluster:** `data-engine`

### ELI10

When you make a new set of books, the program has to keep them somewhere — the same way a
photo can be saved as a JPEG or a PNG. Right now the program has five different ways of
keeping your money records, and they are not equally good: some can save one changed line
instantly, some have to rewrite the entire file every time you change anything, some can
notice when another copy of the program changed the same thing, and some can't.

In the version you can buy, the program never asks — it just picks the good one. In the
version the programmers use, a hidden menu appears that lets you pick. Nobody ever decided
which of those is the real product.

The consequence of getting this wrong is not obvious on day one. If the program asks you to
pick, you will pick something based on the name, and a year later you'll discover your books
are in the slow format that can't warn you when your spouse's copy overwrote your entry. If
the program never asks, then the day you *do* want to keep the books somewhere your whole
household can reach, there's no way to say so.

### Alternatives

**A. One store, never a choice.** The product has exactly one way of keeping books (a local
file). Every other format becomes an import or export target reachable from `File ▸ Export`,
never from `File ▸ New`. The debug submenu is deleted rather than promoted.

- **Pros.** Nothing to explain, nothing to get wrong, one code path to test and to make fast.
  Every capability in the product can be assumed present — no feature has to ask "does this
  store support that?" Removes the entire class of "the user is on the format that can't do X"
  support problem. Smallest surface for the redesign to carry forward.
- **Cons.** Forecloses household sharing unless it is added later as something other than a
  storage choice. The owner's own working setup (a SQL Server instance) becomes, formally, a
  development configuration rather than a supported one.
- **Unintended consequences.** Quietly makes D-34 much smaller: if books are always one local
  file, "two copies editing at once" means "the same file opened twice on one machine," which
  a file lease solves without any per-row version counter. It also removes the main
  justification for the `IDatabase` abstraction itself — and an abstraction kept alive with
  one implementation tends to rot, so the redesign must either delete it or consciously keep
  it as a seam for testing. Finally, it makes a future "move to a server" feature *harder*
  to retrofit than designing for it now, because nothing in the UI will have a concept of
  "where the books live."

**B. Choose a *place*, never an engine.** `File ▸ New` offers two plainly-worded options —
"On this computer" and "Shared with my household" — described entirely in terms of
consequences ("everyone in the house can open these books at once; needs a server on your
network"). The engine name never appears. Adding a third place later is a new option, not a
new technology.

- **Pros.** Matches how a person actually thinks about the question. Honest about the real
  trade-off (sharing vs. simplicity) without making anyone learn what SQLite is. Gives the
  capability differences a place to be disclosed at the moment they matter. Consistent with
  modern Windows conventions — Fluent-style setup flows describe outcomes, not components.
- **Cons.** Only worth building if there is genuinely more than one place, which is D-37's
  call. If SQL Server stays developer-only this option is a one-item list, which is worse
  than no list. Requires the shared option to actually work in a release build, which today
  it does not (F4).
- **Unintended consequences.** Committing to "shared" as a user-visible promise makes
  multi-user concurrency a *product requirement*, which drags D-34 from "nice to have" to
  "must be correct," and drags the credential-handling defects in #58 from "developer
  inconvenience" to "release blocker." It also creates a support obligation the project may
  not want: a household SQL Server that won't start is now the product's problem.

**C. Start local, offer a move later.** Everyone starts on the single local store, with no
question asked. A separate, explicit action — `File ▸ Move these books to a shared server…` —
performs a one-time migration, with its own wizard, its own prerequisites check and its own
explanation. The choice exists but is never on the critical path of creating books.

- **Pros.** New users are never blocked by a decision they can't yet make. The sharing
  capability is discoverable exactly when a user goes looking for it. Migration is a first-class,
  testable operation instead of an implicit consequence of a radio button. Keeps the
  "one store" simplicity for the 95% case while preserving the seam.
- **Cons.** Requires a real, tested data-migration path (local file → server catalog),
  which the product does not have today; "Save As to a different engine" is not the same
  thing and does not carry the version columns or the credential registry entry. More work
  than A, and the work is in the least-exercised part of the codebase.
- **Unintended consequences.** A one-way migration needs a "move back" story or users will be
  stranded; building both directions doubles the cost. It also creates a second class of
  books in the recent-files list (files vs. registry entries), which the UI already handles
  inconsistently (`OpenRegisteredDatabase` vs. `LoadDatabase`) and would have to unify.

### Panel discussion

**Data Engine Expert** opened by rejecting the premise that this is a storage question at all:
the five engines are not five equally-valid choices, they are one real store (SQLite), one
half-finished server story, one legacy format the product can no longer create (SQL CE), and
two serialisation formats wearing a store's interface. Presenting that as a menu was never
defensible — which is exactly why it is `#if DEBUG`.

**Architect** agreed and pushed on the seam question: whichever option wins, the *interface*
should stop pretending all five are peers (that is D-35). The decision here is narrower —
what appears in `File ▸ New`.

**UI/UX Expert** was unequivocal that a technology name must never appear in a new-file flow,
and that if a choice exists at all it must be option B's phrasing. They noted that today's
release behaviour (no question) is already the right *shape*; the risk is a redesign
"improving" it by surfacing the debug menu because it looks like a feature.

**Operations Engineer** pointed at F4: the shared option cannot be offered in a release build
today, because the SQL scripts the bootstrap reads are not copied outside Debug. Offering B
without fixing that ships a broken menu item.

**Developer** noted that A is nearly free (delete a conditional menu), B is moderate, and C is
the expensive one because it requires a real migration. In a redesign already carrying a full
UI rewrite, spending the budget on a migration wizard for a capability nobody has asked for
is hard to justify.

**Test Engineer** observed that A collapses a combinatorial test matrix — every feature
currently has to be reasoned about against five engines, and most of that reasoning has never
been done. C reintroduces the matrix but confines it to one tested operation.

**Adversarial Expert** made the case the panel had to answer: "you are about to recommend A
and quietly kill the only reason this codebase has an abstraction layer — and the product
owner personally runs the server configuration. That is not a neutral simplification, it is
deleting the owner's own setup." They also flagged that C's "move later" is the option that
*sounds* best and is most likely to be half-built and abandoned, exactly like the server
bootstrap already was.

The panel converged on this: **`File ▸ New` should not ask an engine question under any
alternative** — that much is unanimous and does not need the owner. What is genuinely open is
whether a *place* choice exists at all, and that is D-37's question, not this one.

### Recommendation

**Alternative A as the default, with option C's seam deliberately preserved but not built.**
Concretely: `File ▸ New` creates the single local store with no question, in every build
configuration; the debug engine submenu is deleted; the `IDatabase` seam survives as the
place a future "shared books" story would attach (and as the test seam), with a written note
that it has one supported implementation.

If D-37 comes back "SQL Server is shipped," this recommendation changes to **B**, and option
C's migration becomes the honest way to reach it — but the panel is not recommending B on its
own initiative, because B's cost is entirely a consequence of D-37, not of this decision.

### Needs-your-decision flag

**Partially — deferred to D-37.** The panel is confident recommending that no engine choice
ever appears in `File ▸ New`. Whether a *place* choice exists at all is not this panel's call
and is flagged under D-37.

**Resolved via the D-37 owner decision** — see "Owner decision" under D-37. SQL Server is
developer/DEBUG-only, so no place choice is surfaced to end users; this alternative-A
recommendation stands as written.

---

## D-33 — Is there a power-user data surface, and what is it?

**From:** Phase 2 OQ7 · **Cluster:** `data-engine`

### ELI10

Hidden in the menus there is a box where you can type instructions in the computer's own
database language and it will do them to your money records. It was put there so the
programmer could look at things quickly.

Two problems. First, most people can't write in that language, so as a "ask a question about
my money" feature it helps almost nobody. Second — and the panel checked this in the code,
because the written notes said otherwise — **that box works on ordinary people's ordinary
files, and it can destroy things**. If you type an instruction that deletes every transaction,
it deletes every transaction, shows you a blank result, says nothing, and the program keeps
acting as though nothing happened.

So the real decision is not "should power users get a toy." It is: the program currently has
a loaded gun on the menu, and separately it has no good way for a curious person to ask their
own questions. Those are two different things and the redesign should answer them separately.

### Alternatives

**A. Delete the free-form console from the product; add nothing.** The menu items go. The
capability survives only behind a developer/diagnostics flag that shipped builds do not expose
(or not at all). "Ask a question of my data" is served by the existing advanced search and by
export-to-spreadsheet.

- **Pros.** Removes the live hazard in F2 immediately and completely, at near-zero cost.
  Honest: the product never had an end-user query story, and pretending the SQL box was one
  flatters it. Export-to-CSV plus a spreadsheet is a genuinely better power-user answer than a
  read-only SQL grid with no charting, sorting or saving.
- **Cons.** Takes away the only escape hatch for "the UI won't show me what I know is in
  there," which is occasionally the thing that saves a user. Loses a real (if niche)
  diagnostic tool for the maintainer.
- **Unintended consequences.** `Query ▸ Show Last Update` (the statement log) sits on the same
  menu and is genuinely useful for "did it actually save?" — deleting the whole menu would
  take that with it, so the log viewer has to be re-homed somewhere (Help ▸ Diagnostics) or it
  disappears by accident. Also removes the only current consumer of `IDatabase.QueryDataSet`
  and `GetLog`, which then become dead interface members that D-35 should delete.

**B. Safe structured query surface, built on the existing query vocabulary.** The redesign
promotes `Query.cs`'s field/operator model — the same vocabulary the advanced search already
uses — into a first-class, saveable, named-query surface: build a question from fields and
conditions, see the matching transactions, save it, re-run it, export it. No SQL anywhere.

- **Pros.** Serves the actual user goal ("find everything where…") for people who cannot write
  SQL, which is most of them. Reuses machinery that already exists and is already exercised by
  the register's search box. Results are *domain objects*, so they can be acted on — recategorised,
  exported, charted — which a raw result grid cannot do. Naturally read-only; the hazard
  cannot exist. Fits Fluent/WPF-UI patterns well (a filter-builder flyout is a solved UI shape).
- **Cons.** It is a feature, not a fix — real design and build cost. It will never answer the
  genuinely arbitrary question ("how many payees have no transactions in three years"), so a
  determined power user is still stuck. Scope creep risk is high: a query builder can absorb
  unlimited effort.
- **Unintended consequences.** A saveable named query is, functionally, a report — so this
  overlaps the `reports-charts` cluster and risks building a second, parallel reporting
  concept. The redesign should decide whether a saved query *is* a report definition, or the
  product ends up with two. It also creates a persistence problem (where do saved queries
  live? in the money file? then they are one more thing to version and migrate).

**C. Read-only diagnostics console, behind an explicit developer mode.** The free-form box
survives, but: opened through a connection that cannot write, refusing non-`SELECT` statements
outright rather than executing them silently; engine-uniform (an engine that cannot answer
says so instead of returning the whole file, fixing B-105/B-106 by honesty rather than by
implementation); reachable only when the user has turned on a "developer tools" setting, and
labelled as unsupported.

- **Pros.** Keeps the escape hatch and the maintainer's tool. Cheap. Turns the two engine bugs
  into a one-line capability check. Honest labelling means nobody mistakes it for a feature.
- **Cons.** "Read-only" over SQLite is easy to *say* and easy to get wrong (attaching a second
  read-only connection is the reliable way; pattern-matching the statement text is not). A
  half-enforced read-only console is more dangerous than an obviously dangerous one, because
  users trust it.
- **Unintended consequences.** A developer-mode toggle is a new product concept that will
  attract other things (the `Edit ▸ Cleanup` diagnostics from Phase 2 OQ2, the GC/Environment
  dumps), which is arguably good — but it means the redesign now owns a "developer mode"
  surface with its own discoverability and support questions.

### Panel discussion

**Adversarial Expert** led, and reframed the decision around F2: "the register treats this as
a taste question about power users. It is not. The default configuration currently ships a
write-capable SQL console that reports success by showing nothing. Whatever else is decided,
that is not a redesign question, it is a thing to stop doing." No one disagreed.

**Data Engine Expert** confirmed the mechanism and added that the reason the guard fails is F1
— `as SqlServerDatabase` was written when that cast meant "this is a server," and then SQLite
was made a subclass of it. The same latent mistake exists anywhere else in the codebase that
type-tests against the base class, and the redesign should grep for it rather than fix this
one site. They also noted that `QueryDataSet`'s contract ("returns a `DataSet` named Results,
or `null`") is what makes a destructive statement silent: a non-query returns no table, which
the dialog renders as nothing at all.

**UI/UX Expert** argued that A and B are the real product answer and C is an engineering
convenience: "the user goal underneath 'ad-hoc SQL' is *I know this is in there and your UI
won't show it to me*. The fix for that is a better search and a better export, not a better
console." They supported B strongly and noted the filter-builder pattern is well-served by the
target library.

**Developer** costed it: A is an afternoon; C is a few days if read-only is enforced by
connection rather than by parsing; B is weeks and touches the report story.

**Test Engineer** pointed out that B is the only alternative whose correctness is *testable in
the domain layer* — query definitions in, transactions out, no UI needed. A raw SQL console is
essentially untestable in any useful sense, which is part of why its current behaviour went
unnoticed for so long.

**Architect** supported B's direction but flagged the report overlap as a genuine structural
risk: "if a saved query and a report definition are different types, we have built the same
feature twice, and the reports cluster is deciding its own version of this in parallel."

**Operations Engineer** — *nothing to add here* beyond noting that a developer-mode toggle
would want to be off by default in any packaged build, which is routine.

### Recommendation

**Do A immediately and B as the product capability; use C only if the maintainer wants the
tool back, and only with connection-enforced read-only.**

Sequenced: (1) remove the free-form SQL console from the shipped menus now — it is a live
hazard, not a design question; (2) re-home the statement log under a diagnostics surface so it
is not lost; (3) design the structured query surface as an extension of advanced search, and
**coordinate with the `reports-charts` panel so a saved query and a report definition are the
same concept**, not two.

The panel is confident about (1) and (2). On (3) it is recommending a direction, not a scope.

### Needs-your-decision flag

**No for the removal; yes for the scope of the replacement.** Deleting the write-capable
console is a safety fix the panel recommends without reservation. *How far* the structured
query surface goes — a filter builder, versus saveable named queries, versus a full
user-defined-report story — is product taste and budget, and it should be decided jointly
with the reports cluster rather than here.

---

## D-34 — What does the concurrency guarantee actually cover?

**From:** Phase 9 OQ2 + Phase 9 OQ23 · **Cluster:** `data-engine` · *Defect instances: #58*

### ELI10

Imagine two people writing in the same notebook from different rooms. The program has a
system to stop them scribbling over each other: every entry carries a little number, and when
you go to change an entry the program checks the number is still what you last saw. If it
isn't, someone else got there first, and you get told.

That system works when you change **one** entry. It does **not** work when you press Save and
the program writes everything at once — for that path it neither checks the number nor
updates it. Which is backwards, because "I sat here for an hour and then saved everything" is
precisely when someone else is most likely to have changed something in the meantime.

There's a second half. Some things in your books aren't entries in their own right — the
individual lines inside a split transaction, the units inside a rental building. They don't
get their own number; they're supposed to be protected by their parent's. One of them — rental
units — isn't protected by anything at all, and gets written even after its parent building
has already been deleted.

The bug is a bug and is already written down. What isn't decided is what the promise actually
is: does "we'll warn you if someone else changed this" apply to everything you can change, or
only to some things, and is the program even trying to support two people at once?

### Alternatives

**A. One write path, optimistic concurrency everywhere.** Delete the per-collection
`UpdateXxx` whole-file save entirely. Saving becomes "apply every dirty aggregate through the
batch path," which already checks and increments versions, and already defers in-memory
side-effects until after commit (`postCommitActions` — the pattern #58 says exists and is
unused). There is no second way to write.

- **Pros.** Both #58 defects (lost-edit-on-failed-commit, and the missing version check) are
  fixed by *deletion*, not by adding a second correct implementation. One path means one thing
  to test. The design document's claim — "the version column is the guarantee" — becomes true
  instead of aspirational. Makes saving faster on large files as a side effect, because a full
  save stops rewriting unchanged rows.
- **Cons.** The whole-file path is also the *initial* write path (a brand-new file, an import
  of a whole Quicken history) where per-row batching of 50,000 transactions may be
  substantially slower than a bulk insert. Needs a deliberate bulk-load path that is explicitly
  exempt (a fresh, empty target cannot conflict with anyone).
- **Unintended consequences.** Removes the only code path that works on `SqlServerDatabase`,
  `SqlCeDatabase`, `XmlStore` and `CsvStore`, since `SaveOne`/`SaveBatch` throw
  `NotImplementedException` on all of them. So this alternative **forces D-35's hand**: engines
  that cannot batch stop being stores. That is arguably correct, but it should be a conscious
  consequence, not a surprise. It also means the in-memory model's dirty-tracking becomes
  load-bearing in a way it currently isn't — a bug in `ChangeType` tracking silently becomes
  a bug in what gets persisted.

**B. Ratify aggregate-root granularity and make the boundary real.** Keep versions on the
eleven aggregate roots and deliberately not on children — but enforce the boundary:
a child change bumps its parent's version, a child is only ever written inside a
parent-scoped save, and `RentUnits` is folded into the building's aggregate so
`SaveRentUnitsForBuilding` cannot run outside a version-checked parent write (which also
removes the write-after-parent-DELETE hole).

- **Pros.** The existing design document is right in principle; this makes the implementation
  match it. Aggregate-root granularity is the correct model for this domain — a split line has
  no independent meaning, and a conflict on it is a conflict on its transaction. Fewer version
  columns than the alternative of versioning everything, and conflicts are reported at a
  granularity a user can understand ("this transaction changed," not "line 3 of this
  transaction changed").
- **Cons.** "Bump the parent when a child changes" is easy to describe and easy to miss one
  call site of. It needs to be structural (children cannot be saved except through the parent's
  save) rather than a convention, or it will decay.
- **Unintended consequences.** Coarser conflicts mean more *false* conflicts: two people
  editing different split lines of the same transaction now collide where they needn't. For a
  household product that is fine; for anything busier it isn't. It also means the conflict
  *resolution* UI has to show whole-transaction differences, which is more UI than a single
  changed field.

**C. Drop optimistic concurrency; take a lease on the books instead.** When you open a set of
books you take an exclusive (or explicitly-read-only) lease. A second copy is told "these
books are open on MARK-DESKTOP; open read-only?" No per-row versions, no conflict resolution
UI, no merge semantics.

- **Pros.** Dramatically simpler, and it delivers the *user-visible* promise ("don't let me
  lose work to another copy") more reliably than optimistic concurrency does, because it
  prevents the situation rather than detecting it afterwards. For a single-household product
  where simultaneous editing is rare-to-never, it matches reality. Removes the version column,
  the version migration, and the entire conflict-detection surface. Trivially testable.
- **Cons.** Genuinely blocks a legitimate case (two people, two accounts, same evening). Leases
  need expiry and a break-the-lease story, or a crashed process locks the books until someone
  deletes a file. On a network share, advisory locking is unreliable.
- **Unintended consequences.** Forecloses any future web/mobile companion, where concurrent
  access is the normal case rather than the exception. Also quietly makes the whole-file save's
  #58 lost-edit defect *the only* remaining save-integrity concern — which is good, but it
  means that defect must be fixed regardless of which alternative wins, and it should not be
  allowed to hide behind "we're changing the concurrency model anyway."

### Panel discussion

**Data Engine Expert** led on the mechanics and made the sharpest point: because
`SqlServerDatabase.Save(MyMoney)` is a non-virtual `public void`, **SQLite inherits it
verbatim** — so this is not a SQL Server problem that a local user is insulated from. The
default local store has both #58 save defects. They favoured A without hesitation: "there is
a correct implementation of this in the same folder, with a comment explaining why it is
correct, and the other path ignores it. That is not a design decision, that is finishing the
job." On B they noted that the aggregate boundary is defensible and that `RentUnits` is simply
outside it by mistake.

**Architect** supported A and B together as one answer — A is the path question, B is the
granularity question, and they are orthogonal. They stressed that B must be *structural*: if
a child can be saved without its parent, someone will do it.

**Adversarial Expert** pressed hard on whether any of this is warranted: "who is the second
user? This is a personal-finance app being evaluated as a Quicken replacement by one person.
You are about to spend the redesign's budget making a multi-writer concurrency model correct
for a scenario that may never occur once. C gives the same user-visible promise for a tenth of
the effort, and C's failure mode — 'the books are already open' — is one every user already
understands from Excel." They also noted that optimistic concurrency without a *conflict
resolution UI* is only half a feature: detecting a conflict and then showing the user a
message box saying "someone else changed this" is not obviously better than preventing it.

**UI/UX Expert** agreed with that last point and said it plainly: today the promise resolves
to a warning dialog, and a warning dialog whose only options are "overwrite" and "lose my
edit" is a bad experience. If A/B is chosen, the redesign owes a real reconcile view, and that
is not small.

**Operations Engineer** noted the deployment asymmetry: C works identically on a local file
and a server; A/B needs the version migration script to have been applied, which today names a
specific catalog (`USE MyMoney;`) and covers eleven tables. They also flagged that if D-37
makes SQL Server developer-only, "two copies at once" reduces to "the same file opened twice
on one machine," which C handles perfectly and A/B is overkill for.

**Test Engineer** was split. A/B is testable — the project has already demonstrated it can
write a genuine concurrency-conflict regression test (a real deadlock was reproduced when
designing the cross-thread marshalling tests). But testing C is nearly free, and testing A/B
properly means multi-process fixtures, which are slow and flaky.

**Developer** costed A as moderate (mostly deletion plus a bulk-load exemption), B as small,
and C as small but with an unpleasant tail (lease expiry, crash recovery, network shares).

### Recommendation

**A + B, conditional on D-37.** If SQL Server is a shipped configuration, the panel recommends
alternative A (one write path, through the version-checked batch path) combined with B
(ratified aggregate-root granularity, enforced structurally, `RentUnits` folded into its
building). If SQL Server is developer-only, the panel recommends **C** — a file lease — and
that the version column be kept only as far as it is already built, not extended.

**Genuine unresolved disagreement, recorded rather than papered over.** The Adversarial Expert
and the Operations Engineer do not accept A+B even under a shipped-SQL-Server outcome: their
position is that concurrent editing is a scenario nobody in this product's actual user
population has ever hit, that optimistic concurrency without a reconcile UI is a half-feature,
and that the budget belongs in the save-integrity defect (#58's first bullet) which affects
every user on every save. The Data Engine Expert and Architect hold that the mechanism is
already three-quarters built and correct, and abandoning it now wastes more than finishing it.
The panel did not resolve this.

**Regardless of which wins:** the lost-edit-on-failed-commit defect (#58, first bullet) must be
fixed. It is not a concurrency question — it destroys a single user's work on a single machine
with no second copy anywhere — and it must not be allowed to wait on this decision.

### Needs-your-decision flag

**Needs your decision.** The question underneath is "is 'more than one person editing these
books at the same time' a promise this product makes?" That is a product-scope call the panel
cannot make, it is tightly coupled to D-37, and the panel genuinely split on it.

**Resolved via the D-37 owner decision** — see "Owner decision" under D-37. SQL Server is
developer-only, so alternative C (a file lease) is what the shipped SQLite path needs; A+B
remains the target specifically for the developer-only SQL Server engine, not a shipped-product
requirement.

---

## D-35 — Which storage engines does the redesign keep, and what must each do?

**From:** Phase 9 OQ12 · **Cluster:** `data-engine`

### ELI10

The program can keep your books in five different formats. They look interchangeable from the
outside — the program has one list of things a format must be able to do — but three of the
five simply refuse to do several of them. Ask one of those three to save a single changed
transaction and it throws its hands up; ask an XML file to answer a question about your data
and it hands back the entire file and pretends that was the answer.

Nobody is told. If you choose the XML format, every single edit rewrites your whole file, you
get no warning when another copy changed something, and the "ask a question" box lies to you.
The program's own list of required abilities is really a wish list.

The decision is which formats survive, and — more importantly — what the minimum is. Something
that can't meet the minimum isn't a place to keep books. It might still be a perfectly good
*file format* to export to and import from, which is a different job.

### Alternatives

**A. One store, several formats.** Exactly one thing is a *store* (SQLite, plus a server store
if D-37 says so). Everything else — XML, binary XML, CSV, SQL CE — becomes a format: something
`File ▸ Export` writes and `File ▸ Import` reads, whole-file, with no pretence of being a
place to work. The type system says so: an `IDataStore` interface with the full capability
floor, and a separate, much smaller `IDataFormat` with just load-and-save.

- **Pros.** The single most clarifying change available in this cluster. `NotImplementedException`
  as a capability signal disappears — you cannot pass a format where a store is required.
  Every feature can assume the floor. B-105 and B-106 (query and log return nothing on
  XML/CSV) stop being bugs because those members stop existing on those types. Honest to the
  user: exporting to XML obviously doesn't give you a live workspace; "saving as XML" does not
  obviously tell you that.
- **Cons.** Existing users of `.xml`/`.bxml` as their working file — if any exist — lose that
  and must convert. SQL CE (`.sdf`) must remain *readable* for legacy files, so there is one
  awkward case: a read-only legacy store, which is neither cleanly a store nor cleanly a format.
- **Unintended consequences.** `BinaryXmlStore` is the only thing that encrypts (D-43), so
  demoting it to an export format quietly decides part of D-43: file encryption becomes a
  property of *exports*, not of the working file — unless the working store gains its own
  encryption. That is a real coupling and the two decisions must be taken together. Also,
  81% of `XmlStore.cs` is a complete, unused binary-XML serializer whose own header says it is
  "not in use" — demoting the file makes deleting that 1,338 lines obviously safe, which is a
  pleasant second-order win.

**B. Keep all five, publish a capability matrix, and make the UI honest.** Each engine declares
what it supports (`SupportsPerRecordSave`, `SupportsConcurrency`, `SupportsQuery`, …), the UI
disables or explains what is unavailable, and choosing a limited format shows a plain-language
warning up front.

- **Pros.** Nothing is taken away. The capability-declaration pattern is a real improvement
  over `NotImplementedException` regardless of which alternative wins. Maximum backwards
  compatibility.
- **Cons.** Preserves the combinatorial problem it documents: every new feature now has to
  answer "what does this do on XML?" forever. A capability matrix is a way of *describing* an
  inconsistent product, not of fixing one. Users will still choose the bad option and be
  unhappy — a warning dialog at creation time is read once and forgotten.
- **Unintended consequences.** Capability flags tend to leak into the UI as disabled controls
  with no explanation, which users experience as the product being broken. And the flags
  multiply: today's five capabilities become fifteen as features are added, at which point
  nobody can reason about any given combination — which is the situation the audit just
  spent nine phases discovering.

**C. Minimum viable: one store, delete the rest outright.** SQLite only. XML, binary XML, CSV
and SQL CE are removed entirely, with a one-time conversion utility for anyone holding an old
file.

- **Pros.** Smallest possible surface. Deletes thousands of lines (the binary XML serializer,
  the SQL CE reflection shim, `XmlCsvReader`'s 1,058 unreachable lines, `CsvStore`). Fastest to
  a maintainable baseline.
- **Cons.** Removes a genuine capability — "my books are a plain text file I can read, diff and
  keep in git" is a real thing some users value, and XML export also serves as a
  disaster-recovery format that does not depend on a SQLite library existing in ten years.
  Also removes the only path off the product, which is a bad look.
- **Unintended consequences.** Losing a human-readable serialisation removes the most useful
  debugging tool the project has for "what is actually in this file." And with no plain-text
  export, the product becomes harder to leave, which in a personal-finance tool is a trust
  issue disproportionate to the code involved.

### Panel discussion

**Data Engine Expert** described the current portfolio bluntly: one real store (SQLite), one
server store that is unreachable through the normal path (D-38), one legacy read-only format
(SQL CE, which no "create new" path even offers), and two serialisers. "There is no portfolio.
There is one store and four things that have been given a store's interface." They supported A
and argued the interface split is the fix, independent of which formats survive.

**Architect** agreed and made the structural case: the `IDatabase` interface is the problem
statement. A single interface whose members throw on most implementations is not an
abstraction, it is a union type pretending to be a contract. Splitting it is the change that
prevents the next five years of the same drift, and it is worth doing even if the portfolio
decision changes later.

**UI/UX Expert** supported A over B on the grounds that capability matrices are a designer's
way of avoiding a decision: "a warning at creation time is not informed consent, it is a
liability waiver. If a format cannot do the things the product promises, the product should
not offer it as a place to keep books."

**Test Engineer** strongly supported A: the interface split converts a five-way matrix into
one store's full test suite plus a trivial round-trip test per format. They also noted that
the format round-trip test is genuinely valuable and largely missing today.

**Developer** noted the split is mechanical and low-risk (the call sites are few), and that A
makes a large amount of dead code obviously deletable — which matters for a redesign trying to
establish a maintainable baseline.

**Operations Engineer** insisted on one thing across all alternatives: whatever survives must
have a *working, tested backup*, and today three engines' `Backup` can't overwrite and the
command isn't even reachable from the UI (Phase 2 OQ5). "Backup belongs in the floor, and it
is currently the floor's weakest plank."

**Adversarial Expert** defended keeping plain XML, and won a concession: "you are about to
delete the only format a human can read. When SQLite's file gets corrupted — and it does — the
XML export is how someone reconstructs their life. Keep it, but keep it honestly: as an export,
never as a workspace." They also challenged the panel to say what the floor actually *is*,
rather than gesturing at one.

The panel then wrote the floor down explicitly, which was the most productive part of the
discussion:

**Capability floor for anything that is a *store*:**
1. Save a single changed record without rewriting everything.
2. Participate in whatever concurrency guarantee D-34 settles on.
3. Load and save atomically — a failed save leaves both the file and the in-memory model
   unchanged (#58's first bullet).
4. Produce a backup copy that can safely overwrite an existing one.
5. Bring an older file's shape up to date on open (D-39).
6. Report what it did, for diagnostics (the statement log).

**Capability floor for a *format*:** round-trip the full model faithfully, and nothing else.

### Recommendation

**Alternative A, with the interface split as the load-bearing change.** One store
(SQLite; plus the server store if and only if D-37 says so, in which case it must meet the
floor above — today it does not). `XmlStore` survives as a documented export/import format,
explicitly for portability and disaster recovery. `CsvStore` is already unreachable dead code
and should be deleted outright, not demoted. `BinaryXmlStore` is demoted to an encrypted
export container and its decision is taken jointly with D-43. `SqlCeDatabase` is
read-only-legacy: it can open an existing `.sdf` to convert it, and nothing else.

Unanimous. The one point the panel wants on record is that **backup belongs in the floor** and
is currently the weakest item in it.

### Needs-your-decision flag

**No.** The panel is confident. The only variable — whether the server store is in the
portfolio — is D-37's, and the floor above applies to it either way.

**That variable is now resolved via the D-37 owner decision** — see "Owner decision" under
D-37. SQL Server is in the portfolio as a developer-only configuration, physically isolated
into its own DLL per the owner's directive; the floor above applies to it unchanged.

## Owner guidance

Follow-up guidance from the owner, given after the D-37 decision above, in the owner's own
words:

> It might make the most sense to start rebuilding the new system by building the data layer
> DLLs, as well as the app and business layers of the test subsystem first. That way we can
> ensure that we have the test processes to drive the happy path and the error path of the data
> layer. An installation will be either SQLite, or it will be SqlServer, and if we were to
> consider an alternative db, it would be exclusively that. The brainstorming of the data layer
> design should consider where common control logic and import/export and reporting output logic
> should live. Other than that, I leave it to you.

**Implementation-sequencing guidance, for the future implementation plan.** When redesign
implementation actually begins, the data-layer DLLs and the test-subsystem infrastructure for
the app and business layers should be built first, ahead of other work — specifically so
working test processes exercising both the happy path and the error path of the data layer
exist from the start, rather than being added after the fact.

**Single-engine-per-installation constraint — reinforces and sharpens D-37's DLL-separation
directive.** Any single installation of the product runs exactly one storage engine: SQLite or
SQL Server, never both, and never a runtime choice between them within one running instance. If
an additional alternative engine is ever added in the future, it too would be an exclusive
per-installation choice, not a third option alongside a hybrid. Where D-37's directive
established that the SQL Server and SQLite engines must be physically separable into different
DLLs, this sharpens that to: a given deployed installation only ever has one of them
present/active at all.

**Open question — unresolved, explicitly deferred to the future data-layer design session.**
Where should common control logic, import/export logic, and reporting-output logic live in the
new architecture — the data layer, the business layer, or somewhere else? The owner has flagged
this as something that future design/brainstorming work on the data layer needs to address; it
is not answered here.

---

## D-36 — Auto-heal integrity errors, surface them, or keep ignoring them?

**From:** Phase 9 OQ3 · **Cluster:** `data-engine` · *Defect instances: #58*

### ELI10

Every single time the program opens your books, it checks them for a specific kind of damage:
a transfer between two accounts where only one half survives, a transaction that claims to be
a transfer to somewhere that doesn't exist, a transaction that somehow has two transfers when
it should have one. It finds these, writes them all down in a list, hands the list back — and
whoever asked for it throws it in the bin without looking.

There is even a method sitting there called "heal," meant to repair these. It is empty. It has
a little joke comment in it where the repair code should be.

So: the program knows your books are damaged, on every single open, and has never once told
you. The decision is what to do instead — quietly fix it, show you and let you decide, or
check only when you ask.

### Alternatives

**A. Repair silently on load.** Implement `Heal` properly: re-link the survivable ones,
demote unmatchable transfers to ordinary transactions, drop duplicate transfer links. Write
what was done to the log. The user sees nothing.

- **Pros.** The books are always consistent. No new UI. Nothing for a non-technical user to
  understand or act on. Fastest to ship.
- **Cons.** Silently modifying a person's financial records without telling them is very hard
  to defend, and this is money. A "repair" is a guess: demoting a broken transfer to an
  ordinary transaction changes what an account's balance means, and no algorithm can know
  whether the missing half was deleted deliberately or lost.
- **Unintended consequences.** Hides the *cause*. These errors are produced by something —
  probably a bug in transfer deletion (the panel notes `RemoveTransfer` is already documented
  as asymmetric) or an interrupted save. Auto-repair removes the evidence, so the underlying
  defect never gets found and the repair runs forever. It also makes the errors untestable in
  production: nobody can answer "how often does this happen?" because nothing records that it
  happened. And a repair on load makes the file *different* after merely opening it, which
  breaks the reasonable expectation that opening is read-only and interacts badly with
  read-only/lease scenarios (D-34, alternative C).

**B. Surface a review list the user acts on.** Errors are collected as they already are, and
a non-modal indicator appears ("3 problems found in your data"). Opening it shows each problem
in plain language, points at the transaction involved, and offers the specific repairs that
make sense for that problem, with a "fix all the obvious ones" for the easy class. Nothing
changes until the user says so.

- **Pros.** Honest. The user keeps control of their own records. Turns a class of silent
  corruption into something visible and fixable — which is the entire point of the Phase 1
  scenario ("have broken transfers found and reported") that is currently only half true.
  The problems are genuinely explainable in ordinary words. Fits Fluent conventions well
  (an InfoBar, not a dialog).
- **Cons.** Real UI work that doesn't exist today. Needs each error type to have a
  human-readable explanation and a defensible set of offered repairs — which is design work,
  not just code. Risks alarming a user about something that has never affected them.
- **Unintended consequences.** Once there is a "problems in your data" surface, everything
  else will want to live in it (orphaned splits, categories with no transactions, unbalanced
  splits, currencies with no rate) — which is good, but it means designing a general
  data-health concept rather than a transfer-specific one, and that is a bigger commitment
  than it first appears. It also creates an obligation: if the product shows a problem it
  cannot offer a fix for, that is worse than not showing it.

**C. Check on demand only.** No load-time surfacing. A `Tools ▸ Check my data` command runs
the validation and shows the results, the way a disk check works. Load-time detection still
happens (it is free) and still writes to the log, but nothing interrupts.

- **Pros.** Zero risk of alarming anyone. Cheapest UI. Clear mental model borrowed from a
  thing users already understand. Keeps opening a file genuinely read-only.
- **Cons.** Approximately nobody will ever run it. A problem that requires the user to suspect
  a problem before they can find it is, in practice, still not reported.
- **Unintended consequences.** Pushes the discovery burden onto support ("run Check my data
  and tell me what it says"), which is fine for a maintainer-supported tool and useless for a
  solo user.

### Panel discussion

This was the shortest argument of the session and the closest to unanimous.

**Data Engine Expert** set the frame: "the detection is done, it is correct, and it costs
nothing — it already runs on every load. The entire decision is what happens to a list that
is currently thrown away. That makes this the cheapest thing in the whole cluster and
probably the highest ratio of user value to effort."

**Adversarial Expert** made the case for A and then withdrew it themselves: "my instinct is
'just fix it, users don't want a chore list' — but I can't defend silently altering someone's
financial records. And the killer argument is the cause: if you auto-heal, you will never find
out why transfers are breaking, and something *is* breaking them." They then challenged B
from the other side: "a badge saying 'your data has problems' with no explanation a normal
person can act on is worse than silence. If you build B, every error type needs a sentence a
human understands and a fix that is obviously right — otherwise you have just invented anxiety."

**UI/UX Expert** accepted that constraint and proposed the shape: non-modal, dismissible, never
on the critical path of opening a file; plain language per item ("This transfer to *Savings*
is missing its other half — the matching entry in Savings isn't there"); offered actions
phrased as outcomes, not operations. They were firm that it must not be a modal dialog on
load, which is how this kind of feature usually gets built and usually gets hated.

**Architect** noted the generalisation risk from B's unintended consequences and argued to
design it as a general data-health surface from the start but *populate it only with the
errors already detected* — the extensibility is nearly free if designed in, and retrofitting
it is not.

**Test Engineer** pointed out the happy accident that the errors are already unit-tested (the
tests are the only current consumer of the list), so B mostly needs UI tests, and the
validation logic itself already has coverage to build on.

**Operations Engineer** — *nothing substantial to add*, except that whatever is surfaced should
also be written to the application log so a support conversation can start from evidence.

**Developer** costed B as the InfoBar plus a list view plus per-error-type repair commands —
genuinely small by the standards of this redesign.

### Recommendation

**Alternative B, combined with C's explicit command.** Collect on load as today; surface
non-modally and never block opening; provide a plain-language review list with per-item
repairs and a "fix the unambiguous ones" bulk action; **and** provide a `Check my data`
command so the check can be run deliberately. Never modify records without consent.

`DataError.Heal` should be **deleted**, not implemented — repairs belong as explicit,
named, per-error-type operations on the domain model, not as a polymorphic "heal" that
encourages exactly the silent-mutation behaviour the panel is rejecting.

Design it as a general data-health surface that currently holds one family of errors. Log
every detection regardless of whether the user acts.

### Needs-your-decision flag

**No.** Confident. This is the cheapest high-value item in the cluster and the panel found no
serious argument against the recommended shape.

---

## D-37 — Is SQL Server a shipped configuration or a developer one?

**From:** Phase 8 OQ24 + Phase 9 OQ22 · **Cluster:** `data-engine`

### ELI10

Besides keeping your books in a file on your own computer, the program can keep them on a
proper database server — the kind a small business would run — so more than one person in the
house could use them at once. There's a lot of machinery for this: it can set a bare server up
from scratch, invent its own logins, install everything it needs.

Except: in the version people actually install, the pieces it needs to do that setup **aren't
included in the box**, and it doesn't remember a server set of books when you restart, so
you'd have to go find it through the menus every single time.

The panel found something the written notes didn't: this isn't an accident. The build file
says so out loud — *"Release never ships them; SQL Server stays a DEBUG-only engine."* Somebody
already decided. What was never done is telling anyone, or cleaning up all the things that
still behave as though it were a real, supported option.

The consequence matters a lot for everything else. If server storage is real, then several
serious credential and injection problems become release blockers and the two-people-at-once
machinery has to be correct. If it's a developer's tool, most of that work evaporates.

### Alternatives

**A. Ratify developer-only, and finish the job.** SQL Server is a development and testing
engine, stated as such in the docs. The release build removes every user-facing trace: no
server entries in the recent-books list, no `File ▸ Add User`, no registry-driven open path.
`DbFlavor.SqlServer` stops being the fall-through for unrecognised extensions (F3). The
credential exposures in #58 that only exist on this path become development-hygiene items
rather than release blockers.

- **Pros.** Matches what the build already does and what the code already says (F4), so it is
  the cheapest path to internal consistency. Retires a large surface of security problems
  (plaintext passwords in `dataengine.config.json`, the `sp_addLogin` injection with its
  `sysadmin` grant, the half-bootstrap credential loss) from "must fix before shipping" to
  "fix when convenient" — a genuinely large reduction in redesign risk. Simplifies D-32
  (no place choice), D-34 (concurrency collapses to one machine) and D-35 (one store).
- **Cons.** The product owner's own setup is a SQL Server instance, so this formally demotes
  the configuration they personally use. It also throws away substantial completed work —
  the stored-procedure engine, the access-script surface, the bootstrapper — or rather,
  relegates it to a test fixture.
- **Unintended consequences.** Retiring the sharing story is easy to do and hard to undo: the
  UI concepts (a registry of named databases, a places list) would be deleted and would have
  to be reinvented. It also removes the project's only *integration* test target that exercises
  a real client/server boundary, which has been useful. And the security defects, though no
  longer release blockers, are still live on the owner's own machine — "developer-only" is not
  "nobody's data."

**B. Promote to a shipped configuration.** SQL Server becomes supported: scripts ship in
release, `DataEngineStartup` reconnects in release, the bootstrap path gets a release test,
and the credential and injection defects in #58 are fixed as release blockers (Credential
Manager or DPAPI instead of plaintext JSON; parameterised login creation without a `sysadmin`
grant; bootstrap that persists credentials before it creates them).

- **Pros.** Delivers the household-sharing capability, which is a real differentiator against
  a single-file product. Justifies and completes the persistence-concurrency work already done
  (D-34 alternative A becomes obviously correct). Honest about a path that the code otherwise
  half-supports.
- **Cons.** Substantial: a release-tested server bootstrap means testing against real SQL
  Server editions on machines the project doesn't control. "Install SQL Server" is a support
  burden far out of proportion to a personal-finance app. Every one of the #58 security items
  becomes mandatory and some are not small. A wrongly-built bootstrap can leave logins on a
  user's server that nobody knows the password to.
- **Unintended consequences.** Shipping a product that creates SQL Server logins with generated
  passwords makes the project responsible for credential lifecycle — rotation, recovery,
  revocation when a household member leaves — none of which exists. It also drags in the
  `sysadmin` grant question: `AddLogin` currently makes every shared user a server
  administrator, and fixing that properly means designing a real permission model, not just
  escaping a string.

**C. Advanced, opt-in, explicitly unsupported.** Ships in release, but behind a setting the
user has to find and turn on, with documentation that says plainly "this is for people who run
their own database server; it is not supported and we will not help you set one up." The
security fixes that are cheap get done; the expensive ones are documented as limitations.

- **Pros.** Keeps the capability alive and available to the handful of users who want it
  without a support commitment. Preserves the owner's setup as a first-class thing. A middle
  road that doesn't delete work.
- **Cons.** "Unsupported but shipped" is a fiction: users will find it, use it and ask for
  help. Worse, shipping a known SQL-injection path and plaintext credentials with a disclaimer
  is not a defensible security position — a disclaimer does not make an injected `sysadmin`
  grant acceptable.
- **Unintended consequences.** Creates a second-class configuration that every future feature
  must be reasoned about against, without the testing budget to actually do so — which is
  precisely the situation the audit found the product in today. Also tends to be the outcome
  that produces the *next* audit finding, because nobody owns it.

### Panel discussion

**Operations Engineer** led, and opened with F4: "this is already decided, in a build
condition, with a comment. What we are being asked is whether to make the rest of the product
agree with a decision that has already been taken."

**Data Engine Expert** supported ratifying A, with one sharp qualifier: "developer-only does
not mean the defects are acceptable. The `AddLogin` injection grants `sysadmin` on a real
server; the fact that only a developer can reach it means it is a smaller *blast radius*, not
a smaller *bug*. And it is on the owner's own machine." They also raised F3 as a defect that
must be fixed under every alternative: a mistyped filename currently gets interpreted as a
server catalog name.

**Architect** pointed out how much this decision unlocks. Four other decisions in this cluster
(D-32, D-34, D-35, D-38) are waiting on it, and three of them get substantially simpler under
A. "This is the highest-leverage item in the cluster and it should be answered first."

**Adversarial Expert** argued the other way, deliberately: "you are all reasoning from a build
comment. A build comment is somebody's decision from a Tuesday, not a product strategy. The
owner runs SQL Server. The project has built a stored-procedure access layer, a bootstrapper,
a credential registry and a concurrency design *for this*. Ratifying developer-only doesn't
simplify the product, it writes off maybe five thousand lines of deliberate work because the
`<Content>` item is in the wrong `ItemGroup`." They pressed further: "and what is the actual
alternative story for a household? 'Put the file on a network share'? That is worse — SQLite
over SMB is a known corruption risk, and nobody has decided whether the product even allows
it."

That last point landed and the panel had no good answer to it. It is recorded below.

**UI/UX Expert** noted that under A the product needs an honest answer to "can my spouse and I
both use this?", and "put the file on OneDrive" is not one — it is actively dangerous for a
SQLite file.

**Test Engineer** observed the practical consequence either way: under A the SQL Server path
stays as a test fixture and must keep working; under B it needs a release-configuration
integration test against a real server, which the project has the setup for (the owner's
instance) but which cannot run unattended in the way this project's tests are expected to.

**Developer** costed A as small (delete UI paths, fix the fall-through, move scripts out of
the shipping story) and B as the largest single item anywhere in these twelve decisions.

### Recommendation

**Needs your decision — but the panel's default is A.** On the evidence, the project has
already chosen developer-only, and A merely makes the rest of the product consistent with
that. Six of seven panel members would ratify A if forced to choose today, primarily because
B's security obligations are large, mandatory and not currently met.

Two things the panel insists on regardless of the outcome:

1. **F3 is a defect under every alternative.** `DbFlavor.SqlServer` must stop being the
   fall-through for unrecognised file names. A typo currently routes a user into the
   raw-SQL server engine.
2. **"Developer-only" does not downgrade the #58 security items to "won't fix."** They are
   live on the owner's machine. They move from release blocker to ordinary bug, not to
   non-issue. The `sp_addLogin` injection with its `sysadmin` grant in particular should be
   fixed or the code path deleted.

And one question the panel could not answer, which the owner should: **if SQL Server goes,
what is the honest answer to "can two people in my house use this?"** The panel's view is that
the only acceptable answers are "no, one at a time" (D-34 alternative C, a file lease) or
"yes, via a server" (B here). "Put the file on a network share or OneDrive" is not an
acceptable answer and the product should actively warn against it.

### Needs-your-decision flag

**Needs your decision — and it should be answered first.** D-32, D-34, D-35 and D-38 all
change shape depending on it. It is not a technical question: it is "is this a single-user
product?"

## Owner decision

**Ratified: developer/DEBUG-only (Alternative A).** SQL Server is not a shipped configuration.
It remains a developer and testing engine, available in DEBUG builds, and is not exposed to
end users in a release build.

**In the owner's own words:**

> Right now I am the only one interested in SQL Server. I think it will be useful enabling
> multi-user scenarios in the future, and enabling AI agents to be running in parallel with me.
> But the people on the repository that I subscribed to 'MoneyTools' are more interested in the
> ease of use of a single user scenario. So for now this is a developer, DEBUG release only
> feature. This feature could be disabled by conditional compile directives, and having the SQL
> Server and the SQLite engines in different DLLs in the data layer.

**Architectural directive beyond the panel's proposal.** Alternative A as the panel wrote it
leaned on `#if DEBUG` guards and deleting user-facing traces from the release build. The owner
is requiring something stronger: **physical separation** of the SQL Server engine and the
SQLite engine into different DLLs within the data layer, so the SQL Server engine can be
excluded from a release build or deployment outright — not merely conditionally compiled
inline. `#if DEBUG` alone is not sufficient; the released assembly set should not contain the
SQL Server engine at all.

The owner's stated reason for keeping the SQL Server engine alive at all, rather than deleting
it, is to leave the door open for future multi-user scenarios and for running AI agents in
parallel with the owner themselves. That framing is why D-34's more thorough concurrency work
(alternative A+B) still has a home — as the target for the SQL Server engine specifically, not
as a near-term requirement of the shipped product.

**Downstream effects:**

- **D-32 (engine choice for the user).** Holds as the panel recommended: alternative A, no
  engine choice ever surfaced to end users. `File ▸ New` asks no engine question in any build
  configuration; the debug engine submenu stays debug-only — and, per the DLL-separation
  directive, becomes physically absent from a release build rather than merely hidden.
- **D-34 (concurrency guarantee scope).** The panel's recommendation was conditional — "A+B if
  SQL Server ships, otherwise C, a file lease." Since SQL Server does not ship to end users,
  **C (a file lease) is what the shipped SQLite path needs.** The more thorough A+B
  (a version-checked batch save path, aggregate-root-level concurrency granularity) can still
  be the target specifically for the SQL Server engine, in service of the owner's stated future
  multi-user/parallel-agent goal — but it is now explicitly a developer-configuration concern,
  not a blocking requirement for the redesign's default shipped path. (The #58
  lost-edit-on-failed-commit defect remains a must-fix regardless, as the panel already noted.)
- **D-35 (which engines survive).** SQLite is confirmed as the shipped floor. SQL Server
  survives as a developer-only configuration, physically isolated per the DLL-separation
  directive above.
- **D-38 (which SQL Server engine is the real one).** Unchanged recommendation — the
  stored-procedure engine (alternative A) — now clearly scoped as developer/future-multi-user
  tooling rather than a near-term shipping concern; its priority drops accordingly, as the
  panel already anticipated.

This reframes the panel's own "can two people in my household use this at once?" question
raised earlier in this section: it is not a near-term shipped-product requirement. It is
explicitly one of the multi-user/parallel-agent futures the owner is deliberately keeping the
door open for via a physically separable engine, not a load-bearing feature of the default
product.

---

## D-38 — Which SQL Server engine is the real one?

**From:** Phase 9 OQ4 · **Cluster:** `data-engine`

### ELI10

There are two completely separate pieces of code for talking to a database server. One writes
out database commands as text and sends them. The other only calls a fixed set of pre-installed
routines on the server — which is much safer, because the account the program uses day to day
isn't even allowed to touch the tables directly.

The careful one is three thousand lines and is the whole reason all those pre-installed
routines exist. The text-writing one is the one you actually get when you open a server
database the normal way.

So the safe engine is mostly unreachable, and the one you get is the one that builds commands
by gluing text together — which is exactly how the apostrophe-in-a-memo bug and the
"adding a user runs whatever text you typed" bug happen.

Somebody has to say which one is real. Almost everything else about server storage depends on
the answer.

### Alternatives

**A. The stored-procedure engine is the only engine.** Delete `SqlServerDatabase`'s role as a
concrete engine. Extract its genuinely generic parts (schema reflection, table mapping, the
shared reader loops) into an abstract base — which also ends the absurdity of `SqliteDatabase`
inheriting from a class named after SQL Server (F1). `DbFlavor.SqlServer` constructs the
stored-procedure engine. Administrative operations (create catalog, bootstrap, add login) move
out of `IDatabase` entirely into a separate, clearly-named admin client, because they are not
storage operations.

- **Pros.** Fixes a remarkable number of separate register and issue entries *by deletion*:
  the `DBString` line-break corruption (P9 OQ17), the unescaped-field save failure (P9 OQ18),
  the `sp_addLogin` injection (#58) and the `Create Database {0}` unbracketed interpolation all
  live on the raw-SQL path and stop existing. Makes the access-script surface and the
  least-privilege design actually load-bearing instead of decorative. The base-class extraction
  fixes F1, which in turn fixes D-33's accidental cast and any other latent
  `as SqlServerDatabase` test.
- **Cons.** The bootstrap genuinely needs elevated, non-stored-procedure operations (you cannot
  call a procedure that doesn't exist yet to create the database that will hold it). So
  "delete the raw-SQL engine" really means "move its few legitimate uses somewhere honest,"
  which is more work than deleting a file.
- **Unintended consequences.** The abstract-base extraction touches every engine, including
  SQLite, which is the one thing all users depend on — a refactor with real regression risk
  in the least forgiving part of the product. It should be done with the storage test suite in
  place first, not alongside. It also removes `SqlCeDatabase`'s base implementation, which is
  fine only because D-35 demotes SQL CE to read-only legacy.

**B. The raw-SQL engine is the real one.** Delete the stored-procedure engine, grant the
day-to-day login direct table rights, and fix the escaping defects in place (parameterise
everything, which the base class already supports via `SupportsParameterizedUpdate` — it is
simply `false` for this engine).

- **Pros.** Deletes 3,075 lines and sixteen SQL script files. Simpler deployment: no procedure
  deployment step, no bootstrapper, no script-copying build condition (which is F4's whole
  problem). Parameterising the raw path is mechanical and well-understood.
- **Cons.** Discards the least-privilege model deliberately — the day-to-day account would now
  be able to do anything to the tables, so a bug (or an injection that survives the
  parameterisation pass) has unlimited reach. Reverses the persistence-concurrency design
  that the version-column migration and the `*_SaveBatch` procedures exist to implement, which
  is recent, deliberate work.
- **Unintended consequences.** Killing the stored-procedure engine also kills the only working
  implementation of `SaveOne`/`SaveBatch` on the server side — so D-34's alternative A would
  have to be rebuilt on the raw path, undoing more than it deletes. The apparent saving is
  illusory.

**C. Keep both; make `DbFlavor` distinguish them.** `DbFlavor.SqlServerDirect` and
`DbFlavor.SqlServerStoredProc`, chosen by configuration.

- **Pros.** No deletion, no refactor, smallest immediate change. Preserves the direct engine
  for administrative work where it is genuinely needed.
- **Cons.** Doubles the thing the register is complaining about: two engines, two sets of
  defects, two test matrices, and a user-facing (or config-facing) choice between them that
  nobody can make meaningfully. It is the "capability matrix" answer from D-35 applied to a
  single storage backend, and it fails for the same reason.
- **Unintended consequences.** Enshrines the raw-SQL path as supported, which means its
  escaping defects must be fixed and kept fixed forever, rather than deleted. Also leaves F1
  unaddressed — SQLite still inherits from a concrete SQL Server engine — so the accidental
  type-test class of bug stays live.

### Panel discussion

**Data Engine Expert** was unequivocal for A and did most of the talking. "The
stored-procedure engine is the one that matches the security model the project deliberately
built: the day-to-day login has no table rights. The raw engine cannot even work against that
login — so what you get from `DatabaseFactory` is an engine that would fail against the
server the bootstrapper sets up. That is not two competing designs, it is one design plus a
leftover." They added that the escaping defects are symptomatic, not root: any hand-built SQL
string path will regrow them, so parameterising the raw engine (B) fixes today's instances and
not the class.

**Architect** framed the deeper point: the root cause is F1 — `SqlServerDatabase` is
simultaneously an interface implementation, a concrete engine and the shared base class for
three unrelated engines. Those are three jobs. Splitting them is the change, and the
"which engine is real" question mostly dissolves once they are separated. They also supported
moving administrative operations out of `IDatabase`, noting that `AddLogin`, `Create` and
`Delete` (of a whole catalog) are not things a *storage engine* should expose — which is
exactly why the injection bug is reachable from a menu.

**Developer** agreed and warned about sequencing: the base-class extraction is the riskiest
refactor in this cluster because SQLite rides on it. Test suite first.

**Test Engineer** endorsed that ordering and noted the silver lining — a proper abstract base
makes the storage contract testable once against all implementations, which is D-35's floor.

**Adversarial Expert** probed B seriously: "three thousand lines is a lot to keep for a
configuration that may be developer-only. If D-37 says developer-only, why not delete the
stored-procedure engine and keep the simple one?" The Data Engine Expert's answer carried the
room: the stored-procedure engine is the one that *works* against the least-privilege login
the bootstrapper creates, and it is the only server-side implementation of per-record save. B
deletes the working engine and keeps the broken one.

**UI/UX Expert** — *nothing to add here.* This is entirely internal; the only user-visible
consequence is which defects exist, and that is not a design question.

**Operations Engineer** noted one real point in C's favour and then withdrew it: procedure
deployment is an operational step that direct SQL avoids. But under D-37's likely outcome
(developer-only), that step is a developer's problem, not an operational one.

### Recommendation

**Alternative A, unanimously.** The stored-procedure engine is the real one. Extract the
genuinely shared machinery into an abstract base so that no engine inherits from a concrete
SQL Server implementation (fixing F1 and, with it, D-33's accidental cast). Move
administrative operations — create catalog, drop catalog, add login — out of `IDatabase` into
a separate admin client that is not a storage engine at all; this is the structurally correct
home for the bootstrap's legitimate need for direct SQL, and it removes the injection surface
from the storage interface entirely.

If D-37 returns developer-only, this remains the right answer but drops in priority — it
becomes a cleanup rather than a security fix.

**Sequencing:** the storage contract test suite (D-35's floor) should exist before the
base-class extraction, because that refactor touches SQLite.

### Needs-your-decision flag

**No.** Confident and unanimous. The only variable is priority, which follows D-37.

**That variable is now resolved via the D-37 owner decision** — see "Owner decision" under
D-37. SQL Server is developer-only, so this recommendation drops to cleanup priority rather
than a near-term security fix.

---

## D-39 — How does a schema upgrade work, and what is the user told?

**From:** Phase 9 OQ9 + Phase 9 OQ10 · **Cluster:** `data-engine`

### ELI10

As the program learns to record new things, it needs to change the shape of the container your
books are kept in — add a new slot, rename one, remove one. When you open an older file, it
does this automatically.

For the most common change, SQLite can't just alter the container, so the program builds a new
empty one, copies everything across, **destroys the original**, and renames the new one into
its place. The list of what to copy comes from a hand-written reader that squints at a line of
stored text and tries to work out what the columns are. If that reader gets it wrong, the copy
is incomplete — and then the original is destroyed anyway. There is exactly one safety check:
if it found *no* columns at all, it stops. If it found *some* of them, it goes right ahead.

The panel found the reader is more fragile than the notes said: before parsing, it replaces
every comma in the stored text with a line break, which would chop a type like `decimal(8,12)`
in half. That doesn't bite today only because no column in this program happens to use one.
It is a landmine, not a bug — waiting for someone to add a column with a precision.

There's a second, unrelated half. The program has a mechanism for asking permission before a
one-way conversion — and it is switched off for four of the five formats, one of them with a
"try/catch" that returns the same answer either way, so it does nothing at all.

### Alternatives

**A. Versioned migration scripts, checked in and tested.** The database carries a schema
version number. Each change is a numbered, checked-in migration file with a test. On open, the
product applies any missing migrations in order, inside a transaction. The reflective
"compare the model to the file and work out the difference at run time" machinery is retired.

- **Pros.** Every schema change is reviewable as a diff before it ever runs on a user's file,
  which is the single biggest safety improvement available here. Deterministic and reproducible:
  the same file always upgrades the same way. Testable — a migration test applies it to a
  fixture of the previous version and asserts the result. The SQL Server side already works
  this way, so the two engines would finally agree. Makes "what version is this file?" a
  question with an answer.
- **Cons.** More ceremony per change: adding a property to the domain model now requires
  writing a migration rather than just working. That friction is exactly what makes it safe,
  and exactly what makes developers skip it. Needs a baseline migration that can adopt every
  existing file shape in the wild.
- **Unintended consequences.** Reflection-driven DDL currently means the schema *cannot* drift
  from the model — the file is always brought into line with the code. Under A they can drift:
  someone adds a property and forgets the migration, and the mismatch surfaces at run time as a
  missing column. This needs a guard (a start-up assertion, or a test that reflects the model
  and compares it to the migrated schema) or A trades one failure mode for another.

**B. Keep reflective DDL, fix the introspection.** Replace the hand-written `CREATE TABLE`
scanner with `PRAGMA table_info` — SQLite's own structured answer to "what columns does this
table have," which needs no parsing at all. Use SQLite's modern `ALTER TABLE … DROP COLUMN`
(3.35+) and `RENAME COLUMN` (3.25+) so the destructive table rebuild is needed far less often.
Take an automatic backup copy of the file before any rebuild. Add tests for the rebuild path.

- **Pros.** Cheap, and it removes the actual hazard: the mis-parse that precedes a `DROP TABLE`
  becomes impossible because there is no parse. `PRAGMA table_info` is the obvious right answer
  and the hand-written scanner appears to predate the SQLite versions that make it unnecessary.
  Keeps the zero-ceremony developer experience. The pre-rebuild backup is a few lines and
  converts the worst case from "data lost" to "data recoverable."
- **Cons.** Still means a user's file is restructured by logic that was decided at run time and
  never reviewed. Still no answer to "what version is this file?" Drift is impossible but so is
  auditability.
- **Unintended consequences.** Relying on `DROP COLUMN` sets a minimum SQLite version (3.35,
  2021), which interacts with the fact that the project currently references *two* SQLite
  packages and uses only one — that needs resolving first or the effective version is unclear.
  A pre-rebuild backup also needs a cleanup policy or it will quietly accumulate copies of the
  user's entire financial history next to the original.

**C. Hybrid — generate, review, then apply only what was reviewed.** The reflection machinery
still computes the difference between the model and the schema, but at *build* time, emitting
a proposed migration script that a human reviews and checks in. At run time only checked-in
scripts are applied. (This is how Entity Framework migrations work, and developers know the
pattern.)

- **Pros.** Keeps the convenience (you don't hand-write the SQL) and gains the safety (nothing
  runs on a user's file that a human hasn't read). Solves A's drift problem, because the
  generator is the thing that detects drift. Familiar shape to any .NET developer.
- **Cons.** The most machinery of the three: a build-time generator, a migration store, a
  version table, and the discipline to run the generator. Most likely of the three to be built
  half-way.
- **Unintended consequences.** Introduces a build-time code-generation step into a project that
  currently has none, which affects CI and the "just open it in Visual Studio" story.

**The user-communication axis (orthogonal to all three).** Four positions: say nothing for
changes that keep the file readable by older versions (today's behaviour for four engines);
always tell the user afterwards; ask before anything one-way; always take an automatic backup
before touching the file regardless of what is said.

### Panel discussion

**Data Engine Expert** led with the technical finding and was blunt about it: "the scanner
should not exist. SQLite has `pragma table_info` and has had it forever — it returns name,
type, nullability and default as rows. There is no reason to read `sqlite_master.sql` as text,
and the `Replace(\",\", \"\\r\\n\")` line means this parser cannot handle any parameterised type.
It survives because the schema happens to have none. Add one `decimal(10,2)` and the rebuild
silently drops columns and then drops the table." They also flagged that the `select sql from
sqlite_master where tbl_name = '…'` query returns a row per object (indexes too) and takes the
first via `ExecuteScalar`, which is order-dependent.

**Test Engineer** picked up the "no test coverage named" point and sharpened it: "a code path
whose failure mode is `DROP TABLE` on real user data, with no test, is the highest-risk thing
in this cluster measured by consequence. Whatever else is decided, a fixture-based test that
upgrades a real old-format file and asserts every row survived is not optional."

**Operations Engineer** made the point that won the communication half: "argue about mechanism
all you like — take a copy of the file first. It is a `File.Copy` before a destructive
operation on irreplaceable data. Every alternative should include it, and it makes the
mechanism choice much less frightening."

**Architect** favoured C as the destination and B as the immediate step, noting that A's drift
risk is real and under-appreciated: "reflection-driven DDL has one genuine virtue — the schema
cannot be wrong. Throwing that away for auditability is a trade, not an upgrade."

**Developer** agreed, and noted B is a few hours' work — swapping a parser for a `PRAGMA` — and
removes the sharpest edge immediately, which is a strong argument for doing it regardless of
the longer-term direction.

**UI/UX Expert** took the communication axis: opening a file must not become a dialog. The
right shape is silent for additive changes (the user gains nothing from being told a column
was added), an unobtrusive after-the-fact note for anything structural, and an explicit,
clearly-worded confirmation *only* where the file becomes unreadable by an older version —
which is the one case where declining is a rational thing to want. They also flagged
`UpgradeRequired` as badly named: it reads as "does this file need upgrading" and actually
means "does upgrading this file make it one-way." Renaming it is half the fix.

**Adversarial Expert** questioned whether any of this matters: "how often does the schema
actually change? Look at the history — rarely. You are proposing a migration framework for a
handful of changes a decade." The Test Engineer's answer settled it: frequency is irrelevant
when the failure mode is total data loss and the safety check is `if (first)`. The Adversarial
Expert accepted that and redirected: "then do the cheap safety fix now and stop designing a
framework." That became the recommendation's shape.

### Recommendation

**B immediately, C as the destination, with mandatory pre-upgrade backup and corrected
communication.**

1. **Now (cheap, high value):** replace the hand-written `CREATE TABLE` scanner with
   `PRAGMA table_info`; use `ALTER TABLE … DROP COLUMN` / `RENAME COLUMN` where the SQLite
   version allows, so the destructive rebuild becomes rare; **take an automatic copy of the
   file before any rebuild**; add fixture-based upgrade tests that assert row-for-row survival.
   Resolve the duplicate SQLite package references so the minimum version is knowable.
2. **Destination:** move to generated-but-reviewed versioned migrations (C), sharing the
   versioned-script model the SQL Server side already uses, with a schema-version row in the
   file.
3. **Communication:** silent for additive, non-destructive changes; an unobtrusive after-the-fact
   note for structural ones; explicit confirmation *only* for genuinely one-way conversions.
   Rename `UpgradeRequired` to say what it means (`RequiresOneWayConversion`) and implement it
   honestly per engine — `SqlServerDatabase`'s current version returns `false` from both
   branches of its `try`/`catch` and exists only for its connection side-effect, which is a
   bug to delete rather than a behaviour to preserve.

### Needs-your-decision flag

**No.** The panel is confident. The immediate fix is uncontroversial and the destination is a
standard pattern. The only judgement call — how much the user is told — the panel resolved on
the principle that a user should be interrupted exactly when they have a decision to make,
and not otherwise.

---

## D-40 — One credential window for six jobs, or six windows?

**From:** Phase 4 OQ1 · **Cluster:** `security`

### ELI10

There is one window in this program that asks for passwords. It is used for six completely
different things: putting a password on your own file, proving you know that password,
signing in to your actual bank, answering your bank's extra security questions, entering a
security code, and changing your password. It's the *same* window each time, with different
words written into it at the last second.

Those are not the same job. Typing a password to unlock your own file on your own computer is
almost risk-free. Typing your bank login sends it over the internet to whoever the program
thinks your bank is. But they look identical, so you learn "this window means type a
password" — and one day something that looks exactly like it appears, and you will type your
bank password into it without a second thought. That's how people get robbed.

There's something worse that the window design can't fix: your bank username and password are
kept in your books file in ordinary readable columns. So this decision is about the window, but
the window is not where the biggest problem is.

### Alternatives

**A. Split by trust domain: two families of window, never confusable.** One family for the
product's own secrets (the file password, and changing it) — plain, local, product-branded.
A second family for third-party credentials (bank sign-in, the extra-security questionnaire,
the security code) — visually distinct, always showing which institution is being signed in to
and the exact address the credentials will be sent to, with different chrome that the local
family never uses.

- **Pros.** Directly attacks the phishing training problem: the user learns that "the window
  with the bank's name and address at the top" is a different, higher-stakes thing. Showing
  the destination address is the single most useful anti-phishing affordance available and
  costs almost nothing. Each family can be designed for its real audience.
- **Cons.** Two designs to build and maintain. Masking, clipboard behaviour, paste handling and
  accessibility have to be right in both — and getting them right *twice* is how one of them
  ends up wrong.
- **Unintended consequences.** Once the bank family displays the endpoint, the product has
  implicitly promised that endpoint is trustworthy — which it is not, because it comes from an
  unauthenticated third-party directory over plain HTTP (D-42). So A only works if D-42 is
  resolved with it; showing a URL the product can't vouch for is theatre. The two decisions
  should be taken together.

**B. One component, typed configurations, distinct presentations.** A single audited
credential control with a *typed* configuration — a `LocalFilePassword` case, a
`ThirdPartySignIn` case, and so on — instead of runtime prose rewriting and dynamically added
fields. One implementation of masking, clipboard and accessibility; several deliberately
different presentations driven by which case it is.

- **Pros.** The security-sensitive mechanics are implemented and audited exactly once, which is
  the strongest argument for today's unified window and the one worth preserving. Removes the
  actual smell — it is not that the window is shared, it is that subclasses mutate it at
  runtime by adding controls and rewriting prose, which makes it impossible to reason about
  what any given instance looks like. Idiomatic in the target library: one control, several
  templates. Maps naturally to a WPF-UI `ContentDialog` with per-case content.
- **Cons.** A shared component drifts toward the lowest common denominator — the bank case
  needs things (institution identity, endpoint display, "this goes over the internet" framing)
  that the local case must not have, and a shared control makes it easy to give both or
  neither.
- **Unintended consequences.** Typed configurations make the set of credential prompts *closed*,
  which is good discipline but means an unanticipated future prompt (a new MFA style, an OAuth
  redirect) needs a new case rather than a runtime tweak — that is the right trade, but it will
  feel like friction the first time it bites.

**C. Delegate third-party sign-in out of the product.** Bank credentials are never typed into a
product-drawn window at all: the sign-in happens in a hosted browser view against the
institution's own page (OAuth-style), and the product keeps only a token. The in-app credential
window then has exactly one job — the local file password.

- **Pros.** The correct long-term answer, and where the industry has gone: the user types their
  bank password into their bank's own page, with the browser's own address bar and certificate
  indicator doing the authentication work no in-app window can do. The product stops holding
  bank passwords at all, which also disposes of the stored-credential problem. Reduces the
  credential window to a single trivial case.
- **Cons.** Requires the institution to support it. OFX direct connect — which is what this
  product speaks — predates OAuth and mostly does not. So C is not implementable against the
  current protocol for most institutions; it is a statement about where to go, not a design
  that can ship.
- **Unintended consequences.** Adopting C partially (some institutions OAuth, others password)
  produces exactly the inconsistency A is trying to remove, and arguably worse: the user now
  sees two kinds of bank sign-in and cannot tell which is legitimate. Partial adoption may be
  worse than none.

### Panel discussion

**UI/UX Expert** led and was the strongest voice: "the failure mode here is not a bug, it is
*training*. Six different-stakes actions behind one identical window teaches the user that the
window is unremarkable. The single highest-value change is that a window asking for a *bank*
credential must never be mistakable for one asking for a *local* password, and must show the
institution and the destination." They were clear that this is a presentation requirement, not
an architectural one — B's shared implementation is fine as long as the presentations are
genuinely distinct.

**Developer** preferred B on maintenance grounds and identified the real defect precisely: the
subclasses call `AddUserDefinedField` and rewrite the prose at runtime, so no static reading of
the code tells you what any instance looks like. "That is the thing to kill. Sharing the
component is fine; mutating it at runtime is not."

**Architect** agreed and framed the synthesis: B is the engineering shape, A is the design
requirement, and they compose — one audited core, strictly distinct presentations, enforced by
the type system rather than by discipline.

**Adversarial Expert** pushed hard, as instructed for the security decisions, and made the
point that reframed the discussion: "you are polishing the front door. The bank username and
password are stored in the money file in ordinary columns — `OnlineAccounts.UserId` and
`OnlineAccounts.Password`. Whatever the window looks like, the credentials end up in a file
that may or may not be encrypted (D-43), sitting next to a plaintext SQL Server credential
file, on a machine where anything can read them. A beautiful sign-in window on top of that is
not a security improvement, it is a nicer-looking one." They also challenged the
endpoint-display idea: "showing a URL only helps if the user knows what their bank's URL should
be. Most don't. It helps against a *changed* endpoint, not against a wrong one — which is
D-42's job, not this window's."

That was accepted and it sharpened the recommendation: display the endpoint **and** whether it
has changed since last time, which is a comparison the product *can* make and the user *can*
act on.

**Test Engineer** noted that a typed-configuration component is testable — each case can be
asserted to render the right fields and the right chrome — whereas the current runtime-mutation
design is essentially only testable by screenshot.

**Operations Engineer** — *nothing to add here*, beyond noting that credential storage location
(Credential Manager vs. the money file) is an operational question that belongs with D-43.

**Data Engine Expert** — *largely outside my lane*, except on the storage point the Adversarial
Expert raised: bank credentials in plain table columns is a storage decision and should be
resolved as one. Their recommendation: move online-banking credentials into Windows Credential
Manager — the product already has a wrapper for exactly this (`DatabaseSecurity`), used for the
file password and not for these — and keep only a reference in the file.

### Recommendation

**B as the implementation, A as the binding design requirement.** One audited credential
component with typed, closed configurations; runtime prose-rewriting and dynamic field
injection removed. Strictly distinct presentations per trust domain: third-party sign-in always
carries the institution's identity, the destination address, and a clear indication when that
address has **changed since the last successful sign-in** — which is the comparison the product
can actually make. The local file password prompt never uses that chrome, and vice versa.

**C is the right direction and not currently implementable** against OFX direct connect; the
panel records it as the destination should the product's connectivity story change, and warns
that partial adoption would be worse than none.

**The panel's strongest finding here is outside the window.** Online-banking credentials are
stored in the money file as ordinary columns. They should move to Windows Credential Manager
— the product already has and uses that wrapper elsewhere. **The panel rates this above the
window redesign in security value**, and notes it is not currently captured as a decision or,
as far as the panel could tell, as a filed bug.

### Needs-your-decision flag

**No** for the window design — the panel is confident. The credential-*storage* finding is a
plain defect and should be filed rather than decided.

**Panel flags: would benefit from dedicated security / anti-phishing UX expertise.** Credential
prompt design is its own discipline with real research behind it (what actually stops users
typing credentials into hostile look-alikes), and the panel is reasoning from principles rather
than from that literature.

---

## D-41 — What may a shareable diagnostic log contain?

**From:** Phase 6 OQ13 · **Cluster:** `security` · *Defect instances: #56*

### ELI10

When the program talks to your bank and something goes wrong, it saves a transcript of the
whole conversation so somebody can work out what happened. There's a "Details…" link whose
entire purpose is to get you to send that transcript to someone for help.

Whoever wrote it did the hard part: passwords and security answers are blanked out. But the
transcript still contains your bank's identifying code, **your account number**, and every
single transaction in the response — every purchase, every amount, every date.

So the program invites you to email a stranger a complete statement of your finances with your
account number on it, and nothing on the screen says that's what's in the file.

### Alternatives

**A. Structured error report by default; the conversation is never the default share.** The
"Details…" affordance produces a short, structured report: what was attempted, the protocol
version, the status and error codes the institution returned, timing, the product version, and
*no* statement content and *no* identifiers. The full conversation stays on disk for the user's
own use and is never what the share action hands over.

- **Pros.** Safe by construction — there is nothing sensitive to leak because nothing sensitive
  is included. Small and readable, so a user can see exactly what they are sending. Covers a
  large fraction of real support cases, which are usually "this error code, this protocol
  version" rather than "this specific transaction."
- **Cons.** The remaining fraction of cases are precisely the hard ones — a malformed
  transaction, a date the parser rejects, an amount in an unexpected format — and those cannot
  be diagnosed without the content. Drives an escalation path ("now please send the full log")
  which recreates the original problem with extra steps.
- **Unintended consequences.** If the structured report is insufficient often enough, users and
  maintainers will route around it by sending the raw file anyway — and then it is being shared
  with *no* redaction and no warning, because the safe path didn't cover the case. A safe
  default that doesn't work is worse than a warned-about unsafe one.

**B. Full-fidelity log, kept local, never framed as shareable.** The transcript keeps
everything (minus credentials, as today). The "Details…" affordance opens the *folder* with a
clear explanation of what the files contain, rather than offering to share them. Sharing
becomes an explicit, informed act by the user.

- **Pros.** Maximum diagnostic value retained. Honest — it tells the user what is in there and
  lets them decide. No redaction machinery to build or get wrong. Cheapest.
- **Cons.** "Here is a folder, mind how you go" is not a design, it is a disclaimer. In practice
  a user seeking help will send the file, and the product will have technically warned them.
  Relies entirely on the user reading and understanding a warning at the moment they are
  frustrated and want help — the worst possible moment.
- **Unintended consequences.** The logs accumulate indefinitely in `OfxLogs\` with no retention
  policy, so over years the folder becomes a complete unencrypted archive of the user's
  statement history, sitting in a known location. That is a meaningful exposure independent of
  whether anything is ever shared, and B does nothing about it.

**C. Two-tier with redaction at share time.** Full logs are kept locally (as B). A distinct
"Prepare a log to send" action produces a *separate* sanitised file: identifiers replaced with
stable pseudonyms (the same account number becomes `ACCT-1` everywhere it appears), amounts
optionally retained or rounded, payee names removed, with a preview of what is being sent and
a checklist of what was removed.

- **Pros.** Keeps diagnostic value where it matters — consistent pseudonyms preserve the
  structure and correlation that makes a log useful ("the same account appears in both
  requests," "these three transactions share a payee") while removing the identifying content.
  The user sees exactly what will leave the machine. Satisfies the register's own stated goal:
  the product should be able to state what leaves the machine.
- **Cons.** Redaction is the kind of thing that is 95% right and leaks in the remaining 5% —
  an unanticipated element, a free-text memo containing an account number, an institution that
  puts identifiers somewhere unusual. Building it means enumerating what to keep (allow-list)
  rather than what to remove (deny-list), or it will leak.
- **Unintended consequences.** A redacted log implies a guarantee. If the product says "this is
  safe to send" and it isn't, that is worse than no promise at all — so the preview is not a
  nicety, it is the feature. Also: pseudonymising amounts and dates may not be enough to
  prevent re-identification if the log is shared publicly (a specific set of transactions is
  quite identifying), which the wording must not over-claim.

### Panel discussion

**Adversarial Expert** opened by pointing at the affordance rather than the file: "the defect
is not that the log has account numbers in it — a diagnostic log reasonably might. The defect
is a button whose purpose is to get the user to hand it over. Either the button changes or the
file changes. Changing the file is harder and changing the button is free, so at minimum,
change the button today."

**UI/UX Expert** agreed and added the decisive requirement: whatever is shared, the user must
be able to *see* it before it leaves. "A preview is not polish. It is the only thing that makes
an assurance meaningful, and it is what lets a cautious user trust the feature."

**Architect** favoured C on the allow-list principle: redaction must be built as "include these
named elements" and never as "remove these named elements," because the deny-list approach is
exactly what produced the current gap — nine credential elements were removed, and the ones
nobody thought of were not.

**Developer** noted that the protocol is structured, so an allow-list is genuinely feasible:
walk the response tree, keep status and error elements, replace identifier elements with
stable pseudonyms, drop the rest. Not a large piece of work, and it can be unit-tested against
saved fixtures.

**Test Engineer** liked C for exactly that reason: "redaction is one of the few security
features that is straightforwardly testable — feed a fixture with known identifiers, assert
none appear in the output. That test should be strict, and it should fail closed on any
element the allow-list doesn't recognise."

**Operations Engineer** raised the retention point that none of the alternatives had addressed:
the logs grow forever, in a known folder, unencrypted. "Whatever is decided about sharing, cap
retention and tighten the folder's permissions. That is a real exposure with no sharing
involved at all."

**Data Engine Expert** — *nothing to add here*; OFX logs are not storage.

**Developer** raised, and the panel accepted, that `SaveLog`'s existing masking is worth
keeping in the local file — it is correct as far as it goes — so C builds on it rather than
replacing it.

### Recommendation

**Alternative C, built as an allow-list, with A's structured summary as the default first
offer.** Concretely:

1. The `Details…` affordance offers the **structured error report** first — it covers most
   cases and carries nothing sensitive.
2. "Prepare a full log to send" is a separate, deliberate action producing a **separate**
   sanitised file, built as an allow-list over the response structure, with stable pseudonyms
   for identifiers so correlation survives redaction.
3. **A preview of exactly what will be sent** is mandatory, not optional.
4. The unredacted log stays local, keeps its existing credential masking, gains a **retention
   policy** and tightened folder permissions, and is never what a share action hands over.
5. The redaction allow-list gets a strict fixture test that fails closed on unrecognised
   elements.

The wording should say what was removed, and should not over-claim anonymity — a specific set
of transactions remains identifying even without an account number.

### Needs-your-decision flag

**No.** The panel is confident. The retention/permissions item is a plain defect the panel
found in passing and recommends filing alongside #56 rather than deciding.

---

## D-42 — Should the product trust a third-party institution directory?

**From:** Phase 6 OQ18 · **Cluster:** `security` · *Defect instances: #56*

### ELI10

To know how to talk to your bank, the program downloads a list of banks from a community
website. That list includes **the web address the program will send your bank username and
password to**.

It downloads this list over an ordinary, unprotected connection — the kind where anyone
between you and that website (a coffee-shop wifi, a compromised router, your internet
provider) can change what comes back without you or the program noticing. Change one line, and
the program will cheerfully post your banking login to whatever address the attacker chose.
Then it saves that address and keeps using it.

There's a partial safety net: if the stored address has no protocol on the front, the program
adds the secure one before sending. But if the attacker's address already says the insecure
one, it's left exactly as-is.

Switching to a protected connection is the obvious fix and it's already written down as a bug.
The bigger question the fix doesn't answer: should a list from a website the program's authors
don't control get to decide where your banking password goes at all?

### Alternatives

**A. Keep the directory, but never let it silently change a credential destination.** Fetch
over HTTPS with proper certificate validation. **Refuse** to post credentials to a non-HTTPS
endpoint — refuse and explain, rather than silently upgrading. Treat any change to an
institution's credential-receiving address as requiring explicit confirmation showing the old
and new addresses and when the previous one was last used successfully. New institutions the
user has never connected to get a confirmation of the destination the first time.

- **Pros.** Keeps the feature working and the directory useful. Closes the transport attack
  (HTTPS) and the persistence attack (an entry cannot be silently repointed). The
  changed-endpoint comparison is a check the product *can* make correctly, unlike "is this the
  right URL," which it can't. Cheap: all of it is a few well-placed checks.
- **Cons.** Confirmation prompts are clicked through. A user shown "this bank's address changed,
  continue?" will continue — they have no basis to judge, and the prompt appears at the moment
  they are trying to download transactions. Security that depends on a user's judgement about
  a URL is weak security.
- **Unintended consequences.** Hard-refusing non-HTTPS endpoints will break any institution
  that genuinely still uses plain HTTP, and some legacy OFX endpoints did. That is the correct
  outcome — credentials must not go over plain HTTP — but it will read as "the product broke my
  bank," and needs a clear message saying why rather than a generic failure.

**B. Ship a curated, signed list; the community directory becomes an opt-in lookup.** The
product ships its own vetted institution list, updated with releases and signed so it cannot be
tampered with in transit or at rest. The community directory is available as an explicit
"search for more institutions" action that never silently overwrites a configured endpoint and
always requires confirmation.

- **Pros.** The strongest position: the credential destination for the common case comes from
  something the project controls and signs. Removes the third-party's ability to influence
  where credentials go by default. Signing also protects the on-disk cached list, which A does
  not.
- **Cons.** Someone has to curate it. Bank OFX endpoints change without notice, and a stale
  shipped list means the product breaks for a user until the next release — a worse
  day-to-day experience than a slightly risky live directory. For a single-maintainer project
  this is a real, recurring obligation and the most likely thing to be abandoned.
- **Unintended consequences.** A shipped list makes the project implicitly responsible for the
  correctness of each endpoint — a liability the community directory currently absorbs. It also
  ties institution support to the release cadence, which pushes toward more frequent releases
  than the project may want.

**C. Drop directory sync; the user supplies their institution's details.** No remote directory.
The user enters their bank's OFX address, organisation and identifier (which their bank
publishes, and which sites like ofxhome list for people to read). The product remembers them.

- **Pros.** The product never fetches a credential destination from anywhere. Maximum honesty:
  the user chose where their credentials go. No curation burden, no transport risk, no cache
  poisoning.
- **Cons.** Hostile to set up. Most users cannot find their bank's OFX endpoint, and a
  significant fraction will give up — which for a Quicken-replacement evaluation is a
  material problem, since Quicken's institution list is one of the things it does well. It
  also just moves the trust problem to the user, who will look it up on... a community website.
- **Unintended consequences.** Users copying endpoints from a web page introduces typo and
  copy-from-the-wrong-place risks that an automated directory doesn't have — arguably a *worse*
  attack surface, since a plausible-looking wrong URL pasted by a confused user gets no scrutiny
  at all.

### Panel discussion

**Adversarial Expert** was asked to push hard here and did. They accepted A as necessary and
then attacked it: "HTTPS fixes the man-in-the-middle. It does not fix the fact that ofxhome is
a community-edited site. Anyone who can edit an entry there can change where a user's banking
password goes, and HTTPS delivers that change with a valid certificate and a green padlock.
You have secured the pipe and left the source unauthenticated. And your mitigation is a
confirmation dialog, which users click through. Be honest about what A buys: it stops the
coffee-shop attack, not the directory-compromise attack."

Nobody could refute that, and the panel records it as the honest limitation of its own
recommendation.

**Architect** made the case for A anyway on proportionality: "B is correct and will not be
maintained. An unmaintained curated list is not more secure than a live one — it is a list
that stops working, and the response to 'my bank doesn't work' will be a user manually pasting
a URL from somewhere, which is C's failure mode with extra steps. Recommending something that
won't be sustained is not a security recommendation."

**Operations Engineer** supported that on concrete grounds: OFX endpoints change, and there is
no notification channel. A curated list is a standing operational commitment.

**UI/UX Expert** focused on making the confirmation as useful as a confirmation can be: show
the *domain*, not the full URL; state plainly whether this domain has been used successfully
before; and reserve the interruption for the case that is actually suspicious (a *changed*
destination), not every connection — because a prompt shown every time is a prompt nobody
reads.

**Developer** noted every part of A is small: switch two URL constants, add certificate
validation (the default, as long as nothing disables it), add a stored "last known good
endpoint" per institution and compare, and refuse non-HTTPS at post time.

**Test Engineer** flagged that A is testable end-to-end with a fake directory: assert the
product refuses a plain-HTTP endpoint, assert a changed endpoint prompts, assert an unchanged
one does not. That test suite is worth more than the individual fixes because it prevents
regression.

**Data Engine Expert** — *nothing to add here*, outside my lane.

**Adversarial Expert** then raised the strategic question that the panel could not settle:
"should this product be doing OFX direct connect at all in 2026? US institutions have been
retiring it for years. You may be hardening a feature that will not work in three years
regardless of what you do to it. If the answer is 'we're keeping it,' fine — do A. If the
honest answer is 'this is legacy,' then the right investment is import-from-file, and this
decision shrinks to 'stop shipping the plain-HTTP fetch.'"

### Recommendation

**Alternative A, with hard rules rather than soft prompts wherever a hard rule is possible.**

1. HTTPS with certificate validation for the directory fetch — this is the #56 bug; just fix it.
2. **Never post credentials to a non-HTTPS endpoint.** Refuse and explain. Do not silently
   upgrade a stored `http://` URL; a stored plain-HTTP endpoint is treated as untrusted and
   requires the user to re-confirm the institution.
3. **An institution's credential-receiving address may never change silently.** Store the last
   endpoint that produced a successful sign-in; any change requires explicit confirmation
   showing the previous and new domain and when the previous one last worked.
4. A first-time connection to a new institution confirms the destination domain once.
5. Test with a fake directory: refuse plain HTTP, prompt on change, stay quiet otherwise.

**Recorded honestly, at the Adversarial Expert's insistence:** this is not "good enough" in an
absolute sense. It closes the network attack and the silent-repoint attack. It does **not**
close the case where the community directory itself is edited maliciously and serves a valid
HTTPS endpoint the user has never seen before — for a new institution, the user's confirmation
is the only control, and that control is weak. Alternative B would close it; the panel does not
recommend B because it judges the curation burden unsustainable for this project, and an
abandoned curated list is a worse outcome than a live one. **If the product owner is willing
to own a curated institution list, B is the better answer and the panel would change its
recommendation.**

### Needs-your-decision flag

**Partially — two questions for you.**

1. **Are you willing to maintain a curated institution list?** If yes, B is better and the
   panel switches. If no, A is the recommendation and its limitation above is accepted
   knowingly.
2. **Is OFX direct connect a capability the redesign is investing in at all**, given the
   direction of the industry? If it is being kept only for existing users, the proportionate
   answer shrinks to "fix the HTTPS bug, never post to plain HTTP, and don't build anything
   else here."

**Panel flags: would benefit from dedicated security expertise.** Specifically: supply-chain
trust for a community-maintained endpoint directory, and whether endpoint pinning or
certificate pinning per institution is warranted. The panel is reasoning from general
principles.

---

## D-43 — What does "password-protect my file" promise?

**From:** Phase 8 OQ9 · **Cluster:** `security` · *Defect instances: #52/#56 family*

### ELI10

The program offers to put a password on your financial file. That sounds like "nobody can read
this without the password." Here is what it actually does.

Turning a password into a key is supposed to be deliberately slow, so that someone trying
millions of guesses is slowed to a crawl. This program does it **twice** — not two million
times, two — using a method retired around twenty years ago. It also mixes in a fixed extra
ingredient that is the same in every file the program has ever made, and starts the scrambling
from the same fixed position every time. Both of those are supposed to be different for every
file; making them the same means someone who cracks one file has a head start on all of them.
And the result isn't sealed, so someone could alter the scrambled file without you being able
to tell.

There's something more immediately damaging. To read or write your protected file, the program
first writes the **entire unprotected contents** into the computer's temporary folder as an
ordinary file. On the reading side, the line that deletes it comes after the parsing step — so
if anything goes wrong while reading, that complete unencrypted copy of your whole financial
history just stays there. Forever.

The comment in the code says these settings "cannot change," and it's right that changing them
breaks every file already made. So the decision isn't "fix it" — it's how to get from here to
something real without stranding anyone.

### Alternatives

**A. Versioned encrypted container.** A real file format: a magic header and a version number,
a random salt per file, a random nonce per file, a modern key-derivation function with a proper
work factor (PBKDF2-SHA256 at a high iteration count, or Argon2id), and authenticated
encryption (AES-GCM, or AES-CBC with an HMAC) so tampering is detectable. Streamed directly —
no plaintext temp file, ever. Version 1 (today's format) remains readable; everything written
is version 2; on opening a version 1 file the user is offered a one-time re-encryption.

- **Pros.** Fixes every cryptographic defect at once and makes the feature mean what its name
  says. The versioned header means this never has to be a one-way door again — a future change
  is version 3. Nobody is stranded. Authentication (the missing property today) is arguably the
  most important gain: unauthenticated CBC is malleable, and for financial records "nobody can
  tell it was altered" is a serious weakness.
- **Cons.** Real cryptographic work in a codebase with no crypto expertise on hand, and getting
  a container format subtly wrong is worse than the status quo because it inspires confidence.
  A high iteration count makes opening measurably slower, which users notice.
- **Unintended consequences.** Once the format is versioned and authenticated, a *corrupted*
  file becomes an *unopenable* file — authentication means refusing to decrypt damaged data,
  where today a corrupted file might partially parse. That is correct behaviour and will be
  experienced as a regression by anyone whose file gets damaged. It raises the stakes on backup
  (D-35's floor) considerably.

**B. Withdraw file encryption; point at the operating system.** Remove the password affordance
from the file formats. Document BitLocker, EFS and encrypted archives as the way to protect
the file at rest. Keep only the Credential Manager integration that avoids re-prompting.

- **Pros.** Honest. Stops making a promise the implementation does not keep. Zero crypto to
  maintain. BitLocker genuinely is better protection than anything this product would build,
  and most Windows machines have it. Removes the plaintext-temp-file problem by removing the
  code path.
- **Cons.** Removes a feature users may be relying on, and "use BitLocker" does not cover the
  case the feature actually serves: a file copied to a USB stick, emailed, or put in a cloud
  folder, where whole-disk encryption does nothing. That is the realistic threat for a personal
  finance file and B has no answer to it.
- **Unintended consequences.** Existing encrypted `.bxml` files still need a read path, so the
  weak crypto code stays in the product regardless — B saves less code than it appears to.
  And a product that removes a security feature has to explain why, which is an awkward
  release note.

**C. Encrypt the store itself (SQLCipher), and use A's container only for exports.** The
default store is SQLite; move it to a properly-encrypted SQLite build with a real KDF. Whole
file encrypted at rest, page by page, with no plaintext temp copy. The `.bxml` path is retired
to an export format using A's container.

- **Pros.** Protects the file people actually use, not a format almost nobody chooses. (Note
  the working default is SQLite; `.bxml` is the *only* format that encrypts today, and it is
  not the default — so the encryption feature as it exists protects the least-used path.)
  No plaintext temp file by construction. A mature, well-reviewed implementation instead of a
  hand-rolled container. Would also cover the stored bank credentials raised in D-40.
- **Cons.** A native dependency, with all that implies for packaging — and this product ships
  via ClickOnce, where native dependencies and per-architecture builds are genuinely painful.
  Migrating an existing unencrypted SQLite file to an encrypted one is a full rewrite of the
  file, which needs to be transactional and backed up. The project currently references two
  different SQLite packages and uses one, which must be resolved first.
- **Unintended consequences.** An encrypted store means a lost password is unrecoverable data
  — a much more serious situation than today, where the password protects an occasional export.
  That demands a genuinely good "are you sure, there is no recovery" flow and a strong nudge
  toward Credential Manager storage, or the product will generate support cases it cannot
  resolve. It also makes the file opaque to external tools, which removes a diagnostic avenue.

### Panel discussion

**Adversarial Expert**, pushing as instructed, started with the threat model: "before choosing
a cipher, say what this protects against. It is not a compromised machine — if malware is
running as the user, everything here is moot. It is a *copied file*: a backup, a USB stick, a
cloud folder, a stolen laptop with no disk encryption. That is a real threat and it is exactly
the one BitLocker does not cover. So B's 'use BitLocker' answer is wrong for the actual threat,
and I say that as someone who wanted B to win."

They then attacked A: "you have no cryptographer. A hand-rolled container is how people
introduce padding oracles and nonce reuse. If you must encrypt, use something someone else got
right."

That argument carried the room toward C for the store.

**Data Engine Expert** supported C on its merits and added the observation that reframed the
decision: "the encryption feature today protects `.bxml`, which is not the default format. The
default is SQLite, and it is unencrypted unless a password is passed — and its password uses
`System.Data.SQLite`'s built-in encryption, which is itself legacy and not equivalent to
SQLCipher. So the product's encryption story is weak in two different places and the one users
actually touch has had less attention than the one they don't."

**Operations Engineer** objected strongly to C on deployment grounds: "SQLCipher is a native
dependency. This product ships ClickOnce. That combination is a known source of pain —
per-architecture payloads, trust prompts, update failures. You are proposing to take the most
reliable part of the product, the local file, and make its deployment story the most fragile
part." They favoured A for exports plus B's honesty for the working file: "encrypt the thing
that leaves the machine, and tell people to use BitLocker for the thing that doesn't."

**Architect** noted the coupling to D-35: if `.bxml` is demoted to an export format, then
"password-protect my file" becomes "password-protect my export" — which is a *smaller and more
honest* promise, and one alternative A serves perfectly. Whether the working store is also
encrypted is then a separate question, which is C.

That decomposition is what the panel settled on.

**Developer** costed it: A is a week if done carefully with existing .NET primitives
(`Rfc2898DeriveBytes` with SHA-256, `AesGcm` — both in the framework, no third-party
dependency); C is larger and mostly deployment risk, not code.

**Test Engineer** flagged that the legacy read path needs a permanent fixture test — a
version 1 file checked into the repository that must keep opening forever — or the
compatibility promise will be broken by accident within a year.

**UI/UX Expert** raised the recovery question for C: "if the working file is encrypted and the
password is lost, that data is gone. Today, losing an export's password loses an export. Those
are very different conversations to have with a user, and C needs a flow that makes the
consequence unmistakable *before* the password is set."

**Everyone** agreed on one thing without argument: **the plaintext temp file must go,
immediately, regardless of which alternative wins.** Writing the user's complete financial
history to `%TEMP%` as an ordinary file — and, on the read path, deleting it only after a parse
that might throw — makes the encryption largely pointless today. Streaming through
`CryptoStream` instead is straightforward; at absolute minimum the delete belongs in a
`finally`. (The code comment says in-memory chaining was abandoned because of a padding
exception — that is a solvable bug, not a reason for the design.)

### Recommendation

**A for the export format, now. C for the store, as a separate decision the owner should make
on deployment appetite. B rejected.**

1. **Immediately, independent of everything else:** eliminate the plaintext `%TEMP%` file by
   streaming encryption/decryption. At minimum, move the delete into a `finally`. This is a
   bug fix, not a design decision, and it currently undermines the entire feature.
2. **Export container (alternative A):** versioned header, per-file random salt and nonce,
   PBKDF2-SHA256 with a high iteration count (or Argon2id), and **authenticated** encryption.
   Use framework primitives (`Rfc2898DeriveBytes`, `AesGcm`) — no hand-rolled construction and
   no third-party crypto dependency. Read version 1 forever, with a checked-in fixture test;
   always write version 2; offer one-time re-encryption on open.
3. **The working store (alternative C):** the panel does **not** reach consensus. Recommended
   on security merit by the Data Engine Expert, the Adversarial Expert and the Architect;
   opposed by the Operations Engineer on ClickOnce/native-dependency risk, with the UI/UX
   Expert noting the unrecoverable-password problem it creates. **This is the owner's call**
   and turns on how much deployment fragility is acceptable.
4. **Related, and rated higher than the cipher work by two panel members:** bank credentials
   are stored in the money file in plain columns (see D-40). Moving them to Windows Credential
   Manager protects the most sensitive content in the file without any cryptographic work at
   all, and under alternative B it would be the *only* protection those credentials get.

### Needs-your-decision flag

**Needs your decision, on the store half only.** The export container (A) and the temp-file fix
are recommended confidently. Whether the working SQLite store becomes encrypted — accepting a
native dependency, a migration, and an unrecoverable-password failure mode — is a product and
deployment-appetite call the panel genuinely split on and cannot make for you.

**Panel flags: would benefit from dedicated cryptography expertise.** Specifically: container
format design, KDF parameter selection and work-factor tuning, AEAD choice, and the
compatibility-migration strategy for existing version 1 files. The panel's recommendation is at
the level of "use well-established primitives, don't invent a construction" and should be
reviewed by someone who does this professionally before it is implemented.

---

## Session summary

| ID | Recommended alternative | Needs your decision? |
|---|---|---|
| D-32 | A — no engine choice in `File ▸ New`; seam preserved, not built | Partial (deferred to D-37) |
| D-33 | Remove the write-capable SQL console now; structured query surface as the replacement | Partial (scope of replacement) |
| D-34 | A+B (one write path, aggregate-root granularity) *if* SQL Server ships; C (file lease) if not | **Yes** — and panel split |
| D-35 | A — one store, everything else a format; split `IDataStore` from `IDataFormat` | No |
| D-36 | B+C — surface a non-modal review list with explicit repairs, plus a "Check my data" command | No |
| D-37 | Panel default A (ratify developer-only), 6 of 7 | **Yes** — answer this first |
| D-38 | A — stored-procedure engine is the only engine; extract an abstract base; admin ops leave `IDatabase` | No |
| D-39 | B now (`PRAGMA table_info` + pre-upgrade backup + tests), C as destination | No |
| D-40 | B implementation + A design requirement; move bank credentials to Credential Manager | No |
| D-41 | C — allow-list redaction at share time, with a structured report as the default offer | No |
| D-42 | A — HTTPS, refuse plain-HTTP endpoints, never silently repoint | Partial (two questions) |
| D-43 | A for exports now; C for the store is the owner's call; fix the `%TEMP%` plaintext immediately | **Yes** (store half) |

**Experts who opted out of a decision:** UI/UX Expert on D-38 (no user-visible surface);
Data Engine Expert on D-41 and D-42 (outside storage), and largely on D-40 except the
credential-storage point; Operations Engineer on D-33 and substantially on D-36.

**Additional expertise flagged:** D-40 (security / anti-phishing credential-UX), D-42
(supply-chain trust for a third-party endpoint directory; endpoint or certificate pinning),
D-43 (cryptography — container format, KDF parameters, AEAD choice, migration).

**Items the panel found that are defects, not decisions, and recommends filing:**

- The ad-hoc SQL console is reachable and **write-capable on the default SQLite store**, and
  reports a destructive statement by showing nothing (F2). The register, following B-23,
  records this as server-only; it is not.
- `DbFlavor.SqlServer` is the fall-through for any unrecognised file name (F3), so a typo
  routes a user into the raw-SQL server engine.
- `SqliteDatabase.GetTableSchema` replaces every comma with a newline before parsing, so any
  parameterised column type (`decimal(p,s)`) would be mis-parsed immediately before a
  `DROP TABLE`. Dormant only because no column declares a precision.
- Online-banking credentials are stored in the money file as ordinary `OnlineAccounts` columns,
  while the product has an unused Credential Manager wrapper for exactly this.
- `OfxLogs\` has no retention policy or permission tightening; it accumulates unencrypted
  statement history indefinitely.
