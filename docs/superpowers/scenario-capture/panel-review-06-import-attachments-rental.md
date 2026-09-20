# Panel Review 06 — Import/Export, Attachments & Documents, Rental Property

One expert-panel session over eight entries from the
[Redesign Decision Register](redesign-decision-register.md):

| | Decision | Cluster |
|---|---|---|
| **D-25** | What should "Money File Import" accept? | `import-export` |
| **D-26** | Is QIF import worth keeping? | `import-export` |
| **D-27** | What happens when an imported transaction isn't in the account's currency? | `import-export` |
| **D-44** | Bring scanning back, or delete it? | `attachments-documents` |
| **D-45** | What happens to paperwork when its transaction is deleted? | `attachments-documents` |
| **D-46** | File documents by account name or by account id? | `attachments-documents` |
| **D-47** | Finish per-unit rent entry, or delete it? | `rental-property` |
| **D-48** | What is a rental property in the model, and how much of it is editable? | `rental-property` |

The panel: **Architect**, **Developer** (C#/.NET, WPF, WPF-UI/Fluent), **Test Engineer**,
**Operations Engineer**, **UI/UX Expert**, **Data Engine Expert** (SQLite/SQL Server), and an
**Adversarial Expert** whose job is to stress-test the emerging consensus before it settles.
Experts opt out explicitly where a decision is outside their lane.

Each decision gets: a plain-language explanation, at least two genuinely different design
directions with pros / cons / unintended consequences, the panel's recommendation, and an
honest **Needs your decision** flag where the answer really turns on product taste rather
than engineering judgement.

**Before the sessions began**, the panel re-read the code behind each register entry rather
than working from the entry alone. Five of the eight turned out to be materially different
from their register summary. Those corrections are called out in the relevant sections and
collected at the end.

Issue [#54](https://github.com/markabrandjord/MyMoney.Net/issues/54) sits next to D-44/D-45/D-46
and is **not re-litigated here** — it is a settled data-loss bug with one correct fix. It is
referenced only where a decision's shape depends on it.

---

## D-25 — What should "Money File Import" accept?

**From:** Phase 4 OQ25 · **Cluster:** `import-export`

### ELI10

Imagine you keep your money notebook on your home computer, and you also keep a second copy
of it on your laptop while you're travelling. When you get home, you want to fold the
laptop's new pages back into the home notebook — without ending up with two copies of every
page you'd already written before you left.

The program has a window for exactly that, called **Money File Import**. It lets you pick
several files at once. But it only actually understands *one* kind of file — the program's
own database file. Hand it anything else and it says *"Import only supports sqllite money
files"* (with "sqlite" misspelled) and gives up on the whole batch, including the files it
could have handled.

That's odd, because the program can *save* your notebook in several different shapes: a
database file, a plain-text file, and a compact file. Three different shapes, all of them
"your whole notebook" — but the merge window accepts only one.

Here's the part that isn't obvious, and it's the real heart of the decision. Merging works
by **serial number**. Every page in your notebook got a serial number the day you first
wrote it. When the laptop copy comes home, the program matches page #4021 in the laptop copy
to page #4021 at home, and updates it. That only works if both notebooks *started life as
the same notebook* — if one was photocopied from the other. If you handed it a friend's
notebook, page #4021 would be a completely unrelated page, and the merge would quietly
overwrite your grocery receipt with their car payment.

So the question is not really "which file shapes should the window accept". It's "**what
does this window promise, and how does it tell whether the file in front of it is safe to
merge?**" Right now it guesses using the file's *name ending* — which tells you the shape of
the file and nothing at all about where it came from.

The real-world consequence: pick the loose answer and a user can silently corrupt their books
by merging a file that was never a copy of theirs. Pick the narrow answer and a user with a
perfectly valid backup in a different shape is told, wrongly, that it isn't a money file.

### What the code actually does (corrections to the register entry)

The register describes `ProcessFile`'s extension check as the gate. The panel found the gate
is one layer up and works differently:

- `MainWindow.OnImport` (around `MainWindow.xaml.cs:4180`) **already dispatches by extension**
  before `MoneyFileImportDialog` is constructed. `.qif` → `ImportQif`, `.ofx`/`.qfx` →
  `ImportOfx`, `.csv` → `ImportCsv`, `.xml` → `ImportXml`, `.db`/`.mmdb` → `ImportMoneyFile`,
  everything else → *"Unrecognized file extension … expecting .qif, .ofx, .csv or .xml"*
  (which itself fails to mention `.db`/`.mmdb`).
- Therefore the misspelled `ProcessFile` message is **effectively unreachable from the menu
  route** — only `.db`/`.mmdb` ever get that far. The message the user actually sees for an
  `.xml` money file is the *other* one, and it does not say "wrong kind of import", it says
  the extension is recognised and then silently routes to something else.
- **`.xml` already means something different on import.** `XmlImporter.ImportXml` remaps
  incoming ids (`remappedIds`) and *appends*. `MoneyFileImportDialog.ImportAccount` matches
  by `FindTransactionById(t.Id)` and *merges*. Same extension, opposite semantics. A user who
  saved their whole book via **Save As → .xml** and then imports it gets a second copy of
  every transaction, not a merge.
- **`.bxml` (binary XML) is a Save As target with no import route at all.** `SaveAsBinaryXml`
  exists; nothing reads it back except `Open`.
- **`.sdf` (SQL CE) is likewise a Save As target** with no import route.

So the true defect surface is larger than "a narrow check and a typo": there are three
whole-file save formats, one of which imports with the *wrong* semantics and two of which
cannot be imported at all.

### Alternatives

#### A. One merge entry point, format-agnostic, gated on lineage rather than extension

Accept any whole-file format the storage layer can open (`.db`, `.mmdb`, `.sdf`, `.xml`,
`.bxml`), open it through `IDatabase`, and then decide whether to merge by asking the *file*,
not the filename: give every money file a **lineage id** (a GUID stamped at creation and
carried through every Save As) and compare it to the open file's. Same lineage → merge by id,
no warning needed. Different lineage → refuse, or offer the append-with-remap path explicitly
as a different, clearly-labelled operation.

**Pros**
- Turns the `MessageBoxEx` "merging only works if both started with the same state — continue?"
  warning from an unenforceable request into a check the product actually performs. That
  warning is currently the *only* thing standing between a user and silent cross-book
  corruption, and it is a checkbox on the user's memory.
- Resolves the `.xml` ambiguity by construction: the *file* says which operation is legal, so
  one extension can serve both.
- Makes `.bxml` and `.sdf` importable for free — they route through the same `IDatabase.Load`.
- Fits D-35's engine-portfolio decision cleanly: whatever engines survive, they all get import
  for nothing.

**Cons**
- Requires a schema addition (one row in the settings/metadata table) in every engine, plus a
  migration for existing files — which lands squarely on D-39's schema-upgrade machinery.
- Existing files have no lineage id. Backfilling is guesswork: the honest answer is "unknown
  lineage", and then the product has to decide what "unknown vs. unknown" means. Most likely
  it means falling back to today's warn-and-hope for a transition period, which is the thing
  being fixed.
- The append-with-remap path has to be designed as a real feature rather than an accident,
  which is net-new UX (see D-33's power-user surface question for where it would live).

**Unintended consequences**
- A lineage id is a **fingerprint that travels with the file**. Two users who share a starting
  file (a template, a sample database, a shared household book that was later split) now have
  the same lineage and will be *permitted* to merge books that have genuinely diverged into
  different people's finances. Lineage proves common ancestry, not that merging is still
  wise — the warning can be narrowed but not deleted.
- Backup/restore (D-50) has to decide whether a restored copy keeps or regenerates its
  lineage. Keep, and "restore to a new file then merge the old one back" works. Regenerate,
  and it silently stops working.
- `Save As` becomes a lineage-preserving operation, which means **`Save As` is no longer a way
  to fork your books**. Some users deliberately use it that way. A separate "start a new book
  from this one" would be needed to replace it.

#### B. Narrow it honestly: this window merges *database* files, and says so

Rename the feature to something like **"Merge a copy of this book"**, restrict it to the
engines whose `IDatabase` can be opened for merge, fix the message text, and make the
extension check a *pre-filter on the file picker* rather than a per-file failure inside the
batch. Fix the batch-abort behaviour so one bad file reports and the rest proceed. Leave
`.xml` import where it is, but rename *that* operation in the UI to "Append transactions from
an XML export" so the two stop colliding in the user's head.

**Pros**
- Cheapest by a wide margin. No schema change, no migration, no new concepts. Mostly naming,
  a file-dialog filter and one loop fix.
- Honest: the window's name matches what it does, and the two XML meanings stop being
  indistinguishable.
- Keeps the risky operation (identity-based merge) on the narrowest possible surface, which is
  defensible while the warning remains the only safeguard.

**Cons**
- Does nothing about the actual hazard. A user can still merge someone else's `.mmdb` and
  corrupt their books; the only protection stays a modal they will click through.
- Leaves `.bxml` and `.sdf` as write-only formats — a user who chose one as their backup
  format has no path back in except Open, which replaces rather than merges.
- Pushes the real decision to whichever future release notices the hazard again.

**Unintended consequences**
- Renaming a menu item breaks every piece of user muscle memory and every screenshot in the
  docs (D-51's help question) for a change that improves nothing functionally. The rename is
  the *visible* part of a fix that is mostly invisible, which reads to a returning user as
  churn.
- A file-dialog filter is a weaker gate than it looks: drag-and-drop and command-line
  arguments both bypass it (`MainWindow.xaml.cs:986` accepts `.mmdb`/`.bxml` from a dropped
  path), so the per-file check has to stay anyway and the "pre-filter" is UX polish, not
  enforcement.

#### C. Split the concept in two: "Merge a copy" and "Import transactions from a file"

Stop trying to make one window serve both. **Merge a copy** is a rare, high-stakes,
whole-book, identity-matched operation with its own confirmation, its own per-account progress
report and its own undo story. **Import transactions** is the routine, low-stakes operation
that already exists for QIF/OFX/CSV/XML and always appends-and-matches heuristically (date,
amount, payee) rather than by id. Route every format to one of the two by *what the user is
trying to do*, asked up front, not by extension.

**Pros**
- Matches the user's actual mental model. "I'm bringing my laptop's copy home" and "I'm
  bringing in my bank's download" are not the same task, and today they share a menu.
- Makes the dangerous operation *feel* dangerous and the routine one *feel* routine —
  currently both are one click off the same menu.
- Gives heuristic matching (which the QIF path already has via `Transactions.Merge`, and which
  the duplicate-detection work in D-12 touches) one home instead of three.
- Doesn't foreclose A: lineage checking can be added to "Merge a copy" later, and only there.

**Cons**
- The most UI work of the three. Two entry points, two flows, two sets of progress reporting,
  and a decision about what the old menu items become.
- Some formats genuinely serve both purposes (a `.mmdb` could be your laptop copy *or* a
  friend's book you want to cherry-pick from), so the up-front question has a "it depends"
  answer for at least one case.
- Doubles the surface the Test Engineer has to cover at exactly the point where the existing
  coverage is thinnest.

**Unintended consequences**
- Two import paths tend to drift. The moment one gains attachment handling (P4-IMPORT-6 /
  #54) or currency handling (D-27) and the other doesn't, the product has two different
  answers to the same question again — which is how it got here.
- "Import transactions from a `.mmdb`" — cherry-picking from a non-lineage database — is a
  capability that does not exist today and would have to be *built*, not just routed. If the
  panel isn't prepared to build it, option C collapses back into B with extra menus.

### Recommendation

**B now, A next, and treat C as the naming that makes A legible** — with one genuine
disagreement recorded below.

The reasoning that won: the register frames this as a scope question ("how many formats?"),
and the panel's re-read says the scope question is the *less* important one. The important
finding is that `.xml` silently means two different operations, and that the identity-merge
hazard is guarded only by a modal. Adding formats to a window whose safety model is "we asked
the user to be sure" makes the hazard broader, not narrower. So the format expansion should
**follow** the lineage check, not precede it.

- **Architect**: A is the only option that makes the merge contract checkable. Everything else
  is an improvement to a guess. But A depends on D-39 (schema upgrade) having an answer, and
  that's a different panel's decision, so it cannot be scheduled first.
- **Developer**: B is a day's work and mostly deletion. A is maybe a week once D-39 exists. The
  `IDatabase` abstraction already makes the format-agnostic load trivial — `SqliteDatabase` is
  hard-coded into `ProcessFile` purely because nobody generalised it, not because it resists.
- **Test Engineer**: A is the first version of this feature that can be tested *negatively* —
  today there is no way to assert "refuses to merge an unrelated book", because nothing
  distinguishes one. Strongly for A. Notes that B's batch-abort fix needs a test that a
  three-file batch with a bad middle file still imports files one and three, which is a
  regression waiting to happen.
- **Operations Engineer**: A's backfill is the real cost. "Unknown lineage" on every existing
  file means the check is inert for the entire installed base until they re-save. Suggests
  stamping lineage on *load* as well as create, so any file opened once by the new version
  gets one — which converts the installed base silently and makes the check live within one
  session for most users.
- **UI/UX**: C's split is right regardless of A or B. The current menu presents a
  potentially-destructive whole-book operation with the same weight as importing a bank
  download. Would like the merge operation to carry a Fluent `InfoBar` with the lineage
  verdict ("This file is a copy of your book, last diverged 12 March") rather than a modal
  asking the user to confirm something they cannot know.
- **Data Engine Expert**: The lineage id belongs in the same metadata/settings table that
  D-39's schema version lives in — one migration, not two. Warns that `XmlStore`/`CsvStore`
  have no such table and would need the id in a document attribute, which is trivially
  editable by a user with a text editor. Lineage is a **safety interlock, not a security
  control**, and the design must not later lean on it as though it were one.
- **Adversarial**: "How often does anyone actually merge two copies of their own book?" If the
  honest answer is 'almost never', then A is a schema migration and a new concept in service
  of a feature nobody uses, and the *right* answer might be to delete the merge window
  entirely and tell people to use one file. Pushed hard on this. The panel's counter: the
  fork's owner is mid-migration from Quicken and will plausibly be running two books in
  parallel during the evaluation, which is exactly the merge case — and issue #54's attachment
  bug is in the merge path, implying someone does use it. Not fully resolved; see the flag.

**Genuine disagreement**: the Adversarial Expert does not accept that merge earns a schema
change, and would ship B and stop. The other six would ship B and schedule A. Nobody argued
for expanding formats before the lineage check exists — that much was unanimous.

**Panel flags:** would benefit from **product-telemetry / user-research** input that none of
the seven roles can supply — specifically, whether whole-book merge is a real workflow for
this product's users or a vestigial one. That single fact decides between "B and stop" and
"B then A".

### Needs your decision?

**Partly — yes on scope, no on direction.** The direction (fix the honesty and the batch
behaviour first; do not broaden formats until merge can verify what it's merging) is a
confident engineering recommendation. Whether whole-book merge deserves a lineage id and a
migration — or should be retired — is a product call about how you personally expect to use
this product during and after the Quicken migration. **Needs your decision.**

---

## D-26 — Is QIF import worth keeping?

**From:** Phase 6 OQ20 · **Cluster:** `import-export`

### ELI10

You're moving house, and you want to bring twenty years of financial records with you. Your
old program can write those records out as a file. One of the oldest and most common shapes
for that file is called QIF — think of it as a very plain list, one fact per line, with a
single letter at the start of each line saying what kind of fact it is. `D` for date, `T` for
amount, `P` for who you paid.

This program can read QIF files. But the way it reads them has a problem you'd notice
immediately if you were doing it by hand: **it writes each record into the new book as it goes,
and if it hits a line it doesn't understand, it stops dead and leaves everything before that
point already written.** Import a file with 3,000 transactions and a weird line at number
2,700, and you now have 2,699 transactions in your book, a message saying "unknown format on
line 8,412", and no way to tell which ones made it. Run it again and you'll get duplicates.
There's no undo.

It's also picky in a way that doesn't match reality. Real exported files often begin with
sections describing your accounts and your category list. This program doesn't know those
sections exist and falls over on the very first line of a full export from Microsoft Money.

So: is this worth repairing, or should the program say "export your data as one of these
other formats instead"?

That question matters more here than it normally would, **because this whole fork exists to
move somebody off Quicken.** If the QIF door is the door they're walking through, it needs to
work. If it isn't, fixing it is expensive charity.

### What the code actually does

- `QifImporter.cs` is **451 lines** — small. Six `throw` sites: unknown account type, account
  type mismatch, unknown `C` (cleared) code, illegal self-transfer, split with no amount,
  unknown field code.
- **No `BeginUpdate`/`EndUpdate`.** Every other importer wraps its writes (P6-ALL-1). This one
  writes straight through, so every list in the UI churns row-by-row while it runs, and a
  throw leaves a partial commit.
- The `!Type:` header check maps account kinds, and **the same check refuses correct merges and
  permits wrong ones** (Phase 6 OQ6) — an orthogonal bug, but it lands on the same lines any
  rewrite would touch.
- A first-time import marks nothing `Unaccepted` (Phase 6 OQ7), so P6-QIF-8's "review what
  arrived" promise holds only for merges into a non-empty account.
- P6-QIF-7 is a deliberate, documented design choice — reconciled-in-the-old-product arrives
  merely *cleared* — and is genuinely good. Any replacement must keep it.

### Alternatives

#### A. Rewrite QIF properly: parse-all-then-commit, with an error report

Two passes. Pass one parses the entire file into an in-memory result with a per-line
diagnostic list and **commits nothing**. Pass two, only if the user accepts, writes inside a
single `BeginUpdate`/`EndUpdate` scope. Unknown field codes and unrecognised sections become
*warnings on a report*, not exceptions. Add `!Account`, `!Type:Cat`, `!Type:Class` section
handling. Show the user a summary before writing: "3,000 transactions, 14 lines not
understood (listed), 2 accounts will be created".

**Pros**
- Turns the single worst property of the importer — irrecoverable partial state — into a
  non-event. This is the fix that matters; the section handling is secondary.
- A parse-only pass is **trivially unit-testable** with no database at all. The current design
  is untestable without a live `MyMoney` graph, which is why it has no tests.
- 451 lines is small enough that a rewrite is a days-not-weeks task, and the field semantics
  (the genuinely fiddly, knowledge-heavy part) are already correct and can be lifted wholesale.
- Fixes Phase 6 OQ5, OQ6 and OQ7 in the same pass, because they all live in the code being
  restructured.

**Cons**
- QIF is an *under-specified* format. "Parse it all first" means deciding what to do with
  constructs no spec pins down — two-digit years, `'` vs `/` date separators, locale-dependent
  decimal separators, memo lines longer than the format allows. A strict parser rejects real
  files; a lenient one silently misreads them. The current code sidesteps this by throwing,
  which is at least loud.
- Building the diagnostic report is most of the work, and it is UI work (D-15's shared dialog
  base, D-18's export question) as much as parser work.
- Investing in a format that is genuinely in decline.

**Unintended consequences**
- A two-pass design needs the whole file in memory. Fine for 3,000 transactions; a 200 MB
  twenty-year export from a shared business book is a different matter, and "parse all first"
  quietly sets a file-size ceiling the streaming version doesn't have.
- The moment the importer reports "14 lines not understood", users will ask what they were and
  want to fix them. That's a *text-editing* affordance nobody planned, and the alternative is
  a report the user can't act on — which is arguably worse than the current message, because
  the current message at least gives a line number.
- Making QIF the good, transactional, well-reported importer makes it **better than the OFX and
  CSV paths**, which have their own issues. Users will notice and route everything through
  QIF, which is the format the panel least wants to be the main door.

#### B. Retire QIF; make OFX/QFX and CSV the supported migration routes

Delete `QifImporter`, remove the menu item and the `.qif` dispatch, and invest the same effort
in the CSV mapping importer (which already has column mapping, sign flipping and saved
mappings — P4-IMPORT-7…10) so that a CSV export from anything becomes a first-class route.
Document the migration path explicitly.

**Pros**
- Removes 451 lines of the least-safe code in the product plus four tracked open questions,
  at a cost of zero.
- CSV is the format *every* tool can emit, including modern Quicken, Excel, and every bank's
  web export. The mapping importer already handles the hard part (columns differ per source).
- Concentrates import investment where it also serves ongoing use, not just one-time migration.
  A user imports CSV forever; they import QIF once.
- Sidesteps D-25's format-sprawl problem instead of adding to it.

**Cons**
- **CSV loses structure QIF carries.** Splits, transfers, cleared status, cheque numbers,
  investment actions — QIF encodes all of these and the importer reconnects transfers across
  accounts (P6-QIF-4), which CSV fundamentally cannot. Telling a user with twenty years of
  split transactions to export CSV is telling them to lose their splits.
- Removing the only route in for a format is irreversible in practice — nobody adds it back.
- If the fork's owner has a QIF file in hand *right now*, this is the option that blocks them.

**Unintended consequences**
- QIF's transfer reconnection (`Importer.FindMatchingTransfer`/`FindSplitTransfer`) lives on the
  shared `Importer` base and is used by the QIF path. Deleting QIF orphans genuinely useful
  matching logic that the CSV path does not currently call — so "delete QIF" quietly means
  "delete transfer reconnection" unless someone rewires it, which nobody will notice until a
  user's transfers come in as 2× unrelated transactions.
- Opening balances (P6-QIF-5) arrive as real `Account.OpeningBalance` via QIF. Via CSV they'd
  arrive as a transaction dated day one, which is a visibly different book.

#### C. Keep QIF, but make it non-destructive the cheap way: import into a scratch book

Don't rewrite the parser. Instead, run the *existing* importer against a **fresh, empty
in-memory `MyMoney` graph**, let it throw wherever it likes, and only merge the scratch graph
into the user's real book if it completed. A throw costs the user nothing but a message.

**Pros**
- Delivers the single most valuable property of option A — never leave the user's book in a
  half-written state — for a fraction of the work, and without touching 451 lines of fiddly,
  correct-today field handling.
- The merge step already exists (it's the same identity/heuristic merge D-25 is about), so
  this composes with whatever D-25 decides rather than competing with it.
- Preserves every behaviour users may already depend on, including the quirks.
- Reversible: if it turns out QIF is genuinely load-bearing, this is a clean base to then
  rewrite on. If it turns out nobody uses it, nothing was over-invested.

**Cons**
- Still throws on the first unknown line, so a file that is 90% valid still imports 0%. That's
  *safer* than today but not *more capable* — the user with a Microsoft Money full export is
  still stuck at line one.
- Two `MyMoney` graphs alive at once has memory and event-wiring implications; the domain model
  is not obviously reentrant in that way (`AttachmentManager`, `AttachmentWatcher` and the
  settings singletons all assume one graph).
- Feels like a workaround, because it is one. Someone will have to own the decision not to
  finish the job.

**Unintended consequences**
- A scratch graph with no attachment manager and no watcher attached is a *different*
  environment from the real one. Any importer behaviour that depends on those (and
  `AttachmentManager` subscribes to change events globally) behaves differently in the
  scratch pass — a class of bug that only shows up in production.
- If the scratch-graph pattern works, it becomes the obvious answer for OFX and CSV too, which
  is good — but it also becomes a load-bearing architectural pattern that was adopted as a
  shortcut, without the design attention a load-bearing pattern deserves.

### Recommendation

**C as the immediate move, with A as the target if — and only if — a real QIF file is actually
on the critical path for this fork's migration.** Not B.

The argument that won: the panel separated two things the register bundles. "Is QIF *safe*"
and "is QIF *capable*" are different questions with very different price tags. Unsafe is
unacceptable and cheap to fix (C). Incapable is annoying and expensive to fix (A). Retiring it
(B) fails on one concrete fact: **CSV cannot carry splits or transfers**, and a Quicken user
with twenty years of history has both. That makes B a data-losing migration dressed as a
simplification, and the panel would not recommend it for a product whose entire reason for
existing in this fork is that migration.

- **Developer**: C is maybe 100 lines and a careful look at what subscribes to `MyMoney`
  change events. A is a genuine rewrite but a small one — 451 lines, and the field semantics
  survive. Warns that the scratch-graph approach needs `BeginUpdate` on the *merge* step
  regardless, so P6-ALL-1 gets satisfied either way.
- **Test Engineer**: A is the only option that produces a testable unit (a parser with no
  database). C is testable as an integration property — "throw mid-import leaves the book
  byte-identical" — which is exactly the regression nobody can currently write. Wants that
  test either way, and points out it can be written *against today's code as a failing test*
  before anything changes.
- **Architect**: sceptical of C becoming permanent. Accepts it on the condition that the
  scratch-graph pattern is either promoted deliberately to all importers or confined
  explicitly to QIF with a comment saying why. Drifting into it accidentally is how the
  current mess accumulated.
- **UI/UX**: the current failure mode — a message box naming a line number in a file the user
  has never opened — is the worst possible output. Even without A's full report, C should
  say *"nothing was changed"* prominently, because the user's first fear on seeing an import
  error is "what did it half-do to my data". Answering that is most of the value.
- **Operations Engineer**: C has no migration story at all, which is its main virtue. A needs
  none either. B needs a release note explaining a removed capability, which for a
  personal-finance app is the kind of note that makes people not upgrade.
- **Data Engine Expert**: opts out. *"This is an in-memory transactionality question, not a
  storage one — the writes all funnel through the same persistence layer whichever option
  wins. Nothing to add."*
- **Adversarial**: "You are all assuming a QIF file exists." Modern Quicken (US, post-2018)
  restricts QIF export to cash, asset and liability accounts — **it will not export a checking
  or brokerage account to QIF at all.** If the owner is on current Quicken Classic, the QIF
  door may already be nailed shut from the other side, and the actual migration route is
  Quicken's own QXF, or per-account CSV, or the OFX/QFX files the bank provides. In that case
  all three options are answering a question that isn't being asked. Strong point; the panel
  could not resolve it without knowing the owner's Quicken version. It also cuts *against* B:
  if QIF isn't the migration route, then QIF isn't urgent, and deleting it is equally
  unurgent — C's cheapness is the right level of investment for something that isn't on the
  critical path.

**Genuine disagreement**: none on direction, once the Adversarial point landed — it actually
strengthened the case for C by lowering the stakes. The Architect's reservation about C
becoming a permanent shortcut is recorded rather than resolved.

**Panel flags:** would benefit from **hands-on Quicken-migration expertise** — specifically,
what the owner's Quicken version can actually export, and whether QIF is reachable at all.
None of the seven roles can answer that from the codebase, and it materially changes the
priority (though not the choice) here.

### Needs your decision?

**No on the engineering choice — yes on one input.** The panel is confident recommending C
over A and firmly against B. But **please confirm what your Quicken version will export**: if
QIF is available and you intend to use it, A moves up the list; if it isn't, C is the whole
answer and QIF stays a legacy convenience.

---

## D-27 — What happens when an imported transaction isn't in the account's currency?

**From:** Phase 6 OQ23 · **Cluster:** `import-export` · *Defect instances: #56*

### ELI10

Your bank account is in dollars. One day the bank sends the program a record of a purchase
you made in euros. What should the program do?

Right now: **nothing at all.** It reads the "this is in euros" note, throws it away, and
records the number as if it were dollars. Spend €50 and your dollar account goes down by 50
dollars.

There are two safety checks in the program written specifically to catch this. Neither one
runs. One has had its whole body commented out with a note saying *"todo: how to support
multi-currency properly"*. The other has its condition backwards in a way that guarantees it
can never fire — it only complains about a currency if it has already complained about that
currency before, and it only records "I've complained about this one" *inside* the complaint
it can never reach. So the list starts empty and stays empty forever.

Fixing those two checks is a straightforward bug and is already written down as issue #56.
The decision isn't *whether* to fix them. The decision is **what the fixed check should do**,
and there are four genuinely different answers:

1. **Refuse.** Don't import it; tell the user.
2. **Import it, flag it.** Bring it in at face value, but mark it so the user can see it's wrong
   and fix it by hand.
3. **Convert it.** Use the exchange rate the program already stores and record the dollar
   equivalent.
4. **Remember what it really was.** Store "this was €50, at this rate, on this day" — the truth
   — and show dollars.

The real-world consequence is not small. Option 3 without care produces a book that *looks*
correct and is quietly wrong, because the rate the program has stored is today's rate, not the
rate on the day of the purchase. Option 4 is the only one that matches what your bank
statement actually says — and it's the only one that needs the program's records to grow a new
field they've never had.

### What the code actually does (correction to the register entry)

The register says the fix could "store the transaction in its own currency (which the domain
model's P1-CUR-* scenarios suggest it can represent)". **The panel's re-read says it cannot.**

- `Currency` lives on **`Account`** (`Money.cs:3010`), not on `Transaction`. `Transaction`
  (`Money.cs:10888`), `Split` (`13870`) and `Investment` (`14433`) have no currency member at
  all.
- P1-CUR-1…5 describe a *currency list*, *rates*, a *home currency for totals* and *formatting*.
  They describe **account-level** currency with display-time normalisation
  (`Account.GetNormalizedAmount`). They do not describe per-transaction currency.
- So option 4 is **a schema change to the three largest tables in the product**, not a
  configuration of something already there. That materially changes its cost and moves it
  into D-39's territory.

Two further findings the register does not mention, both of which narrow the choice:

- **`CURRATE` is never read.** `ProcessCurrency` reads only `CURSYM`. The OFX record carries
  the rate the institution used, right next to the symbol — the single most valuable piece of
  data for any conversion option — and it is discarded.
- **`ORIGCURRENCY` is never read, and it means the opposite of `CURRENCY`.** In OFX, a
  `<CURRENCY>` aggregate means *the amount is expressed in that currency* (conversion needed).
  An `<ORIGCURRENCY>` aggregate means *the amount is already in the account's currency* and the
  foreign currency is recorded for information only (no conversion needed, and converting
  would be a double conversion). The code handles neither, so a fix that treats every currency
  tag as "needs converting" will **corrupt every `ORIGCURRENCY` transaction** — which, for a
  US card used abroad, is the *common* case. This is a live trap in front of option 3.

### Alternatives

#### A. Refuse the statement, name the problem

Restore `CheckUSD` as a real check against `Account.NonNullCurrency` (not hard-coded USD),
wire an equivalent into the bank and card paths, and on mismatch add a real error row and skip
that statement. Implement `ORIGCURRENCY` correctly so it is *not* treated as a mismatch.

**Pros**
- Cannot produce a wrong number. For a personal-finance product, "I didn't import it" is
  always recoverable; "I imported it wrong" may never be noticed.
- Smallest correct change. Mostly restoring code that already exists in comment form.
- The error-row plumbing (`DownloadData.AddError`) is already there and already surfaced in the
  download results panel, so the user-facing half is free.

**Cons**
- A user with a genuinely multi-currency account (a euro account at a US bank; a UK user whose
  bank declares GBP against an account the user set up before adding currencies) is simply
  blocked, with no path forward except editing the account's currency.
- "Refuse" at *statement* granularity is coarse: one foreign transaction blocks the whole
  month's download.

**Unintended consequences**
- `Account.Currency` is frequently unset or defaulted on accounts created before the user
  thought about currency — `FindCurrencyOrDefault` papers over it. Turning on a strict check
  will surface that as a wave of "your statement is in USD but your account is in (nothing)"
  errors on accounts that have worked fine for years. **The check's first release will look
  like a regression** unless unset-currency accounts are treated as "adopt the statement's
  currency" rather than "mismatch".
- Refusing at the statement level interacts with the OFX download's own retry logic: a
  permanently-refused statement may be re-requested every download, forever.

#### B. Import at face value, flag the transaction for the user

Bring the transaction in unconverted, but mark it — a `TransactionFlags` bit, or reuse
`Unaccepted` — and surface it with an explanation. Let the user decide: edit the amount, move
the transaction, change the account's currency, or accept it.

**Pros**
- Never blocks. The user's book stays in step with their bank, which is the whole point of
  downloading.
- Matches the product's existing philosophy for *everything else* it's unsure about:
  `Unaccepted` already means "I brought this in, you should look at it" (P6-QIF-8, P6-ALL-2).
  This is the consistent answer, not a new concept.
- No schema change, no rate dependency, no conversion maths to get wrong.

**Cons**
- The book is *temporarily wrong*, and the flag is the only thing saying so. A user who
  bulk-accepts (which the UI makes easy) silently ratifies a bad number.
- Account balances and every report are wrong until the user acts, and nothing stops them
  reconciling in that state.

**Unintended consequences**
- Flags are per-transaction; the *balance* is not. A flagged transaction poisons the account
  balance, the net-worth report (D-21) and the reconciliation (P3-RECON-*) with no flag of
  their own. The signal is attached to the wrong object for the damage it describes.
- Reusing `Unaccepted` for this overloads a bit that already means "newly imported" — after
  the first accept-all, there is no distinguishable state left. A dedicated flag is really
  required, which makes this less cheap than it first looks.

#### C. Convert on import using the recorded rate, and keep the evidence

Use the OFX's own `CURRATE` — the institution's rate, on the day, which the code currently
discards — to convert, store the converted amount as the transaction amount, and record the
original amount, original currency and rate used in the memo or a `TransactionExtra`. Fall
back to B (flag, don't convert) when no `CURRATE` is present. Implement `ORIGCURRENCY` as
"already converted, record only".

**Pros**
- Produces the number the user's own statement shows, using the rate their own bank used. This
  is the *correct* answer, not an approximation — and the data to do it is already in the file
  being parsed.
- No dependency on `Currency.Ratio`, which is the right call: D-4 establishes that `Ratio` is
  dollar-anchored, ambiguous and *current*, so using it to convert a historical transaction is
  wrong twice over.
- `TransactionExtra` already exists (P1-TXN-15, P1-TAX-2/3) as a place to hang per-transaction
  side data without a schema change to `Transaction` itself.

**Cons**
- Only works when the institution supplies `CURRATE`. Many don't, so the fallback path is
  mandatory and the behaviour is inconsistent across banks — the hardest kind of behaviour to
  explain to a user.
- Getting `CURRENCY` vs `ORIGCURRENCY` backwards produces a double conversion that looks
  plausible and is very hard to spot. This is the highest-risk option to implement correctly.
- Does nothing for QIF or CSV imports, which carry no rate at all.

**Unintended consequences**
- A converted transaction is no longer reversible to what the bank said. If the user later
  fixes their account currency, or the rate is found to be wrong, there is no clean undo —
  the original is in a memo, which is free text. Storing evidence in a memo means **the
  evidence is editable and un-queryable**, which will be regretted the first time someone
  needs to audit it.
- This quietly establishes that *transaction amounts are derived values*. Nothing else in the
  product works that way, and reports that re-derive totals from amounts have no idea.

#### D. Make currency a property of the transaction (the real model fix)

Add currency (and rate-at-time) to `Transaction`/`Split`/`Investment`, store what the bank
actually said, and convert only at display and aggregation time via
`Account.GetNormalizedAmount`'s existing normalisation path.

**Pros**
- The only option that is *right* rather than *good enough*. The stored record matches the
  statement; every derived view is a view.
- Makes D-4 (what a currency ratio means) answerable rather than perpetually fudged, and makes
  D-19 (what a mixed-currency total should say) implementable.
- Every importer benefits, not just OFX.

**Cons**
- Schema change on the three biggest tables, across SQLite, SQL Server, SQL CE, XML and CSV
  stores. Lands on D-39 (schema upgrade) and D-35 (which engines survive) before it can start.
- Every aggregation in the product — balances, reports, charts, reconciliation, tax — has to
  learn that amounts have currencies. That's a wide blast radius through code the panel has
  not audited.
- Historical rates are needed to make it meaningful, and the product has no historical rate
  store (`Currency.Ratio`/`LastRatio` is current-and-previous, nothing more).

**Unintended consequences**
- Per-transaction currency makes *account*-level currency ambiguous rather than replacing it,
  and the two will disagree. Every "what currency is this account in" question in the codebase
  becomes "what currency is this account's *balance* in", which is a different and harder
  question when the transactions disagree.
- Rebuilding balances under mixed currencies means totals depend on when you ask, which breaks
  the (currently safe) assumption that a reconciled balance is stable. Reconciliation (D-12,
  P3-RECON-*) would need rethinking.

### Recommendation

**A as the immediate fix, with C's `ORIGCURRENCY` handling included as part of it, and D
recorded as the right long-term model but explicitly deferred behind D-39 and D-4.**

The reasoning that won: the panel started leaning toward C, because the rate is right there in
the file and throwing it away is galling. The Adversarial Expert killed it for now, and the
Data Engine Expert supplied the decisive detail. The argument:

> Option C requires getting `CURRENCY` vs `ORIGCURRENCY` exactly right, in a codebase with no
> OFX tests, in a file format where the common case (`ORIGCURRENCY` — a US card used abroad)
> is the one that must *not* be converted. Getting it backwards produces a double conversion
> that looks plausible on screen and is invisible in every report. The current bug at least
> produces an obviously wrong number the moment the user compares to their statement. **A fix
> that can silently produce a subtly wrong number is worse than a bug that loudly produces an
> obviously wrong one.**

So: fix the guards (that's #56), make them check the account's actual currency rather than
hard-coded USD, wire them into the bank and card paths as well as investment, implement
`ORIGCURRENCY` as "no mismatch, record only" — and on a genuine mismatch, **refuse and
explain**, rather than guessing.

- **Data Engine Expert** (fully in lane here): D is the only model that survives contact with
  a real multi-currency user, and the panel should say so plainly rather than leave it implied.
  But it is a three-table schema change plus an aggregation rewrite, and it cannot be scheduled
  before D-39 has an answer. Also notes that `SqlMoney` (used for the SQL Server column
  mappings throughout `Money.cs`) is a currency-blind fixed-point type — per-transaction
  currency needs a companion column, not a smarter money type.
- **Architect**: the deferral must be *written down as a deferral*, not left as silence. A
  refuse-on-mismatch check that nobody records as temporary becomes the permanent answer by
  default, and then multi-currency is "not supported" forever without anyone deciding it.
- **Developer**: A is small — uncomment, generalise USD to `account.NonNullCurrency`, add the
  two missing call sites, invert the `currencyErrors` condition. Half a day. C is two days and
  a week of nerves.
- **Test Engineer**: A is testable today — feed `ProcessBankResponse` an OFX fragment with a
  mismatched `CURDEF` and assert an error row. C needs a test matrix of
  `CURRENCY`/`ORIGCURRENCY` × present/absent `CURRATE` × bank/card/investment that nobody will
  write. Strongly prefers the option that can be verified this week.
- **Operations Engineer**: flags the unset-currency wave as the real deployment risk of A —
  accounts with no currency set must be treated as matching, or the first release of this fix
  will look like a mass import failure. Wants that behaviour explicit in the fix, not emergent.
- **UI/UX**: "refuse" must not mean a silent skip. The download results panel already has an
  error row concept; the message should say *which* currency, *which* account, and what the
  user can do (set the account's currency, or import manually). A refusal the user can't act
  on is a dead end.
- **Adversarial**: asked whether a single-user, US-based, dollar-denominated owner should spend
  anything here at all. Conceded the answer is yes, but only at option A's price — because the
  current code doesn't just fail to *support* foreign currency, it silently produces wrong
  balances, and that's a correctness bug regardless of whether the feature is wanted.

**Genuine disagreement**: the Data Engine Expert would prefer D scheduled rather than merely
recorded, and considers "deferred behind D-39" a polite way of saying never. Noted rather than
resolved.

### Needs your decision?

**No.** The panel is confident. Fix the guards, refuse on genuine mismatch, handle
`ORIGCURRENCY`, treat unset account currency as a match, and write the per-transaction-currency
model down as a deferred decision rather than an omission. The only thing you might overrule is
whether to spend anything here at all — and the panel's answer is that this is a
wrong-numbers bug, not a missing feature, so yes.

---

## D-44 — Bring scanning back, or delete it?

**From:** Phase 4 OQ22 · **Cluster:** `attachments-documents`

### ELI10

There's a window in the program for keeping receipts attached to a purchase. Originally, it
had a **Scan** button: put a receipt in your scanner, press the button, and the picture
lands straight on the transaction.

That button isn't there any more. But it hasn't been *removed* — it's been **commented out**,
which means the code is still sitting in the file with a mark in front of every line saying
"ignore this". The window's own description still calls it "ScanDialog". And the part that
translates scanner errors into English — *"paper jam"*, *"cover open"*, *"lamp off"* — is
still live, compiled code that nothing calls.

So the program is carrying around the remains of a feature. The question is whether to bring
it back, replace it with something more modern, or finally sweep it out.

The honest real-world context: almost nobody scans receipts on a flatbed any more. They
photograph them with a phone, and the photo shows up in a cloud folder. If the program could
just *watch a folder* and pick up whatever lands there, that would cover what scanning used
to do and a lot more besides — and, as it happens, the program **already watches folders**
for exactly this purpose.

### What the code actually does

- The commented block in `AttachmentDialog.xaml.cs` (lines ~264–345) covers scanner selection
  (`ShowSelectDevice`), acquisition (`ShowAcquireImage`), transfer (`ShowTransfer`) and error
  handling. The XAML button is commented out at line 84.
- `GetWiaErrorMessage` (line 352) and `enum WiaErrorCode` (line 1450) are **live, compiled,
  uncalled** code.
- All `WIA.*` references are *inside comments only*, so the **COM interop reference has already
  been removed from the project**. Reviving means re-adding a COM dependency, not
  uncommenting — which is a much bigger job than it looks, and a fussy one under .NET 10.
- The public entry point is still named **`ScanAttachments`** (line 720) and is the product's
  actual "open the attachment window for this transaction" API. The name is the last live
  trace of the original intent.
- **`AttachmentWatcher` already watches the attachment directory** (P8-ATT-7) and picks up
  files dropped in from outside, in the background, and sets the paperclip. The
  "watch a folder" capability is not hypothetical here — it is the thing that already works.

### Alternatives

#### A. Revive WIA scanning

Re-add the COM interop, uncomment, modernise the async handling, test against real hardware.

**Pros**
- The intent is fully written down; the flow is understood.
- For a user who *does* have a document scanner (and people who keep twenty years of financial
  records disproportionately do), it is the fastest possible path from paper to filed.
- Nothing else in the redesign is blocked by it.

**Cons**
- WIA interop is genuinely unpleasant: COM apartment threading, driver-specific behaviour,
  `ShowSelectDevice` blocking the UI thread, and error modes (the very enum still sitting in
  the file) that only appear with hardware attached.
- **Untestable in CI.** No scanner, no test. Every FlaUI test in this repo runs unattended
  (per the project's own standing rule); a feature that requires hardware is a permanent hole
  in the suite.
- Re-adds a native dependency to an app that currently has none of consequence, which
  complicates packaging (ClickOnce, `MyMoneyPackage.sln`) and platform targeting.

**Unintended consequences**
- COM interop in a WPF app pins threading assumptions that the redesign's async/MVVM direction
  (D-2's view-state question) will want to move away from. Reviving it now buys a constraint
  the redesign is trying to shed.
- One hardware-dependent feature in the product sets a precedent: the next request is TWAIN
  support for older scanners, then multi-page feed handling, then OCR. Scanning is a doorway
  to a scope the product has no business being in.

#### B. Replace it with a watch-folder / import-from-folder flow

Let the user nominate one or more inbox folders (phone-sync folder, scanner's output folder,
Downloads). New files appearing there show up in the attachment window as "unfiled paperwork"
that can be dragged onto a transaction — or, better, the product proposes matches by date and
amount if the file name contains them.

**Pros**
- Covers scanning (every scanner writes to a folder), phone cameras, email attachments saved
  to disk, and bank PDFs — with one mechanism.
- **Mostly already built.** `AttachmentWatcher` does the watching; `AttachmentManager` does the
  filing; the P8-ATT-7 background scan already turns files-on-disk into paperclips. What's
  missing is the *inbox* concept and the UI to triage it.
- Fully testable: a test drops a file in a folder and asserts what happens. No hardware.
- Aligns perfectly with P8-ATT-2's deliberate "your documents are ordinary files" property.

**Cons**
- The "unfiled paperwork" inbox is net-new UI, and it's a surface with real design work in it
  (what does a triage queue look like in Fluent? how does the user dismiss something that
  isn't paperwork?).
- Watching arbitrary user folders is more invasive than watching the app's own directory —
  permissions, network drives, cloud-sync placeholder files that aren't really there yet
  (OneDrive Files On-Demand will hand you a 0-byte reparse point).
- Doesn't help the user standing at a scanner who wants one click.

**Unintended consequences**
- A watch folder outside the product's own tree means the product now reacts to files it
  doesn't own. If a sync client rewrites a file mid-scan, or an antivirus quarantines one, the
  product's watcher is the thing that notices — and `AttachmentWatcher`'s current error
  handling is a background task with no user-visible failure path (Phase 8 OQ23's "background
  failures are swallowed completely").
- Auto-proposing matches by date and amount is duplicate-detection by another name, which
  collides with D-12's decision about when duplicate detection should run.

#### C. Delete the dead block, keep the entry point, ship neither

Remove the commented code, `GetWiaErrorMessage` and `WiaErrorCode`; rename `ScanAttachments`
to something honest; fix the class comment. Rely on drag-and-drop (which works) and the
existing background watcher (which works). Revisit only if asked.

**Pros**
- Free, immediate, and removes a standing source of confusion for anyone reading the file —
  including whoever fixes #54, which lives in this exact file.
- The capability gap is genuinely small: drag-and-drop from any folder already works, and the
  watcher already picks up files copied in.
- Leaves B available later at no extra cost; deleting dead WIA code does not foreclose a
  watch-folder feature.

**Cons**
- Discards a written-down intent that someone once cared about.
- Does nothing to improve the paperwork workflow, which is the *point* of the attachments area.

**Unintended consequences**
- Deleting `GetWiaErrorMessage` removes the only evidence in the codebase that scanning was
  ever contemplated. That's mostly good, but it means the decision has to be recorded
  *somewhere* (this document) or it will be rediscovered and re-debated.

### Recommendation

**C now, B as the actual feature, decisively not A.**

The reasoning that won: the panel's re-read found the decisive fact — **the COM reference is
already gone**, so "revive it" is not uncommenting, it is re-integrating a native dependency.
That reframes A from "cheap revival" (as the register understandably reads it) to "new native
integration, untestable in CI, for a declining use case". Meanwhile B turns out to be *mostly
already implemented*: the watcher exists, the filing exists, the paperclip-from-disk path
exists. The gap between where the product is and where B lands is one inbox concept and one
triage surface.

Do C immediately — it costs nothing, it cleans the file that #54's fix has to touch anyway,
and it removes the misleading class comment. Then treat B as the real attachments feature
whenever the attachments area is next opened.

- **Developer**: A is a week and a permanent CI hole. C is twenty minutes. B is a few days on
  top of machinery that already exists. The `ScanAttachments` rename should happen with C —
  it is the single most misleading identifier in the attachments code.
- **Test Engineer**: decisive. A produces a feature no unattended test can exercise, which in
  this repo (every FlaUI suite runs unattended) means a feature with zero regression coverage
  in a file that already has a HIGH-priority data-loss bug. Will not endorse A.
- **UI/UX**: an "unfiled paperwork" inbox is a better product than a scan button even for
  users who own scanners — it's where the scan *lands*. Modern Fluent patterns support this
  well (a `ListView` triage pane with drag targets). Notes that phone→cloud→folder is how
  receipts actually reach a desktop in 2026, and a scan button doesn't participate in that at
  all.
- **Operations Engineer**: A affects packaging (COM registration, bitness) and the ClickOnce
  story. C reduces the binary. B adds a settings surface for inbox folders and a permissions
  question on network/cloud paths — manageable, but real. Flags OneDrive Files On-Demand
  placeholders as a concrete hazard for B: `File.Exists` is true, the bytes are not local, and
  reading blocks on network.
- **Architect**: B generalises correctly — "paperwork arrives from outside" is the real
  concept, and scanning is one instance of it. Supports C-then-B. Warns that the inbox must not
  become a second, parallel attachment store; it is a staging area with a strict lifecycle, or
  it becomes the place documents go to be forgotten.
- **Data Engine Expert**: opts out. *"Attachments live on the filesystem beside the data file,
  by design (P8-ATT-2). Nothing here touches storage. Nothing to add."*
- **Adversarial**: pushed the simplest answer nobody had raised — *"drag-and-drop already
  works. Is an inbox actually better than dragging a file from Explorer onto a transaction?"*
  The panel's answer: for one receipt, no. For the twenty that accumulate between sessions,
  yes — because the user has to remember which ones they've already filed, and an inbox
  remembers for them. Accepted, but with the caveat that B should be judged on whether it
  beats Explorer, and should not be built if it can't.

**Genuine disagreement**: none. This was the panel's fastest consensus.

### Needs your decision?

**No for C — just do it.** **Mildly yes for B**: whether an unfiled-paperwork inbox is worth
building depends on how much paperwork you actually intend to keep in this product. If
attachments are a nice-to-have for you, C alone is the complete answer and B should not be
built.

---

## D-45 — What happens to paperwork when its transaction is deleted?

**From:** Phase 8 OQ13 · **Cluster:** `attachments-documents`

### ELI10

You've got a purchase in your book, and you've attached the receipt to it — a photo, or the
PDF the shop emailed you. That receipt is the only copy you have.

Now you delete the purchase. Maybe it was a duplicate. Maybe you fat-fingered a row and hit
Delete. Maybe you're tidying up.

**The receipt is deleted too. Immediately. Permanently. With no warning, and not even into the
Recycle Bin.**

The person who wrote that code knew it wasn't right — there's a note in the file that says
*"todo: would be nice to warn the user they are losing them…"*. The note appears **twice**,
in two different places, because the same thing happens in two different code paths.

Here's what makes it genuinely uncomfortable. Deleting a transaction is one of the easiest,
lowest-stakes things you can do in the program — one key. And in the program's own internal
bookkeeping, the deleted transaction isn't *really* gone yet: it's marked "deleted" and
cleaned up later, which is how programs usually leave themselves room to change their mind.
But the *file* is gone the instant you press the key. The cheap, reversible action has an
expensive, irreversible side-effect bolted to it.

So: should deleting a transaction ask first? Should the receipt go somewhere you can get it
back from? Or should receipts simply stop being deleted at all?

### What the code actually does

- `AttachmentManager.OnMoneyChanged` (line ~150) and `OnTransactionsChanged` (line ~193) both
  call `DeleteAttachments(t)` on `ChangeType.Deleted`, each carrying the same todo comment.
  **Both handlers are subscribed**, so for a transaction reaching both paths the deletion runs
  twice (harmless today, because the second pass finds nothing — but it means two places to
  fix, and one may be missed).
- `DeleteAttachments` → `TempFilesManager.DeleteFile` → **`File.Delete`**. Not `Recycle`. The
  only softening is that a *locked* file is queued for retry (P8-ATT-9) — i.e. the one case
  where the file survives is the case where Windows wouldn't let it die.
- **`ChangeType.Deleted` is a soft delete in this model.** Per the project's own notes,
  `PersistentObject.OnDelete()` flips the change type and fires an event; actual removal from
  the container happens on save. So the domain object is in a recoverable state at exactly the
  moment the file becomes unrecoverable.
- This fires on **every** `Deleted` change — including deletions from an import rollback, a
  merge, or any future bulk operation. Nothing distinguishes "the user pressed Delete on this
  row" from "the engine removed this during a batch".

That last point is the one the panel considers most serious, and the register does not raise
it: **the trigger is a model event, not a user action.** Any code path that deletes
transactions — now or in future — destroys files as a side effect, whether or not a human
intended a deletion.

### Alternatives

#### A. Warn and confirm

On deleting a transaction that has attachments, show a confirmation naming the files.

**Pros**
- Simple, obvious, and directly addresses the asymmetry: an irreversible consequence gets an
  interception.
- No storage changes, no lifecycle to manage.

**Cons**
- **Makes a common action heavier for a rare consequence.** Most deleted transactions have no
  attachments — but the user who *does* hit the dialog is mid-cleanup, deleting several rows,
  and will click through it.
- Multi-row delete either produces N dialogs or one vague one.
- Does nothing about the non-user-initiated paths, where there is no one to ask. A confirmation
  dialog raised from a background merge is worse than no dialog.

**Unintended consequences**
- A confirmation implies a choice, so the dialog needs a "delete the transaction but keep the
  files" option — and if you keep them, they are now orphaned files that nothing references
  and nothing will ever clean up. **The dialog creates the orphan problem that option B is
  designed to manage**, without any of B's management.
- Modal dialogs from model-change events are exactly the pattern behind Phase 8 OQ3 ("a
  question asked from a background thread answers itself"). This would add another instance of
  a known-broken pattern.

#### B. Orphan the files into a holding area with a recoverable window

Move attachments of deleted transactions into a `.Attachments\.deleted\<transaction-id>\`
folder (or similar), with a timestamp. Sweep anything older than N days on startup, the same
way `TempFilesManager` already sweeps. Offer no UI at first — the folder *is* the UI, which is
consistent with P8-ATT-2's "your documents are ordinary files you can find in Explorer".

**Pros**
- Zero friction: nothing asks, nothing blocks, nothing changes for the 95% of deletions with
  no attachments.
- Genuinely recoverable, by the user, with Explorer, which is the product's stated philosophy
  for documents.
- **Works for non-user-initiated deletions too** — the merge path, the import rollback,
  anything. It is a property of the deletion, not of the UI that triggered it.
- Nearly free to build: `TempFilesManager` already owns "files to deal with later, including
  across restarts". This is a move instead of a delete, plus an age-based sweep.

**Cons**
- Silent recovery is only discoverable if the user knows the folder exists. Without any UI,
  a user who deletes a receipt by accident and doesn't know about `.deleted` experiences it as
  the current behaviour.
- Disk grows. For image-heavy users with a retention window, meaningfully.
- The sweep is a *second* place that deletes files permanently, and it runs unattended.

**Unintended consequences**
- A `.deleted` folder inside the attachments tree will be **picked up by `AttachmentWatcher`'s
  directory scan** (P8-ATT-7) unless explicitly excluded — which would set paperclips on
  transactions from files that were supposed to be gone, and possibly re-associate them by id.
  This is a real, specific trap, not a hypothetical.
- Backups (D-50) and the "move my attachments folder" flow (P8-ATT-4) both have to know to
  include or skip the holding area, and neither currently has a concept of an excluded
  subtree.
- It changes the meaning of "delete" from "gone" to "gone for 30 days", which for a user who
  deleted something *because* it was sensitive is a privacy surprise. That interacts with D-43
  (what "password-protect my file" promises) — encrypted book, unencrypted deleted receipts.

#### C. Stop deleting attachments on transaction delete entirely; sweep unreferenced files on demand

Decouple the two. Deleting a transaction deletes the transaction. Attachments become garbage
when nothing references them, and a *user-initiated* "clean up unreferenced documents" action
(with a preview of what it will remove) collects them.

**Pros**
- The clean architectural answer: files have their own lifecycle and are not a side-effect of
  a domain event. Removes the model-event trigger entirely, which is the root problem.
- No confirmations, no retention windows, no hidden folders.
- The cleanup action is a place where the user can *see* what's unreferenced before agreeing,
  which is far better information than a confirmation at delete time.

**Cons**
- Unreferenced files accumulate indefinitely for users who never run the cleanup — and those
  are the same users who will never find it.
- Requires a reliable "is this file referenced" answer. Today, association is by
  **filename-encoded transaction id** in a per-account folder, so an orphan is detectable —
  but only if account folders are unambiguous, which **D-46 says they are not**. Two accounts
  colliding into one folder means a file may look unreferenced in one account's view and
  referenced in the other's. **C is not safely implementable until D-46 is resolved.**
- A cleanup that can delete the wrong file because of a folder collision is worse than never
  cleaning up.

**Unintended consequences**
- Re-creating a transaction with the same id (which the merge path can do) would silently
  re-adopt the old attachments. Sometimes delightful, sometimes very wrong.
- A "clean up unreferenced documents" command is a destructive bulk action, which is a new
  category for this product and needs the undo story (D-49) that doesn't exist.

### Recommendation

**B, with the `.deleted` folder explicitly excluded from `AttachmentWatcher`'s scan, and a
minimal discoverability affordance.** The panel considered this the clearest call of the eight.

The reasoning that won: the register frames the choice as warn / orphan / defer-cleanup, and
the panel's re-read added the fact that settles it — **the deletion is triggered by a model
change event, not by a user gesture.** That eliminates A as a complete answer, because a
confirmation can only intercept the paths where a user is present, and the dangerous paths
(merge, rollback, bulk) are exactly the ones where they aren't. It also weakens C, because C's
correctness depends on D-46, which is unresolved.

B is the only option that is a property of the *deletion* rather than of the *UI*, and it is
the cheapest to build because `TempFilesManager` already implements the hard part (a durable,
cross-restart list of files to deal with later).

On discoverability, the panel split the difference: no dialog at delete time, but the
transaction-delete status message should say *"Transaction deleted (2 documents kept for 30
days)"* when attachments were involved. That is a passive, non-blocking mention that costs
nothing and makes the folder findable — and it fits whatever D-5 decides the status model is.

- **Architect**: B is a compromise, and says so. C is the architecturally correct answer and
  the panel should record that B is a stepping stone to it, not a destination. Once D-46 lands
  and folder identity is unambiguous, the `.deleted` sweep and the unreferenced-file collector
  are the same machinery.
- **Developer**: B is a `File.Move` instead of a `File.Delete`, plus a startup sweep modelled
  on the one `TempFilesManager` already runs. Under a day. Emphasises that the watcher
  exclusion is **not optional** — without it, the feature actively creates phantom paperclips.
- **Test Engineer**: B is the most testable of the three — delete a transaction with an
  attachment, assert the file moved rather than vanished, assert the watcher ignores it, assert
  the sweep removes it past the window (with an injectable clock). A is the least testable: a
  modal raised from a model event is precisely the thing the project's own `AppCrashGuard`
  notes were written about. Asks that the "two handlers, same fix" duplication be collapsed
  into one code path as part of the change, so there is one place to test.
- **UI/UX**: strongly against A. A confirmation dialog on the product's cheapest action
  degrades the whole register's feel, and users learn to dismiss it within a week — at which
  point it provides no protection and costs a keystroke forever. Supports the passive status
  message. Would eventually like a "Recently removed documents" view, but not as part of this.
- **Operations Engineer**: retention window must be a setting with a sane default (30 days),
  and the sweep must be resilient to the folder being on a cloud-synced path. Notes B
  interacts with backup (D-50): a backup taken mid-window contains deleted documents, which
  is either a feature or a surprise depending on the user.
- **Data Engine Expert**: opts out, with one observation. *"Worth noting that the domain's
  delete is soft and the file's delete is hard — the transaction is recoverable at the moment
  the file isn't. If a future redesign makes the domain delete recoverable in the UI (D-49's
  undo), option B is the only one of the three that lets the attachment come back with it.
  Otherwise, nothing to add on storage."*
- **Adversarial**: asked whether anyone deletes transactions that have attachments often enough
  to justify any of this. Then answered their own question against themselves: the frequency
  is irrelevant, because the cost is unbounded — a single lost receipt for a tax-deductible
  expense is a real financial loss, and the product currently offers no recovery at any price.
  Also raised the privacy angle on B (deleted-but-retained documents in an otherwise-encrypted
  book) and asked that it be recorded as a known trade-off rather than discovered later.

**Genuine disagreement**: the Architect regards B as a compromise rather than the answer, and
wants C's direction recorded as the target. Not a dispute about what to build now.

### Needs your decision?

**No.** The panel is confident recommending B. The one thing you might want to set is the
retention window's default (30 days proposed) and whether deleted-document retention is
acceptable in a book you may password-protect (D-43).

---

## D-46 — File documents by account name or by account id?

**From:** Phase 8 OQ14 · **Cluster:** `attachments-documents`

### ELI10

Your receipts and bank statements aren't stored *inside* the program's file — they're kept in
ordinary folders sitting next to it, one folder per account. That's deliberate and genuinely
useful: you can open Explorer, click into "Visa", and see your paperwork like any other
files.

The folders are named after the accounts. And there's the problem, in two parts.

**Part one:** Windows won't allow certain characters in folder names — `:`, `/`, `\` and a
few others. So the program strips them out. That means an account called `Visa: Joint` and an
account called `Visa / Joint` both become a folder called `Visa Joint`. Two different accounts,
one folder, everything mixed together. And because documents inside a folder are matched to
transactions by number, and both accounts have their own numbering, paperwork from one account
can attach itself to a transaction in the other.

**Part two:** the program lets you have two accounts with *exactly* the same name. For bank
statements, it doesn't even use the folder — it keeps a list in memory filed under the raw
name — so two identically-named accounts share one list outright.

The obvious fix is to name folders after the account's internal number instead — `17` instead
of `Visa Joint`. Collisions become impossible. But then you open Explorer and see a row of
numbered folders, and the "you can find your own documents" promise is gone.

So it's a straight trade: **a name a human can read**, or **a name that can't collide**. Or
some third thing that gets both.

### What the code actually does

- Both `AttachmentManager` and `StatementManager` build paths from
  `NativeMethods.GetValidFileName(account.Name)`.
- Account renames are handled (`AttachmentWatcher.OnRenameAccount` with a remembered
  original-name map; `StatementManager.OnRenameAccountFolder` plus `UpdateBundledPointers`).
  **This machinery exists only because folders are named after a mutable property**, and it is
  a meaningful fraction of the complexity in both managers.
- `StatementManager` additionally keys its in-memory index by the raw (unsanitised) account
  name — so its collision surface is different from, and wider than, the folder's.
- Within a folder, a document is matched to a transaction by **id encoded in the filename**, so
  a folder collision is not merely cosmetic — it produces cross-account misattribution.
- `StatementManager`'s bundled-statement feature (P8-STMT-4) stores **relative paths from one
  account's folder into another's**, which means folder names are embedded in stored data, not
  just used at runtime. Renaming folders has to repair those pointers — and any change to the
  naming scheme has to migrate them.

That last point is the migration cost nobody has priced: the folder name is not purely
derived, it is *referenced*.

### Alternatives

#### A. Name folders by account id

`<database>.Attachments\17\`, `<database>.Statements\17\`.

**Pros**
- Eliminates the entire collision class — stripped-character collisions, duplicate names,
  and the statement index's raw-name keying — in one change.
- **Deletes the rename machinery.** Ids don't change, so `OnRenameAccount`, the original-name
  map, `OnRenameAccountFolder` and `UpdateBundledPointers` all become unnecessary. That is a
  real simplification of two of the more intricate classes in the product, and it removes the
  window during which a rename can be interrupted half-done.
- Makes bundled-statement relative paths stable forever.
- Trivially testable.

**Cons**
- Breaks P8-ATT-3's stated value — *"a user looking at the files directly can tell which
  account a document belongs to"* — which the catalog explicitly records as deliberate.
- A user who loses or corrupts their database file is left with a tree of numbered folders and
  no way to know what they contain. Today, the folder names *are* a recovery aid.
- Migration: every existing user's folders have to be renamed, and every stored bundled-path
  rewritten, on first run of the new version. That is a filesystem migration with no
  transaction around it.

**Unintended consequences**
- `Account.Id` is an internal surrogate key. Making it a **user-visible filesystem path**
  promotes it to part of the product's external contract — it can never be renumbered, and any
  future "rebuild the database" or "merge two books" operation that reassigns ids (which
  `XmlImporter.remappedIds` already does) silently detaches every document.
- Numbered folders are hostile to exactly the user who benefits most from P8-ATT-2: someone
  restoring from a backup, or looking for a receipt years later on a machine without the app.

#### B. Readable names plus a manifest

Keep human-readable folder names, but write a small `folders.json` / `index.xml` at the root of
the attachments tree mapping account id → folder name. The product reads the manifest as
authority; the name is presentation. Collisions are resolved by disambiguating suffixes
(`Visa Joint`, `Visa Joint (2)`) recorded in the manifest.

**Pros**
- Keeps both properties: readable in Explorer, unambiguous to the program.
- The manifest is where a rename gets recorded, so renames become a one-line manifest edit plus
  an optional cosmetic folder rename — simpler than today's map-and-repair dance.
- `StatementManager` already maintains a per-account `index.xml`, so a manifest is an
  established pattern here, not a new one.

**Cons**
- Introduces a file that can get out of sync with reality. A user who renames a folder in
  Explorer (which the current design positively invites — the folders are *meant* to be
  browsed) breaks the mapping, and the product has to detect and heal that.
- Two sources of truth is exactly the shape of bug the attachments code already suffers from.
- More code than A, and the recovery logic (manifest missing, manifest stale, folder renamed
  externally) is where the real work lives.

**Unintended consequences**
- The manifest becomes a **single point of failure for every document in the product**. Lose it
  or corrupt it and readable folder names are the fallback — which means the disambiguation
  suffixes must be reconstructible from account names alone, which partially defeats the
  manifest's purpose.
- It creates a file the user must not edit inside a tree the product tells them to browse
  freely. That's an inherently uncomfortable instruction.

#### C. Hybrid — id-stamped readable names

Folder names of the form `<sanitised account name> (<account id>)` — `Visa Joint (17)\`,
`Visa Joint (23)\`. The trailing id makes the folder unique; the leading name makes it
readable. The product resolves a folder by parsing the trailing id and ignores the rest.

**Pros**
- Gets both properties with **no manifest, no second source of truth, and no recovery logic** —
  the folder name *is* the mapping, and the authoritative part of it (the id) is
  self-describing.
- A user in Explorer sees `Visa Joint (17)` and knows exactly what it is. A user restoring from
  backup sees the same.
- Renames become optional cosmetics: the product can rename the readable part, and if it fails
  or is interrupted, **nothing breaks** — the id still resolves. That removes the whole class
  of half-completed-rename bugs.
- Tolerates the user renaming folders in Explorer, as long as the `(id)` survives; and the
  product can repair a missing one by matching the readable part.

**Cons**
- Mildly ugly. A folder list with a parenthesised number on every entry looks technical.
- Still needs a one-time migration of existing folders and stored bundled paths (same cost as
  A).
- Parsing an id out of a filename is a small piece of string handling that must be exactly
  right, including for account names that themselves end in parentheses.

**Unintended consequences**
- Same id-promotion concern as A: the id becomes externally visible and therefore
  effectively immutable. Slightly softened, because the readable part gives a fallback if ids
  are ever reassigned.
- Encourages the same pattern elsewhere (export filenames, backup naming), which is probably
  fine but should be a deliberate convention rather than a local trick.

### Recommendation

**C — id-stamped readable folder names.** This was close to unanimous, and the panel
considers it the case where the register's framing (name *or* id, with manifest as the third
option) slightly under-serves the obvious fourth answer.

The reasoning that won: the trade-off the register describes — readable vs. collision-free —
is only a real trade-off if the folder name must be *either* a name *or* an id. It doesn't.
Embedding the id in the readable name makes the name authoritative and unique simultaneously,
with no second file to keep in sync and no recovery logic for a manifest that's gone stale.
And it is the only option that makes rename failures harmless, which is worth as much as the
collision fix: today an interrupted rename (P8-ATT-6 / P8-STMT-5) leaves documents stranded,
and that path has no rollback.

The migration is the real work, and it is the same work for A and C: rename existing folders,
rewrite `StatementManager`'s bundled relative paths, and do it idempotently so an interrupted
migration can resume. C at least leaves a half-migrated tree comprehensible.

- **Architect**: C for the reason above — it removes a source of truth rather than adding one.
  Notes that the id-in-the-name convention should be written down as a product-wide rule, since
  the same question recurs for exports and backups.
- **Developer**: parsing is a trailing-`(\d+)` match on the folder name; the fiddly case is an
  account genuinely named `Visa (2)`, which the sanitiser doesn't currently touch. Resolvable
  (match only at the very end, and always write the id) but must be tested.
- **Test Engineer**: C is the only option where the collision scenarios become writable tests
  without a filesystem manifest fixture — create two accounts whose sanitised names collide,
  assert two folders. Also wants a test for the migration's idempotency: run it twice, assert
  no change the second time. Flags that the migration is the highest-risk piece and deserves a
  dry-run mode that logs what it would do.
- **Operations Engineer**: the migration is the whole story. It touches user data outside the
  database, it cannot be wrapped in a transaction, and it runs on first launch of a new
  version. Wants it to (a) log every move, (b) be resumable, (c) never delete, only move, and
  (d) leave a breadcrumb file recording what the old names were. Without (d), a user who has to
  roll back to the previous version finds their documents invisible.
- **UI/UX**: P8-ATT-3's readability property is worth defending and C defends it. Warns against
  A on a specific scenario — the user who has lost their database and is trying to reconstruct
  from documents. That user exists, and numbered folders abandon them completely.
- **Data Engine Expert** (partly in lane): `Account.Id` is an integer surrogate. The panel
  should confirm whether ids are stable across the SQLite↔SQL Server↔XML round trip and across
  `Save As` — **if `Save As` to a different engine renumbers accounts, C and A both break
  silently**, and B would too. Recommends this be verified before committing to any of the
  three, and notes `XmlImporter.remappedIds` proves at least one code path *does* renumber.
  This is the one genuine blocker the panel identified.
- **Adversarial**: "Two accounts named `Visa: Joint` and `Visa / Joint` — has anyone ever had
  those?" Conceded the *specific* example is contrived, but the *duplicate account name* case
  (which the model permits outright, and which `StatementManager` handles worst) is entirely
  ordinary — two people in a household each with a "Checking" account. Also pushed on doing
  nothing: the collision is rare, the migration is risky, and a risky migration for a rare bug
  is a bad trade. The panel's counter: the rename machinery being deleted is worth the
  migration on its own, independent of the collision fix. Partially accepted.

**Genuine disagreement**: the Adversarial Expert's "the migration risk exceeds the bug's cost"
position was not fully answered. Everyone agreed C is the right *destination*; there was
residual doubt about whether the journey is worth taking now rather than at the next
already-planned filesystem change.

**Panel flags:** the Data Engine Expert's question — **are account ids stable across engines,
`Save As` and merge?** — must be answered before implementing. If they are not, this decision
changes entirely and option B (manifest) becomes the only viable one.

### Needs your decision?

**No on direction, yes on timing.** C is the panel's confident recommendation for what the
scheme should be. Whether to run the migration now or defer it to the next occasion that
already touches the attachments tree (D-45's holding area would be one) is a scheduling call
on risk appetite — **your decision**, and the panel would find either answer defensible.

---

## D-47 — Finish per-unit rent entry, or delete it?

**From:** Phase 3 OQ1 · **Cluster:** `rental-property` · *Tracked as [#53](https://github.com/markabrandjord/MyMoney.Net/issues/53)*

### ELI10

The program can track rental properties. You can describe a building, say who owns it, list
its individual flats, and nominate which of your spending categories count as that building's
income, repairs, taxes and so on. Then it shows you, year by year, whether the building made
money.

What it *cannot* do is record what each tenant actually paid. There's no screen for it.

Except — there sort of is. There's a screen file in the program called `RentPayementsView`
(the misspelling is in the original) that looks exactly like what you'd want: a grid of flats
down the side, months across, an amount and a note for each. It's just that nothing in the
program can open it. There's no menu item, no button, nothing.

So the obvious reading is: *someone built this screen and forgot to hook it up.* Plug it in
and you get the missing half of the feature for almost nothing.

**That reading is wrong, and the panel thinks this is the most important correction in this
whole review.**

The screen is a picture frame with no picture behind it. It's laid out to show a flat's name,
a month, an amount and a note — **but the program has no such thing anywhere.** There is no
"rent payment" in its records. Nothing stores one, nothing loads one, nothing saves one. The
screen asks for data from a filing cabinet that was never built. Even if you added a menu item
tomorrow, the grid would come up empty, forever, because the part that's missing isn't the
wiring — it's the entire filing cabinet.

So the real question isn't "finish this screen or delete it". It's: **do you want the program
to track rent payments at all?** Because that's building something new, not finishing
something old.

### What the code actually does (major correction to the register entry)

The register (and #53, and Phase 3 OQ1) describe `RentPayementsView.xaml` as *"a complete,
working surface … never constructed anywhere in the product"*. The panel's re-read found:

- The grid's columns bind to **`Unit.Name`**, **`Unit.Renter`**, **`Amount`**, **`Note`**, and
  the group header binds to **`Items[0].Month`**.
- **No type in the codebase has that shape.** Searches across `MyMoney`, `MyMoney.Business` and
  `MyMoney.Data` for a member of type `RentUnit` named `Unit`, for any `Month` property, and for
  a rent-payment-like class return nothing. `RentUnit` itself has `Name`, `Renter`, `Note`,
  `Building` — it is the *unit*, not a payment against one.
- `RentalBuildingSingleYear` (the type the view's constructor accepts) exposes `Departments`,
  `YearStart`, `YearEnd` — **not** a collection of per-unit monthly payments.
- The constructor stores its argument in a field (`this.yearMonth`) and **never assigns
  `DataContext`**. The `CollectionViewSource` binds to `{Binding}`, i.e. the DataContext that
  is never set. **Even if constructed and shown, the grid renders empty.**
- There is **no persisted rent-payment entity**: no table in `SqliteDatabase`/`SqlDatabase`, no
  `PersistentContainer`, no column mappings.

So this is a **UI shell over a data model that was never built**, not a finished feature
awaiting a menu item. Completing it means: a new persisted entity, tables in every surviving
storage engine, a schema migration, XML/CSV serialisation, change tracking, the view model,
*and* the view — of which the view is the only part that exists, and it is the cheapest part.

One further fact that materially changes the "delete it?" half of the question:

- **Rental support is already an opt-in per-database feature flag.**
  `DatabaseSettings.RentalManagement`, surfaced as a checkbox in `AppSettings` and honoured by
  `MainWindow.UpdateRentalManagement`, removes the entire rental area from navigation when off
  (P2-PREF-5). So rental already costs the non-rental user nothing in the UI.

### Alternatives

#### A. Build rent-payment tracking properly

New `RentPayment` entity (unit, period, amount, date received, note), container, tables across
the surviving engines, migration, serialisation — then the view model and the existing view
rewritten to bind to it.

**Pros**
- Delivers the genuinely missing half of rental tracking. Today a landlord can see what a
  building *earned in aggregate* (derived from categorised transactions) but not *who paid
  what* — which is the question a landlord actually has.
- Enables arrears tracking, which is the real value: "unit 3 is two months behind" is
  information the product cannot express at all today.
- The domain model's patterns (`PersistentObject`/`PersistentContainer`) make adding an entity
  mechanical, if tedious.

**Cons**
- **It is a from-scratch feature**, priced as one: entity + 3–5 storage engines + migration +
  serialisation + change tracking + view model + view. The existing view saves maybe 10% of it,
  and only if its shape survives contact with a real view model.
- Rent payments *also* arrive as bank transactions. The product would now have two
  representations of the same money — a `RentPayment` record and a categorised `Transaction` —
  and reconciling them is a genuinely hard design problem that nobody has started.
- It is the largest net-new build in this entire review, for the product's narrowest feature.

**Unintended consequences**
- **Double-counting is near-certain.** `RentBuildings.AggregateBuildingInformation` derives
  income from categorised transactions. If rent payments are also recorded separately, either
  the aggregation ignores them (and they're decorative) or it includes them (and every rent
  payment counts twice). Resolving this means rent payments must *link to* transactions — which
  turns a simple entity into a relationship with all the transfer-like edge cases
  (`RemoveTransfer`'s asymmetry, split handling) that already make transfers the hardest part
  of the model.
- A rental sub-ledger invites the next requests: leases, deposits, tenant contact details,
  late fees. That is property-management software, and this is a personal-finance app.

#### B. Delete the orphaned view; keep rental tracking as it is

Remove `RentPayementsView.xaml`/`.xaml.cs`, close #53 with the finding that there was never a
data model behind it, and leave rental as the aggregate-reporting feature it actually is.

**Pros**
- Free, and removes a file that has actively misled two rounds of analysis (the catalog, the
  register) into believing a feature was nearly done.
- Rental tracking as it exists is *coherent*: nominate categories, get year-by-year
  profitability. It answers a real question (is this property making money?) completely.
- The feature flag means rental costs non-landlords nothing already.

**Cons**
- Closes the door on per-tenant tracking without deciding whether it's wanted — though the
  door was never as open as it looked.
- A landlord user is left with a genuine gap.

**Unintended consequences**
- Deleting the view also deletes the only written-down record of what the per-unit screen was
  meant to look like. If it's ever built, that design restarts from nothing. (Mitigated by this
  document and by #53's history.)

#### C. Get per-tenant visibility from data that already exists

Don't build a new entity. Instead, let a `RentUnit` be nominated as a **payee or a category**
(or let units map to payees), so that rent received from unit 3 is already identifiable in the
transaction register. Then add a per-unit breakdown to the existing rental summary, derived the
same way the building-level numbers are derived today.

**Pros**
- **No new entity, no schema change, no double-counting** — there is exactly one record of each
  payment, and it is the bank transaction, which is where the money actually is.
- Fits the product's existing grain: `RentBuilding` already works by nominating existing
  categories (`CategoryForIncome` etc.). Extending that pattern to units is consistent rather
  than novel.
- Reconciles automatically, because the rent payment *is* the transaction.
- Arrears become answerable ("no payment from unit 3 this month") without a second ledger.

**Cons**
- Requires the user to categorise or attribute rent income per unit, which is manual discipline.
  A landlord receiving one bank transfer covering two units has to split it.
- A category-per-unit or payee-per-unit scheme pollutes the user's category/payee lists with
  entries that aren't really categories or payees.
- Expected-rent (what the unit *should* pay) still has nowhere to live, so "two months behind"
  needs at least a per-unit expected amount — a small addition to `RentUnit`, not a new entity.

**Unintended consequences**
- Overloading categories or payees for unit identity is the same category of mistake as
  `AccountType.CategoryFund` (D-1) — using an existing concept for something it isn't, which
  the product already has a history of. If done, the linkage should be an explicit field on
  `RentUnit` (`CategoryForRent`, mirroring `RentBuilding`'s existing fields), not a naming
  convention.
- Per-unit derivation runs into the same split-handling subtleties as P1-RENT-5
  (`GetTotalAmountMatchingThisCategoryId`), which already exist and already work — so this is
  reusing tested logic rather than writing new logic, which is a point in its favour.

### Recommendation

**B immediately, C as the feature if rental matters to you, firmly not A.**

The reasoning that won: once the panel established that the view has no data model behind it,
A stopped being "finish the feature" and became "build a feature", and it did not survive being
priced as one. And when priced as a new feature, C is plainly better than A for this product —
because C has one record of each payment (the bank transaction) instead of two, and the
double-counting problem that sinks A doesn't exist in C at all.

Delete the orphaned view now regardless of which way you go on the feature: it is actively
misinformation, it has already cost two analysis passes, and it saves nothing if C is chosen
(C doesn't need that grid shape).

- **Developer**: the correction is decisive. A is measured in weeks across five storage
  engines; the existing `.xaml` contributes a layout, and layouts are the cheap part. C is a
  field on `RentUnit` plus a grouping in the existing summary — days.
- **Architect**: A introduces a second ledger for money that is already in the primary ledger.
  That is the single worst thing you can do to a double-entry-ish model, and the product has
  already been burned by parallel representations (transfers). C keeps one source of truth.
  Unambiguous.
- **Data Engine Expert** (in lane, because A is a schema change): A means a new table in
  SQLite, SQL Server, SQL CE, plus XML and CSV serialisation, plus a migration — and per D-39
  the schema-upgrade path is itself an open question. Adding a table to a product that hasn't
  decided how it upgrades schemas is backwards. C requires one nullable column on `RentUnits`,
  which is the smallest possible migration and still needs D-39 to exist.
- **Test Engineer**: C reuses `GetTotalAmountMatchingThisCategoryId` and `MatchAnyCategories`,
  which already have the split-handling behaviour (P1-RENT-5) and are testable headlessly today.
  A requires round-trip persistence tests across every engine for a brand-new entity, which is
  the most expensive kind of test this codebase has.
- **UI/UX**: notes that the orphaned grid's design is *fine* — flats down, months across is
  the right shape — but that C can present the same shape from derived data, so nothing is
  lost by deleting the file. Also notes the feature flag means rental UI can be as specialised
  as it likes without burdening anyone else.
- **Operations Engineer**: B has no migration. C has a trivial one. A has a real one. Given the
  installed base is essentially one user mid-evaluation, migration risk is low right now —
  which is an argument for doing schema changes *now* if they're going to happen at all. The
  only argument the panel heard in A's favour, and it argues for timing, not for A.
- **Adversarial**: asked the blunt question — *"does the owner of this fork actually own rental
  property?"* If not, this is all theoretical and B is the entire answer. Also challenged C:
  *"a landlord with two units and one bank transfer has to split every deposit by hand — will
  they?"* Fair, and unresolved; C's value depends on the user's willingness to attribute
  income per unit. If they won't, C delivers nothing and B is again the whole answer.

**Genuine disagreement**: none on A. The C-vs-B choice is entirely contingent on facts only
the owner has.

**Panel flags:** would benefit from **actual landlord / property-accounting domain expertise**
to judge whether per-unit tracking without leases, deposits and expected-rent schedules is
useful at all, or whether it's the first 10% of a feature that is only valuable at 80%. None of
the seven roles can answer that.

### Needs your decision?

**No for the deletion — delete the orphaned view, the finding is unambiguous and #53 should be
closed with it.**

**Yes for the feature.** Whether to build per-unit rent tracking at all depends on whether you
own rental property and would use it. If yes, the panel recommends C over A. If no, B is the
complete answer. **Needs your decision.**

---

## D-48 — What is a rental property in the model, and how much of it is editable?

**From:** Phase 4 OQ23 · **Cluster:** `rental-property`

### ELI10

When the program stores a rental property, it keeps: a name, an address, some notes, who owns
it and in what shares, which spending categories belong to it — **and four more things: the
date you bought it, what you paid, how much of that was the land, and what you think it's worth
now.**

Those last four are real. They're in the storage file. They get saved and loaded every time.

But there is **no way to type them in.** The property window doesn't show them. Nothing else
shows them. Nothing reads them either — they don't appear in any report, they don't feed into
your net worth, they don't do anything at all. They're like four labelled boxes in a filing
cabinet that nobody can open and nobody has ever put anything in.

The interesting part is *which* four. "What you paid" and "how much of that was the land" is a
very specific pair. There's only one reason to separate them: **for tax.** When you rent out a
property, you can deduct a bit of the building's value every year as it wears out — but not
the land, because land doesn't wear out. So you need to know the two numbers separately. Those
two fields are, unmistakably, the start of a tax calculation nobody finished.

And "what you think it's worth now" is equally clearly a net-worth number — it's the only way a
program that tracks your total wealth would know your house is part of it.

So somebody designed a rental property to be a thing that *depreciates for tax* and *counts
toward your net worth*. Then they built the window and left all four out.

The decision: are these four fields the ghost of a feature nobody wants, or the outline of two
features that were half-designed and should now either be finished or formally abandoned?

### What the code actually does

- `RentBuilding.PurchasedDate`, `PurchasedPrice`, `LandValue`, `EstimatedValue`
  (`Money.cs:6201–6256`) — all four have `ColumnMapping` attributes, all four round-trip
  through `SqliteDatabase` and `SqlDatabase` (INSERT, UPDATE and SELECT all name them).
- `RentalDialog.xaml` binds **`Name`, `Address`, `Note`, `OwnershipName1/2`,
  `OwnershipPercentage1/2`, the units grid, and the six category pickers**. None of the four.
- A codebase-wide search finds **no reader** outside `Money.cs` and the storage layer. Not in
  `NetWorthReport`, not in `Taxes/`, not in any rental view or report.
- So: **fully persisted, never written by the UI, never read by anything.** The only way a value
  can get in is an import from a file that already has one — i.e. a database written by a
  build that once had a UI for them, or hand-edited data.
- `SqlDatabase`'s `UPDATE RentBuildings` notably **omits `PurchasedDate`** while including the
  other three — so even the storage layer's support is inconsistent. An imported purchase date
  survives the initial insert and is lost on the next update.

That last detail is new and worth recording: the fields are not merely unused, one of them is
actively *dropped* on update.

### Alternatives

#### A. Finish the property record: add the four fields to the editor, and make them mean something

Add purchase date, purchase price, land value and estimated value to `RentalDialog`
(a "Property" tab alongside Owners / Units / Categories). Feed `EstimatedValue` into the
net-worth report as an asset. Fix the `UPDATE` omission. Leave depreciation out.

**Pros**
- Makes four persisted fields honest at low cost — a tab of four inputs and one report line.
- `EstimatedValue` in net worth is genuinely useful and self-contained: for most people a
  property is their largest asset, and a net-worth figure that omits it is badly wrong. This is
  the highest value-per-effort item in the rental cluster.
- Purchase price and date are useful as plain record-keeping even without depreciation — "what
  did I pay for this, and when" is a question people ask.

**Cons**
- `EstimatedValue` is a number nothing updates. A net-worth report anchored on a value the user
  typed in 2019 is confidently wrong, and *more* misleading than omitting it — because it looks
  maintained.
- Land value with no depreciation calculation is a field with no purpose. Including it invites
  "so where's the depreciation?"
- Touches D-21 (does net worth include what you owe / what you own) and should not be decided
  independently of it.

**Unintended consequences**
- Adding a property to net worth makes the net-worth report **partly manual** for the first
  time — every other input is derived from transactions and downloaded prices. That is a
  change in the report's character, and once one manual asset is allowed, "let me add my car"
  follows immediately, which is a whole manual-assets feature.
- An estimated value with no "as of" date is worse than useless for a trend chart: the
  net-worth-over-time graph would apply today's estimate to every historical point, silently
  rewriting the user's past net worth. **`PurchasedDate` exists but `EstimatedValueDate` does
  not** — so doing this correctly needs a fifth field.

#### B. Delete the four fields; a rental property is a name, an address, owners, units and categories

Drop the columns (or leave them dormant and stop pretending), and define the rental feature as
what it demonstrably is: a lens over categorised transactions that answers "did this building
make money this year".

**Pros**
- Honest and coherent. The feature that exists is complete and answers a real question well.
- Removes an implied tax capability the product does not have and, per D-29/D-30, is
  already struggling to keep current for the capabilities it *does* have.
- No new UI, no manual-asset can of worms, no stale-value problem.

**Cons**
- Column removal is a destructive migration for any user who has values in them (via import).
  Probably nobody — but "probably" is doing work.
- Forecloses depreciation and property-in-net-worth without really debating them.

**Unintended consequences**
- Deleting `PurchasedPrice`/`LandValue` deletes the only evidence that depreciation was ever
  contemplated, which — as with D-44's WIA code — means the decision must be recorded or it
  will be rediscovered.
- A rental feature that *cannot* represent the property as an asset is a rental feature that
  can never answer "what's my return on this investment", which is arguably the landlord's
  central question and which the current year-by-year profit view only half answers.

#### C. Model the property as an account

Stop treating a rental property as a special entity with bolted-on value fields, and represent
it as an **asset account** (the model already has `AccountType` values for assets) with the
building as its balance. Purchase price becomes an opening balance; estimated value becomes a
revaluation transaction; the property's cash flows are already ordinary categorised
transactions. `RentBuilding` keeps only what is genuinely property-specific: address, units,
owners, the category nominations.

**Pros**
- Net worth includes the property **for free**, via the mechanism that already exists, with no
  manual-asset special case and no report changes.
- Revaluations get a date automatically, because transactions have dates — solving the stale-
  value and historical-net-worth problems that sink A.
- Purchase price + date land naturally as an opening balance, and a sale becomes a transaction
  rather than a deletion.
- Consistent with how the product already handles loans (P1-LOAN-1: a loan is an account with a
  shrinking balance), so the pattern is established and understood here.

**Cons**
- Largest conceptual change of the three, and it partially dissolves `RentBuilding` into the
  account model — which is a bigger refactor than the rental feature's user base justifies.
- Land value still has nowhere natural to live (it's a *component* of a balance, not a balance).
- An asset account that the user must revalue by hand is still manual; C makes the manual step
  dated and auditable, not absent.

**Unintended consequences**
- Rental properties appearing in the accounts list changes the navigation for every user who
  has one, and interacts with the rental feature flag (a property-as-account would presumably
  remain visible even with rental tracking off, which may be right or may be confusing).
- Depreciation as a scheduled transaction series is then *natural* — which is a feature the
  panel is otherwise recommending against, made easy by the back door.

### Recommendation

**A's net-worth half, B's tax half — and record the split explicitly.**

Concretely: **add purchase date, purchase price and estimated value to the property editor,
add an "estimated as of" date, feed estimated value into net worth, fix the `UPDATE`
omission — and delete `LandValue`**, on the explicit finding that its only purpose is a
depreciation calculation the product does not have, is not going to have, and would be
irresponsible to half-implement given D-29's finding that the tax data is already of uncertain
vintage.

The reasoning that won: the four fields aren't one feature, they're two half-features, and they
deserve opposite answers. Net worth is cheap, valuable and self-contained. Depreciation is a
tax capability requiring a depreciation schedule, a method (MACRS, straight-line), a recovery
period, a basis adjustment and a recapture-on-sale calculation — none of which exist, all of
which must be right, and all of which go stale annually. `LandValue` is the visible tip of that,
and keeping it implies an intent the panel does not think the product should have.

C is architecturally attractive and the panel spent real time on it, but it dissolves a working
feature into a larger refactor for a narrow user base. Recorded as the direction to take *if*
manual assets are ever wanted generally — because at that point the account model is the right
home and the rental case comes along for free.

- **Architect**: prefers C on the merits and would choose it if manual assets were being built
  anyway. Accepts A-minus-LandValue as the proportionate answer for the rental feature alone.
  Insists the `LandValue` deletion be recorded as a deliberate no-to-depreciation.
- **UI/UX**: the "estimated as of" date is not optional. Without it the net-worth trend chart
  silently rewrites history, and a user comparing this year to five years ago gets a number
  that never existed. Would show the estimate with its date inline ("$420,000 as of Mar 2024")
  and grey it when stale.
- **Developer**: a fourth tab on `RentalDialog` is trivial; the report change is one line in
  `NetWorthReport`; the `UPDATE` omission is a one-word fix. Half a day total. Notes
  `RentBuilding.ShallowCopy` (Phase 4 OQ13 — unit edits survive Cancel) is in the same dialog
  and should be fixed in the same pass, since the new fields would inherit the same defect.
- **Data Engine Expert**: the `UPDATE` omission means a `PurchasedDate` imported today is lost
  on the next save — so the field isn't dormant, it's *lossy*. Fix it or drop it; leaving it is
  the worst state. Dropping `LandValue` should be a nullable-column deprecation (stop reading
  and writing it) before a physical drop, so a rollback to a previous version still works —
  which is D-39's problem and another reason D-39 should land first.
- **Test Engineer**: net worth including a manual asset needs a test that pins the "as of" date
  semantics, because that is where the silent-history-rewrite bug would live. Straightforward
  once the semantics are decided.
- **Operations Engineer**: dropping a column is the only migration here and it should be a
  soft deprecation. Everything else is additive.
- **Adversarial**: two challenges. First — *"an estimated value the user types once and never
  updates makes the net-worth report worse, not better. You're adding a number that will be
  wrong within a year."* Partly answered by the "as of" date and the stale indicator; not fully,
  because a stale number still anchors the total. Second — *"why is a rental property the only
  thing in this product you can manually value? What about the house you live in?"* This one
  landed: the recommendation gives rental properties a capability (manual asset valuation)
  that the far more common case — an owner-occupied home — doesn't get. That is an odd product
  shape, and it is the strongest argument for C, which would give both the same mechanism.

**Genuine disagreement**: the Adversarial Expert's second point is not resolved. Giving rental
properties manual valuation while a primary residence has none is genuinely lopsided, and the
panel acknowledges the recommendation is the *proportionate* answer rather than the *coherent*
one. Two members (Architect, Adversarial) would rather see manual assets done properly once, in
the account model, than done narrowly for rental properties.

### Needs your decision?

**Yes.** Two product-taste questions the panel cannot settle for you:

1. **Should net worth include manually-valued assets at all?** If yes, the coherent answer is a
   general manual-asset capability (option C's direction) covering your home as well as any
   rental property — and the rental fields then fall out of that. If no, `EstimatedValue` should
   go the way of `LandValue` and the rental record shrinks to name, address, owners, units and
   categories.
2. **Is depreciation ever in scope?** The panel recommends no, firmly, and recommends deleting
   `LandValue` to say so. If you disagree, say so before it's removed.

The parts that don't need you: fixing the `UPDATE` omission (do it either way), and fixing
`ShallowCopy`'s Cancel behaviour while in that dialog.

---

## Cross-cutting findings

Five corrections to the register surfaced during the re-read, collected here because they
matter beyond their own decision:

1. **D-47 — `RentPayementsView` has no data model behind it.** The register, the catalog
   (Phase 3 OQ1) and issue [#53](https://github.com/markabrandjord/MyMoney.Net/issues/53) all
   describe it as a complete, working surface that was never wired up. It binds to `Unit`,
   `Month`, `Amount`, `Note` — a shape no type in the codebase has — and never sets its own
   `DataContext`. Completing it is a from-scratch feature, not a wiring job. **#53's framing
   should be corrected.**
2. **D-27 — `Transaction` has no currency.** The register suggests per-transaction currency is
   representable today. It is not: currency lives only on `Account`. Option "store it in its own
   currency" is a three-table schema change.
3. **D-27 — OFX's `CURRATE` and `ORIGCURRENCY` are both unread**, and `ORIGCURRENCY` means the
   *opposite* of `CURRENCY`. Any conversion-on-import fix that ignores this corrupts the common
   case (a domestic card used abroad).
4. **D-44 — the WIA COM reference is already gone**; all `WIA.*` usages are inside comments.
   "Revive scanning" is a new native integration, not an uncommenting.
5. **D-48 — `SqlDatabase`'s `UPDATE RentBuildings` omits `PurchasedDate`** while including the
   other three value fields. The field is not dormant, it is lossy on update. This is a small
   untracked defect worth filing regardless of which way D-48 goes.

Two further items worth filing independently of any decision here:

- **D-45** — `DeleteAttachments` is invoked from two separately-subscribed handlers
  (`OnMoneyChanged` and `OnTransactionsChanged`), each carrying the same todo. Whatever is
  decided, these should collapse to one code path.
- **D-46** — the Data Engine Expert's blocking question: **are `Account.Id` values stable
  across engines, `Save As`, and merge?** `XmlImporter.remappedIds` proves at least one path
  renumbers. This must be answered before any id-based document filing scheme is implemented.

## Summary table

| | Decision | Recommended | Needs your decision? |
|---|---|---|---|
| **D-25** | Money File Import scope | Fix honesty + batch behaviour now (B); lineage-id verification next (A); do **not** broaden formats first | **Partly** — is whole-book merge a real workflow for you? |
| **D-26** | QIF import | Scratch-graph import so a failure changes nothing (C); full rewrite (A) only if QIF is genuinely your migration route; **not** deletion | **No** on direction — but confirm what your Quicken can export |
| **D-27** | Imported foreign currency | Fix the guards, check against the account's real currency, handle `ORIGCURRENCY`, **refuse** on genuine mismatch; record per-transaction currency as deferred | **No** |
| **D-44** | Scanning | Delete the dead WIA block now (C); build a watch-folder/inbox (B) if paperwork matters; **not** WIA revival | **No** for the deletion; **mildly** for the inbox |
| **D-45** | Attachments on transaction delete | Move to a `.deleted` holding area with a retention window (B), excluded from the watcher, with a passive status mention | **No** |
| **D-46** | Document folder naming | Id-stamped readable names — `Visa Joint (17)` (C) | **No** on direction; **yes** on timing (migration risk) |
| **D-47** | Per-unit rent entry | Delete the orphaned view now (B); if rental matters, derive per-unit from transactions (C); **not** a new rent-payment ledger (A) | **Yes** — do you own rental property and would you use it? |
| **D-48** | Rental property fields | Add purchase date/price + estimated value with an "as of" date to the editor and to net worth; **delete `LandValue`** (no depreciation); fix the `UPDATE` omission | **Yes** — manual assets in net worth at all? depreciation ever in scope? |

### Expert opt-outs

- **Data Engine Expert** opted out of **D-26** (in-memory transactionality, not storage) and
  **D-44** (attachments are filesystem-side by design), and contributed only an observation on
  **D-45**. In lane and substantive on D-25, D-27, D-46, D-47 and D-48.
- No other expert opted out of any decision.

### "Would benefit from additional expertise" flags

- **D-25** — product-telemetry / user-research: is whole-book merge actually used?
- **D-26** — hands-on Quicken-migration expertise: what can current Quicken Classic actually
  export, and is QIF reachable at all?
- **D-47** — landlord / property-accounting domain expertise: is per-unit rent tracking useful
  without leases, deposits and expected-rent schedules?
