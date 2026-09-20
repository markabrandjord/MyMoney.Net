# Panel Review 03 — `navigation-ui` and `cross-cutting`

One expert-panel session over eight of the 53 entries in
`redesign-decision-register.md`: the whole of the `navigation-ui` cluster
(**D-5**, **D-6**, **D-7**) and the whole of the `cross-cutting` cluster
(**D-49**, **D-50**, **D-51**, **D-52**, **D-53**).

| | Decision | Recommended | Needs your decision |
|---|---|---|---|
| D-5 | Model for telling the user what it is doing | Two channels: priority/dwell status line + persistent activity log | No |
| D-6 | Compact/density mode | Delete `Compact.xaml`; no app-wide density dimension | No |
| D-7 | Theming contract with a third theme | Validated token contract + "follow the system" as the third mode | No |
| D-49 | Build undo, or remove the menu items | Remove the dead menu items; operation-scoped checkpoint undo, not a journal | **Partly** (scope for v1) |
| D-50 | Does the product back up the user's data | Self-contained data folder + reachable complete backup **and** restore | **Partly** (scheduling) |
| D-51 | In-product help | Coverage as an enforced contract + a visible help affordance | No |
| D-52 | Is printing a capability | One paginated-document path off the existing `FlowDocument` | No |
| D-53 | Shipping/versioning reference data | Embed everything, version it visibly, refresh via the app update channel | No |

**Panel members.** Architect · Developer (C#/.NET/WPF, WPF-UI/Fluent) · Test
Engineer · Operations Engineer · UI/UX Expert · Data Engine Expert (SQLite /
SQL Server) · Adversarial Expert.

**Three corrections the panel made to the register's own framing before
debating.** Each is stated in the relevant section, but they are collected here
because they changed a recommendation:

1. **D-5** — the register describes the status-suppression timers as a blunt
   rate limiter against "far more `ShowMessage` calls than a user could read."
   There are **16 `ShowMessage` call sites in the entire product**. The timers
   are not a rate limiter; they are a *priority* mechanism implemented as a
   wall clock, protecting two specific messages from being overwritten by the
   background stock-quote downloader. That reframing produced a different
   recommendation than "rate-limit the status line."
2. **D-51** — the register reads as "four windows have help." The product
   actually has a **complete 56-page user manual in `docs/` published to
   `moneytools.github.io/MyMoney.Net`**, and F1 is wired at roughly seventeen
   sites (main window, accounts panel with per-account-type keyword switching,
   the quick-filter control, four dialogs, and nine report views set in code).
   The decision is not "ship help or not"; it is "is coverage a contract."
3. **D-52** — the register calls printing "build-from-nothing." Reports are
   already real WPF `FlowDocument`s, and a `FlowDocument` is an
   `IDocumentPaginatorSource` — the *pipeline* is a handful of lines. What is
   genuinely missing is print-shaped **layout** (`FlowDocumentReportWriter`
   sets `doc.MinPageWidth = maxWidth + 100`, i.e. the document is deliberately
   laid out to screen width, and it is themed, so a dark-mode report prints as
   an ink disaster). Pipeline cheap, layout real.

---

## D-5 — What is the product's model for telling the user what it is doing?

**From:** Phase 2 OQ9 · **Cluster:** `navigation-ui`

### ELI10

At the bottom of the window there is a thin strip of text that tells you what
just happened — "loaded your file in 400 milliseconds", "your accounts add up
to £X", "couldn't get today's exchange rates". There is only ever room for one
of these at a time, and a new one instantly wipes out the old one. Nobody keeps
a list of the ones you missed.

That causes a specific problem. When you open your file, the program wants to
tell you two useful things at once — how long it took, and what you're worth.
But at the same moment it also starts quietly fetching today's share prices in
the background, and *that* chatters away: "getting history for MSFT…", "getting
history for AAPL…", one line per company you own. Those background messages
would instantly scribble over the useful ones.

The fix somebody made years ago was a stopwatch: ignore *every* message for the
first three seconds after the program starts, and again for five seconds after
a file finishes loading. It works, in the sense that the important message
survives. But it is a very blunt instrument — during those windows a genuine
error message is thrown away too, and nobody is told.

What's being decided: when the status strip gets redrawn in the new design,
what replaces the stopwatch? If nobody decides, the new design will either copy
the stopwatch without knowing why, or delete it and bring the chatter back.

### What the code actually does

- `IStatusService` has four members (`ShowMessage`, `ShowOutput`,
  `ShowProgress`, `ClearStatus`). `ShowMessage` has **16 call sites** across
  `MyMoney` and `MyMoney.Business`.
- `MainWindow.ShowMessage` drops the message entirely if
  `NativeMethods.TickCount < this.loadTime + 3000`. `InternalShowMessage` drops
  it if `DateTime.Now < this.skipMessagesUntil`. `skipMessagesUntil` is set to
  `now + 5s` at exactly one place: immediately after posting the "Loaded from
  *file* in *N* milliseconds" message.
- The messages being suppressed come overwhelmingly from
  `StockQuoteManager.ShowMainWindowStatus` — "Downloading history for
  *SYMBOL*…", "History updated for *SYMBOL*", plus error text — fired once per
  holding.
- `ShowMessageUIThread` is a single assignment: `this.StatusMessage.Content =
  text`. No severity, no lifetime, no queue, no history.
- A separate output pane exists (`ShowOutput` → `OutputView.AppendText`) and is
  used by import flows. `AnimatedMessage` exists for a message that changes
  after the fact. `MessageBoxEx` is the modal channel.

So the product already has *three* channels (transient line, output pane, modal
box) with no rule about which is which, and the transient one has no concept of
a message mattering more than another.

### Alternatives

**A5-1 — Prioritised transient status line.** Keep one status line; give every
message a severity and a minimum dwell time. A lower-priority message cannot
overwrite a higher-priority one that is still inside its dwell; equal-priority
messages queue briefly rather than stomping. Both wall-clock guards are deleted.

- *Pros.* Smallest possible change to the shape of the product. Fixes the real
  defect (errors being silently discarded inside the suppression window) rather
  than working around it. Testable with an injectable clock. The 16 call sites
  are few enough to classify by hand in an afternoon.
- *Cons.* Still lossy by design — a message you were away from the screen for
  is gone forever. Does not help the user who wants to know *why* the quote
  download failed for one holding out of forty.
- *Unintended consequences.* Dwell times make the status line feel laggy if set
  generously, because a chatty background job now *queues* rather than
  overwriting; the queue can run long after the work finished, so the strip
  reports stale news. Also, once messages have priority, every caller has to
  pick one, and callers in `MyMoney.Business` (the quote manager) now need a
  vocabulary from a UI concern — a small layering leak.

**A5-2 — Two channels: ephemeral status + persistent activity log.** Split
explicitly. An ephemeral line (or a Fluent `Snackbar`) carries *foreground*
results of things the user just did, and is allowed to be lossy. A persistent
activity/notifications surface — a flyout with a badge — receives everything
that is background, long-running, or a failure, and keeps it for the session.
Nothing is ever silently discarded; the timers become unnecessary because
chatter never competes with results.

- *Pros.* Matches the platform convention users already know. The quote
  downloader's forty lines become one collapsible "Updating prices (12 of 40)"
  entry instead of forty overwrites. Errors become findable after the fact,
  which is the single biggest gap today. Removes the layering leak: background
  code reports *to the activity log*, not to "the status bar."
- *Cons.* A new surface to design, build and test. Risk of becoming a place
  errors go to be ignored, if nothing ever draws attention to the badge.
- *Unintended consequences.* An activity log that keeps history is a place
  account names, institution names and symbols accumulate — which quietly makes
  it a screenshot-hazard and interacts with D-41's "what may a shareable
  diagnostic contain." Also, once there is a durable list of failures, users
  will reasonably expect a retry button on each entry, which is a bigger
  commitment than the log itself.

**A5-3 — No global status surface; feedback lives where the work lives.**
Delete the status strip. A long operation shows progress inline in the surface
that owns it; a failure becomes a Fluent `InfoBar` docked in that same surface,
dismissible and persistent until dismissed. The bottom bar carries only durable
state (net worth, unsaved-changes indicator, current file).

- *Pros.* The most modern answer and the most discoverable: feedback appears
  where the user is looking, not in a strip at the far edge of a maximised
  window that nobody reads. An `InfoBar` cannot be silently overwritten, which
  structurally solves the original problem. No priority vocabulary needed.
- *Cons.* Background work that belongs to no surface (quote downloads,
  exchange-rate fetch, the attachment watcher) has nowhere to go — so this
  alternative needs A5-2's activity log anyway, or it just loses those messages
  differently. Retrofitting an `InfoBar` slot into every surface is more edits
  than either alternative above.
- *Unintended consequences.* Per-surface `InfoBar`s that persist until
  dismissed will accumulate and eat vertical space in the register, the one
  surface where vertical space is most contested (see D-6). And the
  net-worth-on-open message (P2-STATUS-1) has no owning surface, so a genuinely
  liked behaviour would need re-siting.

### Panel discussion

**Developer** opened by killing the register's premise: sixteen call sites is
not a storm. Reading `MainWindow.xaml.cs:2119-2121` makes the real intent
obvious — the five-second window starts on the line *immediately after* the
"Loaded from…" message is posted. It exists to protect that one message.

**Test Engineer** made the argument that carried most weight: a suppression
window keyed to `DateTime.Now` and `NativeMethods.TickCount` is untestable in
any honest way. Any test of "does an error appear in the status bar" is either
flaky or has to sleep. Priority-plus-dwell with an injected clock is
deterministic. "Whatever else we pick, the wall clock has to go" was agreed by
everyone.

**UI/UX Expert** argued for A5-3 in principle and A5-2 in practice: the bottom
status strip of a maximised financial app is the least-read pixel on the
screen, and the product currently puts *errors* there. But the quote downloader
and the exchange-rate service genuinely have no owning surface, so a pure A5-3
loses them.

**Architect** flagged that A5-1 pushes a UI vocabulary (severity) down into
`MyMoney.Business`, while A5-2 does the opposite — business code reports facts
("started downloading history for MSFT", "failed: rate limited") and the shell
decides presentation. That asymmetry decided it for him.

**Operations Engineer** noted that all three `ShowMessage` paths already marshal
through `UiDispatcher.BeginInvoke`, so none of these alternatives introduces a
threading problem — but Phase 8's OQ3 finding (`MessageBoxEx.Show` returns
`None` immediately off the UI thread) means the *modal* channel is currently
unreliable from background code, so any design that says "and errors go to a
message box" is building on sand. That is a bug (#52 family), but it constrains
the design: background failures must not require a modal answer.

**Data Engine Expert** weighed in narrowly: save and load timings are the only
user-visible feedback the persistence layer produces, and a SQL Server load can
be genuinely slow. Whatever replaces this must keep a progress affordance for
load/save, not just a completion message.

