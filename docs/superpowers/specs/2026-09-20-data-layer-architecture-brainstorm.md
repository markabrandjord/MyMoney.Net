# Data-layer architecture brainstorm — the rebuild's first foundation slice

**Status: brainstorm output, not a spec.** This is the design-space exploration that issue #5
was explicitly written as the input to. It is deliberately *not* polished into spec form —
it exists to drive an owner review, after which the agreed path becomes a real design spec and
then an implementation plan, per the brainstorming skill's normal process.

**That owner review is now complete** (revisions 2–11; all eight of §4's items resolved by the
owner), and revision 12 is the final self-review pass. This document is the input to implementation
planning from here. **It is deliberately larger than one plan should be** — see the note under §5's
slice table for the recommended split and the one scheduling gap that split has to resolve.

**Date:** 2026-09-20 · **Branch:** `rebuild/data-layer-foundation` · **Panel:** the usual 7 roles.

**Revision 2 (2026-09-20, same day).** The owner read round 1 and gave follow-up guidance. This
document is *extended*, not replaced — round 1's structure, candidates, trade-offs and open items
all survive except where today's guidance genuinely changes them. What the owner said, and where
each piece landed:

| Owner guidance | Where it is answered |
|---|---|
| "At this point the system is a test system. We can nuke the DB and re-pave it… if all that work is done in sprocs." | New §1.5 (S-0, S-1); adversarial pushback in §6.7 |
| "When someone creates a new DB, the process is more or less the same on SQLite and SQL Server." | New §1.5's six-step process table, engine-by-engine |
| "The create-DB sproc creates the tables (named columns, indexes, foreign keys, etc.)." | §1.5's step table + ledger design |
| "**The sprocs need to support both the normal upgrade process and the nuke-and-pave process.**" | §1.5 S-1 — **one** mechanism, two entry points; this is the revision's central constraint |
| "Sprocs with parameterized input to implement CRUD functions as transactions, because they are safer." | New §1.6 (made explicit, was implicit) |
| "Sprocs to deal with tabular inputs where multiple records need to be input as a transaction." | New §1.6 (TVP/`json_each` batch pattern, carried forward explicitly) |
| "Use as much of the db functionality in SQLite as possible, so the IDatabase layer makes the two engines appear similar." | New §1.7 — a new design principle, worked out concretely, **including where it does not hold** |
| "Test routines for the Business layers may need a mock database for speed… maybe SQLite without a disk file. The database experts can revisit." | §2.4 — sub-decision **T-1 now has a recommendation**, not a deferral |
| "Eventually when we turn on a production db, we cannot just nuke and pave. We have to have a real upgrade process for the db and the app." | New §1.8 — explicitly deferred, with the three places today's design must not foreclose it; new open item #8 |

Round 1's §4 open-items list is re-scored at the end of §4: today's guidance **resolves one**,
**reframes two**, and **adds one**.

**Revision 3 (2026-09-20, same day, separate conversation).** The owner clarified the product's
actual concurrency goal while resolving D-34 in the `panel-review-05-data-engine-security.md`
scenario-capture document: the shipped application must be able to service **a single human user
and potentially an AI agent working at the same time** — genuine simultaneous access, not just
protection against rare accidental overlap. This directly resolves open item #2 from §4 (left
"unchanged" as of revision 2) and reverses the assumption behind it:

| Owner guidance | Where it is answered |
|---|---|
| "If we do [service a human and an AI agent at once] the race condition between the two updaters is possible. So we just have to be able to write transactions that know how to recover if the transactions fail. The retry logic should probably be in the business layer." | §4 item 2 (now resolved, not open); new §1.6a states the data-layer/business-layer split explicitly |
| "Yes, version-checked concurrency, retry in business layer. And the test business layer should have one or more APIs that will have a means to test this functionality." | §2.4 — `FaultInjectingStore` formalized as the test-tier conflict-simulation API; §5 slice 7 updated to build the retry loop and test it |

**What this changes, concretely:** the shipped SQLite path keeps version-checked optimistic
concurrency (`ConcurrencyConflictException` on a stale `RowVersion`) as its permanent concurrency
model — there is no lease to design, build, or fall back to. Nothing in this document's schema,
provisioning, tiering, or `IMoneyStore` design changes shape as a result, because that design
already assumed version-checked writes throughout (§1.6, R-CRUD-4) — a file lease was never
actually built into any candidate here; it was an *open question* (§4 item 2) about whether the
shipped product should additionally serialize access on top of the existing version check. That
question is now closed: no lease, ever, on the shipped path. The changes below thread the "retry
lives in the business layer, and the test tier must be able to trigger a conflict on demand"
architecture through the sections that talk about the conflict contract, T-1, and the slice plan.

**Revision 4 (2026-09-20, same day).** A small, additive revision on two owner points. Both are
narrower than revisions 2 and 3 — neither changes a candidate, a recommendation or the slice
order's shape — but the second one required reconciling the owner's guidance against something
this document already said, rather than simply recording it.

| Owner guidance | Where it is answered |
|---|---|
| "`IMoneyStoreTestControl` is too coarse — it only does whole-database things. I want to clear a table, delete and restore 1 record, clear a table or all tables, etc." | New §2.6 — the granular surface, and the worked-out answer to *"is table-level a decomposition of the whole-database wipe?"* (**no** — they are two different ladders; only the data ladder decomposes) |
| "It will cover views when we get around to using them. SQLite will allow us to query using views, but we cannot update using one. The business layer will have to comprehend that difference." | New §2.7 — how reset interacts with views (§2.7.1), the type-system shape that makes the asymmetry structural rather than remembered (§2.7.2), and the reconciliation with §1.7.1 (§2.7.3) |

**The one thing this revision does not simply accept.** §1.7.1 already lists `INSTEAD OF` triggers
on views as *"adopt selectively — the closest thing SQLite has to a stored procedure for writes"*,
which means SQLite *can* be made to accept a write through a view, and this document had said so
before the owner said it could not. That is a real contradiction, not a wording mismatch, and
§2.7.3 resolves it rather than papering it: **the rule is adopted as an architectural constraint
(views are a read surface; nothing in the business layer ever writes through one), the `INSTEAD OF`
entry in §1.7.1 is demoted from "adopt selectively" to "deferred behind a spike, and never
business-layer-visible", and one narrow question is flagged back to the owner** (§2.7.3, "What the
owner should confirm"). §1.7.1's table and §7's expertise-gap item are edited to match, so the
document does not argue with itself.

**Revision 5 (2026-09-20, same day).** The smallest revision so far, and deliberately so: it
changes **three names and nothing else**. No candidate, recommendation, slice, requirement or
guarantee in this document changes shape.

| Owner guidance | Where it is answered |
|---|---|
| "It makes sense to use a templated method so as to reduce the amount of lines of code we have to maintain." | Confirmed and recorded as settled — the generic write methods on `IMoneyStore` stay generic. §1.6b opens by stating this so a later reader does not mistake a rename for a redesign |
| "But I hope you can find a better name." | New §1.6b — the naming analysis, the recommendation, the alternatives the panel rejected and why, and the rename applied consistently through every section that names these methods |

**The outcome, in one line:** `SaveOne<T>` becomes **`SaveRoot<TRoot>`**, `SaveBatch` becomes
**`SaveRoots`**, and **`SaveTransfer` keeps its name** — because it was the only one of the three
that was already named after *what it saves* rather than *how many*, and it is therefore the
pattern the other two are being brought into line with, not an exception to it. Reasoning in §1.6b.

**Two things this revision deliberately does not touch**, stated here because both look like
omissions otherwise:

- **The SQL stored-procedure layer is not renamed.** `Currencies_SaveBatch`,
  `dbo.CurrencySaveBatchRow`, `Payees_AccessProcs` and the rest keep their names. A T-SQL proc
  cannot be generic across tables with different column shapes, so that layer has to be
  per-entity and already is; the owner has confirmed it reads well as-is. §1.6b records that the
  resulting C#-name/proc-name mismatch (`SaveRoots` calling `Currencies_SaveBatch`) is a
  deliberate seam, so a later slice does not "tidy" it in either direction.
- **Today's merged `IDatabase` is not renamed.** `IDatabase.SaveOne<T>`/`SaveBatch`/
  `SaveTransfer`, `SqliteDatabase.SaveBatch`, `SaveOneCategory` and
  `SqlServerStoredProcDatabase.ExecuteSaveBatchProc` are **working code on `master`**, and every
  mention of them in §0/§0.1 is a *verified fact about the current tree*. Those citations keep the
  old names on purpose — renaming them would make this document's evidence section describe code
  that does not exist. The new names apply to the **rebuilt `IMoneyStore` port**, which is created
  in slice 1 and first implemented in slice 3.

**Revision 6 (2026-09-20, same day).** A follow-up to revision 5 that looks like another naming
question and is not one: there is a real semantic decision under it. The owner objected to calling
`SaveRoot` to perform a *delete*, proposed three fully separate methods, and asked the panel to
judge that against the coordinating session's counter-proposal rather than ratify either.

