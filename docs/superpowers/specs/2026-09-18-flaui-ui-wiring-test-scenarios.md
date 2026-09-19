# FlaUI / business-layer test scenarios

## Purpose and scope — read this before adding a scenario

This document catalogs end-user scenarios for MyMoney.Net, organized around
the same 6 functional areas the published help file itself uses (Home,
Basics, Accounts, Charts, Reports, Development —
`https://moneytools.github.io/MyMoney.Net/`, built from this repo's `docs/`
via MkDocs), plus one cross-cutting section for real functionality the help
file doesn't document at all.

It serves three purposes:

1. **Business-layer API test scenarios** — for any feature whose logic
   already lives in (or is portable to) `Source/WPF/MyMoney.Business`, a
   direct unit test that calls the API with controlled data (success and
   invalid-input cases) and needs no UI at all. This is almost always the
   *cheaper* and *first* thing to close for a `Business-layer gap` row.
2. **FlaUI UI-wiring test scenarios** — once a scenario's business-layer
   logic is tested, the *thin* remaining question is: does the real WPF
   dialog, when a person interacts with it, correctly collect input and
   correctly call that API? That's a FlaUI test's job, and it should not
   re-prove the business logic through the UI.
3. **Help-file gap/staleness tracking** — building this catalog directly
   from the help file's own structure surfaced real documentation gaps
   (features with no help page at all) and at least one stale page (a
   diagram, not a scenario) along the way. See "Documentation gaps found"
   near the end. Use this catalog as a standing input to a documentation
   pass, not just a test-writing one.

**Do not duplicate coverage.** Before writing either kind of test, check
this document's own "Business-layer API" and "Status" columns — if a
column already cites an existing test file, that logic is covered; a new
scenario for the same logic is redundant. `Business-layer gap` means the
opposite is true: nothing tests it yet, at any layer.

## Conventions to follow when implementing a scenario

Based on the existing pattern in `Source/WPF/UITests/PayeeSelectionTests.cs`:

- Live in `Source/WPF/UITests/`, use `FlaUI.Core`/`FlaUI.UIA3`, NUnit.
- Use a checked-in fixture database (`Source/WPF/UITests/Fixtures/`) rather
  than building state through the UI first, where practical.
- If the scenario needs a registered database (post-#32, File|Open is a
  `DatabaseRegistry` picker, not a free-text browser), register the
  fixture against the real default registry path in the test and remove
  it in a `finally`/`TearDown` — there's no per-test registry override.
- Clean up the `Application`/`UIA3Automation` in `[TearDown]`, best-effort,
  so a cleanup failure doesn't mask the actual test failure.
- Per this repo's established FlaUI gotchas (see the `flaui-wpf-testing`
  skill): these tests need a real interactive desktop session to run
  correctly — they are not run via plain `dotnet test` in CI or in an
  unattended/background agent session (launching the real app this way
  hangs waiting for input that never arrives — confirmed directly during
  this migration, see the ledger for
  `docs/superpowers/plans/2026-09-18-business-layer-subsystem-migration.md`'s
  Task 1). When verifying facts for this catalog, prefer reading source
  directly over trying to run FlaUI or the app itself.
- For business-layer unit tests, no new mocking framework — reuse
  `MyMoney.TestSupport`'s `MockDatabase` and the existing fake pattern
  (`FakeBusinessLayerUiCallback`/`FakeImportProgressReporter` in
  `QifImporterTests.cs`/`DatabaseLifecycleTests.cs`).

## Status legend

- **Not started** — no test exists yet.
- **Business-layer gap** — the underlying business-layer coverage this
  scenario would sit on top of doesn't exist yet either (or the logic
  itself hasn't been extracted from the WPF project); close that first.
- **Written** — a test exists; note the file.
- **Passing** — last confirmed passing in an interactive session, with date.

---

## Home

No scenarios. `docs/index.md` is a pure landing/marketing page — every
concrete claim on it (themes, account types, OFX/CSV import, charts,
reports, tax export, reconciliation, search, attachments, calculator,
currencies, install) links straight into one of the other 5 areas below,
and there's no distinct "Home" view or dialog in the running app to test
in its own right.

---

## Basics

