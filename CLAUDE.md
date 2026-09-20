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
  the section is expanded — and even once visible, no account is auto-selected, so the
  transaction `DataGrid` (`AutomationId="TheGrid_BankTransactionDetails"`) renders zero `DataItem`
  rows (just column headers) until one is explicitly selected via `SelectionItem.Pattern.Select()`.
  `Source/WPF/UITests/Basics/OpenFixtureDatabase.SelectAccount`/`ExpandCategoriesPanel`/
  `EnsureToolboxSectionExpanded` do this (idempotently, since the app/window is reused across
  every test in a `[SetUpFixture]` session, so a section an earlier test expanded stays expanded).
  Also: the `DataGrid` always renders one extra `{NewItemPlaceholder}` `DataItem` for its
  add-new-row affordance regardless of filtering — real transaction rows are the ones whose
  `Name` starts with `"Transaction:"`. Discovered 2026-09-19 writing `QuickSearchFlaUiTests.cs`.

- **Each left-hand toolbox section (Accounts/Categories/Payees/Securities) is a real WPF
  `Expander`** (`Controls/Accordion.xaml.cs`'s `Add()` builds one per section), whose automation
  peer is exposed as `ControlType.Group` and natively supports the `ExpandCollapse` pattern —
  call `section.Patterns.ExpandCollapse.Pattern.Expand()` on the section itself. The nested
  `AutomationId="HeaderSite"` element is just the Expander's default-template toggle button;
  clicking it directly (`Click()`) proved unreliable in practice (worked for Accounts, silently
  did nothing for Categories in the same run) — prefer the pattern call on the section, not a
  click on its child. Discovered 2026-09-19 writing `CategoriesFlaUiTests.cs`.

- **Opening a second database in the same app session can pop a "Save Changes" `YesNoCancel`
  prompt (`MainWindow.SaveIfDirty`, title `"Save Changes"`, button `AutomationId="ButtonNo"`)
  before the real "Open Database" dialog** — observed even when no test explicitly edited
  anything (e.g. just showing/expanding the Categories panel for the first time in a session was
  enough to flip `MainWindow.dirty`). Since `BasicsAppSession` launches the app once per
  `[SetUpFixture]` and every test in a multi-test fixture calls `OpenFixtureDatabase.Open` again,
  any fixture with 2+ FlaUI tests can hit this. `OpenFixtureDatabase.Open` now detects a
  `"Save Changes"` modal and clicks `ButtonNo` (discard) before continuing to the real dialog.
  Discovered 2026-09-19 writing `CategoriesFlaUiTests.cs`.

- **The Categories rename edit box does not select-all on entry.**
  `CategoriesControl.xaml.cs`'s `OnTextEditorForRenaming_Loaded` sets
  `CaretIndex = Text.Length` (end of the existing text), not select-all — typing immediately
  after F2 appends to the existing label instead of replacing it (confirmed: doing this literally
  produced a real category named `"MoviesVideos"`, not a rename attempt at all). A real rename
  test needs `Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A)` before
  typing the new name. Discovered 2026-09-19 writing `CategoriesFlaUiTests.cs`.

- **A dismissed-looking `MessageBoxEx` modal can silently keep blocking the whole shared FlaUI
  app session if the test never clicks its button.** `CategoriesFlaUiTests.RenamingCategoryToExistingName_IsRejected`
  correctly triggers `CategoriesControl`'s real "Category ... already exist" collision dialog,
  but originally never dismissed it - and `AutomationElement.FindFirstDescendant`/`FindAllDescendants`
  keep working and returning correct-looking results even while a modal is up (they just read the
  automation tree; they don't check for blocking dialogs), so that test's own assertions still
  passed. The dialog then sat open for the rest of the session, and a *later, unrelated* FlaUI
  test (`CurrenciesFlaUiTests`) started failing for no apparent reason of its own - root-caused
  only by taking a screenshot (`FlaUI.Core.Capturing.Capture.Screen().ToFile(...)`) at the point
  of failure and seeing the leftover dialog sitting on screen. **Lesson: any test that
  deliberately triggers a modal (error dialog, confirmation, etc.) must assert the modal appeared
  AND dismiss it before returning** - a passing assertion is not proof the UI is back in a clean
  state for the next test in the same shared session; when a later, unrelated test fails
  mysteriously, screenshot the live state before assuming it's that test's own bug.

- **A `DataGridCell`'s automation element goes stale across its own edit-mode template swap.**
  `CurrenciesView.xaml`'s Symbol column swaps `myTemplateSymbol` (read-only `TextBlock`) for
  `myTemplateSymbolEdit` (a `FilteringComboBox`) on `BeginEdit` - and the `AutomationElement`
  handle captured *before* entering edit mode (e.g. to `.Click()`/`.DoubleClick()` it) cannot
  reliably find the new `ComboBox` via `.FindFirstDescendant(...)` on itself afterward, even
  though the ComboBox is genuinely there and visible (confirmed via screenshot while the search
  returned null) - the cell's visual subtree gets replaced, not mutated in place. Fix: after
  triggering edit mode, re-search for the edit control from a stable ancestor (the grid, not the
  old cell handle).

- **`CurrenciesView`'s Symbol cell (`myTemplateSymbolEdit`, a `local:FilteringComboBox`,
  `IsEditable="True"`) does not select an item just from typing.** Typing sets the edit box's
  `Text` but leaves `SelectedItem` (and therefore the two-way-bound `Currency.Symbol`) untouched
  unless the dropdown is actually open - confirmed via a tree dump showing zero `ListItem`s after
  typing alone. The working sequence: click the cell, double-click (or Enter, when the grid
  itself reliably has focus) to `BeginEdit`, `F4` to open the dropdown, type the code (also live-
  narrows the list via `FilteringComboBox`'s own `FilterChanged`/`ComboBoxCultureInfo_FilterChanged`),
  click the first filtered match (its `SelectionItem` pattern isn't supported - use `.Click()`,
  not `Patterns.SelectionItem.Pattern.Select()`), then exactly one `Enter` to commit the row - a
  second `Enter` was tried and confirmed harmful (it re-opens edit on the *next* placeholder row
  instead of just committing). Discovered 2026-09-19 writing `CurrenciesFlaUiTests.cs`.

- **Right-click context-menu invocation, confirmed working**: `AutomationElement.RightClick()`
  on a `DataGrid` row opens `TransactionsView`'s real `ContextMenu`, and its items (e.g.
  `menuItemRenamePayee`) are then findable/invokable the same as any menu bar item - no special
  handling needed, worked first try. Click the row first (select it, satisfying a command's
  `CanExecute`) before right-clicking.

- **`RenamePayeeDialog`'s "To" field (`comboBox1`/`PART_EditableTextBox`) starts pre-filled with
  the same payee name being renamed** (`ShowDialogRenamePayee(payee)` sets both `Payee` and
  `RenameTo` to the same payee) - same caret-at-end-not-select-all gotcha as the Categories
  rename box, needs Ctrl+A before typing or it appends.

- **`BasicsFixtureBuilder`'s pre-seeded "AMC THEATRES 1234" alias (added for the regex-
  consolidation scenario) means renaming that same payee with Auto-Rename checked exercises
  `RenamePayeeDialog.OnOkButton_Click`'s *re-point an existing alias* branch, not its *create a
  new alias* branch** (`Aliases.FindAlias(pattern)` finds the existing one, so `AddAlias` is
  never called for it) - a test asserting a new alias was *created* here would be testing the
  wrong branch. Discovered 2026-09-19 writing `PayeesFlaUiTests.cs`.