| Owner guidance | Where it is answered |
|---|---|
| "I don't like calling `SaveRoot`/`SaveOne<T>` to perform a **delete** — 'Save' reads wrong for that operation." | New §1.6c. The objection is **upheld**, and on a stronger ground than taste: a `Save` call that removes a row is invisible at the call site and at code review, and this codebase has a live way for a root to become deleted behind a caller's back |
| "My instinct is three separate methods — `AddRoot`/`UpdateRoot`/`DeleteRoot` — accepting that it gives up some code sharing." | §1.6c, option (b) — **not adopted.** The panel's reason is different from, and stronger than, the code-sharing cost the owner already anticipated |
| *(Coordinating session's counter-proposal)* "Three thin public wrappers that each assert the root is in the claimed state, over one shared internal dispatch." | §1.6c, option (c) — **adopted in mechanism, amended in shape.** The shared internal and the asserting wrapper are kept; one of the three wrappers is rejected on evidence |

**The outcome, in one line:** the single-root write surface becomes **`SaveRoot<TRoot>` plus
`DeleteRoot<TRoot>`** — `Save` never performs a delete again, because it now *refuses* a root
marked deleted — with `SaveRoots` and `SaveTransfer` unchanged in name and shape, and all four
routed through **one** internal insert/update/delete executor so R-CRUD-2's atomicity and
post-commit deferral stay written exactly once. **Add and Update are deliberately not split**,
because the panel found real call sites that genuinely cannot know which of the two they are
performing, while no call site is ever unsure whether it is deleting. Reasoning, and what each of
the three options actually costs, in §1.6c.

**Revision 7 (2026-09-20, same day).** A recording revision, not a design one: the owner answered
§4 item 4, the document's single biggest open architectural fork, and this revision records that
decision in place rather than re-deriving or redesigning around it — §1 was already built, by
deliberate construction, to work either way.

| Owner guidance | Where it is answered |
|---|---|
| "The query-based pattern replaces the whole-graph-in-memory pattern everywhere." | §4 item 4 (now resolved, not open); §4.1 adds the "Net after revision 7" note; the closing summary is updated to match |

**The outcome, in one line:** whole-graph `Load()` (reading the entire dataset into one in-memory
`MyMoney` object graph at startup, which the app then works against directly) **does not survive**.
Every part of the application goes through targeted queries against the data layer — not just
reports, which is what originally motivated `IMoneyQuery` — instead of walking an already-loaded
object tree. This is the larger-rewrite option, chosen deliberately. Because §1's design already
worked either way by construction, no candidate, port shape, or slice changes as a result; the two
places that described `IMoneyQuery` as reports-scoped (§1's introduction of the port, and §8's
summary bullet) are tightened to say so plainly, and item #3's "lag the query surface on SQL
Server" recommendation is flagged as framing that may now be stale — without being resolved here,
per the owner's instruction that this is a recording pass, not a new design pass.

**Revision 8 (2026-09-20, same day).** The owner answered three more §4 items. **Two are pure
recordings** — a scope call and an appetite call, both deferrals, neither of which changes a line
of design. **The third is not**: the owner's answer to the sample-data question is more nuanced
than the yes/no the panel posed, and the nuance creates a genuine architectural tension that this
revision resolves rather than notes.

| Owner guidance | Where it is answered |
|---|---|
| *(item #5, XML as a day-one export format)* "**deferred**" | §4 item 5 (now resolved); §4.1's revision-8 note records what the deferral leaves unserved and which already-designed capability covers it |
| *(item #6, credential storage)* "**later**" | §4 item 6 (now resolved); §4.1's revision-8 note |
| *(item #7, sample data)* "test only. but in this case, they are letting the customer test the capability of the software to see what the UI and the reports look like. I guess in that sense, it could be considered a feature, but I would argue, that it should only be done on a db marked as test." | §4 item 7 (now resolved) **and new §1.9**, which works out the placement, the API shape, and where the `TestDatabase` guard is actually enforced |

**Why item #7 needed design work and #5/#6 did not.** The owner's answer says two things that pull
in opposite directions under this document's own tiering model: sample-data generation is
*customer-reachable* (an evaluating buyer runs it to see what the UI and the reports look like —
that is a shipped feature, not a developer convenience), **and** it must run *only against a
database marked as a test database*. §1's protection mechanisms were built for capabilities that
are one or the other, not both: **assembly-absence** protection (§1 Candidate A — the dangerous
code is not in a release build's bits at all, which is how `IMoneyStoreTestControl` is protected)
would make the feature unreachable by the very customer it exists for, while §1.8's **runtime-flag
refusal** was scoped to *destructive* operations, which sample data is not. §1.9 resolves this:
sample data ships in `MyMoney.Business` and is guarded by **§1.8's existing runtime mechanism, not
a new one** — the same `TestDatabaseGuard.Require` call and the same exception type that slice 5b
already builds for the provisioner — with the guard's stated scope widened from *"destructive
operations"* to *"operations that must only ever touch a test database,"* of which destruction is
now one kind and fabricating indistinguishable-from-real data is the other.

**The outcome, in one line:** `SampleDataGenerator` **stays in `MyMoney.Business` and ships**,
split into a pure, unguarded `SampleDataFactory` (deterministic, no store, no I/O) and a guarded
`SampleDataService` whose *first statement* is `TestDatabaseGuard.Require(store.Identity, …)`; its
write path is the **ordinary** `IMoneyStore.SaveRoots` surface with no privilege of any kind; and
the flag it reads is carried on the open store handle (`StoreIdentity.IsTestDatabase`) rather than
re-fetched from config at the call site, so the guard reads the flag off the same object the writes
go to. Reasoning, the three layers of what a user actually sees, and the honest crack, in §1.9.

**Revision 9 (2026-09-20, same day).** A recording revision, not a design one: the owner resolved
§4 item 8 — *when does the nuke-and-pave phase end, and what is the trigger* — in a way that
dissolves the question as the panel originally framed it, rather than answering it on the panel's
own terms.

| Owner guidance | Where it is answered |
|---|---|
| "If a db is a test db, it is always a test db. If it is not, it never is a test db. You get to set that attribute at create time, and it remains as long as the db remains. When the test routine calls a SP that clears the part of the db that it needs to resume testing, when that SP completes, nuke and pave is over. The[se] routines should be idempotent." | §4 item 8 (now resolved, not open); §1.5 S-0 gets a clarifying cross-reference; new §2.6.8 states and checks the idempotency property; §4.1, §6.7 and the closing summary are updated to match |

**The outcome, in one line:** there is no single project-wide moment to name, because the question
was never really about the *project* — it is about the *database*, and `DatabaseEntry.TestDatabase`
is a permanent attribute fixed at that database's creation, not a phase any database passes through.
A database created as test can always be reset; a database created as production never could be,
from the instant it existed. This is the exact same permanent, per-database flag revision 8's §1.9
already built `StoreIdentity.IsTestDatabase` and `TestDatabaseGuard` around for sample data — this
revision is the recording that the flag's permanence is not just an implementation convenience,
it is the entire answer to "when does nuke-and-pave end." "Nuke and pave is over" describes what
happens every time a test-reset routine (§2.6's `ClearAllData`/`ClearTables`/`ClearTable`/
`ResetSchema`, already designed in revision 4) *finishes running* — a routine, repeated event during
testing, not a one-time milestone — which is exactly why the owner's second sentence, that these
routines must be idempotent, is load-bearing rather than a side remark: idempotency is what makes
"nuke and pave is over" true after *every* call, not just the first. This also closes the panel's
original underlying worry about compatibility properties being hard to retrofit: every database,
test or production, goes through the same versioned `Schema_ApplyTo` machinery from its first
creation (§1.5 S-1), so there is nothing to retrofit when a production database eventually appears
— it was never a test database that "graduated," it is simply a database whose `TestDatabase` flag
was false from the start. §2.6.8 verifies the idempotency claim against the design rather than
assuming it, and tightens one previously-implicit case (`DeleteRow` on an already-absent row).

**Revision 10 (2026-09-20, same day).** A belated recording, not new work: the owner answered §4
item 1 — *rebuild in place or in a parallel `Source/Rebuild/` tree* — early in this document's
review process, before several of the intervening revisions, but the coordinating session at the
time failed to dispatch the recording. This revision closes that gap now; nothing about the
decision itself is new.

| Owner guidance | Where it is answered |
|---|---|
| "In place." | §4 item 1 (now resolved, not open); §4.1 and the closing summary updated to match |

**The outcome, in one line:** the rebuild happens **in place**, in the existing `Source/WPF/` tree
— not in a parallel `Source/Rebuild/` tree. Revision 2's note on this item already stands: nuke-
and-pave removed the strongest argument for a parallel tree (*"the existing database format must
keep working while the new one is built"*), which materially de-risked the in-place choice without
itself deciding it — that de-risking is why the decision was an easy one once made, not a
justification invented after the fact. §7's assumption of *in place, incrementally* — already
stated as an assumption throughout this document — is now the settled answer, not a placeholder.
§5's slice plan needed no edit: every deliverable in the slice table is already named by assembly
(`MyMoney.Data.Sqlite`, `MyMoney.TestKit`, etc.), not by a tree path, so it never hedged on this
question in the first place.

**Revision 11 (2026-09-20, same day).** A recording revision, not a design one: the owner answered
§4 item 3 — *must SQL Server stay at feature parity throughout the rebuild* — the last open item on
the original list, closing it out entirely.

| Owner guidance | Where it is answered |
|---|---|
| *(the general parity question, given earlier in this conversation)* "We can prototype functionality on SQLite, and use that to develop the scrum features. But it would be best to upgrade the SQL Server library in the same feature checkin. If it lags a bit, we can live with that, but we need to keep them as similar as we can, otherwise we cannot test reliably." | §4 item 3 (now resolved, not open) |
| *(asked directly whether that principle extends to the query layer specifically)* "Same principle for the query layer too." | §4 item 3; explicitly closes revision 7's "may now be stale" flag on the "lag the query surface" recommendation |

**The outcome, in one line:** no carve-out for `IMoneyQuery` or any other query-surface capability.
Default practice is to update SQL Server's implementation of a new store capability in the **same
feature check-in** as SQLite's — prototyping on SQLite first when useful is fine, occasional lag is
tolerable but not planned for, and the two engines stay as close together as practical throughout,
because divergence undermines the shared `DatabaseContractTests`-style suite's reliability. This
closes the open-items list: of §4's original seven items plus item #8, **all eight are now resolved
by the owner**. See §4 item 3 for the full reasoning, §4.1 for the closed stale-framing flag, and §8
for the updated closing summary.

**Revision 12 (2026-09-20, same day). Final self-review pass before handoff to implementation
planning.** Not an owner-guidance revision: no owner input, no decision recorded, no design changed.
This was a fresh end-to-end read of the whole document — not a diff of revision 11 — specifically
looking for the drift that eleven incremental passes, several by different dispatched agents each
doing its own occurrence sweep, can accumulate without any single pass noticing. **No placeholders,
no unresolved items and no contradicted decisions were found.** The naming sequence, the concurrency
reversal, the `Load()`/`IMoneyQuery` resolution and the §4 item counts were each spot-checked by
searching for the superseded term rather than by trusting the revision that claimed to have swept
it, and all four came back clean:

| Claim spot-checked | Result |
|---|---|
| Revisions 5 + 6's method rename (`SaveOne`/`SaveBatch` → `SaveRoot`/`DeleteRoot`/`SaveRoots`/`SaveTransfer`) | **Clean.** Every surviving `SaveOne`/`SaveBatch` is either a §0/§0.1 verified-fact citation of code on `master`, a T-SQL proc name §1.6b deliberately keeps, or the naming analysis itself. No snippet, table or prose reference uses an intermediate shape |
| Revision 3's file-lease reversal | **Clean.** Every "lease" occurrence discusses it as the rejected alternative |
| Revision 7's `Load()`/`IMoneyQuery` resolution | **Clean** on the hedging — nothing still describes `IMoneyQuery` as reports-scoped or optional. Two *consequences* of the resolution were found unswept: see items 2 and 8 below |
| §4/§4.1/§8 item numbering and counts | **Clean.** Re-derived by reading: seven original items plus #8, eight resolved, zero open; every "net after revision N" running total is arithmetically right |

**Twelve things were found and fixed inline.** Eight are genuine (a reader would have been misled or
an implementer would have built the wrong thing); four are polish:

1. **§1's assembly list pointed at a `§1.4` that does not exist** (this document has no §1.1–§1.4).
   Repointed to §1's own Recommendation, which is where SQLite provisioning's shipped/in-process
   status is actually decided.
2. **§1 said `IMoneyQuery`'s third implementation is "the test double… over the in-memory graph"** —
   directly contradicting revision 2's T-1 resolution (no `MockStore` is built) and §6.4's argument,
   which was *the* argument that carried T-1. Rewritten to say there is deliberately no third
   implementation.
3. **§1.9.3 and §5's slice 5b put the `TestDatabase` guard at "every `IMoneyStoreTestControl` entry
   point"** — which §2.6.4 explicitly rejects in favour of one check at the guarded factory, *on
   the stated grounds that the twenty-first method is the one that forgets*. Revision 8 introduced
   the contradiction by paraphrasing. Both repointed to §2.6.4's shape.
4. **§5's slice 6 said "all ten Tier-0 tests from §2.5"; §2.5 lists eleven** since revision 8 added
   `TestDatabaseFlag_IsReadOnlyByTheSharedGuard`, which slice 5b delivers. Restated as "the other
   ten of eleven," naming where the eleventh lands.
5. **§2.6.2 and §2.7.1 both credit the snapshot-version tightening to §2.6.6**, which is about
   `TableRef` drift and says nothing about snapshots — the rule is stated only in §2.7.1's own
   table. Both references corrected.
6. **§2.7.2's write-method signatures had drifted from §1.6c's final ones** (`where TRoot :
   IAggregateRoot` versus §1.6c's `where TRoot : PersistentObject, IAggregateRoot`, which is what
   lets the base class read `ChangeType` for revision 6's precondition). Matched to §1.6c, with a
   note that §1.6c is authoritative and that both types live in `MyMoney.Business`, so the
   constraint costs no layering.
7. **§7's items were numbered 1, 2, 4, 3 in source order.** Under normal Markdown rendering an
   ordered list renumbers sequentially, so the `INSTEAD OF`-triggers item would have rendered as
   item **3** — silently breaking all three cross-references to "§7 item 4" (§1.7.1's table and
   §2.7.3's second argument and spike recommendation). Reordered; no text changed.
8. **§3.2's `IDataFormat` signature is written against a whole `MyMoney` object graph that revision
   7 removed.** The placement ruling is unaffected and stands; the parameter type is now flagged as
   unsettled rather than reading as decided. Not re-derived, because item #5 deferred the work and
   nothing is being built against it.
9. *(Polish)* §3.2 still named SQLite backup as close-and-`File.Copy`, which §1.7.1 superseded with
   `VACUUM INTO` in revision 2 — §1.7.1 had noticed and §3.2 was never updated.
10. *(Polish)* §5's "Revised for revision 2" note listed only revision 2's slice edits although
    revisions 4, 6 and 8 each changed a slice too.
11. *(Polish)* "§5's ten-slice table" (the table has twelve rows, numbered to 10); §6.6's heading
    said "Seven assemblies" where §1 says 7–9.
12. *(Polish)* The status header said the owner review was still ahead; it is complete.

**One genuine gap is flagged, not filled** — it is a scheduling question, not a design hole, and
filling it would be new design work this pass has no mandate for: **no slice in §5 implements
`IMoneyQuery`.** Slice 1 introduces it as a type and nothing builds it. That was defensible while
the port was reports-scoped, but revision 7 made it the whole application's read path and revision
11 ruled out a SQL Server carve-out for it, and neither pass revisited §5's table (both were
recording passes, and §4.1 says so in both cases). It now sits in tension with §6.4's *"the contract
suite must grow to cover `IMoneyQuery` in the same slice the port is introduced — not later."*
Slice 3's `LoadAccounts` is the same question in miniature: this document never pins whether simple
reads go through `IMoneyStore` or `IMoneyQuery`. Recorded under §5's table for the implementation
plan to resolve deliberately.

**One scope call is added** (§5, under the slice table): the document is larger than one plan, and
the recommended split is slices 1–7 (the SQLite vertical, one demonstrable end state) and slices
8–10 (SQL Server parity plus the two measurement gates). §3's rulings, §3.3's reporting redesign,
§1.8's upgrade workflow and §1.9's sample-data rewrite are decided but unscheduled and are in
neither plan.

**Two things deliberately left alone**, both because they are already correct as written: §2.7.3's
views-rule confirmation question to the owner (§8 and §4.1 both state that nothing waits on it, and
the panel's recommendation stands either way), and §1.6c's `DeleteRoot`-on-a-never-persisted-root
semantics, which §4.1 deliberately routes to slice 3's contract test as a behaviour to pin rather
than a trade-off to choose. Both are real outstanding questions; neither is a defect in this
document, and the implementation plan inherits both as stated.

---

## 0. What this design is built on top of (verified, not assumed)

Everything below was checked against the working tree and the merged history, not recalled:

| Fact | Where verified |
|---|---|
| `MyMoney.Business` has **zero** WPF-family assembly references, enforced by a live test | `Source/WPF/UnitTests/LayerBoundaryTests.cs` — 3 tests: Business WPF-free, Data WPF-free, and the two are genuinely distinct assemblies |
| `IDatabase` (with `SaveOne<T>`/`SaveTransfer`/`SaveBatch`), `IAggregateRoot` (`long Id`), `DbFlavor` all live in **`MyMoney.Business`**, and `MyMoney.Data` references `MyMoney.Business` — i.e. the port/adapter direction is already correct | `Source/WPF/MyMoney.Business/IDatabase.cs`, `MyMoney.Data.csproj` |
| All five engines (`SqliteDatabase` 2,720 ln, `SqlDatabase`/`SqlServerStoredProcDatabase` 3,991 + 3,075 ln, `SqlCeDatabase`, `XmlStore`, `CsvStore`) are in **one** `MyMoney.Data.dll`, ~12.9k lines | `Source/WPF/MyMoney.Data/` |
| One shared `DatabaseContractTests` suite (~1,600 ln across 4 partial files) runs against Mock, SQLite and SQL Server | `Source/WPF/MyMoney.TestSupport/DatabaseContractTests*.cs` |
| `MyMoney.TestSupport` is **simultaneously** a library (`MockDatabase`, `BasicsFixtureBuilder`, `SqlServerTestDatabase`) and a test project (`Microsoft.NET.Test.Sdk` + `[TestFixture]` classes) | `MyMoney.TestSupport.csproj` |
| No production project references `MyMoney.TestSupport` today — the one-way dependency **already holds, but nothing enforces it** | `ProjectReference` grep across all `.csproj` |
| `UnitTests.csproj` references **`MyMoney.csproj` (the WPF UI project)** | `Source/WPF/UnitTests/UnitTests.csproj:26` |
| SQL Server tiering is real: `DatabaseRole {Admin, User, Test}`, `MyMoneyAdmin`/`MyMoneyUser`/`MyMoneyTest`, `_Test_Reset` procs deployed **only** into a `testDatabase: true` catalog | `DatabaseRegistry.cs`, `SqlServerBootstrapper.CreateCatalog` |
| **Schema creation today is *not* sproc-driven** — `CreateCatalog` calls `adminDatabase.LazyCreateTables()`, which is reflection-to-DDL C# (`GetCreateTableScript`), then replays `Migrations/*.sql` as raw batch scripts | `SqlServerBootstrapper.cs:145`, `SqlDatabase.cs` |
| SQLite already sets `journal_mode=WAL`, `busy_timeout=5000`, `foreign_keys=ON` | `SqliteDatabase.cs:167-185` |

The `LazyCreateTables` row matters a lot: the owner's constraint *"Admin creates the database, then
adds the stored procedures that themselves create tables, indexes, views"* is **not** what the code
does today. It is a genuine change, not a formalization — the formalization part is the
three-login/role model, which does exist.

### 0.1 Additional facts verified for revision 2

Checked the same way — against the working tree, not recalled — because revision 2's whole subject
is schema management and engine parity, and both were only sketched in round 1:

| Fact | Where verified | Why it matters here |
|---|---|---|
| **There is no schema-version ledger of any kind today.** `SqlServerBootstrapper` replays *every* file in `SqlScripts/Migrations/` in filename order on every fresh-catalog creation, and correctness rests entirely on each script's own hand-written guard (`IF COL_LENGTH(...) IS NULL BEGIN ALTER TABLE ... END`). Nothing records what has been applied. SQLite has no migration replay at all. | `SqlServerBootstrapper.cs:170-177`, `SqlScripts/Migrations/2026-09-17-add-version-column.sql` | This is the root cause behind issue #34, not a coincidence: a per-script hand-written guard only covers what its author remembered to guard. Nobody wrote an index guard, so indexes aren't guarded. §1.5 replaces "trust the guard" with "a ledger plus a drift check." |
| SQL Server's batch write is **one** proc call taking a table-valued parameter (`@Rows dbo.CurrencySaveBatchRow READONLY`), with `SET XACT_ABORT ON; BEGIN TRANSACTION;` and the conflict check inside the proc. | `SqlScripts/Access/*_AccessProcs.sql`, `SqlServerStoredProcDatabase.ExecuteSaveBatchProc` | The owner's "sprocs for tabular input, as one transaction" already exists on this engine. It is the shape §1.6 carries forward. |
| SQLite's batch write is a **C# `switch` loop** over roots, issuing per-root `INSERT`/`UPDATE`/`DELETE` statements inside one `BeginTransaction()`, with `PRAGMA defer_foreign_keys = ON`. | `SqliteDatabase.SaveBatch` (`:1009-1110`), `SaveOneCategory` etc. | Same *guarantees* as SQL Server (atomic, parameterized, version-checked), genuinely *different programming model*. This is precisely the asymmetry the owner's "use more SQLite functionality" guidance is aimed at. §1.7. |
| SQLite's post-commit `RowVersion` is **computed in C#** (`c.RowVersion = callerRowVersion + 1`) rather than read back from the engine, even though the `UPDATE` itself does `Version = Version + 1` server-side. | `SqliteDatabase.cs:1165-1181` | A small but real instance of "logic that could live in the engine lives in C# instead." SQLite's `RETURNING` clause would make it authoritative. §1.7. |
| The bundled SQLite engine is **3.53.4** (`sqlite` 3.53.4 via `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5). | `MyMoney.Data.csproj:14`, NuGet cache | Well past every feature §1.7 proposes using: `RETURNING` (3.35), built-in JSON incl. `json_each` (3.38), `STRICT` tables (3.37), generated columns (3.31), `UPSERT` (3.24). None of §1.7 requires a version bump or a different provider. |
| `SqliteDatabase` holds **one long-lived cached connection** (`this.sqliteConnection`), reopened only if closed. | `SqliteDatabase.Connect()` | Directly relevant to T-1: an in-memory SQLite database lives exactly as long as its connection, and this design already keeps one. The lifetime model an in-memory store needs is the one the code already has. |
| `DatabaseEntry.TestDatabase` (that exact casing, deliberately) already exists in the registry config, and already gates whether destructive `Test/*` procs are deployed into a catalog. | `DatabaseRegistry.cs:59-62`, `SqlServerBootstrapper.cs:185-194` | The "mark test databases in config so production ones are distinguishable" half of the deferred production-upgrade workflow **already exists**. §1.8 builds on it rather than inventing it. |

---

## 1. Candidate approaches for the data layer

All three share a premise the panel treats as settled, because D-35's capability floor already
decided it: **`IDatabase` gets split.** A single interface whose members throw on most
implementations is a union type wearing a contract's clothes. Everything below splits it three
ways along the *privilege* axis the owner named:

```
IMoneyStore            — everyday read/write. No DDL, no destructive bulk operations.
IMoneyStoreProvisioner — create catalog/file, deploy + run schema, upgrade, backup, delete.
IMoneyStoreTestControl — put the store into a known state, below the object graph and below
                         the version check: reset the schema, clear all tables or some tables,
                         delete/capture/restore an individual row, snapshot/restore. Test tier
                         only. **Expanded in revision 4 — see §2.6 for the actual surface.**
```

Plus a fourth, non-store contract for what D-35 called *formats*:

```
IDataFormat            — round-trip the whole model to/from a stream. XML export, CSV export.
                         Lives in the business layer, NOT in a data-layer engine assembly.
```

*(Revision 8: this **placement** is settled and unchanged, but its **timing** is now decided —
§4 item 5's answer is "deferred," so neither `IDataFormat` nor an XML implementation is in the
first slices. Nothing in §5's slice order referenced them, so nothing there changes.)*

The three candidates differ in **how that tiering is enforced in .NET**, which is the only
question with real trade-offs.

---

### Candidate A — Tier-per-assembly (**recommended**)

Each tier of each engine is its own DLL. Privilege separation is expressed as *"the code isn't
in the deployed bits"*, which is exactly the property SQL Server already gives via
`_Test_Reset` procs only being deployed into a test catalog (P9-GUARD-2).

```
MyMoney.Domain                      entities, IAggregateRoot, ConcurrencyConflictException
MyMoney.Data.Contracts              IMoneyStore, IMoneyStoreProvisioner, IMoneyQuery, DTOs
                                    (could equally stay in MyMoney.Business — see note below)

MyMoney.Data.Sqlite                 IMoneyStore + IMoneyQuery            [always shipped]
MyMoney.Data.Sqlite.Provisioning    IMoneyStoreProvisioner               [shipped; see the
                                                                         Recommendation below]
MyMoney.Data.SqlServer              IMoneyStore + IMoneyQuery            [DEBUG only]
MyMoney.Data.SqlServer.Provisioning IMoneyStoreProvisioner + bootstrapper [DEBUG only]

MyMoney.TestKit.Contracts           IMoneyStoreTestControl, test-only DTOs   [never shipped]
MyMoney.Data.Sqlite.TestTier        IMoneyStoreTestControl impl              [never shipped]
MyMoney.Data.SqlServer.TestTier     IMoneyStoreTestControl impl              [never shipped]
```

**How the SQLite facade gets its teeth.** The destructive capability isn't hidden behind an
`internal` modifier or a runtime flag — the SQL that can empty the database *does not exist in
any assembly a release build contains*. `MyMoney.Data.Sqlite.dll` contains no `DELETE FROM`
without a `WHERE Id=`, no `DROP TABLE`, no `VACUUM INTO` over-the-top-of-an-existing-file. And
critically: **`IMoneyStoreTestControl` itself lives in `MyMoney.TestKit.Contracts`, which no
production assembly references** — so production code cannot even *name the type* it would need
to implement or call. A copy-pasted implementation wouldn't compile.

That is the honest equivalence claim, and §6 states precisely where it falls short of real
SQL Server role separation.

**How the SQL Server tiering maps.** One-to-one, and the .NET assembly boundary lines up with
the login boundary rather than duplicating it:

| .NET | SQL Server |
|---|---|
| `MyMoney.Data.SqlServer` (`IMoneyStore`) | connects as `MyMoneyUser` — `EXECUTE` on `Access/*` procs only, zero table grants |
| `MyMoney.Data.SqlServer.Provisioning` | connects as `MyMoneyAdmin` (`dbcreator`) — calls `master.dbo.MyMoney_CreateCatalog`, deploys `Schema/*` + `Access/*` procs, then **executes** `Schema_Apply` |
| `MyMoney.Data.SqlServer.TestTier` | connects as `MyMoneyTest` — `EXECUTE` on `*_Test_Reset` only, and those procs are only ever deployed into a `testDatabase: true` catalog |
| `sa` | nowhere in .NET as a stored credential — prompted for, used once by the bootstrap entry point, never persisted |

**Schema-creation-through-sprocs (the owner's constraint), concretely.** Replace today's
reflection-to-DDL `LazyCreateTables()` with a versioned proc family:

1. `sa` (prompted, once per server) runs `MyMoney_BootstrapServer.sql` → creates the three
   logins, grants `MyMoneyAdmin` `dbcreator`, deploys `master.dbo.MyMoney_CreateCatalog`.
2. `MyMoneyAdmin` calls `MyMoney_CreateCatalog` → empty catalog exists, `MyMoneyAdmin` is its
   owner.
3. `MyMoneyAdmin` deploys `SqlScripts/Schema/*.sql` — a set of procs (`dbo.Schema_ApplyTo(@TargetVersion)`,
   `dbo.Schema_CurrentVersion()`) that contain the `CREATE TABLE`/`CREATE INDEX`/`CREATE VIEW`
   DDL, each step idempotent and guarded.
4. `MyMoneyAdmin` **executes** `dbo.Schema_ApplyTo(@TargetVersion = N)`. The DDL runs inside the
   database, from a versioned, auditable object — not from string-building C#.
5. `MyMoneyAdmin` deploys `Access/*` procs and `GRANT EXECUTE ... TO MyMoneyUser`.
6. If and only if `testDatabase: true`: deploy `Test/*` procs, `GRANT EXECUTE ... TO MyMoneyTest`.

Upgrade becomes: deploy the newer `Schema_*` proc, call `Schema_ApplyTo(N+1)`. The existing
ad-hoc `Migrations/*.sql`-replayed-by-hand convention folds into step 3/4.

**The SQLite analogue of step 3/4.** SQLite has no server-side procedures, so the equivalent
"the DDL lives in a versioned artifact, not in C# string-building" is: the provisioning assembly
carries the DDL as **embedded `.sql` resources**, applied by a migration runner keyed on a
version ledger. Same versioning semantics, same auditability, same idempotency, and the
same "this code is in the provisioning assembly, not the store assembly" separation. The
reflection-to-DDL path (`GetCreateTableScript`) is deleted on both engines — it's the reason the
two engines' schemas have quietly drifted before.

> **Revision 2 supersedes the four paragraphs above in detail, not in direction.** The owner's
> follow-up added a constraint round 1 did not have — *the same proc family must serve both the
> nuke-and-pave process and the eventual real upgrade process* — and asked for the SQLite side to
> be worked out concretely rather than asserted symmetric. **§1.5 is the authoritative schema-
> management design**; read it in place of the sketch above. The one substantive correction it
> makes: round 1 said SQLite keys its runner on `PRAGMA user_version`. §1.5 replaces that with a
> real `__SchemaHistory` table on both engines, for reasons given there — `user_version` is a
> single integer with no room for a per-step record, and per-step records are exactly what issue
> #34 needs.

**What happens to `IDatabase`'s three write methods and `IAggregateRoot`:** they move onto
`IMoneyStore` **unchanged in behavior, renamed in revision 5 and split from three methods into four
in revision 6** — `SaveOne<T>` → `SaveRoot<TRoot>` **+ `DeleteRoot<TRoot>`**, `SaveBatch` →
`SaveRoots`, `SaveTransfer` unchanged (§1.6b, §1.6c). They are the one part of the current data
layer with a proven, three-engine, ~60-test shared contract behind them; nothing here justifies
reopening their *semantics*, and neither revision does — revision 5 changes two names, revision 6
changes which name a caller types to reach the already-existing delete branch, and neither changes
a guarantee. Two changes around them:

- **`Save(MyMoney)` does not exist on `IMoneyStore`.** R1 said to retire it; it is still on
  `IDatabase` today. The rebuild is the clean moment.
- **`IAggregateRoot.Id` stays `long`.** Non-negotiable, already load-bearing.

**New: `IMoneyQuery`, the read-side port issue #5's Reports finding requires.** *(Revision 7: the
owner has since decided `Load()` does not survive — see §4 item 4 — which makes `IMoneyQuery` the
universal read-access pattern for the whole application, not a reports-specific addition. Reports
remains the finding that first surfaced the need; it is no longer the boundary of its scope.)* Not
`IQueryable`
— the SQL Server tier is stored-proc-only, so an expression-tree provider is literally
impossible there, and an `IQueryable` that silently materializes everything and filters in
memory is worse than no abstraction at all. Instead, a closed, typed request:

```csharp
public sealed record AggregateQuery(
    IReadOnlyList<long> AccountIds,        // empty = all
    DateRange Range,
    TransactionStatusFilter Status,        // reconciled / cleared / uncleared / any
    IReadOnlyList<long> CategoryIds,
    GroupBy Primary,                       // Account | Month | Year | Payee | Category | None
    GroupBy Secondary);

IReadOnlyList<AggregateRow> IMoneyQuery.Aggregate(AggregateQuery q);
IReadOnlyList<TransactionRow> IMoneyQuery.List(ListQuery q);
```

SQLite implements it as parameterized SQL; SQL Server as a `_Query` proc family taking a TVP of
filter values (consistent with the TVP pattern Phase 2c already established). There is deliberately
**no third, hand-written implementation** — business-layer tests run against the real SQLite one
over an in-memory database (§2.4's T-1 resolution; `MockStore` is not built). The shared contract
suite grows to cover it — see §6.4 for why that's not optional.

**Pros**
- Enforcement is structural and *provable by inspecting the output directory*, which is the
  only form of "defense in depth" that survives a bad merge.
- Directly satisfies D-37 (SQL Server physically absent from a release) and D-35 (one engine per
  install) with no `#if` in sight — packaging, not preprocessor.
- The release-packaging boundary test ("no `MyMoney.Data.SqlServer*` in a Release publish output")
  is trivial to write and unambiguous when it fails.
- Each engine's store assembly stays small enough to read.

**Cons**
- 7–9 data-layer assemblies where there is 1 today. Real friction: more `.csproj` files, more
  solution noise, slower cold build.
- Store and provisioner for one engine share connection/mapping/type-conversion code, so either a
  shared `MyMoney.Data.Sqlite.Common` assembly appears (more assemblies) or `InternalsVisibleTo`
  appears (a crack — see §6.2).
- Not the shape most .NET developers reach for first; needs the reasoning written down or it gets
  "simplified" back in six months.

---

### Candidate B — One assembly per engine, tiering by gated handle types

Two engine assemblies (`MyMoney.Data.Sqlite`, `MyMoney.Data.SqlServer`), each containing all
three tiers' code, with the tiers expressed as distinct .NET types you can only *obtain* through
a gated factory:

```csharp
public static class SqliteStore
{
    public static IMoneyStore OpenUser(string path);
    public static IMoneyStoreProvisioner OpenProvisioner(string path, ProvisioningGrant grant);
    internal static IMoneyStoreTestControl OpenTest(string path, TestGrant grant);
}
// TestGrant's only public constructor lives in MyMoney.TestKit; SqliteStore has
// [InternalsVisibleTo("MyMoney.Data.Sqlite.TestTier")].
```

**Pros**
- Two assemblies instead of seven; no code duplication, no shared-common-assembly problem.
- Compile-time safety is still real: you cannot construct a `TestGrant` from production code.
- Substantially cheaper to build, and much easier to hold in your head.

**Cons**
- **The destructive code ships.** `MyMoney.Data.Sqlite.dll` in a release build contains the
  method that empties the database; only the capability *token* is withheld. Reflection reaches
  it in four lines. That is materially weaker than SQL Server's "the proc was never installed",
  and calling the two equivalent would be the comforting fiction the Adversarial Expert warned
  about.
- `InternalsVisibleTo` is load-bearing rather than incidental, which makes §6.2's failure mode
  structural rather than accidental.
- D-37's *"the released assembly set should not contain the SQL Server engine at all"* is still
  satisfiable (the whole SqlServer assembly is excluded), but the SQLite test-tier half of the
  same principle is not.

---

### Candidate C — Admin and Test tiers as out-of-process tools

Only `IMoneyStore` is a library the app links. Provisioning and test control are a separate
executable (`MyMoney.DataAdmin.exe`) the installer, the developer, and the test harness invoke.

**Pros**
- Strongest separation available without OS-level ACLs: different process, different credentials,
  different lifetime. It is the closest .NET analogue to what SQL Server actually does.
- Makes provisioning a real operations artifact — scriptable, CI-runnable, loggable — which the
  Operations Engineer wants regardless of which candidate wins.
- Turns "who is allowed to reshape the books" into an answerable question at the process level.

**Cons**
- **Kills the sub-minute TDD loop** if tests provision per test. A fresh SQLite file per test is
  currently sub-millisecond; a process launch is 50–200 ms, × several hundred tests = minutes,
  not seconds. That directly contradicts issue #5's stated cadence requirement.
- More to build before the first vertical slice exists, which contradicts the owner's own
  "data-layer DLLs + test subsystem first" sequencing.
- Process-boundary error reporting (exit codes, stderr parsing) is worse than exceptions.

---

### Recommendation: **A, borrowing one thing from C**

**Take Candidate A.** Tier-per-assembly, because the only enforcement mechanism that survives a
bad merge under deadline pressure is *"the code is not in the build output"*, and because it is
the only candidate where the SQLite facade's guarantee and the SQL Server role model's guarantee
are the same *kind* of guarantee rather than a strong one and a polite one.

**Borrow from C for SQL Server provisioning only.** The `sa`-credentialed server bootstrap is
genuinely a one-time operator action, not something app code should ever perform in-process. Make
it a thin console entry point over `MyMoney.Data.SqlServer.Provisioning` (`dotnet run --project
... bootstrap --server Redmond`). SQLite provisioning stays in-process — creating a file is not a
privileged act in any meaningful sense, and per-test provisioning speed is load-bearing.

**On `MyMoney.Data.Contracts` vs. leaving the ports in `MyMoney.Business`:** the Developer's
position, which the panel accepts, is *don't create the assembly yet*. The port interfaces can
stay in `MyMoney.Business` exactly where `IDatabase` is today — the dependency direction is
already right, and a separate contracts assembly buys nothing until a non-business consumer
appears. **`MyMoney.TestKit.Contracts` is different and must be separate from day one**, because
its entire purpose is to be absent from the production dependency graph.

**Mitigating A's real cost (the shared-code problem):** do *not* create
`MyMoney.Data.Sqlite.Common`, and do *not* use `InternalsVisibleTo`. Instead, accept a small
amount of duplication between store and provisioner (they need different things: the store needs
row mapping, the provisioner needs DDL and version bookkeeping — the genuine overlap is
connection-string construction and open/close, which is ~40 lines). If that assessment turns out
to be wrong once real code exists, revisit — but "we need `InternalsVisibleTo`" is the signal that
the tier split was drawn in the wrong place, not that the rule is too strict.

---

## 1.5 Schema management — one mechanism, two entry points (Data Engine Expert leading)

This section is revision 2's centre of gravity. The owner's two messages contain a constraint that
looks like two requirements and is actually one:

> *"We can 'nuke' the DB, and then re-pave it with new tables, indexes and views if all that work
> is done in sprocs."*
> *"**The sprocs need to support both the normal upgrade process and the 'nuke and pave' process.**"*

Read together, that rules out the obvious implementation — a fast `CreateEverything` proc for
fresh databases plus a separate `Upgrade` path bolted on later. Two code paths means the upgrade
path is the one that's never exercised, and the first time it runs for real is the first time it
runs on data that matters. The design below has **one** mechanism.

### S-0. The phase declaration, written down so it can expire

**Right now, every database this project touches is a test database.** Nuke-and-pave is the
accepted model for this phase: to change the schema, drop everything and re-apply from zero. No
data preservation is promised, because there is no data worth preserving. `DatabaseEntry.TestDatabase`
already exists in the registry config and already means exactly this.

This is an explicit, time-boxed decision, not a permanent property, and §1.8 states what has to
become true before it expires. The panel accepts it — it is the correct trade for this phase, and
pretending otherwise would buy migration ceremony with no data behind it. §6.7 names the one risk
it does create.

**Revision 9 sharpens this rather than changing it (§4 item 8).** "Time-boxed" is true of *this
project's current databases*, which all happen to be test databases today, but it is not a clock
running against the project as a whole. The real boundary is per-database and permanent:
`DatabaseEntry.TestDatabase` is set once, at that database's creation, and never changes for as
long as the database exists — the same flag revision 8's §1.9 reads as `StoreIdentity.IsTestDatabase`
to guard sample data. A database created as test can always be nuked and paved; a database created
as production never could be, from the moment it was created. There is no future instant at which
*today's* test databases stop being nukeable — they don't graduate; a later, separately-created
production database simply never has the capability reachable at all. See §4 item 8 for the full
reasoning and §2.6.8 for the idempotency property this resolution depends on.

### S-1. The unified mechanism

There is exactly one thing that changes a schema: **an ordered list of versioned, individually-
recorded steps, applied by a single executor.** Everything else is an entry point into it.

```
Schema_CurrentVersion()              -> int     highest fully-applied step, 0 on an empty database
Schema_ApplyTo(@TargetVersion)                  apply every unapplied step from current+1..target
Schema_Verify()                      -> rows    introspected actual schema vs. expected; drift report
Schema_DropAll()                                [test tier only] drop every object this family owns
```

| Operation | Is | Not |
|---|---|---|
| **Create a new database** | `Schema_ApplyTo(N)` against an empty catalog — current version is 0, so it applies steps 1..N | a separate `CreateEverything` proc |
| **Nuke and pave** | `Schema_DropAll()` then `Schema_ApplyTo(N)` — which is then *literally indistinguishable* from creating a new database | a separate reset path |
| **Upgrade (deferred, §1.8)** | `Schema_ApplyTo(N)` against a database at version M — applies steps M+1..N | a separate upgrade path |

The three differ only in what `Schema_CurrentVersion()` returns when they start. That is the whole
trick, and it is what makes the owner's constraint satisfiable rather than merely stated: **the
upgrade path is exercised on every single fresh-database creation**, because creating a fresh
database *is* an upgrade, from version 0.

### S-2. The ledger — a table, not `PRAGMA user_version`

Round 1 proposed `PRAGMA user_version` for SQLite. Revision 2 rejects that and uses the same
ledger table on both engines:

```sql
CREATE TABLE __SchemaHistory (
    Version      INTEGER NOT NULL PRIMARY KEY,   -- step number
    StepName     TEXT    NOT NULL,               -- '0007_add_transactions_payee_index'
    ChecksumHash TEXT    NOT NULL,               -- hash of the step's SQL text as applied
    AppliedUtc   TEXT    NOT NULL,
    AppliedBy    TEXT    NOT NULL                -- login/user that ran it
);
```

`PRAGMA user_version` is a single 32-bit integer in the file header. It can say *"this database is
at version 7."* It cannot say *which* steps produced that, when, by whom, or whether the step-7 SQL
in this build is the same step-7 SQL that actually ran. Every one of those is needed by the
deferred production-upgrade workflow (§1.8), and `ChecksumHash` in particular is the only cheap
defence against the failure mode where someone edits an already-applied step's SQL and every
database that already ran it silently disagrees with every database that hasn't. A table costs one
`CREATE TABLE` and removes an entire class of "which build produced this schema?" questions.

`__SchemaHistory` itself is step 0, created by the executor before the ledger exists to record it —
the one genuine bootstrap exception, and it is the same exception on both engines.

### S-3. Steps are forward-only, additive, and own their own idempotency

A step is a numbered unit containing DDL. Rules:

1. **A step is never edited once it has been applied anywhere.** A mistake in step 7 is fixed by
   step 8. `ChecksumHash` enforces this by detection: the executor refuses to proceed if an
   already-recorded step's checksum no longer matches. During the nuke-and-pave phase this rule is
   *cheap to follow and also cheap to break* — nothing stops you editing step 7 and re-paving — so
   the checksum check must be **on from day one**, not added when production appears. Turning it on
   later means turning it on against a corpus of steps nobody was disciplined about.
2. **Every step is idempotent in its own right** (`IF NOT EXISTS`-guarded), *and* the ledger means
   it is never asked to be. Belt and braces, deliberately: the ledger is the mechanism, the guard is
   the safety net for the case where a step half-applied and the transaction didn't cover it (SQL
   Server DDL is transactional; SQLite DDL is transactional; both are fine here — but a step that
   spans a proc redeploy plus DDL is where this bites).
3. **Each step runs inside a transaction, and the ledger row is written in the same transaction.**
   A step either applied and is recorded, or did neither. This is not negotiable and is the reason
   `Schema_ApplyTo` loops step-by-step rather than wrapping the whole range in one transaction —
   a failure at step 9 of 12 must leave a database at version 8, not at "version 0 but actually
   partly at 9."
4. **Everything the schema owns is a step**: tables, *named columns*, indexes, foreign keys, views,
   check constraints, table types (TVPs), and the `Access/*` CRUD procs themselves. The owner listed
   "tables (named columns, indexes, foreign keys, etc.)" — the "etc." is doing real work there and
   §1.6 is where the proc half lands.

### S-4. How this kills issue #34 — and the honest caveat

**[Issue #34](https://github.com/markabrandjord/MyMoney.Net/issues/34)**: `CreateOrUpdateTable`
only runs `GetCreateIndexScripts(mapping)` inside the *"table doesn't exist yet"* branch, so an
index added to an already-existing table's mapping is silently never created — on either engine.
New tables get their indexes, new columns get handled explicitly, new indexes on existing tables
fall in a hole with no error and no warning.

The new mechanism removes the hole **structurally**, because the concept that caused it no longer
exists. There is no "does this table already exist?" branch anywhere. Adding an index is a step:

```sql
-- 0012_add_transactions_payee_date_index.sql
CREATE INDEX IF NOT EXISTS IX_Transactions_PayeeId_Date ON Transactions (PayeeId, [Date]);
```

- A **fresh** database is at version 0, applies steps 1..12, and gets the index because step 12 ran.
- An **existing** database at version 11 applies step 12, and gets the index because step 12 ran.

Same step, same executor, same reason. There is no branch that can skip one case and not the other,
because there is no branch.

**The honest caveat, stated plainly: this converts a silent-wrong-behavior bug into a
discipline requirement.** The mechanism guarantees *"every written step runs everywhere."* It does
not guarantee *"someone wrote the step."* If a developer adds an index to a `[TableMapping]`
attribute (or whatever replaces it) and forgets to write the corresponding step, the index is still
missing — just for a different reason. Today's bug is "the code can't do it"; the new failure mode
is "nobody asked for it." That is strictly better, but it is not nothing, and it is exactly the
place the Adversarial Expert would expect a design document to overclaim.

Three things close that gap, and they are **part of this design, not follow-ups**:

1. **`Schema_Verify()` — a drift check, not a hope.** Introspect the live schema (`sqlite_master`
   + `PRAGMA index_list`/`table_info`/`foreign_key_list` on SQLite; `sys.tables`/`sys.indexes`/
   `sys.foreign_keys`/`INFORMATION_SCHEMA` on SQL Server) and compare against the expected object
   set for the current version. Report every difference. This is the routine issue #34's own
   "Suggested fix shape" section was reaching for, generalized from indexes to everything, and
   moved from "run it during upgrade" to "run it as an assertion."
2. **The schema-equality contract test — the one that would actually have caught #34.** In the
   Tier-2 store-contract suite, per engine:

   > Build database **A** fresh at version N. Build database **B** fresh at version N−1, then
   > `Schema_ApplyTo(N)`. **Introspect both and assert the schemas are identical** — every table,
   > column, type, nullability, index (including its column list and uniqueness), foreign key,
   > and view.

   Today's code fails this test the moment an index is added to an existing table, which is
   precisely issue #34 reproduced as a red test. It runs on every build from slice 2 onward,
   which — per S-1 — is what keeps the never-yet-needed upgrade path honest during a phase where
   nobody upgrades anything.
3. **Deleting the reflection-to-DDL path entirely.** `GetCreateTableScript` /
   `GetCreateIndexScripts` / `CreateOrUpdateTable` / `LazyCreateTables` go away on both engines.
   The bug cannot recur in code that no longer exists, and keeping both mechanisms alive "for
   now" is how the two engines' schemas drifted in the first place.

### S-5. "More or less the same process" on both engines — concretely

The owner asked that creating a database look the same on both engines. It can, at the level of
*process*; it cannot at the level of *mechanism*, and §1.7 is honest about why. Here is the
process, step by step, with what each engine actually does:

| # | Step (identical on both) | SQL Server | SQLite |
|---|---|---|---|
| 1 | **Create the empty database** | `MyMoneyAdmin` calls `master.dbo.MyMoney_CreateCatalog` (already exists) | Create the file; `Connect()` applies `foreign_keys=ON`, `journal_mode=WAL`, `busy_timeout` (already exists) |
| 2 | **Install the schema-management logic into it** | Deploy `SqlScripts/Schema/*.sql` → real procs `dbo.Schema_ApplyTo`, `Schema_CurrentVersion`, `Schema_Verify` | Load the embedded `Schema/*.sql` step resources + the executor from `MyMoney.Data.Sqlite.Provisioning`. **See §1.7 for what "sproc-equivalent" honestly means here** |
| 3 | **Run it to build the schema** | `EXEC dbo.Schema_ApplyTo @TargetVersion = N` | `provisioner.ApplyTo(N)` — same step list, same ledger, same per-step transaction, same order |
| 4 | **Install the CRUD access layer** | Deploy `Access/*.sql` procs; `GRANT EXECUTE ... TO MyMoneyUser` | Steps 1–3 already created the views/triggers §1.7 describes; there are no grants to make (§1.7's honest limit) |
| 5 | **Install the test-tier capability, iff `TestDatabase: true`** | Deploy `Test/*.sql`; `GRANT EXECUTE ... TO MyMoneyTest` | `MyMoney.Data.Sqlite.TestTier` is present in the build or it is not (§1/§6.1) |
| 6 | **Verify** | `EXEC dbo.Schema_Verify` — must report no drift | `provisioner.Verify()` — same comparison, engine-native introspection |

Steps 1, 3, 5 and 6 are genuinely the same operation. Steps 2 and 4 are where the engines diverge,
and the divergence is real: on SQL Server the schema logic is *installed into the database and runs
there*; on SQLite it is *carried by the provisioning assembly and runs in-process against the file*.
The version ledger, the step list, the step SQL (modulo dialect), the ordering, the transaction
boundaries, the verify comparison and the entry-point API are identical. **What is not identical is
where the executor's control flow physically lives, and no amount of design makes that identical,
because SQLite has no server.** Saying otherwise would be exactly the "comforting fiction" §6.1
already warns about for the privilege model.

---

## 1.6 CRUD and batch procs — parameterized and transactional, stated explicitly

The owner asked for parameterized, transactional CRUD sprocs "because they are safer," and for
tabular-input sprocs that write multiple records as one transaction. Both already exist in the
Phase 2 persistence-concurrency work. Round 1 carried them forward *implicitly* ("the three write
methods move onto `IMoneyStore` unchanged"). Revision 2 states them as requirements, because an
implicit carry-forward is a thing a later slice can quietly drop. *(Revision 5 renames two of the
three — `SaveRoot`/`SaveRoots`/`SaveTransfer`, per §1.6b. Revision 6 splits the delete branch out
of `SaveRoot` into a named `DeleteRoot<TRoot>`, making it four methods, per §1.6c. Neither touches
any requirement below: every one of R-CRUD-1..5 applies to all four methods, because all four run
through the same internal executor.)*

**R-CRUD-1 — every store write is parameterized.** No SQL built by string concatenation with a
value in it, on either engine. This is already true of every write path today. *(It is not yet true
of two schema-introspection reads: `SqliteDatabase.cs:218` and `:230` interpolate a table name into
`SELECT ... FROM sqlite_master WHERE tbl_name='" + name + "'`. Those are internal, non-user-supplied
identifiers, so it is not a live injection vector — but both live in code §1.5 deletes, and the
replacement introspection in `Schema_Verify` must not reintroduce the pattern.)*

**R-CRUD-2 — every store write is atomic at the call boundary.** `SaveRoot`, `DeleteRoot`,
`SaveTransfer` and `SaveRoots` each either apply completely or not at all, including their
in-memory side effects.
SQL Server achieves this inside the proc (`SET XACT_ABORT ON; BEGIN TRANSACTION;`); SQLite achieves
it with `BeginTransaction()` plus the `postCommitActions` deferral that keeps `RowVersion`/
`OnUpdated()` from being applied to the object graph until the commit actually succeeded. That
deferral is a genuinely hard-won behavior — a stale `RowVersion` claiming a commit the rollback
undid is a silent-corruption bug — and it is carried forward verbatim.

**R-CRUD-3 — tabular input is one proc call, one transaction.** The established pattern is a
table-valued parameter: `dbo.Currencies_SaveBatch @Rows dbo.CurrencySaveBatchRow READONLY`, one
call per root type, set-based inside the proc, with the two-result-set contract (assigned ids +
new versions) already contract-tested. This carries forward unchanged on SQL Server and is the
**target shape** for SQLite under §1.7 (which currently loops in C#).

**R-CRUD-4 — the conflict contract is engine-independent.** Zero rows affected by a
version-checked `UPDATE`/`DELETE` means `ConcurrencyConflictException` carrying the store's actual
current version. Identical on both engines today; contract-tested; unchanged.

**R-CRUD-5 — the CRUD procs are schema steps (§1.5, S-3 rule 4).** Deploying `Access/*` is not a
separate ritual sitting outside the version ledger. It is how the ordering bug the bootstrapper
comment already records — `CREATE OR ALTER PROCEDURE` eagerly validating column references against
*existing* tables, so `Payees_AccessProcs.sql` failed before the `Version` column migration ran —
stops being a comment explaining a hand-ordered sequence and becomes a step number.

### 1.6a Concurrency: version-checked on the shipped path, retry lives in the business layer
*(New in revision 3, resolving §4 item 2.)*

The owner has settled the product-scope question that was open as of revision 2: the shipped
application must support **a single human user and potentially an AI agent working against the
same books at the same time** — real simultaneous access, not merely tolerance of rare accidental
overlap. A lease (one exclusive holder, everyone else waits or is read-only) is the wrong
mechanism for that goal — it would force the human and the agent to take turns. **The shipped
SQLite path's permanent concurrency model is version-checked optimistic concurrency, exactly as
R-CRUD-4 already states it** — there is no lease anywhere in this design, and none is being added.

What revision 3 makes explicit is the **layer split**, in the owner's own words: *"We just need to
know that the transaction completed normally, or we need to fall back and re-query and then
re-try the transaction... The retry logic should probably be in the business layer."*

- **The data layer's contract stays exactly what R-CRUD-4 already says: attempt the write, report
  success, or throw `ConcurrencyConflictException` carrying the store's actual current version.**
  It does not retry, re-query, or decide how to reapply a change — that would require knowing what
  the caller's intent *was*, which is a business concept the store must not know about, for the
  same reason §3.1 already keeps "prompt" and "recent file" out of the store.
- **The business layer owns the retry loop.** On a caught `ConcurrencyConflictException`, an
  `AppServices`/use-case method (§3.1) re-queries current state, decides how to reapply the
  intended change (which may be "reapply verbatim," "merge," or "surface a conflict to the
  caller" — a policy choice per use case, not a data-layer concern), and retries the write. This
  is new logic, not a relocation of existing logic — no retry loop exists anywhere in the codebase
  today; today's behavior is "the conflict propagates to the UI as a message box" (per the
  superseded D-34 discussion this revision replaces).
- **The test tier needs a first-class way to trigger this on demand.** See §2.4's
  `FaultInjectingStore` — it is designed for exactly this and is formalized in revision 3 as the
  answer to the owner's *"the test business layer should have one or more APIs that will have a
  means to test this functionality."*

This is additive to R-CRUD-4, not a change to it — R-CRUD-4 already described an
engine-independent conflict *contract*; 1.6a states who is on the other end of it.

**Two consequences revision 6 adds here, because §1.6c's state assertions sit directly in this
loop's path.** Both are small and both are the kind of thing that is expensive to discover later:

1. **The retry loop must not catch the assertion exception, and the assertion must not be a
   `ConcurrencyConflictException`.** §1.6c's `SaveRoot`/`DeleteRoot` throw when the root is not in
   a state the method admits. That is a *caller bug* — a programming error, not a runtime race —
   and it is unconditionally reproducible. If it were signalled with the same exception type the
   retry loop catches, the loop would re-query, reapply and retry a call that can only ever fail
   the same way, turning an immediate, obvious crash into a spin. So the assertion throws
   `ArgumentException` (the *argument's* state is wrong, not the store's), the retry loop catches
   `ConcurrencyConflictException` **and nothing wider**, and §2.4's `FaultInjectingStore` gets one
   more job: proving the loop lets an `ArgumentException` straight through.
2. **A failed write leaves the root's change state untouched, which is what makes retry legal at
   all.** R-CRUD-2's `postCommitActions` deferral already guarantees this — `OnUpdated()` never runs
   unless the commit succeeded — so after a caught `ConcurrencyConflictException` a root that was
   `Changed` is still `Changed` and a root that was `Deleted` is still `Deleted`. The retry can
   therefore call the *same* method again and pass the same assertion. Under a unified `SaveRoot`
   this was true but invisible; with a state precondition on the method it becomes load-bearing,
   so it is stated rather than assumed, and it belongs in the Tier-2 contract suite as
   *"after a conflict, the same call is still admissible."*

### 1.6b Naming the store's write methods — `SaveRoot` / `SaveRoots` / `SaveTransfer`
*(New in revision 5. Developer leading, per the panel's C#/.NET idiom brief.)*

> **Read §1.6c next.** Everything in this subsection still holds — revision 6 does not reopen any
> of it — but revision 6 adds a **fourth** method, `DeleteRoot<TRoot>`, and narrows what `SaveRoot`
> accepts. §1.6c is the authoritative statement of the final write surface; §1.6b is where the
> `Root`-not-`One` and `Roots`-not-`Batch` reasoning lives, and revision 6 reuses that reasoning
> rather than re-deriving it.

**First, what is settled and not up for discussion here.** The owner has confirmed the *mechanism*:
these stay **generic (templated) methods**, one body serving `Account`, `Category`, `Transaction`,
`Payee`, `Security`, `RentBuilding` and everything else that implements `IAggregateRoot`, rather
than a hand-written `SaveAccount`/`SaveCategory`/`SaveTransaction` per type. That is the entire
point — it is what keeps the C# write surface at three methods instead of thirty, and it is what
makes §2.7.2's compile-time guarantee possible at all (a per-type method surface would have thirty
places for a DTO-shaped hole to open instead of one). **This subsection changes names, not shape.**

#### Why `SaveOne` and `SaveBatch` are worth fixing

The names are not arbitrary — they came from the 2026-09-16 persistence-concurrency design, which
inventoried every call site in the app and sorted them into four *groups* by shape, then named a
method per group: *"`SaveOne<T>` — Group 1's shape… `SaveTransfer` — Group 2's shape…
`SaveBatch` — Group 4's shape."* Naming a method after the migration bucket it was carved out of
is completely reasonable **while the migration is the thing everyone is thinking about**. That
reason has now expired: the groups are not a concept anyone reading `IMoneyStore` in six months
will have ever heard of, and what is left is a name that describes *cardinality* — how many things
you hand it — which is the least interesting fact about either method.

Three concrete problems, in increasing order of how much they cost:

1. **`One` is ambiguous at a glance.** It can be read as a quantity ("save one of them"), as an
   identity ("save *the* one"), or — read fast, in a call like `SaveOne(a)` — as a leftover from
   an `Update`/`UpdateOne`/`UpdateAll` family that does not exist. The owner's own first recall of
   this method was "`UpdateOne`," which is a small but real data point that the name is not
   sticking as written.
2. **Neither name says what the method accepts**, which is the one thing a reader actually needs.
   The constraint `where TRoot : IAggregateRoot` is the whole contract — it is why the method can
   be generic, it is why `Split` and `Investment` cannot be passed (they have no commit boundary
   of their own), and it is why §2.7.2's `store.Save…(transactionRow)` fails to compile. A name
   that says "one" tells you the arity, which the signature already told you. A name that says
   "root" tells you the *admission rule*, which the signature only tells you if you read the
   constraint clause.
3. **`Batch` names an engine-side strategy the port must not promise.** Today "batch" means a
   table-valued parameter on SQL Server and a C# `switch` loop on SQLite, and §1.7.1's slice-10
   benchmark may yet keep the loop if `json_each` loses on realistic batch sizes. A port method
   named after the implementation technique is a leak in exactly the direction this whole document
   is trying to close: if the benchmark says "loop wins," `SaveRoots` is still a true name and
   `SaveBatch` has quietly become a false one.

#### The recommendation

```csharp
void SaveRoot<TRoot>(TRoot root) where TRoot : IAggregateRoot;   // was SaveOne<T>
void SaveRoots(IReadOnlyList<IAggregateRoot> roots);             // was SaveBatch
void SaveTransfer(Transaction from, Transaction to);             // unchanged - see below
```

*(Revision 6 adds `void DeleteRoot<TRoot>(TRoot root)` beside `SaveRoot` and narrows `SaveRoot` to
refuse a deleted root. The three names above are unchanged by that; see §1.6c.)*

**Keep the verb, change the noun.** `Save` is the right verb and is not the problem: it is the
word the domain, the UI, the existing `IDatabase`, and the stored procs all already use, and
swapping it for `Commit`, `Persist`, `Write` or `Store` would churn every call site to say the
same thing in a less familiar way. (`Commit` was considered and rejected on a specific ground, not
a stylistic one — see the rejected list below.) What was wrong was the **noun**: `One` and `Batch`
describe the *shipment*; `Root` and `Roots` describe the *cargo*, which is what the caller has in
their hand and what the compiler is going to check.

**Why `SaveTransfer` is explicitly kept.** It is not being grandfathered in; it is the exemplar.
`SaveTransfer` already names its subject — a transfer, meaning exactly two peer `Transaction`s that
must co-commit — rather than its cardinality, and it is specific, self-describing and reads
naturally in a call site, which is precisely the property the owner asked for. The rule the panel
is applying is therefore **not new to this family**: two of the three names broke it, and the
third one is where the rule came from. Renaming it (to `SaveTransferPair`, say) would be change
for its own sake, and the panel says so rather than leaving it silently untouched.

#### Consistency with what this document has already established

This is the strongest argument for `SaveRoot`/`SaveRoots` over the alternatives, and it is an
argument from the document rather than from general taste. §2.6.2's test-control surface — written
two revisions ago, by a different role, without this naming question in view — independently
arrived at exactly this convention:

| §2.6.2 already says | The shape of the rule |
|---|---|
| `ClearTable(TableRef)` / `ClearTables(IReadOnlyCollection<TableRef>)` | verb + **subject**, singular for one, plural for many |
| `CaptureRow(...)` / `CaptureRows(...)` | same |
| `RestoreRow(...)` / `RestoreRows(...)` | same |
| `RemoveRow(...)` / `RemoveRows(...)` | same |
| `ClearTable(TableRef)` vs. `ClearTable<TRoot>()` | **overload** when the cardinality is the same and only the way of naming the target differs; **rename** when the cardinality differs |

`SaveRoot`/`SaveRoots` is that rule applied to the write port, with `TRoot` as the type-parameter
name for the same reason `ClearTable<TRoot>` uses it. §2.7.2's Tier-0 test name
`StoreWriteMethods_AcceptOnlyAggregateRoots` already reaches for "roots" as this surface's noun
when it has to describe the property in words — the interface simply had not caught up.

Against the broader .NET convention: the BCL's own singular/plural pair is `Add`/`AddRange`,
`Insert`/`InsertRange`, `Remove`/`RemoveRange`, and EF Core follows it with `Add`/`AddRange`,
`Update`/`UpdateRange`. `SaveRoot`/`SaveRootRange` is the letter-perfect translation of that and
the panel rejects it: `Range` carries a contiguity connotation from `List<T>` that is actively
wrong for a heterogeneous set of unrelated aggregate roots, and it reads badly. The in-document
convention wins over the BCL one where they disagree, because a reader of this codebase meets
`ClearTables` and `CaptureRows` long before they think about `List<T>`.

#### Alternatives the panel considered and rejected

| Candidate | Why not |
|---|---|
| `Save<TRoot>(root)` / `Save(IReadOnlyList<IAggregateRoot>)` — bare, overloaded | Tempting, and shortest. Rejected because `Save(MyMoney)` is being retired (§1) and a bare `Save` re-reads as its replacement — the one name guaranteed to confuse anyone who worked on the old `IDatabase`. Overloading also makes `store.Save(x)` un-greppable: you cannot search for the single-root call sites separately from the batch ones, which matters during a migration that will touch every one of them |
| `SaveAggregate` / `SaveAggregates` | **Rejected on a hard collision.** In this codebase "aggregate" already means reporting's group-and-subtotal: `IMoneyQuery.Aggregate(AggregateQuery)`, `AggregateRow`, §6.4's "two implementations of aggregation." `SaveAggregate` would read as "save a report total." `Root` carries the DDD meaning without the homonym |
| `Commit<TRoot>` / `CommitAll` | "Commit" is the right *concept* (atomic, all-or-nothing, R-CRUD-2) but the wrong *word here*: `IDbTransaction.Commit()` is three feet away in the same assembly, and a `Commit` on the store implies a preceding stage/track phase — a unit-of-work model this store deliberately does not have. Naming a method after a pattern you are not implementing is worse than a bland name |
| `Persist` / `Write` / `Store` | No clearer than `Save` and each costs something: `Persist` is jargon for the same idea, `Write` reads as raw I/O beneath the domain, and `store.StoreRoot(...)` stutters |
| `SaveRoot` / `SaveBatch` (rename only the single) | Leaves the family reading `SaveRoot`/`SaveBatch`/`SaveTransfer`, where the middle member alone is named after mechanism instead of subject — half a fix, and the half left undone is the one problem 3 above says will age badly |
| `SaveEntity` / `SaveEntities` | "Entity" is strictly weaker than "root": every aggregate root is an entity, but `Split` and `Investment` are entities too and are exactly what this method must refuse. `SaveEntity(split)` reads legal and is not |

#### The one honest cost, stated

`SaveRoot` and `SaveRoots` differ by one character. That is a real readability tax in a diff or a
skim, and the panel does not pretend otherwise — it is the same tax `ClearTable`/`ClearTables` and
`CaptureRow`/`CaptureRows` already pay in §2.6.2, accepted there for the same reason it is accepted
here: the alternative is an asymmetric pair where the two members look unrelated, which is worse
for the far more common task of *finding* the method than for the rarer task of telling two
adjacent call sites apart. The mitigation is the one already in the plan — §2.7.2's
`StoreWriteMethods_AcceptOnlyAggregateRoots` Tier-0 test means a future fourth write method has to
pass the same admission rule, so the family cannot grow a member whose name implies something the
constraint does not enforce.

#### What this costs to adopt, and where the old names deliberately survive

Nothing, today. `IMoneyStore` does not exist yet — it is created in slice 1 and first implemented
in slice 3 — so this is a rename applied to a design, not a refactor applied to a codebase. The
merged `IDatabase` on `master` keeps `SaveOne`/`SaveBatch`/`SaveTransfer` until the rebuild
replaces it, and every citation of it in §0/§0.1 keeps the old names because those sections are
this document's evidence, not its proposal.

The SQL layer likewise keeps its names: `SaveRoots` calls `dbo.Currencies_SaveBatch` with a
`dbo.CurrencySaveBatchRow` TVP, and that mismatch is intentional. The C# method is one generic
entry point over every root type; the proc is necessarily one object per table, because T-SQL
cannot be generic over differing column shapes. They are named for what each one actually is, and
the mapping between them lives in the engine adapter, which is the only place that should know
both vocabularies. Recorded here so a later slice does not "align" them in either direction.

### 1.6c Should a delete be reached by calling `Save`? — `SaveRoot` + `DeleteRoot`
*(New in revision 6. Developer leading on the C# shape; Adversarial Expert and Test Engineer
contributing the sections marked as theirs.)*

The owner: *"I don't like calling `SaveRoot` to perform a delete — 'Save' reads wrong for that
operation."* Their instinct was three fully separate methods. The coordinating session
counter-proposed three thin asserting wrappers over one shared internal. The panel was asked to
judge all three rather than ratify either, and the answer is **neither of them exactly** — the
evaluation below finds a fact about what `SaveRoot` actually does that changes the arithmetic.

#### First: the objection is upheld, and not as a matter of taste

Three independent reasons, in increasing weight:

1. **A destructive call that does not say so is invisible at review.** The line
   `store.SaveRoot(account)` is character-for-character identical whether that account is being
   created, edited, or permanently removed — the difference lives in a field on the object, set
   somewhere else. Nothing in the call site, the diff, or the grep result distinguishes them.
   Every other place in this design treats "the destructive operation should be nameable and
   findable" as a property worth paying for — §1's whole tier split, §2.6.4's
   `TestDatabase` guard, §6.1's honest statement of what the SQLite facade can claim. A write port
   where the delete is unnameable is out of step with the rest of the document.
2. **This codebase has a live way for a root to become deleted behind a caller's back.**
   `PersistentObject.OnDelete()` is a **soft** delete — it flips one field and fires an event; it
   does not remove anything from its container, and it has no guard of any kind (CLAUDE.md records
   this for `Category`, where only the *UI* layer checks whether transactions still reference the
   category). So a root reaching the store marked `Deleted` when its caller believed it was merely
   edited is a reachable state, not a thought experiment. Under a unified `SaveRoot`, that is a
   **silent wrong-behaviour** class: the row disappears and the call that did it reads like a save.
3. **The verb's object is wrong.** §1.6b's whole move was cardinality → subject (`One` → `Root`).
   Revision 6 finds the subject was still slightly off: the thing being saved is not the root, it
   is *the root's pending change*, and one of those changes is a removal. "Save the account"
   describing a deletion is a genuine mismatch between what the name says and what happens.

#### Second: what `SaveRoot` actually does today — checked, not recalled

This is the fact that reshapes the decision, and it is not what the framing of the question
assumed. `SaveRoot` does **not** have three internal outcomes. It has four, and the fourth is not
reachable by any name:

| Root's `ChangeType` | What the store does (`SqliteDatabase.SaveOneCategory`, `:1141`; same shape per root type) |
|---|---|
| `Inserted` | `INSERT`, then post-commit `RowVersion = 1`, `OnUpdated()` |
| `Changed` | version-checked `UPDATE`; zero rows → `ConcurrencyConflictException` |
| `Deleted` | version-checked `DELETE`; zero rows → `ConcurrencyConflictException` |
| `None` | **falls through the whole `if`-ladder to the comment "No pending change - nothing to do."** — a legal, silent no-op |

Two further facts, both verified in the tree rather than assumed, because the proposals below
stand or fall on them:

- **The state space is closed and the three predicates are mutually exclusive.**
  `PersistentObject.change` is a single field (`Money.cs:455`) and only four values are ever
  assigned to it — `None`, `Changed`, `Inserted`, `Deleted` (`:484`, `:492`, `:501`, `:513`,
  `:548`). `ChangeType`'s other four members (`Reloaded`, `Rebalanced`, `ChildChanged`,
  `TransientChanged`) are only ever *event-argument* values and never reach the field. So an
  assertion of the form "this root is `Inserted`" is well-defined and cannot be ambiguously true
  alongside another. **This was the panel's first worry about the asserting-wrapper proposal and
  it is not a real one.**
- **`OnUpdated()` deliberately does not clear `Deleted`** (`Money.cs:479-486`: `if (this.change !=
  ChangeType.Deleted)`). A root stays `IsDeleted` forever after a successful delete. That matters
  twice below.
- **A root whose own state is `None` can still have real work to do.** `SaveOneTransaction`
  (`SqliteDatabase.cs:1875-1958`) runs `SaveTransactionSplitsAndInvestment` **regardless** of the
  transaction's own change state — so a `Transaction` at `None` with a dirty `Split` writes rows
  through a call that produces no statement for the root itself. The root's change state is a
  description of the root's *row*, not of what the call does.

The consequence for this decision is direct: **"three separate operations" is a description of
three SQL branches, not of three caller intentions**, and a method named `UpdateRoot` would, for a
`Transaction`, routinely `INSERT` and `DELETE` split rows. Any option that promises "each method
does one kind of thing" is promising something the aggregate boundary does not deliver.

#### The three options, evaluated

**(a) Keep `SaveRoot` unified, exactly as revision 5 left it.**

- *Buys:* the smallest surface; zero divergence risk against the one part of the data layer that
  already has a proven, three-engine, ~60-test contract behind it; and — the strongest single
  argument for it — **it handles the call sites that genuinely do not know their own state.** The
  2026-09-16 call-site inventory's Group 1 is literally *"Dialog OK / `DataGrid.RowEditEnding`"*,
  and a grid row commit (`SecuritiesView.OnDataGridCommit`, `TransactionsView.OnDataGridCommit`)
  receives one object out of the row's `DataContext` with no idea whether it is the grid's
  new-row placeholder or an existing row being edited. A create-or-edit dialog is the same shape.
- *Costs:* the three reasons in the first section above. The delete is unnameable, unfindable, and
  reachable by accident.

**(b) Three fully independent methods, no shared internals (the owner's instinct).**

- *Buys:* the clearest possible names, and each method's SQL readable in one place.
- *Costs:* three, and the owner already anticipated only the first.
  1. **The code sharing given up is specifically the part that must not be given up.** R-CRUD-2's
     `postCommitActions` deferral is described in this document as *"a genuinely hard-won
     behavior — a stale `RowVersion` claiming a commit the rollback undid is a silent-corruption
     bug"*, carried forward *verbatim*. Three independent methods means three copies of the
     transaction/rollback/deferral scaffolding, hence three places for that bug to reappear, and
     nothing forcing them to agree.
  2. **It does not actually remove the unified dispatch — it duplicates it.** `SaveRoots` takes a
     heterogeneous, mixed-state collection *by definition* (reconciliation, merges: some roots
     changed, some deleted), so the insert/update/delete dispatch has to exist inside the batch
     path no matter what is done to the singular one. Option (b) buys three names at the price of
     a **second** implementation of something that is required anyway. On both engines today,
     `SaveOne<T>` is literally `SaveBatch(new[] { root })` — one identical line
     (`SqliteDatabase.cs:994`, `SqlServerStoredProcDatabase.cs:1710`) — so this is not a
     hypothetical duplication being avoided; it is an existing de-duplication being undone.
  3. **It pushes the dispatch into every caller.** Every create-or-edit call site becomes
     `if (x.IsInserted) store.AddRoot(x); else store.UpdateRoot(x);`. That is the store's one
     branch re-written N times in the business layer, which is issue #34's lesson (a hand-repeated
     decision drifts; a derived one cannot) applied at the write port instead of the clear list.

**(c) Three thin asserting wrappers over one shared internal (the coordinator's proposal).**

- *Buys:* cost 1 and cost 2 of option (b) vanish — the deferral, the transaction and the version
  check stay written once — while the caller still types a name that says what they mean. And the
  assertion is not decorative: reason 2 in the first section is a real, reachable, silent
  wrong-behaviour class that `UpdateRoot`'s `!IsDeleted` check converts into an immediate throw.
- *Costs:* **cost 3 of option (b) survives completely.** `AddRoot` asserting `IsInserted` and
  `UpdateRoot` asserting `IsChanged` are exactly as hostile to a create-or-edit call site as three
  independent methods are — more so, because now the wrong guess *throws* rather than quietly
  doing the right thing. The proposal fixes (b)'s structural problem and inherits (b)'s ergonomic
  one.

#### The Adversarial Expert's stress-test of (c), as asked

*Is the "assert the state matches" check valuable, or dead weight that will never fire?*
**Split verdict, and the split is the whole finding.**

- The **delete-related half is live** — that is, a save refusing a root marked deleted, and a
  delete requiring one. It fires on the one transition that genuinely happens behind a caller's
  back (`OnDelete()`'s guardless soft delete, reason 2 above), and the behaviour it prevents is row
  loss. That is the highest-value assertion available anywhere on this port.
- The `AddRoot`/`UpdateRoot` half is **mostly dead, and where it is not dead it is wrong.** For it
  to fire usefully, a caller would have to *believe* a root is new when it is persisted, or vice
  versa. But a caller that knows which one it is does not get it wrong, and a caller that does not
  know cannot call either — so the check does not catch a mistake, it manufactures one. The
  create-or-edit grid commit is not an edge case: it is Group 1, the single largest category in
  the call-site inventory this whole write surface was carved out of.

*Is there a real scenario where a caller genuinely does not know the object's state before
calling?* **Yes, and it is the common case, which is the opposite of what the counter-proposal
assumed.** `DataGrid.RowEditEnding` fires identically for the new-row placeholder and for an edit
of an existing row; the handler pulls one object from `e.Row.DataContext` and has one code path
(`SecuritiesView.xaml.cs:254`). A create-or-edit dialog (`AccountDialog`, used for both) is the
same. Conversely, the panel could not construct a call site that is unsure whether it is deleting:
deletion in this app is always a separate, deliberately-invoked command (`CategoriesControl.
Delete()`, a Delete menu item, a row-delete gesture), never the tail of an edit commit. **The
insert/update distinction is frequently unknown to the caller; the delete distinction never is.**
That asymmetry is the recommendation.

*Does asserting state in the singular methods, while `SaveRoots`/`SaveTransfer` take mixed-state
collections and assert nothing, create an asymmetry worth naming?* **Yes — name it, and it is
smaller under the recommendation than under (c).**

- `SaveRoots` **cannot** assert: mixed state is its purpose. Reconciliation commits a set of
  changed transactions; a merge commits a survivor (changed) plus a victim (deleted) in one
  transaction. Requiring uniform state would destroy the method.
- `SaveTransfer` **cannot** assert either: creating a new transfer passes two `Inserted`
  transactions, while `TransformTwoTransactionIntoTransfer` links two *existing* ones and passes
  two `Changed`.
- So `SaveRoots(new[] { root })` is, and remains, a one-line unvalidated route to the same
  executor the validated methods guard. **This is a real crack and the document says so rather than
  claiming a guarantee it does not have** — it is the same shape as §6.2's `InternalsVisibleTo`
  warning, arriving at the write port. Three things keep it honest: the batch method's name makes
  a single-element call visibly odd at review; the Tier-0 write-surface test below pins the public
  member set so a fourth unvalidated singular method cannot quietly appear; and under the
  recommendation only **one** distinction is being guarded, so there is exactly one thing the back
  door can be used to dodge instead of three.

#### The recommendation: (c)'s mechanism, two wrappers instead of three

```csharp
public interface IMoneyStore            // MyMoney.Business, alongside today's IDatabase
{
    // Insert-or-update. Refuses a root marked deleted.
    void SaveRoot<TRoot>(TRoot root)   where TRoot : PersistentObject, IAggregateRoot;

    // Remove. Refuses a root not marked deleted.
    void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot;

    // Unchanged from revision 5. Mixed-state by definition; assert nothing.
    void SaveRoots(IReadOnlyList<IAggregateRoot> roots);
    void SaveTransfer(Transaction from, Transaction to);
}
```

with, in each implementation, **one** private executor that all four call — the existing
insert/update/delete dispatch, unchanged, including its transaction, its version check and its
`postCommitActions` deferral.

Preconditions, stated exactly, because the loose version of them is wrong:

| Method | Admits | Throws `ArgumentException` on | Why not tighter |
|---|---|---|---|
| `SaveRoot` | `Inserted`, `Changed`, `None` | `Deleted` | `None` must be admitted: a `Transaction` at `None` with a dirty `Split` is a real, necessary write (`SaveOneTransaction` runs the owned-children pass regardless of the root's own state). Requiring `IsChanged` would reject it, and there would be no other method to call |
| `DeleteRoot` | `Deleted` | `Inserted`, `Changed`, `None` | Exact, because the caller always knows |

**`SaveRoot` therefore keeps a silent no-op path** (a genuinely clean root with genuinely clean
children), and the panel states that plainly rather than implying the name is now fully precise:
the store cannot tell "clean, nothing to do" from "clean root, dirty children" without inspecting
owned children, which is engine-specific work that belongs below the port, not in a precondition.
What `SaveRoot` now guarantees is the thing the owner actually asked for, stated at exactly the
strength it holds: **it will never remove the root's own row.** It can still delete rows belonging
to that root's *owned children* — a saved `Transaction` whose `Split` was removed deletes that
split — because the aggregate is the commit unit and a `Split` has no boundary of its own (§1.6b).
The looser claim ("`SaveRoot` never deletes anything") is the one a reader will assume, so the
precise one is written here and repeated in the honest-costs list below.

#### Why this is the right trade, in plain terms

The owner's objection is that one word is doing two jobs. The fix is to give the second job its own
word — not to split the first job in half as well. Creating and editing are two jobs the *caller*
usually cannot tell apart at the moment of the call (the grid does not know whether the row you
just finished typing is new); deleting is a job the caller always knows it is doing, because
deleting is always something a person deliberately asked for. So the split that matches reality is
one cut, not two. Making two cuts would force every "save whatever the user just typed" call site
to first ask a question it has no business asking, and to get it wrong sometimes — and the new
method would then throw in the caller's face for a distinction that never mattered to it.

And the cut that is made is the valuable one twice over: the delete becomes greppable (you can ask
"what in this codebase can remove an account?" and get an answer), and the accident where a root
was marked deleted somewhere else and a save quietly removed it now stops at the door.

#### Developer: the C# shape, and whether the shared mechanism stays reachable

The question that decides whether the validated names mean anything: **can a caller still reach the
unvalidated dispatch?** Three sub-answers.

1. **The shared executor is not on the port at all, and is not `public`.** `IMoneyStore` declares
   the four methods above and nothing else write-side. The dispatch is a `protected abstract void
   WriteRoots(IReadOnlyList<IAggregateRoot> roots)` on an abstract `MoneyStoreBase` in
   `MyMoney.Business` (beside the port, no engine code in it), implemented once per engine. The
   four public methods are **non-virtual** members of that base: each validates, wraps its argument
   if needed, and calls `WriteRoots`. Nothing public reaches the executor except through one of the
   four. `internal` + `InternalsVisibleTo` is explicitly not the mechanism here — §6.2.
2. **Writing the validation once, in the base, is the point.** If each engine carried its own copy
   of the precondition, the two engines could disagree about what `SaveRoot` admits, which is
   exactly the class of divergence §6.8 exists to catch and the contract suite exists to police —
   at which point the check has become another thing to keep in sync. One base-class
   implementation, one behaviour, one test. It also removes real duplication that exists today:
   `SaveOne<T>` is the same one-line delegation on both engines (`SqliteDatabase.cs:994`,
   `SqlServerStoredProcDatabase.cs:1710`), and that line moves into the base and is deleted twice.
3. **Decorators are unaffected, and should not derive from the base.** `RecordingStore` and
   `FaultInjectingStore` (§2.4) implement `IMoneyStore` directly and forward each of the four
   public methods to the inner store, where the precondition fires. They record and inject at the
   *public* level, which is what they want anyway (`SaveRoot(Account#3, v2)` is a more useful
   recording than `WriteRoots([...])`). `protected abstract WriteRoots` being unreachable from a
   decorator is therefore not a constraint to work around — it is the correct shape.

One rejected alternative worth recording: C# default interface implementations would let the four
validated methods live on `IMoneyStore` itself over a single abstract member, with no base class.
It works on .NET 10 and it is elegant. The panel declines it for the same reason §1's Candidate A
notes about assembly count — *"not the shape most .NET developers reach for first; needs the
reasoning written down or it gets 'simplified' back in six months"* — and here the abstract base
costs nothing the DIM version saves.

#### Test Engineer: what this changes in the test plan

**Yes, this needs real coverage, and it lands in two tiers, not one.** A precondition that is only
documented is a comment.

1. **Tier 0, new — the write surface is exactly the four named methods.** Reflection over
   `IMoneyStore`: the set of public write members is `{SaveRoot, DeleteRoot, SaveRoots,
   SaveTransfer}` and no other, and none of them is an unvalidated singular-root method. This is
   the test that gives the design its value over time, because the whole benefit evaporates the day
   someone adds a convenience `Save<T>` back. It sits beside §2.7.2's
   `StoreWriteMethods_AcceptOnlyAggregateRoots`, which is unchanged in intent but now enumerates
   four members instead of three.

   ```
   StoreWriteSurface_IsExactlyTheFourNamedMethods        (revision 6, §1.6c)
   ```
2. **Tier 2, new — the preconditions actually throw, per engine.** Six cases, cheap and fast:
   `SaveRoot` on a `Deleted` root throws `ArgumentException` **and writes nothing**; `DeleteRoot`
   on an `Inserted`, a `Changed` and a `None` root each throw and write nothing; `SaveRoot` on a
   `None` root whose owned child is dirty **succeeds and writes the child** (the case a tighter
   precondition would have broken — this is the regression test for the mistake the panel nearly
   made); and, per §1.6a, *after a `ConcurrencyConflictException` the same call is still
   admissible*, which is what makes the business-layer retry loop legal.

   These run per engine in the shared contract suite even though the check lives once in
   `MoneyStoreBase`. That is deliberate and cheap: the contract suite's job is to prove the two
   engines are indistinguishable through the port, and a precondition is part of the port's
   observable behaviour.
3. **`FaultInjectingStore` gains one job** (§2.4, §1.6a): proving the retry loop lets an
   `ArgumentException` through rather than retrying it. A caller-bug exception caught by a retry
   loop becomes a spin, and that is a nastier failure than the original bug.
4. **Nothing else in the test plan moves.** §2.6's test-control ladder, §2.6.4's `ResetIdentity`
   parity assertion and §2.7.2's projection tests are untouched — they sit either below the port
   (test control) or on the type system (projections), and neither cares how many names the write
   surface has.

#### The honest costs

- **`SaveRoot` is still not a fully precise name.** It admits `None` and no-ops. See the table
  above for why that is deliberate and why the alternative is worse.
- **`SaveRoot` can still cause `DELETE` statements to run** — for *owned children*. Saving a
  `Transaction` whose `Split` was removed deletes that split's row. This is correct (the aggregate
  is the unit, and the split has no commit boundary of its own — §1.6b), but it means the guarantee
  is precisely *"`SaveRoot` never removes the root's own row"*, not *"`SaveRoot` never deletes
  anything."* Worth stating in exactly those words, because the looser claim is the one a reader
  will assume.
- **`DeleteRoot` (store) now sits one character from `DeleteRow` (test control, §2.6.2), and they
  do dangerously different things** — version-checked domain delete versus raw, unchecked,
  below-the-graph row removal. §1.6b already accepted a one-character tax for
  `SaveRoot`/`SaveRoots`; this is a second one and it is the more dangerous of the two. Mitigations,
  none of which is "be careful": they live in different assemblies, are reached through different
  interfaces, and the test-control one is obtainable only through the guarded factory of §2.6.4.
  §2.6.3's comparison table is amended in this revision to lead with the two names side by side, so
  the collision lands in the one place the difference is explained.
- **A latent issue this rename surfaces rather than creates, flagged not fixed.** Because
  `OnUpdated()` never clears `Deleted` (`Money.cs:482`), and because a root that was created and
  then deleted before any save is also `Deleted`, `DeleteRoot` can be handed a root that was never
  persisted — whereupon the version-checked `DELETE` affects zero rows and raises
  `ConcurrencyConflictException`, which is a misleading diagnosis of "this row was never there."
  The same is true of calling delete twice. This behaviour exists today under `SaveRoot` and is not
  a regression; a named `DeleteRoot` merely makes it *statable*. **Recommendation: the Tier-2
  contract suite pins the intended answer** (the panel's view is that deleting a never-persisted
  root should be a no-op, detectable via `RowVersion == 0`, but that is a semantics call the
  implementation slice should make deliberately rather than inherit).

#### Alternatives the panel considered and rejected *(revision 6)*

| Candidate | Why not |
|---|---|
| `AddRoot` / `UpdateRoot` / `DeleteRoot`, fully independent (owner's instinct) | Three copies of R-CRUD-2's post-commit deferral; does not remove the unified dispatch because `SaveRoots` needs it anyway; and it pushes an `if (x.IsInserted)` branch into every create-or-edit call site. See option (b) above |
| `AddRoot` / `UpdateRoot` / `DeleteRoot` as asserting wrappers (coordinator's proposal) | Right mechanism, one wrapper too many. The Add/Update assertion cannot fire on a caller that knows its own state and *falsely* fires on the many that do not — Group 1's grid-row and create-or-edit-dialog commits. Adopted in its shared-internal form; see the recommendation |
| `SaveChanges<TRoot>(root)`, EF Core's naming, keeping one method | Genuinely tempting: EF's `SaveChanges` deletes rows and surprises nobody, because the noun is *changes*, not the entity. Rejected because it fixes the reading and not the finding — the delete is still unnameable and ungreppable, and reason 2's behind-your-back soft delete still lands silently. It also invites the unit-of-work reading §1.6b rejected for `Commit` |
| `SaveRoot` / `RemoveRoot` | Pure naming preference over `DeleteRoot`. `Delete` matches the SQL, matches `ChangeType.Deleted`, matches the domain's `OnDelete()`, and matches §2.6.2's `DeleteRow`. `Remove` is already spoken for by `RemoveRow`/`RemoveRows`, where it means *"temporarily, and restored on `Dispose`"* — the opposite of permanence |
| Keep unified `SaveRoot` and add `DeleteRoot` as an *alias* both admitting any state | The worst of both: two names that do the same thing teach a reader that the distinction is cosmetic, and the assertion — the one part with real bug-catching value — is gone |
| Fold delete into `IMoneyStoreTestControl` and leave `IMoneyStore` write-only-ish | Category error. Deleting an account is a domain operation with version checks and domain semantics (§2.6.3's table). Test control is fixture machinery that runs *below* the version check and is not shipped |

---

## 1.7 New design principle: SQLite should use SQLite (Data Engine Expert)

> **P-SQLITE.** Where SQLite can express a behavior natively, express it in SQLite — not in
> imperative C# around SQLite. The goal is that `IMoneyStore` sits over two engines that *work*
> alike, not two engines that merely *expose* alike.

The owner's reasoning is sound and the panel endorses it, with one caution stated up front and
again in §6.8: **this principle is worth following where it removes a genuine behavioral
difference, and worth resisting where it merely relocates one.** What follows is the concrete
audit the guidance asked for — what SQLite offers that the current code doesn't use, and where the
limits are real.

### 1.7.1 What SQLite actually offers that this codebase is not using

Engine version 3.53.4 ships with all of these (§0.1); none needs a package change.

| SQLite capability | Not used today | What it would buy | Panel verdict |
|---|---|---|---|
| **`RETURNING`** (3.35+) | `SaveOneCategory` computes `c.RowVersion = callerRowVersion + 1` in C# after an `UPDATE` that did `Version = Version + 1` server-side | The *authoritative* post-write version, read from the engine, exactly as SQL Server's `_SaveBatch` procs return theirs. Removes an inference that is correct today only because nothing else can touch the row mid-transaction | **Adopt.** Small, removes a real C#-side simulation of a database fact, and makes both engines report versions the same way |
| **`json_each()` / `json_tree()`** (3.38+, built in) | SQLite loops in C#: one `ExecuteNonQueryInTransaction` per root | The genuine **TVP equivalent**. Pass the whole batch as one JSON parameter, then `INSERT ... SELECT ... FROM json_each(@Rows)` — one parameterized statement, set-based, engine-side, one transaction. This is the single largest structural convergence available between the two engines | **Adopt, measured.** It is the direct answer to R-CRUD-3 on SQLite. Verify with a benchmark: at small batch sizes a prepared-statement C# loop inside one transaction may genuinely beat JSON parsing, and "more native" is not a reason to be slower. Measure, then choose — and if the loop wins, say so here rather than adopting for aesthetics |
| **Views** | No `CREATE VIEW` anywhere | Read-side shapes (`IMoneyQuery`'s aggregates) defined once in SQL rather than assembled in C#, and defined in a *schema step* so they are versioned like everything else | **Adopt — as a read surface only.** This is where §1's `IMoneyQuery` should land on SQLite, and it is the closest parity with SQL Server's `_Query` proc family. Revision 4 adds the constraints: explicit column lists (never `SELECT *`), a view change is its own numbered step, and **nothing writes through a view** — see §2.7 |
| **`INSTEAD OF` triggers on views** | Not used | The closest thing SQLite has to a **stored procedure for writes**: multi-statement write logic that lives in the database, is versioned as a schema step, and is invoked by a plain `INSERT INTO SomeView VALUES (...)`. A store-tier caller issues one statement; the multi-step logic runs engine-side | **Revision 4: do not adopt now.** Demoted from round 2's "adopt selectively." A trigger-backed writable view is the one construct that would make the read/write asymmetry §2.7 establishes a *convention* instead of a *property*, and SQLite has no grant with which to make a view read-only in the engine. It also collides with the `RETURNING` adoption one row above (a post-write version read through a trigger-backed view is, at best, unverified — §7 item 4). Kept as a **deferred, single-candidate spike** (transfer both-sides), with a standing constraint if it is ever taken: never above `IMoneyStore`. Reasoning and the owner question in §2.7.3 |
| **`CHECK` constraints** | Not emitted | Domain invariants enforced by the engine on both engines instead of by C# on one | **Adopt** for invariants that are genuinely schema-level (non-negative where truly non-negative, enum ranges). Resist the urge to push business rules in: a `CHECK` that fires is an opaque error, and §7's UI note about plain-language failures applies |
| **`STRICT` tables** (3.37+) | Not used — SQLite's default dynamic typing silently accepts a string in an `INTEGER` column | Column types that actually mean something, which is *exactly* the kind of SQL-Server-alike behavior this principle is about. It is also the single cheapest way to stop a type mismatch surviving a SQLite test and failing on SQL Server | **Adopt.** Strong recommendation; low cost since the schema is being rebuilt from zero anyway |
| **Generated columns** (3.31+) | Not used | Derived values computed by the engine rather than assigned in C# | **Note, don't adopt yet.** No current need; listing it so the option is known |
| **`UPSERT` (`ON CONFLICT DO UPDATE`)** (3.24+) | Not used | Insert-or-update in one statement | **Note, don't adopt.** The store's write paths are explicitly branch-on-`IsInserted`/`IsChanged`/`IsDeleted` because the *change type* is a domain concept carrying version semantics. Collapsing it into an upsert would lose the conflict check, which is a regression dressed as a simplification. **Revision 6 does not change this**: splitting `DeleteRoot` out of `SaveRoot` (§1.6c) changes which *name* a caller types, not the internal branching — `SaveRoot` still chooses `INSERT` or `UPDATE` by change type inside one executor, and it does so for the same version-semantics reason this row gives for refusing to let SQL make that choice instead |
| **`PRAGMA index_list` / `table_info` / `foreign_key_list`** | `TableExists` scrapes `sqlite_master.sql` text | Structured schema introspection — exactly what `Schema_Verify` (§1.5, S-4) needs, and what issue #34's suggested fix named | **Adopt.** Required by §1.5 regardless |
| **`PRAGMA foreign_key_check`, `integrity_check`, `quick_check`** | Not used | An engine-native consistency assertion usable by the test tier and by the deferred backup/restore verification (§1.8) | **Adopt** in the test tier and provisioner |
| **`ATTACH DATABASE` + `VACUUM INTO`** | `VACUUM INTO` noted as absent-by-design from the store tier | A file-level backup/duplication primitive that is transactionally consistent, unlike `File.Copy` of a live WAL database | **Adopt in the provisioner tier**, where §3.2 already puts engine-native backup — and note it is *better* than the close-and-`File.Copy` round 1 described, because it does not require closing |

### 1.7.2 Where the limits are real — no amount of "use more SQLite" closes these

Stated flatly, because the owner's goal is that the two engines *appear* similar through
`IMoneyStore`, and an abstraction that oversells that is worse than one that documents the seam:

| Limit | Why it is unclosable | What the design does instead |
|---|---|---|
| **No stored procedures.** SQLite has no procedural language at all — no `CREATE PROCEDURE`, no variables, no control flow. | It is a library linked into the process, not a server with a query engine that executes code on your behalf. | Views cover *set-shaped read* logic. **Revision 4: multi-statement *write* logic does not get the `INSTEAD OF` escape hatch after all** (§1.7.1, §2.7.3) — it stays in the store assembly, inside one transaction, which is where `SaveTransfer` and `SaveRoots` already put it and where R-CRUD-2's post-commit deferral already lives. Everything needing loops or branching likewise stays in the provisioning/store assembly as versioned, embedded SQL plus a thin executor. §1.5's process table names this as step 2's real divergence rather than papering it, and revision 4 makes the gap slightly *wider* and more honest than revision 2 claimed. |
| **No server-side identities, roles, grants or permissions.** There is no `MyMoneyUser` on SQLite, and no `GRANT EXECUTE`. | There is no server and no authentication boundary. The process that opens the file has the file's OS rights, entirely. | §1's tier-per-assembly model, and §6.1's already-written honest statement of exactly how far that falls short. Revision 2 changes nothing here and adds no new claim. |
| **Triggers cannot be conditionally bypassed by privilege.** | Same reason. | Don't put test-tier-only behavior in a trigger. Test-tier capability stays an assembly-presence question. |
| **No table-valued parameters as a typed, server-declared object.** `json_each` is functionally equivalent but is not a declared type with a schema the engine validates. | SQLite has no user-defined types. | Accept the asymmetry; the contract suite (not the type system) is what proves the two batch paths behave identically. |
| **DDL-in-a-transaction differs in the details.** Both engines wrap DDL in transactions, but SQLite cannot `ALTER TABLE DROP CONSTRAINT`, has limited `ALTER TABLE` generally, and historically needs the 12-step table-rebuild dance for anything structural. | Engine design. | §1.5's steps are written per-dialect anyway. The *ledger, ordering and transaction-per-step semantics* are identical; the SQL inside a step is not, and never claimed to be. During the nuke-and-pave phase this is nearly free — a table rebuild on an empty test database is trivial — which is a genuine, if temporary, argument for getting the schema shape right *now*, while restructuring is cheap. |
| **No server-side scheduled/background work.** | No server. | Nothing in the design depends on it. Listed so nobody proposes it. |

### 1.7.3 One place the panel pushes back on the principle itself

**Do not move the version-conflict check into a SQLite trigger.** It is tempting — a `BEFORE
UPDATE` trigger raising `RAISE(ABORT, ...)` on a version mismatch is more "native" than checking
`rowsAffected == 0` in C#. Resist it:

- The current check is already engine-side (`WHERE Id=@Id AND Version=@ExpectedVersion`); what's in
  C# is only the *interpretation* of zero affected rows. That is not simulation, it's a return-code
  read.
- A trigger would make the two engines' conflict signal genuinely different: an aborted statement
  with a message string on SQLite, versus a rowcount plus a follow-up `SELECT` on SQL Server. That
  is **more** divergence, not less, and the conflict path is the single most contract-tested
  behavior in the data layer.
- It would push a well-tested behavior into a place that is harder to test, to satisfy a principle
  whose purpose is convergence it does not deliver here.

This is the shape of the general caution: P-SQLITE is a tool for removing real asymmetries
(1.7.1's `json_each`, `RETURNING`, views, `STRICT`), not a goal to be maximized.

### 1.7.4 The second place the panel pushes back — views are a read surface *(new in revision 4)*

> **P-VIEW.** A view is a **query surface only.** No layer of this application writes through a
> view — not the business layer, and (for now) not the data layer either. Views are adopted
> enthusiastically for reads (§1.7.1) and are the SQLite landing site for `IMoneyQuery`; the
> write side goes through `IMoneyStore`'s methods against real tables, always.

This is the owner's rule, adopted. It is stated here so it sits next to P-SQLITE, because the two
principles pull against each other on exactly one construct — `INSTEAD OF` triggers — and a reader
who finds P-SQLITE first needs to find the exception at the same time. **§2.7 is the full
treatment**: how reset interacts with views (§2.7.1), how the type system makes P-VIEW structural
rather than remembered (§2.7.2), and the reconciliation with §1.7.1 plus the one question flagged
back to the owner (§2.7.3).

---

## 1.8 The real production-upgrade workflow — explicitly deferred, explicitly not foreclosed

The owner: *"Eventually when we turn on a production db, we cannot just 'nuke and pave' to upgrade
a db. We have to have a real upgrade process for the db and the app."*

**This document does not design that workflow.** The shape was already agreed conceptually in an
earlier session — backup, data reformatting, stored-procedure modification, app-binary update,
automated test-run verification, with test databases marked in config so they are distinguishable
from production ones — and was explicitly deferred with *"all current databases treated as test
databases until production testing workflow is needed."* That deferral stands. Nothing in the
current slice plan builds it.

What revision 2 **does** owe is the compatibility check the owner asked for: where would today's
design make that future work harder? The panel found three places, and all three are cheap now and
expensive later:

1. **The ledger has to exist from the first schema step, not be retrofitted.** A production upgrade
   needs to answer *"what version is this database, what has been applied to it, and is the step-7
   in this build the step-7 that ran?"* A database created before the ledger existed can never
   answer that — it can only be guessed at by introspection. This is why §1.5 S-2 puts
   `__SchemaHistory` in from slice 2 and why `ChecksumHash` is populated from day one even though
   nothing checks it against a second build yet. **Cost now: one table and a hash. Cost later:
   unanswerable.**
2. **The upgrade path has to be exercised continuously, or it is fiction.** §1.5's whole design —
   one mechanism, fresh-create *is* upgrade-from-0, plus S-4's fresh-vs-upgraded schema-equality
   test — exists to make the never-yet-needed path run on every build. Without it, the first real
   upgrade would be the first execution of code nobody has ever run, against the only data that
   has ever mattered. **This is the single most important compatibility property in the document.**
3. **`Backup` must be in the provisioner contract and contract-tested from the start.** The
   production workflow's first step is a backup, and the Operations Engineer's standing position
   (§3.2) is that backup is the capability floor's weakest plank *today*. A backup routine that
   only appears when production appears is a backup routine that has never been restored from.
   §1.7.1's `VACUUM INTO` finding strengthens this: a transactionally-consistent SQLite backup is
   available and better than close-and-copy, so there's no cost argument for deferring it.

Two further notes, flagged rather than designed:

- **`DatabaseEntry.TestDatabase` is today the only thing standing between a destructive operation
  and a database.** It already gates test-proc deployment on SQL Server. When production databases
  become real, `Schema_DropAll` and the whole test tier must refuse to run against an entry without
  that flag — and the panel's position is that the refusal belongs in the **provisioner contract**
  (fail loudly, always compiled in) rather than only in the test-tier assembly's absence, because
  "the assembly isn't shipped" protects end users but does not protect a developer running a test
  suite against the wrong registry entry. That is a real, currently-live exposure on the owner's
  own machine, not a hypothetical.

  > **Revision 8 widens this bullet's scope, deliberately.** "Always compiled in" was chosen above
  > for a developer-protection reason; revision 8 gives it a second, *customer-facing* consumer
  > (§1.9's sample-data feature), and in doing so changes the guard's stated scope from
  > **"destructive operations fail loudly against an entry not marked as a test database"** to
  > **"operations that must only ever touch a test database fail loudly against an entry not
  > marked as one."** There are now two kinds: operations that *destroy* data, and operations that
  > *fabricate data indistinguishable from real data*. Sample-data generation is squarely the
  > second and is not destructive at all — so a reader who stops at the word "destructive" would
  > wrongly conclude it is out of the guard's scope. It is not, and §1.9 is why. The mechanism is
  > unchanged; only the sentence describing what it covers is.

  > **Revision 9 promotes this same permanence from a note to the actual resolution of §4 item 8.**
  > The flag's "set once at creation, never changed" property is not just an implementation detail
  > supporting the future upgrade workflow or a scope-widening for sample data — it is the entire
  > answer to "when does nuke-and-pave end." See §4 item 8.
- **App-binary/schema compatibility is a two-sided question this document only half-answers.**
  `Schema_ApplyTo` handles "the app is newer than the database." The reverse — an older binary
  opening a database at a *higher* version than it knows — needs a refusal, not a best-effort open.
  Minimum viable shape: the store checks `Schema_CurrentVersion()` at open and fails with a
  plain-language message if it exceeds the version the binary was built for. Cheap to add now,
  awkward to add once databases are out in the world. **Recommend adding it in slice 3**; not
  otherwise part of the deferred workflow.

---

## 1.9 Sample data — a shipped feature with a runtime test-database guard *(new in revision 8)*

The owner, resolving §4 item 7, verbatim:

> *"test only. but in this case, they are letting the customer test the capability of the software
> to see what the UI and the reports look like. I guess in that sense, it could be considered a
> feature, but I would argue, that it should only be done on a db marked as test."*

That is not a yes/no answer to the question the panel asked, and it should not be recorded as one.
Worked through, it says three separate things:

1. Sample-data generation is **reachable by end customers** of a shipped release. *"Let me see what
   the UI and the reports look like before I commit my real finances to this"* is a legitimate
   product capability — the evaluation path this whole fork exists to walk, in fact.
2. It is nonetheless **"test only"** in the sense the owner means: the data it produces is
   fictitious, and it has no business being mixed into a database someone keeps.
3. The line between those two is drawn **at the database**, not at the audience: *"only on a db
   marked as test"* — regardless of who invokes it, or why, or from where.

### 1.9.1 The tension this creates with §1's two protection mechanisms

This document has established two different ways to keep a dangerous capability away from a
database, for two different tiers, and **sample data fits neither cleanly**:

| Mechanism | Where it is used today | Why it does not work here |
|---|---|---|
| **Assembly absence** — the code is not in a release build's bits, and production cannot even *name* the type (§1 Candidate A; `IMoneyStoreTestControl` in `MyMoney.TestKit.Contracts`, §2.5 layer 4) | `IMoneyStoreTestControl` and both `*.TestTier` implementations; `MyMoney.Data.SqlServer*` under D-37 | It would make the feature **unreachable by the customer it exists for.** If `SampleDataGenerator` lived in `MyMoney.TestKit`, a release build would contain no way to invoke it at all — which is the opposite of what the owner's first clause asks for |
| **Runtime flag refusal** — a check against `DatabaseEntry.TestDatabase`, always compiled in, that throws (§1.8, slice 5b) | Destructive provisioner operations (`Schema_DropAll`, delete, restore-over) | Nothing structurally; but its *stated scope* was **"destructive operations"**, and sample-data generation is purely **additive**. The mechanism fits; the sentence describing it did not |

The resolution is therefore the second mechanism with its scope corrected, and — importantly —
**no new mechanism is invented.** §1.8 already put the refusal in an always-compiled-in contract
rather than in the never-shipped test tier, for its own reasons. That decision turns out to be
exactly what a shipped, customer-reachable, test-database-only capability needs, so sample data
reuses the *same static method and the same exception type*, not a parallel one that can drift.

### 1.9.2 Where the flag lives: on the open store handle, not at the call site

The guard needs an answer to *"is the database I am about to write to marked as a test
database?"* The naive shape passes a `DatabaseEntry` into the service's constructor. The panel
rejects it: a caller can pass a **different** entry than the one the store was actually opened
from, and nothing would notice. The check must read the flag off the same object the writes go to.

So the identity of the open database becomes a property of the open store:

```csharp
// MyMoney.Business, beside IMoneyStore and MoneyStoreBase (§1.6c)
public sealed record StoreIdentity(
    string         DisplayName,      // the registry entry's key — for plain-language messages
    DataEngineType Engine,
    bool           IsTestDatabase,   // DatabaseEntry.TestDatabase, as of open
    int            SchemaVersion);   // Schema_CurrentVersion() at open (§1.8's second bullet)

public interface IMoneyStore
{
    StoreIdentity Identity { get; }   // read-only; the only new member revision 8 adds
    // ... the four write members (§1.6c) and the reads, unchanged
}
```

**This costs no new I/O.** §1.8 already requires the store to read `Schema_CurrentVersion()` at
open (to refuse a database newer than the binary), and `IsTestDatabase` comes from the same
`DatabaseEntry` that supplies the connection string. `Identity` is a read-only property, not a
write member, so §1.6c's `StoreWriteSurface_IsExactlyTheFourNamedMethods` Tier-0 test is
unaffected — and that test is also the reason the guard **cannot** live inside `IMoneyStore`:
adding a fifth, gated write member would break it, and gating `SaveRoots` itself on
`IsTestDatabase` would gate every ordinary write in the product. The guard therefore necessarily
lives at the **capability** boundary, one layer above the store. That is a constraint the design
derived, not a preference.

**Every failure mode of the input is fail-safe**, which is worth recording because it is load-
bearing and it is luck the panel should not rely on silently: `DatabaseEntry.TestDatabase` is a
`bool` defaulting to `false`, so a missing JSON field, a casing regression (the exact-casing
hazard already pinned by `DatabaseRegistryTests.Save_WritesTestDatabaseFieldWithExactCasing`), an
unparsed registry, or a store opened from an entry that was never written all resolve to
`false` — i.e. the capability **refuses**. There is no way for the flag to break *open*.

### 1.9.3 The guard itself — one type, three call sites

```csharp
// MyMoney.Business. Always compiled in, in every configuration. Never in a *.TestKit* assembly.
public static class TestDatabaseGuard
{
    public static void Require(StoreIdentity identity, string capability)
    {
        if (!identity.IsTestDatabase)
        {
            throw new TestDatabaseRequiredException(identity.DisplayName, capability);
        }
    }
}

public sealed class TestDatabaseRequiredException : Exception
{
    public string DatabaseDisplayName { get; }
    public string Capability { get; }
}
```

Exactly three call sites, and the third is the whole point of this section:

| # | Caller | Protects | Reachable in a release build? |
|---|---|---|---|
| 1 | Destructive `IMoneyStoreProvisioner` operations — `Schema_DropAll`, delete, restore-over-the-top | The owner's own machine, today (§1.8) | Yes — `MyMoney.Data.Sqlite.Provisioning` is a shipped assembly (§1, Candidate A) |
| 2 | The point an `IMoneyStoreTestControl` is **obtained** — its guarded factory, once, per §2.6.4 (*not* repeated in every method: §2.6.4 rejects that explicitly, on the grounds that the twenty-first method is the one that forgets) — defense in depth *behind* assembly absence | A developer running the test suite against the wrong registry entry | No — the test tier is never shipped |
| 3 | **`SampleDataService.Populate` (revision 8)** | A customer, an agent, or a script fabricating thousands of fictitious transactions into a database someone intends to keep | **Yes — and it is the only one of the three a customer can reach** |

### 1.9.4 Placement and API — the generator splits in two

`SampleDataGenerator` is in `MyMoney.Business` **today** (`Source/WPF/MyMoney.Business/SampleDataGenerator.cs`,
with the WPF-facing options-dialog half as `Walkabout.Assistance.SampleDatabase` in `MyMoney.csproj`).
It **stays in `MyMoney.Business` and stays shipped.** What changes is that it splits along the
guard line, so the guard has exactly one thing to sit in front of:

```
MyMoney.Business  [shipped]

  Walkabout.Business.Sampling.SampleDataSpec       the SampleData histogram (accounts, payee
                                                   frequencies, employer, paycheck) — a plain
                                                   deserialized spec object

  Walkabout.Business.Sampling.SampleDataFactory    PURE. (spec, options, quotes, seed) -> SampleDataSet
                                                   No store, no I/O, no ambient graph, no guard.
                                                   Deterministic for a given seed.

  Walkabout.Business.AppServices.SampleDataService GUARDED. Holds an IMoneyStore. Writes through
                                                   the ordinary write surface. Nothing else.
```

```csharp
public sealed class SampleDataService          // AppServices band (§3.1)
{
    private readonly IMoneyStore store;

    public void Populate(SampleDataSpec spec, SampleDataOptions options)
    {
        TestDatabaseGuard.Require(this.store.Identity, "Add sample data");   // FIRST statement
        SampleDataSet set = SampleDataFactory.Build(spec, options);
        // FK order: currencies -> accounts -> payees -> categories -> securities -> transactions
        this.store.SaveRoots(set.Accounts);
        this.store.SaveRoots(set.Payees);
        // ... etc. Ordinary writes. Ordinary version checks. Ordinary retry loop (§1.6a).
    }
}
```

**The guard is the first statement**, before generation and long before any write, so a refusal
costs nothing and — critically — leaves **nothing partially written**. A refusal that happened
after three of six `SaveRoots` calls would be worse than no guard, because it would leave a
non-test database containing fictitious accounts.

`SampleDataFactory` being **pure** is not cosmetic. It means:

- the existing `randomSeed:` determinism that `SampleDataRegressionTests.cs` already depends on
  survives the reshape intact, and is testable with no database of any kind;
- the stock-quote history the current generator needs (`Dictionary<string, StockQuoteHistory>`,
  loaded from disk today by the WPF half) stays an **injected input** rather than an I/O
  dependency, keeping the factory pure and the WPF half responsible for the file access it
  already owns;
- there is exactly one guarded entry point to protect, rather than a guard sprinkled through
  generation code.

**One concrete rewrite this revision surfaces, which is not a relocation.** Today's
`SampleDataGenerator.Create` *mutates an ambient live `MyMoney` object graph* —
`accounts.AddAccount(sa.Name)` against `this.money` — and relies on a later whole-graph save.
**Revision 7 killed that shape**: `Load()` does not survive, so there is no ambient graph to
mutate. `SampleDataFactory` must therefore **return** a `SampleDataSet` of roots rather than
mutate anything, and `SampleDataService` writes them. This is real work, not a project-file move,
and it should be sized as such whenever sample data gets scheduled.

### 1.9.5 Is the write path special? No — and that is a decision, not an omission

**`SampleDataService` writes through `IMoneyStore.SaveRoots` and nothing else.** No provisioner
access, no test-control access, no DDL, no identity reseed, no version-check bypass, no bulk
loader. It is an ordinary business-layer feature whose only unusual property is that it refuses to
run against a non-test database.

The tempting alternative — give it a fast privileged bulk path through the test-control tier, since
it inserts thousands of rows — is rejected, and §2.6.2 already contains the argument: `Seed` was
deliberately **removed** from `IMoneyStoreTestControl` because *"it stops a 'convenience' seeder
from quietly becoming a second, domain-rule-free way to create data."* Sample data is the most
load-bearing instance of that argument in the whole document, for a reason specific to what the
owner said it is for: **a customer evaluating the product is evaluating what the real write path
produces.** A sample database built by a privileged loader that skipped FK enforcement, version
assignment or aggregate composition could look perfectly correct in the UI and the reports while
being a shape ordinary usage cannot produce. The evaluation would then be of something that does
not ship.

Two consequences worth having, both free:

- Sample-data population becomes a genuine, realistic integration exercise of `SaveRoots`, FK
  ordering and R-CRUD-2 atomicity, at a batch size nothing else in the slice plan reaches.
- It gives **slice 10**'s `json_each` batch benchmark (§1.7.1) a realistic workload instead of a
  synthetic one — thousands of transactions is exactly the size at which the C#-loop-versus-
  set-based-statement question stops being academic.

### 1.9.6 What the user actually sees — three layers, and only the first is normal

1. **The normal customer path meets no guard at all.** The evaluating customer's entry point is a
   *"try it with sample data"* affordance on the new-database / welcome flow, which creates a
   database with `TestDatabase: true` — the checkbox already exists in `NewSqliteDatabaseDialog`
   and `NewSqlServerDatabaseDialog` — and populates it in one step. The guard is satisfied by
   construction. This is what makes the owner's two clauses compatible in practice rather than
   only in principle: the customer gets the capability, and the database it runs against is
   test-marked because the flow that offered it made it so.
2. **The command is disabled, with a reason, on a non-test database.** File ▸ Add Sample Data's
   `CanExecute` returns false when `store.Identity.IsTestDatabase` is false, with a tooltip
   saying why. Greyed and explained, not an error.
3. **If the capability is reached anyway, it refuses** — `TestDatabaseRequiredException`, thrown
   before anything is generated or written, surfaced through the existing
   `IBusinessLayerUiCallback` port as plain language rather than a raw exception string (§7's
   UI/UX rule):

   > *"'My Real Money' is not marked as a test database. Adding sample data creates dozens of
   > fictitious accounts and thousands of fictitious transactions that are indistinguishable from
   > real entries once saved, so it is only allowed on a database marked as a test database.
   > Create a new database with 'Test database' checked to try MyMoney out with sample data."*

**Is layer 3 warranted given layer 2 exists?** The panel says yes, and on three specific grounds
rather than generic defense-in-depth:

- **§1.6a's concurrency model explicitly admits a non-UI actor.** The shipped product's stated
  goal is *a human and an AI agent working the same books at once.* A `CanExecute` gate does not
  exist for the agent. This one is decisive and it is specific to this product: the owner has
  already decided that something which is not the UI will be calling the business layer.
- **Slice 7's success criterion is that the business layer is callable with no UI present.** A
  capability that is only safe because of a UI gate is, by definition, not safely callable
  headlessly — which contradicts the slice's own goal.
- **New call sites are the realistic failure, not a stale flag.** Sample data already has two
  call sites in the current tree (`MainWindow.OnCommandAddSampleData` and
  `MenuExportSampleData_Click`) plus the `ScenarioTest` DGML model, and every future one would
  have to remember the gate. A guard *inside* the capability cannot be forgotten by a caller.

### 1.9.7 How this is enforced rather than documented

| Tier | Test | What it pins |
|---|---|---|
| 1 — Business | `SampleDataService_RefusesNonTestDatabase_AndWritesNothing` — a store double whose `Identity.IsTestDatabase` is `false`, wrapped in `RecordingStore` (§2.4): assert `TestDatabaseRequiredException` **and** that `RecordingStore` saw **zero** writes | The guard runs *before* generation and *before* any write — not after a partial one. The second assertion is the one that matters; the first alone would pass on a guard placed last |
| 1 — Business | `SampleDataService_PopulatesTestDatabase_ThroughTheOrdinaryWriteSurface` — over the T-1 in-memory-SQLite store, assert `RecordingStore` saw only `SaveRoots` calls and no provisioner or test-control call | §1.9.5's "no privilege" claim is a property of the code, not a paragraph |
| 1 — Business | `SampleDataFactory_IsDeterministicForASeed` — carried forward from the existing `SampleDataRegressionTests.cs`, reshaped to the factory's return-a-set signature | The existing regression protection survives the split, with no database involved |
| 0 — Architecture | `TestDatabaseFlag_IsReadOnlyByTheSharedGuard` — IL scan of the production assemblies for callers of `StoreIdentity.get_IsTestDatabase`; the allow-list is `TestDatabaseGuard` and the UI's `CanExecute` handler | **A second, bespoke check cannot appear.** This is the test that keeps the "same mechanism, not a parallel one" property true a year from now |
| 2 — Store contract | `StoreIdentity.IsTestDatabase` round-trips the registry entry's flag, per engine | The single input the whole guard depends on behaves identically on both engines |

`TestDatabaseFlag_IsReadOnlyByTheSharedGuard` is an **IL scan**, which is heavier than the
existing Tier-0 tests' `GetReferencedAssemblies()` checks (§2.5) — stated plainly rather than
glossed. The panel judges it worth the weight because the failure it prevents (a second
hand-rolled `if (entry.TestDatabase)` somewhere that drifts from the shared one) is precisely the
failure that makes "it's the same mechanism" stop being true, and nothing cheaper catches it. The
UI `CanExecute` allow-list entry is deliberate, not a loophole: layer 2 above *must* read the flag
to grey the command, and it is a read with no write behind it.

### 1.9.8 The honest crack, stated

**The guard is on the capability, not on the data.** `SampleDataFactory` is `public` and
unguarded, so a caller can build a `SampleDataSet` by hand and pass its roots to `SaveRoots`
itself, and nothing refuses. This is **deliberate and correct**: `SaveRoots` of ordinary roots is
exactly what happens when a user types transactions, and gating it would gate the product. What
the guard protects is the *named, one-call, bulk fictitious-population feature*, which is what the
owner's "only on a db marked as test" is actually about — a caller who reassembles it by hand has
left the feature and is doing ordinary writes.

Making `SampleDataFactory` `internal` would not close this either, and is rejected for a reason
already in the document: it would need `InternalsVisibleTo` to stay testable, and §6.2 names that
as the most likely crack in the whole design, with a Tier-0 test
(`ProductionAssemblies_DeclareNoInternalsVisibleTo`) specifically forbidding it in production
assemblies.

This is the same shape of admission as §1.6c's `SaveRoots(new[] { root })` crack — a one-line
unvalidated route to the same executor — and it is recorded here in the same spirit: bounded,
named, and covered by a test that pins the surface rather than papered over.

### 1.9.9 Where this lands in the slice plan

The **guard and `StoreIdentity`** land in **slice 5b**, which already exists for exactly this
mechanism; §5's table is updated to say so. The **`SampleDataService` rewrite itself does not land
in slices 1–10 at all**, and deliberately so: those slices stop at `Account`, while sample data
needs `Payee`, `Category`, `Security`, `Transaction` and owned `Split` roots. The owner's answer
resolved *where it lives and what guards it*, not *when it is built* — so no slice is added here
to imply a commitment that was not made. When it is scheduled, size it as the §1.9.4 rewrite, not
as a relocation.

---

## 2. Test-subsystem architecture (Test Engineer leading)

### 2.1 What's wrong with the current shape

Three things, all fixable:

1. **`MyMoney.TestSupport` is both a library and a test project.** It carries
   `Microsoft.NET.Test.Sdk` + `[TestFixture]` classes *and* is `ProjectReference`d by `UnitTests`
   and `UITests` for `MockDatabase`/`BasicsFixtureBuilder`. Anything that wants the test doubles
   drags the test runner with it, and the ~60 contract tests live in a library rather than a
   suite.
2. **`UnitTests.csproj` references `MyMoney.csproj`** — the WPF UI project. The existing
   test-scoping convention says business-layer tests don't touch the UI; the project graph says
   otherwise. (It's not gratuitous: `TransactionCollection`, the only concrete
   `FilteredObservableCollection<Transaction>`, lives in the UI project — a known gotcha. But
   it means "business test" is currently an honor system.)
3. **The one-way test-tier dependency holds today purely by nobody having broken it.** Nothing
   checks it.

### 2.2 Proposed assembly shape

```
MyMoney.TestKit.Contracts   IMoneyStoreTestControl and test-only DTOs. Library. Never shipped.
MyMoney.TestKit             THE "test business layer" DLL the owner asked for.
                            Fixture builders, scenario/seed DSL, store doubles, fault injection,
                            assertion helpers, the shared store-contract base classes.
                            Library only — NO test-runner packages, NO [TestFixture].
                            References: MyMoney.Business, MyMoney.TestKit.Contracts.
                            Referenced by: test projects only. Zero production references.

MyMoney.Data.*.TestTier     Per-engine IMoneyStoreTestControl implementations.

MyMoney.Tests.Architecture  Boundary/packaging enforcement. Milliseconds. Runs on every build.
MyMoney.Tests.Business      Business-layer TDD suite. Target: several hundred tests, < 60s.
                            May NOT reference the WPF UI project. (New rule; see 2.1 #2.)
MyMoney.Tests.Data          Store-contract suite, one fixture per engine.
UITests                     FlaUI. Unchanged in role; keep AppCrashGuard, BasicsTestSetup.
```

The owner's constraint — *test-tier DLL may call the user-tier DLL; the user-tier DLL has zero
reference to the test-tier DLL* — is satisfied by `MyMoney.TestKit → MyMoney.Business`, with no
arrow back.

### 2.3 Test types and where each runs in the cadence

| Tier | What it proves | Speed | When |
|---|---|---|---|
| **0 — Architecture** | No WPF in Business/Data; no production assembly references `*.TestKit*`/`*.TestTier*`; no production assembly declares `InternalsVisibleTo`; a Release publish contains no `MyMoney.Data.SqlServer*`; no type in `MyMoney.Data.Sqlite` implements `IMoneyStoreTestControl` | < 1 s | Every build |
| **1 — Business unit** | Computed results correct; correct calls made *to* the store; store-signalled errors handled correctly | target < 60 s for several hundred | The TDD loop — run the one new test repeatedly, whole tier at end of pass |
| **2 — Store contract** | Each engine implements `IMoneyStore`/`IMoneyQuery`/`IMoneyStoreProvisioner` identically | SQLite seconds; SQL Server minutes | End of pass (SQLite) / end of cycle (SQL Server) |
| **3 — UI wiring (FlaUI)** | The UI hands input to the business API and displays/handles what comes back | ~4 min | End of development cycle only |

This **keeps** the existing three-layer scoping convention from the Basics spec — UI tests don't
re-verify business logic, business tests don't re-verify persistence mechanics — with two
amendments: Tier 0 is new (the convention had no architecture band), and Tier 1 must lose its
UI-project reference.

### 2.4 The store doubles — and sub-decision T-1 (*resolved in revision 2*)

*(Round 1's position, kept for the reasoning; superseded in its conclusion by T-1 below.)*
**Keep `MockDatabase`'s proven behaviors**, reshaped as `MockStore : IMoneyStore, IMoneyQuery`:
the `DataContractSerializer` round-trip (so tests exercise a real round-trip rather than
object-identity coincidence), the committed-version bookkeeping, and its genuine
`ConcurrencyConflictException` on stale `RowVersion` — that last one is the single most valuable
thing in the current test support, because it makes error-path testing the default rather than an
afterthought. Add two things it doesn't have:

- **`RecordingStore`** — a decorator that records every operation (`SaveRoot(Account#3, v2)`,
  `Aggregate(...)`) so a business test can assert *what was asked of the data layer*, which is
  explicitly shape (b) of the business-layer testing responsibility and is currently done by
  inference.
- **`FaultInjectingStore`** — a decorator configured to throw on the Nth call, or to throw
  `ConcurrencyConflictException` for a named root, or to fail mid-`SaveRoots`. **Revision 6 adds
  one more job** (§1.6c, §1.6a): proving the business-layer retry loop lets an `ArgumentException`
  — the exception `SaveRoot`/`DeleteRoot` raise when a root is not in a state the method admits —
  straight through, instead of treating it as a conflict and retrying a call that can only fail
  identically forever. This is the
  concrete answer to the owner's *"test processes to drive the happy path **and the error path**
  of the data layer"*. **Revision 3: this is also the formal answer to the owner's separate,
  later requirement that "the test business layer should have one or more APIs that will have a
  means to test [conflict/retry] functionality"** (§1.6a) — `FaultInjectingStore.ThrowConflictFor
  (rootId)` (or equivalent) is the one, first-class, documented way a business-layer test
  deliberately provokes a `ConcurrencyConflictException` and asserts on the `AppServices` retry
  loop's behavior, superseding the old ad hoc pattern this formalizes (save a `MockDatabase` root
  once, then mutate its `RowVersion` to a stale value before saving again — a real mechanism, but
  an incidental side effect of test setup rather than a named capability). Because `MockStore` is
  deleted below, `FaultInjectingStore` wrapping the real in-memory SQLite store is not just *a*
  way to test this, it is *the* way.

**Sub-decision T-1:** should the default business-test double be a hand-written `MockStore` at all,
or should it be **`RecordingStore`/`FaultInjectingStore` wrapped around a real SQLite in-memory
store** (`Data Source=:memory:`)?

- *For the real-SQLite-decorated option:* zero divergence risk (the mock and the engine cannot
  disagree, because there is only one implementation), one fewer thing to maintain, and the
  aggregation query in §1 doesn't need a second LINQ implementation that can silently drift from
  the SQL one.
- *Against:* unknown speed at several-hundred-tests scale, and it couples every business test to
  the schema being current.

Round 1 left this as *"a measurement, not an opinion."* The owner has asked for the data-engine
role's actual judgment, noting their own instinct was in-memory SQLite.

#### T-1 — resolved: **in-memory SQLite, decorated. Do not build `MockStore`.**

The panel's recommendation, with the reasoning rather than just the verdict:

1. **The owner's instinct is right, and §1.7 makes it more right than it was this morning.**
   P-SQLITE's whole point is that behavior should live in the engine. A hand-written `MockStore`
   is, by construction, a C# re-implementation of engine behavior — the exact thing the new
   principle says to stop doing. Building a mock while adopting P-SQLITE would be the document
   contradicting itself in two adjacent sections.
2. **§6.4's divergence problem is otherwise unsolvable, not merely hard.** `IMoneyQuery` brings
   aggregation into the port. A mock needs a LINQ-over-memory implementation of every aggregate;
   the engines have SQL ones; and a subtly-wrong subtotal in the mock makes every business-layer
   report test assert against a fiction that looks green. Deleting the second implementation makes
   the bug class structurally impossible rather than test-detectable.
3. **The speed objection is weaker than it looks, for reasons specific to this codebase.**
   `SqliteDatabase` already holds one long-lived cached connection (§0.1) — which is exactly the
   lifetime model `:memory:` requires, since an in-memory database exists only as long as its
   connection. No new lifetime design is needed. An in-memory `Schema_ApplyTo` run is DDL against
   an empty page cache with no fsync and no journal; the realistic per-test cost is *sub-
   millisecond to low-single-digit milliseconds*, against a several-hundred-test budget of 60
   seconds. The plausible failure mode is not "SQLite is slow," it is "schema setup is repeated
   needlessly per test" — which is a fixture-design problem with a known fix (below), not an
   engine problem.
4. **"It couples every business test to the schema being current" is a feature in this phase.**
   During nuke-and-pave (§1.5 S-0), a business test failing because the schema step wasn't written
   is *correct behavior* — it is the same signal §1.5 S-4 says the design needs more of, arriving
   earlier and cheaper.

**How to build it so the speed objection stays hypothetical:**

- One `:memory:` store per test, over one connection, disposed at teardown. No file, no cleanup,
  no cross-test leakage, no `AppCrashGuard`-style residue.
- Apply the schema via **the same `MyMoney.Data.Sqlite.Provisioning` executor production uses**,
  not a test-only shortcut. If that turns out too slow per test, the fix is to build the schema
  once and use SQLite's **backup API / `VACUUM INTO`** to clone a prepared in-memory template per
  test (milliseconds, and it also exercises §1.8's backup plank) — *not* to introduce a parallel
  test-only schema path, which would reintroduce drift by the back door.
- **Known gotcha, from reading `Connect()`:** `PRAGMA journal_mode=WAL` is not applicable to an
  in-memory database (it reports `memory`). The pragma sequence must tolerate that rather than
  treating a non-`wal` result as a failure — a small, concrete thing that will otherwise surface as
  a confusing first-run error in slice 4.
- `RecordingStore` and `FaultInjectingStore` stay exactly as round 1 designed them. They are
  decorators over `IMoneyStore` and do not care what they wrap, which is the property that makes
  this switch cheap. Fault injection in particular gets *better*: a decorator can now inject a
  fault into a store whose non-faulted behavior is the real engine's.

**What slice 9 becomes.** Not an open decision — a **verification gate on a decision already
made**. Run several hundred real business tests against the in-memory store and record the wall
time. The kill criterion, stated in advance so it can't be rationalized afterwards: **if the
business-test tier exceeds 60 s and profiling shows the store (not the tests) is the cause, and
the template-clone optimization above doesn't fix it, only then revisit a hand-written mock** — and
if that happens, the mock must be generated from or contract-tested against the same
`StoreContractTests` suite the engines run, so §6.4's divergence problem is bounded rather than
reopened.

**What this changes elsewhere in the document:** `MockStore` is removed from slice 4's deliverables
(§5) and from §8's summary. `MockDatabase`'s three genuinely valuable behaviors are not lost — the
`DataContractSerializer` round-trip becomes unnecessary (a real store round-trips through SQL,
which is strictly more honest), and committed-version bookkeeping plus a real
`ConcurrencyConflictException` on stale `RowVersion` are things the SQLite store *already does for
real*. Deleting the mock loses nothing it was valued for.

### 2.5 How the one-way dependency is actually enforced — four layers

1. **No `ProjectReference`.** Necessary, insufficient, one merge away from untrue.
2. **Build-time failure.** A `Directory.Build.targets` in the production projects with a target
   that inspects resolved `@(ReferencePath)` and errors on any `*.TestKit*`/`*.TestTier*` match.
   This is the layer that beats deadline pressure, because it fails at `dotnet build`, not at a
   test run someone might skip.
3. **Architecture test** (Tier 0), following the existing, working `LayerBoundaryTests` pattern —
   `assembly.GetReferencedAssemblies()` over the compiled DLL, which is the check that already
   caught a false positive from MSBuild's verbose log and is therefore the one the project already
   trusts.
4. **The type isn't reachable.** `IMoneyStoreTestControl` lives in `MyMoney.TestKit.Contracts`;
   production doesn't reference it; production code cannot name it to implement or call it.

A fifth option — `Microsoft.CodeAnalysis.BannedApiAnalyzers` — is available but redundant given
(2) and (4); mention it only if a specific API (not a whole assembly) ever needs banning.

**New Tier-0 tests to write, concretely:**

```
ProductionAssemblies_DoNotReferenceTestKit          (each of Business, Data.Sqlite, MyMoney UI)
ProductionAssemblies_DeclareNoInternalsVisibleTo
SqliteStoreAssembly_ContainsNoTestControlImplementation
ReleasePublishOutput_ContainsNoSqlServerEngineAssembly
MyMoneyBusiness_HasNoWpfAssemblyReference           (carry forward, unchanged)
MyMoneyData_HasNoWpfAssemblyReference               (carry forward, per-engine now)
TestsBusiness_DoesNotReferenceTheWpfUiProject       (fixes 2.1 #2)

ProjectionTypes_DoNotImplementIAggregateRoot        (revision 4, §2.7.2)
StoreWriteMethods_AcceptOnlyAggregateRoots          (revision 4, §2.7.2 - now four members, §1.6c)

StoreWriteSurface_IsExactlyTheFourNamedMethods      (revision 6, §1.6c)

TestDatabaseFlag_IsReadOnlyByTheSharedGuard         (revision 8, §1.9.7)
```

The fourth one is the test that makes D-37 real rather than aspirational, and it is the one most
likely to be quietly deleted when it becomes inconvenient — so it should assert against an actual
`dotnet publish -c Release` output directory, not against a `.csproj`.

`ProjectionTypes_DoNotImplementIAggregateRoot` and `StoreWriteMethods_AcceptOnlyAggregateRoots` are
revision 4's; they are what turns *"the business layer knows views are read-only"* from a sentence
in a document into a property of the compiled assemblies. §2.7.2 gives their exact shape.

`TestDatabaseFlag_IsReadOnlyByTheSharedGuard` is revision 8's (§1.9.7). It is an **IL scan** —
heavier than the four `GetReferencedAssemblies()`-style checks above, and called out as such —
that asserts the only production callers of `StoreIdentity.get_IsTestDatabase` are
`TestDatabaseGuard` and the UI's `CanExecute` handler. It exists to stop a *second*, hand-rolled
`if (entry.TestDatabase)` check appearing somewhere and drifting from the shared one, which is the
single way §1.9's "it's the same mechanism as §1.8's, not a parallel one" claim stops being true.

`StoreWriteSurface_IsExactlyTheFourNamedMethods` is revision 6's, and it is the test that makes
§1.6c's split worth having a year from now: `IMoneyStore`'s public write members are exactly
`SaveRoot`, `DeleteRoot`, `SaveRoots` and `SaveTransfer`, with no unvalidated singular-root method
beside them. The benefit of naming the delete disappears the moment someone re-adds a convenience
`Save<T>` that does everything, and this is the thing that has to be deliberately amended for that
to happen. The *behavioural* half — that the preconditions genuinely throw — is Tier-2 and per
engine; §1.6c's Test Engineer note lists the six cases.

---

## 2.6 `IMoneyStoreTestControl`, expanded — three levels of reset *(new in revision 4)*

Revisions 1–3 gave this contract one granularity: *wipe the whole database, seed it, snapshot and
restore it.* The owner's objection is correct and the panel should have caught it — a test that
wants to know *"what does `AddAccountService` do when the `Payees` table is empty?"* or *"what
happens to this report when this one transaction disappears mid-run?"* has, today, exactly one
tool: destroy and rebuild everything. That is both slow and imprecise: it resets state the test
was depending on, so the test has to re-establish it, which means the reset is no longer isolating
anything.

### 2.6.1 The question that has to be answered first: is table-level a decomposition of the wipe?

**No, and getting this wrong would produce a confused API.** The whole-database wipe in
revisions 1–3 is `Schema_DropAll()` + `Schema_ApplyTo(N)` — a **schema** operation that destroys
data as a *side effect* of destroying and rebuilding the objects that hold it. A table clear is a
**data** operation that deliberately leaves every schema object exactly where it was.

They are not the same operation at two granularities. They are two ladders:

| Ladder | Rungs | What it touches | What it leaves alone |
|---|---|---|---|
| **Schema** | `ResetSchema(N)` | Every table, index, view, trigger, constraint, TVP/table type, proc — and `__SchemaHistory` itself | Nothing. This is nuke-and-pave (§1.5 S-1) |
| **Data** | `ClearAllData()` → `ClearTables(subset)` → `DeleteRow(table, id)` | Rows | Every schema object, including `__SchemaHistory`, indexes and views |

Only the **data** ladder decomposes, and it decomposes cleanly — the three rungs are literally one
executor with a narrowing target:

```
ClearAllData()          ==  ClearTables(<every data table, FK-ordered>)
ClearTables(ts)         ==  for each t in FK-order(ts): DELETE FROM t        [one transaction]
DeleteRow(t, id)        ==  DELETE FROM t WHERE Id = @id                    [one transaction]
```

The schema ladder does **not** decompose into the data ladder and must not be implemented in terms
of it. `ResetSchema` is not "clear all tables, then fix up the schema": if the schema has drifted —
which, during nuke-and-pave, is the normal reason you are resetting at all — clearing rows does
nothing about it. Conversely `ClearAllData` must never be implemented as `ResetSchema`, because
its entire value is that it does *not* re-run DDL.

**The practical rule for a test author**, which belongs in the TestKit's own docs:

- Schema might be stale or wrong → `ResetSchema(N)`.
- Schema is fine, I want a blank slate → `ClearAllData()`.
- I want a blank slate in one corner → `ClearTables(...)`.
- I want one row gone for the next twelve lines → `RemoveRow(...)` in a `using`.

### 2.6.2 The surface

```csharp
namespace MyMoney.TestKit.Contracts;   // never referenced by a production assembly (§2.5)

public interface IMoneyStoreTestControl : IDisposable
{
    // ---- Schema level: revisions 1-3's operation, unchanged, reusing §1.5 verbatim ----
    void ResetSchema(int targetVersion);          // Schema_DropAll() + Schema_ApplyTo(N)
    int  CurrentSchemaVersion { get; }            // Schema_CurrentVersion()

    // ---- Data level ----
    ClearResult ClearAllData(ClearOptions options = default);
    ClearResult ClearTables(IReadOnlyCollection<TableRef> tables, ClearOptions options = default);
    ClearResult ClearTable(TableRef table, ClearOptions options = default);
    ClearResult ClearTable<TRoot>(ClearOptions options = default) where TRoot : IAggregateRoot;

    // ---- Row level ----
    RowSnapshot    CaptureRow(TableRef table, long id);               // throws if absent
    RowSnapshot?   TryCaptureRow(TableRef table, long id);
    RowSetSnapshot CaptureRows(TableRef table, RowFilter filter);     // see 2.6.5 on RowFilter
    void           DeleteRow(TableRef table, long id);                // no version check, no cascade
    void           RestoreRow(RowSnapshot row);                       // verbatim: Id and Version too
    void           RestoreRows(RowSetSnapshot rows);
    IRowScope      RemoveRow(TableRef table, long id);                // capture + delete; Dispose restores
    IRowScope      RemoveRows(TableRef table, RowFilter filter);

    // ---- Whole-database snapshot: unchanged in role, tightened in 2.7.1 (Restore refuses a
    //      snapshot whose recorded schema version differs from the target's current one) ----
    StoreSnapshot Snapshot();
    void          Restore(StoreSnapshot snapshot);

    // ---- Introspection the above needs, and tests find useful in their own right ----
    IReadOnlyList<TableRef> Tables { get; }        // data tables only; never views (§2.7.1)
    long RowCount(TableRef table);
}

public readonly record struct ClearOptions(
    bool ResetIdentity = true,                     // see 2.6.4 - the cross-engine trap
    bool VerifyForeignKeysAfter = true);           // PRAGMA foreign_key_check / DBCC CHECKCONSTRAINTS

public sealed record ClearResult(IReadOnlyDictionary<TableRef, long> RowsDeleted);

public interface IRowScope : IDisposable { RowSetSnapshot Removed { get; } }
```

`Seed` is deliberately **absent**, and that is a change from the revision-1–3 sketch. Seeding is a
`MyMoney.TestKit` concern that writes **through `IMoneyStore`** — which means a seeded fixture has
exercised the real write path, the real version assignment and the real FK enforcement, exactly as
T-1 (§2.4) argues for everywhere else. What the test-control tier offers instead is the *fast*
path: `Snapshot()` a seeded database once, `Restore()` it per test. Splitting these two apart is
strictly better than the single `Seed` the earlier revisions implied, because it stops a
"convenience" seeder from quietly becoming a second, domain-rule-free way to create data.

> **Revision 8 makes this paragraph load-bearing outside the test tier.** §1.9.5 reaches the same
> conclusion for the shipped sample-data feature, and cites this argument to do it: sample-data
> generation writes through the ordinary `IMoneyStore.SaveRoots` surface rather than getting a
> privileged bulk path here, because a customer evaluating the product is evaluating *what the
> real write path produces*. This is also why `Snapshot`/`Restore` picked up a second job in
> revision 8 — §4.1 notes that it, not the now-deferred XML export (§4 item 5), is what preserves
> a hand-built scenario across a nuke-and-pave.

### 2.6.3 Why these cannot just be done through `IMoneyStore` — the justification for the tier

A reviewer's first reaction to `DeleteRow` should be *"`IMoneyStore` can already delete."* It can,
and that is a different operation. **Revision 6 makes this table load-bearing rather than
explanatory**, because §1.6c names the store's delete `DeleteRoot<TRoot>` — one character from
`DeleteRow`, and the two do dangerously different things. This is the place that difference is
documented; §1.6c's honest-costs list points here:

| Through `IMoneyStore` — `DeleteRoot<TRoot>(root)` | Through `IMoneyStoreTestControl` — `DeleteRow(table, id)` |
|---|---|
| Takes a **root object**, typed, generic-constrained to `IAggregateRoot` | Takes a **`TableRef` and a raw `long` id**. No object, no type relationship to the domain at all |
| Refuses a root not marked `Deleted` (`ArgumentException`, §1.6c) — the domain has to have agreed the thing is going | Refuses nothing. The row's change state is not consulted because there is no object to consult |
| Version-checked; throws `ConcurrencyConflictException` on a stale `RowVersion` (R-CRUD-4) | No version check at all. A test that has no idea what version a row is at can still remove it |
| Applies domain semantics — `SaveTransfer` touches both sides, deleting a transfer's other side is refused when reconciled | Applies none. One row, one table. If the result violates an FK, the engine says so and the operation rolls back |
| Runs `postCommitActions`, updates the in-memory graph, marks objects `OnUpdated()` | Runs below the object graph entirely |
| Is the thing under test | Is the fixture |

That last row is the whole argument. These operations exist precisely so that a test can put the
store into a state the domain rules would not let it reach, or would only let it reach through a
sequence of calls that is itself under test. Using the production API to build the fixture for a
test of the production API is how a test ends up asserting that a bug is consistent with itself.

**The consequence, which must be documented where a test author will hit it:** every test-control
operation is invisible to any live `MyMoney` object graph or cached store state. After any of
them, the caller re-reads. The TestKit fixture base class should make that the default rather than
a remembered step.

### 2.6.4 How this reuses §1.5's machinery — and the one place it must not hand-maintain a list

`ResetSchema` is `Schema_DropAll()` + `Schema_ApplyTo(N)`, unchanged. The interesting reuse is in
the *data* ladder, and it is the single most important implementation constraint in this section:

> **The clear executor derives its table list and its delete order by introspection, from the same
> mechanism `Schema_Verify()` uses (§1.5 S-4 / §1.7.1's `PRAGMA table_info`/`foreign_key_list`,
> `sys.tables`/`sys.foreign_keys`). It never carries a hand-written list of tables.**

This is issue #34's lesson applied somewhere it would otherwise recur verbatim. A hand-maintained
`ClearAll` list has exactly #34's failure shape: someone adds a table in step 23, forgets the
clear list, and every test from then on runs against a table that is never emptied — silently,
with no error, and with the symptom appearing somewhere else entirely. Deriving the list means a
new table is cleared the moment its step exists, for the same reason and by the same route that a
new index is created the moment its step exists.

Concretely, per engine:

| Concern | SQLite | SQL Server |
|---|---|---|
| Table set | `sqlite_master WHERE type='table'`, minus `sqlite_%` internal tables and minus `__SchemaHistory` | `sys.tables`, minus `__SchemaHistory` |
| Delete order | Reverse topological order over `PRAGMA foreign_key_list`; `PRAGMA defer_foreign_keys=ON` inside the transaction as the safety net for a cycle (the same pragma today's `SqliteDatabase.SaveBatch` already uses, §0.1) | Reverse topological order over `sys.foreign_keys`. **No** `NOCHECK CONSTRAINT` — a clear that disables constraint checking can leave a state the schema forbids |
| Statement | `DELETE FROM t` (no `WHERE`), which SQLite's truncate optimization turns into a page-drop | `DELETE FROM t`. **Not `TRUNCATE TABLE`** — it is refused on any table an FK references, so it cannot be applied uniformly, and parity beats a constant factor here |
| Where the logic lives | The `MyMoney.Data.Sqlite.TestTier` executor, in-process (§1.7.2's unclosable limit, again) | `Test/*` procs — `dbo.Test_ClearTables @Tables dbo.TableNameList READONLY` — deployed **only** into a `TestDatabase: true` catalog and granted only to `MyMoneyTest`, exactly as the existing `_Test_Reset` procs are (§0.1) |
| Transaction | One, around the whole call | One, inside the proc, `SET XACT_ABORT ON` |

**The cross-engine trap, and why `ClearOptions.ResetIdentity` defaults to `true`.** After
`DELETE FROM Accounts`, the next `Id` a fresh insert receives is **not** the same on the two
engines. SQLite's `INTEGER PRIMARY KEY` is the rowid and allocates `max(rowid)+1`, so an emptied
table starts again at 1 by itself — *unless* the table is declared `AUTOINCREMENT`, in which case
the high-water mark persists in `sqlite_sequence` and must be deleted explicitly. SQL Server's
`IDENTITY` never rewinds without `DBCC CHECKIDENT(..., RESEED, 0)`. A test that asserts
`account.Id == 1` after a clear therefore passes on one engine and fails on the other, for reasons
having nothing to do with what it is testing. This is precisely the class of silent divergence
§6.8 warns the new nativeness principle can create, arriving from the test tier instead. The
contract suite must pin it: **after `ClearTable` with `ResetIdentity: true`, the next inserted row
has the same Id on both engines as it would in a freshly paved database.** That assertion is
cheap, and it is the only thing that keeps the option honest.

**Where the `TestDatabase` guard goes.** Slice 5b already requires destructive provisioner
operations to refuse an entry not marked `TestDatabase: true` (§1.8). Granularity makes that
*more* important, not less, and moves where the check belongs: `ResetSchema` reads as dangerous
and gets respect; `ClearTable(TableRef.Payees)` reads as housekeeping. So the guard is **at the
point the `IMoneyStoreTestControl` is obtained** — the factory checks the registry entry once and
refuses to hand back an instance at all — rather than repeated in twenty methods where the
twenty-first will be forgotten. On SQL Server the same guarantee arrives twice over, since the
`Test/*` procs are not deployed into a non-test catalog and `MyMoneyTest` has no table grants.

### 2.6.5 Row-level: what "delete and restore 1 record" actually has to mean

Three decisions here have real consequences and none of them is obvious.

**1. `DeleteRow` does not cascade, and that is the point.** If another table's FK references the
row, the delete fails and rolls back, surfacing as `StoreTestControlException` wrapping the engine
error. The alternative — cascade, or defer FK enforcement for the call — is tempting and wrong,
because `RestoreRow` would then be a lie: it restores the one row it captured, while the rows the
cascade took are gone for good and the test's "restore" silently leaves the database in a state
neither before nor after. A test that genuinely wants a row and its dependents gone uses
`CaptureRows` + `RemoveRows` and says so. **Restore must be exactly the inverse of remove, or it
is not a restore.**

**2. `RestoreRow` re-inserts verbatim — same `Id`, same `RowVersion`, same every column.** It is
not "insert a new row with the same values." A restored row that came back with a fresh `Id` or a
bumped version would break every reference to it and would make the version-conflict tests
(§1.6a's whole subject) unreproducible. This means `RestoreRow` writes the identity column
explicitly — `SET IDENTITY_INSERT ... ON` on SQL Server, a plain explicit `Id` value on SQLite.

**3. The scoped form is the one tests should actually use.** The primitives exist, but the shape
that makes this feature pay for itself is:

```csharp
using (control.RemoveRow(TableRef.Of<Payee>(), payeeId))
{
    // The report runs against a database where this payee's row does not exist.
    var model = reportBuilder.Build(request);
    Assert.That(model.Rows, Has.None.Matches<ReportRow>(r => r.PayeeId == payeeId));
}
// Row is back, byte-for-byte, including its RowVersion. The rest of the fixture is untouched.
```

`Dispose` restores inside its own transaction and throws if the restore fails, rather than
swallowing — a silently-failed restore would leak state into every subsequent test in the fixture,
which is the exact failure mode `AppCrashGuard` was written to stop tolerating elsewhere in this
project (see CLAUDE.md's note on why that check is deliberately not wrapped in a try/catch).

`RowFilter` for the multi-row forms is deliberately **not** a SQL string. It is the same closed,
typed shape §1's `IMoneyQuery` uses for its filters — a small record of column/operator/value
triples — so the test tier does not become the one place in the design where string-built SQL is
acceptable (R-CRUD-1).

### 2.6.6 `TableRef`, and keeping it from drifting

`TableRef` is a `readonly record struct` wrapping a name, with no public constructor. Instances
come from `TableRef.Of<TRoot>()` (aggregate roots, compile-time safe) or from a static
`Tables.Accounts`-style class for the tables no root maps to (join tables, lookup tables).
Anything else — a typo, a renamed table, or a **view** (§2.7.1) — cannot be constructed.

The anti-drift guard is the same shape as slice 2b's schema-equality test, and belongs in the
Tier-2 contract suite: **assert that the set of `TableRef`s the TestKit exposes is exactly the set
of data tables the introspected schema contains at the current version.** Add a table without a
`TableRef`, or leave a `TableRef` behind after dropping a table, and that test goes red. This is
what keeps §2.6.4's derived clear list and the typed surface from disagreeing.

### 2.6.7 Honest note: granularity is not automatically faster

It is easy to assume the fine-grained operations exist for speed. Sometimes; not always, and the
answer differs by engine, which matters because the same tests run on both:

| Reset | SQLite (`:memory:`, T-1's default) | SQL Server |
|---|---|---|
| `ResetSchema(N)` | DDL against an empty page cache — sub-ms to low-ms (§2.4) | Seconds. Genuinely expensive |
| `ClearAllData()` | Comparable to the above, sometimes slower once FK-order introspection is counted | **Much** cheaper than reprovisioning. This is where the granular API earns its keep |
| `Restore(snapshot)` | Cheapest of all — SQLite's backup API / `VACUUM INTO` over a prepared template (§1.7.1, §2.4) | Expensive; `RESTORE DATABASE` needs exclusive access |
| `RemoveRow(...)` scope | Trivial | Trivial |

So the recommended default per-test reset stays **per-engine, not universal**: template
snapshot-restore on SQLite, `ClearAllData()` on SQL Server, `ResetSchema` only when the schema
itself is in question. The TestKit fixture base should choose this per engine so individual tests
never encode the choice — and slice 9's measurement gate (§2.4) should record all three numbers
rather than just the one it currently plans to.

### 2.6.8 Idempotency — the owner's explicit requirement, checked against the design *(new in revision 9)*

Resolving §4 item 8, the owner stated a requirement this section had not previously made explicit:
*"When the test routine calls a SP that clears the part of the db that it needs to resume testing,
when that SP completes, nuke and pave is over. The[se] routines should be idempotent."* That is a
concrete, checkable claim about the operations in this section, not a general aspiration — so it is
checked here per operation rather than declared once for all of them.

**The schema ladder — idempotent by construction.** `ResetSchema(N)` is unconditionally
`Schema_DropAll()` then `Schema_ApplyTo(N)` (§1.5 S-1). Neither step is conditioned on "was this
already reset" — `Schema_DropAll` drops every object the family owns whether or not anything is
there to drop, and `Schema_ApplyTo(N)` always starts from whatever `Schema_CurrentVersion()`
reports, which is 0 immediately after a drop. Calling `ResetSchema(N)` twice in a row produces the
identical schema both times; there is no state a second call could find that the first call didn't
already produce.

**The data ladder — idempotent by construction.** `ClearAllData()`, `ClearTables(subset)`, and
`ClearTable<TRoot>()` are all, per §2.6.4, `DELETE FROM t` with no `WHERE` clause, per table, in
FK-order, inside one transaction. A `DELETE` with no `WHERE` against a table that is already empty
deletes zero rows and succeeds on both engines — nothing in the design conditions success on the
table being non-empty first. Calling any of these operations any number of times in a row leaves
the same tables in the same empty state after every call. `ClearOptions.ResetIdentity: true`
(§2.6.4's cross-engine trap) does not break this: reseeding an identity/rowid counter that is
already at its reset value is itself a no-op reset to the same value, not an error.

**Row-level reads (`CaptureRow`, `TryCaptureRow`, `CaptureRows`, `RowCount`) are trivially
idempotent** — they do not mutate anything, so this property is not in question for them.

**`DeleteRow` — idempotent, but this document had not said so, and now does.** §2.6.2 and §2.6.5
state that `DeleteRow` does not cascade and rolls back on an FK violation, but neither place said
what happens when `id` does not exist. A plain `DELETE FROM t WHERE Id = @id` that matches no row
is a normal zero-rows-affected success on both engines, not an error — so `DeleteRow` is idempotent
as designed *provided* the implementation does not add an "assert exactly one row was affected"
check that this document never specified. **Revision 9 closes that gap explicitly: `DeleteRow(table,
id)` must succeed as a no-op when `id` is already absent.** This is the one place in this section
where revision 9 adds a constraint rather than merely documenting one that was already implied — a
test-reset call site can now call `DeleteRow` unconditionally, without first checking existence, and
stay idempotent.

**`RestoreRow`/`RestoreRows` — deliberately *not* idempotent, and that is correct, not a gap.**
§2.6.5 point 2 requires `RestoreRow` to reinsert **verbatim** — same `Id`, same `RowVersion`, every
column — specifically so a restored row doesn't break references or dodge the version-conflict
tests it exists to support. That means calling `RestoreRow` a second time with the same snapshot is
a primary-key violation *by design*: the row it would insert already exists, because the first call
put it there. This is not a violation of the owner's requirement, because `RestoreRow` is not one of
"the[se] routines" the owner is describing — it is not a routine that "clears the part of the db it
needs to resume testing." It is the single-shot inverse half of a capture-then-delete pair, meant to
run exactly once per `RemoveRow`, normally from `IRowScope.Dispose` (§2.6.5 point 3: *"Row is back,
byte-for-byte... Dispose restores inside its own transaction and throws if the restore fails, rather
than swallowing"* — a throw on a *second* Dispose, or on any double-restore, is the correct behavior
under that contract, not a bug to fix). A test author who wants a repeatable, always-safe-to-call
reset uses `ClearAllData`, `ClearTables`, `ClearTable`, or `ResetSchema` — all four idempotent as
shown above. Stating the distinction here stops a future reader from assuming every
`IMoneyStoreTestControl` member shares one idempotency contract; they do not, and the difference is
intentional, not an oversight.

**Net:** every operation that actually matches the owner's "SP that clears the part of the db it
needs to resume testing" description — `ResetSchema`, `ClearAllData`, `ClearTables`, `ClearTable` —
is idempotent as this design already had it, with one explicit tightening added here (`DeleteRow`'s
no-op-on-missing-row behavior). `RestoreRow`/`RestoreRows` are a narrower, single-shot kind of
operation, correctly excluded from that property. No implementation change is required by this
revision; §2.6.2's interface comments and the eventual contract-test suite (§2.6.6's anti-drift
pattern) should each gain one line pinning `DeleteRow`'s no-op-on-missing behavior so it is asserted,
not just stated in prose.

---

## 2.7 Views: reset behavior, and the read/write asymmetry *(new in revision 4)*

The owner: *"It will cover views when we get around to using them. SQLite will allow us to query
using views, but we cannot update using one. The business layer will have to comprehend that
difference."*

Three separable questions live in that sentence, and they have different answers.

### 2.7.1 What resetting does to views — worked out, not assumed

**A view is a stored *definition*, not stored *data*.** On SQLite it is a row in `sqlite_master`
with `type='view'` holding its SQL text; on SQL Server it is an object in `sys.views`. It holds no
rows of its own; it is re-evaluated at query time. That single fact determines almost all of the
behavior, but not quite all of it:

| Operation | Effect on views | Why |
|---|---|---|
| `ClearTable` / `ClearAllData` | **Nothing to do.** The view definition survives untouched; querying it afterwards returns zero rows because its underlying tables are empty | A view has no state to reset. It is "automatically reset" in the only sense that matters |
| `DeleteRow` / `RestoreRow` | Nothing to do; the view reflects the change immediately, in both directions | Same reason |
| `Snapshot` / `Restore` | Snapshot captures **data only**, tagged with `Schema_CurrentVersion()`. View definitions are schema and are neither captured nor restored | A snapshot is not a schema backup. **The tightening revision 4 adds here** (referenced from §2.6.2's surface): `Restore` **refuses** a snapshot whose recorded version differs from the target's current version, loudly, rather than restoring rows into a schema that may have reshaped under them |
| `ResetSchema` | **Views must be explicitly dropped and recreated**, and this is the one place views need real handling | Below |

`Schema_DropAll()` must drop views, **and drop them before the tables they depend on** — required
on SQL Server, harmless-but-done-anyway on SQLite for parity of the step list. Equally,
`Schema_Verify()`'s expected object set must **include** views, or a paved database missing a view
reports no drift and the first symptom is an `IMoneyQuery` call failing at runtime. Both of these
are one-line consequences, but both are the kind of one line that is omitted when nobody writes it
down.

Two further concrete findings, neither hand-wavable:

1. **Never `SELECT *` in a view — always an explicit column list.** SQLite stores the view's SQL
   text and re-resolves `*` when the query is prepared, so a `SELECT *` view silently *gains* a
   column when its underlying table does. SQL Server binds the column list at `CREATE VIEW` time
   and keeps serving the old shape until someone runs `sp_refreshview`. That is two different
   behaviors from one schema step — a textbook §6.8 divergence, arriving through a feature §1.7.1
   enthusiastically adopted. An explicit column list makes both engines do the same thing, and it
   makes a view change what it should be: **its own numbered step** that drops and recreates the
   view (`DROP VIEW IF EXISTS` + `CREATE VIEW` on SQLite, which has no `CREATE OR ALTER VIEW`;
   `CREATE OR ALTER VIEW` on SQL Server).
2. **A view step is cheap and always safe to re-run, which makes it the most tempting place to
   break S-3.1** (never edit an applied step). There is no data to migrate and no rebuild to do,
   so editing step 14's view definition and re-paving *feels* free — and during nuke-and-pave it
   is, right up until it is not. `ChecksumHash` (§1.5 S-2) catches it, which is one more reason
   that check has to be on from slice 2 rather than "when it matters" (§6.7).

There is one non-obvious exception to "a view holds no data": a **SQL Server indexed view** is
materialized and does hold rows. The engine maintains it automatically as part of the DML, so a
`DELETE FROM` still leaves it correct with no special handling — but it makes the clear slower and
it has no SQLite counterpart, so it falls under §6.8 rule 1: **not adopted, and if ever proposed,
it must be matched or declined in writing.** Listed so the "views hold no data" claim above is
true as stated rather than true-with-an-unmentioned-asterisk.

### 2.7.2 "The business layer will have to comprehend that difference" — in the type system

The weak version of this is a documented convention. The strong version, which is what the panel
recommends and what the rest of this design's approach to enforcement demands, is that **the
business layer cannot express a write against view-backed data, because no type it can reach
permits it.** Four mechanisms, in increasing order of how load-bearing they are:

**1. The read port and the write port are different interfaces, and use cases take only what they
need.** This already exists (§1) and is the foundation: `IMoneyQuery` has no write methods at all.
A use case that reports takes `IMoneyQuery` in its constructor and nothing else — it cannot write
anything, through a view or otherwise, because it is holding no object that can.

**2. Query results are projections, and projections are not entities.** Every row type
`IMoneyQuery` returns (`AggregateRow`, `TransactionRow`, and every view-backed type that follows)
is a `sealed record` with `init`-only members implementing a marker interface:

```csharp
public interface IProjection { }        // MyMoney.Business - read-model marker, no members

public sealed record TransactionRow(...) : IProjection;
public sealed record AggregateRow(...)   : IProjection;
```

and critically, **no projection implements `IAggregateRoot`.** Since every write on `IMoneyStore`
is generically constrained to roots —

```csharp
void SaveRoot<TRoot>(TRoot root)   where TRoot : PersistentObject, IAggregateRoot;
void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot;  // rev 6, §1.6c
void SaveRoots(IReadOnlyList<IAggregateRoot> roots);
```

*(The `PersistentObject` half of the constraint is §1.6c's — it is what lets the base class read the
root's `ChangeType` for its precondition; `PersistentObject` and `IAggregateRoot` both live in
`MyMoney.Business`, so it costs no layering. §1.6c is the authoritative signature; the argument
below turns on the `IAggregateRoot` half.)*

— `store.SaveRoot(transactionRow)` does not compile, and neither does
`store.DeleteRoot(transactionRow)`. Not "is discouraged", not "throws at runtime": there is no
overload either can bind to. A business-layer developer who tries to write back something they
queried discovers it at the moment they type it, which is the only feedback loop that reliably
works. Revision 6's split does not weaken this in any way — it adds a fourth member carrying the
*same* constraint, which is exactly what `StoreWriteMethods_AcceptOnlyAggregateRoots` (§2.5) exists
to keep true as the family grows.

**3. Projections carry no `RowVersion`.** This is the subtle one and it matters more than it
looks. If a projection carried a version, a determined developer could construct a root from a
projection's fields and save it — and the version they carried is whatever the query saw, which
may be stale by the time they write. That is a concurrency bug (§1.6a) smuggled in through a
report. So the round trip is deliberately made to cost something: to write, you re-read the root
through `IMoneyStore`, which hands you the authoritative version. **The round trip is possible;
it just has to be explicit, and the explicitness is the feature.**

**4. The business layer never names a view at all.** This is the real answer to the owner's
sentence, and it is stronger than "views are read-only." A view is an implementation detail of an
`IMoneyQuery` implementation: the SQLite store may answer `Aggregate(q)` by selecting from
`v_MonthlyCategoryTotals`, the SQL Server store by executing `dbo.Aggregate_Query` — and the
business layer, which issued a typed `AggregateQuery`, knows about neither. It has no `ViewRef`
type, no view name, no connection, and no statement text. **There is nothing for it to
"comprehend" at runtime, because the asymmetry has been resolved at the layer boundary rather
than passed across it.**

Where view names *do* exist as first-class values is the provisioner and test tiers, and they are
deliberately a **different type** from `TableRef` (§2.6.6): `ViewRef` appears in `Schema_Verify`'s
drift report and nowhere else. `IMoneyStoreTestControl.ClearTables` takes `TableRef`, so "clear a
view" is not a runtime error message — it is a type error, which is the same trick applied at the
other end of the stack.

**The two Tier-0 tests this needs** (added to §2.5's list):

```
ProjectionTypes_DoNotImplementIAggregateRoot
    every IProjection implementer, by reflection over MyMoney.Business, is not an IAggregateRoot,
    and exposes no settable member and no RowVersion

StoreWriteMethods_AcceptOnlyAggregateRoots
    every write member of IMoneyStore has every parameter constrained to (or typed as)
    IAggregateRoot or a collection thereof - so a future method cannot quietly open a hole
```

The second is the one that earns its place over time: the property is easy to hold today when
`IMoneyStore` has a small, closed write surface, and easy to lose on the day someone adds a method
that takes a DTO "just for this one import path." **Revision 6 is the first live test of that
test** — it adds a fourth write method (`DeleteRoot`), and the right outcome was that the new
member had to satisfy the same admission rule rather than the rule bending to accommodate it. It
did. Note the division of labour with revision 6's own Tier-0 test: this one polices what the write
methods *accept*; `StoreWriteSurface_IsExactlyTheFourNamedMethods` polices *how many there are and
what they are called*.

### 2.7.3 The `INSTEAD OF` triggers tension, stated and resolved

**The tension, plainly.** The owner says SQLite cannot update through a view. Strictly as an
engine fact, that is *incomplete*: a bare view is not updatable, but an `INSTEAD OF` trigger on a
view makes `INSERT`/`UPDATE`/`DELETE` against that view legal, with the trigger body doing the
real work against the underlying tables. §1.7.1 of this document — written before the owner's
guidance — not only knew that, it **recommended it**, as *"the closest thing SQLite has to a
stored procedure for writes,"* verdict *"adopt selectively."* So this is not the owner mis-stating
an engine capability that the design can quietly route around; it is the owner stating an
architectural rule that contradicts a recommendation the document already made. One of them has
to move.

**The resolution: the rule wins, and §1.7.1 moves.** Not because the owner said so — the panel's
job here is to say whether it is right — but because three independent arguments point the same
way, and two of them are stronger than the owner's own:

1. **A writable view makes P-VIEW a convention instead of a property, at exactly the moment
   §2.7.2 is making it a property.** SQLite has no grants (§1.7.2), so there is no way to make a
   view read-only *in the engine*. The read-only-ness of views is therefore held entirely in .NET
   types. Introducing one view that accepts writes means the invariant is "views are read-only
   except the ones that aren't," which cannot be expressed in the type system and therefore has to
   be remembered — the precise failure mode §2.7.2 exists to eliminate.
2. **It collides with an adoption §1.7.1 made in the same table.** `RETURNING` was adopted so the
   post-write `RowVersion` is read authoritatively from the engine rather than inferred in C#.
   Whether `RETURNING` behaves usefully on a write routed through an `INSTEAD OF` trigger is
   exactly one of the unknowns §7's item 4 already admits the panel does not have operational
   experience of. Adopting both means shipping a write path whose version reporting is unverified,
   which is the single most contract-tested behavior in the data layer (§1.7.3).
3. **§6.8 rule 1 is satisfiable but the convergence is weaker than it reads.** An `INSTEAD OF`
   trigger *does* have a SQL Server counterpart (a proc), so on the face of it adopting it
   converges. But the counterpart is only structural: on SQL Server the multi-statement logic runs
   under a login with no table grants, invoked through an object the user tier is explicitly
   granted `EXECUTE` on; on SQLite it runs in-process under a connection that could have issued
   the underlying statements directly anyway. The *shape* converges; the *guarantee* does not —
   which is §6.1's already-written caution arriving in a new place.

Against all that, what `INSTEAD OF` buys is multi-statement write logic living in the database.
`SaveTransfer` and `SaveRoots` already put that logic in one place, inside one transaction, with a
hard-won post-commit deferral (R-CRUD-2) that a trigger would have to be re-proven not to break.
That is not enough to pay for points 1 and 2.

**So, concretely:**

- §1.7.1's `INSTEAD OF` row is rewritten from **"adopt selectively"** to **"do not adopt now"**,
  retained as a deferred single-candidate spike (the transfer both-sides write, per §7 item 4's
  own recommendation), with a standing constraint attached: **if it is ever adopted, it is an
  implementation detail strictly below `IMoneyStore`, never a business-layer-visible write
  surface.**
- §1.7.2's "no stored procedures" row no longer offers `INSTEAD OF` as the write-side escape
  hatch. The seam between the engines is correspondingly *wider* than revision 2 claimed, and
  saying so is the point (§1.5 S-5's closing paragraph applies unchanged).
- **The decision gets an enforcement, not just a paragraph.** A Tier-2 contract test per engine:
  *the schema owns no `INSTEAD OF` trigger* — `sqlite_master WHERE type='trigger'` with no
  `INSTEAD OF` in the SQL, `sys.triggers` with `is_instead_of_trigger = 0` throughout. If the
  spike is ever taken, that test is the thing someone has to deliberately amend, which is exactly
  the conversation that should happen at that moment.

**What the owner should confirm** (one question, and the panel is not stalling on it — the
recommendation above stands either way until told otherwise):

> Is the rule **(a)** *"nothing in this codebase ever writes through a view, full stop"*, or
> **(b)** *"the business layer never writes through a view; the data layer may use trigger
> machinery internally if a future spike proves it out"*?
>
> **The panel recommends (b) as the stated rule and (a) as the current state** — they are
> indistinguishable today, because no trigger-backed write path exists or is planned, and they
> only diverge if someone later proposes the transfer-both-sides spike. Stating it as (b) keeps
> that door open without costing anything now; stating it as (a) closes it permanently, which is
> also a defensible call but a more expensive one to reverse. Either way, §2.7.2's business-layer
> guarantees are unchanged, which is why this is a confirmation rather than a blocker.

---

## 3. The open question, resolved: where common control / import-export / reporting-output logic lives

One rule drives all three answers:

> **The data layer owns only what is engine-specific. The business layer owns everything
> computable. The UI layer owns only pixels and gestures.**

### 3.1 Common control logic → business layer, in an explicit application-services band

`MainWindow.xaml.cs`'s 5,209 lines of orchestration (open/close/switch database, upgrade prompt
flow, dirty-state handling, recent files, command routing) decompose into use-case classes in
`MyMoney.Business` — call the band `AppServices`, sitting alongside the existing
`DatabaseLifecycle`, which is already exactly this shape and is the working precedent.

They talk to the UI only through the existing callback ports (`IBusinessLayerUiCallback`,
`IDataLayerUiCallback`). **Explicitly not the data layer:** a store must not know what a "recent
file", a "prompt", or a "pending changes flyout" is. A store that knows about prompts is a store
that can't be tested without a prompt.

*(Whether `MainWindow`'s decomposition is in scope for this effort or a separate one remains
issue #5's own open question — §4 keeps it there.)*

### 3.2 Import/export → split three ways, by what each part actually is

- **Foreign-format parsing and writing** (QIF, OFX/SGML, CSV, XML) → **business layer**
  (`MyMoney.Business.Formats`). Pure `bytes → domain` / `domain → bytes`. Most of this is already
  in `MyMoney.Business` after the subsystem migration; the OFX *download* half keeps its
  `IOnlineService`-style network seam and stays testable by mocking that seam.
- **Whole-file formats that are not stores** (XML export, CSV export, and — per D-35 — anything
  that can't meet the store capability floor) → also business layer, behind
  `IDataFormat { void Write(MyMoney, Stream); MyMoney Read(Stream); }`. **They must not live
  behind `IMoneyStore` and must not live in an engine assembly.** *(Revision 12 flag: that
  signature is written in terms of a whole `MyMoney` object graph, which **revision 7 removed** —
  `Load()` does not survive. The **placement** decision in this bullet is unaffected and stands;
  the parameter type is not settled and must be re-derived whenever item #5's deferral ends. It is
  not re-derived here because nothing is being built against it — see §4 item 5.)* This is what finally kills
  `NotImplementedException`-as-a-capability-signal: you cannot pass a format where a store is
  required, because they are different types in different assemblies. *(Revision 8: the owner has
  **deferred** `IDataFormat` and the XML implementation out of the first slices — §4 item 5. This
  bullet's placement decision is unaffected; only the schedule is. §4.1 records what the deferral
  leaves unserved.)*
- **Engine-native duplication/backup/restore** (SQLite `VACUUM INTO` — revision 2's §1.7.1
  supersedes the close-and-`File.Copy` this bullet originally named, because it is transactionally
  consistent and needs no close; SQL Server `BACKUP`/`RESTORE`) → **data layer, provisioner tier**,
  because it is engine-specific by definition. Already decided by R1; this just names the tier it lands in. The Operations
  Engineer's standing insistence that *backup is part of the capability floor and is currently its
  weakest plank* attaches here: `IMoneyStoreProvisioner.Backup` must be able to overwrite, and must
  be contract-tested.

### 3.3 Reporting-output logic → three stages, consistent with D-17 and issue #5

This is the part issue #5 identified as needing genuine redesign rather than relocation, and
today's D-17 decision (Markdown intermediate → Microsoft Print to PDF) constrains the answer. The
three stages land in three different places, and that is the whole point:

**Stage 1 — data layer (`IMoneyQuery`).** Filtering and subtotaling happen in SQL. Today
`CashFlowReport.Generate()` calls `GetAllTransactionsByTaxDate()` — it loads *everything* and
filters in memory. The query port from §1 is what replaces that, and it is the only part of
reporting that is engine-specific.

**Stage 2 — business layer (`ReportModel`).** A report becomes
`IReportBuilder.Build(ReportRequest) → ReportModel`, where `ReportModel` is a plain, serializable,
format-agnostic tree: sections, tables, rows, and typed cells carrying a *value* plus *formatting
intent* (currency, percent, negative, emphasis, group-header, expandable-detail) — **never a
`Brush`, a `UIElement`, or a `MouseButtonEventHandler`.** This is issue #5's "format-agnostic data
stream", made concrete. `IReportWriter` and `IReport.Generate(IReportWriter)` are **deleted, not
ported** — every one of `IReportWriter`'s WPF-typed members is the reason `CsvReportWriter` has to
implement methods about brushes.

**Stage 3 — renderers (`ReportModel` → text).** `ReportModel → Markdown` (D-17's intermediate),
`ReportModel → CSV` (D-18's guarantee). Both are pure functions over a plain data structure, so
both are trivially unit-testable with string assertions — which is exactly the resolution D-17's
owner decision offered to the Test Engineer's recorded dissent, and it only works if Stage 2 is
genuinely separate.

**Where Stage 3 lives — a deliberate anti-recommendation.** The tempting answer is a new
`MyMoney.Reporting.Render` assembly. The Developer's objection holds: don't create an assembly
before a second consumer exists. **Put Stage 3 in `MyMoney.Business.Reporting`** (namespace and
folder, same assembly), with the Tier-0 WPF-free boundary test covering it. Split it out only if
and when a non-business consumer appears (a CLI report generator, say). The architectural
property that matters — *the renderer has no UI dependency and is testable by string comparison* —
is provided by the boundary test, not by the assembly count.

**What stays in the UI project:** a Markdown viewer control, and the `PrintDialog` call against
the "Microsoft Print to PDF" driver. That is all. Charts stay in the UI as visual controls;
`ChartData`/`CategoryData`/`RentalData`'s data-shaping moves to Stage 2 alongside the report
builders, since it is the same `query → model` shape.

---

## 4. What genuinely needs the owner's decision

These are not things the panel is dodging — each is a product or appetite question that a
technical panel cannot legitimately answer.

1. ~~**Does the rebuild happen in place, or in a parallel tree?**~~ **Resolved — in place.** The
   owner's decision, verbatim: **"In place."** The rebuild replaces `MyMoney.Data` and friends
   incrementally, in the existing `Source/WPF/` tree, with the app building and running throughout
   — not a parallel `Source/Rebuild/` tree with the current app frozen until the new stack catches
   up. This was decided early in the review process, before several of the later revisions; it is
   recorded here belatedly (revision 10), not newly decided. Revision 2's note on this item is the
   relevant context for why the call was easy: nuke-and-pave had already removed the strongest
   argument for a parallel tree (*"the existing database format must keep working while the new one
   is built"*), materially de-risking the in-place option without itself deciding it. Everything in
   §7 assumed *in place, incrementally*; that assumption is now the settled answer.

2. ~~**Does the shipped SQLite path still need D-34's file lease?**~~ **Resolved in revision 3 —
   no.** D-37's downstream effects had said *"since SQL Server does not ship to end users, C (a
   file lease) is what the shipped SQLite path needs"* — but the owner has since decided, in the
   `panel-review-05-data-engine-security.md` scenario-capture document, that the shipped product's
   actual concurrency goal is a human and an AI agent working the same books at once, which a
   lease actively defeats. **The shipped SQLite path keeps version-checked optimistic concurrency
   permanently; no lease is built.** Retry on a detected conflict lives in the business layer, not
   the data layer. See §1.6a for the resolution and its consequences for this design (in short:
   none to the schema/store shape, because R-CRUD-4 already assumed version-checked writes — the
   consequence is entirely in the new business-layer retry loop and its test-tier support).

3. ~~**Must SQL Server stay at feature parity throughout the rebuild?**~~ **Resolved — same
   principle as everything else, extended explicitly to the query layer.** The owner's general
   parity answer, given earlier in this conversation (before this was formally item #3), verbatim:
   **"We can prototype functionality on SQLite, and use that to develop the scrum features. But it
   would be best to upgrade the SQL Server library in the same feature checkin. If it lags a bit,
   we can live with that, but we need to keep them as similar as we can, otherwise we cannot test
   reliably."** Asked directly whether that principle extends to the query layer specifically —
   given revision 7 made `IMoneyQuery` the whole application's read path, not just reports' — the
   owner confirmed, verbatim: **"Same principle for the query layer too."**

   This closes the loop revision 7 flagged (§4.1's "Net after revision 7" note, below): the panel's
   original recommendation — *lag `IMoneyQuery` and other read-side capabilities on SQL Server while
   schema management does not, on the premise that the query surface mainly served reports* — is
   **not adopted as written**, because the premise it rested on (query-surface-as-reports-only) no
   longer holds. The owner's answer is uniform rather than surface-specific: default practice is to
   implement and contract-test a new store capability — schema management, CRUD, and now the query
   surface alike — **on both engines in the same feature check-in**; prototyping on SQLite first
   when useful is fine; occasional lag is tolerable but not planned for; and the two engines are
   kept as close together as practical throughout, specifically because divergence undermines the
   shared `DatabaseContractTests`-style suite's reliability. No special carve-out survives for
   `IMoneyQuery` just because it was originally motivated by reports.

4. ~~**Does whole-graph `Load()` survive?**~~ **Resolved in revision 7 — no.** The `MyMoney`
   object graph with its `PersistentObject` change tracking assumed "load everything at startup,
   hold it in memory." The `IMoneyQuery` port made incremental, query-backed loading *possible*;
   this was the biggest architectural fork in the entire rebuild — it decided whether `Money.cs`'s
   object graph survives as the domain model or becomes a view over queries. The owner's decision,
   verbatim: **"The query-based pattern replaces the whole-graph-in-memory pattern everywhere."**
   `Load()` does not survive. Every part of the application — not only reports, which is what
   originally motivated `IMoneyQuery` — goes through targeted queries against the data layer
   instead of walking an already-loaded object tree. The panel deliberately designed §1 to work
   **either way by construction**, so nothing in this design's shape changes as a result; this is a
   recording of the decision, not new design work. See §4.1 for what this leaves stale elsewhere.

5. ~~**Is `XmlStore` kept as an export format from day one?**~~ **Resolved in revision 8 —
   deferred.** The owner's decision, verbatim: **"deferred."** D-35's yes-as-a-format position is
   untouched — XML remains the disaster-recovery, human-readable, "only way off the product"
   format, and §3.2's placement decision (behind `IDataFormat`, in the business layer, **not**
   behind `IMoneyStore` and **not** in an engine assembly) stands unchanged. What is decided is
   *timing*: **`IDataFormat` and an XML implementation are not in the first implementation
   slices.** This was always a scope call rather than a principle, and the owner has made it. No
   design work follows; see §4.1's revision-8 note for the one argument this leaves unserved and
   which already-designed capability covers it instead.

6. ~~**Credential storage.**~~ **Resolved in revision 8 — later.** The owner's decision, verbatim:
   **"later."** The three SQL Server logins' generated passwords stay in plaintext in
   `dataengine.config.json` for now; moving them to Windows Credential Manager / DPAPI is **not**
   done in the near-term slices. The panel's recommendation was "now" (the bootstrap is being
   rewritten anyway) and the owner has decided otherwise — which is what this item existed to ask.
   The defect itself does not stop being a defect: it remains a real, live exposure on the owner's
   own machine, deferred rather than dismissed, and it should be a tracked item rather than a
   line in this document. No design work follows.

7. ~~**Is "sample data" a product feature or a test capability?**~~ **Resolved in revision 8 — and
   the answer is neither of the two options the panel offered.** The owner's decision, verbatim:
   **"test only. but in this case, they are letting the customer test the capability of the
   software to see what the UI and the reports look like. I guess in that sense, it could be
   considered a feature, but I would argue, that it should only be done on a db marked as test."**

   Unpacked: sample-data generation is a **shippable, customer-reachable capability** (an
   evaluating buyer runs it to see what the UI and the reports look like — that is a feature, not a
   developer convenience), **and** it must only ever run against a database marked
   `DatabaseEntry.TestDatabase`, whoever invokes it and from wherever. The panel's question was a
   false binary; the owner's answer draws the line at the *database*, not the *audience*.

   This one **did** need design work rather than recording, because it fits neither of §1's two
   protection mechanisms as they stood: assembly absence would make the feature unreachable by the
   customer it exists for, and §1.8's runtime-flag refusal was scoped to *destructive* operations,
   which sample data is not. **See new §1.9** for the resolution: `SampleDataGenerator` stays in
   `MyMoney.Business` and ships, split into a pure unguarded `SampleDataFactory` and a guarded
   `SampleDataService` whose first statement is `TestDatabaseGuard.Require(store.Identity, …)` —
   **§1.8's existing mechanism, not a new one**, with its scope widened from "destructive
   operations" to "operations that must only ever touch a test database." The write path is the
   ordinary `SaveRoots` surface with no privilege at all. The panel's standing recommendation
   (*"keep it a product feature in `MyMoney.Business`, and let the test tier seed through
   `MyMoney.TestKit` separately"*) is **confirmed on placement and amended on protection**: it
   stays in `MyMoney.Business`, and it gains a guard the recommendation did not have.

8. ~~**(New, revision 2.) When does the nuke-and-pave phase end, and what is the trigger?**~~
   **Resolved in revision 9.** §1.5 S-0 accepted nuke-and-pave *for this phase*, and §1.8 kept the
   exit affordable — but "eventually" was not a criterion, so the panel asked the owner for the
   **trigger**, not the date — e.g. *"the first database I import real Quicken data into and intend
   to keep."* The owner's answer dissolves the question rather than answering it on the panel's own
   terms:

   > "If a db is a test db, it is always a test db. If it is not, it never is a test db. You get to
   > set that attribute at create time, and it remains as long as the db remains. When the test
   > routine calls a SP that clears the part of the db that it needs to resume testing, when that
   > SP completes, nuke and pave is over. The[se] routines should be idempotent."

   There is no single project-wide moment for the panel to name, because there was never really a
   project-wide phase to end — only a per-database, permanent attribute
   (`DatabaseEntry.TestDatabase`, already in the registry config, §1.5 S-0; the same flag revision
   8's §1.9 reads as `StoreIdentity.IsTestDatabase` to guard sample data) fixed once at that
   database's creation and never changed afterward. A database created as test can always be nuked
   and paved; a database created as production never could be, from the instant it existed. Every
   database — test or production alike — goes through the *same* versioned `Schema_ApplyTo`
   machinery from its first creation (§1.5 S-1), so there is nothing to retrofit when a production
   database eventually appears: it is a database whose `TestDatabase` flag was false from the start,
   not an existing test database that "graduated." "Nuke and pave is over" simply describes what
   happens every time a test-reset routine (§2.6's `ClearAllData`/`ClearTables`/`ClearTable`/
   `ResetSchema`, already designed in revision 4) *finishes running* — routine and repeated during
   testing, not a one-time milestone — which is why the owner's second sentence ("these routines
   should be idempotent") is load-bearing rather than a side note: idempotency is exactly what makes
   "nuke and pave is over" true after *every* call to a reset routine, not just the first. §2.6.8
   verifies that property against the design rather than assuming it, and adds one explicit
   tightening (`DeleteRow`'s no-op-on-missing-row behavior) where the design had left it implicit.

### 4.1 Re-scoring the round-1 list against today's guidance

Today's guidance touches four of the seven. Stated explicitly, per the revision's brief:

| # | Status after revision 2 | Status after revision 3 |
|---|---|---|
| 1 — in place vs. parallel tree | **Reframed, and materially de-risked.** The strongest argument for a parallel tree was always *"the existing database format must keep working while the new one is built."* S-0 removes it: there is no data to preserve and no user to keep shippable for. That does not *decide* the question — build-breakage and reviewability arguments survive untouched — but it takes the scariest constraint off the table, and it strengthens §7's assumption of *in place, incrementally*. Still the owner's call. *(Resolved by the owner, revision 10 — see below: in place.)* | Unchanged. |
| 2 — SQLite file lease | Unchanged. Nothing today bears on it. | **Resolved — no lease, ever, on the shipped path.** See §4 item 2 and §1.6a. The owner's simultaneous human+AI-agent goal rules a lease out; version-checked concurrency with business-layer retry is the permanent model. |
| 3 — SQL Server feature parity throughout | **Sharpened, and partly answered by implication.** "The process should be more or less the same on SQLite and SQL Server" is a parity requirement, but specifically about *schema management*, and it is a stronger claim than round 1's framing: §1.5's step list, ledger and verify semantics must be parity **from slice 2**, not caught up at a milestone, because a ledger that only one engine has is not a ledger. The question the owner still owns is narrower than round 1 posed it: **may `IMoneyQuery` and other read-side capabilities lag on SQL Server while schema management does not?** The panel's recommendation is yes — lag the query surface, never the schema surface. *(Resolved by the owner, revision 11 — see §4 item 3: no — the same-check-in principle applies uniformly, no query-surface carve-out.)* | Unchanged. |
| 4 — does whole-graph `Load()` survive | Unchanged, and worth saying why, since it's easy to assume otherwise: nuke-and-pave makes *schema* change cheap; it says nothing about whether the in-memory `MyMoney` object graph remains the domain model. Still the biggest fork in the rebuild, still undecided, and §1 still works either way. | Unchanged. |
| 5 — `XmlStore` as a day-one export format | **Arguably resolved, in the direction of "yes, sooner."** Under nuke-and-pave, an XML export is the only thing that lets a developer keep a hand-built scenario across a re-pave. That is a genuine new argument for it being early rather than deferred — but it is a scope call, so it stays on the list with a recommendation attached rather than being ticked off unilaterally. | Unchanged. |
| 6 — credential storage | Unchanged. Panel still recommends now. | Unchanged. |
| 7 — sample data: product or test? | **Reframed, and now more urgent.** Under nuke-and-pave, "re-populate a freshly paved database with something to look at" is a routine developer action, which makes `SampleDataGenerator` load-bearing for daily work rather than a nice-to-have feature. The panel's recommendation firms up: **keep it a product feature in `MyMoney.Business`**, and let the test tier seed through `MyMoney.TestKit` separately, rather than merging the two — but this needs the owner's confirmation more than it did this morning, not less. | Unchanged. |

Net after revision 2: **one resolved by the panel** (T-1, §2.4 — it was a sub-decision, not on this
list, but it was the largest genuinely-open technical question in round 1), **two reframed** (#1,
#3), **two strengthened with recommendations** (#5, #7), **one added** (#8), and **two untouched**
(#2, #4).

**Net after revision 3:** item **#2 moves from untouched to resolved by the owner** (not the
panel — this one was explicitly the owner's to decide, and they decided it). #4 remains the only
fully open item among the original seven; #8 (nuke-and-pave's exit trigger) remains open as well.

**Net after revision 4: no change to this list.** Neither the test-control expansion (§2.6) nor
the views work (§2.7) touches any of the eight, which is itself worth recording — both were
additive by construction. Revision 4 raises exactly one thing for the owner, and it is
deliberately **not** added here as item #9, because it is a confirmation of a decision the panel
has already made and can defend, not a question the panel is unable to answer:

> **Confirmation, not a blocker (§2.7.3):** is the views rule *"nothing in this codebase ever
> writes through a view"* or *"the business layer never writes through a view, and the data layer
> may use trigger machinery internally if a future spike proves it out"*? The panel recommends the
> second as the stated rule with the first as the current state — they are indistinguishable today
> and diverge only if someone proposes the transfer-both-sides `INSTEAD OF` spike. Work proceeds on
> the recommendation either way; a "no, make it absolute" answer costs one line in §1.7.1 and one
> sentence in §2.7.3.

**Net after revisions 5 and 6: no change to this list either.** Both were write-port naming and
shape questions; neither touches a product or appetite decision. Revision 6 raises **no** new item
for the owner and adds no confirmation request — unlike revision 4's views question, the panel had
the evidence it needed in the tree (the 2026-09-16 call-site inventory, `PersistentObject`'s
change-state machinery, and what the store's dispatch actually does per root type) and could answer
outright rather than hand part of it back. §1.6c does flag **one latent implementation-semantics
question** — what `DeleteRoot` should do with a root that was never persisted — but deliberately
routes it to slice 3 and its contract test rather than up to the owner, because it is a behaviour
to pin, not a trade-off to choose.

**Net after revision 7: item #4 moves from untouched to resolved by the owner.** The owner has
decided: *"The query-based pattern replaces the whole-graph-in-memory pattern everywhere."*
`Load()` does not survive; see §4 item 4 for the decision as recorded. This is a recording of a
product/appetite decision, not new design work — §1 was already designed to work either way by
construction, so nothing here changes shape.

One piece of framing elsewhere in this section is now stale as a direct consequence, flagged here
rather than resolved: **item #3's table row** (SQL Server feature parity) recommends letting
`IMoneyQuery` and other read-side capabilities lag on SQL Server "while schema management does
not," on the premise that the query surface's main consumer was reports. With whole-graph `Load()`
gone, `IMoneyQuery` is how *every* read reaches the data layer, on every engine — so a lagging
query surface on SQL Server would mean the SQL Server tier can't serve ordinary application reads,
not just stale reports, while schema management races ahead. Item #3 remains open and is not
resolved here; the panel's recommendation may need revisiting in light of this, but that is the
panel's or the owner's call to make separately, not something this recording pass decides.
**Resolved in revision 11** — see §4 item 3. The owner's answer removes the premise this
recommendation rested on rather than revisiting it in isolation: no query-surface carve-out
survives, under the same-check-in principle that already governs everything else in this document.

**Net after revision 8: items #5, #6 and #7 all move to resolved by the owner**, which takes the
original seven down to **two still open** (#1, #3) plus #8. Two of the three were pure recordings;
the third was not, and the difference is worth stating rather than flattening:

| # | Status after revision 8 |
|---|---|
| 5 — `XmlStore` as a day-one export format | **Resolved — "deferred."** The owner's verbatim word. Not in the first implementation slices. §3.2's *placement* decision (business layer, behind `IDataFormat`, never behind `IMoneyStore`) is untouched — only the *timing* is decided, which is all this item ever asked. Revision 2's row in the table above had argued "yes, sooner"; that recommendation is now **overridden by the owner**, which is the correct outcome for an item the panel had explicitly flagged as a scope call it could not make unilaterally. |
| 6 — credential storage | **Resolved — "later."** The owner's verbatim word. The panel recommended "now"; the owner decided otherwise. The plaintext passwords in `dataengine.config.json` remain a real live exposure, deferred rather than dismissed, and belong on the issue tracker rather than in this document. |
| 7 — sample data: product or test? | **Resolved, and the answer was not one of the two options offered.** *"test only… they are letting the customer test the capability… it could be considered a feature… it should only be done on a db marked as test."* Both at once, with the line drawn at the database. This is the only one of the three that needed design rather than recording — see §4 item 7 and **new §1.9**. The panel's placement recommendation (`MyMoney.Business`, shipped, not `MyMoney.TestKit`) is **confirmed**; what it lacked was a guard, and §1.9 supplies one by widening §1.8's existing mechanism rather than inventing a second. |

**What #5's deferral leaves unserved, flagged not redesigned.** Revision 2's argument for XML
being *early* was specific and is worth not losing: under nuke-and-pave, an XML export was *"the
only thing that lets a developer keep a hand-built scenario across a re-pave."* That need does not
go away with the deferral. It is, however, **already served by something this document designed
for other reasons** — §2.6.2's `IMoneyStoreTestControl.Snapshot()` / `Restore(StoreSnapshot)`,
which preserves a seeded database across a reset and is explicitly the fast path §2.6.2 chose over
a `Seed` method. The difference that matters, and the reason this is a flag rather than a
substitution: `Snapshot` is **test-tier only and never shipped**, so it covers the developer's
re-pave case but *not* D-35's disaster-recovery / "only way off the product" case, which remains
genuinely unbuilt until `IDataFormat` is scheduled. Recorded so the deferral is not later
mistaken for the need having evaporated.

**No slice-plan reference goes stale from #5's deferral**, which the panel checked rather than
assumed: §5's slice table contains no XML, `XmlStore` or `IDataFormat` deliverable — the
"yes, sooner" argument lived only in §4.1's row-5 recommendation above and never made it into the
slice order. So the deferral costs nothing in §5, and the only edit it forces is the one made
here. One adjacent confusion worth pre-empting, since both involve XML: `SampleDataGenerator`'s
`Export(path)` writes the **sample-data spec** (`SampleData.xml` — the payee/account frequency
histogram) via `XmlSerializer`, which is *not* `IDataFormat`'s whole-model round-trip and is
unaffected by #5's deferral. §1.9's sample-data work does not wait on XML.

**Net after revision 9: item #8 moves from open to resolved by the owner**, which takes the
original-seven-plus-#8 list down to **two still open** (#1, #3). The owner's resolution reframes
rather than answers the panel's original "name the trigger" request — there is no project-wide
trigger, because `DatabaseEntry.TestDatabase` is a permanent, per-database attribute fixed at
creation (the same flag revision 8's §1.9 already built `StoreIdentity.IsTestDatabase` and
`TestDatabaseGuard` around), and "nuke and pave is over" simply describes a test-reset routine
finishing, not a milestone the project passes through once. See §4 item 8 for the reasoning and the
owner's verbatim clarification, §1.5 S-0 for the cross-reference into the schema-management design,
and §2.6.8 for the idempotency property the resolution depends on. Nothing in this design's shape
changes as a result: `DatabaseEntry.TestDatabase` was already the gate (§1.8, and now doubly so via
§1.9's `TestDatabaseGuard`), the versioned schema machinery already applied identically to every
database from its creation (§1.5 S-1), and §2.6.8 adds one explicit clause (`DeleteRow`'s
no-op-on-missing-row behavior) rather than a new mechanism. This is a recording of a product/appetite
decision, not new design work, consistent with revisions 3 and 7's resolutions of items #2 and #4.

**Net after revision 10: item #1 moves from open to resolved by the owner**, which takes the
original-seven-plus-#8 list down to **one still open** (#3). The owner's decision, verbatim: **"In
place."** The rebuild happens in the existing `Source/WPF/` tree, incrementally, not in a parallel
`Source/Rebuild/` tree. Unlike #8, this is not a new decision surfacing for the first time — it was
given early in the review process, before several of the intervening revisions, and simply went
unrecorded until now; revision 10 is that belated recording, not new design work. See §4 item 1 for
the decision as recorded and revision 2's row above for the de-risking context (nuke-and-pave
removing the "existing format must keep working" argument for a parallel tree) that made the call
easy. Nothing in this design's shape changes as a result: §7 already assumed *in place,
incrementally* throughout, and §5's slice plan was already assembly-named rather than tree-path-named,
so neither needed an edit beyond this recording.

**Net after revision 11: item #3 moves from open to resolved by the owner, closing the
original-seven-plus-#8 open-items list entirely — zero still open.** The owner's decision, verbatim
(extending the general parity answer given earlier in this conversation, before this was formally
item #3): **"Same principle for the query layer too."** §4 item 3 has the full reasoning, including
the general-parity answer it extends and the explicit closing of revision 7's "may now be stale"
flag on the "lag the query surface" recommendation (see this section's "Net after revision 7" note,
above). Nothing in this design's shape changes as a result — no candidate, port, or slice was ever
built assuming a lag, per §1's Candidate A introduction of `IMoneyQuery` and §6.4's requirement that
the contract suite cover `IMoneyQuery` the slice it appears — the only edits are this recording, the
closed revision-7 flag, and matching tightenings noted in §8's closing summary.

---

## 5. Recommended starting point, sanity-checked

The owner's proposed first vertical slice — *"enough to support adding an account to the
database"* — **holds up well.** `Account` is an `IAggregateRoot` with no owned children (unlike
`Transaction` → `Splits`/`Investment`), so it exercises the full insert/update/delete + version
conflict path without dragging aggregate-composition complexity in on day one. Three wrinkles to
flag:

- **You can't add an account before provisioning exists**, so the slice is really
  *provision-then-add*, and that's good — it forces the Admin tier to be real from the first
  commit rather than bolted on.
- **`Account` has FK relationships** to `Currency` and `OnlineAccount` (both nullable). With
  `foreign_keys=ON` (already set), the slice's schema must create those *tables*, even though it
  needn't create any *rows*. Small, concrete, easy to get wrong once.
- **`IAggregateRoot.Id` is `long`.** Don't let a fresh schema narrow it to `INT` because that's
  what the current tables say.

### Proposed slice order

*(Revised for revision 2: slices 2, 4, 7, 8 and 9 changed; 2b, 5b and 10 are new. Touched since:
slice 5 by revision 4 (§2.6's data/row ladder), slices 3, 6 and 7 by revision 6 (§1.6c's
`SaveRoot`/`DeleteRoot` split), slice 5b by revision 8 (§1.9's shared guard). Revisions 7, 10 and
11 changed no slice — each checked and recorded that in §4.1.)*

| # | Deliverable | Proves |
|---|---|---|
| 1 | `IMoneyStore` / `IMoneyStoreProvisioner` / `IMoneyQuery` ports (in `MyMoney.Business`), `MyMoney.TestKit.Contracts` with `IMoneyStoreTestControl` | The tier split exists as types before any engine implements it |
| 2 | `MyMoney.Data.Sqlite.Provisioning` — the §1.5 executor: `__SchemaHistory` ledger, per-step transactions, checksums, `ApplyTo`/`CurrentVersion`/`Verify`; steps creating `Accounts` + FK-target tables as `STRICT` | Schema-as-versioned-artifact, not reflection-to-DDL; and the upgrade mechanism exists before anything needs upgrading |
| **2b** | **The fresh-vs-upgraded schema-equality test** (§1.5 S-4): build at N; build at N−1 then `ApplyTo(N)`; assert introspected schemas identical. Include a step that adds an index to a table created by an earlier step — the issue #34 shape | **Issue #34 cannot recur.** The upgrade path runs on every build from here on |
| 3 | `MyMoney.Data.Sqlite` — `SaveRoot<Account>` **and `DeleteRoot<Account>`** over one shared `WriteRoots` executor on `MoneyStoreBase` (§1.6c), `LoadAccounts`, conflict detection, `RETURNING`-read versions (§1.7.1); plus §1.8's open-time version check ("this database is newer than this binary") | The proven single-root save pattern (today's `SaveOne`, renamed `SaveRoot` in revision 5 — §1.6b, and split into save/delete in revision 6 — §1.6c) survives the reshape, engine-side, with the insert/update/delete dispatch still written exactly once |
| 4 | `MyMoney.TestKit` — in-memory-SQLite store fixture (T-1), `RecordingStore`, `FaultInjectingStore`, `StoreContractTests` base with the Account cases | Happy path **and** error path from day one, as the owner asked — over a real engine, not a mock |
| 5 | `MyMoney.Data.Sqlite.TestTier` — `IMoneyStoreTestControl`'s **schema** level (`ResetSchema` = `Schema_DropAll` + `ApplyTo(N)`, i.e. nuke-and-pave through the §1.5 machinery) **and its data/row levels** (§2.6): introspection-derived `ClearAllData`/`ClearTables`/`ClearTable`, `CaptureRow`/`DeleteRow`/`RestoreRow` + the `RemoveRow` scope, `TableRef` with its anti-drift contract test, and the `ResetIdentity` parity assertion (§2.6.4) | The SQLite facade, for real; the owner's nuke-and-pave as a first-class operation rather than a script; and a reset granularity a test can actually aim (§2.6) |
| **5b** | **The shared `TestDatabase` guard** (§1.8, §1.9.3): `StoreIdentity` on `IMoneyStore` (§1.9.2), `TestDatabaseGuard.Require` + `TestDatabaseRequiredException`, wired into the provisioner's destructive operations and into the factory that hands out an `IMoneyStoreTestControl` (§2.6.4 — once, at acquisition, not per method); plus Tier-0 `TestDatabaseFlag_IsReadOnlyByTheSharedGuard` and the per-engine flag round-trip test (§1.9.7) | The only guard that currently exists becomes enforced rather than assumed — **and it is built once, here, as the mechanism §1.9's shipped sample-data feature reuses rather than parallels** |
| 6 | `MyMoney.Tests.Architecture` — the other ten of §2.5's **eleven** Tier-0 tests (the eleventh, revision 8's `TestDatabaseFlag_IsReadOnlyByTheSharedGuard`, lands with its mechanism in slice 5b), including revision 4's `ProjectionTypes_DoNotImplementIAggregateRoot` and `StoreWriteMethods_AcceptOnlyAggregateRoots` (§2.7.2) and revision 6's `StoreWriteSurface_IsExactlyTheFourNamedMethods` (§1.6c); plus — in `MyMoney.Tests.Data` (§2.2), since these are Tier-2 and per engine, not architecture tests — the *schema owns no `INSTEAD OF` trigger* check (§2.7.3) and §1.6c's six precondition cases | The guarantees are enforced, not asserted — including "the business layer cannot write to view-backed data," which is a compile-shaped property rather than a documented rule, and "`SaveRoot` cannot remove a row," which is a behaviour a test proves rather than a name that implies it |
| 7 | `AddAccountService` in `MyMoney.Business`, including its conflict-retry loop (§1.6a: catch `ConcurrencyConflictException` **and nothing wider**, re-query, reapply, retry) + its in-memory-store-backed tests, using `FaultInjectingStore` to provoke a conflict on demand and to prove an `ArgumentException` from §1.6c's preconditions is *not* retried | The business layer is callable with no UI present, and version-checked concurrency with business-layer retry (§1.6a) is a working, tested pattern from the very first slice — not deferred to a later one — with the caller-bug/race distinction pinned rather than assumed |
| 8 | `MyMoney.Data.SqlServer{,.Provisioning,.TestTier}` for the same slice: real `Schema_ApplyTo`/`Schema_Verify` procs over the same step list and same ledger, plus slice 2b's equality test per engine, plus the `Test/*` half of §2.6 (`dbo.Test_ClearTables` taking a table-name TVP, deployed only into a `TestDatabase: true` catalog) | The tiering maps twice, the schema mechanism is parity (§4.1 #3), the contract suite is genuinely shared, and §2.6.4's `ResetIdentity` divergence is pinned rather than discovered |
| 9 | **T-1 verification gate** (§2.4): run the real business-test tier against the in-memory store; record wall time against the 60 s budget and the stated kill criterion | The decision already made is confirmed by measurement, not re-opened |
| 10 | **`json_each` batch benchmark** (§1.7.1): SQLite `SaveRoots` as one set-based statement vs. today's C# loop, at realistic batch sizes | P-SQLITE is applied on evidence, not aesthetics — and R-CRUD-3 lands on both engines or is honestly declined on one |

**How much of this is one implementation plan** *(revision 12, the self-review's scope call)*. The
document as a whole is **not** one plan's worth of work: §3's placement rulings, §3.3's reporting
redesign, §1.8's deferred upgrade workflow and §1.9's sample-data rewrite are all decided-but-
unscheduled, and none of them is in the table above. The table is the right unit, and it splits
naturally in two at the engine boundary:

- **Plan A — slices 1 through 7** (the SQLite vertical: ports, provisioning + ledger, the
  fresh-vs-upgraded equality test, the store, the TestKit, the test tier, the shared guard, the
  Tier-0 band, and `AddAccountService` with its retry loop). This is one coherent plan with one
  demonstrable end state — *provision a database and add an account, headlessly, with the
  concurrency path tested* — and every slice in it is a prerequisite of the next.
- **Plan B — slices 8 through 10** (SQL Server parity for the same slice, then the two measurement
  gates). Slice 8 depends on all of Plan A existing, and slices 9 and 10 are verification of
  decisions Plan A implements, not new capability. Nothing in Plan A is blocked by Plan B.

Splitting there is a recommendation, not a constraint — but running the whole table through one
planning pass would produce a plan whose second half is written against code that does not exist
yet, which is the failure mode that makes long plans stale rather than the one that makes them long.

**Where sample data lands in this order (revision 8), and where it deliberately does not.** The
guard is slice **5b**, above — it is the same mechanism the provisioner needs, built once (§1.9.3).
The `SampleDataService`/`SampleDataFactory` rewrite itself is **not in slices 1–10 and is not added
as a slice here.** Two reasons, both honest: slices 1–10 stop at `Account`, while sample data needs
`Payee`, `Category`, `Security`, `Transaction` and owned `Split` roots; and the owner's decision
(§4 item 7) settled *where it lives and what guards it*, not *when it is built* — so adding a
numbered row would imply a scheduling commitment nobody made. When it is scheduled, size it as the
§1.9.4 rewrite (the factory must *return* roots instead of mutating an ambient `MyMoney` graph,
which revision 7 removed), not as a project-file move. One nice side effect worth remembering at
that point: sample data is the first realistic workload for slice **10**'s `json_each` batch
benchmark (§1.9.5).

**One gap in this table, found by the revision-12 self-review and flagged rather than filled.**
**No slice implements `IMoneyQuery`.** Slice 1 introduces it as a *type*; nothing after that
delivers `Aggregate`/`List` on either engine, and no slice carries its contract tests. That was
defensible while the port was reports-scoped and reports were out of the first slices — but
**revision 7 made `IMoneyQuery` the whole application's read path**, and **revision 11 ruled out any
SQL Server carve-out for it**, and neither revision revisited this table (both were recording
passes; §4.1 says so in both cases). Two things now sit in tension with the table as written: §6.4's
*"the contract suite must grow to cover `IMoneyQuery` in the same slice the port is introduced — not
later"*, and slice 3's `LoadAccounts`, which is a read whose port — `IMoneyStore`'s simple reads or
`IMoneyQuery` — this document never pins. **This is a scheduling/scoping question for the
implementation plan, not a design hole**: the port's shape (§1), its SQLite landing site (§1.7.1's
views), its SQL Server shape (`_Query` proc family) and its parity rule (§4 item 3) are all decided.
What is undecided is which slice builds it and what slice 3's read goes through. Flagged here so the
plan resolves it deliberately instead of inheriting a table that predates two decisions.

**One honest deviation from the literal instruction.** The owner's guidance says build *"the data
layer DLLs, as well as the app and business layers of the test subsystem first."* The business
half of that is slices 4/7 above. The **app** half is the part to push back on: there is no
rebuilt app layer yet for app-layer tests to test, and the project already has working, hard-won
FlaUI infrastructure (`BasicsTestSetup`, `AppCrashGuard`, the shared-session lifecycle notes in
`docs/dev/flaui-basics-test-notes.md`). Recommendation: **carry that infrastructure forward as-is
and re-point it when there's a rebuilt app to point it at**, rather than building a second UI test
harness against a UI that doesn't exist. Flagged rather than silently skipped.

---

## 6. Adversarial review — where this design is weakest

### 6.1 The SQLite facade is not equivalent to SQL Server role separation, and the design must say so

SQL Server's guarantee is enforced by a *different process*, under a *different identity*, and the
destructive routine is *not installed* on a production catalog. SQLite's file is opened by the
same process that would do the damage, with full OS rights to it, always. No arrangement of .NET
types changes that.

What Candidate A's facade **can** honestly claim:

- The code capable of the destructive operation is **not in the shipped assembly set** (mirrors
  P9-GUARD-2's real property).
- Production code **cannot name the type** needed to request it.
- A build fails, and a test fails, if either becomes untrue.

What it **cannot** claim: that a buggy, compromised, or deliberately-reflective production process
is prevented from destroying the file. The only genuine strengthenings available are Candidate C's
process separation or OS file ACLs, and the panel judges neither proportionate for a
single-user desktop finance app. **State this scope limit in the eventual spec in these words** —
an overstated guarantee is worse than an honest partial one, because it stops people looking.

### 6.2 `InternalsVisibleTo` is the most likely crack

Not a hypothetical: it is the standard .NET answer to "the test needs to reach this," and it
dissolves the whole tier split in one line. Hence the Tier-0 test
`ProductionAssemblies_DeclareNoInternalsVisibleTo`. If a genuine need ever appears, it means the
tier boundary was drawn in the wrong place — move the boundary, don't punch through it.

### 6.3 The deadline shortcut that actually happens

*"I just need `BasicsFixtureBuilder` from TestKit in this one production class."* A test-run-time
failure is easy to ignore when you're mid-task; a `dotnet build` failure is not. That's why §2.5's
mechanism (2) — the build-time `ReferencePath` check — matters more than mechanism (3), even
though (3) is the one that matches the project's existing, trusted pattern. **Both**, not either.

### 6.4 Two implementations of aggregation, silently diverging

`IMoneyQuery` will have a LINQ-over-memory implementation in the test double and a SQL
implementation per engine. If the shared contract suite covers only writes (as it does today) and
not queries, then every business-layer report test is asserting against a fiction, and the first
time anyone notices is when a real report subtotal is wrong. **The contract suite must grow to
cover `IMoneyQuery` in the same slice the port is introduced — not later.** This was also the
strongest single argument in favor of T-1's decorated-real-SQLite option, which makes the problem
structurally impossible — and in revision 2 it is the argument that carried T-1 (§2.4). With no
mock, the "two implementations" in this section's title are now only the two *engines*, which the
shared contract suite was always designed to compare. The requirement above is unchanged and still
not optional: the contract suite must cover `IMoneyQuery` the slice it appears.

### 6.5 The simpler design nobody has proposed — *adopted in revision 2*

Delete `MockStore`. Run every business test against a real in-memory SQLite store, decorated for
recording and fault injection. Fewer moving parts, zero divergence, one implementation of every
query. Round 1 stopped short of recommending it only because nobody had measured whether it fits
the sub-minute budget.

**Revision 2 takes it.** §2.4's T-1 resolution adopts this design and keeps the measurement as a
verification gate with a pre-stated kill criterion, rather than as a precondition. The adversarial
note that survives: *a design section that adopts the thing the adversarial section proposed is
exactly where nobody looks for the flaw.* The flaw, if there is one, is that the in-memory store
makes every business test depend on the schema steps being written and correct — so a broken
schema step now fails hundreds of tests instead of a handful, with a first error message about
SQL rather than about business logic. The panel judges that an acceptable and even desirable
coupling during nuke-and-pave (§2.4 point 4), but it is a real change in failure ergonomics and
whoever is debugging at 11pm should have been told.

### 6.6 Seven-to-nine assemblies is a real cost, not a rounding error

More projects, slower cold builds, more `.csproj` churn in reviews, and a structure that the next
person will try to "simplify." The mitigation is not discipline; it is that the Tier-0 tests fail
loudly when the simplification happens, and that the reasoning is written down where the tests
point to it.

### 6.7 Nuke-and-pave is accepted — here is the risk it creates anyway

The owner has decided this and the panel agrees with the decision. Naming the risk is not
re-litigating it; it is the reason §1.5 and §1.8 are shaped the way they are.

**The risk is not data loss. It is habit formation.** Nuke-and-pave makes the *incremental* step
the expensive one and the *rewrite* the cheap one, on every schema change, for as long as the phase
lasts. Every day of that phase trains a workflow — "change the table definition, re-pave, move on" —
that is exactly wrong on the day production arrives, and it does so quietly, because nothing ever
fails. Several specific expressions of it:

- **Steps get edited instead of appended.** Rule S-3.1 says a step is immutable once applied. Under
  nuke-and-pave that rule protects nothing you can see, so it is the first rule to erode. This is
  the entire reason `ChecksumHash` must be populated and checked from slice 2 rather than "when it
  matters" — the check is the only thing that makes the rule observable during a phase where
  breaking it is free.
- **Steps don't get written at all.** Why add step 12 for an index when re-paving from a corrected
  step 4 works? Because on the day it isn't free, the corrected step 4 is a lie about what every
  existing database ran. §1.5 S-4's caveat is honest that the mechanism does not prevent this;
  slice 2b's equality test is what makes the omission visible.
- **The "end of the phase" never gets declared** — it just turns out, retrospectively, to have
  happened three months ago on a database somebody started caring about. This was open item #8;
  **resolved in revision 9**: there is no single project-wide phase to declare the end of —
  `DatabaseEntry.TestDatabase` is fixed per database at creation, so a database that starts as test
  never "graduates," and a database that starts as production never has nuke-and-pave reachable at
  all (§4 item 8). That resolution removes the *ambiguity* this bullet names, but not the underlying
  habit-formation risk itself: nothing about a permanent, per-database flag stops someone from
  continuing to depend on a database marked `TestDatabase: true` past the point it should have been
  replaced by a real one — that remains a people/process risk, not something the flag design can
  close by itself.
- **`Schema_DropAll` exists and works, in a codebase whose only guard is a config flag.** §1.8 and
  slice 5b address this; flagged here because "we're in the test phase" is precisely the reasoning
  that makes a destructive operation feel safe to leave unguarded, and precisely the reasoning that
  stops being true without anyone editing any code.

None of this argues for migration ceremony now. It argues that the three cheap properties in §1.8
are the price of taking the shortcut safely, and that they are cheap *only* while the phase lasts.

### 6.8 "Use more native SQLite" can itself become a divergence source

P-SQLITE (§1.7) is aimed at convergence, and mostly achieves it. But the same principle, applied
without the §1.7.3 brake, produces the opposite: a `CHECK` constraint, a trigger, or a `STRICT`
column that exists on SQLite and has no SQL Server counterpart is a behavioral difference hiding
under one interface — the precise failure the owner is trying to eliminate, arrived at from the
other direction. Two rules follow, and they belong in the eventual spec:

1. **An engine-native behavior adopted on one engine must be matched on the other or explicitly
   declined in writing.** `STRICT` tables have a SQL Server counterpart (typed columns, which it
   has always had), so adopting them converges. A `CHECK` constraint with no SQL Server twin
   diverges. **Revision 4 adds the case where this rule is satisfied and is still not enough:** an
   `INSTEAD OF` trigger encoding a write rule *does* have a counterpart (a proc), so by this rule
   it converges — yet it was declined anyway, for reasons this rule does not capture (§2.7.3).
   Rule 1 is a necessary test, not a sufficient one. The two concrete divergences revision 4 found
   by applying it are both textbook cases for this section: `SELECT *` inside a view resolves at
   prepare time on SQLite and at create time on SQL Server (§2.7.1), and a table clear rewinds id
   allocation on SQLite but not on SQL Server (§2.6.4). Both were found by going looking, which is
   the only way they get found before a test lies about them.
2. **The shared contract suite is the arbiter, not the principle.** If a native feature can't be
   shown to produce identical observable behavior through `IMoneyStore` on both engines, it doesn't
   go in — however native it is. §6.4 already makes this argument for `IMoneyQuery`; §1.7 widens
   the surface it has to cover, which is a real, recurring cost of the new principle and should be
   budgeted as one.

Stated as a tension rather than resolved, because it is a genuine one: the owner asked for maximum
use of SQLite's own functionality *in service of* making the two engines look alike, and those two
goals do not point the same direction at every decision. Where they conflict, the panel's
recommendation is that **parity wins over nativeness** — §1.7.3's version-conflict example is the
worked case.

### 6.9 The sample-data guard is a runtime check in shipped code, which is the weakest of the three protections *(new in revision 8)*

§1.9 puts a customer-reachable, data-fabricating capability behind a runtime `if`, in an assembly
that ships. Compared with the two protections around it — assembly absence (§1 Candidate A) and
SQL Server's *different login, different process* separation (§6.1) — that is plainly the weakest
form, and the design should not pretend otherwise. What it can and cannot claim, in §6.1's format:

**Can claim:** the check is always compiled in, in every configuration, so it cannot be lost to a
`#if` or a Release packaging decision; it is the *same* code path as the provisioner's destructive
guard, so there is one behaviour to reason about rather than two that drift; it runs before any
generation or any write, so a refusal leaves nothing partial; every failure mode of its input
resolves to "refuse" (§1.9.2); and a Tier-0 IL scan keeps it the only reader of the flag.

**Cannot claim:** that a production process which has the database open cannot write fictitious
rows into it. It obviously can — `SaveRoots` is right there, and §1.9.8 says so directly. The
guard stops the *feature*, not the *data*, and a caller who assembles a sample set by hand has
simply left the feature.

**Why the panel accepts that**, rather than reaching for a stronger mechanism:

- The stronger mechanism (assembly absence) is **ruled out by the requirement itself** — the owner
  wants an evaluating customer to reach this in a release build. There is no version of "not in the
  shipped bits" that satisfies that. This is not a case where a better option was available and the
  cheap one was chosen.
- The threat model is **accident, not adversary.** The realistic failure is a new call site, a
  script, or an agent running sample-data population against the wrong open database — all of which
  a runtime refusal stops cleanly. A user determined to put fake transactions in their own real
  books can type them.
- The thing that would actually erode this over time is not the mechanism's weakness but a
  **second, divergent copy of the check**, which is exactly what §1.9.7's Tier-0 test is aimed at.
  That is where the panel spent the enforcement effort, and it is the right place.

**The honest residual risk**, stated so it is not discovered later: `DatabaseEntry.TestDatabase`
is a single user-settable checkbox with no confirmation and no audit, and §1.9's whole guarantee
rests on it. A user who ticks "Test database" on a database they later fill with real financial
data has silently re-enabled sample-data population against their real books, and nothing in this
design notices. That is arguably an argument for the flag being *harder to set after a database has
non-trivial data in it* — but that is a product decision about the registry UX, not a data-layer
one, and it is flagged here rather than designed. It is the same flag §1.8 already identified as
*"the only thing standing between a destructive operation and a database"*, so this is not a new
dependency revision 8 introduced — only a new consumer of one the document already flagged.

---

## 7. Role notes, opt-outs, and expertise the panel does not have

**UI/UX Expert — largely opting out** (backend-heavy phase), with two contributions worth keeping:

- Provisioning and store failures must reach the user as plain-language messages through the
  existing callback port, not as a raw `SqlException` or `SQLiteException` string. A failed
  bootstrap that says *"Login failed for user 'MyMoneyAdmin'"* is a support ticket; the tiering
  model makes several such failures newly reachable. **Revision 8 gives this its first concrete
  instance in shipped, customer-facing code:** §1.9.6's `TestDatabaseRequiredException` is thrown
  in the business layer and must surface through `IBusinessLayerUiCallback` as the plain-language
  refusal quoted there, never as a type name — and, per the same bullet's logic, the *normal*
  customer path is layer 1 (a "try it with sample data" flow that creates a test-marked database),
  with the message reserved for the case a caller reached the capability another way.
- `ReportModel`'s cells must carry formatting *intent*, not presentation. The whole point of the
  eventual Fluent/WPF-UI restyle is that it shouldn't require touching a single report class.

**Expertise the panel does not have — named, not faked:**

1. **Whether "Microsoft Print to PDF" behaves under an unattended automated session.** D-17's
   decision rests on it, and the Operations Engineer already flagged that printing touches the
   spooler and driver stack — the least predictable part of Windows — and that a headless/agent
   machine may have no print queue at all. This needs an empirical spike (does the driver exist,
   does it prompt for an output path, does it throw with no default printer), not a panel
   judgment. Given this project's standing requirement that tests run fully unattended, resolve it
   *before* committing Stage 3 to a print-dependent verification path — the Markdown-string
   assertions are unaffected either way, which is precisely why D-17's intermediate representation
   was the right call.

2. **A hardened-SQL-Server-policy review of "Admin deploys procs that perform DDL."** The Data
   Engine Expert can design the proc family, but whether a real least-privilege server policy
   permits `MyMoneyAdmin` to do it without an effectively-`db_owner` grant is a DBA question. On a
   developer-only engine (D-37) the blast radius is small, so this does not block the design — but
   it should not be written up as a security property until someone who does this for a living has
   looked at it. **Revision 2 raises the stakes slightly**: §1.5 makes proc-executed DDL the *only*
   schema mechanism rather than one of two, so a policy that forbade it would need a fallback this
   document does not currently have. Still not blocking — but worth confirming before slice 8
   rather than after.

3. **Nobody here can speak for the report recipient.** D-17's own panel flagged this and the owner
   resolved the format question; it stays flagged only because §3.3's `ReportModel` shape (which
   cells, which formatting intents) will eventually be judged by whoever receives a report.

4. **(Revision 2; superseded in its recommendation by revision 4.) Whether `INSTEAD OF` triggers
   on views are a maintainable way to encode multi-statement write logic in SQLite at this
   scale.** The gap itself is unchanged and is stated here for the record: the panel has no
   operational experience of a codebase that leans on them heavily, and the failure modes
   (debuggability, error messages, interaction with `defer_foreign_keys`, behavior under
   `RETURNING`) are known to exist and not known in detail. **What changed is the recommendation
   built on top of it.** Revision 2 said "adopt for one concrete case, evaluate, then generalize";
   revision 4 says **do not adopt now at all** (§1.7.1, §2.7.3), keeping the transfer-both-sides
   case as a *deferred* spike rather than a first adoption — because the owner's views-are-
   read-only rule plus §2.7.2's type-system work make a writable view actively costly, and because
   this very item's `RETURNING` unknown is one of the three arguments that carried the decision.
   The gap therefore no longer blocks anything on the current path; it becomes live again only if
   the spike is ever proposed. Explicitly *not* a reason to skip §1.7's other findings, which are
   independent of this one.

---

## 8. Summary of what this document proposes

- **Split `IDatabase` three ways along the privilege axis** (`IMoneyStore` /
  `IMoneyStoreProvisioner` / `IMoneyStoreTestControl`) plus a non-store `IDataFormat`, and make
  each tier of each engine **its own assembly** (Candidate A), so privilege separation is
  "the code isn't in the build output" rather than "the code is politely hidden."
- **Keep `IAggregateRoot` and the write methods unchanged in behavior, renamed in revision 5 and
  split from three into four in revision 6** (§1.6b, §1.6c): `SaveOne<T>` → **`SaveRoot<TRoot>`**,
  `SaveBatch` → **`SaveRoots`**, `SaveTransfer` **kept as-is** because it was already named after
  what it saves rather than how many — it is the pattern, not the exception. The methods stay
  **generic**, which the owner has confirmed is the point (one body per operation instead of one
  per entity type); the nouns changed from cardinality (`One`, `Batch`) to subject (`Root`,
  `Roots`), matching §2.6.2's existing `ClearTable`/`ClearTables`, `CaptureRow`/`CaptureRows`
  convention.
- **Revision 6: a delete is no longer reached by calling `Save` (§1.6c).** The owner objected that
  `SaveRoot` performing a delete reads wrong, and the panel upheld it on stronger ground than
  taste — the destructive call was unnameable and ungreppable, and `OnDelete()`'s guardless soft
  delete makes "a root got marked deleted elsewhere and my save silently removed the row" a
  reachable bug rather than a hypothetical. **`DeleteRoot<TRoot>` is split out; `SaveRoot` now
  refuses a root marked deleted.** Insert and update are deliberately **not** split from each
  other: the call-site inventory's largest group is *"Dialog OK / `DataGrid.RowEditEnding`"*, which
  genuinely cannot know whether the row it is committing is new or existing, whereas no call site
  is ever unsure it is deleting. All four methods route through **one** private executor
  (`protected abstract WriteRoots` on a `MoneyStoreBase` in the port assembly), so R-CRUD-2's
  atomicity and its hard-won post-commit deferral stay written exactly once and the two engines
  cannot disagree about the precondition. The named asymmetry: `SaveRoots` and `SaveTransfer` are
  mixed-state by definition and assert nothing, so `SaveRoots(new[] { root })` remains a one-line
  unvalidated route to the same executor — stated as a real crack rather than papered over, and
  bounded by a Tier-0 test pinning the public write surface to exactly those four members.
- Also retire `Save(MyMoney)`; **add `IMoneyQuery`**, a closed, typed filter/aggregate port (not
  `IQueryable`), because reporting needs SQL-side filtering and subtotaling that doesn't exist
  today. *(Revision 7: reporting was the port's original motivating case, not its scope boundary —
  the owner has decided the query-based pattern replaces whole-graph `Load()` everywhere, so
  `IMoneyQuery` is now how every part of the application reads, not just reports. See §4 item 4.)*
  The SQL procs (`Currencies_SaveBatch` et al.) are deliberately **not** renamed — they are
  necessarily per-entity and read fine.
- **Schema management becomes one versioned mechanism with several entry points** (§1.5): an
  ordered list of immutable steps, a `__SchemaHistory` ledger with checksums, one per-step
  transaction, and `Schema_ApplyTo`/`CurrentVersion`/`Verify`/`DropAll` — real stored procs on SQL
  Server, embedded SQL plus an in-process executor on SQLite, same step list and same semantics.
  **Creating a database, nuking-and-paving, and the eventual production upgrade are the same
  operation started from different versions**, which is the owner's unifying constraint and the
  reason the upgrade path is exercised on every build. Reflection-to-DDL (`LazyCreateTables`,
  `CreateOrUpdateTable`) is deleted on both engines, which removes **issue #34** structurally —
  backed by a fresh-vs-upgraded schema-equality contract test (slice 2b) that reproduces #34 as a
  red test against today's code.
- **CRUD and batch writes stay parameterized and transactional, stated as requirements**
  (§1.6, R-CRUD-1..5) rather than carried forward implicitly, including the TVP-per-batch pattern
  and the engine-independent conflict contract.
- **Concurrency, resolved (§1.6a, revision 3): version-checked optimistic concurrency, permanently,
  on the shipped SQLite path — no file lease.** The owner's actual goal is a human and an AI agent
  working the same books at once, which a lease would defeat by serializing access. The data layer
  keeps reporting conflicts (`ConcurrencyConflictException`, unchanged); the business layer gains a
  new retry loop (re-query, reapply, retry) that does not exist today; and `FaultInjectingStore`
  (§2.4) is formalized as the test-tier's first-class API for deliberately provoking a conflict to
  test that loop. This closes §4 item 2, which revision 2 had left open.
- **New principle P-SQLITE** (§1.7): express behavior in SQLite where SQLite can express it —
  `RETURNING`, `json_each` batches, views, `STRICT` tables, structured `PRAGMA` introspection,
  `VACUUM INTO` backups — with an explicit, honest list of what cannot close (no procedures, no
  roles or grants, no typed TVPs) and **two** cases where the panel pushes back on the principle
  itself: don't move the version-conflict check into a trigger (§1.7.3), and don't make views
  writable (§1.7.4's P-VIEW, new in revision 4).
- **Test control gets three levels, not one (§2.6, revision 4).** `ResetSchema(N)` is the existing
  `Schema_DropAll` + `Schema_ApplyTo(N)` nuke-and-pave, unchanged. Underneath it and *separate from
  it* sits a data ladder — `ClearAllData()` → `ClearTables(subset)` → `DeleteRow`/`CaptureRow`/
  `RestoreRow` and a `using`-scoped `RemoveRow` — that leaves every schema object, including
  `__SchemaHistory`, untouched. Table-level clear is **not** a decomposition of the schema wipe
  (they are two ladders; only the data one decomposes), the clear executor **derives** its table
  set and FK delete order by introspection rather than carrying a hand-written list (issue #34's
  lesson, applied where it would otherwise recur verbatim), and the whole surface sits below the
  version check and below the object graph on purpose — it builds fixtures, it is not the thing
  under test. Two concrete cross-engine traps are pinned by contract test: identity/rowid
  reseeding after a clear, and a snapshot's schema version.
- **Views are a read surface, and the business layer cannot write through one — structurally
  (§2.7, revision 4).** Resetting needs no special view handling at the *data* level (a view holds
  no rows), but `Schema_DropAll` must drop views before tables and `Schema_Verify` must include
  them, view definitions never use `SELECT *` (it resolves at prepare time on SQLite and at create
  time on SQL Server), and a view change is its own numbered step. The read/write asymmetry is
  enforced by four stacked type-system properties rather than by documentation: separate
  `IMoneyQuery`/`IMoneyStore` ports, query results marked `IProjection` and never `IAggregateRoot`
  (so neither `SaveRoot(row)` nor `DeleteRoot(row)` compiles), projections carrying **no** `RowVersion` (so a stale
  version cannot be smuggled from a report into a write), and the business layer never naming a
  view at all. **`INSTEAD OF` triggers are demoted from revision 2's "adopt selectively" to "do
  not adopt now"**, kept as a deferred single-candidate spike with a standing "never above
  `IMoneyStore`" constraint, and enforced by a per-engine contract test asserting the schema owns
  no `INSTEAD OF` trigger. One narrow confirmation is flagged back to the owner (§2.7.3): whether
  the rule is "nothing ever writes through a view" or "the business layer never does" — the panel
  recommends the latter as the rule and the former as the current state, and nothing waits on the
  answer.
- **Sample data is a shipped feature with a runtime test-database guard (§1.9, revision 8).** The
  owner's answer was not the product-or-test binary the panel posed: it is customer-reachable (an
  evaluating buyer runs it *"to see what the UI and the reports look like"*) **and** restricted to
  a database marked `DatabaseEntry.TestDatabase`, with the line drawn at the database rather than
  the audience. That combination fits neither of §1's protection mechanisms as they stood —
  assembly absence would hide the feature from the customer it exists for, and §1.8's runtime
  refusal was scoped to *destructive* operations, which sample data is not. The resolution invents
  **no new mechanism**: `SampleDataGenerator` stays in `MyMoney.Business` and ships, split into a
  pure unguarded `SampleDataFactory` and a guarded `SampleDataService` whose first statement is
  the **same** `TestDatabaseGuard.Require(store.Identity, …)` that slice 5b builds for the
  provisioner — with §1.8's stated scope widened from "destructive operations" to "operations that
  must only ever touch a test database." The flag is read off `StoreIdentity` on the **open store
  handle**, not from a `DatabaseEntry` threaded to the call site, so the guard reads it off the
  same object the writes go to, and every way the flag can break resolves to `false` (i.e. the
  capability refuses). The write path is deliberately **unprivileged** — ordinary `SaveRoots`,
  ordinary version checks, no test-control bulk loader — because a customer evaluating the product
  is evaluating what the real write path produces. Enforcement is a Tier-1 test asserting the
  refusal writes **nothing** plus a Tier-0 IL scan pinning the shared guard as the only reader of
  the flag, not a paragraph. The named crack: the guard is on the capability, not the data, so
  hand-assembling a sample set and calling `SaveRoots` is an ordinary write and is not refused —
  correct, and the same shape of admission as §1.6c's.
- **Test subsystem**: `MyMoney.TestKit` as a true library (not a test project), four test tiers
  with a new Tier-0 architecture band, four layered enforcement mechanisms for the one-way
  dependency, and `RecordingStore`/`FaultInjectingStore` so the error path is testable from day
  one. **T-1 resolved (§2.4): the default business-test double is a decorated in-memory SQLite
  store; `MockStore` is not built.**
- **The real production-upgrade workflow is deferred, not foreclosed** (§1.8): named as future
  work, with the three properties today's design must hold continuously because they cannot be
  established retroactively — the ledger from the first step, the upgrade path exercised on every
  build, and a contract-tested backup.
- **Open question resolved**: control logic → business layer's application-services band;
  format parsing/writing → business layer behind `IDataFormat`; engine-native backup/duplication →
  provisioner tier; reporting → three stages (query in the data layer, `ReportModel` in the
  business layer, Markdown/CSV renderers in the business layer's `Reporting` namespace), with
  `IReportWriter` deleted rather than ported.
- **First slice**: provision-then-add-an-Account on SQLite, with the schema ledger, the
  fresh-vs-upgraded equality test, the test subsystem and the Tier-0 boundary tests landing
  alongside it, then the same slice on SQL Server to prove the tiering *and the schema mechanism*
  map twice.
- **All originally-flagged items are now resolved.** Of §4's original seven items plus new item
  #8 — eight items total — **all eight are resolved by the owner; none remain open.** This closes
  the "needs the owner's decision" list this document has tracked since round 1. Resolved by the
  owner, in order:
  - **#1** — rebuild in place, not in a parallel `Source/Rebuild/` tree (revision 10; §4 item 1).
    A belated recording rather than a new decision: the owner gave this early in the review
    process, before several of the intervening revisions; revision 2's note on nuke-and-pave
    de-risking the in-place option (by removing the "existing format must keep working" argument
    for a parallel tree) is the relevant context for why the call was easy.
  - **#2** — no file lease, ever, on the shipped path (revision 3; §1.6a).
  - **#3** — SQL Server must stay at feature parity throughout, with no carve-out for the query
    surface (revision 11; §4 item 3). The owner's general parity answer, verbatim: *"We can
    prototype functionality on SQLite, and use that to develop the scrum features. But it would
    be best to upgrade the SQL Server library in the same feature checkin. If it lags a bit, we
    can live with that, but we need to keep them as similar as we can, otherwise we cannot test
    reliably."* Asked directly whether that extends to the query layer specifically, the owner
    confirmed: *"Same principle for the query layer too."* This closes revision 7's flag (below,
    #4) that the "lag `IMoneyQuery` on SQL Server" recommendation had gone stale once the query
    surface became the universal read path rather than a reports-only one.
  - **#4** — whole-graph `Load()` does not survive: *"The query-based pattern replaces the
    whole-graph-in-memory pattern everywhere"* (revision 7; §4 item 4). `IMoneyQuery` is now the
    universal read-access pattern, not a reports-specific addition — which §4.1 flagged as leaving
    item #3's "lag the query surface on SQL Server" recommendation stale; **resolved in revision 11
    (see #3, above).**
  - **#5** — XML-as-export-format: **"deferred"** (revision 8). Not in the first slices; §3.2's
    placement behind `IDataFormat` is untouched. §4.1 records that the developer-facing need this
    leaves unserved (keeping a hand-built scenario across a re-pave) is already covered by
    §2.6.2's `Snapshot`/`Restore`, while D-35's disaster-recovery case genuinely is not.
  - **#6** — credential storage: **"later"** (revision 8). The plaintext SQL logins in
    `dataengine.config.json` stay for now, against the panel's recommendation of "now"; deferred
    rather than dismissed, and belongs on the tracker.
  - **#7** — sample data: shipped, customer-reachable, **and** test-database-only (revision 8;
    §1.9). The only one of revision 8's three that needed design rather than recording.
  - **#8** — what ends the nuke-and-pave phase: there is no project-wide trigger to name, because
    `DatabaseEntry.TestDatabase` is a permanent, per-database attribute fixed at creation, and
    "nuke and pave is over" describes a test-reset routine finishing rather than a milestone
    (revision 9; §4 item 8). *"If a db is a test db, it is always a test db... [these reset]
    routines should be idempotent"* — §2.6.8 confirms the reset routines this depends on
    (`ResetSchema`, `ClearAllData`, `ClearTables`, `ClearTable`) are idempotent as designed, with
    one explicit tightening added (`DeleteRow`'s no-op-on-missing-row behavior).