**Adversarial Expert** pushed back on A5-2: "an activity centre in a
single-user desktop budgeting app" is the kind of thing that gets built and
then gets a badge that is permanently at zero. He proposed the simplest
possible answer nobody had said out loud: **the only messages that must not be
lost are failures.** Successes are pleasant and disposable. So the minimum
viable design is one ephemeral line plus one persistent place failures go — and
that place already exists, because `Logger` writes
`%TEMP%\MyMoney\Logs\` and P2-HELP-5 already offers "open my logs."

That landed. The panel's conclusion absorbed it: A5-2, but the persistent half
is justified *by failures*, not by a general desire to keep history, and it
should be the user-facing front end of the log the product already writes
(which would also fix Phase 8's OQ23 — three background failure paths currently
swallowed into empty `catch` blocks).

### Recommendation

**A5-2, scoped by the adversarial framing.** Two channels:

1. **Ephemeral status** — one line (or Fluent `Snackbar`), lossy by design,
   carrying foreground results only. No wall-clock suppression; instead a
   minimum dwell so a result is not instantly overwritten, and background
   chatter simply does not use this channel at all.
2. **Persistent activity/problems surface** — everything background,
   long-running, or failed. Presented as a flyout with a count, backed by the
   log the product already writes. Background code reports facts; the shell
   decides presentation.

Delete `loadTime + 3000` and `skipMessagesUntil` outright — they become
unnecessary once chatter and results are on different channels. Keep an inline
progress affordance for load/save per the Data Engine Expert's point. Route
Phase 8 OQ23's three swallowed-exception sites into the new problems surface;
that is three lines each and converts three invisible failures into visible
ones.

No unresolved disagreement. The UI/UX Expert's preference for A5-3's per-surface
`InfoBar`s is recorded as a compatible later addition, not a competing answer.

### Needs your decision

**No.** Panel is confident. One sub-question the owner may want to answer
cheaply: whether the existing `OutputView` pane (used by import flows) survives
as a third channel or is folded into the activity surface. The panel leans
"fold it in" but did not treat that as load-bearing.

---

## D-6 — Is there a compact/density mode, and is it a user setting?

**From:** Phase 8 OQ18 · **Cluster:** `navigation-ui`

### ELI10

Every button, box and list row in the program has a size. You can make them all
a bit smaller so more fits on the screen, or leave them roomy so they're easier
to hit and read. Some programs give you a switch for this ("comfortable" /
"compact").

Sitting in this program's folder is a file that would make everything smaller.
It was copied in from a *different* program years ago and then forgotten — no
switch turns it on, no code even opens it. It has been dead weight ever since.

Separately, the transaction list *does* have its own switch, but it does a
different thing: it shows each transaction as one line or as three stacked
lines. That's about how much *information* per row, not how *tall* a button is.
Easy to confuse the two.

What's being decided: does the new design promise a whole-program "make
everything smaller" switch — which means every single screen ever built has to
be checked at both sizes, forever — or is the forgotten file just junk to throw
away?

### What the code actually does

- `Themes/Compact.xaml` is 30 resource entries, headed with the literal comment
  `<!-- from: D:\git\ModernWpf\ModernWpf\DensityStyles\Compact.xaml -->`. Every
  key in it is a **ModernWpf** key: `ControlContentThemeFontSize`,
  `TextControlThemeMinHeight`, `ListViewItemMinHeight`,
  `ComboBoxMinHeight`, `DataGridRowMinHeight`, the `DatePickerFlyoutPresenter*`
  family, and so on.
- Nothing merges it. `AppTheme.SetTheme` is only ever called with
  `"Themes/Dark.xaml"` or `"Themes/Light.xaml"`, hard-coded in
  `MainWindow.OnThemeChanged`. No setting names it.
- The two `IsCompact="True"` hits elsewhere in the codebase are in
  `AttachmentDialog.xaml` and refer to a ModernWpf control property, unrelated
  to this dictionary.
- The register's cross-reference is `P3-REG-3` ("Choose between a compact and a
  detailed row"), driven by `OneLineView` / Ctrl+T. That is *content* density —
  one line vs. three stacked lines showing category and memo. It is orthogonal
  to control metrics.

The decisive fact: **the app is migrating off ModernWpf to WPF-UI.** Every key
in `Compact.xaml` is a ModernWpf resource key. After the migration those keys
resolve to nothing. The file is not "a density mode waiting to be switched on";
it is a vendored fragment of a library being removed.

### Alternatives

**A6-1 — Ship a product-wide density setting.** Own a canonical token set
(spacing, control heights, font sizes, row heights) with Comfortable and
Compact values, expose it in settings, and make it a dimension every surface
must honour.

- *Pros.* Power users with thousands of transactions genuinely want more rows
  per screen. Consistency: one switch, whole app. If it is going to exist at
  all, deciding now is far cheaper than retrofitting.
- *Cons.* WPF-UI does not ship a density dictionary the way ModernWpf/WinUI do,
  so this is a token set the project **owns and maintains forever**, including
  for every third-party control it hosts. Every new surface, forever, must be
  designed and reviewed at two densities.
- *Unintended consequences.* It doubles the visual-regression surface — which
  is not hypothetical here, because this branch already has `VisualGuard`
  visual-invariant tests. Every screenshot baseline becomes two baselines. It
  also interacts badly with Windows' own display scaling and text-size
  accessibility setting: a user at 150% scale who also picks Compact gets a
  combination nobody tested. And a "make everything smaller" control with no
  corresponding "make everything bigger" is an accessibility trap: the only
  direction offered is the one that hurts low-vision users.

**A6-2 — No app-wide density; per-surface content density only.** Delete
`Compact.xaml`. Keep `OneLineView` in the register. Where another surface has a
genuine density need (the holdings list, say), give that surface its own
affordance in its own terms. Chrome metrics come from the WPF-UI defaults,
unmodified.

- *Pros.* Free today, and forecloses nothing, because the deleted file's keys
  are worthless post-migration. Keeps the visual-regression surface
  single-valued. Honours the actual observed user need — "show me more
  transactions" — with the control that already exists and is already bound to
  Ctrl+T.
- *Cons.* Slightly inconsistent by construction: one surface has a density
  toggle and others don't. A user who wants everything tighter has no answer.
- *Unintended consequences.* Accepting WPF-UI's default metrics unmodified
  means the product's information density is now set by an upstream library's
  taste and moves when that library updates — a real, if minor, loss of
  control. Worth pinning the library version deliberately rather than
  floating it.

**A6-3 — Defer to the platform.** No in-app density control at all; honour
Windows display scaling and the system text-size setting properly, and
otherwise leave metrics alone. Treat "I want more on screen" as an OS-level
concern the user already has a control for.

- *Pros.* Philosophically the cleanest, and the only one that helps low-vision
  users as well as density-seekers, because the platform control goes both
  ways. Zero maintenance.
- *Cons.* Windows' text-scaling support in WPF is uneven, and a data grid that
  scales its font without reflowing its columns looks broken. "Honour it
  properly" is not free — it is its own testing commitment.
- *Unintended consequences.* Scales *everything*, including the register's
  column widths, so a user who scales up to read the amounts loses columns off
  the right edge — which the product currently handles by column virtualisation
  and horizontal scroll that few users find.

### Panel discussion

**Developer** settled the factual question in one line: those are ModernWpf
keys, ModernWpf is going away, the file is dead in the new world regardless of
what we decide. Anyone arguing for A6-1 is arguing to *write a new* density
token set, not to switch on an existing one.

**Test Engineer** gave the strongest cost argument. VisualGuard tests landed on
this branch two commits ago. A density dimension multiplies every visual
baseline by two, permanently, and the failure mode of an untested density is
exactly the kind of thing screenshots catch and nothing else does. The cost is
not the tokens; it is the doubled verification surface for the life of the
product.

**UI/UX Expert** conceded the point but was careful to preserve the real user
need underneath it. A personal-finance register is a dense data surface; users
with ten years of history genuinely do want more rows visible. But when he
enumerated what actually consumes vertical space in the register, the answer
was three-line rows, not control padding — and `OneLineView` already fixes
that, with a keyboard shortcut, and it is already remembered across sessions.
A6-1 would be solving a solved problem with a much larger hammer.

**Architect** added that A6-1's real cost is a *contract*: "every surface
honours density" is only true if enforced, and an unenforced density dimension
decays exactly the way `IView.SerializeViewState` decayed (D-8: two of eight
surfaces implement it properly). Introducing a second contract with the same
decay profile, in the same redesign that is trying to fix the first one, is a
bad trade.

**Adversarial Expert** asked who requested this. Nobody. The register's own
wording is "before a redesign invents one" — and the honest answer to "should
we invent this" is usually no. He then argued the *other* side to test the
consensus: if the redesign is going to be Fluent, and Fluent's default metrics
are noticeably roomier than the current ModernWpf ones, the migration may make
the register *less* dense than it is today, and users will notice a regression
they never asked for. That is a real risk and it is not addressed by deleting a
file.

The panel accepted that as a caveat rather than a reason to build A6-1: the
right response to "Fluent's defaults are too roomy for a data grid" is to set
the grid's row metrics deliberately in one place, not to ship a user-facing
density switch. Recorded as a build-time obligation below.

**Operations Engineer** and **Data Engine Expert** both opted out. ("Nothing to
add — this touches no deployment or storage concern.")

### Recommendation

**A6-2.** Delete `Themes/Compact.xaml`. No app-wide density setting, no density
token dimension, no second cross-cutting contract for every surface to honour
and decay against. Keep and extend per-surface content density
(`OneLineView`/Ctrl+T) where a surface genuinely earns it.

With one build-time obligation attached, from the Adversarial Expert's
challenge: **during the WPF-UI migration, set the register's row metrics
explicitly and compare rows-visible-per-screen against the current build.**
Fluent defaults are roomier than ModernWpf's; if the register loses rows, fix
it at the grid, deliberately, in one place. Do not let that regression become
the argument for retrofitting a density mode later.

No disagreement among the seven on the outcome.

### Needs your decision

**No.** Confident, and cheap to revisit — deleting the file forecloses nothing,
because its contents are worthless once ModernWpf is gone.

**Panel flags: would benefit from accessibility expertise.** None of the seven
roles covers low-vision or motor-accessibility requirements properly. The
observation that a one-directional "make it smaller" control is an
accessibility hazard, and the question of how the redesign should honour Windows
text scaling and High Contrast, both deserve someone who does this for a living.
This flag is shared with D-7.

---

## D-7 — How does theming work when a third theme exists?

**From:** Phase 8 OQ19 · **Cluster:** `navigation-ui`

### ELI10

The program comes in a light look and a dark look, and you can switch between
them without restarting. Most of the colours switch correctly because they're
looked up by name each time they're needed.

But some things are drawn by hand in code — charts, the report pages, a few
custom bits. Those grab a colour once and keep it. To make *those* follow the
switch, someone wrote a helper that hands out a special kind of colour that can
be changed later, and keeps a list of every one it handed out so it can go
round updating them when the theme changes.

The trouble is that the helper is opt-in. If a piece of code forgets to ask it
and just grabs a colour directly, that thing stays the wrong colour until you
restart the program — and nothing tells you. There are known places that
deliberately do this. Nobody has a list of the accidental ones.

Worse, and the panel found this while reading the code: if the new look is
missing even *one* colour the helper was asked to track, the update crashes
partway through — and the crash is caught and thrown away. The result is that
the program ends up half in one theme and half in the other, with no error, no
warning, and (in a shipped build) no log entry either.

What's being decided: what the rule is. Should every colour go through the
name-lookup system so this whole class of problem can't happen? Should there be
a checked list of colour names that fails loudly if a theme is missing one?
Or should the program admit it can't fully switch and ask you to restart?

### What the code actually does

`Source/WPF/MyMoney/Utilities/AppTheme.cs` is 110 lines and is the whole
mechanism.

- `GetThemedBrush(name)` looks up a resource, **clones** it into an unfrozen
  `SolidColorBrush`, caches the clone by name and returns it. If the resource
  is missing it throws; if it is not a `SolidColorBrush` it throws.
- `SetTheme(name)` merges the new dictionary, removes the old one, then calls
  `UpdateDynamicBrushes()`, then raises `ThemeChanged`. The entire body is
  wrapped in `try { … } catch { }` with the comment *"Survive not find the theme
  set by the user."*
- `UpdateDynamicBrushes()` iterates the cache:

  ```csharp
  var newBrush = Application.Current.TryFindResource(name) as Brush;
  if (newBrush == null) { Debug.WriteLine($"Dynamic brush '{name}' not found after theme change!"); }
  if (newBrush is SolidColorBrush solid) { brush.Color = solid.Color; }
  else { throw new Exception("Can only theme SolidColorBrush"); }
  ```

**Panel finding, not previously recorded.** The register says
`UpdateDynamicBrushes` "only writes a debug line when a brush named in code is
missing from the incoming theme." It does write that line — and then falls
straight through into the `else` and **throws**, because `null` is not a
`SolidColorBrush`. That exception is swallowed by `SetTheme`'s bare `catch`.
The consequences, in order:

1. The dictionary swap has already happened, and `_theme`/`_name` are already
   updated, so the app believes the theme changed.
2. The brush cache is left **partially** updated — clones iterated before the
   throw have the new colour; the rest keep the old one.
3. `ThemeChanged` **never fires**, so every subscriber that redraws on theme
   change (charts, report views) is never told.
4. `Debug.WriteLine` is compiled out of a Release build, so there is no
   diagnostic at all.

Net effect: one missing colour name in a theme dictionary silently produces a
half-themed application in a shipped build. That is precisely the failure a
third theme would trigger, and it is worse than the register's description.
Recommend appending this to #52 as a distinct defect; the panel did not find it
in the B-list.

Scale: `GetThemedBrush` has 28 call sites across 11 files — `AreaChart`,
`WpfConverters`, `FlowDocumentReportWriter`, `RetirementPlan`,
`ChangeInfoReport`, `InternetExplorer`, `MessageBoxEx`, `AccountsControl`,
`BalanceControl`, `ChangeTracker`, `TransactionsView`. Several of those are
genuinely code-drawing surfaces (charts, flow-document report content) where
eliminating code-side colour entirely is not realistic.

### Alternatives

**A7-1 — Pure dynamic resources; no code-side brushes at all.** Every colour
resolved via `{DynamicResource}` in XAML. `GetThemedBrush` deleted. Code that
must draw (charts, report documents) subscribes to a theme-changed signal and
re-renders from scratch rather than mutating cached brushes.

- *Pros.* Structurally eliminates the whole problem class — there is no cache
  to go stale and no opt-in to forget. WPF's dynamic-resource machinery does
  the work and is well understood.
- *Cons.* "Re-render the chart on theme change" is more work than "change a
  brush's colour," and charts are the most expensive things to re-render. The
  `FlowDocumentReportWriter` case is worse: it builds a document once; making it
  theme-reactive means regenerating the report, which for the portfolio or tax
  report means recomputing it.
- *Unintended consequences.* Forcing re-render on theme change means a theme
  switch can take visible seconds on a large report — and if the app follows the
  system theme automatically (see below), that re-render happens at whatever
  moment Windows decides to switch at sunset. It also quietly forecloses
  "print always uses light colours" (D-52), because if the document is
  theme-reactive there is no longer a stable non-themed rendering to print.

**A7-2 — A validated token contract.** Define one canonical list of colour
tokens. Every theme dictionary must define all of them; every code-side lookup
must name one of them. Enforce with a test that loads each theme dictionary and
asserts every token resolves to a `SolidColorBrush`, plus a test that every
name passed to the themed-brush resolver is in the list. `UpdateDynamicBrushes`
changes from throw-on-surprise to log-and-continue. Keep the resolver, but make
forgetting it detectable.

- *Pros.* Turns "a redesign introducing a third theme would find these one at a
  time" into "a redesign introducing a third theme finds them all at once, at
  test time, before a user ever sees it." Cheap — this is one or two unit tests
  and a list, not infrastructure. Compatible with WPF-UI's own token set, which
  the migration should adopt wholesale rather than hand-maintaining Light/Dark.
  Keeps charts fast (mutate the brush, don't re-render).
- *Cons.* The canonical list is another artefact to keep current; adding a
  colour means touching the list and every theme. That friction is the point,
  but it is friction.
- *Unintended consequences.* A strict "every theme defines every token" rule
  makes a *partial* theme impossible — which sounds fine until someone wants an
  accent-only variant that inherits everything else. Worth designing the list as
  base + overlay from the start rather than discovering it later. Also, the
  test needs to run against the real merged dictionary, which means it is a
  WPF-hosting test (STA thread, `Application.Current`), not a plain unit test —
  slightly more awkward than it sounds.

**A7-3 — Keep best-effort; make restart-on-theme-change explicit.** Accept that
some code-drawn colour will not follow a live switch. Tell the user: changing
theme takes effect on restart.

- *Pros.* Zero work. Honest, in the narrow sense that the product stops
  promising something it doesn't deliver.
- *Cons.* Windows switches light/dark on a schedule by default. An app that
  needs a restart to follow it is visibly behind the platform, and the user
  can't even make it follow — they'd have to restart the app at sunset.
- *Unintended consequences.* "Restart to change theme" means the theme is read
  at startup, which means the *settings* surface where you change it now shows
  a value that doesn't match what you're looking at — a small but constant
  wrongness. And it does nothing about the half-themed-on-missing-token failure,
  which happens at startup too.

### Panel discussion

**Developer** made the migration argument. WPF-UI ships its own theme resource
set and an `ApplicationThemeManager`; the migration is the moment to adopt those
tokens rather than continue hand-maintaining `Light.xaml` and `Dark.xaml`. Doing
that makes a "third theme" almost free in the sense that matters — and clarified
what the third theme should actually *be*.

**UI/UX Expert** answered that directly and changed the shape of the decision:
the third mode users want is not a third palette. It is **"follow the system"** —
auto light/dark. Windows has offered this since 2018 and every modern app
honours it. A hand-authored third colour scheme is a hobby; system-follow is
table stakes. Separately, High Contrast is a *fourth* concern and a real
accessibility requirement, and it is not a palette you author — it is a mode you
get out of the way for.

That reframing is what made A7-3 indefensible: system-follow means the theme can
change while the app is running, at a moment the user didn't choose. Restart is
not an option.

**Test Engineer** costed A7-2 and found it unusually cheap for its value: a
token-coverage test over the theme dictionaries is maybe forty lines, and it
catches exactly the failure the panel just discovered by reading. He also noted
VisualGuard can screenshot each theme, which catches the *half*-themed state
that a token test alone would not. Two cheap tests cover the class.

**Architect** argued against A7-1 on a second-order ground the others hadn't
raised: making every drawn surface theme-reactive couples the chart and report
layers to the theming system permanently, and the report layer has a separate
requirement coming (print, D-52) that wants a *stable, non-themed* rendering.
Those two requirements fight. A7-2 keeps them independent — the document is
built with resolved brushes, and print builds it with the print palette.

**Adversarial Expert** asked whether a validated token contract is gold-plating
for a single-maintainer fork. The Test Engineer's forty-line answer defused it.
He then pressed a better point: A7-2 fixes *detection*, not the underlying
opt-in nature of the resolver. A developer who writes `new SolidColorBrush(
Colors.Red)` in a chart is still invisible to the token test, because they never
named a token. True, and worth stating honestly — A7-2 catches missing and
misnamed tokens, not colour invented from nowhere. The mitigation is a lint or
review convention against literal colour construction outside the palette, not
another mechanism.

**Operations Engineer** noted the deployment angle is nil but the *diagnostic*
angle is not: `Debug.WriteLine` in a Release build is nothing. Any theme
failure should go to `Logger`, which the product already has and already
surfaces via P2-HELP-5.

**Data Engine Expert** opted out. ("No persistence dimension here.")

### Recommendation

**A7-2, with system-follow as the third mode.** Specifically:

1. Adopt WPF-UI's theme token set as the base during migration rather than
   maintaining bespoke Light/Dark dictionaries.
2. Define the canonical token list; enforce with (a) a dictionary-coverage test
   per theme and (b) a test that every code-side token name is in the list.
   Add VisualGuard baselines per theme to catch the half-themed state.
3. Fix `UpdateDynamicBrushes` to log-and-continue via `Logger` instead of
   throwing, and stop `SetTheme` swallowing exceptions silently. **File the
   half-themed finding above as a distinct defect against #52.**
4. The third mode is **Light / Dark / Follow system**, not a third hand-authored
   palette, and it must work live — no restart.
5. Keep a resolver for code-drawn colour (charts, report documents) so those
   stay fast and so the report layer keeps a stable rendering for print (D-52).
6. Convention, not mechanism, against constructing literal colours outside the
   palette — the panel was explicit that A7-2 does not detect this.

No unresolved disagreement.

### Needs your decision

**No.** Panel is confident.

**Panel flags: would benefit from accessibility expertise.** High Contrast mode
is a distinct requirement from light/dark and none of the seven roles can say
authoritatively what the product must do to honour it, nor whether the chosen
palettes meet WCAG contrast ratios — which matters more than usual in an app
whose primary signal is red-vs-black numbers. Shared flag with D-6.

---

## D-49 — Build undo, or remove the menu items?

**From:** Phase 2 OQ1 · **Cluster:** `cross-cutting` · *Defect instances: #52*

### ELI10

Almost every program has Undo — you do something, you regret it, you press
Ctrl+Z and it's as if you never did. This program has Undo and Redo sitting in
its Edit menu, greyed out, always. They have never worked. Nothing behind them
was ever built.

There *are* two safety nets, though, and they're at opposite extremes. If
you're in the middle of typing into a box, Escape puts the box back how it was.
And because the program only writes to your file when you tell it to save, you
can always close without saving and throw away everything since you opened it.

The gap is the middle. If you just deleted a transaction, or told it to
recategorise four hundred transactions at once, or merged two payees together,
there is nothing between "the tiny thing Escape covers" and "throw away your
whole session."

What's being decided: do you build a real Undo — which means the program has to
remember, for every single change, exactly what the thing looked like before,
across a very large and very old piece of machinery — or do you take the greyed-
out menu items away and instead make the few genuinely scary operations
individually reversible?

### What the code actually does

- `MainWindow.OnCommandCanUndo` and `OnCommandCanRedo` both read, in full:
  `// not implemented yet` / `e.CanExecute = false;` / `e.Handled = true;`. The
  execute handlers are empty bodies.