- **A physical `Click()` on a transaction grid row can silently fail to move selection when the
  fixture also seeds a near-duplicate pair (e.g. `BasicsFixtureBuilder`'s two `TARGET T-1234`
  rows).** The app's own duplicate-detection feature auto-selects/expands a "Merge" connector UI
  for that pair, and a screenshot taken immediately after `Click()`-ing a different row showed
  selection still sitting on the duplicate pair - the click never took. Fix:
  `AutomationElement.Patterns.SelectionItem.Pattern.Select()` (a real UIA selection call) instead
  of a mouse-coordinate `Click()` for selecting a specific transaction row in a fixture that also
  has duplicates in it. Discovered 2026-09-19 writing `SplitsAndTransfersFlaUiTests.cs` - root-
  caused only by screenshotting state right after the click, same technique as the Task 5/7
  leftover-dialog and stale-cell-handle findings.
  - The split-details sub-grid (`TheGridForAmountSplit`, `AutomationId="TheGridForAmountSplit"`)
    is reached the same way as the main-grid "Rename Payee" flow: right-click the transaction row
    → `menuItemSplit` (`Command="CommandSplits"`). Its columns are Payee(0)/Category(1)/
    Payment(2)/Deposit(3)/Memo(4), and it has the same `{NewItemPlaceholder}` row and stale-
    cell-handle-after-edit-mode-swap behavior as every other `MoneyDataGrid` in this app.
  - F6's handler (`OnDataGrid_KeyDown`, `TransactionsView.xaml.cs`) only works when the target
    cell is genuinely in edit mode: it looks for an editable control inside
    `dataGrid.CurrentCell.Column.GetCellContent(...)`, and the read-only cell template
    (`myTemplateSplitPayment`) is a plain `TextBlock` with nothing editable in it - only
    `myTemplatePaymentEditInTheSplitDetailedView` (edit mode) has the `TextBox` F6 needs.

