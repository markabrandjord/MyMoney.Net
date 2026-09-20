# FlaUI test-authoring notes: Basics area

Narrow, UI-automation-specific findings from writing the `Source/WPF/UITests/Basics/*`
FlaUI test suite (the 2026-09-18 Basics test implementation plan). These are mechanics
that only matter when writing or maintaining FlaUI tests against this part of the app -
durable domain/business-layer knowledge from the same effort lives in `CLAUDE.md`
instead. Moved here in Task 17 to keep `CLAUDE.md` (loaded into every session
regardless of task) from carrying test-authoring-only detail permanently.

See also the user-level `flaui-wpf-testing` skill for FlaUI mechanics that aren't
specific to this repo (the input-vs-pattern-operations distinction, native-dialog
enumeration gaps, etc.) - this file is the repo-specific companion to that skill.

## Shared FlaUI app-session lifecycle

`BasicsAppSession` launches the app once per `[SetUpFixture]` (the whole
`Walkabout.UITests.Basics` namespace shares one process), so state and leftover UI
carries between tests in ways it wouldn't if each test launched its own app instance.

- **Opening a database does not select or display any account's register.** The
  Accounts panel (`MainWindow.xaml.cs`'s `toolBox.Add("ACCOUNTS", "AccountsSelector",
  ...)`) starts collapsed - its account `ListItem`s (`AutomationId` = the account's
  name) aren't even in the UIA tree until the section is expanded - and even once
  visible, no account is auto-selected, so the transaction `DataGrid`
  (`AutomationId="TheGrid_BankTransactionDetails"`) renders zero `DataItem` rows (just
  column headers) until one is explicitly selected via
  `SelectionItem.Pattern.Select()`. `OpenFixtureDatabase.SelectAccount`/
  `ExpandCategoriesPanel`/`EnsureToolboxSectionExpanded` do this (idempotently, since a
  section an earlier test expanded stays expanded for the rest of the session). Also:
  the `DataGrid` always renders one extra `{NewItemPlaceholder}` `DataItem` for its
  add-new-row affordance regardless of filtering - real transaction rows are the ones
  whose `Name` starts with `"Transaction:"`.

- **Each left-hand toolbox section (Accounts/Categories/Payees/Securities) is a real
  WPF `Expander`** (`Controls/Accordion.xaml.cs`'s `Add()` builds one per section),
  whose automation peer is exposed as `ControlType.Group` and natively supports the
  `ExpandCollapse` pattern - call `section.Patterns.ExpandCollapse.Pattern.Expand()` on
  the section itself. The nested `AutomationId="HeaderSite"` element is just the
  Expander's default-template toggle button; clicking it directly (`Click()`) proved
  unreliable in practice (worked for Accounts, silently did nothing for Categories in
  the same run) - prefer the pattern call on the section, not a click on its child.

- **Opening a second database in the same app session can pop a "Save Changes"
  `YesNoCancel` prompt** (`MainWindow.SaveIfDirty`, title `"Save Changes"`, button
  `AutomationId="ButtonNo"`) before the real "Open Database" dialog - observed even
  when no test explicitly edited anything (e.g. just showing/expanding the Categories
  panel for the first time in a session was enough to flip `MainWindow.dirty`). Since
  every test in a multi-test fixture calls `OpenFixtureDatabase.Open` again, any
  fixture with 2+ FlaUI tests can hit this. `OpenFixtureDatabase.Open` detects a
  `"Save Changes"` modal and clicks `ButtonNo` (discard) before continuing to the real
  dialog.

- **A dismissed-looking `MessageBoxEx` modal can silently keep blocking the whole
  shared session if a test never clicks its button.** A test that correctly triggers a
  real error/confirmation dialog but never dismisses it leaves it sitting open for the
  rest of the session - and `AutomationElement.FindFirstDescendant`/
  `FindAllDescendants` keep working and returning correct-looking results even while a
  modal is up (they just read the automation tree; they don't check for blocking
  dialogs), so the *triggering* test's own assertions can still pass. A *later,
  unrelated* test can then start failing for no apparent reason of its own -
  root-causable only by taking a screenshot
  (`FlaUI.Core.Capturing.Capture.Screen().ToFile(...)`) at the point of failure and
  seeing the leftover dialog. **Lesson: any test that deliberately triggers a modal
  must assert it appeared AND dismiss it before returning** - a passing assertion is
  not proof the UI is back in a clean state for the next test in the same session; when
  a later, unrelated test fails mysteriously, screenshot the live state before assuming
  it's that test's own bug. (This is the same class of problem `AppCrashGuard.cs` - see
  `CLAUDE.md` - now catches automatically for real app crashes specifically.)

## Finding elements

- **A `DataGridCell`'s automation element goes stale across its own edit-mode template
  swap.** Several grids in this app (`CurrenciesView`'s Symbol column, the Splits
  sub-grid's Payment column, the Attachment column) swap a read-only display template
  for an editable one on `BeginEdit`, and an `AutomationElement` handle captured
  *before* entering edit mode cannot reliably find the new edit control via
  `.FindFirstDescendant(...)` on itself afterward, even though the control is genuinely
  there and visible (confirmed via screenshot while the search returned null) - the
  cell's visual subtree gets replaced, not mutated in place. Fix: after triggering edit
  mode, re-search for the edit control from a stable ancestor (the grid or the row, not
  the old cell handle).

- **`CurrenciesView`'s Symbol cell (`myTemplateSymbolEdit`, a `local:FilteringComboBox`,
  `IsEditable="True"`) does not select an item just from typing.** Typing sets the edit
  box's `Text` but leaves `SelectedItem` (and therefore the two-way-bound
  `Currency.Symbol`) untouched unless the dropdown is actually open - confirmed via a
  tree dump showing zero `ListItem`s after typing alone. The working sequence: click
  the cell, double-click (or Enter, when the grid itself reliably has focus) to
  `BeginEdit`, `F4` to open the dropdown, type the code (also live-narrows the list via
  `FilteringComboBox`'s own `FilterChanged`), click the first filtered match (its
  `SelectionItem` pattern isn't supported - use `.Click()`, not
  `Patterns.SelectionItem.Pattern.Select()`), then exactly one `Enter` to commit the
  row - a second `Enter` re-opens edit on the *next* placeholder row instead of just
  committing.

- **Right-click context-menu invocation, confirmed working**:
  `AutomationElement.RightClick()` on a `DataGrid` row opens `TransactionsView`'s real
  `ContextMenu`, and its items (e.g. `menuItemRenamePayee`) are then findable/invokable
  the same as any menu bar item - no special handling needed, worked first try. Click
  the row first (select it, satisfying a command's `CanExecute`) before right-clicking.

- **Rename edit boxes place the caret at the end of the existing text, not select-all**
  - both `CategoriesControl.xaml.cs`'s `OnTextEditorForRenaming_Loaded` (Categories
  panel F2 rename) and `RenamePayeeDialog`'s "To" field (pre-filled with the same payee
  name being renamed) do this. Typing immediately after opening the edit box appends to
  the existing text instead of replacing it (confirmed: doing this literally produced a
  real category named `"MoviesVideos"`, not a rename attempt at all). A real rename
  test needs `Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A)`
  before typing the new name.

- **A `TreeItem` bound to a `Category` gets its automation `Name` from
  `Category.ToString()`** (returns the full colon-separated `Name`, e.g.
  `"Fun:Movies"`) when no explicit `AutomationProperties.Name` is set - not the `Label`
  ("Movies") its `TextBlock` visibly displays (that leaf text only shows up as a
  separate nested `Text` element's own `Name`). Search `TreeItem`s by the full dotted
  name, not the visible label. (See `CLAUDE.md` for the underlying
  `Category.Name`-vs-`Label` domain fact this follows from.)

- **The duplicate-connector's "Merge" button has no `AutomationId`** -
  `TransactionConnectorAdorner` (`TransactionsView.xaml.cs`) creates it in code as a
  plain `RoundedButton` with `Content = "Merge"` and never sets
  `AutomationProperties.AutomationId`; find it via
  `ByControlType(Button).And(ByName("Merge"))` instead (a WPF `Button`'s automation
  `Name` defaults to its `Content` when it's a plain string). It renders inside a WPF
  `Adorner` (`AdornerLayer` overlay), not the normal element tree, but is still
  discoverable via a normal `FindFirstDescendant` walk from the main window - adorners
  are still part of the same visual tree UIA walks. It auto-appears (no explicit
  "select the row" step needed) via a `DispatcherTimer`-delayed `ShowPotentialDuplicates`,
  once whatever transaction is currently selected has a nearby
  `Transactions.FindPotentialDuplicate` match - confirmed in this repo's Basics
  fixture, where the grid's default selection already lands on one of the two seeded
  `TARGET T-1234` duplicates as soon as the account opens.

- **A non-modal, owned `Window.Show()` (e.g. `AttachmentDialog`) can be completely
  invisible to UIA desktop-enumeration**, even scoped by `ByProcessId`, while a
  screenshot proves it's genuinely rendered on screen.
  `automation.GetDesktop().FindAllChildren(...)` and
  `Application.GetAllTopLevelWindows(...)` are the same underlying UIA mechanism
  (confirmed from FlaUI's own source), so neither is an independent check against the
  other - this is a documented, known-flaky UIA path (FlaUI#57/#239), not something
  specific to native common dialogs despite the `flaui-wpf-testing` skill's reference
  file being framed around those. The fix: raw Win32 `EnumWindows` via P/Invoke,
  bypassing UIA's desktop-children enumeration entirely
  (`Source/WPF/UITests/Basics/Win32WindowFallback.cs`, lifted from the skill's
  `references/native-dialogs.md`) - finds the window by HWND reliably, then everything
  else (patterns, `FindFirstDescendant`, etc.) works completely normally on the wrapped
  element.

- **`Transaction.HasAttachment` (the grid's paperclip icon) did not visibly flip
  within 10 seconds in this environment** (it's driven by `AttachmentWatcher`'s
  background scan, dispatched at `DispatcherPriority.Background`) - don't gate a FlaUI
  test on this icon appearing. It doesn't matter for testing "does the app see this
  attachment" anyway: open the `AttachmentDialog` directly (via the Attachment column's
  edit-mode button) and it finds a pre-seeded file regardless of whether the icon ever
  appeared - see `CLAUDE.md` for why.

## Input mechanics

- **`QuickFilterControl`'s Quick Search box only applies its filter on a literal Enter
  keypress** (`OnTextBox_KeyUp` checks `e.Key == Key.Enter`) - `TextChanged` alone
  (fired on every keystroke) only toggles the clear-filter (X) button's visibility, it
  does not touch the actual filter. FlaUI's `AsTextBox().Enter(text)` (despite the
  name) only types the text via simulated keyboard input; it does not itself send an
  Enter keypress. Fix: follow it with
  `FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER)`.

- **A physical `Click()` on a transaction grid row can silently fail to move selection
  when the fixture also seeds a near-duplicate pair** (e.g. `BasicsFixtureBuilder`'s
  two `TARGET T-1234` rows). The app's own duplicate-detection feature
  auto-selects/expands a "Merge" connector UI for that pair, and a screenshot taken
  immediately after `Click()`-ing a different row showed selection still sitting on the
  duplicate pair - the click never took. Fix:
  `AutomationElement.Patterns.SelectionItem.Pattern.Select()` (a real UIA selection
  call) instead of a mouse-coordinate `Click()` for selecting a specific transaction
  row in a fixture that also has duplicates in it.
  - The split-details sub-grid (`TheGridForAmountSplit`) is reached the same way as the
    main-grid "Rename Payee" flow: right-click the transaction row -> `menuItemSplit`
    (`Command="CommandSplits"`). Its columns are Payee(0)/Category(1)/Payment(2)/
    Deposit(3)/Memo(4), and it has the same `{NewItemPlaceholder}` row and
    stale-cell-handle-after-edit-mode-swap behavior as every other `MoneyDataGrid`.
  - F6's handler (`OnDataGrid_KeyDown`, `TransactionsView.xaml.cs`) only works when the
    target cell is genuinely in edit mode: it looks for an editable control inside
    `dataGrid.CurrentCell.Column.GetCellContent(...)`, and the read-only cell template
    is a plain `TextBlock` with nothing editable in it.

## Fixture-specific notes (`BasicsFixtureBuilder`)

- **The pre-seeded "AMC THEATRES 1234" alias means renaming that same payee with
  Auto-Rename checked exercises `RenamePayeeDialog.OnOkButton_Click`'s *re-point an
  existing alias* branch, not its *create a new alias* branch** (`Aliases.FindAlias
  (pattern)` finds the existing one, so `AddAlias` is never called for it) - a test
  asserting a new alias was *created* here would be testing the wrong branch.