- `Utilities/UndoManager.cs` is a working generic command stack (`CanUndo`,
  `CanRedo`, `Undo()`, `Redo()`, bounded history). It is used by the separate
  `navigator` instance that drives back/forward *navigation*. The `UndoManager
  manager` field created in `MainWindow`'s constructor for editing undo is never
  touched.
- **The decisive technical fact.** The domain's change notification is
  `PersistentObject.OnChanged(string name)` → `FireChangeEvent(this, name,
  ChangeType.Changed)` → `ChangeEventArgs(object item, string name, ChangeType
  type)`. There is **no old value** anywhere in that chain. A change journal
  built on the existing event stream physically cannot reverse a property
  change. Adding old values means changing the signature of the hottest path in
  a ~15k-line domain file and every `OnChanged` override.
- `ChangeTracker` already listens to that stream and maintains a per-type list
  of what changed since load, with navigation links to each change — i.e. the
  product already has *review* of pending changes, just not reversal.
- Save is explicit and dirty-state driven, so "close without saving" is a real
  coarse undo. WPF's grid editing gives per-cell/per-row Escape revert.

### Alternatives

**A49-1 — Full command-journal undo.** Every mutation routes through a command
object with `Do`/`Undo`. Ctrl+Z works everywhere, unbounded within a session.

