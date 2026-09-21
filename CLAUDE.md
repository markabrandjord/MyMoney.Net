# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope of this file

Build/test/contribution steps already live in `docs/dev/index.md` — don't
duplicate them here, link to them. This file covers things that aren't
obvious from the docs or from reading the code: the fork workflow specific to
this checkout, and a running log of non-obvious gotchas as they're discovered.

## What this is

This is `markabrandjord`'s fork of [MoneyTools/MyMoney.Net](https://github.com/MoneyTools/MyMoney.Net),
a WPF personal-finance app (C#, .NET 10.0). Being evaluated as a Quicken
Classic replacement by importing Quicken data into it; if it doesn't work out
this checkout gets deleted, otherwise the migration becomes permanent.

## Git remotes and workflow

- `origin` → `markabrandjord/MyMoney.Net` (your fork) — fetch & push
- `upstream` → `MoneyTools/MyMoney.Net` (original project) — fetch only, push is disabled
- `master` tracks `origin/master`

Sync fork with upstream:
```
git checkout master
git fetch upstream
git merge upstream/master
git push origin master
```

New work branches off `master`. To contribute back, push the branch to
`origin` and open a PR against `MoneyTools/MyMoney.Net:master` (`gh pr create`).

## Build / test / run

See `docs/dev/index.md` for the full walkthrough (Visual Studio, DGML
scenario tests, project dependency diagram). Quick reference:

- Solution: `Source/WPF/MyMoney.sln` (main app + tests); `Source/WPF/MyMoneyPackage.sln` is the ClickOnce packaging project.
- Build: `dotnet build Source/WPF/MyMoney.sln`
- Unit tests (NUnit, in `Source/WPF/UnitTests`): `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
- Run every test project in the solution (e.g. also picks up `Source/WPF/MyMoney.TestSupport`): `dotnet test Source/WPF/MyMoney.sln`
- Single test: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=TestMethodName"`
- The `ScenarioTest` project is a separate model-based integration test driven by `TestModel.dgml`, not run via `dotnet test` — see the docs for the DGML Test Monitor tooling it needs.
- Windows-only (WPF + WinForms); build/run requires Windows.

## Architecture

- Root namespace `Walkabout`. Main app project: `Source/WPF/MyMoney`.
- `Database/Money.cs` is the core domain model — a single ~15k-line file
  defining the object graph (`MyMoney`, `Accounts`/`Account`, `Transactions`/`Transaction`,
  `Categories`/`Category`, `Securities`, `Payees`, `Splits`, `RentBuildings`, etc.).
  Every persistent type derives from `PersistentObject`, and collections derive
  from `PersistentContainer`, which is what drives change tracking
  (`ChangeType`: Inserted/Changed/Deleted/...) and dirty-state/save logic.
- Storage is pluggable behind `IDatabase` (`Database/IDatabase.cs`): implementations
  include `SqliteDatabase`, `SqlDatabase`, `SqlCeDatabase`, `XmlStore`, `CsvStore`.
  SQLite is the default local file format.
- `Importers/` and `Ofx/` handle bringing in external data (OFX/QFX, CSV, QIF-style
  imports) — relevant to the Quicken-conversion evaluation this fork exists for.
- `Reports/`, `Charts/`, `Taxes/` are feature areas built on top of the core model.
- **There are now TWO WPF applications in `Source/WPF`**, both in the same solution and
  both shipped: the legacy `MyMoney` project described above, and `MyMoney.Shell` — the
  UI/UX redesign's vertical slice (WPF-UI/Fluent chrome + MVVM, `MyMoney.Shell` root
  namespace, its own `MyMoney.Tests.Shell` unit tests and `UITests/Shell` FlaUI tests). It
  runs *alongside* the legacy app rather than replacing it, has its own `MainWindow`, and
  talks to `IMoneyStore`/`IMoneyQuery` (currently over a throwaway in-memory SQLite database
  opened in its `App.OnStartup`) instead of `Database/Money.cs`'s object graph. A change to
  one app is not automatically a change to the other, and "the app" in an older note or issue
  almost always means the legacy one.
- Other source trees are separate/experimental UI ports, not part of the main
  app: `Source/Xamarin` (legacy mobile) and `Source/Uno` (in-progress Uno
  Platform port). Don't assume changes to `Source/WPF/MyMoney` need mirroring there.