### Categories

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Add new category via transaction grid | Type `Food:Groceries` into a transaction's Category cell (not seen before) | `CategoryDialog` → `Categories.GetOrCreateCategory(name, type)` | New top-level `Food` category and child `Groceries` category created, both attached to the transaction | Empty fixture DB | Business layer written — `CategoriesTests.cs` (`GetOrCreateCategory`'s colon-splitting parent/child creation, and that calling it twice doesn't duplicate); the transaction-grid UI-wiring path itself is still untested |
| Rename category (Categories panel) | Right-click a category → **Rename**, type an existing category's name | `CategoriesControl.OnRenameNode_CommitAndStopEditing` (blocks rename onto an existing name per the page's own description) | Rename rejected/no-ops when name collides with an existing category; succeeds and renames in place otherwise | Fixture with `Fun:Movies` and `Fun:Videos` categories | Written — `CategoriesFlaUiTests.cs` (F2 rather than the right-click menu item, but the same `CommandRenameCategory`/`OnRenameNode_CommitAndStopEditing` path). Covers rename-onto-existing-name rejection and Escape-cancels-unchanged; found along the way that `OnTextEditorForRenaming_Loaded` places the caret at the end of the existing text (not select-all), so a real F2 rename needs Ctrl+A before typing or it appends instead of replacing |
| Delete category with no transactions | Right-click a category with zero transactions → **Delete** | `Category.OnDelete()` | Category removed immediately, no dialog | Fixture with an unused category | Written — `CategoriesTests.cs`. Real behavior confirmed by reading the code: `OnDelete()` is a soft delete (flips `ChangeType` to `Deleted`); the raw `Categories.Count` is unaffected, but `GetCategories()` (the "live" view) filters deleted categories out immediately - which is what the UI tree binds to |
| Delete category with transactions (redirect) | Right-click a category with transactions → **Delete**, pick a target category in the resulting `MergeCategoryDialog` | `MergeCategoryDialog` → `Transaction.ReCategorize(oldCategory, newCategory)` for each affected transaction, then `Category.OnDelete()` | All transactions previously on the deleted category now show the target category; deleted category gone from the tree | Fixture with a category that has 2+ transactions | Business layer written — `CategoriesTests.cs` covers the real bulk pattern (`Transactions.GetTransactionsByCategory` + per-transaction `ReCategorize`, matching `CategoriesControl.xaml.cs`'s own merge handler); `MergeCategoryDialog`'s UI wiring itself is still untested |
| Move category via drag/drop | Drag one category node onto another (no Ctrl) | `CategoriesControl.OnDragDropSourceOnTarget` → re-parents the category (no business-layer call — pure model mutation of `ParentCategory`) | Dragged category becomes a child of the drop target in the tree | Fixture with two sibling top-level categories | Not started |
| Merge category via Ctrl+drag/drop | Ctrl+drag one category onto another | `CategoriesControl.Merge(source, target)` → `Transaction.ReCategorize` per transaction, then `source.OnDelete()` | All of source's transactions now carry target's category; source category removed | Fixture with `Fun:Videos` (has transactions) and `Fun:Movies` | Business layer written — same `ReCategorize`/`OnDelete` coverage as the row above (`CategoriesTests.cs`); the Ctrl+drag UI wiring itself is still untested |
| Merge onto a category that doesn't exist yet (rename to new name under a different parent) | Drag/drop rename producing a `newName` not matching any existing category | `CategoriesControl.OnRenameNode_CommitAndStopEditing` → `Categories.GetOrCreateCategory(newName, type)` then `Merge` | New category created under the target parent, source's transactions recategorized onto it, source deleted | Fixture category with transactions | Not started |

### Payees & Aliases

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Simple payee rename (no alias) | Right-click a transaction → **Rename Payee**, enter new name, leave Auto-Rename unchecked, OK | `RenamePayeeDialog` → `Payees.FindPayee(value, true)`, `MyMoney.FindAliasMatches`, `MyMoney.ApplyAlias(alias, transactions)` | Renamed transaction(s) now show new payee; no `Alias` row created | Fixture with one messy downloaded payee name | Business layer written — `PayeesAndAliasesTests.cs` (`ApplyAlias` renames the matching transaction's payee). Dialog UI wiring itself still untested |
| Rename with Auto-Rename (creates an Alias) | Same, but check **Auto-Rename** | `RenamePayeeDialog` → `Aliases.AddAlias(alias)` + `ApplyAlias` | New `Alias` row persists mapping the raw string to the chosen payee; a later transaction with that exact payee string auto-renames on import/entry | Same fixture | Written — UI wiring: `PayeesFlaUiTests.RenamePayeeWithAutoRename_CreatesAliasAndRenamesTransaction` (real right-click → Rename Payee → Auto-Rename → OK, verified via both the transaction grid and the Aliases view). Found the fixture's pre-seeded "AMC THEATRES 1234" alias means this exercises the *re-point an existing alias* branch (`Aliases.FindAlias` finds it, so `AddAlias` is never called), not the *create new* branch - `Aliases.AddAlias` itself is still separately covered in `PayeesAndAliasesTests.cs`'s `FindSubsumedAliases` test. Business layer: `ApplyAlias`/`Aliases.AddAlias` (`PayeesAndAliasesTests.cs`) |
| Rename with no matching transactions found | Enter a pattern with Auto-Rename that matches nothing existing | `RenamePayeeDialog.OnOkButton_Click` → `MyMoney.FindAliasMatches` returns empty → confirmation prompt ("do you want to save it anyway?") | Dialog warns; Cancel keeps editing, OK still creates the alias with 0 transactions affected | Fixture with a pattern guaranteed not to match | Business layer written — `PayeesAndAliasesTests.cs`'s `FindAliasMatches_NoMatchingTransactions_ReturnsEmpty`. The dialog's "save it anyway?" confirmation-prompt wiring itself is still untested |
| Regex alias consolidation | Rename Payee dialog: enter a broader regex pattern (e.g. `.*ALASKA[ ]+AIR.*`) with Use-Regex + Auto-Rename checked, where several narrower existing aliases would be subsumed | `RenamePayeeDialog.CheckConflicts` → `MyMoney.FindSubsumedAliases(alias)`; on OK, `Aliases.RemoveAlias` for each subsumed alias, `Aliases.AddAlias` for the new one | Dialog's "clashing aliases" list shows the subsumed patterns before OK; after OK, those narrower `Alias` rows are gone and only the new regex alias remains | Fixture with 3+ narrow aliases (e.g. "ALASKA AIR 123", "ALASKA  AIRLINES") for the same payee | Written — `PayeesAndAliasesTests.cs`'s `FindSubsumedAliases_BroaderRegexSubsumesNarrowerAliases`, confirming the previously-unverified real semantics: `FindSubsumedAliases` tests each existing alias's own `Pattern` string as plain text against the broad alias's `Matches` (i.e. `broadAlias.Matches(narrowAlias.Pattern)`), not a pattern-vs-pattern string comparison. Dialog UI wiring (the "clashing aliases" list) still untested |
| Regex consolidation with conflicting payees | Same, but the subsumed aliases map to *different* payees than the new one | `RenamePayeeDialog.OnOkButton_Click`'s conflict-warning branch (lists conflicting payee names, up to 10 + "...") | Warning dialog appears before commit; Cancel aborts with no aliases touched | Fixture with subsumed aliases pointing at 2+ different payees | Not started |
| Delete an alias (View/Aliases) | `AliasesView` → select a row → Delete | `Aliases.RemoveAlias` | Alias removed; page's own note confirms this does *not* undo the original rename, only stops future auto-renames | Fixture with 1+ aliases | Not started |
| Alias regex that matches nothing | Create a regex alias whose pattern never matches any payee string | `Alias.Matches(string)` returns false for all inputs | Alias persists but has zero effect; no crash on save | Fixture DB, arbitrary regex like `^ZZZ_NEVER_MATCHES$` | Business-layer gap: `Alias.Matches` untested |

### Splits & Transfers

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Create a split with unassigned amount | Right-click a transaction → **Split**, add 2 split rows that don't sum to the transaction total | `Splits.Unassigned`/`HasUnassigned` (computed properties, `Money.cs` ~13260-13310) | Unassigned amount shown at bottom of split list; split button shows red outline | Fixture with a plain transaction | Written — `SplitsAndTransfersTests.cs`. Found `Unassigned` is not auto-recomputed from `AddSplit`/`Split.Amount` in a headless scenario (`Split.OnAmountChanged` is an empty method, and `Splits.AddSplit`'s `InsertItem` only fires property-changed notifications) — a direct business-layer caller must call `Splits.Rebalance()` explicitly; real UI code relies on WPF's grid plumbing to trigger it eventually |
| F6 balances a split | Focus a split's Payment field, press F6 | `TransactionsView` F6 handler reads `SelectedTransaction.NonNullSplits.Unassigned` and writes it into the focused cell | Split amount adjusts so total unassigned becomes 0 | Fixture with an out-of-balance split | Written — `SplitsAndTransfersFlaUiTests.cs`'s `F6OnOutOfBalanceSplit_FillsRemainingAmount` (right-click → Splits… → F6 on a fresh empty split row fills it with the full 60.00 unassigned amount) |
| Create a transfer by typing "Transfer to:" | In a transaction's Payee field, type `Transfer to: <account name>`, enter amount | `Transaction.PayeeOrTransferCaption` setter → `MyMoney.Transfer(t, account)` → `MyMoney.Rebalance(account)` | A matching transaction is auto-created in the target account, linked | Fixture with 2 accounts | Business layer written — `SplitsAndTransfersTests.cs`'s `Transfer_CreatesLinkedTransactionInTargetAccount` (`MyMoney.Transfer` directly; the "Transfer to:" text-parsing UI-wiring path itself is still untested) |
| Delete one side of an unreconciled transfer | Delete either transaction of a linked transfer pair | `MyMoney.RemoveTransfer(t)` | ~~Both sides removed~~ **Corrected**: only the *other* side (the transfer's `Transaction`/"target") is actually removed via `RemoveTransaction`; the side `RemoveTransfer` was called on just has its own `Transfer` link cleared and survives as an ordinary, non-transfer transaction | Fixture with a 2-sided transfer, neither side reconciled | Written — `SplitsAndTransfersTests.cs`'s `RemoveTransfer_UnreconciledBothSides_RemovesTargetSideOnly`, which corrects this row's own original "both sides removed" assumption after tracing `MyMoney.RemoveTransfer(Transfer)`'s real source/target roles |
| Delete one side of a reconciled transfer (blocked) | Delete a transfer transaction where the *other* side is reconciled | `MyMoney.RemoveTransfer` should refuse / the delete path should reject it | Delete is blocked, transaction unchanged, per the page's documented behavior | Fixture with one side marked reconciled | **Resolved / Written** — `SplitsAndTransfersTests.cs`'s `RemoveTransfer_PartnerIsReconciled_ThrowsAndLeavesBothSidesIntact`. Enforcement traced to `MyMoney.RemoveTransfer(Transfer t)`: when the other side's `Status == TransactionStatus.Reconciled`, it throws `MoneyException("Transfer is reconciled on the other side and cannot be modified outside of balancing the target account.")` before touching either side — a real exception, not a bool result or silent no-op |
| F12 create a transfer from a non-transfer transaction | Select a transaction with no transfer, press F12, confirm the prompt to link it to a matching transaction found in another account | Same `MyMoney.Transfer` path, entered via a different UI trigger | Transaction becomes a linked transfer | Fixture with a same-amount, same-date transaction sitting unlinked in two accounts | Not started |
| Split-transfer (transfer inside a Split row) | In a Split's Payee column, type a transfer target account | `Split`-level transfer creation (same `Transaction`/`MyMoney.Transfer` machinery, invoked per-split) | Linked transaction created in the target account for just that split's amount | Fixture with a multi-split deposit | Business-layer gap: split-level transfer path untested; also worth confirming the page's stated constraint ("can't have a split transfer go to a split on the other side") is actually enforced somewhere |

### Merging Duplicate Transactions

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Duplicate detected and merged via Merge button | Select a transaction; a dark-blue connector appears linking a detected duplicate; click **Merge** | `Transactions.FindPotentialDuplicate(t, tc, range)` (detection) → `TransactionsView.Merge(t, u, false)` (WPF-only survivor-selection logic) → `Transaction.Merge(other)` (business-layer field-preserving merge) | The two transactions collapse into one; a Category set on one and a Memo set on the other are both preserved (per the page's stated guarantee); the connector disappears | Fixture with two near-duplicate transactions (e.g. same $11.73 Target charge, different FID) | Written — business layer: `MergingDuplicateTransactionsTests.cs`. Found `FindPotentialDuplicate` requires the transaction itself to already be an element of the candidate list (`tc.IndexOf(t) > 0`) and only checks its immediate list-neighbors — it is NOT a scan of the whole list for anything matching `t`; a candidate list missing `t` itself returns null unconditionally, even with an exact duplicate present. UI wiring: `MergingDuplicateTransactionsFlaUiTests.cs` — the connector auto-appears (a `DispatcherTimer`-delayed `ShowPotentialDuplicates`) once a selected transaction has a nearby match, with no explicit "select one" step needed beyond opening the account; its "Merge" button is a code-created `RoundedButton` with no `AutomationId` (found via `ByName("Merge")` instead). The survivor-selection rules (reconciled/transfer/investment/unaccepted precedence) still live only in `TransactionsView.xaml.cs` with no business-layer seam — same pattern as `BalanceControl`'s embedded logic under Balancing Accounts, below. **UI/UX note**: filed as [markabrandjord/MyMoney.Net#42](https://github.com/markabrandjord/MyMoney.Net/issues/42) — this connector-overlay pattern isn't a standard Windows control; a comparison dialog was suggested as a better direction |
| Merge preserves attachments from the removed side | Same, where the non-surviving transaction has an attachment and the survivor doesn't | `AttachmentManager.MoveAttachments(u, t)` (WPF-project-only, no business-layer counterpart) | Attachment now appears on the surviving transaction | Fixture with an attachment on one of the two duplicates | Business-layer gap: `AttachmentManager` isn't migrated at all — see Attachments, below |
| Mark as not-a-duplicate | Click the "(x)" button on the duplicate connector instead of Merge | `TransactionsView.OnConnectorClosed` sets `Transaction.NotDuplicate = true` on both sides | Connector dismissed; re-selecting either transaction later does not re-prompt | Same fixture pair | Not started |
| Merge two transactions that are not actually duplicates (forced) | Manually invoke merge (e.g. via drag/drop per the page) on two unrelated transactions | Same `Transaction.Merge` path | Behavior when merging genuinely unrelated transactions (does it silently combine memo/category, does it warn?) — this is the closest thing to a "genuine failure/invalid input" case for this feature and isn't described as blocked anywhere | Fixture with two clearly-unrelated transactions | Partially resolved — `MergingDuplicateTransactionsTests.cs` found and tested two real guards: `Merge` throws `ApplicationException` when both sides are transfers to *different* accounts (an unresolvable conflict, not silently resolved), and returns `false` without merging any field when the duplicate's category is the synthetic "transfer to/from deleted account" placeholder. There is still no general "these two transactions aren't actually related" rejection — merging two arbitrary unrelated transactions with ordinary categories would still silently combine fields |

### Currencies & Securities

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Add a new currency row manually | `View/Currencies`, add a new row, type a valid 3-letter code and ratio | `CurrenciesView`'s grid `InsertItem` → `Currencies.AddCurrency(currency)` | New `Currency` persists; `CultureCode`/`Name` auto-populate from `Currency.GetCultureForCurrency` when left blank | Fixture DB | Written — business layer: `CurrenciesTests.cs` (`AddCurrency` persistence; also `MultiCurrencyTransactions_ComputeCorrectUsdEquivalent` covering EUR/CAD/GBP/JPY/AUD amount/ratio math). UI wiring: `CurrenciesFlaUiTests.cs` (`AddingNewCurrencyRow_PersistsWithEnteredCode`, the other 5 currencies CHF/MXN/INR/BRL/CNY + a fresh "SEK" add) - covers Symbol entry via the `FilteringComboBox` (F4 to open, type to filter, click match, Enter to commit) and persistence; the `CultureCode`/`Name` auto-fill-on-blank behavior specifically is still untested |
| Add a currency with an unrecognized/invalid code | Same, but type a nonsense 3-letter code like `ZZZ` | `Currency.GetCultureForCurrency("ZZZ")` | No rejection — silently falls back to `CultureInfo.CurrentCulture`, so Name/CultureCode auto-fill with the *wrong* locale instead of erroring; genuine "invalid input silently accepted" case | Fixture DB | Written — `CurrenciesTests.cs` confirms and documents the fallback-to-CurrentCulture behavior as intended reality, not a bug |
| Remove a currency | Select a currency row, delete it | `Currencies.RemoveCurrency(currency)` | Currency removed; any account/report referencing it should be checked for a validation guard or lack thereof | Fixture with an unused currency | Written — `CurrenciesTests.cs` (unused/not-yet-saved currency removed immediately, same `IsInserted`-gated immediate-removal path as `Categories.RemoveCategory`) |
| Edit stock split history for a security | `View/Securities`, expand a security's split history, add/edit a split row | `StockSplits.AddStockSplit`/`RemoveStockSplit` | Cost-basis calculations for that security update correctly | Fixture security with buy transactions | Already covered — `CostBasisTests.cs` and `DatabaseContractTests.SimpleEntities.cs` exercise this thoroughly; no new gap |
| Enter CUSIP for a new manually-added security | `View/Securities`, add a security with Type=Private and a CUSIP | `Security` domain object (plain property setters) | Security persists with CUSIP; future OFX/CSV import matching by CUSIP succeeds (that matching logic is Importers/Ofx-owned) | Fixture DB | Not started; low priority — thin property editor, no branching business logic of its own |

**Not fully verified**: no distinct UI entry point was found for `Security.Merge` (CUSIP/symbol dedup) — it's apparently only exercised from import/OFX paths, not a user-invoked "merge two securities" action from `Securities.md` itself.

### Auto-Categorization

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Empty category auto-fills from payee history | Enter an amount on a transaction for a known Payee, tab into the (empty) Category field | `AutoCategorization.AutoCategoryMatch(t, payeeOrTransferCaption)` (WPF-free, `MyMoney.Business/AutoCategorization.cs`) | Category field auto-populates with the statistically-closest category from that payee's transaction history | Fixture with several past "ARCO → Auto:Fuel" transactions at varying amounts | Written — `AutoCategorizationTests.cs` |
| No matching payee history (nothing to suggest) | Same, for a brand-new Payee never seen before | Same API, returns `null`/no match | Category field stays empty, no exception | Fixture with a payee that has no prior transactions | Written — `AutoCategorizationTests.cs` |
| Split-transaction history preferred/avoided per algorithm | Payee history contains both plain and split transactions at similar amounts | `AutoCategorization.AddPossibility`/`FindPreviousTransactionByPayee`'s split-vs-normal count comparison | Algorithm "avoids picking a matching transaction that contains a Split unless there's no other choice" per the page — verify this branch actually holds | Fixture with mixed split/non-split history for one payee | Business-layer gap: this specific documented heuristic is unverified |
| Zero-amount transaction falls back to closest-by-date | Enter a Payee with no Amount yet, then tab into Category | `FindPreviousTransactionByPayee`'s `amount == 0` branch (closest-by-date fallback) | Category from the most recent same-payee transaction is suggested, ignoring amount statistics | Fixture with payee history at various dates | Written — `AutoCategorizationTests.cs` |

### Quick Search & Advanced Queries

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Quick Search literal match | Type `costco` into the Quick Search box, Enter | `QuickFilterParser<T>.Parse` → `FilterKeyword`/`FilterLiteral.MatchSubstring` (WPF-free, `MyMoney.Business/Utilities/QuickFilterParser.cs`) | Only transactions containing "costco" (case-insensitive) shown | Fixture with Costco and non-Costco transactions | Written — business layer: `QuickFilterParserTests.cs`; UI wiring (incl. the Enter-to-commit behavior): `QuickSearchFlaUiTests.cs` |
| Quick Search boolean AND/OR/NOT/parens | Type `costco and fuel`, `costco or fuel`, `costco and (gas or fuel)`, `costco !gas`, `"costco &gas"` per the page's own examples | Same parser | Each expression's documented result set matches | Same fixture, richer data | Partially written — `QuickFilterParserTests.cs` covers AND, OR, and the quoted-literal-escapes-operators case; NOT (`!gas`) and parenthesized sub-expressions (`costco and (gas or fuel)`) still untested |
| Advanced Query builder, single-field match | `Query` menu, build a query row (e.g. Category = "Food:Groceries") | `Transactions.ExecuteQuery(QueryRow[])` (WPF-free, `MyMoney.Business/Query.cs` — lives on `MyMoney.Transactions`, not `MyMoney` itself, despite this row's original API note) | Only matching transactions (including matching splits, shown as read-only fake rows per the page) returned | Fixture with categorized transactions incl. splits | Written — `ExecuteQueryTests.cs` (single-field Equals + no-match cases; the split-row fake-Transaction path is still untested) |
| Advanced Query builder, multi-row AND/OR | Build 2+ query rows with a Conjunction of And/Or | `Transactions.ExecuteQuery` conjunction-combining logic | Result set matches the documented AND/OR semantics | Same fixture | Partially written — `ExecuteQueryTests.cs` covers a 2-row AND (and found/documented that `QueryRow.Matches(decimal)`'s `GreaterThan` is actually `>=`); multi-row OR still untested |
| Advanced Query on a value that matches nothing | Query for a Payee/Category that doesn't exist in the data | `Transactions.ExecuteQuery` | Empty result set, no exception | Fixture DB, deliberately-absent search value | Written — `ExecuteQueryTests.cs` |

**Also flagged**: `Source/WPF/MyMoney/Views/TransactionSelectors.cs` (the F5/F6/F7/F8 pivot selectors and the `TransactionFilterSelector` behind the Filtering dropdown) is pure logic with no WPF dependency but still lives in the WPF project, not `MyMoney.Business` — low-stakes (thin wrappers over already-tested methods) but a real "hasn't migrated yet" case.

### Attachments

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Add attachment via drag/drop or paste | Drag a file (or paste an image/rich text) onto a transaction | `AttachmentManager` (`Source/WPF/MyMoney/Attachments/AttachmentManager.cs` — filesystem-path/directory logic, **not migrated**, no business-layer counterpart) | Paperclip icon appears; file stored under `*.Attachments/<account>/<transactionId>` | Fixture DB + a sample file | Business-layer gap: `AttachmentManager` needs a WPF-free extraction (same precedent as `StatementManager`, under Statements below) before a meaningful business-layer test can exist |
| Delete an attachment | Open the attachment dialog, delete the selected item | Same (`AttachmentManager`) | File removed from disk and from the transaction's attachment list | Fixture with an existing attachment | Business-layer gap: same as above |
| Scan and crop a receipt | Click **Scan**, adjust crop boundary, save | `AttachmentDialog.xaml.cs` (scanner integration, WPF-only, no meaningful business logic to extract) | Cropped image saved as the attachment | Requires physical/virtual scanner hardware — likely impractical to automate | Business-layer gap (infra, not logic): scanner hardware dependency makes this low priority regardless of the `AttachmentManager` migration |

### Sample Data

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Populate sample data into a new empty database | Help menu → "Populate Sample Data" | `SampleDatabase.Create()` (WPF-facing) → `SampleDataLoader.LoadEmbeddedSampleData` + `Walkabout.Data.SampleDataGenerator` (both WPF-free) | Accounts, categories, transactions, and stock trades appear as described | New empty database | Already covered at the business-layer level — `SampleDataRegressionTests.cs`/`SampleDataLoaderTests.cs`. Only the dialog wiring (`SampleDatabaseOptions`) is unproven — Not started |
| Populate from a custom template file | Same, but use the dialog's browse button to pick a non-default `SampleData.xml` | `SampleDatabase.Create()`'s custom-path branch (reloads via `XmlSerializer` and rebuilds quotes to match the custom file's securities) | Custom accounts/securities appear instead of the default set | A hand-crafted alternate `SampleData.xml` | Not started |
| Export current data as a sample-data template | Help menu → "Export Sample Data" | `SampleDatabase.Export(path)` | Generated histogram-based template file opens; per the page, values are randomized/anonymized, not real user data | Fixture DB with real-looking transactions | Not started; verify the anonymization claim specifically — does exported data ever leak literal Payee/Memo strings? |

### Skipped in Basics (reference-only, no distinct testable action)

`install.md` (ClickOnce download page), `Updates.md` (ClickOnce update-check UI, outside app control), `Keyboard.md` (every shortcut with real logic — F12 transfer nav, F6 split-balance, Ctrl+Space accept/reconcile, Ctrl+Enter auto-categorize — is already captured under its owning feature above), `Calculator.md` (self-contained WPF control, no domain-model interaction), `Navigation.md` (thin selector wrappers already discussed under Quick Search), `Clipboard.md` (Copy/Cut/Paste glue with no business-layer counterpart and no failure mode beyond what's covered under Balancing Accounts), `Setup.md` (the initial database-location dialog is the same flow as File lifecycle, under Accounts), `Options.md` (flat settings panel, no branching business logic — database-password change/removal is already covered by `DataEnginePasswordGeneratorTests.cs`).

---

## Accounts

### File lifecycle

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| New database (SQLite) | File \| New \| SQLite (or the non-DEBUG default) | `DatabaseLifecycle.Open` via `MainWindow`'s call site | New `.mmdb` file created and opened, empty | New temp filename | Not started |
| Open database, no upgrade needed | File \| Open, pick a current-schema fixture | `DatabaseLifecycle.Open` | Opens normally, data visible | A current-schema fixture (e.g. reuse `PayeeSmokeTest.mmdb`) | Not started |
| Open database, upgrade required, user accepts | File \| Open an old-schema fixture, click **Yes** on the upgrade prompt | `DatabaseLifecycle.Open` → `IBusinessLayerUiCallback.ConfirmWithDetails` returns `true` → engine `Upgrade()` runs | Database upgrades and opens; password persisted before load (per `DatabaseLifecycleTests`' already-covered ordering) | An old-schema fixture (needs creating/checking in) | Business-layer gap: confirm `DatabaseLifecycleTests` actually has an accept-path case with a *real* pre-upgrade fixture, not just a faked accept; then write this |
| Open database, upgrade required, user declines | Same fixture, click **No** | Same path, `ConfirmWithDetails` returns `false` | Load aborts, no password written, no crash | Same old-schema fixture | Not started (business-layer decline path already covered by `DatabaseLifecycleTests` — this scenario only needs to prove the real dialog's No button actually returns `false` to the callback) |
| Save / Save As, each format | File \| Save, then Save As into `.mmdb`/`.xml`/`.bxml`/SQL CE `.sdf` | `MainWindow`'s `Save`/`SaveAs*` methods (not extracted — see the migration's own rationale for why) | File written, reopens with same data | A small fixture with a few transactions | Not started |
| Export to CSV | File \| Export \| CSV | `Exporters.Export`/`ExportPrompt` → `IBusinessLayerUiCallback.PromptSaveFileName`/`OpenExportedFile` | Exported file opens and contains expected rows | A small fixture | Not started (business-layer formatting already covered by `CsvTransactionFormatTests`/`ExportersTests` — this only proves the dialog wiring and that the file actually opens afterward) |
| Export current transaction view (context menu) | Right-click a transaction grid → **Export...** | `TransactionsView.OnCommandViewExport` → same `Exporters.ExportPrompt`/`Exporters.Export` API as File \| Export \| CSV | Exported file contains only the rows currently in view (investment columns vs. non-investment columns per `ContextMenu.md`'s table) | A small fixture with both a bank account and an investment account view open | Not started (same business-layer coverage as the row above — this only proves the *second* call site wires the same API with the right row subset/columns) |

### CSV import

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| CSV import, column mapping | File \| Import a CSV without a saved mapping | `IBusinessLayerUiCallback.PromptForCsvFieldMapping` → `CsvImportController.ImportCsv` | Mapping dialog appears pre-populated from headers; after mapping, transactions import correctly | A small CSV with a header row not matching a saved `CsvMap` | Not started (mapping/import logic covered by `CsvTransactionImporterTests`/`CsvImportControllerTests` — this proves the real dialog produces a `CsvMap` that round-trips correctly) |
| CSV import, account picker | Import a CSV without an "Account Number" column | `IBusinessLayerUiCallback.PickAccount` | Account-picker dialog appears; selected account receives the imported transactions | A small CSV with no account-number column | Not started |
| CSV import, malformed file | Import a CSV that fails to parse | `ShowError` (or whichever method now handles this) | Error dialog shown, app doesn't crash, no partial import | A deliberately malformed CSV | Not started |

### Stock quotes

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Stock quote download error | Trigger a quote download with a bad/expired API key | `IBusinessLayerUiCallback.ClearOutputLog`/`AppendErrorLog` | Output pane shows the error, log-file link (if present) actually opens the log | An intentionally-invalid API key in test settings | Not started |
| Configure Online Service (success) | Online menu \| Online Services... → pick twelvedata/yahoo, enter API key + per-minute/day/month limits | `OnlineServiceDialog` → `DatabaseSettings`/`StockQuoteManager` construction with `IStockQuoteService`/`StockQuoteThrottle` | Settings persist (reopen dialog shows same values); a subsequent download respects the entered throttle limits | A fake API key + small limit values (e.g. limit=1/minute) | Not started (throttle logic itself covered by `StockQuoteThrottleTests.cs` — this only proves the dialog's fields round-trip into `StockQuoteThrottle`/`DatabaseSettings` correctly) |
| Configure Online Service, invalid limit value | Enter a negative or non-numeric value in a rate-limit field | (none identified — no validation found in `OnlineServiceDialog.xaml.cs`) | Dialog should reject or clamp; currently unclear whether it does | Negative number / non-numeric text in a limit field | Business-layer gap: no validation seam found; needs a closer source read to confirm actual behavior before writing a test |
| Exchange rate download reflected in Currencies view | Add a non-USD currency account → triggers `ExchangeRateService.CreateOrUpdate` from `AccountDialog.OnCurrencyChanged` | `ExchangeRateService.CreateOrUpdate` (already tested — `ExchangeRateServiceTests.cs`) | `AccountDialog`'s rate textbox updates with the fetched rate; `CurrenciesView` reflects it | A non-USD currency code (e.g. "EUR") with a fake/mocked rate source | Not started (rate-fetch logic covered by `ExchangeRateServiceTests.cs` — this only proves `AccountDialog`'s currency combo wiring calls it and displays the result) |

### OFX download

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| OFX download, protocol error | Trigger a download against a mock/test server returning a malformed OFX response | `IBusinessLayerUiCallback.ConfirmWithDetails` (CHALLENGERQ/PINCHRQ paths) or the generic error path (`error.GetType().FullName + ": " + error.Message` + stack trace rendering, restored in the migration's Task 8) | Generated `OfxErrorTemplate.htm` report actually contains the exception type name and stack trace | A test OFX server/fixture returning a malformed response (needs building — may not exist yet) | Business-layer gap: no test infrastructure exists yet for driving a fake OFX server response; scope that before attempting this UI test |
| MFA challenge | Trigger a download requiring MFA | `MfaChallengeDialog` | Dialog renders challenge phrases from `MfaPhrases.xml` correctly | Needs a bank/test account that actually triggers MFA — likely the hardest scenario to set up | Not started, lowest priority (read-verified-only, no realistic manual step reaches this today) |
| Online Accounts dialog: bank list lookup + Connect | Online \| Download Accounts → pick a bank from `OFXBankList.md`'s ~338 entries, click Connect | `OfxInstitutionInfo` list (already in `MyMoney.Business/Ofx/OfxInstitutionInfo.cs`) → `OnlineAccountDialog.OnButtonVerify`/`GetProfile` (still fully WPF-embedded, ~1700 lines, no seam) | Profile response renders institution info + credential fields | A fixture/mock OFX profile response | Business-layer gap: `OnlineAccountDialog`'s signon/profile/account-matching orchestration has no business-layer extraction at all — the largest single gap found in this whole audit; the underlying `OfxObjectModel` parsing is tested (`OfxObjectModelTests.cs`) but the orchestration around it is not |
| Account matching: green check vs. question mark | After Connect, downloaded account numbers are matched against local accounts | `OnlineAccountDialog.AccountListItem` (WPF-embedded) — matching against `Account.OfxAccountId`/`AccountAlias` | Question-mark shown when no match; green check when `OfxAccountId` matches; clicking question-mark lets user pick/add the account | A profile response with one matched and one unmatched account number | Business-layer gap: same as above — matching logic lives entirely in `AccountListItem`, no unit-testable seam |
| Multi-factor authentication / AuthToken / Change Password dialogs | Bank challenges with MFA / AuthToken / ChangePassword during signon | `OnlineAccountDialog.HandleSignOnErrors`/`GetSignOnCode`/`PromptForAuthToken`/`PromptForPassword` → `MfaChallengeDialog`/`AuthTokenDialog`/`ChangePasswordDialog` | Correct dialog chosen per `OfxErrorCode`; only the MFA path is tracked above — AuthToken and ChangePassword paths aren't tracked anywhere yet | OFX signon responses with each error code | Business-layer gap: `GetSignOnCode`/`HandleSignOnErrors` dispatch logic is WPF-embedded with no seam |
| Multiple online accounts, same institution | Edit OnlineAccount "Name" to be unique | `OnlineAccountDialog` → `MyMoney.OnlineAccounts.AddOnlineAccount` (business-layer container exists in `Money.cs`) | Two `OnlineAccount` records with distinct names/credentials both persist and reconnect independently | Two `OnlineAccount` entries for the same bank with different Name/credentials | Not started |
| Deselect/disconnect an online account | Click the checkmark icon to toggle "disconnected" | `Account.OnlineAccount = null` or an `IsDisconnected`-style flag (need to confirm exact field) | Account no longer receives future downloads; can be reconnected later | An account with an active `OnlineAccount` link | Not started — need to confirm the exact persisted field for "disconnected" state before writing |
| Bank-specific OFX error codes | Connect against a bank returning one of the cataloged error codes (TYPE1 security, SSL trust failure, 400/404/500, etc.) | `OnlineAccountDialog.HandleSignOnErrors`/`GetSignOnCode` (WPF-embedded) | Correct, specific error message surfaces to the user, not just a generic failure | Mocked OFX responses reproducing a few of the cataloged codes | Business-layer gap: same dispatch logic as above; a special case of the "OFX download, protocol error" row above, worth folding in once that row's "no fake OFX server" gap is closed |

### Bank / Credit Card / Investment account setup

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Create a new Checking/Savings account | Accounts panel context menu → Setup Accounts → fill Name, Account Type = Checking, Opening Balance | `AccountDialog.HandleOk` → `Account` fields — `Account`/`Accounts` already WPF-free in `Money.cs` | New account appears in the Accounts panel under the right group, opening balance seeds the register | Name="Test Checking", OpeningBalance=1000.00 | Not started |
| Create account with invalid name | Type a name containing a char from `Accounts.InvalidNameChars` (`{ } : * " / \ < > \| ?`) | `AccountDialog.OnNameChanged` (pure WPF validation; `Accounts.InvalidNameChars` itself is business-layer data) | Textbox turns red, tooltip shows valid-chars message, OK button disabled | Name="Test/Account" | Not started |
| Account number silently truncated | Enter an Account Number/Online Account Id longer than 20 chars (or 50 for `OfxAccountId`) | `Account.OfxAccountId`/`AccountId` setters → `Truncate(...)` — no rejection or warning at any layer | Value is silently truncated, not rejected — no error shown to user | A 60-character account number string | Business-layer gap: no validation exists to test against; this scenario would document current silent-truncation behavior |
| Credit Card account shows negative balance in red | Create account with Type=Credit, add a charge transaction | Same `AccountDialog`/`Account` API; color is theme-driven | Balance displays as negative in the themed color; account listed under "Credit" group | Type=Credit, one $50 charge | Not started |
| Investment (Brokerage/Retirement) account shows Activity column + Portfolio tab | Create account with Type=Brokerage, add a Buy/Sell/Dividend transaction | `Account`/`Transaction.InvestmentType` (already WPF-free); Portfolio tab backed by `Reports/PortfolioReport.cs` → `CostBasisCalculator` (tested) | Transaction grid shows investment columns; second "Portfolio" tab renders holdings | Type=Brokerage, one Buy of 10 shares | Not started |
| Sell more shares than currently held (investment) | Enter a Sell transaction with Units exceeding current holding | `CostBasisCalculator`/`Transactions.Rebalance` | Behavior currently unverified — no test found for this oversell case | A security with 10 shares held, sell 20 | Business-layer gap: close with a direct `CostBasisCalculator`/oversell unit test first — establish the actual behavior (negative holding? exception? silently allowed?) before any UI test can assert on it |

### Loans

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Create Loan account + record initial loan amount | Setup Accounts \| Type=Loan → set Principal/Interest categories → enter initial loan transaction | `LoanDialog.ButtonOk` → `Account.CategoryForPrincipal`/`CategoryForInterest` → `MyMoney.GetOrCreateLoanAccount` → `Loan.GetLoanPaymentsAggregation` (all WPF-free, `Money_Loans.cs`) | Loan register groups payments by year, balance column shows remaining principal | A Loan account with Principal/Interest categories + one payment split | Not started (no `LoanTests.cs`/`Money_LoansTests.cs` exists — add direct coverage first) |
| Refinance: interest rate change recomputes principal/interest | Enter a new interest rate on an existing loan | `Loan.CalculatePercentageOfInterest`/`ComputeLoanAccountBalance` | Principal/interest split recomputes automatically for subsequent payments | A loan with several payments, then a rate change transaction | Not started (no direct unit test of `CalculatePercentageOfInterest`) |
| F12 jump to related payment transaction | Select a loan payment row, press F12 | `LoansView` → `CommandGotoRelatedTransaction` (WPF-embedded navigation) | Navigates to the split transaction in the source checking account | A loan with a payment sourced from a checking-account split | Not started |
| Loan with Principal/Interest categories not set | Create Loan account, leave categories null, add payments | `Loan.AddPaymentIfMatchingCategoriesForPrincipalOrInterest` returns `null` when either category is null | No payments show up in the loan register — silently, no error | A Loan account with categories left blank | Not started — cheap to unit-test directly since the early-return is already business-layer code |
| Invalid/degenerate interest rate | Force a scenario where `Interest`/`runningBalance` yields a nonsensical `Percentage` | `Loan.CalculatePercentageOfInterest` — no bounds checking | Confirm whether the UI just displays a nonsensical rate with no warning | A loan payment sequence that drives `runningBalance` negative or near-zero | Business-layer gap: no test covers this edge case |

### Assets

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Create Asset account, track value over time | Setup Accounts \| Type=Asset, Opening Balance = initial value, later add a transaction recording new appraised value | Same `AccountDialog`/`Account` API as bank accounts — no Asset-specific business logic found | Account shows in Asset group, contributes to Net Worth | Type=Asset, OpeningBalance=607000, later transaction +44000 | Not started |
| Asset with negative opening balance or negative value transaction | Enter a negative number in Opening Balance or a large negative transaction | (none found — no validation on `Account.OpeningBalance` or transaction `Amount` for Asset accounts) | Confirm whether the app accepts this silently | OpeningBalance = -50000 | Business-layer gap: no validation exists anywhere to test against |

### Account Aliases

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Add alias manually from Account dialog | In `AccountDialog`'s alias combo box, type text + Enter | `AccountDialog.OnAccountAliasesKeyDown` → `MyMoney.AccountAliases.AddAlias`/`FindAlias` (already WPF-free) | New `AccountAlias` created, appears in the combo dropdown | Alias text "XXXXXXXXXXXX12345" | Not started (no `AccountAliasTests.cs` exists) |
| Delete an alias via the close-box | Click the small close box on an alias item in the dropdown | `AccountDialog.OnAccountAliaseDeleted` → `AccountAliases.RemoveAlias` | Alias removed from both the UI list and the underlying collection | An account with one existing alias | Not started |
| Duplicate alias pattern moves to new account | Add an alias pattern that already exists on a different account | `AccountAliases.AddAlias(AccountAlias)` — explicit "duplicate, so perhaps user is trying to move this alias" branch, reassigns `AccountId` instead of erroring | Alias silently moves to the new account, no confirmation shown — worth surfacing as a UI-wiring concern | Same alias pattern entered on two different accounts | Not started — a surprising-but-not-broken success path worth an explicit test given no confirmation step exists |

### Balancing Accounts

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Balance an account successfully | Right-click account → Balance Account, enter statement date + ending balance matching computed balance | `BalanceControl.Reconcile`/`UpdateBalances` → `MyMoney.ReconciledBalance`/`EstimatedBalance` → `Done_Click` → `StatementManager.AddStatement` | "Off by" reaches $0, Done enabled, transactions marked Reconciled | A fixture account with known transactions and a matching statement ending balance | Not started |
| Balance with mismatched ending balance | Enter an ending balance that doesn't match the sum of marked-reconciled transactions | `BalanceControl.CheckDone` (WPF-embedded, no business-layer seam) | "Off by" shows nonzero delta, Done stays disabled; a sign-flip suggestion appears if the entered balance is the exact negation | Ending balance off by a small amount, and separately, the exact negated value | Business-layer gap: `CheckDone`'s delta/sign-flip logic has no seam or unit test — the underlying balance math is business-layer, but the reconciliation workflow *state machine* is not |
| Non-numeric/empty statement balance entered | Leave "Ending Balance" blank or type non-numeric text, tab away | `BalanceControl.CheckValidDecimal` (WPF-embedded, `decimal.TryParse` failure path) | Error MessageBox shown, focus returns to the textbox, Done stays disabled | Empty string, and separately "abc" | Business-layer gap: parsing/validation lives entirely in the dialog with no extracted, testable rule |
| Interest-earned auto-transaction | Enter a nonzero value in "Interest Earned" during balancing | `BalanceControl.TextBoxInterestEarned_LostFocus` → `MyMoney.Transactions.NewTransaction`/`AddTransaction` + `Rebalance` | A new interest transaction is created; clearing the field back to 0 removes the auto-created transaction | Statement with detected interest of $0, user enters $12.34 | Not started |
| Cancel out of balancing mid-way | Start balancing, mark some transactions reconciled, click Cancel | `BalanceControl.Cancel_Click`/`OnDone(true)` | Reconciliation is undone, but the docs explicitly flag that amount edits made *during* re-balance mode aren't safely rolled back on Cancel | A previously-balanced account re-entered into balance mode, with a reconciled transaction's amount edited, then Cancel | Business-layer gap: the page itself documents this as a known rough edge with no described safety net — worth a direct test establishing actual behavior |
| Re-edit a past reconciliation | Pick a previous statement date, change the statement balance, click Done | `BalanceControl.ComboBoxPreviousReconcileDates_SelectionChanged` → `StatementManager.UpdateStatement` (WPF app project, not `MyMoney.Business` — see Statements below) | Past statement highlights its reconciled transactions in green; changing its balance can throw off later statements per the page's own warning | An account with 2+ past reconciliations | Not started |
| Modify a Reconciled transaction outside balance mode | Try to change the Amount or Delete a transaction with Status=Reconciled, outside of Balance mode | (need to confirm exact enforcement point — likely a status check in `Money.cs`) | Edit/Delete is blocked | A Reconciled transaction, attempt an edit | Not started — should confirm which business-layer property enforces this before writing. **Not fully verified**: enforcement point not traced to a specific line. |

### Statements

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Attach a statement PDF while balancing | During Balance Account, click Browse next to Statement File, pick a PDF | `BalanceControl.OnBrowseStatement` → `StatementManager.AddStatement`/`ComputeHash` (plain C# class, `Source/WPF/MyMoney/Attachments/StatementManager.cs` — WPF-free logic, but not in `MyMoney.Business`) | PDF copied into `<dbname>.Statements/<account>/`, index.xml updated, "Goto Statement" becomes enabled | A small fixture PDF file | Business-layer gap: `StatementManager` has zero WPF dependencies but hasn't been migrated to `MyMoney.Business` — a strong migration candidate, entirely untested |
| Statement shared across two accounts (bank-consolidated statement) | Provide the *same* statement file for two different accounts' balancing sessions | `StatementManager.FindBundledStatement`/`ComputeHash` — hash-based dedup | Second account's "Goto Statement" opens the same physical file; only one copy exists on disk | Same PDF hash provided for account A then account B | Business-layer gap: good, self-contained unit-test target (pure file/hash logic) once extracted |
| Goto Statement from a reconciled transaction | Right-click a reconciled transaction → Go to statement | `StatementManager.GetStatementFullPath` → opens file via OS shell | Correct PDF opens for that transaction's statement period | A reconciled transaction with an associated statement | Not started |
| Renamed account keeps its statement history | Rename an account that has existing statements | `StatementManager.OnAccountRenamed`/`OnRenameAccountFolder`/`UpdateBundledPointers` | Statements directory renamed on disk, bundled-statement pointers in *other* accounts' indexes updated | An account with statements, renamed, plus a second account bundled to the same statement file | Business-layer gap: looks the most bug-prone of the `StatementManager` set, would benefit most from direct coverage |

### Cost Basis

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Portfolio Report reflects cost basis after a stock split | Open the investment account's Portfolio tab after recording a security + a stock split | `Reports/PortfolioReport.cs` → `CostBasisCalculator` (already tested — `CostBasisTests.SimpleCostBasis`) | Report shows adjusted quantity and cost-basis-per-share matching the doc's worked example | Reuse/mirror `CostBasisTests.SimpleCostBasis`'s fixture | Not started — thin render-only check; business logic is already covered |
| Tax Report splits multi-lot sale into separate gain/loss events | Sell shares acquired across multiple lots/dates, view Tax Report | `CapitalGainsTaxCalculator` (tested via `CostBasisTests.CostBasisAcrossTransfers`) | Report lists separate short/long-term events per lot per the doc's FIFO example | Reuse the multi-lot fixture pattern | Not started — same "thin render" caveat |
| Cost basis flows across an account transfer | Transfer shares between two investment accounts, then sell from the destination | `CostBasisCalculator` (already tested — `CostBasisTests.CostBasisAcrossTransfers`) | Tax Report on the destination account shows the *original* acquisition date/cost-basis | Reuse `CostBasisAcrossTransfers`'s fixture | Not started — business logic fully covered; only a report-rendering check would be new |

### Context Menu actions

Note: `Splits...`, `Delete`, `View Transaction by Account/Category/Payee/Security`, `View Security`, `Category Properties`, `One Line View`, `Show All Splits`, `Lookup Payee` are simple navigation/view-toggle commands with no meaningful business-layer surface — omitted as pure UI. `Export...` is covered under File lifecycle above. `Toggle Void`/reconcile-related items overlap Balancing Accounts above.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Recategorize all transactions in view | Right-click → Recategorize all..., pick From/To category, confirm | `TransactionsView.OnRecategorizeAll` (fully WPF-embedded, no business-layer method) | Every transaction in the current filtered view gets the new category; transactions outside the view untouched | A filtered view (e.g. by payee) with 3+ transactions | Business-layer gap: no `RecategorizeAll`-equivalent API exists on `MyMoney`/`Transactions` |
| Set Tax Date on a transaction | Right-click → Set Tax Date..., pick a different year/date | `TransactionsView.OnSetTaxDate` → `MyMoney.TransactionExtras.AddExtra`/`TransactionExtra.TaxDate` | `TransactionExtra.TaxDate` is set, affects Tax Report inclusion | A transaction, set tax date to prior year | Not started |
| Move a transaction to a different account | Right-click → Move..., pick destination account | `TransactionsView.OnMoveTransaction` → `AccountHelper.PickAccount` (WPF) → `t.Account = account` + `AttachmentManager.MoveAttachments` if applicable | Transaction (and its attachments) now appear under the destination account | A transaction with an attachment, target account different from source | Not started |
| Move a Reconciled transaction (blocked) | Right-click a Reconciled transaction → Move... | `TransactionsView.OnMoveTransaction` explicitly checks `t.Status == TransactionStatus.Reconciled` | MessageBox error shown, transaction stays in its original account | A Reconciled transaction | Not started — a clean, already-implemented failure case, good first candidate |
| Toggle Accept / Accept All | Right-click → Toggle Accept, or Accept All on a downloaded-transactions view | `TransactionsView.ToggleTransactionStateAccept` — no batch "AcceptAll" business-layer method | Bold/unaccepted styling clears; `UnacceptedReport` no longer lists it | A downloaded, unaccepted transaction | Not started |
| Toggle Void | Right-click → Toggle Void on a bad-check transaction | Sets `Transaction.Status = TransactionStatus.Void` (business-layer property) | Transaction excluded from balance/reconciliation | A transaction to void | Not started |
| Rename Payee with auto-alias | Right-click → Rename Payee, provide new name + set up an alias | `RenamePayeeDialog` → `Payees`/`Alias` APIs | Payee renamed on the transaction; future transactions matching the alias auto-rename | A transaction, rename its payee and add a matching alias pattern | Not started |

---

## Charts

### Method note

Read all 5 `docs/Charts/*.md` pages plus every `.cs` file in `Source/WPF/MyMoney/Charts/` and the graph-generator classes in `Source/WPF/MyMoney/Views/GraphGenerators.cs`. **All Charts scenarios are `Business-layer gap`** — the design spec's own survey predicted this and it holds: chart-data classes are either genuinely UI-coupled or, where portable, not yet extracted.

**Notable finding**: of the classes named in the migration design spec's survey, only `RentalData.cs` is genuinely WPF-free today (`ChartData.cs`/`CategoryData.cs` both carry `System.Windows.Media.Color`/`Brush`). More significantly, **`Source/WPF/MyMoney/Views/GraphGenerators.cs`** (`TransactionGraphGenerator`, `BrokerageAccountGraphGenerator`, `SecurityGraphGenerator`, behind the existing `IGraphGenerator` interface) has **zero `System.Windows.*` references** and is the real data-prep feeder for both the Trend Graph and Stock Chart — a genuine cheap-win extraction candidate, on the same footing as the already-completed StockQuotes migration. By contrast, the pie-chart tally logic (`CategoryChart.xaml.cs`) and history-chart bucketing (`HistoryBarChart.xaml.cs`) are algorithmically simple but are private methods baked into `UserControl` subclasses holding `Brush`-typed state — an extraction there would need a new seam cut, not just a file move.

### Chart tab switching

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Switch between chart tabs (Trends/History/Expenses/Income/Stock/Loan/Rental) for the current view | Click a chart tab below the transaction register | (none — tab visibility plumbing lives in `MainWindow.xaml.cs`) | Correct tab set shown/enabled per view type; a previously-selected tab that becomes invalid falls back to Trends | Fixture with a regular account, a security, a loan, and a rental building | Business-layer gap |

### History Chart

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| View history chart by category or payee, default Year range (up to 24 years) | Switch Transaction view to By Category/By Payee, open "History" tab | (none — data prep is `MainWindow.UpdateHistoryChart`; bucketing is `HistoryBarChart.UpdateChart`/`AddColumn`, entangled with `Brush`) | Column count/labels/amounts match transactions aggregated per year | Fixture with transactions spanning several years in one category, including at least one investment Add/Remove | Business-layer gap |
| Change range dropdown to Month (last 24) or Day (last 31) | Select Month/Day in the range dropdown | Same as above | Correct bucket boundaries and count | Same fixture with dense recent activity | Business-layer gap |
| Fiscal Year Start shifts year boundaries and labels columns "FYnn" | Set Fiscal Year Start via Options, view History tab in Year mode | (none — `AddColumn`'s `fiscalYearStart > 0` branch) | Column boundaries start on the configured month; label reads "FY" + following calendar year | Fixture + a non-January fiscal year start | Business-layer gap |
| Click/drill into a column | Left-click a bar in the History chart | (none — `HistoryBarChart.OnColumnClicked` → `MainWindow` filters `TransactionsView`) | Transaction view filters to that column's date range; back button restores prior view | Same fixture | Business-layer gap |
| Right-click Export to CSV | Right-click chart → Export | `ChartData.Export()` (writes CSV, opens via shell) | Exported CSV's rows/columns match the chart's series/labels/values | Same fixture | Business-layer gap — note this specific method doesn't itself touch `Color`, a near-miss for portability |
| Edge case: history chart with zero transactions in view | Open History tab on an empty/filtered-to-nothing view | n/a | `HistoryBarChart.UpdateChart` guards empty input, no crash | Fixture account/category with no transactions | Business-layer gap |

### Pie Charts

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| View Income/Expense pie charts by category | Select an account/category/payee view; open Expenses/Income pie tabs | (none — aggregation is private methods inside `CategoryChart.xaml.cs`) | Slice values/labels equal sums of transaction/split amounts grouped by root category, respecting transfers and unassigned-split handling | Fixture with categorized income/expense transactions, at least one split, one transfer, one uncategorized transaction | Business-layer gap |
| Click a pie slice to pivot into that category's transactions | Left-click a slice, then click the Expenses tab again | (none — `CategoryChart.OnPieSliceClicked` → `MainWindow.PieChartSelectionChanged`) | Transaction view filters to the clicked category; re-opening shows children as new slices | Fixture with a parent category having 2+ child categories | Business-layer gap |
| Toggle a category's visibility via the legend swatch | Click the color swatch next to a legend entry | (none — `CategoryChart.OnLegendToggled`) | Hidden slice disappears, amount excluded from displayed total; clicking again restores it | Fixture with 3+ categories, one dominating in size | Business-layer gap |
| Edge case: hide every category / all categories net to zero | Toggle off every legend entry, or use data that nets to $0 | n/a | Total shows $0.00, no crash | Fixture where expense and matching refund cancel to zero | Business-layer gap |
| Export pie chart data to CSV | Right-click chart → Export | `CategoryChart.OnExport` → `ChartData.Export()` | Exported CSV rows match visible (non-hidden) slice labels/values | Same fixture | Business-layer gap |

### Stock Chart

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| View a security's full price history | Switch to "By Security", select a security with quote history, open "Stock" tab | `SecurityGraphGenerator` (`Views/GraphGenerators.cs` — **zero `System.Windows.*` references**, behind `IGraphGenerator`) | Plotted points' dates/values equal `StockQuoteHistory.History` closes in date order | A security with a checked-in stock-quote fixture | Business-layer gap — real cheap-win candidate: `GraphGenerators.cs` looks portable today, just needs moving + a test |
| Edge case: security has no stock quote history | Select a security with no downloaded quotes | Same as above | Stock tab doesn't appear, or renders empty without crashing | Security fixture with empty/absent quote history | Business-layer gap |
| Edge case: missing stock-split data produces a spurious vertical decline | View a security whose price actually split but has no matching `StockSplit` record | `SecurityGraphGenerator.Generate()` plots raw `.Close` values with **no split adjustment applied at all** | Confirms this documented gotcha is a genuine code-level artifact, not just a UI glitch — good regression fixture: same security with vs. without the split entity | A security's quote history spanning a real split, with and without the `StockSplit` entity | Business-layer gap |
| "Same context menu items as Trend Graph" (timeframe / Export) | Right-click the Stock chart | Same wiring as Trend Graph, below | See Trend Graph rows | Same security fixture | Business-layer gap |

### Trend Graph

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| View an account's balance (or brokerage/retirement market value) over time | Select an account, open "Trends" tab | `TransactionGraphGenerator`/`BrokerageAccountGraphGenerator.Prepare()`/`Generate()` (`Views/GraphGenerators.cs`, **zero `System.Windows.*` references**) | Plotted running balance/market value matches an independently-computed running total | A regular checking-account fixture; a brokerage-account fixture with buys/sells and a pending `StockSplit` | Business-layer gap — same cheap-win candidate as Stock Chart, same file |
| Hover the mouse over the trend line | Move mouse over the chart | (none — pure UI) | Tooltip/pointer follows mouse, shows correct date/balance | Any populated fixture | Business-layer gap — genuinely UI, no extraction candidate |
| Click on the trend line to scroll transaction view to that date | Left-click on the chart | (none — UI event wiring) | Transaction view scrolls/selects the transaction at the clicked point | Same fixture | Business-layer gap |
| Right-click → change timeframe and Add Series to compare periods | Right-click chart, pick timeframe, "Add Series" | (none — still calls the same `IGraphGenerator`s per series) | Two series render with correct per-period date ranges | Fixture with 2+ years of transaction history | Business-layer gap |
| Export button → CSV | Click Export | `ChartData.Export()` (same path as History/Pie) | Exported CSV matches the currently displayed series/values | Same fixture | Business-layer gap |
| Edge case: date range with zero transactions | Select a timeframe with no data | (none) | Chart renders empty/flat without crashing | Fixture account with opening balance only | Business-layer gap |

---

## Reports

### Note regarding method

Read all 10 `docs/Reports/*.md` pages and the corresponding `Source/WPF/MyMoney/Reports/*.cs` implementations. The design spec's framing holds up: every report's `Generate(IReportWriter writer)` interleaves computation with direct writer calls — no separable "compute the numbers" step exists. **Essentially every scenario below is a `Business-layer gap`**, cataloged as *future API requirements* rather than existing seams. All reports pull from the live `MyMoney` graph via ad hoc LINQ, confirming `IDatabase` only supports whole-graph `Load()`, not filtered queries.

#### Net Worth Report

Parameters: report date (drives historical drill-down), normalized currency; filters accounts by type/tax-status.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Net worth as-of today, mixed accounts | Reports \| Net Worth | Would need: `ComputeNetWorth(MyMoney, DateTime, currencyCode)` returning per-category totals, decoupled from `IReportWriter` | Total matches sum of bank cash + investment cash + securities (by tax status) − credit − loans + assets | Fixture with checking, brokerage, 401k, Roth IRA, one Asset, one credit card, one loan | Business-layer gap |
| Net worth with zero accounts | Open a brand-new/empty database, run Net Worth report | Same API against an empty `MyMoney` | Report renders with all-zero totals, no crash | Empty/new database | Business-layer gap |
| Net worth on a historical date with quote history | Edit the report date field to a past date | Same API, pulls prices via `StockQuoteCache.GetSecurityMarketPrice` | Total reflects historical security prices, not current | Fixture with historical stock quote data | Business-layer gap |
| Net worth with a security missing SecurityType | Run report where a held security has `SecurityType.None` | Same API; needs to surface a "has-none-type" flag decoupled from the writer | Report/API signals the None-type warning without inspecting writer output | Fixture with one security with unset SecurityType | Business-layer gap |
| Net worth historical bar-chart series (5yr back) | Chart populates in background after initial render | Would need: `GetHistoricalNetWorth(MyMoney, endDate, yearsBack)` independent of the `AnimatingBarChart` control it's written directly into | Series values match per-year cash-balance/portfolio results | Fixture spanning several years of transactions | Business-layer gap |

Future-API filter vocabulary: date (point-in-time + historical range), currency normalization, account-type predicates (currently ad hoc lambdas).

#### Investment Portfolio Report

Parameters: report date; optional single account; optional `SecurityGroup`/`AccountGroup` drill-down. Uses `CostBasisCalculator` (FIFO) grouped by tax status and security type.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| All-up portfolio summary, mixed tax statuses | Reports \| Investment Portfolio (no account selected) | Would need: `ComputePortfolioSummary(MyMoney, date)` → grouped holdings with market value/gain-loss, independent of the writer | Table totals match sum of held lots' current market value; Gain/Loss matches `MarketValue - TotalCostBasis` | Fixture with holdings across a taxable brokerage, a 401k, a Roth IRA, same security in multiple lots | Business-layer gap |
| Single-account portfolio (Portfolio tab) | Click Portfolio tab on one investment account | Same, account-filtered | Shows only that account's holdings; cash/transfers between accounts excluded | Fixture with 2+ investment accounts, transferred security | Business-layer gap |
| Expand a security group to FIFO lot detail | Click expander triangle on e.g. "IBM corp total" | Would need: `GetLotDetail(MyMoney, date, security, accountFilter)` | Each row's Quantity/Cost Basis/Unit Cost matches a distinct buy lot | Fixture with multiple buys at different prices/dates, one partial sell | Business-layer gap |
| Portfolio with pending (unmatched) sales | Sell more shares than FIFO can currently match | Would need the future API to expose `CostBasisCalculator.GetPendingSales()` as data, not inline text | Pending-sale warning surfaces with account/units/security/date | Fixture with a sale lacking a matching buy lot | Business-layer gap |
| Zero holdings (no investment accounts) | Run report on a database with no investment accounts | Same summary API against empty holdings | Report shows empty/"N/A" sections, no crash | Database with only bank/credit accounts | Business-layer gap |
| CSV export of portfolio | Click Export button | `PortfolioReport.Export(filename)` → `CsvReportWriter` (already synchronous/writer-based) | Exported CSV contains same rows as on-screen report | Small portfolio fixture | Business-layer gap (still routes through the same `Generate`/writer interleaving) |

Future-API filter vocabulary: date, account filter (single vs. all), tax-status grouping, security-type grouping, FIFO lot detail on demand — the richest read-query shape in this category.

#### Cash Flow Report

Parameters: start/end date range (default 5yr), By Years vs. By Month, normalized currency.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| 5-year default cash flow, by year | Reports \| Cash Flow (default range) | Would need: `ComputeCashFlow(MyMoney, startDate, endDate, ByYear\|ByMonth)` → category-tree totals per period, decoupled from the writer | Income/Expense/Investments/Unknown group totals per year match manual sums; "Net cash flow" footer = sum of all | Fixture spanning 5+ years with income, expense, investment-type categories | Business-layer gap |
| Change to 12-month monthly view | Switch interval dropdown, narrow date range to 1 year | Same API, `byMonth=true` | 12 monthly columns; totals reconcile to the yearly view | Same fixture, 1-year window | Business-layer gap |
| Uncategorized / unbalanced-split transactions | Transactions with no category, or splits that don't sum to the total | Would need an explicit "Unknown" bucket and a separate unassigned-split bucket | "Unknown" row appears with correct total; unassigned-split amounts separately identifiable | Fixture with one uncategorized transaction and one split not summing to total | Business-layer gap |
| Zero transactions in range | Set date range with no transactions | Same API against an empty set | All totals zero, no crash | Fixture, date range outside all transaction dates | Business-layer gap |
| CSV export | Click Export button | `CashFlowReport.Export(filename)` | CSV rows match the on-screen category breakdown per period | Small fixture | Business-layer gap |
| Drill down from a cell to transactions | Click a blue linked number in a category/period cell | Would need the future API to keep the transaction list backing each cell addressable, not embedded in a UI-click-tied structure | Clicking returns exactly the transactions tallied into that cell | Fixture with a category having 2+ transactions in the same period | Business-layer gap |

Future-API filter vocabulary: date range, period granularity (year/month), category-type bucketing with parent/child roll-up, normalized currency, split-transaction handling; uses `Transaction.TaxDate`, not raw `Date`.

#### Tax Report (Income Tax Report)

Parameters: tax year (fiscal-year-aware), Investments Only vs. All Accounts, consolidate-on-date-sold. Skips tax-deferred/tax-free accounts for capital gains.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Income tax report for a year with tax categories set | Reports \| Income Tax Report, pick a year | Would need: `ComputeTaxCategoryTotals(...)` plus `ComputeCapitalGains(...)`, decoupled from the writer | Per-tax-category totals match category-tagged sums; long/short-term gains split matches holding-period rules | Fixture with Tax-Category-tagged categories, taxable-account sales spanning both <1yr and >1yr holds | Business-layer gap |
| Year with no tax categories configured | Run report on a database where no category has a Tax Category set | Same API — already returns `null` in this case | Report/API signals "no tax categories associated" distinctly from "zero activity" | Fixture with categorized transactions but no Tax Category assignments | Business-layer gap |
| Investments Only, tax-deferred/tax-free accounts present | Toggle "Investments Only" | Same capital-gains API, filtered; tax-deferred/tax-free explicitly excluded | 401k/Roth sales never appear in gains output | Fixture with sales in a taxable brokerage AND a 401k in the same year | Business-layer gap |
| Estimated tax payment misfiled to wrong tax year | Right-click a transaction, "Set Tax Year…", assign to prior year, re-run report | Would need the future API to key off `Transaction.TaxDate` override | Transaction moves from one year's report to the other after the override | Fixture with a January estimated-tax-payment transaction | Business-layer gap — flag as a candidate File-lifecycle-style UI-wiring scenario once the underlying API is unit-testable |
| Capital gains with unknown cost basis | Sell a security lacking cost-basis/lot data | Same API; `CapitalGainsTaxCalculator.Unknown` already separates this case | "Capital Gains with Unknown Cost Basis" section populated distinctly | Fixture with a sale lacking a recorded purchase lot | Business-layer gap |
| .txf export for TurboTax | Click Export, save .txf | `TxfExporter.Export(filename, startDate, endDate, investmentsOnly, consolidateOnDateSold)` — already a plain file-writing API, no `IReportWriter` involved | Exported .txf matches expected TXF record format | Same fixture as above | **Not a gap** — this export path is already business-layer-clean; could get a direct unit test now |
| Empty year (no transactions at all) | Pick a tax year with zero transactions | Same APIs against an empty range | "No categories associated" vs. legitimately-empty year are currently indistinguishable — future API should let the caller tell these apart | Fixture with data only outside the selected year | Business-layer gap |

Future-API filter vocabulary: fiscal year with configurable start offset, investments-only vs. all-accounts, consolidate-on-date-sold vs. per-lot, tax-deferred/tax-free exclusion. `TxfExporter.Export` is the one already-decoupled path in the whole Reports category.

#### W2/Other Tax Forms Report

Parameters: tax year only (narrower than Tax Report — no account/investments toggle).

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| W2/tax-form summary with categories configured | Reports \| W3 and Other Tax Forms, pick a year | Would need: `ComputeTaxFormTotals(MyMoney, startDate, endDate)` decoupled from the writer | Per-tax-category total matches sum of transactions/splits tagged with that category's `TaxRefNum` | Fixture with categories set to 2+ different Tax Categories under the same form | Business-layer gap |
| No categories have a Tax Category set | Run report with no Tax Category associations | Same API, should return "empty" distinctly | Report reports "no tax category associations" rather than silently empty | Fixture with categorized transactions but no Tax Category assignments | Business-layer gap |
| Misfiled estimated payment (Jan payment for prior year) | Same "Set Tax Year…" fix as Tax Report, re-run | Same tax-year-override concern | Transaction counts toward the corrected year | Fixture with a January transaction meant for the prior tax year | Business-layer gap |
| Split transaction with multiple tax-relevant categories | A paycheck deposit split across gross pay / withholding / 401k categories | Would need the future API's tally to handle per-split category lookup, currently correct but embedded in the report class | Each split's amount attributes to its own Tax Category bucket | Fixture with a multi-split paycheck deposit | Business-layer gap |

#### Unaccepted Report

No date/filter parameters exposed — fixed, all-accounts, all-time scan.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Unaccepted transactions across multiple accounts | Reports \| Unaccepted Transactions | Would need: `GetUnacceptedTransactions(MyMoney)` decoupled from the writer | Each account section lists exactly its unaccepted transactions; closed accounts excluded; count footer matches list length | Fixture with unaccepted transactions in 2+ open accounts plus one closed account | Business-layer gap |
| Zero unaccepted transactions | Run report where everything's accepted/reconciled | Same API against all-accepted data | "Found 0 unaccepted transactions", no per-account sections, no crash | Fixture with all transactions accepted | Business-layer gap |
| Toggle Accept on a transaction, then re-run | Accept a transaction, regenerate report | Really testing the existing accept-toggle logic (may already be covered elsewhere) plus this report's read | Transaction disappears from the report after acceptance | Fixture with one unaccepted transaction | Business-layer gap for the report read; check first whether Toggle-Accept itself already has non-UI coverage |

Future-API filter vocabulary: simplest of the category — "all transactions across all open accounts where `Unaccepted == true`, grouped by account."

#### Future Bills Report

No user-facing filter controls; internally hardcoded 5-year lookback / 12-month forecast.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Recurring expense forecast, typical data | Reports \| Future Bills | Would need: `ForecastRecurringPayments(MyMoney, asOfDate, monthsAhead)` wrapping the already-plain `Payments.FindRecurringPayments` | Each month's predicted payments match `RecurringPayment.GetNextPrediction()`; year total matches sum | Fixture with 2+ recurring Expense-category transactions with `Category.Frequency` set | Business-layer gap |
| No recurring payments found | Data with no Expense category having a Frequency set / too little history | Same API against non-recurring data | Empty result, "No recurring payments found", no crash | Fixture with only one-off expense transactions | Business-layer gap |
| Force a category into the report via Frequency override | Categories dialog: set a category's Frequency to non-None | A category-configuration write, separate from the report read | Category appears in next run even with sparse history | Fixture category with Frequency manually set | Flag as out-of-category (Categories dialog), not a Reports gap |
| Transactions older than the 5-year lookback window | Data has only a single very old occurrence (>5yrs ago) | Same forecast API — validates the hardcoded 5-year trim once extracted as a parameter | Old-only payment does NOT appear in forecast | Fixture with one old Expense transaction, no recent recurrence | Business-layer gap |

Two currently-hardcoded internal parameters (5yr lookback, 12mo horizon) should become real parameters in the future API.

#### Account Summary Report

Parameters: report date, normalized currency. Iterates every `AccountType`, summing cash + investment value per account.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Account summary as of today, default currency | Reports \| Account Summary | Would need: `ComputeAccountSummary(MyMoney, date, currencyCode?)` decoupled from the writer | Each account's row balance matches `Transactions.GetBalance(...)`; grand total only shown when a single common currency applies | Fixture with accounts across several `AccountType`s | Business-layer gap |
| Normalized to a foreign currency (e.g. AUD) | Set normalized currency to AUD | Same API, ratio conversion applied | All amounts converted; common-symbol total shown in AUD | Fixture + a Currency entry for AUD with a defined ratio | Business-layer gap |
| Mixed currencies produce no single total | Accounts in genuinely different currencies, normalization off | Same API; `various` flag suppresses the grand total | No misleading single total shown | Fixture with 2+ accounts in different native currencies | Business-layer gap |
| Zero accounts of a given type | e.g. no Loan accounts exist | Same API; per-type section omitted | No empty/blank section appears | Fixture missing one or more `AccountType`s | Business-layer gap |
| Historical date with stock-quote-dependent investment value | Change report date to the past | Same API, dependent on `StockQuoteCache` | Investment portion reflects historical price | Fixture with historical stock quotes and an investment account | Business-layer gap |

#### Retirement Plan Report

**Architecturally the outlier of the ten reports** — not a filter/subtotal query but a multi-decade tax/withdrawal simulation engine. `RetirementPlanState` exposes ~15 scalar/enum inputs: ROI, inflation, desired income, current/spouse age, filing status/state, retirement/graduation age, Roth-conversion strategy + years, Social Security amount/age/COLA, tax-bracket inflation. RMD age hardcoded to 75.

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Baseline retirement simulation, no Roth conversion | Enter retirement settings, run report | Would need: `RunRetirementSimulation(MyMoney, RetirementPlanState, stockQuoteCache, stateTaxData)` returning year-by-year series as plain data | Assets-remaining total and per-tax-status breakdown match a hand-computed projection for a deterministic fixture | Fixture with taxable brokerage, 401k, Roth IRA with known balances, round-number ROI/inflation | Business-layer gap |
| Unrealistic retirement age (retire before current age, or past graduation age) | Set `RetirementAge` < `CurrentAge`, or > `GraduationAge` | Same API — needs to define/validate behavior for this invalid input (no visible guard today) | Simulation either rejects clearly or degrades gracefully rather than looping incorrectly | Fixture with `RetirementAge` below `CurrentAge` | Business-layer gap — also a genuine input-validation hole to design, not just a test gap |
| RMD kicks in at age 75 with a large tax-deferred balance | Simulate past age 75 with substantial 401k funds | Same API; exercises the RMD withdrawal branch | Forced RMD amount matches `TaxDeferred / RMDFactor(age)`; taxes jump that year | Fixture with a large 401k balance, simulation crossing age 75 | Business-layer gap |
| Roth conversion strategy comparison | Set Roth Conversion strategy, or run the built-in sweep (0-15 years) | Same API's `RunRothSimulation()` sweep | Each sweep point matches a full independent run with that conversion-years value | Same fixture, varying conversion years 0-15 | Business-layer gap |
| Social security claimed early vs. late (62 vs. 67 vs. 70) | Change Social Security claim age | Same API; exercises spousal SS/COLA compounding | Lifetime SS income and drawdown differ correctly by claim age | Fixture with SS amount set, run three ages | Business-layer gap |
| No tax-deferred accounts at all | Run simulation with only taxable/tax-free holdings | Same API; Tax-Deferred-Strategy control hidden when none exist | Simulation runs without RMD/Roth-conversion logic engaging | Fixture with only a taxable brokerage and a Roth IRA | Business-layer gap |
| Dividend income projection with sparse dividend history | Run simulation on an account with few/no dividend transactions | Same API's dividend-yield projection | With insufficient history, projected dividend income is 0 or clearly flagged, not spurious | Fixture with zero/near-zero dividend history | Business-layer gap |
| Different state tax setting | Change `TaxFilingState` | Same API; drives state tax via `StateTaxes.Load()` | Tax totals differ correctly between a no-income-tax state and a high-tax state | Same fixture, two `TaxFilingState` values | Business-layer gap |

A future business-layer API for Retirement Plan needs its own design thought entirely separate from "filter transactions, subtotal by X" — it's a simulation engine, not a query.

### Cross-report observations for a future business-layer query API

Filter/parameter vocabulary observed across all ten reports:

- **Date/date-range**: point-in-time (Net Worth, Portfolio, Account Summary), explicit start+end (Cash Flow), fiscal-year-aware with configurable offset (Tax Report, W2), multi-year forward projection (Future Bills: 12mo; Retirement Plan: decades).
- **Account filtering**: by `AccountType` enum, by tax-status flags (`IsTaxDeferred`/`IsTaxFree`), single account vs. all, open/closed.
- **Category filtering/grouping**: by `CategoryType` with parent/child roll-up (Cash Flow), by `Category.TaxRefNum` (Tax/W2), by `Category.Frequency` (Future Bills).
- **Transaction-status filtering**: `Unaccepted` flag, `Status == Void`/`IsDeleted` exclusion (done ad hoc inline in nearly every report), split handling with per-split category + unassigned remainder.
- **Currency normalization**: present in Net Worth, Portfolio, Account Summary, Cash Flow; absent elsewhere.
- **Security/holdings queries**: FIFO lot matching + market-value-as-of-date, the richest sub-shape, independently reimplemented in three reports.
- **No filter surface at all**: Unaccepted Report, Future Bills (hardcoded windows).

---

## Development

No scenarios. `docs/dev/index.md` is entirely developer/contributor documentation (build steps, VS Test Explorer, DGML scenario-test tooling, the project dependency diagram) — no end-user application behavior described anywhere on the page.

---

## Undocumented in help file — Menu & API audit

Found by reading `Source/WPF/MyMoney/MainWindow.xaml`'s full menu structure and `Source/WPF/MyMoney.Business/`'s public surface directly, independent of whether any help page mentions them. Items already owned by Taxes/StockQuotes/Importers/Ofx/DatabaseLifecycle/Charts/Reports/core account-CRUD/AutoCategorization/CostBasis/Loans/SampleData/Currencies/Options/Aliases/QuickSearch/Queries are excluded (covered above). Command definitions live in `Source/WPF/MyMoney/Commands/Commands.cs`.

### File menu items not covered elsewhere

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Recent Files (MRU) | File \| Recent Files, click a prior database | None — `RecentFilesMenu` owns the list via `Settings`, outside `MyMoney.Business` entirely | Clicking an entry reopens that database via the normal `DatabaseLifecycle.Open` path; a missing/moved file is handled, not a crash | Open two small fixture dbs in sequence to populate the MRU list | Business-layer gap |
| Open Containing Folder | File \| Open Containing Folder... | None — calls `NativeMethods.ShellExecute("explore", dir)` directly | Explorer opens showing the folder; disabled when no db is open or its directory doesn't exist | Any open fixture db | Business-layer gap — trivial OS shell-out, low priority |
| Export Accounts Dependencies (DGML) | File \| Export Accounts Dependencies (DGML)..., pick save location | `Exporters.ExportDgmlAccountMap(MyMoney, fileName)` | Valid DGML/XML written describing account transfer relationships | Fixture db with several accounts linked by transfers | Business-layer gap — `ExportersTests.cs` covers other exporters but not this one; the caller also has no try/catch around export+shell-open |
| Associate QIF, QFX, OFX and MMDB | File \| Associate QIF, QFX, OFX and MMDB | `FileAssociation.Associate(ext, path)` — writes `HKCU\Software\Classes\...` registry keys directly | Success: registry keys point the four extensions at this app, confirmation shown. Failure: caught, error MessageBox shown | N/A — real HKCU registry mutation | Business-layer gap; a FlaUI test here leaves real registry changes on the test machine — recommend read-only/manual verification, not automated |
| Add User (SQL Server) | File \| Add User..., enabled only when the open database is `SqlServerDatabase` | `SqlServerDatabase.AddLogin(userName, password)` via `AddLoginDialog` | Success: new SQL Server login+user created. Failure: duplicate username/invalid password surfaces a real SQL error | Needs a real SQL Server test instance | Not started — no test coverage found for `AddLogin` |
| File \| Backup — currently unreachable | N/A today | `IDatabase.Backup(path)`, invoked by a fully-wired `CommandBinding`/handler | — | — | Observation, not a scenario: dead/orphaned wiring — a working `CommandBinding` and handler exist, but no `MenuItem`, button, or shortcut anywhere fires it |

### Edit menu — "Cleanup" submenu (database-maintenance utilities)

None of these six items have any matching help page anywhere in `docs/`, and none have unit-test coverage. (`GC.Collect` and an env-var dump are excluded — pure dev/debug utilities.)

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Remove unused securities | Edit \| Cleanup \| Remove unused securities | `MyMoney.RemoveUnusedSecurities()` | Securities held by zero transactions are removed; a referenced security is untouched | Fixture db with one orphan security + one referenced security | Business-layer gap — no test coverage |
| Check Transfers (dangling-transfer detection) | Edit \| Cleanup \| Check Transfers | `MyMoney.CheckTransfers()` → consumed by `TransactionsView.CheckTransfers()`, prompts to fix dangling transfers | A transfer whose other side was deleted is returned in the dangling set and the fix-up prompt appears; a clean db returns empty, no prompt | Fixture db with one transfer manually broken (one side deleted, bypassing the linked-delete) | Business-layer gap — zero coverage; `docs/Basics/Transfers.md` never mentions this feature at all |
| Fix Splits | Edit \| Cleanup \| Fix Splits | **None** — logic is inline in `MainWindow.xaml.cs`'s click handler, not extracted at all | Transactions with `IsSplit=true` but a non-`Split` category get corrected; reports count fixed, or "all good" | Fixture db with a transaction manually put into that inconsistent state | Business-layer gap — logic isn't even in the business layer, can't be unit-tested without extracting it first |
| Remove Duplicate Securities | Edit \| Cleanup \| Remove Duplicate Securities | `MyMoney.RemoveDuplicateSecurities()` → `int` | Duplicate security records collapse to one; returned count matches the confirmation message | Fixture db with two `Security` rows for the same symbol | Business-layer gap — no test coverage |
| Remove Duplicate Payees | Edit \| Cleanup \| Remove Duplicate Payees | `MyMoney.RemoveDuplicatePayees()` → `int` | Same pattern, for payees | Fixture db with two `Payee` rows for the same name | Business-layer gap — no test coverage |
| Reset all category frequencies | Edit \| Cleanup \| Reset all category frequencies | `MyMoney.ResetCategoryFrequencies()` | Category "frequency" counters reset to zero; picker ordering changes on next use | Fixture db with categories that have non-zero frequency counts | Business-layer gap — no test coverage |

### Query menu — raw SQL access

(Not the query-builder panel — that's covered under Basics' Quick Search & Advanced Queries, above.)

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Adhoc SQL Query | Query \| Adhoc SQL Query... | `IDatabase.QueryDataSet(string)`, exercised through `FreeStyleQueryDialog` | Success: valid SELECT populates the results grid; Save writes to XML. Failure: malformed SQL is caught, shown in a MessageBox, no crash. Also: this menu item only does anything when the open db is `SqlServerDatabase` — for SQLite/XML it silently no-ops with **no message at all** | The existing "Redmond"/"MyMoney" SQL Server test instance | Business-layer gap for the failure path — `QueryDataSet` is only incidentally exercised (via `PRAGMA` checks in `SqliteDatabaseContractTests.cs`), never for a malformed-query error; the silent-no-op-on-SQLite behavior is a real UX gap worth confirming is intentional |
| Show Last Update | Query \| Show Last Update... | `IDatabase.GetLog()` — works against any db flavor | Dialog opens pre-populated with the accumulated operation log for the session | Any fixture db, after a couple of edits | Not started — no test coverage found |

### Business-layer capabilities with no help page

| Scenario | UI action | Business-layer API | Verify | Test data | Status |
| --- | --- | --- | --- | --- | --- |
| Command-line argument handling | Launch `MyMoney.exe` with `/n`, `/nd <name>`, `/nosettings`, `/h`/`-?`/`--help`, or a bare filename | `ParseCommandLine` (`MainWindow.xaml.cs`), called from the constructor | Each flag produces its documented effect; an unrecognized flag falls through with no `default` case, silently ignored — confirm this is intended rather than a swallowed-typo bug | None needed for `/h`/`/n`; a small fixture file for the bare-filename case | Business-layer gap — untestable without launching the whole process; the `/h` usage text is the closest thing to docs this has anywhere |

### Observation only — not a testable scenario today

`CommandFileRestore`, `CommandReportBudget`, and `CommandShowBudget` are declared in `Commands.cs` with no `CommandBinding`, `MenuItem`, or handler anywhere. The domain model carries a full `Category.Budget` column and `Transaction`/`Split.IsBudgeted`/`BudgetBalanceDate` change-tracking in `Money.cs`, but nothing in the WPF project references any of it. This reads as an incomplete/abandoned budgeting feature — real business-layer surface, currently unreachable by any end user. Flagged so it isn't silently lost; if it's ever wired up, it needs both business-layer and FlaUI coverage built from scratch.

---

## Documentation gaps found

Building this catalog directly from the help file's own structure surfaced real gaps in the documentation itself, not just in test coverage. Two kinds:

**Missing documentation** (a real, working feature with no help page at all) — every row in "Undocumented in help file — Menu & API audit" above is one of these. The most significant clusters, worth their own help pages if anyone picks up a documentation pass:

- The entire Edit \| Cleanup submenu (6 database-maintenance tools) — none mentioned anywhere in `docs/`.
- The Query \| Adhoc SQL Query and Show Last Update dialogs.
- Command-line launch arguments (`/n`, `/nd`, `/nosettings`, bare-filename opening) — the `/h` usage text is the only documentation this has.
- File \| Associate file extensions and File \| Add User (SQL Server).

**Stale documentation** (a help page describing something that no longer matches the code) — one confirmed instance found:

- `docs/Images/components.png` (referenced from the Development page's "Projects and Packages" section) predates the entire 3-tier layer-extraction work — shows a monolithic `MyMoney` project with no `MyMoney.Business`/`MyMoney.Data` split, references `MSTest`/`EntityFramework` (not used anywhere in the current codebase), and is missing `PerformanceProvider`/`UITests`/`ScenarioTest`/`UIControlsTest` entirely. Filed as [issue #41](https://github.com/markabrandjord/MyMoney.Net/issues/41).

Not filed as issues yet — the missing-documentation items above are tracked here for now; file them individually if/when a documentation pass is actually scheduled, rather than opening 4+ issues speculatively.

---

## Adding a new scenario

Add a row to the relevant table (or a new table for a new area) with the
same six columns. Keep the "Business-layer coverage" judgment explicit —
if you're not sure whether the underlying API is already unit-tested,
check before assuming a FlaUI test is the right next step. If you find a
real feature with no corresponding help page, or a help page describing
behavior that no longer matches the code, add it to "Documentation gaps
found" above too.