- *Pros.* The honest, complete answer. Once it exists, it answers D-13
  (live-edit-with-undo becomes a legitimate dialog convention) and reduces the
  need for confirmation dialogs generally, which is the thing D-14 is worried
  about.
- *Cons.* Enormous. Mutations today happen through plain property setters that
  WPF two-way bindings write to directly; intercepting means either routing
  every setter through a command (touching the entire domain file) or capturing
  old values at the property level — which is the same edit. The missing old
  value in `ChangeEventArgs` is not a detail; it is the reason this is a rewrite
  of the model's change plumbing rather than a feature on top of it.
- *Unintended consequences.* The journal must be **invalidated** by anything
  that isn't a user edit: a file load, an OFX download, a CSV import, the
  attachment watcher, and — per D-20 — `CategoryChart.Tally` writing category
  colours as a side effect of *drawing a pie chart*. Miss one and Ctrl+Z offers
  to undo something the user never did, which is worse than no undo. It also
  interacts with persistence: undoing after a save is a compensating write, so
  either the journal truncates at every save (making it much less useful, since
  this app saves often) or undo becomes a persistence operation with all of
  D-34's concurrency questions attached.

**A49-2 — No general undo; remove the menu items; harden destructive
operations.** Delete Edit▸Undo/Redo. Instead: confirm or make individually
reversible the small set of operations that actually hurt — bulk recategorise,
payee rename/merge, category merge/delete, transaction delete (especially with
an attachment, per D-45), and file merge/import.

- *Pros.* Free, immediate and honest. Removes a permanently greyed menu item
  that currently advertises a capability that does not exist. Targets effort
  exactly where the damage is.
- *Cons.* The register is an editing surface and people make mistakes in it
  constantly. "Close without saving" as the only recovery from a mis-edit is a
  terrible answer — it discards an hour of correct work to fix one wrong cell.
- *Unintended consequences.* Pushes the entire burden onto confirmation
  dialogs, which is precisely the modal-fatigue pattern D-14 identifies as
  dangerous (users learn to dismiss confirmations reflexively, at which point
  the confirmation protects nothing). Also explicitly forecloses
  live-edit-with-undo as an answer to D-13, narrowing that panel's options to
  edit-on-copy or honest no-Cancel.

**A49-3 — Operation-scoped checkpoint undo.** No per-field journal. Before any
*bulk or destructive* operation, take a checkpoint; offer "Undo
&lt;operation&gt;" for that operation specifically (a snackbar action, and an
enabled Edit▸Undo when a checkpoint is available). Single-field edits stay
covered by Escape; the session stays covered by close-without-saving.

- *Pros.* Covers the operations that actually cause the "oh no" moment, at a
  fraction of A49-1's cost. Requires no change to the domain's change plumbing —
  the checkpoint is taken at the operation boundary, where the code already
  knows it is about to do something big. Naturally bounded: one checkpoint (or a
  few), not an unbounded journal.
- *Cons.* Doesn't help the mis-typed cell after focus has left it. "Undo" that
  is sometimes available and sometimes not is a slightly confusing affordance if
  presented as a general Ctrl+Z.
- *Unintended consequences.* **This is the important one.** Restoring a snapshot
  replaces object identity — every open view's selection, every binding, every
  `ChangeTracker` entry points at objects that no longer exist. So a checkpoint
  restore is really a *reload*, and a reload that doesn't put the user back
  where they were is its own bad experience. That makes A49-3 quietly dependent
  on D-8 ("is put-me-back-where-I-was a product guarantee"), which is a
  different panel's decision. Naming this dependency was the panel's main
  contribution here.

### Panel discussion

**Developer** put the missing old value on the table first and it reframed
everything. A49-1 is not "add an undo stack"; it is "change how the domain model
reports change," on the hot path, in the file CLAUDE.md describes as a single
~15k-line object graph. Nobody argued with the cost after that.

**Architect** called A49-1 the single largest architectural commitment in the
entire register and said that if it is in scope it *gates* the redesign — you
cannot build it after the fact, because every mutation site has to be built
through it. So this decision must be made before implementation starts, which
is exactly why it is in the register.

**Adversarial Expert** asked the question that shaped the answer: what does a
user actually press Ctrl+Z *for* in a personal-finance app? Two things. "I just
typed in the wrong cell" — Escape covers that during edit, and re-typing covers
it after. And "I just did something bulk and it went everywhere" — which is
catastrophic, unrecoverable, and nothing covers it. Building an unbounded
journal to serve a use case dominated by one of two cases, where the other is
already handled, is building for a user who doesn't exist.

**Operations Engineer** found the cross-link that made A49-3 attractive: a
pre-operation checkpoint is *also* a crash safety net and *also* the mechanism
D-50 needs for backup. One piece of machinery, three jobs. A checkpoint written
to the backup location before a bulk import is exactly what you want to exist
when the import goes wrong, whether or not the user presses undo.

**Data Engine Expert** then cooled that down with the engine-specific reality.
On SQLite a checkpoint is cheap and the code already exists —
`SqliteDatabase.Backup` does a `PRAGMA wal_checkpoint(TRUNCATE)` before copying,
precisely so the copy isn't missing recent transactions. (`VACUUM INTO` would be
the better primitive still, and avoids the delete-first race in B-108.) On SQL
Server there is no local file to copy; a checkpoint would be a server-side
snapshot with its own permissions, and `SqlDatabase.Backup` already does
something alarming there (flips the recovery model, `WITH INIT`). So A49-3's
cost is *engine-dependent*, and the SQL Server story is weak — which loops
straight into D-37 ("is SQL Server a shipped configuration"). If SQL Server is
developer-only, A49-3 is clean. If it is shipped, A49-3 needs a second design
for it or has to degrade to "no undo on server databases," which is an ugly
thing to explain.

**Test Engineer** was blunt about A49-1: undo is famously the feature that
passes every test you write and corrupts state in the case you didn't think of,
because the test matrix is every property times every operation order. A49-3's
test surface is one round trip per checkpointed operation — do the operation,
restore, assert the model matches the pre-operation state — which is a finite,
writable set.