## Things that have been gotten wrong before

Durable domain/architecture knowledge only below - narrow FlaUI test-authoring
mechanics (element-finding, shared-session lifecycle, input sequences) discovered
during the 2026-09-18 Basics test implementation plan moved to
`docs/dev/flaui-basics-test-notes.md` in Task 17, to keep this file (loaded into every
session) from carrying detail that only matters when writing more FlaUI tests.

- **Merging long-lived branches can hit false "whole-file" conflicts from
  `core.autocrlf=true` + `.gitattributes`' `*.cs text eol=crlf` fighting each
  other.** Symptom: `git merge` reports a conflict where the *entire* file is
  one `<<<<<<<`/`=======`/`>>>>>>>` block, even though the real edits on each
  side are small. Cause: some files' blobs are stored with literal CRLF
  (never renormalized — the repo uses an *incremental* normalization
  strategy per issue #10, so old/untouched files can still be CRLF-native in
  the object store) while others are properly LF-native (git's normal form,
  smudged to CRLF on checkout). When one side of a merge is LF-native and the
  other is CRLF-native, git's diff3 can't align a single line and gives up on
  the whole file. `git merge --abort` can also fail here ("Entry not
  uptodate") because checkout itself re-triggers a clean/smudge mismatch on
  the affected files. Fix: verify with `git cat-file -p <blob> | file -`
  (raw stored bytes, bypassing smudge) which side is inconsistent; extract
  base/ours/theirs via `git cat-file -p <blob-sha>` (get shas from
  `git ls-files -u`), normalize all three to LF, run `git merge-file` on the
  normalized copies to get the real 3-way merge, then copy that result back
  into the working file (git's `eol=crlf` will smudge it back to CRLF on
  checkout as usual). Confirmed via `dotnet build`/`dotnet test` giving
  identical results before and after — the fix only touched line endings and
  genuinely merged content, not behavior.

  **Resolved 2026-09-16** (PR #35, commit `5e703cd`): the whole repo was
  renormalized in one pass via `git add --renormalize .`, which re-runs the
  clean filter against every tracked file's blob and restages any that don't
  match what the effective attributes say they should be — fixing every
  leftover CRLF-native blob in one content-preserving commit (verified via
  `git diff --cached -b --ignore-space-at-eol` being empty, and
  `dotnet build`/`dotnet test` giving identical results before and after).
  Any local checkout still showing files as perpetually "modified" against a
  clean `master` after this point is stale local drift from before this fix
  landed, not a live problem — `git reset --hard origin/master` clears it (a
  plain `git checkout -- .` or `git reset --hard HEAD` can fail to, if the
  working tree was checked out from a pre-renormalization commit; reset
  straight to the post-renormalization target instead of the old `HEAD`).
  New branches created after this point, and fresh clones, should not hit
  this class of issue at all going forward.

- **`Money.cs`'s collection classes (`Categories`, `Payees`, `Currencies`, `Aliases`,
  `Transactions`, `Accounts`, ...) each implement two different `IEnumerable<T>`
  instantiations**, because `PersistentContainer` itself implements
  `IEnumerable<PersistentObject>` while each concrete collection also implements
  `ICollection<TSpecific>` (e.g. `ICollection<Category>`). Passing one of these collections to
  a generic method expecting `IEnumerable<T>` — including LINQ (`.FirstOrDefault(...)`,
  `.Count()`, etc.) — fails with a confusing `CS0411` ("type arguments... cannot be inferred"),
  often naming an unrelated overload (e.g. `ImmutableArrayExtensions.FirstOrDefault`) in the
  error message, because the compiler can't decide which `IEnumerable<T>` to bind `T` against.
  Fix: use a plain `foreach` loop instead (its enumerator resolution isn't affected by this),
  or supply the type argument explicitly if a generic method call is unavoidable
  (`Enumerable.FirstOrDefault<Category>(money.Categories, ...)`). Discovered 2026-09-18 writing
  `BasicsFixtureBuilderTests.cs`.

- **`Category.Name` stores the full colon-separated path** (e.g. `"Fun:Movies"`), not just the
  leaf segment — `Category.Label` derives the leaf name from `Name`'s last `:`-separated
  segment (`Money.cs`'s `Label` getter). A category created via
  `Categories.GetOrCreateCategory("Fun:Movies", ...)` has `Name == "Fun:Movies"` and
  `Label == "Movies"`, not `Name == "Movies"` — easy to assume backwards. Discovered
  2026-09-18.

- **`new Transaction()` (the parameterless constructor) leaves `.MyMoney` permanently `null`,
  even after `money.Transactions.AddTransaction(t)`.** `Transaction.MyMoney`'s getter walks
  `this.Parent as Transactions` → `parent.Parent as MyMoney`, and `Parent` is only set by
  `PersistentObject(PersistentContainer container)` — the constructor
  `Transaction(Transactions container) : base(container)` — never by `AddTransaction`, which
  just inserts into the internal dictionary (`this.transactions[t.Id] = t`) without touching
  `Parent`. Any business-layer code that reads `t.MyMoney` (e.g. `AutoCategorization.
  AutoCategoryMatch`, which calls `t.MyMoney.Transactions.GetTransactionsFrom(...)`) throws
  `NullReferenceException` on a transaction built the parameterless way, even one already added
  via `AddTransaction`. Fix: always construct test transactions via
  `new Transaction(money.Transactions)` (matching production's own `Transactions.NewTransaction
  (Account a)`, which does exactly this), not `new Transaction()`. Discovered 2026-09-19 writing
  `AutoCategorizationTests.cs` — confirmed by reading `AutoCategoryMatch`'s real logic rather
  than patching around the symptom, per this migration's own "characterization testing, don't
  silently fix code to match a guessed test" rule.

- **`Transactions.ExecuteQuery` and `QuickFilterParser`/`Filter<T>` gotchas, found writing
  `QuickSearchQueryTests.cs`:**
  - `ExecuteQuery(QueryRow[])` lives on `MyMoney.Transactions` (`money.Transactions.ExecuteQuery(...)`),
    not on `MyMoney` itself, despite how it reads in passing references.
  - `QueryRow.Matches(decimal)`'s `Operation.GreaterThan` case is implemented as `value >=
    TryParseDecimal(...)` (i.e. inclusive, same as `GreaterThanEquals`) — read `Query.cs` before
    assuming a `>` query excludes the boundary value.
  - `Transaction.Matches(QueryRow)`'s `Field.Payment` case returns `q.Matches(-this.Amount)` only
    when `Amount <= 0` (and `false` otherwise) — Payment is always compared as a positive number.
  - The only concrete `FilteredObservableCollection<Transaction>` subclass is `TransactionCollection`
    in `MyMoney/Views/TransactionsView.xaml.cs` (WPF project, not `MyMoney.Business` — `UnitTests`
    already references `MyMoney.csproj` directly, so this is fine to use from a test). Its
    constructor takes an already-materialized `IEnumerable<Transaction>` — passing the raw
    `Transactions` container itself throws `NotImplementedException` from
    `Transactions.CopyTo(Transaction[], int)`, an unimplemented `ICollection<Transaction>` member
    that `ObservableCollection<T>`'s constructor calls when its source is an `ICollection<T>`. No
    production code path hits this since every real call site already passes a materialized list
    (e.g. `GetSelectedTransactions()`); use `money.Transactions.GetTransactionsFrom(account)` (or
    similar) in tests instead of the container.
  - `ExecuteQuery`'s split-row branch (`Transaction(Transaction t, Split s)`, the synthetic
    read-only proxy transaction returned when a query matches a split but not its parent) proxies
    `Category`/`Amount`/`Memo`/`Payee` from the `Split`, not the parent `Transaction`, and
    `IsReadOnly` is always `true`. `Split.Matches(QueryRow)` explicitly does NOT support
    `Field.Payment`/`Field.Deposit` ("too confusing if we match amount on splits", per its own
    comment) — only Category/Memo/Payee.

- **`Alias.OnChanged` (fired by both the `Pattern` and `AliasType` setters) eagerly constructs
  `new Regex(this.pattern)` whenever `AliasType == Regex` and `Pattern` is non-null** - NOT
  lazily on first `Matches()` call, despite `Matches()`'s own `if (this.regex == null)` guard
  looking like lazy init (that guard is usually a no-op since `OnChanged` already built it). A
  malformed pattern throws `RegexParseException` (an `ArgumentException`) synchronously at
  whichever property assignment completes the `Regex`+malformed-pattern combination - e.g.
  `alias.Pattern = "[unclosed"; alias.AliasType = AliasType.Regex;` throws on the *second* line,
  not later when actually matching. **This means `RenamePayeeDialog.CheckConflicts()`
  (`new Alias() { Pattern = this.Pattern, AliasType = atype }`) would throw an unhandled exception
  if a user checks "Use regular expressions" with an already-malformed pattern typed** - a real,
  live robustness gap in the dialog, found but deliberately not fixed (out of scope for a
  test-writing pass) - flagging here in case it's picked up later.

- **`Transactions.AddTransaction(Transaction)` throws** (`"Failed to add transaction with
  duplicate Id=..."`) if given an explicit (non -1) `Id` that already exists - the `XmlStore.Load`
  replay path's collision guard.

- **`Transactions.FindPotentialDuplicate(t, tc, range)` is NOT a "scan `tc` for anything matching
  `t`" API**, despite reading like one. It requires `t` itself to already be an element of `tc`
  (`int i = tc.IndexOf(t); if (i > 0) { ... }` — silently returns `null` otherwise, even with an
  exact duplicate present in the list) and only then checks `t`'s immediate list-neighbors
  (index `i-1`, `i+1`, `i-2`, `i+2`, ...) for a match, closest first. It's designed to be called
  with `t` sitting inside the same ordered/materialized list you're searching, not with an
  arbitrary "here's my incoming transaction, here's a pool of candidates" pair. The plan's
  original example test built exactly that second (broken) shape and would have silently gotten
  `null` back, not the intended match. Discovered 2026-09-19 writing
  `MergingDuplicateTransactionsTests.cs`.
  - `Transaction.Merge(Transaction t)` throws `ApplicationException("Cannot merge when both
    transactions are transferred to a different place")` when both sides are already transfers
    but to *different* target accounts — a real, unresolvable-conflict exception, not a silent
    pick-one.
  - `Merge` also has an early silent-no-op guard: if the incoming duplicate's `Category` is the
    synthetic "Xfer to/from Deleted Account" placeholder (assigned elsewhere when a transfer's
    target account was deleted), it returns `false` immediately without merging any field at all,
    even ones the survivor is missing.

- **`Transaction.HasAttachment` (the grid's paperclip icon) is driven by `AttachmentWatcher`'s
  background scan, dispatched via `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)`,
  and can lag well behind the file actually being written.** `AttachmentDialog.Transaction`'s
  setter (`LoadAttachments`) does its own synchronous `AttachmentManager.GetAttachments(t)`
  directory scan every time the dialog opens for a transaction, completely independent of
  `HasAttachment` - so anything that needs to know "does the app see this attachment" should open
  the dialog directly rather than poll the icon. Relevant beyond testing: this is the same
  `AttachmentManager` flagged for WPF-free extraction in
  [markabrandjord/MyMoney.Net#44](https://github.com/markabrandjord/MyMoney.Net/issues/44).

- **`Splits`/`Transfer` gotchas, found writing `SplitsAndTransfersTests.cs`:**
  - `Splits.Unassigned`/`HasUnassigned` are not auto-recomputed when you add a split or set a
    split's `Amount` in a headless (no WPF databinding) scenario - `Split.OnAmountChanged` is an
    empty method, and `Splits.AddSplit`'s `InsertItem` only fires property-changed notifications,
    never `Rebalance()`. A direct business-layer caller must call `Splits.Rebalance()` explicitly;
    real UI code only works because WPF's grid plumbing happens to trigger it eventually.
  - **`MyMoney.RemoveTransfer(Transaction t)` does NOT remove both sides of a transfer** - read
    `RemoveTransfer(Transfer t)`'s real logic: it only calls `RemoveTransaction` on the *other*
    side (the transfer's linked `Transaction`, called "target" in the source); the side you called
    it on just has its own `Transfer` link cleared (`t.Transfer = null`) and survives as an
    ordinary, non-transfer transaction. Don't assume symmetric deletion.
  - Removing a transfer whose *other* side is `TransactionStatus.Reconciled` throws
    `MoneyException("Transfer is reconciled on the other side and cannot be modified outside of
    balancing the target account.")` (found in `RemoveTransfer(Transfer t)`) - a real exception,
    not a bool result or silent no-op, and it's checked before either side is touched.

- **`Category`/`Categories` deletion mechanics, found writing `CategoriesTests.cs`:**
  - `Category.OnDelete()` (base `PersistentObject.OnDelete()`) is a **soft delete** - it only
    flips `ChangeType` to `Deleted`, firing a change event. It does **not** remove the category
    from `Categories`'s internal dictionary/`Count`; that's what `Categories.RemoveCategory(c)`
    does, and even that only removes it immediately when `c.IsInserted` is true (a category
    that's never been saved) - otherwise it's removed on the next save. `Categories.GetCategories()`
    is the collection's "live" view and explicitly filters out `IsDeleted` categories - that's
    what the UI tree actually binds to, so "removed immediately" is true from the UI's perspective
    even though raw `Count` doesn't change. Critically, **`OnDelete()` has no guard against a
    category that still has transactions** - only the UI layer (`CategoriesControl.Delete()`)
    checks `GetTransactionsByCategory(c, null).Count > 0` and redirects to `MergeCategoryDialog`;
    calling `OnDelete()` directly (e.g. a script, or a future code path) silently leaves
    transactions pointing at a now-`IsDeleted` category, no exception, no warning.
  - `Transaction.ReCategorize(Category oldCategory, Category newCategory)` is a per-`Transaction`
    instance method - there is no bulk "recategorize all transactions on this category" API.
    The real production pattern (`CategoriesControl.xaml.cs`'s category-merge handler) is
    `Transactions.GetTransactionsByCategory(oldCategory, null)` then call `t.ReCategorize(...)`
    on each result. It reads `this.MyMoney.Categories.ReParent(...)`, so it needs a properly
    parented `Transaction` (see the `new Transaction()` gotcha above).
  - `Categories.RemoveCategory`/`Currencies.RemoveCurrency`'s `forceRemoveAfterSave` branch
    (immediate removal of an already-persisted, non-`IsInserted` item) is a different code path
    from the plain `IsInserted` (never-saved) immediate-removal one - simulate "already persisted"
    in a test via `entity.OnUpdated()` (flips `ChangeType` back to `None`), matching what a real
    load/save cycle does.

- **A FlaUI test's UI assertions can all pass while the real app has crashed in the background -
  nothing was watching for it until Task 15.** Writing `SampleDataFlaUiTests.cs`, cancelling the
  "Add Sample Data" dialog on an empty database threw a real, unhandled `NullReferenceException`
  in production code (`AccountsControl.SelectedAccount`'s setter, called with a `null` `Account`
  from `MainWindow.OnCommandAddSampleData` - filed as
  [markabrandjord/MyMoney.Net#45](https://github.com/markabrandjord/MyMoney.Net/issues/45)).
  `App.xaml.cs`'s `OnUnhandledException` caught it, logged it, and reported it via a
  `MessageBoxEx` dialog - but `MessageBoxEx.Show` displays via `UiDispatcher.BeginInvoke` (posted,
  not blocking), so the crash didn't halt anything the test was doing, and UIA tree reads work
  fine alongside an unrelated modal regardless. Both tests in that run reported "Passed" in
  `dotnet test` output; only a human watching the actual screen live noticed the crash dialog.
  **Fix: `AppCrashGuard.cs`** (`Source/WPF/UITests/Basics/`) - every Basics FlaUI test's shared
  `BasicsTestSetup.CleanUpDatabase` (called from every fixture's `[TearDown]`) now asserts the
  app's own log file (`%TEMP%\MyMoney\Logs\MyMoney_<date>_log.txt`, per `Utilities/Logger.cs`)
  gained no new `"APP ERROR: Unhandled"` line during the test, and proactively dismisses a
  lingering "Unhandled Exception"/"Crash Report" dialog if one is still open (the same
  leftover-modal session-corruption risk documented in
  `docs/dev/flaui-basics-test-notes.md`). This check is deliberately NOT wrapped in a try/catch -
  unlike the best-effort registry/file cleanup next to it, a real crash must fail the test, not be
  silently absorbed. A test that knowingly reproduces
  a real, filed bug (like the Cancel scenario above) is marked `[Ignore("...")]` referencing the
  issue, rather than left to fail every run or having its assertions weakened to tolerate the
  crash. Discovered and fixed 2026-09-19.