- **Error/invalid-input-path backfill (2026-09-19), after the user pointed out the business-layer
  test suite up to this point was almost entirely happy-path** (1 real exception test out of 25).
  Went back through `CategoriesTests.cs`/`CurrenciesTests.cs`/`PayeesAndAliasesTests.cs`/
  `QuickSearchQueryTests.cs`/`DataTests.cs` and added the concrete gaps already read but not
  tested:
  - **`Category.OnDelete()` has no guard against a category that still has transactions** - only
    the UI layer (`CategoriesControl.Delete()`) checks `GetTransactionsByCategory(c, null).Count
    > 0` and redirects to `MergeCategoryDialog`; calling `OnDelete()` directly (as e.g. a script
    or a future code path might) silently leaves transactions pointing at a now-`IsDeleted`
    category, no exception, no warning.
  - **`Categories.RemoveCategory`/`Currencies.RemoveCurrency`'s `forceRemoveAfterSave` branch**
    (immediate removal of an already-persisted, non-`IsInserted` item) was untested - only the
    `IsInserted` (never-saved) immediate-removal path had a test. Simulate "already persisted" in
    a test via `entity.OnUpdated()` (flips `ChangeType` back to `None`), matching what a real
    load/save cycle does.
  - **`Alias.OnChanged` (fired by both the `Pattern` and `AliasType` setters) eagerly constructs
    `new Regex(this.pattern)` whenever `AliasType == Regex` and `Pattern` is non-null** - NOT
    lazily on first `Matches()` call, despite `Matches()`'s own `if (this.regex == null)` guard
    looking like lazy init (that guard is usually a no-op since `OnChanged` already built it). A
    malformed pattern throws `RegexParseException` (an `ArgumentException`) synchronously at
    whichever property assignment completes the `Regex+malformed-pattern` combination - e.g.
    `alias.Pattern = "[unclosed"; alias.AliasType = AliasType.Regex;` throws on the *second*
    line, not later when actually matching. **This means `RenamePayeeDialog.CheckConflicts()`
    (`new Alias() { Pattern = this.Pattern, AliasType = atype }`) would throw an unhandled
    exception if a user checks "Use regular expressions" with an already-malformed pattern
    typed** - a real, live robustness gap in the dialog, found but deliberately not fixed (out of
    scope for a test-writing pass) - flagging here in case it's picked up later.
  - **`Transactions.ExecuteQuery`'s split-row branch** (`Transaction(Transaction t, Split s)`, the
    synthetic read-only proxy transaction returned when a query matches a split but not its
    parent) was untested - confirmed via its constructor that `Category`/`Amount`/`Memo`/`Payee`
    are all proxied from the `Split`, not the parent `Transaction`, and `IsReadOnly` is always
    `true`. Also confirmed `Split.Matches(QueryRow)` explicitly does NOT support
    `Field.Payment`/`Field.Deposit` ("too confusing if we match amount on splits", per its own
    comment) - only Category/Memo/Payee.
  - **`Transactions.AddTransaction(Transaction)` throws** (`"Failed to add transaction with
    duplicate Id=..."`) if given an explicit (non -1) `Id` that already exists - the XmlStore.Load
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