**UI/UX Expert** preferred A49-3 presented as a per-operation affordance ("412
transactions recategorised — **Undo**", in the status/snackbar channel from D-5)
rather than as a general Ctrl+Z, precisely because a sometimes-available Ctrl+Z
is worse than an obviously-scoped button. He also noted that this is a common,
well-understood modern pattern — users recognise it from mail clients.

**Genuine disagreement, recorded.** The **Architect** would prefer **A49-2**.
His argument: A49-3 looks cheap but smuggles in a reload-and-restore-view-state
requirement that belongs to D-8 and has not been decided, and the panel is
choosing a design whose cost depends on another panel's answer. He would rather
remove the menu items, harden the destructive operations with confirmations and
a review step, and revisit undo once D-8 and D-37 have answers. The other six
took the view that the D-8 dependency is real but is a dependency the product
should want anyway (restore-where-you-were is valuable independently), and that
"confirmations only" repeats the D-14 failure mode. This did not fully resolve.

### Recommendation

**A49-3, in two parts, with the Architect dissenting on part two.**

*Part one (unanimous, do now):* **remove Edit▸Undo and Edit▸Redo.** They have
never worked, they are permanently greyed, and they advertise a capability the
product does not have. Delete the unused `UndoManager manager` field with them.
This is the plain-bug half and needs no further debate.

*Part two (six of seven):* build **operation-scoped checkpoint undo** for the
genuinely destructive set — bulk recategorise, payee and category merge/rename,
transaction delete with attachments (D-45), and file import/merge. Present it as
a scoped "Undo &lt;operation&gt;" affordance in the D-5 status channel, not as a
general Ctrl+Z. Reuse the checkpoint as the pre-operation safety copy the
Operations Engineer wants and D-50 needs — one mechanism, three jobs. Use
`VACUUM INTO` on SQLite rather than delete-then-`File.Copy`.

**Do not build a full command journal.** It requires rewriting the domain's
change plumbing to carry old values, it must be invalidated by every non-user
mutation path (including the chart that writes category colours while drawing),
and it collides with the save boundary and D-34's concurrency model.

Two dependencies are explicit and should be honoured rather than worked around:
checkpoint restore is a reload, so it needs **D-8**'s view-state answer; and its
cost on SQL Server needs **D-37**'s answer.

### Needs your decision

**Partly.** Part one is not a decision, it is a cleanup, and the panel is
unanimous.

Part two is **Needs your decision** on *scope and timing*: whether
operation-scoped undo is in the first release or deferred, and which operations
are in the set. That is product taste about how much safety the product owes a
user who is importing a decade of Quicken history — and given this fork's stated
purpose, the owner may weight it much higher than a generic panel would. The
panel is confident about the *shape* (checkpoints, not a journal) and is
genuinely split on whether to commit to it before D-8 and D-37 are answered.

---

## D-50 — Does the product back up the user's data?

**From:** Phase 2 OQ5 + Phase 8 OQ16 · **Cluster:** `cross-cutting` ·
*Defect instances: #52*

### ELI10

Everything you've ever entered — every transaction, every account — lives in one
file on your computer. Next to that file are two folders holding your scanned
receipts and your downloaded bank statements. Those receipts are the only copies
you have.

The program contains a complete, working "make a backup" feature. There is code
for it, and a setting that remembers where your backups should go, and the
setting is saved and loaded like every other setting. But there is **no menu
item, no button and no keyboard shortcut** anywhere that runs it. The feature
cannot be reached. The program faithfully remembers where to put backups you can
never make.

There's a second problem hiding behind the first. Even if you could reach it,
what it copies is *only the one file* — not the receipts, not the statements. So
the thing it would save is the part you could mostly reconstruct from your bank,
and the thing it would leave behind is the part you can't replace at all.

And there is no Restore. The code for that was declared and never written.

What's being decided: does the program take responsibility for protecting your
data — and if so, does that mean everything including the receipts, and does it
happen on its own or only when you ask? Or does it say honestly "that's your
computer's job, here's the folder, back it up yourself" and delete the dead
feature and the setting?

### What the code actually does

- `AppCommands.CommandFileBackup` is declared, has a `CommandBinding` in
  `MainWindow.xaml` (line 44), and a full implementation in `OnCommandBackup`
  with per-flavour file filters (`.dat` / `.sdf` / `.mmdb` / `.xml` / `.bxml`).
  Nothing invokes it. `CommandFileRestore` and `CommandReportBudget` are
  declared in `Commands.cs` and never bound at all.
- `OnCommandBackup`'s own comment: `// todo: threading, progress feedback, &
  confirmation of success.` It calls `TempFilesManager.DeleteFile(fd.FileName)`
  first — because the engines' `Backup` uses `File.Copy` without overwrite —
  then `this.database.Backup(...)`. That delete-first workaround **leaves a
  window in which the previous backup is gone and the new one does not exist
  yet** (B-108).
- Per engine: `SqliteDatabase.Backup` does `PRAGMA wal_checkpoint(TRUNCATE)`
  then `File.Copy` — the checkpoint is correct and deliberate, with a good
  explanatory comment. `SqlDatabase.Backup` (SQL Server) does server-side backup
  and, per B-108, flips the recovery model and uses `WITH INIT`.
  `CsvStore.Backup` shows *"XML Backup is not implemented."*
- `Settings.BackupPath` is a live preference — read, written and migrated like
  any other (Phase 8 OQ16).
- Attachments and statements live in sibling folders beside the data file
  (P8-ATT-2, P8-STMT-3) and are **not** part of any `Backup` implementation.

So: unreachable, incomplete in what it copies, dangerous in its overwrite
handling, and with no restore.

### Alternatives

**A50-1 — Full backup/restore as a product capability.** One archive containing
the data file + attachments + statements + settings. Manual and scheduled (on
close, or daily). Retention of N versions. A real Restore that reopens from an
archive.

- *Pros.* The complete answer, and the only one that protects the irreplaceable
  half. Scheduled backup protects the user who would never remember. Versioned
  retention protects against "I corrupted it three weeks ago and only noticed
  today," which file-level copying does not.
- *Cons.* Substantial: archive format, progress, scheduling, retention policy,
  restore semantics, and a UI for all of it. Backup of a SQL Server database is
  a fundamentally different operation and does not fit the same model.
- *Unintended consequences.* An archive of an entire financial history *plus
  scanned receipts* is a new, highly sensitive artefact sitting wherever the
  user pointed `BackupPath` — possibly a synced cloud folder — with **no
  encryption story**, and the product's existing encryption is the one D-43
  describes as essentially no protection at all. Retention silently consumes
  disk proportional to file size times N, on a file that grows forever.
  Scheduled backup on close makes closing the app slow, which trains users to
  kill it instead.

**A50-2 — Manual, reachable, complete, with restore.** Wire the existing command
to a menu item. Extend it to copy the document folders as well as the data file.
Implement Restore. Use safe primitives (`VACUUM INTO` on SQLite rather than
delete-then-copy). No scheduling, no retention, no archive format — a dated
folder copy.

- *Pros.* Closest to the work already done, and fixes all four current defects
  (unreachable, incomplete, unsafe overwrite, no restore) without inventing a
  subsystem. A dated folder the user can open in Explorer is inspectable and
  needs no tooling to recover from — genuinely valuable for a single-user app.
- *Cons.* Only protects users who remember. No point-in-time versioning beyond
  what the user happens to have made.
- *Unintended consequences.* Restore has to deal with more than files: the
  registry of recent databases, and the attachment/statement path settings,
  which may point somewhere else after a restore into a new location. A restore
  that silently leaves the app pointing at the *old* attachments folder would be
  a quiet data-loss trap of exactly the kind this decision exists to close.

**A50-3 — Hand it to the operating system; make the data self-contained.**
Delete the backup command and `BackupPath`. Instead guarantee that everything —
data file, attachments, statements, settings — lives under **one** folder, tell
the user where it is (P2-FILE-10 already opens it), and document backing that
folder up with File History, OneDrive, or any real backup tool.

- *Pros.* Real backup tools do versioning, offsite copies, scheduling and
  verification enormously better than an app ever will, and the user probably
  already has one. Zero ongoing maintenance. The current layout is *nearly* this
  already — the change is mostly folder discipline plus documentation.
- *Cons.* "My finance app has no backup" reads badly however true the reasoning
  is. Users who have no backup tool now have nothing. There is still no restore
  *path* — the user has to know to copy files back.
- *Unintended consequences.* Actively wrong advice for SQL Server users: "back
  up this folder" does not back up a server database, and a user who follows it
  believes they are protected when they are not. Also, "just put it in OneDrive"
  is a *terrible* idea for a live SQLite file — cloud sync clients corrupt
  open database files, and a product that recommends it will get data-loss
  reports it caused.

### Panel discussion

**Operations Engineer** led and made the framing point: the current state is the
worst of all worlds, because the persistent `BackupPath` preference means the
product is *behaving* as though backups happen. Anything is better than that.

**Data Engine Expert** contributed the most concrete correction. Backup is a
per-engine operation with genuinely different correct answers:

- SQLite: the existing WAL checkpoint is right and the comment explaining it is
  good, but `File.Copy` of a live database is still the wrong primitive.
  `VACUUM INTO` is the online-backup primitive, is atomic, produces a compacted
  file, and removes the need for the delete-first workaround (and B-108's
  window) entirely.
- SQL Server: backup is a server-side operation with its own permissions, its
  own destination (a path on the *server*, not the client), and its own
  retention. Flipping the recovery model on someone's server — which the current
  code does — is a genuinely dangerous thing for a desktop app to do, because it
  changes the transaction-log behaviour of a database that may have other
  policies attached.
- XML / CSV: `Backup` is either a plain file copy or unimplemented.

His conclusion: shipping a backup *capability* only makes sense for the
file-based engines. Server users should be told to use their server's backup.
That is a clean line and it loops into D-35's capability floor and D-37's "is
SQL Server shipped at all."

**Adversarial Expert** made the decisive argument. The risk is not "the user has
no backup." It is "**the user thinks they have one.**" A backup that copies the
data file and silently omits the receipts is *worse than nothing*, because it
manufactures confidence about the exact artefact that can't be recovered. So
whatever is chosen, the completeness of what gets copied is not a nice-to-have —
it is the whole point. He also challenged A50-1's scheduling directly: an app
that backs up on close, to a location the user set once and forgot, is an app
that will eventually fill a disk or quietly write a decade of financial history
into a synced cloud folder.

**UI/UX Expert** proposed the framing that the panel converged on: *your data is
a folder*. One location, visible, openable, containing everything. "Back up now"
makes a dated complete copy of it. That model is understandable without
explanation, needs no archive format, and makes A50-3's external-tool story work
as a *bonus* rather than as the only answer.

**Architect** liked that because it is A50-2 and A50-3 converging rather than
competing: making the data self-contained is valuable regardless of which backup
answer wins, and it is a precondition for either. Do the folder work first; the
backup command becomes almost trivial afterwards.

**Test Engineer** noted that backup-and-restore is one of the most testable
things in the whole register — round trip, compare the object graph, compare the
document folders — but only if restore exists. A backup with no restore is
untestable in the way that matters, because nothing ever proves the backup was
usable. He would make "restore is implemented" a hard requirement rather than a
phase two.

**Developer** costed it: with a self-contained folder and `VACUUM INTO`, backup
and restore together are small. The genuinely fiddly part is restore's
settings/path fixup, per A50-2's unintended consequence.

### Recommendation

**A50-2, built on A50-3's principle.** In order:

1. **Make the data location self-contained** — data file, attachments,
   statements and per-file settings under one folder. This is worth doing on its
   own merits and is a precondition for everything else. P2-FILE-10 already
   opens it.
2. **Wire the existing command to a reachable menu item** and make it copy the
   *whole* folder, not just the data file. The Adversarial Expert's point is
   binding: a partial backup is worse than none.
3. **Implement Restore.** Non-negotiable, per the Test Engineer — a backup
   nothing can restore is unverifiable. Restore must fix up the recent-files
   registry and the attachment/statement paths, or it creates a new silent trap.
4. **Use the engine's safe primitive** — `VACUUM INTO` on SQLite; drop the
   delete-then-copy workaround and close B-108's window.
5. **Scope backup to file-based engines.** Server-database users get a clear
   "use your server's backup" message, not a half-working local copy. Stop
   flipping the recovery model.
6. **Keep `Settings.BackupPath`**, now that it means something, and warn if the
   chosen location is inside a known cloud-sync folder.

Do not build archive formats, retention policies or scheduling in the first
pass.

No unresolved disagreement on this shape.

### Needs your decision

**Partly.** The shape above is panel-confident. **Needs your decision:** whether
**automatic/scheduled backup** is in scope. That is a genuine product-taste call
— it is the difference between a tool that assumes a competent user and one that
assumes a user who will never click Backup, and the panel found honest arguments
both ways. The Adversarial and Operations Engineers are wary of it (disk
consumption, cloud-folder hazard, slow shutdown); the UI/UX Expert thinks a
finance app that loses a year of receipts because nobody clicked a button has
failed regardless of whose fault it technically was.

**Panel flags: would benefit from security expertise.** A complete backup is an
unencrypted archive of a full financial history plus scanned identity-bearing
documents, placed wherever the user points it. D-43 covers file encryption but a
different panel owns it, and none of these seven roles is equipped to threat-
model the backup artefact at rest — particularly the cloud-sync interaction.

---

## D-51 — Does the product ship in-product help?

**From:** Phase 4 OQ19 · **Cluster:** `cross-cutting`

### ELI10

When you're stuck on a screen, pressing F1 is supposed to open the instructions
for *that screen* — not the front page of the manual, the actual page about what
you're looking at.

This program does have a real manual: fifty-six pages covering accounts,
categories, importing, statements, reports, all of it, published as a website.
And F1 does work in a good number of places — the main window, the accounts
panel (which even picks a different page depending on what kind of account
you've selected), the search box, nine report screens and four pop-up windows.

But twenty-five other pop-up windows have nothing attached, so F1 in them lands
on the manual's front page — including the windows for loans, categories and
renaming payees, which are among the ones most likely to confuse someone. And
one screen has the *wrong* page attached: the reports options panel opens the
page about balancing your accounts, because the setting was copied from another
screen and nobody changed it.

There's a subtler problem too: F1 is a key nobody presses any more. There's no
visible "?" anywhere inviting you to look.

What's being decided: is "every screen tells you where its instructions are" a
promise the program keeps and checks — or is it a nice-to-have that will keep
drifting? And if it's a promise, who notices when someone adds a screen and
forgets?

### What the code actually does

The register's "four windows carry a `HelpKeyword`" understates it considerably.

- `HelpService` is an attached property plus an F1 key router that walks up to
  find the nearest keyword. Missing keyword → falls back to `"#_top"`.
  `OpenHelpPage` launches a browser at
  `https://moneytools.github.io/MyMoney.Net/#<keyword>`.
- Keyword sites found: `MainWindow.xaml` (`#_top`), `AccountsControl.xaml`
  (`Accounts/BankAccounts/`) **plus `SetHelpKeywordForSelectedItem` switching it
  per account type**, `QuickFilterControl.xaml` (`Basics/QuickSearch/`), four
  dialogs (`AccountDialog`, `OnlineAccountDialog`, `AttachmentDialog`,
  `SampleDatabaseOptions`), and **nine report views** set in code from
  `MainWindow.xaml.cs` (`Reports/NetworthReport/`, `Reports/TaxReport/`,
  `Reports/InvestmentPortfolio/`, and so on), plus `Basics/Updates/`.
- The manual is **in this repository**: `mkdocs.yml` at the root, 56 markdown
  pages under `docs/` (`docs/Basics/` — 21 pages including `Aliases`,
  `Categories`, `Splits`, `Queries`, `Merging`, `Keyboard`; `docs/Accounts/` —
  17 pages including `Loans`, `SetupAccounts`, `Statements`, `CsvImport`;
  plus `docs/Reports/` and `docs/Charts/`).
- Critically: **topics already exist for most of the uncovered dialogs.**
  `LoanDialog` has no keyword; `docs/Accounts/Loans.md` exists. `CategoryDialog`
  has no keyword; `docs/Basics/Categories.md` exists. `RenamePayeeDialog` has no
  keyword; `docs/Basics/Payees.md` and `Aliases.md` exist.
  `OnlineServiceDialog` has no keyword; `docs/Accounts/OnlineService.md` exists.
- B-48: `ReportsControl`'s keyword is the reconciliation topic, copy-pasted from
  `BalanceControl`.

So the expensive part — writing a manual — is **done**. Closing the coverage gap
is mostly attaching pointers to pages that already exist, not authoring content.

### Alternatives

**A51-1 — Contextual help as an enforced contract.** Every dialog and view
declares a help topic. A test asserts (a) every registered surface has a
non-empty keyword and (b) every keyword resolves to a page that exists in
`docs/`. Topics may be shared — twenty-five dialogs do not need twenty-five new
pages, they need pointers to the twenty that exist. Help becomes a requirement
of the shared dialog base from D-15.

- *Pros.* Cheap, because the content exists and the enforcement is a test. The
  existence check catches the whole class of copy-paste-and-forget that produced
  B-48 (it won't catch a *wrong but real* topic, but it catches typos, renames
  and deletions). Turns a decaying convention into a checked one — the same
  decay D-8 documents for view state and D-15 for dialog conventions.
- *Cons.* Couples the app's test suite to the `docs/` tree being present, which
  is fine today (same repo) and awkward if docs ever move out. Someone owns
  keeping keywords current when pages are renamed — though that is exactly what
  the test enforces.
- *Unintended consequences.* An enforced contract creates pressure to attach
  *some* topic to every surface, and the path of least resistance is attaching a
  vaguely related one. That is how B-48 happened in the first place. The check
  therefore needs to be "the page exists," plus review discipline — the
  mechanism alone does not guarantee relevance.

**A51-2 — Keep best-effort; fix the fallback.** No coverage guarantee. Fix B-48.
Make the F1 fallback smarter: instead of the site's front page, land on the
*section* index for the area the user is in (accounts, reports, importing).

- *Pros.* Cheapest possible improvement, and a smarter fallback genuinely helps
  more than a coverage rule in the surfaces nobody would have written a topic
  for anyway.
- *Cons.* The coverage gap stays and keeps growing with every new surface —
  which is precisely the decay pattern this register exists to stop. "Sometimes
  the right page, sometimes a section index" still trains users not to press F1.
- *Unintended consequences.* A per-area fallback needs a mapping from surface to
  area, which is most of the work of A51-1's keyword coverage without A51-1's
  precision — so it is less of a saving than it appears.

**A51-3 — Drop contextual help; rely on self-explanatory UI.** One
"Documentation" menu item to the site. Delete `HelpService`, `HelpKeyword` and
the F1 router. Invest instead in inline explanation — Fluent `InfoBar`s,
hint text, `HyperlinkButton`s in the surfaces that genuinely need explaining.

- *Pros.* The modern position, and not wrong: help that has to be looked up is a
  symptom of a UI that didn't explain itself. Deletes a whole mechanism.
- *Cons.* Throws away a genuinely good 56-page manual that someone wrote and
  that is still accurate. Some subjects — cost basis, tax lines, OFX
  troubleshooting, reconciliation — cannot be made self-explanatory inline; they
  need prose. Those are exactly the pages that exist.
- *Unintended consequences.* Without a per-surface keyword, the manual becomes
  something the user must navigate from the top, and a manual nobody can enter
  at the right page is only slightly better than no manual. It also removes the
  only structural pressure keeping the docs in sync with the UI at all.

### Panel discussion

**Developer** corrected the register's premise on first reading — this is
seventeen-ish keyword sites and a complete manual, not four dialogs and a hope.
That changed the question from "build or delete" to "guarantee or not," and made
A51-3 look like discarding an asset.

**UI/UX Expert** raised the point that reframed the decision: **F1 is not
discoverable.** Whatever the coverage, a hidden keystroke is not "the product
ships help." Modern Fluent dialogs carry a visible `?` affordance in the title
bar or footer; that is what makes help real. He argued the coverage decision is
secondary to giving help a visible entry point — and that the two together are
cheap because they land in the same place: the shared dialog base from D-15.

**Test Engineer** costed A51-1's enforcement as trivially cheap: enumerate the
dialog/view types, assert a keyword, and check the corresponding `.md` exists
under `docs/`. Since the docs live in the same repository, that is a file-
existence assertion, not a network call. He was careful to state the limit: it
catches missing and dangling topics, not *wrong* ones. B-48's keyword points at
a page that genuinely exists — it's just the wrong page. No mechanism catches
that; review does.

**Operations Engineer** added the operational failure mode nobody had mentioned:
help points at an external site. If that site moves, is renamed, or the Pages
build breaks, **every help link in the product 404s silently** and nothing in
the app knows. A link check in CI is a few lines and closes it. He also noted
this is a network call from a finance app — the URL fragment reveals which
dialog the user is in, which is a trivial privacy leak but a non-zero one, and
the offline user gets nothing at all.

**Adversarial Expert** pressed hardest on that last point: is online-only help
acceptable for a desktop finance app? His own answer was yes — anyone running a
Quicken replacement in 2026 has internet — but he insisted the panel say so
deliberately rather than inherit it, because the alternative (shipping the
markdown and rendering it in-app) is real work that mkdocs-in-repo makes almost
tempting. He also asked the honest question: does *anyone* read this manual? No
one could answer, and nothing measures it. Noted as an unknown rather than
resolved.

**Architect** framed it as the cheap half of D-15: once a shared dialog base
exists and carries owner/centring/taskbar/theme/keyboard, adding "and a help
topic" is one more property with one more coverage test. Deciding D-51 "yes"
costs almost nothing *if* D-15 lands; deciding it "no" saves almost nothing.
That asymmetry settled it.

**Data Engine Expert** opted out. ("No storage dimension.")

### Recommendation

**A51-1, plus a visible affordance.** Specifically:

1. **Coverage as a contract** — every dialog and view declares a help topic,
   enforced by a test that asserts presence and that the target page exists in
   `docs/`. Sharing topics is explicitly fine; this is attaching pointers, not
   writing 25 new pages.
2. **A visible help affordance** in the shared dialog base (D-15) — a `?` in the
   dialog footer or title bar. F1 stays as the accelerator, but stops being the
   only way in.
3. **Fix B-48** and add a CI link check against the published docs site so a
   moved or renamed page fails a build rather than failing a user.
4. **Keep help online-hosted** — deliberately, not by inheritance. The manual
   lives in the repo and publishes via mkdocs; that is a good arrangement and
   in-app rendering is not worth the work.
5. Note that item 1 is an explicit input to **D-15** — "is a help topic on the
   shared base's required list" is asked there and answered **yes** here.

No unresolved disagreement.

### Needs your decision

**No.** Panel is confident. One honest unknown recorded rather than resolved:
nobody knows whether the manual is read, and nothing measures it. If the owner
has a view that documentation is not worth maintaining, that view would change
this answer to A51-3 — but it would be a decision about the docs, not about the
help wiring.

---

## D-52 — Is printing a capability of this product?

**From:** Phase 5 OQ6 + Phase 8 OQ20 · **Cluster:** `cross-cutting`

### ELI10

You can look at your reports on screen — net worth, tax summary, what you own,
cash flow. What you can't do is print one, or save one as a proper document to
email to your accountant. There is no Print anywhere in the program except in
the window that shows a scanned receipt.

There *is* an "export to a web page" option for two of the nine reports, but
what it produces is broken: the styling is fetched from the internet so the file
looks plain when opened offline, every chart is simply missing, and the indented
detail rows come out shifted into the wrong columns. Nobody has used it
successfully in a long time, if ever.

Here's the thing the panel found that changes the shape of this: the reports are
*already* built as proper documents internally — the same kind of document
Windows knows how to split into pages and send to a printer. So making printing
work is much less effort than it first appears. What's genuinely missing is that
the document is laid out to fit your *screen*, not a sheet of paper, and it's
coloured for whichever theme you're using — so printing it today would produce a
wide, dark, ink-soaked mess.

What's being decided: does the program learn to produce a proper paged document
— which covers both printing and "save me a PDF" — or is printing declared out
of scope and effort put somewhere else?

### What the code actually does

- The only `PrintDialog` in the product is `AttachmentDialog.xaml.cs:819`.
- **Reports are real `FlowDocument`s.** `FlowDocumentReportWriter` takes a
  `FlowDocument` and builds paragraphs, tables and hyperlinks into it;
  `FlowDocumentView` hosts it in a `HandyFlowDocumentScrollViewer` (a
  `FlowDocumentScrollViewer` subclass that only customises mouse-wheel scroll
  speed). All nine reports go through this path.
- A `FlowDocument` is an `IDocumentPaginatorSource`. `PrintDialog.PrintDocument(
  ((IDocumentPaginatorSource)doc).DocumentPaginator, "…")` is the whole
  pipeline. `FlowDocumentScrollViewer` also carries a built-in print command, so
  Ctrl+P with focus in the document probably already does *something* today —
  untested and unadvertised, and almost certainly producing the bad output
  described below.
- **The layout is screen-shaped, deliberately.** `FlowDocumentReportWriter`
  sets `this.doc.MinPageWidth = this.maxWidth + 100`, with a comment explaining
  that this stops the viewer squeezing columns — and `// 'Auto' sized columns in
  FlowDocument tables suck.` So the document is explicitly as wide as its widest
  content, which is exactly wrong for a printed page.
- **The content is themed.** `FlowDocumentReportWriter` calls
  `AppTheme.Instance.GetThemedBrush("HyperlinkForeground")`, and the report
  content generally resolves themed colours. A dark-theme report printed as-is
  is an ink disaster.
- The competing output path, `HtmlDocumentReportWriter`, is the one D-17
  describes: CDN-linked Bootstrap, `WriteElement` a no-op so every chart is
  absent, `WriteHyperlink` emitting a bare `span`, expandable groups no-ops so
  detail rows shift a column left. It has rotted.

### Alternatives

**A52-1 — A full print pipeline.** Page setup (size, orientation, margins),
print preview, a print-specific rendering with headers/footers carrying report
title, date and page numbers, and proper pagination.

- *Pros.* The complete answer, and the one that makes a tax packet genuinely
  presentable. Print preview is what users expect before committing ink.
- *Cons.* Page setup and preview are a real amount of UI surface to design,
  build and test, and every report added afterwards must be print-tested
  forever.
- *Unintended consequences.* Forces the report-authoring layer to stop assuming
  screen width — the `MinPageWidth` hack and the hand-managed column widths both
  have to go, which means touching column-width logic in every report. That is a
  larger blast radius than "add printing" implies. It also creates a second
  layout mode that every future report must satisfy, which is a new cross-
  cutting contract of exactly the kind D-8 and D-15 show this codebase decaying
  against.

**A52-2 — PDF-first; skip printing.** Add "Export to PDF." Let the OS handle
printing from there. This also answers D-17's "what is a shareable report."

- *Pros.* A PDF is what the user actually wants to hand to an accountant.
  One artefact, self-contained, no CDN, portable.
- *Cons.* WPF has no built-in PDF writer. Two routes, both bad: drive the
  "Microsoft Print to PDF" queue (which needs the paginator anyway — so it *is*
  A52-1 wearing a hat), or take a third-party PDF dependency (QuestPDF, PdfSharp)
  and write a **second rendering path**.
- *Unintended consequences.* **This is the strongest argument against it.** The
  product already has two report writers and the second one bit-rotted into
  uselessness precisely because it was a parallel path nobody exercised —
  `WriteElement` silently a no-op, groups silently no-ops, output silently
  wrong. Adding a third rendering path repeats that mistake exactly. A PDF
  library also pulls a licensing question into a project that currently has
  none of consequence.

**A52-3 — Minimal honest printing off the existing document.** Wire Print to the
existing `FlowDocument` paginator. Force a light "print palette" during
pagination so output is ink-sane. Set a fixed page width and let the existing
table code reflow to it. No page setup, no preview, no headers/footers in pass
one.

- *Pros.* Genuinely small — the pipeline is a handful of lines because the
  document already exists. Immediately gives the product a capability it
  conspicuously lacks. No new rendering path, no dependency, no second layout
  contract.
- *Cons.* Output is serviceable, not polished: no page numbers, no report title
  on page 2, no control over orientation. Wide reports (portfolio, cash flow)
  will want landscape and won't get it.
- *Unintended consequences.* "Print produces something mediocre" sets an
  expectation that is then hard to walk back; users will immediately ask for
  page setup, and the honest answer is that pass one deliberately omitted it.
  Also, forcing a print palette means the report content must be renderable in
  two palettes, which pushes back on D-7 — it is a reason to keep a *resolver*
  for code-drawn colour rather than making the document itself theme-reactive.

### Panel discussion

**Developer** opened with the correction: the register calls this
build-from-nothing, but reports are already `FlowDocument`s and a `FlowDocument`
paginates itself. The pipeline is nearly free. What is not free is that the
document is deliberately laid out to screen width — and he pointed at the
`MinPageWidth = maxWidth + 100` line and its neighbouring comment about auto-
sized columns as evidence that report column widths are a known sore spot
already.

**Architect** made the argument that dominated the session. This product has a
**report-writer divergence problem**, empirically: `IReportWriter` has two
implementations, the second one silently does nothing for charts, hyperlinks and
groups, and nobody noticed until a nine-phase audit read it. Any alternative
that adds a *third* implementation is repeating a mistake the codebase has
already demonstrated it cannot maintain. That argument eliminated A52-2's
library route outright.

**UI/UX Expert** argued that the real deliverable is a **file**, not paper —
what an accountant wants is a PDF attachment. But he accepted the Developer's
observation that "Microsoft Print to PDF" is a printer on essentially every
Windows machine, so a print pipeline *produces* PDFs for free through the
standard print dialog. The user experience is one extra dropdown selection, not
a missing feature.

**Test Engineer** contributed the most useful non-obvious point: **printing is
testable without a printer.** Paginate the document to an in-memory
`XpsDocument` and assert page count, that no content is clipped, and that the
palette used is the print palette. That runs on a build agent with no printer
installed. This removed the "printing can't be tested so it will rot" objection
that had been the main reason to avoid it.

**Operations Engineer** confirmed no deployment dimension — printing is a
per-machine concern — and endorsed the XPS-paginate approach for CI. He added
one caution: a print path that forces a palette must not mutate the *live*
document, or the on-screen report will flash or stay wrong after printing. Build
a second document for print, or push/pop the palette.

**Adversarial Expert** asked whether anyone prints in 2026. His own answer, on
reflection, was that it doesn't matter — the correct framing is "**produce a
paginated document**," and printing is one destination for it, saving is
another. That reframing is what unified the three alternatives: they are not
print-vs-PDF, they are one paginated-document capability with two outputs. He
then pressed on scope creep: page setup, preview, headers and footers are each
individually reasonable and collectively a project. Pass one should be
deliberately small.

**Data Engine Expert** opted out. ("Nothing to add — no persistence dimension.")

The panel did note honestly that a **one-click** "Save as PDF" — no print dialog
— is *not* available without either a library or a print-queue trick, and did
not pretend otherwise. That is a separate, later call, and it should be made
together with D-17 rather than separately.

### Recommendation

**A52-3, designed so it can grow into A52-1, explicitly not A52-2.**

1. **One paginated-document path**, off the existing `FlowDocument`. Expose it
   as Print (standard `PrintDialog`), from which "Microsoft Print to PDF" gives
   users a PDF today with no new dependency.
2. **A print palette** — force light, ink-sane colours during pagination, on a
   document built for print rather than by mutating the live one. This is an
   input to D-7: keep a colour *resolver* so the report layer can render in two
   palettes.
3. **Fix the layout assumption** — stop setting `MinPageWidth` to content width
   in the print rendering; let tables reflow to the page.
4. **Add the XPS-pagination test** from the start, so this capability is
   verified on a printerless build agent and does not rot the way
   `HtmlDocumentReportWriter` did.
5. **Do not add a third report writer** and do not take a PDF library
   dependency. Coordinate with **D-17**: the panel's view is that the right
   resolution of "what is a shareable report" is *this* document path, and that
   `HtmlDocumentReportWriter` should be retired rather than repaired — but D-17
   belongs to another panel and this is input, not a ruling.
6. Defer page setup, preview and page headers/footers to a second pass, driven
   by actual requests.

No unresolved disagreement on the recommendation. The UI/UX Expert's preference
for a one-click PDF export is recorded as a *later* item contingent on D-17, not
as dissent.

### Needs your decision

**No.** Panel is confident on the mechanism. Flagged for the owner's awareness
rather than decision: if one-click "Save as PDF" (no print dialog) is a
requirement rather than a nice-to-have, that changes the calculus and should be
decided jointly with D-17, because it means accepting either a PDF library or a
print-queue automation.

---

## D-53 — How does the product ship, version and refresh its reference data?

**From:** Phase 7 OQ18 · **Cluster:** `cross-cutting`

### ELI10

Some of what the program knows isn't yours — it's built-in background knowledge.
The tax brackets for each state. The list of tax-form line numbers. The
directory of which bank lives at which internet address for downloading
statements. None of that is your data; all of it goes out of date.

Right now these things arrive in two different ways, and the difference is an
accident rather than a decision. Most of them are **baked inside the program**,
so they can never go missing. But the two tax files are **loose files sitting
next to the program**, loaded with no check that they're actually there — so if
anything goes wrong with the install, the program looks for them, doesn't find
them, and falls over rather than saying "my tax data is missing."

None of them say how old they are, either. Nothing on screen tells you the tax
brackets are from a particular year, so you can't tell whether to trust the
estimate.

What's being decided: one way of delivering this background knowledge instead of
two — and whether it can be refreshed without installing a new version of the
program, which means someone has to run a service that hands out updates, and
you have to trust whatever it hands you.

### What the code actually does

The csproj already votes, six to two, for embedding.

`Source/WPF/MyMoney.Business/MyMoney.Business.csproj`:

- `EmbeddedResource`: `Taxes\TxfSpec.txt`, `Ofx\ofx160.dtd`, `Ofx\ofx201.dtd`,
  `Ofx\MfaPhrases.xml`, `Ofx\OfxProviderList.xml`, `Ofx\OfxErrorTemplate.htm`.
- `Content` + `CopyToOutputDirectory=Always`: `Taxes\FederalTaxes.json`,
  `Taxes\StateTaxes.json` — and *only* those two.

`FederalTaxes.Load()` and `StateTaxes.Load()` both do:

```csharp
var location = typeof(FederalTaxes).Assembly.Location;
var path = Path.GetDirectoryName(location);
var fileName = Path.Combine(path, "Taxes", "FederalTaxes.json");
var json = File.ReadAllText(fileName);
```

No existence check, no try/catch. Under single-file publishing `Assembly.Location`
is the empty string, and `Path.GetDirectoryName("")` throws `ArgumentException`
— a latent packaging landmine. The product currently ships ClickOnce/MSIX/winget
(see `Source/WPF/publish.cmd`), so it is latent rather than live, but it fires
the day anyone tries single-file.

**And the codebase already contains the full sophisticated pattern**, in
`OfxInstitutionInfo`:

- Embedded seed: `GetCachedBankList()` reads
  `Walkabout.Ofx.OfxProviderList.xml` from the assembly's manifest resources
  when no cache exists.
- Local cache: `%LOCALAPPDATA%\MyMoney\ofx-index.xml`, created on demand.
- Network refresh: `http://www.ofxhome.com/api.php?all=yes`, merged into the
  cache and saved — and this is the plain-HTTP, credential-endpoint-supplying
  path that D-42 is about.
- User-editable, per the class comment.

So "embedded baseline → local cache → optional refresh" is not a design to
invent. It exists, in this codebase, and it is the one dataset that genuinely
needs refreshing between releases.

Finally, the app has a **working update channel**: `CheckLastVersion` /
`ChangeListRequest` / `ChangeInfoReport` (P2-STATUS-7) plus winget and ClickOnce.
Shipping data with the build is not a dead end here.

### Alternatives

**A53-1 — Everything embedded, versioned with the build.** All reference data as
`EmbeddedResource` with an explicit version and as-of date inside each payload.
Refresh means updating the app.

- *Pros.* One mechanism. Nothing can go missing, nothing can be tampered with in
  transit, and the single-file landmine disappears because there is no file path
  to resolve. Trivially testable — assert every dataset loads and parses at
  startup. Matches what the csproj already mostly does.
- *Cons.* A tax-table correction, or a bank changing its OFX endpoint, requires
  a release. Tax data changes annually — which is fine, since a January release
  is natural — but an institution endpoint changing is *not* annual and
  genuinely does strand users.
- *Unintended consequences.* Users on an old version silently compute with old
  tax brackets and never know, because nothing surfaces the vintage. Embedding
  alone does not fix the staleness problem; it only fixes delivery. Also,
  embedding the institution directory makes it read-only, which contradicts the
  existing "user can edit it" behaviour.

**A53-2 — Embedded baseline + refreshable cache with visible vintage.**
Generalise the `OfxInstitutionInfo` pattern to every dataset: embedded fallback
that always works offline, a cache under `LocalApplicationData`, a declared
version and as-of date surfaced in the UI, and an HTTPS refresh.

- *Pros.* Current data without a release. Offline-safe. The vintage is visible,
  which is the thing the user actually needs to judge whether to trust a tax
  estimate.
- *Cons.* Someone must run and maintain an update endpoint **forever**. For a
  single-maintainer fork that is the component most likely to rot into a 404 —
  the same risk the Operations Engineer raised for the docs site in D-51.
- *Unintended consequences.* A refresh channel is a **supply chain into a
  finance app**, and this codebase's one existing example is the cautionary tale:
  D-42 documents that the institution directory is fetched over plain HTTP and
  supplies the URL the product subsequently posts banking credentials to. Doing
  this for *more* datasets without first fixing the trust story multiplies that
  exposure. Downloadable tax brackets are a particularly attractive tampering
  target for anyone who can MITM a user.

**A53-3 — Embedded, with explicit staleness disclosure and no refresh service.**
A53-1 plus: every dataset declares its vintage, the UI states it where the data
is used ("Tax rules: 2026 edition"), and past a threshold the product warns and
points at the existing app-update mechanism.

- *Pros.* All of A53-1's simplicity, plus the thing that actually protects the
  user — knowing how old the numbers are. No service to run, no new trust
  boundary. Uses an update channel that already exists and is already
  maintained.
- *Cons.* Still cannot fix a bank endpoint without a release.
- *Unintended consequences.* A staleness warning is only honest if releases
  actually happen on the expected cadence; if the fork goes quiet, the product
  spends its life displaying a warning it cannot act on, which trains users to
  ignore it. That is a commitment, not just a feature.

### Panel discussion

**Architect** started from the csproj: the project has already effectively
decided, six embedded to two loose, and the two loose ones are the outliers with
no fallback and a latent packaging bug. Converging on embedded is ratifying the
dominant convention, not imposing a new one.

**Developer** costed it at two csproj lines and a reader change, and made the
extra point that embedding removes the `Assembly.Location` landmine *entirely*
rather than guarding it — there is no path to resolve, so single-file publishing
becomes a non-issue rather than a handled case.

**Operations Engineer** argued the decisive point against A53-2: **the update
channel already exists and is already maintained.** The app ships via winget,
ClickOnce and MSIX, checks for new versions, and shows a changelog. Standing up
a *second* distribution channel, specifically for reference data, to serve a
project that already has a working one, is adding an operational commitment for
marginal benefit. The dataset whose update cadence genuinely doesn't fit a
release cycle is the institution directory — and that one already has its own
refresh, so it needs fixing (D-42), not generalising.

**Data Engine Expert** made a distinction the register blurs, and it improved
the recommendation. Phase 7's OQ25 lumps "the stock-quote and split caches" in
with reference data. They are not the same thing. Quote history and split caches
are **derived user data** — per-user, per-portfolio, grown over time, and
genuinely valuable to keep. They belong with the user's data folder (and
therefore inside D-50's backup scope), not in the "shipped reference data"
bucket. Treating them as reference data would mean they get wiped on an app
update, which would silently destroy years of accumulated price history. That is
a real trap and he was firm about it.

**Test Engineer** compared verification cost. An embedded dataset is verified by
one startup test: it loads, it parses, its declared version is present. A
downloadable one needs network mocking, cache-invalidation tests, a staleness
clock, and a corrupt-download path. Five times the test surface for a project
whose current tax-data test already ratifies a known defect (D-31).

**Adversarial Expert** aimed at the question underneath. Delivery is the *easy*
half. The hard question is **what the tax data claims**, which is D-29's and
D-30's territory — and if the honest answer there is "estimate only, not tax
advice," then annual embedded refresh is more than sufficient and building an
update service would be solving the wrong problem expensively. Conversely, if
the product claims accuracy, no delivery mechanism rescues it, because the
brackets are only one of several things D-30 shows are wrong (married-filing-
separately mapped to Single brackets, head-of-household mapped to Single at
state level). Delivery should be the cheap option and the *claim* should be
lowered. He also noted that the one thing all three alternatives agree on —
**make the vintage visible** — is the highest-value, lowest-cost item in the
whole decision, and it is independent of delivery.

**UI/UX Expert** agreed and added where it belongs: not buried in an About box.
The vintage should sit next to the number it qualifies — on the tax summary and
estimate surfaces themselves, as a quiet caption.

### Recommendation

**A53-3.** Specifically:

1. **Embed all reference data.** Convert `FederalTaxes.json` and
   `StateTaxes.json` to `EmbeddedResource` alongside `TxfSpec.txt` and the OFX
   resources; read them from manifest resources. Removes two loose files, two
   unguarded `File.ReadAllText` calls and the single-file `Assembly.Location`
   landmine in one change.
2. **Every dataset declares a version and an as-of date** inside its payload.
   `StateTaxes.json` already does this ("2026 effective rates", version
   2026.05.25); `FederalTaxes.json` does not, and `TxfSpec.txt` is version 041
   dated 6/16/06.
3. **Surface the vintage where the data is used** — a caption on the tax
   estimate and summary surfaces, not in About. Highest value per unit of effort
   in this decision, and independent of everything else here.
4. **The app update channel is the refresh channel.** It exists, it is
   maintained, and it already reports what changed. Do not stand up a second
   distribution channel.
5. **One exception, narrowly scoped: the institution directory.** It genuinely
   changes between releases and already has a cache-and-refresh design. Keep it,
   but fix it per **D-42** — HTTPS, and explicit user confirmation before a
   changed endpoint is used to post credentials. Do not generalise this pattern
   to other datasets until that trust story exists.
6. **Do not classify the stock-quote history and split caches as reference
   data.** They are derived user data; they belong in the user's data folder and
   inside **D-50**'s backup scope. Misclassifying them risks wiping years of
   accumulated price history on an app update.

**Dependency, stated rather than resolved:** **D-29** decides what the tax data
*claims*. This recommendation does not change based on that answer — embedding
and a visible vintage are right either way — but if D-29 lands on "tax-year-
versioned with an update path," item 4 would need revisiting, and the panel's
view is that the app update channel *is* that path.

No unresolved disagreement.

### Needs your decision

**No.** Panel is confident on delivery. The genuinely owner-level question in
this area — what the tax data claims and whether it should ship at all — is
**D-29**'s and **D-30**'s, not this one.

**Panel flags: would benefit from tax/regulatory expertise** — but that flag
belongs to D-29, where the question is what the product may claim, not here,
where the question is only how bytes reach the user.

---

## Appendix — panel bookkeeping

### Expert opt-outs

| Decision | Opted out | Stated reason |
|---|---|---|
| D-6 | Operations Engineer, Data Engine Expert | No deployment or storage dimension |
| D-7 | Data Engine Expert | No persistence dimension |
| D-51 | Data Engine Expert | No storage dimension |
| D-52 | Data Engine Expert | No persistence dimension |

The Data Engine Expert weighed in substantively on **D-49** (checkpoint cost is
engine-dependent; SQL Server has no local snapshot), **D-50** (`VACUUM INTO` vs.
`File.Copy`; server-side backup is a different operation; the recovery-model
flip is dangerous), **D-53** (quote and split caches are user data, not
reference data), and lightly on **D-5** (load/save progress must survive the
redesign).

### "Would benefit from additional expertise" flags

- **Accessibility expertise** — **D-6** and **D-7**. One-directional density
  controls, Windows text-scaling support, High Contrast mode, and WCAG contrast
  ratios in an app whose primary signal is coloured numbers. None of the seven
  roles can answer authoritatively.
- **Security expertise** — **D-50**. A complete backup is an unencrypted archive
  of a full financial history plus scanned identity documents, written wherever
  the user points it, possibly into a cloud-sync folder. D-43 covers file
  encryption but is another panel's; nobody here can threat-model the artefact
  at rest.
- **Tax/regulatory expertise** — noted in passing on **D-53**, but the flag
  properly belongs to **D-29**/**D-30**.

### Defects surfaced by this panel that do not appear to be in #52–#58

1. **`AppTheme.UpdateDynamicBrushes` throws on a missing themed brush, and the
   throw is silently swallowed, leaving a half-themed application.** A `null`
   from `TryFindResource` writes a `Debug.WriteLine` (compiled out in Release)
   and then falls through to `else { throw … }`, which `SetTheme`'s bare
   `catch {}` absorbs — after the dictionary swap has already committed, with
   the brush cache partially updated and `ThemeChanged` never fired. See D-7.
   Recommend appending to #52.
2. **`OnCommandBackup`'s delete-then-copy workaround leaves a window with no
   backup at all** — related to but distinct from B-108's "`File.Copy` with no
   overwrite" framing: the *caller's* workaround, not the engine's method, is
   what creates the gap. See D-50.

### Cross-panel dependencies raised

| This decision | Depends on / feeds | Nature |
|---|---|---|
| D-5 | D-41 | An activity log that keeps history accumulates account and institution names |
| D-7 | D-52 | Keeping a colour *resolver* (not a theme-reactive document) is what lets reports render in a print palette |
| D-49 | D-8, D-13, D-14, D-37, D-50 | Checkpoint restore is a reload (D-8); undo's absence narrows D-13; confirmation-only invites D-14's fatigue; SQL Server cost depends on D-37; the checkpoint doubles as D-50's pre-operation copy |
| D-50 | D-35, D-37, D-43, D-45 | Backup is engine-dependent (D-35/D-37); the archive is unencrypted (D-43); deleted attachments are part of what's at risk (D-45) |
| D-51 | D-15 | "Has a help topic" is answered **yes** here as an input to D-15's shared-base contract |
| D-52 | D-17, D-7 | The panel's view is that this document path *is* the shareable-report answer and `HtmlDocumentReportWriter` should be retired — input to D-17, not a ruling |
| D-53 | D-29, D-42, D-50 | D-29 decides the claim; D-42 must be fixed before generalising any refresh channel; quote/split caches belong in D-50's backup scope |
