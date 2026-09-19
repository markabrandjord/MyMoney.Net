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
- Other source trees are separate/experimental UI ports, not part of the main
  app: `Source/Xamarin` (legacy mobile) and `Source/Uno` (in-progress Uno
  Platform port). Don't assume changes to `Source/WPF/MyMoney` need mirroring there.

## Things that have been gotten wrong before

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

- **Opening a database does not select or display any account's register.** The Accounts panel
  (`MainWindow.xaml.cs`'s `toolBox.Add("ACCOUNTS", "AccountsSelector", ...)`) starts collapsed —
  its account `ListItem`s (`AutomationId` = the account's name) aren't even in the UIA tree until
  the section's `HeaderSite` button is clicked (a plain `Button`; `Invoke` pattern throws
  `InvalidOperationException` there, use `Click()`) — and even once visible, no account is
  auto-selected, so the transaction `DataGrid` (`AutomationId="TheGrid_BankTransactionDetails"`)
  renders zero `DataItem` rows (just column headers) until one is explicitly selected via
  `SelectionItem.Pattern.Select()`. `Source/WPF/UITests/Basics/OpenFixtureDatabase.SelectAccount`
  does this (idempotently, since the app/window is reused across every test in a `[SetUpFixture]`
  session, so a section an earlier test expanded stays expanded). Also: the `DataGrid` always
  renders one extra `{NewItemPlaceholder}` `DataItem` for its add-new-row affordance regardless of
  filtering — real transaction rows are the ones whose `Name` starts with `"Transaction:"`.
  Discovered 2026-09-19 writing `QuickSearchFlaUiTests.cs`.

- **`QuickFilterControl`'s Quick Search box only applies its filter on a literal Enter keypress**
  (`OnTextBox_KeyUp` checks `e.Key == Key.Enter`) — `TextChanged` alone (fired on every keystroke)
  only toggles the clear-filter (✕) button's visibility, it does not touch the actual filter.
  FlaUI's `AsTextBox().Enter(text)` (despite the name) only types the text via simulated keyboard
  input; it does not itself send an Enter keypress. Fix: follow it with
  `FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER)`. Discovered
  2026-09-19 writing `QuickSearchFlaUiTests.cs`.

- **`Category`/`Categories` deletion gotchas, found writing `CategoriesTests.cs`:**
  - `Transaction.ReCategorize(Category oldCategory, Category newCategory)` is a per-`Transaction`
    instance method - there is no bulk "recategorize all transactions on this category" API.
    The real production pattern (`CategoriesControl.xaml.cs`'s category-merge handler) is
    `Transactions.GetTransactionsByCategory(oldCategory, null)` then call `t.ReCategorize(...)`
    on each result. It reads `this.MyMoney.Categories.ReParent(...)`, so it needs a properly
    parented `Transaction` (see the `new Transaction()` gotcha above).
  - `Category.OnDelete()` (base `PersistentObject.OnDelete()`) is a **soft delete** - it only
    flips `ChangeType` to `Deleted`, firing a change event. It does **not** remove the category
    from `Categories`'s internal dictionary/`Count`; that's what `Categories.RemoveCategory(c)`
    does, and even that only removes it immediately when `c.IsInserted` is true (a category
    that's never been saved) - otherwise it's removed on the next save. Real production delete
    flows (`CategoriesControl.xaml.cs`) call `OnDelete()` directly for exactly this soft-delete
    effect; `Categories.GetCategories()` is the collection's "live" view, and explicitly filters
    out `IsDeleted` categories - that's what the UI tree actually binds to, so "removed
    immediately" is true from the UI's perspective even though raw `Count` doesn't change.