- **The duplicate-connector's "Merge" button has no `AutomationId`** - `TransactionConnectorAdorner`
  (`TransactionsView.xaml.cs`) creates it in code as a plain `RoundedButton` with
  `Content = "Merge"` and never sets `AutomationProperties.AutomationId`; find it via
  `ByControlType(Button).And(ByName("Merge"))` instead (a WPF `Button`'s automation `Name`
  defaults to its `Content` when it's a plain string). It renders inside a WPF `Adorner`
  (`AdornerLayer` overlay), not the normal element tree, but is still discoverable via a normal
  `FindFirstDescendant` walk from the main window - adorners are still part of the same visual
  tree UIA walks. It auto-appears (no explicit "select the row" step needed) via a
  `DispatcherTimer`-delayed `ShowPotentialDuplicates`, once whatever transaction is currently
  selected has a nearby `FindPotentialDuplicate` match - confirmed in this repo's Basics fixture,
  where the grid's default selection already lands on one of the two seeded `TARGET T-1234`
  duplicates as soon as the account opens.

- **`Transaction.HasAttachment` (the grid's paperclip icon) is driven by `AttachmentWatcher`'s
  background scan, dispatched via `Dispatcher.BeginInvoke(..., DispatcherPriority.Background)`** -
  it did not visibly flip within 10 seconds in this environment (confirmed by polling and
  dumping the grid's Attachment-column cell repeatedly). Don't gate a FlaUI test on this icon
  appearing. It doesn't matter for testing "does the app see this attachment" anyway:
  `AttachmentDialog.Transaction`'s setter (`LoadAttachments`) does its own synchronous
  `AttachmentManager.GetAttachments(t)` directory scan every time the dialog opens for a
  transaction, completely independent of `HasAttachment` - open the dialog directly (via the
  Attachment column's edit-mode button, `TransactionsView.CommandScanAttachment`) and it finds a
  pre-seeded file regardless of whether the icon ever appeared. Transaction Id *is* confirmed
  stable across a real SQLite save/reload round-trip (checked directly, in case that was the
  culprit) - it wasn't.

- **A non-modal, owned `Window.Show()` (e.g. `AttachmentDialog`) can be completely invisible to
  UIA desktop-enumeration**, even scoped by `ByProcessId`, while a screenshot proves it's
  genuinely rendered on screen. `automation.GetDesktop().FindAllChildren(...)` and
  `Application.GetAllTopLevelWindows(...)` are the same underlying UIA mechanism (confirmed from
  FlaUI's own source), so neither is an independent check against the other - this is a
  documented, known-flaky UIA path (FlaUI#57/#239), not something specific to native common
  dialogs despite the flaui-wpf-testing skill's reference file being framed around those. The
  fix: raw Win32 `EnumWindows` via P/Invoke, bypassing UIA's desktop-children enumeration
  entirely (`Source/WPF/UITests/Basics/Win32WindowFallback.cs`, lifted from the skill's
  `references/native-dialogs.md`) - finds the window by HWND reliably, then everything else
  (patterns, `FindFirstDescendant`, etc.) works completely normally on the wrapped element.

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
  - `Category.ToString()` returns `Category.Name` (the full colon-separated path). A `TreeItem`
    bound to a `Category` (Categories panel) gets its automation `Name` from `ToString()` when no
    explicit `AutomationProperties.Name` is set - so a tree item's real UIA name is
    `"Fun:Movies"`, not the `Label` ("Movies") its `TextBlock` visibly displays (that leaf text
    only shows up as a separate nested `Text` element's own `Name`). Search `TreeItem`s by the
    full dotted name, not the visible label. Discovered 2026-09-19 writing `CategoriesFlaUiTests.cs`.
  - `Category.OnDelete()` (base `PersistentObject.OnDelete()`) is a **soft delete** - it only
    flips `ChangeType` to `Deleted`, firing a change event. It does **not** remove the category
    from `Categories`'s internal dictionary/`Count`; that's what `Categories.RemoveCategory(c)`
    does, and even that only removes it immediately when `c.IsInserted` is true (a category
    that's never been saved) - otherwise it's removed on the next save. Real production delete
    flows (`CategoriesControl.xaml.cs`) call `OnDelete()` directly for exactly this soft-delete
    effect; `Categories.GetCategories()` is the collection's "live" view, and explicitly filters
    out `IsDeleted` categories - that's what the UI tree actually binds to, so "removed
    immediately" is true from the UI's perspective even though raw `Count` doesn't change.
