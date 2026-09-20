# End-User Scenario Catalog

This is a **living, phased catalog** of what a user can accomplish with this
application, written in plain language with no implementation detail in the
scenario text itself. It exists to drive a full UI redesign from *user intent*
rather than from the shape of the current UI.

The catalog is built from the application's **source code on `master`** — not by
clicking through the live UI — so that "this phase is done" is a checkable
claim: every relevant file/class has been read, and every user-facing capability
it exposes has either been mapped to a scenario or explicitly logged as not
user-facing.

## Phases

| # | Phase | Scope | Status |
|---|-------|-------|--------|
| 1 | Core domain model | `Money.cs` (+ its `MyMoney` partial in `Money_Loans.cs`) — the persistent object graph and the operations it supports | **Done** |
| 2 | Navigation & shell | Main window, view selectors, command routing | **Done** |
| 3 | Views | `Views/` + `View Selectors/` — the grids, trees and panes the user works in | **Done** |
| 4 | Dialogs | `Dialogs/` — modal flows, wizards, editors | **Done** |
| 5 | Reports + charts | `Reports/`, `Charts/` (+ `Views/GraphGenerators.cs` and `FlowDocumentView`'s report-hosting path, deferred here by Phase 3) | **Done** |
| 6 | Import/export | `Importers/`, `Ofx/`, CSV/QIF/XML storage formats | **Done** |
| 7 | Taxes | `Taxes/` (really `MyMoney.Business/Taxes/`) — tax-line association, capital-gains treatment, bracket estimation, TXF export | **Done** |
| 8 | Cross-cutting | Attachments and statements, market data and exchange rates, settings persistence, storage, app-wide modal UI, theming, logging, updates, printing — plus the final completeness sweep for anything Phases 1–7 left unclaimed | **Done** |
| 9 | Data engine internals | `Source/WPF/MyMoney.Data/` — the five storage engines, the server bootstrapper, the database registry, the credential store and the checked-in SQL scripts; the one area Phase 8 scoped out rather than read | **Done** |

**All nine phases are complete.** See the "Catalog complete" note at the end of this
document for totals and for where the defects this audit found were filed.

### How to extend this catalog

Each phase appends a new top-level section in the same shape as Phase 1:

1. **Scenarios**, grouped by area. Each scenario gets a short title, a 1–4
   sentence implementation-neutral description of what the *user* is trying to
   accomplish, and a traceability note naming the class(es)/file(s) it came
   from. The traceability note is metadata — it must never leak into the
   scenario description itself.
2. A **coverage checklist** listing every top-level type the phase examined,
   each either mapped to the scenario(s) it supports or marked "not user-facing"
   with a one-line reason.

Scenario IDs are stable: `P<phase>-<area>-<n>`. Don't renumber existing ones;
append.

---

# Phase 1: Core Domain Model

**Source examined (in full):**
`Source/WPF/MyMoney.Business/Money.cs` (15,302 lines) and its companion partial
`Source/WPF/MyMoney.Business/Money_Loans.cs` (794 lines), which adds the loan
half of the same `MyMoney` root object.

> Note for later phases: `CLAUDE.md` still describes this file as living at
> `Source/WPF/MyMoney/Database/Money.cs`. It has since moved to the
> `MyMoney.Business` project. The path above is the live one on `master`.

**What this phase covers and deliberately does not.** This section catalogs what
the *data model itself* makes possible — the concepts a user manipulates and the
operations the model supports on them. Where the model hands something off to a
view, a report, a dialog or an importer, the capability is captured here and the
presentation of it is left to the relevant later phase.

---

## 1.1 Working with the financial picture as a whole

### P1-WHOLE-1 — Keep one household's entire financial history in one place
The user maintains a single body of financial data that holds every account,
every transaction, every payee, category, investment and property they track.
Everything else in the product is a way of looking at or changing that one body
of data.
*(`MyMoney`)*

### P1-WHOLE-2 — Have work saved, and know that it's been saved
The user makes changes freely and the product tracks which pieces of information
are new, edited or removed since the last save, so that saving writes exactly
those changes and nothing else. The user is never asked to manage this bookkeeping
themselves.
*(`PersistentObject`/`PersistentContainer` change tracking, `MyMoney.Save`)*

### P1-WHOLE-3 — Make a batch of related changes that lands as one update
When the user does something that touches many records at once — recategorizing,
applying a rename rule, importing — the product treats it as one coherent update
rather than as hundreds of separate ones, so views don't churn and totals don't
flicker part-way through.
*(`BeginUpdate`/`EndUpdate`/`CreateUpdateScope` on `MyMoney`, containers, objects)*

### P1-WHOLE-4 — Be warned when someone else changed the same record
If the same financial data is open in more than one place and two edits collide,
the user is told their record was changed underneath them rather than silently
losing one of the two edits.
*(`PersistentObject.RowVersion`, `ConcurrencyConflictException`)*

### P1-WHOLE-5 — Start over from empty
The user can clear out the current financial data entirely and begin from a blank
slate.
*(`MyMoney.Clear`, each container's `Clear`)*

### P1-WHOLE-6 — Move a whole financial history into a fresh file
The user can take the data they have and push all of it into a new, empty
destination as though every record were brand new — the basis of "save a copy
somewhere else" or "migrate to a different storage choice".
*(`MyMoney.MarkAllNew`, `PersistentContainer.MarkAllNew`)*

### P1-WHOLE-7 — See problems in the data reported rather than crashing
When something goes wrong deep in a bulk operation, the user gets told about it
in context instead of the work silently failing.
*(`MyMoney.ErrorLog`/`LogError`, `MoneyException`, `TransactionException`)*

---

## 1.2 Accounts

### P1-ACCT-1 — Set up the accounts they actually have
The user creates an account for each real-world place their money sits or is
owed, naming it and describing it however makes sense to them.
*(`Accounts.AddAccount`, `Account.Name`/`Description`)*

### P1-ACCT-2 — Say what kind of account it is
The user marks each account as the kind of thing it really is — everyday
chequing or savings, a money-market or cash pot, a credit card or line of
credit, a brokerage or retirement account, an education or health savings
account, a loan, or an asset they own such as a house or a car. The kind of
account changes what the product lets them do with it and how it counts toward
their net worth.
*(`AccountType`, `Account.Type`, `Account.IsInvestmentAccount`)*

### P1-ACCT-3 — Record the account's starting point
The user enters the balance the account had when they began tracking it, so that
every running balance afterwards is correct without having to enter years of
history.
*(`Account.OpeningBalance`)*

### P1-ACCT-4 — Always know what an account is worth right now
For every account the user sees a current balance that reflects its opening
balance plus everything recorded against it, including the market value of
anything held inside an investment account.
*(`Account.Balance`, `Transactions.Rebalance`, `Transactions.GetBalance`,
`AccountBalanceInfo`)*

### P1-ACCT-5 — Look ahead to a future balance
The user can ask what an account's balance will be at a chosen date, taking into
account everything already scheduled or entered up to that point.
*(`MyMoney.EstimatedBalance`, `Transactions.EstimatedBalance`)*

### P1-ACCT-6 — Close an account without losing its history
When the user stops using an account, they mark it closed. It disappears from
the places where they pick accounts day-to-day, but everything recorded against
it stays in their history and in past reports.
*(`Account.IsClosed`, `AccountFlags.Closed`, `Accounts.GetAccounts(filterOutClosed)`)*

### P1-ACCT-7 — Delete an account and have its loose ends tidied up
If the user deletes an account outright, money that was moving to or from it is
not left pointing at nothing — those entries are re-labelled as having come from
or gone to a deleted account, and the account's own entries are removed with it.
*(`Accounts.RemoveAccount`, `MyMoney.ClearTransferToAccount`,
`Categories.TransferToDeletedAccount`/`TransferFromDeletedAccount`)*

### P1-ACCT-8 — Flag an account's tax treatment
The user marks an account as ordinary taxable, tax-deferred, or tax-free, so
that gains and income from it are treated correctly wherever tax matters.
*(`Account.TaxStatus`, `AccountFlags.TaxDeferred`/`TaxFree`, `TaxStatus`)*

### P1-ACCT-9 — Keep the institution's own details with the account
The user stores the identifiers and links that belong with an account — the
number the bank uses for it and the website they go to for it — so they don't
have to look them up elsewhere.
*(`Account.AccountId`, `Account.OfxAccountId`, `Account.WebSite`)*

### P1-ACCT-10 — Be reminded when an account hasn't been reconciled lately
The user sets how long they're willing to let an account go without balancing
it, and the product remembers when they last balanced it and when it last synced.
*(`Account.ReconcileWarning`, `Account.LastBalance`, `Account.LastSync`)*

### P1-ACCT-11 — See which accounts have entries still awaiting their approval
The user can tell at a glance which accounts contain downloaded entries they
haven't yet looked at and accepted.
*(`Account.Unaccepted`, `TransactionFlags.Unaccepted`)*

### P1-ACCT-12 — Include or exclude an account from budgeting
The user marks whether an account participates in their budget at all.
*(`AccountFlags.Budgeted`)*

### P1-ACCT-13 — Hold an account in a foreign currency
The user can keep an account denominated in a currency other than their home
currency and still see it correctly totalled alongside everything else.
*(`Account.Currency`, `Account.GetNormalizedAmount`, `Account.BalanceNormalized`)*

### P1-ACCT-14 — Get cash totals across a chosen set of accounts
The user can ask what their combined cash position was on a date, or across a
series of dates, over whatever subset of accounts they care about.
*(`MyMoney.GetCashBalanceNormalized`, `MyMoney.GetCashBalanceNormalizedBatch`)*

---

## 1.3 Connecting accounts to a financial institution

### P1-ONLINE-1 — Attach an account to the institution it downloads from
The user links an account to the online service that supplies its statements, so
downloads know where to go and which account they belong to.
*(`OnlineAccount`, `Account.OnlineAccount`, `OnlineAccounts`)*

### P1-ONLINE-2 — Store the sign-in details for an institution once
The user records the credentials and connection settings an institution needs
once, and reuses them for every account at that institution rather than
re-entering them.
*(`OnlineAccount.UserId`/`Password`/`UserCred1`/`UserCred2`/`AuthToken`/
`AccessKey`/`UserKey`, `OnlineAccount.Institution`/`FID`/`BankId`/`BrokerId`/
`BranchId`)*

### P1-ONLINE-3 — Answer an institution's extra security questions
When an institution asks additional identity questions during a download, the
user can supply the answers, and those answers are treated as a one-off rather
than being kept on file.
*(`MfaChallengeAnswer`, `OnlineAccount.MfaChallengeAnswers`)*

### P1-ONLINE-4 — Have expired access noticed
The user's access to an institution can carry an expiry, so the product knows
when a stored authorization has gone stale rather than failing mysteriously.
*(`OnlineAccount.UserKeyExpireDate`)*

### P1-ONLINE-5 — Not accumulate connection entries they no longer use
When no account is attached to an institution's connection any more, that
connection stops cluttering the user's list.
*(`MyMoney.RemoveUnusedOnlineAccounts`)*

### P1-ONLINE-6 — Recognise an institution visually
The user sees each institution's own branding where it's available rather than a
wall of identical rows.
*(`OnlineAccount.LogoUrl`)*

---

## 1.4 Recording and editing transactions

### P1-TXN-1 — Record money leaving or arriving
The user enters what happened: the date, who it was with, how much, and which
account it affected. That single act is the backbone of everything else in the
product.
*(`Transaction`, `Transactions.NewTransaction`, `Transactions.AddTransaction`)*

### P1-TXN-2 — Enter amounts as payments or deposits rather than signed numbers
The user thinks in terms of "money out" and "money in" and enters them in
separate places rather than remembering to type a minus sign.
*(`Transaction.Credit`/`Debit`, `Transaction.NegativeAmount`)*

### P1-TXN-3 — Note what a transaction was for
The user attaches a free-text note and, where relevant, a cheque or reference
number to a transaction.
*(`Transaction.Memo`, `Transaction.Number`)*

### P1-TXN-4 — Say where a transaction stands
The user marks a transaction as nothing-special, electronic, cleared, reconciled
or void, and that status governs how much it can still be changed.
*(`TransactionStatus`, `Transaction.Status`/`StatusString`)*

### P1-TXN-5 — Be stopped from quietly corrupting a balanced statement
Once a transaction has been reconciled against a statement, the user can't change
its amount by accident — only deliberately, while balancing that account.
*(`Transaction.Amount` setter, `MoneyException` on reconciled change,
`Transaction.IsReconciling`)*

### P1-TXN-6 — Void a transaction instead of deleting it
The user can neutralise a transaction so it no longer affects any balance while
still keeping the record that it existed.
*(`TransactionStatus.Void`, balance calculations skipping voided entries)*

### P1-TXN-7 — Delete a transaction and have everything that depended on it cleaned up
When the user removes a transaction, the other side of any transfer, its split
detail, and the running counts it contributed to are all brought back into line.
*(`MyMoney.RemoveTransaction`, `Transactions.RemoveTransaction`)*

### P1-TXN-8 — Record sales tax separately from the purchase
The user can record how much of a purchase was tax, so that reports can show
spending with and without it.
*(`Transaction.SalesTax`, `NetSalesTax`, `AmountMinusTax`)*

### P1-TXN-9 — See a running balance as they scroll their history
For each transaction in an account, the user sees what the account's balance was
right after it, in date order.
*(`Transaction.Balance`, `Transactions.GetBalance(updateTransactionBalance)`)*

### P1-TXN-10 — Review and accept what was downloaded
Entries that arrived from an institution are marked as needing the user's
attention until they've looked at them, and the product keeps count of how many
are still waiting.
*(`Transaction.Unaccepted`, `Payee.UnacceptedTransactions`, `Account.Unaccepted`)*

### P1-TXN-11 — See at a glance which entries just arrived
Immediately after a download, the user can pick out the newly arrived entries
from the ones that were already there, and that highlight goes away once the work
is saved.
*(`Transaction.IsDownloaded`, `MyMoney.RecordDownloaded`/`ClearDownloadedState`)*

### P1-TXN-12 — Know which transactions have paperwork attached
The user can see which transactions have a receipt or document filed against
them, and which have a statement associated with them.
*(`TransactionFlags.HasAttachment`/`HasStatement`, `Transaction.HasAttachment`/
`HasStatement`)*

### P1-TXN-13 — Pull up an account's history in a sensible order
The user views everything recorded against one account, in date order, with
same-day entries ordered predictably rather than shuffling around.
*(`Transactions.GetTransactionsFrom`, `TransactionComparerByDate`,
`Transaction.CompareByDate`)*

### P1-TXN-14 — Find the most recent activity on an account
The user can jump to the latest thing that happened on an account.
*(`Transactions.GetLatestTransactionFrom`)*

### P1-TXN-15 — Give a transaction a different date for tax purposes
When a transaction belongs to a different tax period than its calendar date
suggests, the user can record that separately without falsifying the real date.
*(`TransactionExtra.TaxDate`/`TaxYear`, `Transaction.TaxDate`,
`TransactionComparerByTaxDate`)*

---

## 1.5 Moving money between the user's own accounts

### P1-XFER-1 — Record one movement of money, not two
When the user moves money from one of their accounts to another, they record it
once and both accounts reflect it — the money leaves one side and arrives at the
other automatically.
*(`MyMoney.Transfer(Transaction, Account)`, `Transfer`, `Transaction.Transfer`)*

### P1-XFER-2 — See the other account named instead of a payee
On a transfer, the user sees which of their own accounts the money went to or
came from, phrased as a transfer rather than as a payment to a merchant.
*(`Transaction.PayeeOrTransferCaption`, `GetTransferCaption`,
`ExtractTransferAccountName`, `Payees.Transfer`)*

### P1-XFER-3 — Turn an existing entry into a transfer by naming the account
The user can convert an ordinary entry into a transfer simply by saying which of
their accounts it went to, rather than deleting and re-entering it.
*(`Transaction.PayeeOrTransferCaption` setter, `Split.PayeeOrTransferCaption` setter)*

### P1-XFER-4 — Have both sides of a transfer stay in step
Changing the date, memo or amount on one side of a transfer updates the other
side too, so the two halves never drift apart.
*(`Transaction.Date`/`Memo`/`Amount` setters, `InternalSetAmount`)*

### P1-XFER-5 — Transfer between accounts held in different currencies
When the two accounts aren't in the same currency, the amount that arrives is
converted rather than blindly copied.
*(`Currencies.GetTransferAmount`, `MyMoney.Transfer`)*

### P1-XFER-6 — Be protected from breaking a reconciled transfer
If the other side of a transfer has already been reconciled, the user is stopped
from changing or removing it outside of balancing that account.
*(`MyMoney.RemoveTransfer(Transfer)`, `Transaction.Amount`/`Split.Transfer`
setters raising `MoneyException`)*

### P1-XFER-7 — Be told when two transfers genuinely conflict
When the user tries to combine two records that each already transfer somewhere
different, the product says so rather than silently picking one.
*(`Transaction.Merge` throwing on conflicting transfer targets)*

### P1-XFER-8 — Have broken transfers found and reported
The user can have the product check their data for transfers whose other half has
gone missing, so they can be repaired rather than quietly skewing balances.
*(`MyMoney.CheckTransfers`, `Transaction.CheckTransfers`, `Splits.CheckTransfers`,
`AutoFixDandlingTransfer`)*

### P1-XFER-9 — Transfer only part of a transaction
The user can have one line of an itemised transaction be a transfer to another
account while the rest stays ordinary spending.
*(`MyMoney.Transfer(Split, Account)`, `Split.Transfer`)*

### P1-XFER-10 — Be told when a split-to-split transfer isn't supported
If the user tries to build a transfer where both sides are itemised, they get a
clear explanation of why that isn't allowed and what to do instead.
*(`MyMoney.Transfer(Split, Account)` guard message)*

---

## 1.6 Breaking a transaction into parts

### P1-SPLIT-1 — Itemise a single transaction into several lines
When one payment covers several different things, the user can break it into
lines, each with its own amount, category, note and even its own payee.
*(`Splits`, `Split`, `Transaction.IsSplit`/`NonNullSplits`)*

### P1-SPLIT-2 — See how much of a transaction is still unassigned
While itemising, the user sees how much of the total hasn't been allocated to a
line yet, and a plain-language message telling them so.
*(`Splits.Unassigned`, `HasUnassigned`, `SplitsBalanceMessage`, `Splits.Rebalance`)*

### P1-SPLIT-3 — Collapse an itemised transaction back to a simple one
If the user chooses a single category for a transaction that was itemised, the
itemisation is discarded and it becomes a simple transaction again.
*(`MyMoney.Categorize`, `Transaction.Category` setter clearing splits,
`Splits.RemoveAll`)*

### P1-SPLIT-4 — Not be left with blank itemised lines
Lines that were started and left completely empty are cleaned away rather than
lingering as noise.
*(`Splits.RemoveEmptySplits`)*

### P1-SPLIT-5 — Copy an itemisation from one transaction to another
The user can take the breakdown they built on one transaction and apply the same
breakdown to another.
*(`MyMoney.CopyCategory`, `Splits.Serialize`/`DeserializeInto`)*

### P1-SPLIT-6 — Have itemised lines surface in their own right when browsing
When the user browses by category or by payee, a line inside an itemised
transaction shows up as its own row where that's the only part that matches,
rather than being hidden inside the parent.
*(`Transaction(Transaction, Split)` proxy, `Transactions.GetTransactionsByCategory`,
`GetTransactionsByPayee`, `Transactions.ExecuteQuery`)*

---

## 1.7 Payees

### P1-PAYEE-1 — Build up a list of who they deal with
The user records who each transaction was with, and the product accumulates that
into a reusable list so the same name doesn't have to be typed twice.
*(`Payees`, `Payee`, `Payees.FindPayee(name, add)`)*

### P1-PAYEE-2 — Not be tripped up by inconsistent spacing in names
Names that differ only by extra spaces are treated as the same party rather than
splitting the user's history in two.
*(`Payees.FindPayee` whitespace normalization)*

### P1-PAYEE-3 — Rename a party everywhere at once
Renaming a party updates it across every transaction that referred to it, rather
than leaving the old name behind in history.
*(`Payee.Name` setter, `Payees.OnNameChanged`)*

### P1-PAYEE-4 — See which parties still need attention
For each party, the user can see how many of their transactions are still
unaccepted and how many still lack a category.
*(`Payee.UnacceptedTransactions`, `UncategorizedTransactions`, `Payee.Flags`,
`StatusFlags`)*

### P1-PAYEE-5 — Merge duplicate parties into one
When the same real-world party has ended up recorded twice, the user can fold
them together so all the history lands on one name and the counts stay right.
*(`MyMoney.RemoveDuplicatePayees`, `Payee.Merge`)*

### P1-PAYEE-6 — Not accumulate parties they no longer use
Parties that no longer appear on any transaction or rename rule stop cluttering
the user's list.
*(`MyMoney.RemoveUnusedPayees`, `MyMoney.GetUsedPayees`)*

### P1-PAYEE-7 — Find everything involving one party
The user can pull up every transaction with a given party, including itemised
lines that name that party even when the parent transaction doesn't.
*(`Transactions.GetTransactionsByPayee`)*

### P1-PAYEE-8 — Search for a party by typing part of their name
The user narrows a long list of parties by typing a fragment rather than
scrolling.
*(`Payees.GetPayeesAsList(filter)`)*

---

## 1.8 Cleaning up messy downloaded names (rename rules)

### P1-ALIAS-1 — Turn cryptic bank descriptions into readable names
The user sets up a rule that says "whenever something arrives looking like this,
call it this party instead", so downloaded entries read like the real world
rather than like a payment-terminal log.
*(`Alias`, `Aliases`, `Alias.Matches`, `Transactions.Merge` applying aliases)*

### P1-ALIAS-2 — Match either an exact name or a pattern
The user chooses whether a rename rule matches a name exactly or matches a
family of similar names through a pattern.
*(`AliasType`, `Alias.AliasType`, `Alias.Matches`)*

### P1-ALIAS-3 — Preview what a rename rule would affect before committing
Before applying a rule, the user can see every existing transaction and itemised
line it would change.
*(`MyMoney.FindAliasMatches`)*

### P1-ALIAS-4 — Apply a rename rule to history in one go
The user applies a rule across a set of existing transactions and has them all
renamed as one update.
*(`MyMoney.ApplyAlias`)*

### P1-ALIAS-5 — Be warned when a new rule swallows existing ones
When a new, broader rule would make existing narrower rules redundant, the user
is shown which ones so they can tidy up.
*(`MyMoney.FindSubsumedAliases`)*

### P1-ALIAS-6 — Keep the original wording for later matching
When a name is rewritten by a rule, what the institution actually sent is kept,
so future downloads of the same thing still line up with what's already recorded.
*(`Transaction.OriginalPayee`, `Transactions.PayeesMatch`)*

### P1-ALIAS-7 — Have rename rules disappear with the party they point at
Deleting a party also removes the rules that were renaming things into it, so no
rule is left pointing at nothing.
*(`Payees.RemovePayee` → `Aliases.RemoveAliasesOf`)*

### P1-ALIAS-8 — Route a downloaded account identifier to the right account
The user can teach the product that a particular account identifier coming from
an institution belongs to a particular account of theirs, and can move that
mapping to a different account later.
*(`AccountAlias`, `AccountAliases`, `AccountAliases.AddAlias` re-pointing duplicates)*

---

## 1.9 Categories

### P1-CAT-1 — Classify spending and income
The user assigns each transaction to a category so their money can be summarised
by what it was for rather than only by who it was with.
*(`Category`, `Categories`, `Transaction.Category`, `Split.Category`)*

### P1-CAT-2 — Organise categories into a hierarchy
The user groups categories under parent categories to whatever depth suits them,
so "car insurance" can sit inside "car" which sits inside "household", and
totals roll up accordingly.
*(`Category.ParentCategory`/`Root`/`Subcategories`, `Categories.AddParents`,
`Category.GetFullName`, `Category.Label`)*

### P1-CAT-3 — Have intermediate levels created for them
When the user names a category deep in a hierarchy that doesn't exist yet, the
levels above it are created automatically instead of failing.
*(`Categories.GetOrCreateCategory` → `AddParents`)*

### P1-CAT-4 — Rename a category and have its children follow
Renaming a category renames everything beneath it, so the hierarchy stays
coherent and no child is orphaned under the old name.
*(`Category.Name` setter → `RenameSubcategories`, `Category.Label` setter)*

### P1-CAT-5 — Move a category to a different parent
The user can re-home a branch of their category tree, and everything under it
moves with it, keeping its colour, budget, frequency and tax association.
*(`Categories.ReParent`, `Category.AddSubcategory`/`RemoveSubcategory`)*

### P1-CAT-6 — Recategorize transactions when a category moves
When a category is restructured, the transactions and itemised lines that used it
are moved to the equivalent place in the new structure.
*(`Transaction.ReCategorize`, `Transactions.GetTransactionsByCategory`)*

### P1-CAT-7 — Mark a category as income, expense, savings or investment
The user says what kind of flow a category represents, so reports can separate
what came in from what went out and treat investment activity distinctly.
*(`CategoryType`, `Category.Type`)*

### P1-CAT-8 — Colour-code categories, with children inheriting
The user gives a category a colour for use in charts, and categories beneath it
pick up that colour unless they're given one of their own.
*(`Category.Color`, `Category.InheritedColor`)*

### P1-CAT-9 — Describe what a category is for
The user can attach a longer description to a category so its intent is clear
later.
*(`Category.Description`)*

### P1-CAT-10 — Delete a category
The user removes a category they no longer want; it disappears from the places
where categories are chosen, and any categories beneath it go with it.
*(`Categories.RemoveCategory`, `Category.OnDelete` cascading to subcategories,
`Categories.GetCategories` filtering deleted)*

### P1-CAT-11 — Browse everything filed under a category, including its children
Looking at a category shows the user everything in it and everything in the
categories beneath it, including individual lines from itemised transactions.
*(`Category.Contains`, `Transactions.GetTransactionsByCategory`,
`Transaction.CategoryMatches`/`SplitMatchesCategory`)*

### P1-CAT-12 — Find things that were never categorized
The user can pull up everything that has no category and isn't a transfer, so
nothing slips through unclassified.
*(`Categories.Unknown` handling in `GetTransactionsByCategory`)*

### P1-CAT-13 — Have the product's own housekeeping categories exist when needed
Concepts the product needs in order to describe itself — an itemised
transaction, a transfer, a transfer to an account that no longer exists,
sales tax, the various kinds of investment income and expense — are available
without the user having to invent them.
*(`Categories.Split`/`Transfer`/`TransferToDeletedAccount`/
`TransferFromDeletedAccount`/`SalesTax`/`Unknown`/`UnassignedSplit`/
`Investment*`/`InterestEarned`/`Savings`)*

---

## 1.10 Budgeting

### P1-BUDGET-1 — Set a spending target for a category
The user decides how much they intend to spend or receive in a category.
*(`Category.Budget`)*

### P1-BUDGET-2 — Say how often that target applies
The user expresses a target in whatever rhythm matches the real world — daily,
weekly, fortnightly, monthly, quarterly, half-yearly, yearly and several in
between — and the product can convert between those rhythms so targets set in
different terms can still be compared.
*(`CalendarRange`, `Category.Frequency`, `Category.RangeToDaily`/`DailyToRange`)*

### P1-BUDGET-3 — Track actual spending against the target
The user sees how much has actually landed in a category, with amounts rolling
up into parent categories so a group target can be judged as a whole.
*(`Category.Balance` propagating to parent, `Category.ClearBalance`)*

### P1-BUDGET-4 — Mark individual amounts as counted toward the budget
The user decides which itemised lines have been taken into the budget and which
haven't, and the category's running total reflects that.
*(`Split.IsBudgeted`/`SetBudgeted`, `SplitFlags.Budgeted`, `Split.UpdateBudget`)*

### P1-BUDGET-5 — Be told when something can't be budgeted
If the user tries to count an amount toward the budget without saying what
category it belongs to, they're told why it can't be done.
*(`Split.UpdateBudget` raising `TransactionException`)*

### P1-BUDGET-6 — Know what period the budget has been balanced up to
The user can see when budgeting was last brought up to date, and how far back it
goes.
*(`Transaction.BudgetBalanceDate`, `Split.BudgetBalanceDate`,
`Transactions.GetMostRecentBudgetDate`/`GetFirstBudgetDate`)*

### P1-BUDGET-7 — Reset how often categories are expected to recur
The user can clear the recurrence they've assigned across their categories and
start that classification again.
*(`MyMoney.ResetCategoryFrequencies`)*

---

## 1.11 Investments

### P1-INV-1 — Keep a list of the things they invest in
The user maintains the securities they hold or have held, each with a name, a
ticker symbol and an identifier, so the same holding is recognised consistently.
*(`Security`, `Securities`, `Security.Name`/`Symbol`/`CuspId`)*

### P1-INV-2 — Say what kind of investment each one is
The user classifies each holding as a stock, bond, mutual fund, exchange-traded
fund, money-market holding, property trust, futures contract or a private
holding, so like can be grouped with like.
*(`SecurityType`, `Security.SecurityType`, `Security.GetSecurityTypeCaption`)*

### P1-INV-3 — Mark a holding as taxable or not
The user records whether income from a holding is taxable.
*(`YesNo`, `Security.Taxable`, `Investment.TaxExempt`)*

### P1-INV-4 — Record buying, selling, adding and removing holdings
The user records the investment activity that happened — purchases, sales, and
shares moving in or out without a purchase — with the number of units and the
price per unit.
*(`Investment`, `InvestmentType`, `Investment.Units`/`UnitPrice`,
`Transaction.InvestmentType`/`InvestmentUnits`/`InvestmentUnitPrice`)*

### P1-INV-5 — Record the detail of how a trade was placed
Where it matters, the user captures the particular kind of trade — a plain buy or
sell, a short sale, a cover, an opening or closing position.
*(`InvestmentTradeType`, `Investment.TradeType`)*

### P1-INV-6 — Record all the costs attached to a trade
The user records commission, fees, loads, mark-ups or mark-downs, tax withheld
and taxes paid, so the true cost or proceeds of a trade is captured rather than
just the headline price.
*(`Investment.Commission`/`Fees`/`Load`/`MarkUpDown`/`Taxes`/`Withholding`)*

### P1-INV-7 — Keep current and previous prices for a holding
The user sees the latest price for each holding alongside the one before it, so
they can see whether it's up or down and by how much.
*(`Security.Price`/`LastPrice`/`PriceDate`/`PercentChange`/`IsDown`)*

### P1-INV-8 — See what a holding is worth now versus what it cost
The user sees the market value of a position, what it cost, and the resulting
gain or loss in both money and percentage terms.
*(`Investment.MarketValue`/`CostBasis`/`OriginalCostBasis`/`GainLoss`/
`PercentGainLoss`)*

### P1-INV-9 — Have futures contracts sized correctly
Contracts that represent a multiple of the quoted price are valued on that basis
rather than as a single unit, so their value isn't understated.
*(`SecurityType.Futures` factor in `Investment.MarketValue`,
`Transactions.UpdateStockUnitsAndRoutingPath`, `Transactions.GetBalance`)*

### P1-INV-10 — Record stock splits and have history adjust
The user records that a holding split (or reverse-split), and their historical
unit counts and per-unit prices are restated accordingly so cost basis and
performance stay meaningful.
*(`StockSplit`, `StockSplits`, `Investment.ApplySplit`/`ResetCostBasis`,
`MyMoney.ApplyStockSplits`, `Security.StockSplits`)*

### P1-INV-11 — Be prevented from entering a nonsensical split ratio
The user can't record a split with a zero on the wrong side of the ratio.
*(`StockSplit.Denominator` setter raising on zero)*

### P1-INV-12 — See a holding's full trading history
The user pulls up every trade in a given holding, in order, with a running unit
count and value and an indication of whether each trade opened, added to, sold
from or closed out a position.
*(`Transactions.GetTransactionsBySecurity`,
`Transactions.UpdateStockUnitsAndRoutingPath`, `Transaction.RunningUnits`/
`RunningBalance`/`RoutingPath`)*

### P1-INV-13 — See only what they still own
The user can narrow to the holdings they currently still have a position in
rather than everything they've ever traded.
*(`MyMoney.GetOwnedSecurities`, `MyMoney.GetUsedSecurities`)*

### P1-INV-14 — Merge duplicate holdings
When the same holding has been recorded twice, the user can fold them together,
and the product refuses to merge two that are genuinely different instruments.
*(`MyMoney.RemoveDuplicateSecurities`, `Security.Merge`, `MyMoney.CheckSecurities`)*

### P1-INV-15 — Move history from one holding to another
The user can reassign every trade from one holding to another, for example after
a ticker change or a discovered mistake.
*(`MyMoney.SwitchSecurities`)*

### P1-INV-16 — Not accumulate holdings they never traded
Holdings that no trade refers to stop cluttering the user's list.
*(`MyMoney.GetUnusedSecurities`, `MyMoney.RemoveUnusedSecurities`)*

### P1-INV-17 — Move a holding between their own accounts
The user can move shares from one of their investment accounts to another and
have it recorded as a matched removal and addition rather than as a sale and
repurchase.
*(`MyMoney.Transfer` investment handling, `InvestmentType.Add`/`Remove`,
guard rejecting Buy/Sell as a transfer)*

### P1-INV-18 — Search holdings by name or symbol
The user narrows a long list of holdings by typing part of either the name or
the ticker.
*(`Securities.GetSecuritiesAsList(filter)`, `Securities.FindSymbol`,
`Securities.FindSecurityById`, `Securities.AllSymbols`)*

### P1-INV-19 — Have investment accounts valued at market, not just cash
An investment account's balance reflects the current market value of what's held
in it, valued as at the date being looked at rather than always at today.
*(`Transactions.GetBalance` investment branch, `StockQuoteCache` usage,
`MyMoney.SetStockQuoteCache`)*

### P1-INV-20 — Be stopped from filing investment activity in a non-investment account
If investment detail arrives for an account that isn't an investment account, the
user is told which account and which line is the problem.
*(`MyMoney.EnsureInvestmentAccount`)*

---

## 1.12 Multiple currencies

### P1-CUR-1 — Keep a list of the currencies they deal in
The user records the currencies they hold money in, each with its symbol, name
and regional formatting.
*(`Currency`, `Currencies`, `Currency.Symbol`/`Name`/`CultureCode`,
`Currency.GetCultureForCurrency`)*

### P1-CUR-2 — Record exchange rates and see them change
The user keeps the current rate for each currency alongside the previous one, so
movement is visible.
*(`Currency.Ratio`, `Currency.LastRatio`)*

### P1-CUR-3 — Choose which currency everything is totalled in
The user picks a home currency, and cross-account totals and net worth are
expressed in it regardless of what each account is actually held in.
*(`Currencies.DefaultCurrency`, `Account.NormalizedCurrency`/
`NormalizedCurrencyObject`/`GetNormalizedAmount`,
`Accounts.OnDefaultCurrencyChanged`)*

### P1-CUR-4 — Have amounts formatted the way that currency is written
Amounts are shown with the conventions of the currency they're in rather than a
single hard-coded format.
*(`Currency.CultureCode`, `Account.NormalizedCultureInfo`)*

### P1-CUR-5 — Work sensibly before rates are known
If a rate hasn't been supplied yet, amounts are left as-is rather than being
zeroed or mangled.
*(`Currencies.GetTransferAmount` zero-ratio guards,
`Currencies.FindCurrencyOrDefault`, `Currencies.GetDefaultCurrency`)*

---

## 1.13 Balancing an account against a statement

### P1-RECON-1 — Balance an account against a paper or online statement
The user works through an account's entries against a statement, marking off
what appears on it, and sees the reconciled total for that statement date.
*(`MyMoney.ReconciledBalance`, `Transactions.ReconciledBalance`,
`TransactionStatus.Reconciled`)*

### P1-RECON-2 — Record which statement an entry was reconciled on
Each reconciled entry remembers which statement period it was cleared in, so a
later statement doesn't double-count it.
*(`Transaction.ReconciledDate`, month-boundary handling in `ReconciledBalance`)*

### P1-RECON-3 — Keep reconciled entries safe outside of reconciliation
Outside of an active balancing session, the user can't change or remove a
reconciled entry by accident.
*(`Transaction.IsReconciling`, `MyMoney.RemoveTransaction` guard,
`Transaction.Amount` setter guard)*

### P1-RECON-4 — Remember when each account was last balanced
The user can see how long it's been since they balanced each account.
*(`Account.LastBalance`, `Account.ReconcileWarning`)*

---

## 1.14 Bringing in data from elsewhere, and avoiding duplicates

### P1-IMPORT-1 — Have newly downloaded entries matched to ones already recorded
When a download contains something the user already entered by hand, the product
recognises it as the same event and fills in the details the user didn't have —
the institution's reference, a cheque number, a missing note or party — rather
than creating a second copy.
*(`Transactions.Merge`, `Transactions.FindMatching`, `Transactions.DatesMatch`,
`Transactions.PayeesMatch`, `Transaction.FITID`)*

### P1-IMPORT-2 — Have a previously matched entry keep matching next time
Once the user has accepted that a downloaded entry corresponds to one of theirs,
that correspondence is remembered so the same thing doesn't get flagged again on
the next download.
*(`Transaction.MergeDate`, `Transaction.OriginalPayee`)*

### P1-IMPORT-3 — Be shown likely duplicates near each other in time
The user can have nearby entries with the same party and amount flagged as
possible duplicates, searching outward from the entry in question so the closest
candidate comes first.
*(`Transactions.FindPotentialDuplicate`, `Transactions.IsPotentialDuplicate`)*

### P1-IMPORT-4 — Not be nagged about genuinely recurring payments
Regular repeating payments of a similar amount at a similar interval are
recognised as a pattern rather than being reported as duplicates.
*(`Transactions.IsRecurring`)*

### P1-IMPORT-5 — Mark a pair as definitely not duplicates
The user can tell the product that two similar entries really are separate
events, and stop being asked about them.
*(`TransactionFlags.NotDuplicate`, `Transaction.NotDuplicate`)*

### P1-IMPORT-6 — Combine two records into one, keeping the better information
When the user merges two records, each field is filled from whichever side
actually has it, including the investment detail, rather than one side simply
overwriting the other.
*(`Transaction.Merge`, `Investment.Merge`, `Splits.Merge`)*

### P1-IMPORT-7 — Pull in categories, parties and holdings from another file
When data arrives from another financial file, its categories, parties and
holdings are matched to the user's existing ones where they already exist and
created where they don't.
*(`Categories.ImportCategory`, `Payees.ImportPayee`, `Securities.ImportSecurity`)*

### P1-IMPORT-8 — Be told when imported data references something that isn't there
If incoming data transfers to an account that doesn't exist in the destination,
the user is told rather than getting a silently broken transfer.
*(`Transaction.Merge` and `Splits.Merge` account-not-found errors)*

### P1-IMPORT-9 — Have relationships rebuilt after loading a file
After loading, everything that pointed at something else — accounts, categories,
parties, transfers, itemised lines, holdings, rental units — is wired back up and
balances recalculated, so the user opens a working file rather than a set of
disconnected records.
*(`MyMoney.OnLoaded`, `MyMoney.PostDeserializeFixup`, each type's
`PostDeserializeFixup`, `Categories.FixParents`, `Transactions.JoinExtras`)*

---

## 1.15 Finding things

### P1-FIND-1 — Search across everything with a set of conditions
The user builds up a set of conditions — on account, party, category, note,
reference number, date, payment amount, deposit amount, sales tax, status or
whether it's been accepted — combined with "and"/"or", and gets back everything
that matches, across all accounts.
*(`Transactions.ExecuteQuery`, `Transaction.Matches(QueryRow)`, `QueryRow`)*

### P1-FIND-2 — Have matching itemised lines come back as results in their own right
When only one line inside an itemised transaction matches the search, that line
comes back as its own result rather than the whole transaction or nothing at all.
*(`Transactions.ExecuteQuery` split branch, `Split.Matches(QueryRow)`)*

### P1-FIND-3 — Type one thing and match it anywhere
The user types a single piece of text or a number and gets back anything where
it appears — the party, the category, the note, the date, the reference or the
amount — including inside itemised lines.
*(`Transactions.IsAnyFieldsMatching`, `Transactions.IsSplitsMatching`,
`FilterLiteral`)*

### P1-FIND-4 — Narrow by party, category, holding, account or period
The user can pull up everything for one party, one category, one holding, one
account, or one year without constructing a query.
*(`Transactions.GetTransactionsByPayee`/`GetTransactionsByCategory`/
`GetTransactionsBySecurity`/`GetTransactionsFrom`,
`GetTransactionsByCategory(c, filterYear, ignoreAmountZero)`)*

### P1-FIND-5 — Get suggestions drawn from the right accounts
When the product suggests how to classify something based on past behaviour, it
looks at the accounts where that party has actually appeared rather than the
whole history, so suggestions are relevant and arrive quickly.
*(`PayeeIndex`)*

---

## 1.16 Rental property

### P1-RENT-1 — Track a property they rent out
The user records a rental property with its name, address, purchase date and
price, land value, current estimated value and free-text notes.
*(`RentBuilding`, `RentBuildings`)*

### P1-RENT-2 — Record shared ownership of a property
The user records up to two owners of a property and what share each holds.
*(`RentBuilding.OwnershipName1`/`OwnershipName2`/`OwnershipPercentage1`/
`OwnershipPercentage2`)*

### P1-RENT-3 — Tell the product which categories make up a property's economics
The user nominates which of their categories represent that property's rental
income and its taxes, interest, repairs, maintenance and management costs.
*(`RentBuilding.CategoryForIncome`/`CategoryForTaxes`/`CategoryForInterest`/
`CategoryForRepairs`/`CategoryForMaintenance`/`CategoryForManagement`)*

### P1-RENT-4 — See a property's profitability year by year
Without re-entering anything, the user sees each property's income, each class
of expense, and the resulting profit for every year it had activity, plus totals
across all years and the span of years covered.
*(`RentBuildings.AggregateBuildingInformation`, `RentalBuildingSingleYear`,
`RentalBuildingSingleYearSingleDepartment`, `RentExpenseTotal`,
`RentBuilding.TotalIncome`/`TotalExpense`/`TotalProfit`/`Period`)*

### P1-RENT-5 — Have itemised transactions counted correctly against a property
Where a transaction is itemised, only the lines belonging to the property's
categories count toward it, not the whole transaction.
*(`RentBuildings.GetTotalAmountMatchingThisCategoryId`,
`RentBuildings.MatchAnyCategories`)*

### P1-RENT-6 — Break a property into individual rentable units
The user records the separate units within a property, who is renting each, and
notes about each.
*(`RentUnit`, `RentUnits`, `RentUnit.Name`/`Renter`/`Note`/`Building`)*

### P1-RENT-7 — Remove a property or a unit
The user deletes a property or one of its units when it no longer applies.
*(`RentBuildings.RemoveBuilding`, `RentUnits.RemoveRentUnit`)*

---

## 1.17 Loans

### P1-LOAN-1 — Track a loan as an account with a shrinking balance
The user tracks a loan or mortgage as an account whose balance reflects how much
is still owed, reducing as principal is repaid, and never showing a credit.
*(`AccountType.Loan`, `Loan`, `Loan.Rebalance`, `Loan.ComputeLoanAccountBalance`)*

### P1-LOAN-2 — Say which categories represent principal and interest
The user nominates which of their categories represent the principal and the
interest portions of a payment, and the product uses those to recognise loan
payments automatically wherever they appear.
*(`Account.CategoryForPrincipal`/`CategoryForInterest`,
`Loan.GetLoanPaymentsAggregation`)*

### P1-LOAN-3 — Have ordinary payments counted as loan payments automatically
Payments already recorded in the user's everyday accounts — whether whole
transactions or lines inside itemised ones — are picked up as payments against
the loan without being entered twice.
*(`Loan.AddPaymentIfMatchingCategoriesForPrincipalOrInterest`,
`Loan.TransactionAmountMinusAllOtherUnrelatedSplits`)*

### P1-LOAN-4 — Add loan payments that aren't in any account
The user can record a payment against a loan by hand, for periods or payments
that don't appear in the accounts they track.
*(`LoanPayment`, `LoanPayments`, `LoanPaymentAggregation.LoanPayementManualEntry`)*

### P1-LOAN-5 — See the whole payment schedule with a running balance owed
The user sees every payment in date order with its principal, interest, total
payment and the balance still owed after it.
*(`LoanPaymentAggregation`, `Loan.ComputeLoanAccountBalance`)*

### P1-LOAN-6 — Work from an interest rate when the split isn't known
Where the user knows the rate but not the principal/interest breakdown of a
payment, the product works the breakdown out for them; where they know the
breakdown, it works out the implied rate.
*(`Loan.ComputeLoanAccountBalance` percentage branch,
`Loan.CalculatePercentageOfInterest`, `LoanPaymentAggregation.Percentage`)*

### P1-LOAN-7 — Tell a loan they owe from a loan owed to them
The user tracks both money they're paying off and money being paid to them, and
the product distinguishes the two.
*(`Loan.IsLiability`)*

### P1-LOAN-8 — Edit the breakdown of a payment in place
Adjusting a payment's principal or interest updates the underlying record it came
from, whether that was a hand-entered payment or an itemised line in an account.
*(`LoanPaymentAggregation.Principal`/`Interest` setters writing back to
`SplitForPrincipal`/`SplitForInterest`/`LoanPayementManualEntry`)*

---

## 1.18 Tax-relevant capabilities in the model

### P1-TAX-1 — Associate a category with a tax line
The user links a category to the line of a tax form it feeds, so the
classification work they already do carries over at tax time.
*(`Category.TaxRefNum`)*

### P1-TAX-2 — Work to a fiscal year that isn't the calendar year
The user's tax year can start somewhere other than January, and the product works
out which tax year each transaction falls into on that basis.
*(`Transactions.GetTaxYearRange(fiscalYearStart)`,
`TransactionExtras.MigrateTaxYears(fiscalYearStart)`)*

### P1-TAX-3 — Override the tax period for an individual transaction
Where a transaction's calendar date and its tax treatment disagree, the user can
set the tax date separately for that one transaction.
*(`TransactionExtra.TaxDate`, `Transaction.TaxDate`)*

### P1-TAX-4 — Have tax status of accounts and holdings carried through
Whether an account is taxable, tax-deferred or tax-free, and whether a holding's
income is taxable, follows through into how gains and income are grouped.
*(`Account.TaxStatus`, `Security.Taxable`, `Investment.TaxExempt`,
`Investment.Withholding`/`Taxes`)*

### P1-TAX-5 — See how much of a year's spending was sales tax
Totals can be produced with and without sales tax, so the user can see what they
actually paid for things versus what went to tax.
*(`Transaction.SalesTax`/`NetSalesTax`/`AmountMinusTax`,
`Transactions.GetBalance(withoutTax)`, `AccountBalanceInfo.SalesTax`)*

---

## 1.19 Keeping the data healthy

### P1-HEALTH-1 — Run a check over the data and see what needs fixing
The user can have the product look for broken relationships in their data —
transfers with a missing other half, holdings recorded twice, holdings with no
trades, parties nobody uses, institution connections nothing is attached to,
stock splits pointing at a holding that no longer exists — and act on what it
finds.
*(`MyMoney.CheckTransfers`, `MyMoney.CheckSecurities`,
`MyMoney.RemoveDuplicateSecurities`/`RemoveDuplicatePayees`/
`RemoveUnusedSecurities`/`RemoveUnusedPayees`/`RemoveUnusedOnlineAccounts`)*

### P1-HEALTH-2 — Have deletions take effect immediately in what they see
Something the user deletes disappears from the lists they work with right away,
even though it may only be fully removed from storage on the next save.
*(soft delete via `PersistentObject.OnDelete`, `Get*()` accessors filtering
`IsDeleted`, `PersistentContainer.RemoveDeleted`)*

### P1-HEALTH-3 — Undo a deletion that hasn't been saved
Something marked for deletion can be brought back before the change is committed.
*(`PersistentObject.Undelete`, `Categories.GetOrCreateCategory` undeleting)*

### P1-HEALTH-4 — Not lose long text by surprise
Where a field has a length limit, long input is shortened rather than the save
failing outright.
*(`PersistentObject.Truncate` used by every text property)*

---

## Phase 1 coverage checklist

Every top-level type declared in `Money.cs` and `Money_Loans.cs`. "Not
user-facing" entries are infrastructure the user never perceives directly.

### Persistent containers

| Type | Status |
|---|---|
| `Accounts` | P1-ACCT-1, P1-ACCT-6, P1-ACCT-7, P1-ACCT-14, P1-CUR-3 |
| `OnlineAccounts` | P1-ONLINE-1, P1-ONLINE-5 |
| `Aliases` | P1-ALIAS-1…P1-ALIAS-7 |
| `AccountAliases` | P1-ALIAS-8 |
| `Currencies` | P1-CUR-1…P1-CUR-5, P1-XFER-5 |
| `Payees` | P1-PAYEE-1…P1-PAYEE-8, P1-XFER-2 |
| `TransactionExtras` | P1-TXN-15, P1-TAX-2, P1-TAX-3 |
| `Categories` | P1-CAT-1…P1-CAT-13 |
| `Securities` | P1-INV-1, P1-INV-13, P1-INV-14, P1-INV-16, P1-INV-18 |
| `Transactions` | P1-TXN-1…P1-TXN-15, P1-FIND-1…P1-FIND-4, P1-IMPORT-1…P1-IMPORT-6, P1-RECON-1, P1-ACCT-4, P1-ACCT-5 |
| `Splits` | P1-SPLIT-1…P1-SPLIT-6, P1-XFER-9, P1-BUDGET-4 |
| `StockSplits` | P1-INV-10, P1-INV-11, P1-INV-12 |
| `RentBuildings` | P1-RENT-1…P1-RENT-5, P1-RENT-7 |
| `RentUnits` | P1-RENT-6, P1-RENT-7 |
| `LoanPayments` *(Money_Loans.cs)* | P1-LOAN-4, P1-LOAN-5 |

### Persistent entities

| Type | Status |
|---|---|
| `MyMoney` *(root; also partial in Money_Loans.cs)* | P1-WHOLE-1…P1-WHOLE-7, plus it hosts the cross-entity operations cited throughout |
| `Account` | P1-ACCT-1…P1-ACCT-14, P1-LOAN-1, P1-LOAN-2, P1-TAX-4 |
| `OnlineAccount` | P1-ONLINE-1…P1-ONLINE-6 |
| `Alias` | P1-ALIAS-1, P1-ALIAS-2, P1-ALIAS-3 |
| `AccountAlias` | P1-ALIAS-8 |
| `Currency` | P1-CUR-1, P1-CUR-2, P1-CUR-4 |
| `Payee` | P1-PAYEE-1…P1-PAYEE-5 |
| `TransactionExtra` | P1-TXN-15, P1-TAX-3 |
| `Category` | P1-CAT-1…P1-CAT-13, P1-BUDGET-1…P1-BUDGET-3, P1-TAX-1 |
| `Security` | P1-INV-1, P1-INV-2, P1-INV-3, P1-INV-7, P1-INV-14, P1-TAX-4 |
| `Transaction` | P1-TXN-1…P1-TXN-15, P1-XFER-1…P1-XFER-4, P1-SPLIT-1, P1-FIND-1, P1-IMPORT-1…P1-IMPORT-6 |
| `Split` | P1-SPLIT-1…P1-SPLIT-6, P1-XFER-9, P1-BUDGET-4, P1-BUDGET-5, P1-FIND-2 |
| `Investment` | P1-INV-4…P1-INV-9, P1-INV-10, P1-IMPORT-6 |
| `StockSplit` | P1-INV-10, P1-INV-11 |
| `RentBuilding` | P1-RENT-1…P1-RENT-5 |
| `RentUnit` | P1-RENT-6 |
| `LoanPayment` *(Money_Loans.cs)* | P1-LOAN-4, P1-LOAN-5, P1-LOAN-8 |

### Non-persistent domain types that still carry user meaning

| Type | Status |
|---|---|
| `Transfer` | P1-XFER-1…P1-XFER-10 — the link object that makes a transfer one event rather than two |
| `Loan` *(Money_Loans.cs)* | P1-LOAN-1, P1-LOAN-2, P1-LOAN-3, P1-LOAN-6, P1-LOAN-7 — computed loan view; not persisted, rebuilt on demand |
| `LoanPaymentAggregation` *(Money_Loans.cs)* | P1-LOAN-5, P1-LOAN-6, P1-LOAN-8 — an editable row combining derived and hand-entered payments |
| `RentExpenseTotal` | P1-RENT-4 — computed expense rollup |
| `RentalBuildingSingleYear` | P1-RENT-4 — computed per-year rollup |
| `RentalBuildingSingleYearSingleDepartment` | P1-RENT-4 — computed per-year, per-expense-class rollup |
| `AccountBalanceInfo` | P1-ACCT-4, P1-TAX-5 — the computed result of balancing an account |
| `ObservableStockSplits` | P1-INV-10 — the per-holding, always-current view of that holding's splits |
| `PayeeIndex` | P1-FIND-5 — the "which accounts has this party appeared in" lookup that makes suggestions relevant |
| `MfaChallengeAnswer` | P1-ONLINE-3 — deliberately not stored |
| `TransactionException` | P1-BUDGET-5, P1-WHOLE-7 — carries a user-facing problem and the transaction it's about |
| `MoneyException` | P1-TXN-5, P1-XFER-6, P1-XFER-10, P1-WHOLE-7 — carries the rule-violation messages the user actually reads |
| `ConcurrencyConflictException` | P1-WHOLE-4 — surfaces a collision rather than losing an edit |

### Enumerations (each a set of choices the user makes or sees)

| Type | Status |
|---|---|
| `AccountType` | P1-ACCT-2 |
| `AccountFlags` | P1-ACCT-6, P1-ACCT-8, P1-ACCT-12 |
| `TaxStatus` | P1-ACCT-8, P1-TAX-4 |
| `AliasType` | P1-ALIAS-2 |
| `StatusFlags` | P1-PAYEE-4 |
| `CategoryType` | P1-CAT-7 |
| `CalendarRange` | P1-BUDGET-2 |
| `SecurityType` | P1-INV-2, P1-INV-9 |
| `YesNo` | P1-INV-3 |
| `TransactionStatus` | P1-TXN-4, P1-TXN-6, P1-RECON-1 |
| `TransactionFlags` | P1-TXN-10, P1-TXN-12, P1-IMPORT-5, P1-BUDGET-6 |
| `SplitFlags` | P1-BUDGET-4 |
| `InvestmentType` | P1-INV-4, P1-INV-17 |
| `InvestmentTradeType` | P1-INV-5 |
| `ChangeType` | **Not user-facing** — internal classification of what kind of edit happened, used to drive saving |

### Infrastructure — not user-facing

| Type | Reason |
|---|---|
| `PersistentObject` | Base class supplying identity, change tracking and notification. Its *effects* are P1-WHOLE-2/3, but the type itself is never perceived. |
| `PersistentContainer` | Base collection class supplying the same. Same reasoning. |
| `ChangeEventArgs` | Internal description of a change, passed between the model and whatever is listening. |
| `ErrorEventArgs` | Internal envelope for an error being reported onward (the *message* is P1-WHOLE-7). |
| `TransferChangedEventArgs` | Internal notification that a transaction's transfer link is about to change. |
| `SplitTransferChangedEventArgs` | Internal notification that an itemised line's transfer link is about to change. |
| `ThreadSafeObservableCollection<T>` | Threading helper that marshals collection updates to the interface thread. |
| `BatchSync` | Internal reference counter behind batched updates. |
| `AccountComparer` | Sort order helper (accounts by name). |
| `OnlineAccountComparer` | Sort order helper (institutions by name). |
| `CurrencyComparer` | Sort order helper (currencies by symbol). |
| `PayeeComparer` | Sort order helper (parties by name). |
| `PayeeComparer2` | Sort order helper (parties by name; null-tolerant variant). |
| `SecurityComparer` | Sort order helper (holdings by name then symbol). |
| `SecuritySymbolComparer` | Sort order helper (holdings by symbol). |
| `TransactionComparerByDate` | Sort order helper (ascending by date). The *resulting* order is P1-TXN-13. |
| `TransactionComparerByDateDescending` | Sort order helper (descending by date). |
| `TransactionComparerByTaxDate` | Sort order helper (by tax date). Supports P1-TAX-2. |
| `SplitIdComparer` | Sort order helper keeping itemised lines in the order they were entered. |

---

## Open questions from Phase 1

Things a human should double-check, because the call on whether they're
user-facing was a judgement rather than obvious from the code:

1. **`AccountType.CategoryFund` (value 9)** is explicitly commented as unused and
   only retained so existing databases don't shift. Treated as *not* a user
   choice, so it isn't listed under P1-ACCT-2. If the redesign wants to revive
   the idea of funding a category from an account, it would need reinventing
   rather than resurfacing.
2. **`CategoryType.Reserved` and `CategoryType.RecurringExpense`** are both dead:
   `Reserved` is unused-but-undeletable, and `RecurringExpense` is silently
   rewritten to `Expense` on assignment, its role replaced by
   `Category.Frequency`. P1-CAT-7 lists only the live choices; P1-BUDGET-2 covers
   the replacement.
3. **`Account.SyncGuid`** is a per-account synchronisation identifier with no
   visible behaviour in this file. Classified as infrastructure and given no
   scenario. If a later phase finds a dialog exposing it, revisit.
4. **`Transaction`'s transient view flags** (`TransactionDropTarget`,
   `AttachmentDropTarget`, `IsCutting`, `IsExpanded` on `Security`,
   `Category.IsEditing`) are drag-drop and editing states that live on the domain
   objects but belong to the views. Deliberately not given domain scenarios —
   they're Phase 3 material.
5. **`TransactionFlags.Budgeted` has no property on `Transaction`.** Only `Split`
   exposes budget participation (`Split.IsBudgeted`). The flag bit exists and is
   deliberately cleared on import, so whole-transaction budgeting may be a
   capability that was removed or was never completed. P1-BUDGET-4 therefore
   describes itemised-line budgeting only. Worth confirming against the budget
   UI in a later phase.
6. **`Transaction.to` and `Split.to`** are commented "for debugging only" but are
   genuinely read by the broken-transfer check (P1-XFER-8). Treated as
   infrastructure supporting that scenario rather than as their own capability.
7. **Stock quote fetching** (`StockQuoteCache`, `MyMoney.SetStockQuoteCache`) is
   referenced here but implemented elsewhere (`StockQuotes/`). P1-INV-7 and
   P1-INV-19 capture only what the domain model does with prices; how they're
   obtained is Phase 8.
8. **Attachments** are represented here only as a flag on a transaction
   (P1-TXN-12). The filing, viewing and management of the documents themselves is
   Phase 8.

---

# Phase 2: Navigation & Shell

**Source examined (in full):**

| File | Lines |
|---|---|
| `Source/WPF/MyMoney/MainWindow.xaml` | 438 |
| `Source/WPF/MyMoney/MainWindow.xaml.cs` | 5,292 |
| `Source/WPF/MyMoney/Controls/Accordion.xaml` + `Accordion.xaml.cs` | 13 + 348 |
| `Source/WPF/MyMoney/App.xaml.cs` | 389 |
| `Source/WPF/MyMoney/Commands/Commands.cs` (`AppCommands`) | 149 |
| `Source/WPF/MyMoney/Utilities/RecentFilesMenu.cs` | 56 |
| `Source/WPF/MyMoney/Utilities/HelpService.cs` | 114 |
| `Source/WPF/MyMoney/Controls/AppSettings.xaml` + `AppSettings.xaml.cs` | 86 + full |
| `Source/WPF/MyMoney/Controls/QuickFilterControl.xaml.cs` | 86 |
| `Source/WPF/MyMoney/Controls/OutputPane.xaml.cs` | full |

Read in the relevant part only, because they are shell *collaborators* whose own
content belongs to a later phase: `Views/ChangeTracker.cs` (`GetSummary` — what the
pending-changes flyout shows), `View Selectors/AccountsControl.xaml.cs`
(`IContainerStatus` implementation — what the left-nav header figure is),
`Utilities/AppTheme.cs`, `Utilities/MessageBox/MessageBoxEx.xaml.cs` (its use of the
main window's dimming `Shield`).

> **Path note for later phases.** `CLAUDE.md`'s architecture section is accurate for
> this phase: `MainWindow.xaml`/`.xaml.cs` and `Controls/Accordion.xaml*` are exactly
> where it says. Two things worth recording anyway: the left-navigation *panels*
> (`AccountsControl`, `CategoriesControl`, `PayeesControl`, `SecuritiesControl`,
> `RentsControl`, `ReportsControl`, `RetirementControl`, `BalanceControl`) live in a
> folder literally named **`Source/WPF/MyMoney/View Selectors/`** — with a space, and
> *not* under `Views/` — so a `Views/` glob will silently miss them. And `HelpService`
> is in `Utilities/HelpService.cs` despite declaring `namespace Walkabout.Help`.

**What this phase covers and deliberately does not.** This section catalogs the
app's *chrome* — how a user gets into their data, chooses which part of their
finances to work on, moves between places, issues commands, learns what the product
is doing, and shapes how it looks. The *content* of each destination — what an
account register looks like, what a category tree lets you edit, what a report says —
is Phase 3, 4 and 5. Where the shell merely provides the doorway to something a later
phase owns, the doorway is captured here and the room is left alone.

---

## 2.1 Getting into the app and onto the right data

### P2-START-1 — Pick up exactly where they left off
When the user reopens the product it comes back the way they left it: the same
financial file open, the window the same size in the same place, the navigation and
chart panes the same width and height, each screen showing the same selection, and
the charts set up the same way. Nothing has to be re-chosen to resume work.
*(`MainWindow.LoadConfig`/`SaveConfig`, `Settings.WindowLocation`/`WindowSize`/
`ToolBoxWidth`/`GraphHeight`/`GraphState`, `Settings.GetViewState`/`SetViewState`,
`MainWindow.BeginLoadDatabase`)*

### P2-START-2 — Be told when the data they were using has gone
If the file the product last had open is no longer where it was, the user is told
plainly which one is missing rather than being dropped into an empty or broken app.
*(`MainWindow.BeginLoadDatabase` "Previous database no longer exists" message)*

### P2-START-3 — Start straight from a file they double-clicked
Opening a financial file or a downloaded statement from outside the product launches
it on that file. If the product is already running, the existing window is brought to
the front and handed the file instead of a second copy starting up.
*(`App.SaveImportArgs`, `App.BringToFrontApplicationIfAlreadyRunning`,
`MainWindow.ParseCommandLine`, `MainWindow.OnImportFolderContentHasChanged` watching
the shared pending-import list)*

### P2-START-4 — Ask the product how to start it differently
A user starting the product from a command line can ask what options it accepts and
is shown them, including starting with no file loaded at all.
*(`MainWindow.ShowUsage`, `ParseCommandLine`'s `/n`, `/nd`, `/nosettings`, `/h`)*

---

## 2.2 The data file as a whole

### P2-FILE-1 — Start a brand new set of books
The user creates a fresh, empty financial file, names it and says where it should
live, and the product switches to it.
*(`MainWindow.OnMenuFileNewClick`, `NewSqliteDatabase`, `NewSqliteDatabaseDialog`)*

### P2-FILE-2 — Switch to a different set of books
The user picks from the financial files the product already knows about and switches
to that one, after being given the chance to save anything outstanding first.
*(`MainWindow.OnCommandFileOpen`, `OpenDatabaseDialog`, `OpenRegisteredDatabase`,
`SaveIfDirty`)*

### P2-FILE-3 — Jump back to something they were using recently
The files the user has opened lately are offered directly, most recent first, so
switching between two sets of books is a single click.
*(`RecentFilesMenu.Refresh`, `MenuRecentFiles`, `DatabaseRegistry` ordering by last
used, `RegisterRecentDatabase`)*

### P2-FILE-4 — Save their work and be told it happened
Saving writes everything outstanding and the user is told what it was saved to and
how long it took; optionally a sound confirms it. Saving is only offered when there
is somewhere to save to.
*(`MainWindow.Save`, `OnCommandCanSave`, `ShowMessage`, `Settings.PlaySounds`)*

### P2-FILE-5 — See exactly what is waiting to be saved
Before saving, the user can open a summary of everything changed since the last save,
broken down by what kind of record it is and whether it was added, changed or
removed — and can click straight through to the affected transactions.
*(`PendingChangeDropDown`/`pendingChangeFlyout`, `OnOpeningPendingChangeFlyout`,
`ChangeTracker.GetSummary`)*

### P2-FILE-6 — Never lose work by accident
Whenever the user is about to do something that would discard unsaved work — closing
the product, opening another file, reverting, or recovering from an unexpected error
— they are asked first, and can save, discard or back out.
*(`MainWindow.SaveIfDirty`, `OnClosing`, `OnRecentDatabaseSelected`,
`App.HandleUnhandledException` calling `SaveIfDirty`)*

### P2-FILE-7 — Throw away everything since the last save
The user can abandon all changes made since the last save, is told how many changes
that is, and has to confirm before the file is reloaded from storage.
*(`MainWindow.OnCommandRevertChanges`, `OnCommandCanRevert`,
`ChangeTracker.ChangeCount`)*

### P2-FILE-8 — Keep a copy somewhere else, in a form of their choosing
The user can write the whole of their data out to a new file, choosing the form it
takes — the product's own storage, a readable or compact interchange format, or a
plain spreadsheet export of what's currently listed.
*(`MainWindow.OnCommandFileSaveAs` → `SaveAsSqlite`/`SaveAsSqlCe`/`SaveAsXml`/
`SaveAsBinaryXml`/`ExportCsv`; format specifics are Phase 6)*

### P2-FILE-9 — Take a backup
The user can write a backup of their current data to a location they choose, offered
in the form that matches how their data is stored, and is told when it's done.
*(`MainWindow.OnCommandBackup`, `GetBackupPath`, `AppCommands.CommandFileBackup` —
see Open Question 5: this command has no menu item and is currently unreachable)*

### P2-FILE-10 — Get at the files behind their data
The user can open the folder their financial data lives in, to find attachments,
statements, logs or backups. The option is only offered when there is such a folder.
*(`MainWindow.OnCommandOpenContainingFolder`, `OnCommandCanOpenContainingFolder`)*

### P2-FILE-11 — Bring in data from outside
The user can pull in statements, exports and other people's copies of the product's
own data from files on disk, picking several at once, and watch the progress as it
happens.
*(`MainWindow.OnCommandFileImport`, `ImportQif`/`ImportOfx`/`ImportXml`/`ImportCsv`/
`ImportMoneyFile`; the import behaviour itself is Phase 6)*

### P2-FILE-12 — Make the product the natural home for financial files
The user can tell the product to take ownership of statement and data file types, so
that double-clicking one in future opens it here.
*(`MainWindow.OnCommandFileExtensionAssociation`, `FileAssociation.Associate`)*

### P2-FILE-13 — Let someone else sign in to shared data
Where the data is held somewhere that supports more than one person, the user can add
another sign-in. Where it isn't, the option isn't offered at all.
*(`MainWindow.OnCommandFileAddUser`, `OnCommandCanExecuteAddUser`,
`IDatabase.SupportsUserLogin` driving `MenuFileAddUser.Visibility`)*

### P2-FILE-14 — Protect the data with a password, and change it later
Where the storage supports it, the user sets a password when creating a copy and can
change it afterwards from the settings panel; where it doesn't, they aren't asked.
*(`MainWindow.PromptForPassword`, `PasswordWindow`, `AppSettings` password box,
`OnAppSettingsPanelClosed`, `DatabaseSecurityPasswordStore`)*

### P2-FILE-15 — Leave
The user closes the product from the menu or the window, and is given the chance to
save first.
*(`MainWindow.OnCommandFileExit`, `OnClosing`, `OnClosed` saving layout and
preferences)*

---

## 2.3 Choosing which part of their finances to work on

### P2-NAV-1 — Choose between the areas of the product from one place
The user has to choose between a variety of views that let them work on the different
kinds of information the product holds. Choosing one changes what the main working
area shows and presents them a new list of things to pick from within that area.
*(`Accordion`, `MainWindow.toolBox.Add(...)` for ACCOUNTS / CATEGORIES / PAYEES /
SECURITIES / RENTALS, `OnToolBoxItemsExpanded`, `OnSelectionChangeFor_*`)*

### P2-NAV-2 — Have one area open at a time, using all the room available
Opening one area closes whichever was open before and gives the newly opened one the
full height of the panel, so the user is never squinting at five short lists at once.
*(`Accordion.OnExpanderExpanded`/`OnExpanderCollapsed`, `SetRowHeight`)*

### P2-NAV-3 — Pick something out of a long list by typing
Within an area that can hold hundreds of entries, the user narrows the list by typing
part of what they're looking for. The search box belongs to the open area and
disappears when that area is closed.
*(`QuickFilterControl`, `Accordion.Add(..., searchBox: true)` for categories, payees
and securities, `Accordion.FilterUpdated` → `MainWindow.OnToolBoxFilterUpdated`)*

### P2-NAV-4 — See an area's headline figure without opening it
An area can show a single summary number on its own header, so for instance the
user's net worth is visible whether or not the accounts list is the one currently
open.
*(`IContainerStatus`, `Accordion.Add` status text block,
`AccountsControl.SetTextBlock` and its net-worth update)*

### P2-NAV-5 — Have the navigation follow them when they arrive by another route
When the user reaches something by a route other than the left-hand navigation — a
back button, a link in a report, a freshly downloaded batch — the navigation catches
up on its own: the right area opens and the right entry within it is highlighted.
*(`MainWindow.OnAfterViewStateChanged` setting `toolBox.Selected` and each panel's
selection from the view's active account/payee/category/security/rental)*

### P2-NAV-6 — Have task-specific areas appear while a task is under way
When the user starts a task that needs its own controls — balancing an account
against a statement, choosing a report, planning for retirement — a panel for it
appears in the navigation, is selected for them, and disappears again when the task
is over or they navigate elsewhere.
*(`MainWindow.BalanceAccount`/`HideBalancePanel`, `ShowReportsPanel`/
`HideReportsPanel`, `ShowRetirementPanel`/`HideRetirementPanel`, `Accordion.Add`/
`Remove`; the panels' own content is Phase 3/5)*

### P2-NAV-7 — Not be shown areas for things they don't do
A user who doesn't track rental property doesn't see a rentals area at all; turning
the feature on makes it appear and turning it off removes it, without restarting.
*(`MainWindow.UpdateRentalManagement`, `OnUpdateRentalTab`,
`Accordion.ContainsTab`/`RemoveTab`, `DatabaseSettings.RentalManagement`)*

### P2-NAV-8 — Decide how much room navigation gets
The user drags the boundary between the navigation panel and the working area to suit
their screen, and that choice is remembered.
*(`GridSplitter` in `GridColumns`, `Settings.ToolBoxWidth`, `Grid_SizeChanged`)*

---

## 2.4 Retracing their steps

### P2-HIST-1 — Go back to where they just were
Having followed a chain of links — a report to a category, a category to a payee, a
payee to an account — the user can step back through it and forward again, using
on-screen buttons, the keyboard, or the back/forward buttons on their mouse.
*(`MainWindow.Back`/`Forward`, `navigator` (`UndoManager`) of `ViewCommand`s,
`BackButton`/`ForwardButton`, `OnPreviewKeyDown` Alt+Left/Right and browser keys,
`OnPreviewMouseDown` thumb buttons)*

### P2-HIST-2 — Land back on the same row, not just the same screen
Going back doesn't just return to a screen, it returns to the exact entry they were
looking at, unless they were deliberately sent somewhere more specific.
*(`ViewCommand.Undo`/`Redo` restoring `ViewState`,
`MainWindow.RestorePreviouslySavedSelection`, `TrackSelectionChanges`)*

### P2-HIST-3 — Only be offered a direction that exists
Back and forward are offered only when there is actually somewhere to go in that
direction.
*(`OnCommandBackCanExecute`, `CommandBinding_CanExecute`, `UndoManager.CanUndo`/
`CanRedo`)*

---

## 2.5 The main working area

### P2-WORK-1 — Have one place where the work happens
Whatever the user is doing — a register of transactions, a loan schedule, a list of
holdings, currencies, rename rules, a rental summary, a report — it fills the same
main area, so the product has one focal point rather than a scatter of windows.
*(`MainWindow.EditingZone`, `CurrentView`, `SetCurrentView<T>`, `IView`)*

### P2-WORK-2 — Come back to a screen as they left it
Returning to a screen they used earlier shows it configured the way they had it,
within the session and across restarts.
*(`GetOrCreateView<T>` caching one instance per screen, `IView.ViewState`,
`Settings.GetViewState`/`SetViewState`/`GetViewStateNode`, `SaveConfig`)*

### P2-WORK-3 — Decide how much room the working area gets
The user drags the boundary between the working area and the charts beneath it, and
that choice is remembered.
*(`GridSplitter` between `EditingZone` and `TabForGraphs`, `Settings.GraphHeight`)*

### P2-WORK-4 — Always know what they're looking at, and whether it's saved
The window itself says which set of books is open, what is currently being shown, and
whether there is unsaved work.
*(`MainWindow.UpdateCaption`, `IView.Caption`, the `*` dirty marker)*

---

## 2.6 The picture beneath the numbers

### P2-PANE-1 — See a picture of whatever they're working on
Below the working area, the user gets charts that match what's on screen — a balance
trend, a history of activity, a breakdown of income and of spending, a holding's
price history, a loan's payments, a property's profit and loss — without having to
ask for them.
*(`TabForGraphs` and its tabs, `MainWindow.UpdateCharts`, `SetChartsDirty`; the charts
themselves are Phase 5)*

### P2-PANE-2 — Click the picture to filter the numbers
Clicking a slice of the spending breakdown, a bar of the history, or a point on the
trend narrows the list above it to just that, so the picture is a way of navigating
rather than only something to look at.
*(`PieChartSelectionChanged`, `HistoryChart_SelectionChanged`, `OnGraphMouseDown`)*

### P2-PANE-3 — Not be shown pictures that don't apply
Charts that mean nothing for the current screen are not offered at all, and if the
one the user was looking at stops applying they are moved to one that does.
*(`UpdateCharts` tab visibility/selection logic per current view)*

### P2-PANE-4 — Watch what a long job is doing, and dismiss it when done
When the product has something to report from a long-running job, a running commentary
appears in the same strip, and the user can close it when they've read it.
*(`OutputPane`, `ShowOutputEvent`/`HideOutputEvent`, `MainWindow.ShowOutputWindow`/
`HideOutputWindow`, `TabOutput` with its close box)*

### P2-PANE-5 — Follow a download or import as it happens
While statements are being fetched or files imported, the user sees each account's
progress in its own panel, can cancel it, and clicking a finished batch takes them
straight to what arrived.
*(`TabDownload`, `DownloadControl`, `OfxDownloadController`, `ShowDownloadTab`/
`HideDownloadTab`, `OfxDownloadControl_SelectionChanged` → `ViewTransactions`;
download behaviour is Phase 6/8)*

---

## 2.7 Commanding the product

### P2-CMD-1 — Find every capability in one predictable place
Everything the product can do is reachable from a menu bar grouped the way users
expect — the file, editing, what to look at, reports, searching, online activity, and
help.
*(`MainMenu` with `MenuFile`/`MenuEdit`/`MenuView`/`MenuViewReports`/`MenuQuery`/
`MenuOnline`/`MenuHelp`)*

### P2-CMD-2 — Reach the things they do constantly without the menu
The handful of actions a user performs many times a session — fetching new
statements, saving, seeing what's new in the product — sit permanently on screen.
*(`ButtonSynchronize`, `PendingChangeDropDown`, `ButtonShowUpdateInfo`)*

### P2-CMD-3 — Drive the product from the keyboard
The user can search, switch appearance, move back and forward, and reach any menu
without touching the mouse.
*(`Window.InputBindings`: Ctrl+F, Ctrl+L, Alt+Left/Right; menu access keys;
`HelpService`'s F1)*

### P2-CMD-4 — Only be offered what would actually work
Commands that can't do anything right now are visibly unavailable rather than failing
when pressed — saving with nothing to save to, reverting with nothing to revert,
synchronising while a sync is running, updating prices while an update is in flight,
adding a user where that isn't supported.
*(the `CanExecute` handlers: `OnCommandCanSave`, `OnCommandCanRevert`,
`CanSynchronizeOnlineAccounts`, `CanUpdateSecurities`, `OnCommandCanExecuteAddUser`,
`OnCommandCanOpenContainingFolder`)*

### P2-CMD-5 — Cut, copy, paste and delete what they have selected
The clipboard commands act on whatever the user is actually focused on — the text
they're typing, the row they've selected, the report they're reading — rather than
meaning one fixed thing, and say so when something goes wrong.
*(`MainWindow.GetClipboardClient`, `IClipboardClient`, `TextBoxClipboardClient`/
`ComboBoxClipboardClient`/`RichTextBoxClipboardClient`/
`FlowDocumentViewClipboardClient`, `ClipboardMonitor` keeping paste availability
current)*

### P2-CMD-6 — Search from anywhere
A single keystroke puts the cursor in the search box of whatever the user is
currently looking at, wherever they are in the product.
*(`OnFind` → `IView.FocusQuickFilter`, `Find` command bound to Ctrl+F)*

### P2-CMD-7 — Build a precise search when typing a word isn't enough
The user can open a form for constructing a detailed search, run it, and clear it
again, with the working area switching to show the results.
*(`OnCommandShowQuery`/`ShowQueryPanel`/`HideQueryPanel`, `OnCommandQueryRun`,
`OnCommandQueryClear`, `MenuQueryShowForm` check state; the form itself is Phase 3)*

### P2-CMD-8 — Go straight to a list that isn't an account
Some of the product's information isn't reached by picking something in the left-hand
navigation — the holdings list, the currencies list, the rename rules — and the user
opens those directly from a menu.
*(`OnCommandViewSecurities`, `OnCommandViewCurrencies`, `OnCommandViewViewAliases`;
the views themselves are Phase 3)*

### P2-CMD-9 — Tidy up the data from one place
The housekeeping jobs — dropping holdings nothing refers to, checking transfers,
repairing itemised transactions, folding duplicate holdings and parties together,
clearing category frequencies — are gathered in one menu, and each says what it did.
*(`MenuEdit`'s "Cleanup" submenu: `OnRemovedUnusedSecurities`,
`OnCommandTroubleshootCheckTransfer`, `MenuFixSplits_Click`,
`MenuRemoveDuplicateSecurities_Click`, `MenuRemoveDuplicatePayees_Click`,
`OnResetCategoryFrequencies`; the underlying operations are P1-HEALTH-1)*

### P2-CMD-10 — Reach any report from one menu
All the product's reports are listed together, and choosing one replaces the working
area with it.
*(`MenuViewReports` and its commands → `SetCurrentView<FlowDocumentView>` +
`GenerateReport`; the reports are Phase 5)*

### P2-CMD-11 — Reach anything online from one menu
Fetching statements, refreshing prices, setting up a new download and choosing which
services to use are gathered together.
*(`MenuOnline`: `OnSynchronizeOnlineAccounts`, `OnCommandUpdateSecurities`,
`OnCommandDownloadAccounts`, `OnStockQuoteServiceOptions`; behaviour is Phase 8)*

---

## 2.8 Knowing what the product is doing

### P2-STATUS-1 — See what they're worth as soon as the data opens
On opening a file, the user is shown their total net worth across every account
without asking for it.
*(`MainWindow.ShowNetWorth` → `ShowMessage`)*

### P2-STATUS-2 — Get a running commentary along the bottom
A status line reports what the product just did — what was loaded or saved and how
long it took, where a backup went, what an import found, why a rate lookup failed —
without interrupting the user.
*(`StatusMessage`, `IStatusService.ShowMessage`, `InternalShowMessage`,
`AnimatedMessage` for messages that change after the fact)*

### P2-STATUS-3 — See how far through a long job the product is
Importing, downloading and other long operations show a progress bar and a line
saying what is currently being worked on.
*(`ProgressBar`/`ProgressPrompt`, `IStatusService.ShowProgress`, busy cursor)*

### P2-STATUS-4 — Be told about a problem without losing their place
Errors and confirmations appear as a message the user can read at their own pace,
with the detail available if they want it, over a dimmed window so it's clear what is
waiting on them.
*(`MessageBoxEx.Show(message, title, details, ...)`, the main window's `Shield`
overlay)*

### P2-STATUS-5 — Be told the product crashed last time
If the product died unexpectedly on a previous run, the user is told on the next
start and shown what happened, rather than it disappearing silently.
*(`App.CheckCrashLog`, `CrashReport.Load`, `Log.FatalUnhandledException`)*

### P2-STATUS-6 — Be offered a way out of an unexpected failure
When something goes wrong that the product didn't anticipate, the user is told, given
the details, and offered the chance to save their work before anything else happens.
*(`App.OnUnhandledException`/`OnAppDomainUnhandledException`/
`TaskScheduler_UnobservedTaskException1` → `HandleUnhandledException` →
`SaveIfDirty`)*

### P2-STATUS-7 — Notice that there's a newer version, and read what's in it
When a newer version of the product exists, a button appears offering to show what
changed, and after an update the user is shown what's new since the version they were
on, with a way to get the update.
*(`CheckLastVersion`, `ChangeListRequest`, `OnChangeListRequestCompleted`,
`ButtonShowUpdateInfo`, `ShowChangeInfo` → `ChangeInfoReport`,
`OnInstallButtonClick`)*

---

## 2.9 Shaping the product to their preferences

### P2-PREF-1 — Switch between a light and a dark appearance
The user chooses whether the product is light or dark, either from settings or with a
single keystroke, and the change takes effect immediately and is remembered.
*(`AppCommands.CommandToggleTheme` (Ctrl+L), `OnCommandToggleTheme`, `OnThemeChanged`,
`AppTheme.SetTheme`, `Settings.Theme`, `AppSettings`'s theme list)*

### P2-PREF-2 — Adjust preferences without leaving what they were doing
Settings slide in over the side of the window rather than taking over the screen, and
close as soon as the user clicks back into their work or presses the back arrow.
*(`AppSettingsPanel` in `MainWindow.xaml`, `WpfHelper.Flyout`,
`OnPreviewMouseDown` dismissing it, `AppSettings.Closed`)*

### P2-PREF-3 — Say when their financial year starts
The user sets the month their year begins, and everything that reports by year
follows it.
*(`AppSettings` fiscal-year list, `DatabaseSettings.FiscalYearStart`,
`DatabaseSettings_PropertyChanged` → history chart and tax-year migration)*

### P2-PREF-4 — Choose the currency everything is expressed in
The user picks the currency totals are shown in and whether the symbol is displayed
alongside amounts, and the product re-expresses everything accordingly.
*(`AppSettings` currency list, `DatabaseSettings.DisplayCurrency`/`ShowCurrency`,
`MainWindow.ApplyDisplayCurrency`; the conversion itself is P1-CUR-3)*

### P2-PREF-5 — Turn whole features on and off
The user turns off parts of the product they don't use, such as rental-property
tracking, and the corresponding area leaves the navigation entirely.
*(`AppSettings` rental checkbox → `DatabaseSettings.RentalManagement` →
`UpdateRentalManagement`/`OnUpdateRentalTab`; see P2-NAV-7)*

### P2-PREF-6 — Tune the product's smaller habits
The user adjusts the details that affect day-to-day working: whether a sound plays on
save, how many days either side to search when matching a transfer, whether already
reconciled entries can be accepted, and how downloaded statement files should be
read.
*(`AppSettings` checkboxes and text box → `Settings.PlaySounds`/`TransferSearchDays`/
`AcceptReconciled`/`ImportOFXAsUTF8`)*

### P2-PREF-7 — Have every preference and layout choice remembered
Preferences are saved as they're changed and layout as the product closes, so nothing
has to be set twice.
*(`DatabaseSettings_PropertyChanged`'s delayed save, `Settings.Save`,
`MainWindow.SaveConfig` on close, and the "error saving settings" message when it
fails)*

---

## 2.10 Getting help and getting unstuck

### P2-HELP-1 — Get help about the screen they're on
Pressing for help opens documentation about what the user is currently looking at
rather than a generic front page.
*(`HelpService.HelpKeyword` attached to the window and to each report view,
`HelpKeyEventRouter` handling F1, `HelpService.OpenHelpPage`)*

### P2-HELP-2 — Read the full documentation
The user can open the product's documentation from the help menu.
*(`OnCommandViewHelp`)*

### P2-HELP-3 — Find out which version they're running
The user can check the version they have and who supplies the market data behind it.
*(`OnCommandHelpAbout`)*

### P2-HELP-4 — Read what changed between versions
The user can read the list of changes for the version they have at any time, not only
when prompted after an update.
*(`OnCommandViewChanges` → `ShowChangeInfo`, cached change list)*

### P2-HELP-5 — Get at the logs when something is wrong
When the user needs to report a problem, they can open the product's own log folder
directly.
*(`OnCommandViewLogs`, `Logger`'s log path)*

### P2-HELP-6 — Try the product out with realistic data
A user evaluating the product can fill an empty file with generated but realistic
data and immediately land on the first account, and can generate a fresh profile from
their own real data to build such a sample from.
*(`OnCommandAddSampleData`, `SampleDatabase.Create`, `MenuExportSampleData_Click`;
the sample-data dialog itself is Phase 4)*

---

## Phase 2 coverage checklist

Every shell/chrome element examined. "Not user-facing" entries are plumbing the user
never perceives directly.

### Window structure (`MainWindow.xaml`)

| Element | Status |
|---|---|
| `Window` chrome: title, icon, resize grip, modern window style | P2-WORK-4, P2-START-1 |
| `Window.InputBindings` (Alt+Left/Right, Ctrl+F, Ctrl+L) | P2-CMD-3, P2-HIST-1, P2-CMD-6, P2-PREF-1 |
| `Window.CommandBindings` (all 45 bindings) | P2-CMD-1…P2-CMD-11, P2-FILE-*, P2-HELP-* |
| `BackButton` / `ForwardButton` | P2-HIST-1, P2-HIST-3 |
| `MainMenu` — File / Edit / View / Reports / Query / Online / Help | P2-CMD-1 (each item mapped under 2.2, 2.6, 2.7, 2.10) |
| `MenuRecentFiles` | P2-FILE-3 |
| `MenuFileNew` (+ DEBUG-only engine submenu) | P2-FILE-1 — see Open Question 3 |
| `MenuEdit` "Cleanup" submenu | P2-CMD-9 — except `MenuGCCollect`/`MenuEnvironment`, Open Question 2 |
| Toolbar: `ButtonSynchronize` | P2-CMD-2 (behaviour Phase 8) |
| Toolbar: `PendingChangeDropDown` + `pendingChangeFlyout` + Revert button | P2-FILE-4, P2-FILE-5, P2-FILE-7 |
| Toolbar: `ButtonShowUpdateInfo` | P2-STATUS-7 |
| `GridColumns` + vertical `GridSplitter` | P2-NAV-8 |
| `toolBox` (`Accordion`) | P2-NAV-1…P2-NAV-8 |
| `EditingZone` (`ContentControl`) | P2-WORK-1, P2-WORK-2 |
| Horizontal `GridSplitter` | P2-WORK-3 |
| `TabForGraphs` + `TabTrends`/`TabHistory`/`TabIncomes`/`TabExpenses`/`TabStock`/`TabLoan`/`TabRental` | P2-PANE-1, P2-PANE-2, P2-PANE-3 (chart content is Phase 5) |
| `TabOutput` + `OutputPane` + `CloseBox` | P2-PANE-4 |
| `TabDownload` + `DownloadControl` + `CloseBox` | P2-PANE-5 (download behaviour is Phase 6/8) |
| `AppSettingsPanel` (`AppSettings`) | P2-PREF-1…P2-PREF-7, P2-FILE-14 — see Open Question 4 |
| `StatusBar`: `StatusMessage` | P2-STATUS-1, P2-STATUS-2 |
| `StatusBar`: `ProgressPrompt` + `ProgressBar` | P2-STATUS-3 |
| `Shield` overlay | P2-STATUS-4 — the dimming behind a modal message |

### `MainWindow.xaml.cs` responsibilities

| Responsibility | Status |
|---|---|
| Startup / command line (`ParseCommandLine`, `ShowUsage`, `OnMainWindowLoaded`) | P2-START-3, P2-START-4 |
| Config load/save (`LoadConfig`, `SaveConfig`, `GetGraphState`/`SetGraphState`) | P2-START-1, P2-NAV-8, P2-WORK-3, P2-PREF-7 |
| Database lifecycle (`BeginLoadDatabase`, `LoadDatabase`, `CreateNewDatabase`, `OpenRegisteredDatabase`, `RegisterRecentDatabase`, `SaveNewDatabase`, `SaveAs*`, `ExportCsv`) | P2-START-2, P2-FILE-1, P2-FILE-2, P2-FILE-3, P2-FILE-8 |
| Dirty tracking (`SetDirty`, `OnDirtyChanged`, `SaveIfDirty`, `Save`) | P2-FILE-4, P2-FILE-5, P2-FILE-6, P2-WORK-4 |
| `OnDataContextChanged` — rewiring every panel to newly loaded data | **Not user-facing** on its own; its effect is that P2-FILE-2 leaves a working app rather than stale panels |
| View management (`CurrentView`, `GetOrCreateView<T>`, `SetCurrentView<T>`, `cacheViews`) | P2-WORK-1, P2-WORK-2 |
| Navigation history (`Back`, `Forward`, `navigator`, `ViewCommand`, `SaveViewStateOfCurrentView`, `RestorePreviouslySavedSelection`, `TrackSelectionChanges`) | P2-HIST-1, P2-HIST-2, P2-HIST-3 |
| `IViewNavigator` (`NavigateToTransaction`, `ViewTransactions`, `NavigateToSecurity`) | P2-NAV-5 — how other parts of the product ask the shell to go somewhere |
| `IServiceProvider.GetService` | **Not user-facing** — how views, dialogs and reports reach shared services; a few branches have side effects (`ReportsControl`/`RetirementControl`/`DownloadControl` *create* their panel), which is P2-NAV-6 / P2-PANE-5 |
| `IStatusService` (`ShowMessage`, `ShowOutput`, `ShowProgress`, `ClearStatus`) | P2-STATUS-2, P2-STATUS-3, P2-PANE-4 |
| Toolbox selection handlers (`OnToolBoxItemsExpanded`, `OnSelectionChangeFor_*`) | P2-NAV-1 |
| Left-nav task panels (`BalanceAccount`/`HideBalancePanel`, `ShowReportsPanel`, `ShowRetirementPanel`) | P2-NAV-6 |
| `OnAfterViewStateChanged` / `OnBeforeViewStateChanged` | P2-NAV-5, P2-HIST-2, P2-WORK-4 |
| Chart orchestration (`UpdateCharts`, `SetChartsDirty`, `UpdateHistoryChart`, `UpdateTransactionGraph`, `FindCommonParent`, `UpdateCategoryColors`) | P2-PANE-1, P2-PANE-3 (chart content Phase 5) |
| Chart→list drill-down (`PieChartSelectionChanged`, `HistoryChart_SelectionChanged`, `OnGraphMouseDown`) | P2-PANE-2 |
| Clipboard routing (`GetClipboardClient`, the Cut/Copy/Paste/Delete handlers, `ClipboardMonitor`) | P2-CMD-5 |
| Undo/Redo command handlers | **Not user-facing as written** — permanently disabled; Open Question 1 |
| Query panel control (`ShowQueryPanel`, `HideQueryPanel`, `ExecuteQuery`, `OnCommandQuery*`) | P2-CMD-7 |
| Report launching (`OnCommandNetWorth`, `OnTaxReport`, …, `GenerateReport`, `OnReportCreated`) | P2-CMD-10 (reports Phase 5) |
| `ReportEventHandler` (weak-reference bridge from a report back to the shell) | **Not user-facing** — plumbing; its effect (a report link navigating the shell) is P2-NAV-5 |
| Import entry points (`OnCommandFileImport`, `ImportQif`/`ImportOfx`/`ImportXml`/`ImportCsv`/`ImportMoneyFile`, `LoadImportFiles`, `importWatcher`) | P2-FILE-11, P2-START-3 (import behaviour Phase 6) |
| Online entry points (`SyncAccount`, `DoSync`, `OnCommandDownloadAccounts`, `OnCommandUpdateSecurities`, `OnStockQuoteServiceOptions`) | P2-CMD-11 (behaviour Phase 8) |
| Backup (`OnCommandBackup`, `GetBackupPath`) | P2-FILE-9 — implemented but unreachable; Open Question 5 |
| Version check (`CheckLastVersion`, `ShowChangeInfo`, `OnInstallButtonClick`, change-list cache) | P2-STATUS-7, P2-HELP-4 |
| Stock-price plumbing (`SetupOnlineServices`, `OnStockQuoteHistoryAvailable`, `UpdateBalance`, `FillInMissingUnitPrices`, `CleanupStockQuoteManager`) | **Not user-facing from the shell's side** — Phase 8; the shell only hosts it |
| `AfterLoadChecks` (dangling-transaction removal, rebalance, security check, tax-year migration) | **Not user-facing** — startup repair; the user sees only a working file (P1-IMPORT-9) |
| `OnChangedUI` (deciding when charts are stale) | **Not user-facing** — the effect is P2-PANE-1 staying current |
| `Grid_SizeChanged`, `OnKeyboardFocusChanged` (empty), `OnQueryPanelGridSplitterDragCompleted` (empty) | **Not user-facing** — a layout workaround and two dead handlers |
| `EventTracking` nested class | **Not user-facing** — declared, never used anywhere |

### `Accordion` (the left-navigation mechanism)

| Member | Status |
|---|---|
| `Add(header, id, content[, searchBox])` | P2-NAV-1, P2-NAV-3, P2-NAV-4, P2-NAV-6 |
| `Selected` (get/set) | P2-NAV-1, P2-NAV-5 |
| `Expanded` event | P2-NAV-1 |
| `FilterUpdated` event / `OnFilterValueChanged` | P2-NAV-3 |
| `OnExpanderExpanded` / `OnExpanderCollapsed` / `SetRowHeight` | P2-NAV-2 |
| `ContainsTab` / `RemoveTab` / `Remove` / `RemoveRow` | P2-NAV-6, P2-NAV-7 |
| `IContainerStatus` / status `TextBlock` | P2-NAV-4 |
| `OnExpanderToAdd_SizeChanged` | **Not user-facing** — keeps the header's search box or figure from overflowing a narrow panel |
| `GlyphBrush` attached property | **Not user-facing** — styling hook |

### Other shell collaborators

| Type | Status |
|---|---|
| `AppCommands` (`Commands/Commands.cs`) | P2-CMD-1…P2-CMD-11 — except `CommandFileRestore` and `CommandReportBudget`, **not user-facing**: declared but never bound to anything (Open Question 5) |
| `RecentFilesMenu` | P2-FILE-3 |
| `HelpService` + `HelpKeyEventRouter` | P2-HELP-1 |
| `AppSettings` panel | P2-PREF-1…P2-PREF-7, P2-FILE-14 |
| `QuickFilterControl` | P2-NAV-3 (also used inside views — Phase 3) |
| `OutputPane` + its show/hide routed events | P2-PANE-4 |
| `CloseBox` | P2-PANE-4, P2-PANE-5 — the affordance for dismissing a transient pane |
| `AppTheme` | P2-PREF-1 |
| `MessageBoxEx` | P2-STATUS-4 (its own layout is Phase 4) |
| `AnimatedMessage` | P2-STATUS-2 — a status message that changes after the fact |
| `App.MyApplicationStartup` / `LoadSettings` / `SetDefaultSettings` | P2-START-1, P2-START-3, P2-PREF-6 |
| `App.CheckCrashLog` | P2-STATUS-5 |
| `App.HandleUnhandledException` and the three exception hooks | P2-STATUS-6 |
| `App.SaveImportArgs` / `BringToFrontApplicationIfAlreadyRunning` / `FindCurrentRunningMoneyApplication` | P2-START-3 |
| `ChangeTracker.GetSummary` | P2-FILE-5 (the tracker itself is P1-WHOLE-2) |
| `UndoManager` / `Command` / `ViewCommand` | P2-HIST-1, P2-HIST-2 — used as the navigation history, not as edit undo (Open Question 1) |
| `TabCloseBox`, `ProgressDots`, `Resizer`, `StackedBar`, `RoundedButton`, `CustomizableButton`, `SingleLineTextBlock`, `HandyTextBox`, `HandyFlowDocumentScrollViewer`, `MoneyDataGrid`, `MoneyDatePicker`, `FilteringComboBox`, `ColorPickerPanel`, `PasswordControl`, `Calculator`, `TrendGraph`, `QueryViewControl` | **Not shell** — general-purpose controls in `Controls/` that `MainWindow.xaml` does not host directly; they belong to the views and dialogs that use them (Phase 3/4) |
| `View Selectors/*Control` (Accounts, Categories, Payees, Securities, Rents, Reports, Retirement, Balance) | Captured here **only as navigation destinations** (P2-NAV-1, P2-NAV-6). Their own content — lists, trees, context menus, drag-and-drop, inline editing — is Phase 3; see Open Question 6 |

---

## Open questions from Phase 2

Things a human should double-check, because the call was a judgement rather than
obvious from the code:

1. **Undo and Redo are present in the Edit menu but permanently disabled.**
   `OnCommandCanUndo`/`OnCommandCanRedo` unconditionally set `CanExecute = false` and
   the execute handlers are empty; the `UndoManager manager` field created for it in
   the constructor is never used (only the separate `navigator` one, which drives
   back/forward). No scenario claims editing undo. A redesign should decide whether
   this is a capability to build or menu items to drop — but note that the *domain*
   model has no undo support either (nothing in Phase 1 corresponds to it), so it
   would be new work, not resurfacing.
2. **`Edit ▸ Cleanup` ships two developer diagnostics to end users** — "GC.Collect"
   and "Environment" (which dumps every process environment variable into a message
   box). Neither is behind `#if DEBUG`. Treated as *not* user-facing and given no
   scenario; P2-CMD-9 covers only the genuine data-tidying commands. Worth confirming
   that's the intent rather than an oversight.
3. **"File ▸ New" behaves differently in a debug build.** In release it goes straight
   to creating the product's default storage; in debug it grows a submenu offering a
   choice of storage engine. P2-FILE-1 describes the release behaviour (one command,
   no engine choice) on the assumption that the storage engine is not an end-user
   concept. If the redesign intends to expose a storage choice, that needs a real
   decision rather than inheriting a debug-only menu.
4. **The settings panel is a boundary call.** `AppSettings` is a flyout hosted
   directly inside `MainWindow.xaml`, not something under `Dialogs/`, and the theme it
   controls is bound to a window-level keyboard shortcut — so it is catalogued here as
   shell (2.9) rather than deferred to Phase 4. If Phase 4 would rather own the
   *contents* of that panel, P2-PREF-3…P2-PREF-6 are the ones to revisit; P2-PREF-1
   and P2-PREF-2 are genuinely shell either way.
5. **Three commands exist with no way to invoke them.**
   `AppCommands.CommandFileBackup` has a command binding in `MainWindow.xaml` and a
   complete implementation (`OnCommandBackup`, with per-storage-format file filters),
   but **no menu item, button or keyboard gesture anywhere references it** — backup is
   currently dead UI. `CommandFileRestore` and `CommandReportBudget` are declared in
   `AppCommands` and never bound at all. P2-FILE-9 documents backup as an intended
   capability with that caveat; restore and budget reporting get no scenario. A human
   should decide whether backup is a regression to fix or a feature to design in.
6. **The left-navigation panels straddle this phase and Phase 3.** The `View
   Selectors/` controls are simultaneously the navigation mechanism (pick an account →
   the register appears) and rich little views of their own (context menus for adding,
   deleting, renaming and synchronising accounts; drag-and-drop; inline editing;
   pasting an account from the clipboard). Only the navigation half is captured here.
   Their internal capabilities are deliberately left to Phase 3 — which should be told
   to look in `View Selectors/`, not just `Views/`.
7. **`Query ▸ Adhoc SQL Query` and `Query ▸ Show Last Update` expose raw storage.**
   Both open a free-form SQL window, and the adhoc one silently does nothing at all
   unless the data happens to be held in a server database. Not given their own
   scenario under P2-CMD-7, on the judgement that a free-form SQL console is a
   developer tool rather than an end-user capability. If the redesign wants a
   "power user" story, this is where it currently lives.
8. **The bottom chart strip's *arrangement* is shell; its *content* is Phase 5.**
   P2-PANE-1 and P2-PANE-3 describe only which pictures appear when and why — the
   logic for that lives entirely in `MainWindow.UpdateCharts`. What each chart
   actually plots is Phase 5's.
9. **Status messages are suppressed for the first three seconds after startup** and
   again for five seconds after a load completes (`loadTime + 3000`,
   `skipMessagesUntil`). P2-STATUS-2 is therefore quieter in practice than the code's
   many `ShowMessage` calls suggest. Flagged because a redesign that reworks the
   status area could easily reintroduce the message storm these guards exist to
   prevent.

---

# Phase 3: Views

**Source examined (in full):**

| File | Lines |
|---|---|
| `Source/WPF/MyMoney/Views/TransactionsView.xaml` | 929 |
| `Source/WPF/MyMoney/Views/TransactionsView.xaml.cs` | 7,907 |
| `Source/WPF/MyMoney/Views/SecuritiesView.xaml` + `.xaml.cs` | 586 + 982 |
| `Source/WPF/MyMoney/Views/CurrenciesView.xaml` + `.xaml.cs` | 257 + 641 |
| `Source/WPF/MyMoney/Views/AliasesView.xaml` + `.xaml.cs` | 173 + 391 |
| `Source/WPF/MyMoney/Views/LoansView.xaml` + `.xaml.cs` | 247 + 458 |
| `Source/WPF/MyMoney/Views/RentSummaryView.xaml` + `.xaml.cs` | 128 + 239 |
| `Source/WPF/MyMoney/Views/RentPayementsView.xaml` + `.xaml.cs` | 113 + 174 |
| `Source/WPF/MyMoney/Views/FlowDocumentView.xaml` + `.xaml.cs` | 126 + 385 |
| `Source/WPF/MyMoney/Views/TransactionSelectors.cs` | 451 |
| `Source/WPF/MyMoney/Views/GraphGenerators.cs` | 425 |
| `Source/WPF/MyMoney/Views/ChangeTracker.cs` | 541 |
| `Source/WPF/MyMoney/Views/FindManager.cs` | 217 |
| `Source/WPF/MyMoney/Views/IView.cs`, `ViewState.cs`, `TransactionPropertyChangeSubscription.cs` | 51 + 13 + 41 |
| `Source/WPF/MyMoney/View Selectors/AccountsControl.xaml` + `.xaml.cs` | 136 + 1,540 |
| `Source/WPF/MyMoney/View Selectors/CategoriesControl.xaml` + `.xaml.cs` | 141 + 1,167 |
| `Source/WPF/MyMoney/View Selectors/PayeesControl.xaml` + `.xaml.cs` | 64 + 397 |
| `Source/WPF/MyMoney/View Selectors/SecuritiesControl.xaml` + `.xaml.cs` | 83 + 401 |
| `Source/WPF/MyMoney/View Selectors/RentsControl.xaml` + `.xaml.cs` | 91 + 257 |
| `Source/WPF/MyMoney/View Selectors/BalanceControl.xaml` + `.xaml.cs` | 228 + 747 |
| `Source/WPF/MyMoney/View Selectors/ReportsControl.xaml` + `.xaml.cs` | 138 + 214 |
| `Source/WPF/MyMoney/View Selectors/RetirementControl.xaml` + `.xaml.cs` | 263 + 494 |

Also read in full because Phase 2 explicitly deferred it here:
`Source/WPF/MyMoney/Controls/QueryViewControl.xaml` + `.xaml.cs` (155 + 398) — the
advanced-search form hosted inside `TransactionsView`.

> **Path note.** Phase 2's warning holds and is confirmed: the left-navigation panels
> are in **`Source/WPF/MyMoney/View Selectors/`** — with a space — and a `Views/` glob
> misses all eight of them. Two further naming traps found here: the file
> `Views/RentPayementsView.xaml` (note the misspelling) declares a class named
> `RentInputControl`, and `Views/LoansView.xaml.cs` carries a copy-pasted
> `<summary>Interaction logic for RentInputControl1.xaml</summary>` comment. Neither
> file name matches its class name, so searching by class name alone will miss them.

**What this phase covers and deliberately does not.** This section catalogs what a
user can *do inside* a working surface once they've arrived at it: reading the data,
ordering and narrowing it, editing it in place, and the operations reachable from
right-click, keyboard and drag-and-drop. *Getting* to a surface is Phase 2. Anything
that opens a modal window is captured here only as the affordance ("the user can open
X from here"); what that window then does is Phase 4. The content of reports and
charts is Phase 5 and the mechanics of reading or writing a file are Phase 6, even
where the button that starts them lives on one of these surfaces.

---

## 3.1 The transaction register

### P3-REG-1 — Work through one account's entries in a single scrolling list
The user's main working surface is a list of transactions, one per row, that they read
top to bottom and edit directly. Everything else in this phase either changes what
that list contains, changes how much of each entry it shows, or acts on the entry
they have selected.
*(`TransactionsView`, `TheGrid_BankTransactionDetails`, `TransactionCollection`)*

### P3-REG-2 — Get the columns that suit what they're looking at
The list shows different information depending on what the user asked for: an account
register shows a cheque number, date, party, status, sales tax, payment, deposit and
running balance; a cross-account list adds which account and currency each entry
belongs to; an investment account's register adds the kind of activity, the holding,
units and unit price; and a single holding's history adds unit counts restated for
splits, a running holding total and a running value.
*(four `MoneyDataGrid`s: `TheGrid_BankTransactionDetails`, `TheGrid_TransactionFromDetails`,
`TheGrid_InvestmentActivity`, `TheGrid_BySecurity`; `SwitchLayout`)*

### P3-REG-3 — Choose between a compact and a detailed row
Each entry can be shown as a single line, or as three stacked lines that also show its
category and its note. The user toggles between the two with a button or a keystroke,
and the choice is remembered.
*(`OneLineView`, `CommandViewToggleOneLineView` (Ctrl+T), `ToggleShowLines`,
`TransactionPayeeCategoryMemoField`)*

### P3-REG-4 — Order the list by any column, with ties broken predictably
The user sorts by clicking a column heading. Entries that tie on the sorted column
fall back to date, then to amount, then to entry order, so a re-sort never shuffles
same-day entries into a different arrangement.
*(`MoneyDataGrid.SecondarySortOrder="Date,NegativeAmount,Id"`)*

### P3-REG-5 — Read an entry's state from how the row looks
Without opening anything, the user can tell which entries still need their approval
(bold), which just arrived from the bank, which are being reconciled right now, which
have been cut ready to move, which can't be edited, and which category each belongs to
(a colour swatch that a category inherits from its parent).
*(`TransactionCell`'s background/foreground/weight logic,
`TransactionCategoryColorColumn`, `ListItemForegroundUnacceptedBrush`,
`ListItemDownloadedBackgroundBrush`, `ListItemReconcilingBackgroundBrush`,
`ListItemCuttingBackgroundBrush`)*

### P3-REG-6 — See the balance after every entry, kept current as they edit
The account's running balance appears against each row and is recalculated when an
amount, a holding's units, a unit price or an activity type changes, without the user
asking for a refresh.
*(`TransactionNumericColumn` "Balance"/"RunningBalance", `Rebalance`,
`RefreshVisibleColumns`, `fieldsAffectingBalance`)*

### P3-REG-7 — Jump down a long list by typing
Typing jumps the selection to the first entry whose value in the currently sorted
column starts with what was typed, and a short pause starts a fresh search.
*(`TypeToFind`/`TransactionTypeToFind`, disabled while editing)*

### P3-REG-8 — Know what the listed entries add up to
Whatever is currently listed — an account, a category, a search result — the user is
told how many entries that is and what they total, with sales tax and investment value
called out separately when they apply.
*(`ShowBalance` → `Transactions.GetBalance`, `AccountBalanceInfo`, status line)*

### P3-REG-9 — Be told when they're not looking at everything
When a filter, a search or a query means the list is a subset, a watermark appears so
the user doesn't mistake a filtered list for the whole account.
*(`UpdateUX`, `FilterWatermark`)*

### P3-REG-10 — Change an entry's status by clicking it
The status of an entry is a button in its row: clicking cycles it between nothing and
cleared in normal use, and marks it off against the statement while balancing. Hovering
explains what each status means.
*(`TransactionStatusButton`, `ToggleTransactionStateReconciled`, status tooltips)*

### P3-REG-11 — Approve downloaded entries one at a time or all at once
The user marks the selected entry as reviewed with a keystroke, or clears the whole
current list of "needs review" in one action.
*(`CommandAccept` (Ctrl+Space), `CommandAcceptAll`, `ToggleTransactionStateAccept`)*

### P3-REG-12 — Void an entry from the list
The user can neutralise the selected entry without deleting it, and un-void it the same
way. Reconciled entries are left alone.
*(`CommandVoid`, `OnCommandVoid`)*

### P3-REG-13 — Delete an entry, with a confirmation and a guard
Deleting asks first — unless the user holds Shift — and refuses outright for a
reconciled entry that carries an amount.
*(`TransactionCollection.RemoveItem`, `ConfirmDelete`, `Prompt`, `QuietDelete`)*

### P3-REG-14 — Add a new entry in the right place
The user starts a new entry either at the end of the list or immediately after the one
they've selected, and lands straight in the party field ready to type.
*(`InsertNewTransaction` (Insert key), `EditModeSetToColumn("Payee")`,
`CanUserAddRows` on the bank register)*

### P3-REG-15 — Move an entry to a different account
The user can transfer the selected entry wholesale into another account, taking its
paperwork with it. A reconciled entry can't be moved.
*(`CommandMove`, `OnMoveTransaction`, `AccountHelper.PickAccount`,
`AttachmentManager.MoveAttachments`)*

### P3-REG-16 — Give an entry a tax date of its own
From the list the user can set — or clear — a separate tax date for the selected entry
when its calendar date and its tax treatment disagree.
*(`CommandSetTaxDate`, `OnSetTaxDate`, `TransactionExtras`; the picker is Phase 4)*

### P3-REG-17 — Recategorize everything currently listed in one step
Having narrowed the list to the entries they care about, the user can move all of them
to a different category at once.
*(`CommandRecategorize`, `OnRecategorizeAll` over `ViewModel`; the dialog is Phase 4)*

### P3-REG-18 — Cut, copy and paste entries
The user can copy the selected entry (or one itemised line of it) and paste it into an
account, with a cut entry visibly marked as pending until it's pasted or the clipboard
moves on.
*(`Cut`/`Copy`/`Paste`/`Delete`, `IsCutting`, `ClearCutState`, `OnClipboardChanged`)*

### P3-REG-19 — Take what's listed out of the product
Whatever is currently listed can be written out to a file for use elsewhere.
*(`CommandViewExport` → `Exporters.ExportPrompt`; formats are Phase 6)*

### P3-REG-20 — Be offered an account when entering the very first transaction
A user with no accounts yet who starts typing a transaction is asked to set up an
account there and then, rather than being blocked or silently losing the entry.
*(`OnCustomBeginEdit` creating an `AccountDialog` when `t.Account == null`)*

### P3-REG-21 — Look up who a party actually is
The user can search the web for the party on the selected entry, which is often the
quickest way to decode an unfamiliar name from a bank feed.
*(`CommandLookupPayee` (F3), `OnCommandLookupPayee`)*

### P3-REG-22 — Open the statement an entry was reconciled on
For an entry that was balanced against a saved statement, the user can open that
statement document straight from the list.
*(`CommandGotoStatement`, `OpenStatement`, `StatementManager.GetStatementFullPath`)*

### P3-REG-23 — Pivot from one entry to everything like it
From any entry the user can jump to everything in the same account, the same category,
with the same party, or in the same holding — and can open the properties of the
category or the holding it names.
*(`CommandViewTransactionsByAccount` (F8) / `ByCategory` (F7) / `ByPayee` (F6) /
`BySecurity` (F5), `CommandViewSecurity`, `CommandViewCategory`)*

---

## 3.2 Entering and editing an entry in place

### P3-EDIT-1 — Edit the exact field they clicked on
Because a row packs party, category and note into one column, clicking directly on the
note puts the cursor in the note rather than in the party — the user doesn't have to
click once to select the row and again to reach the field they wanted.
*(`OnCustomBeginEdit` → `GetHitFieldName` hit test → `OnStartEdit` matching
`EditorFor<FieldName>`)*

### P3-EDIT-2 — Move through the fields of an entry with the keyboard
Tab and Shift-Tab step forward and back through just the editable fields of the row,
skipping the read-only ones.
*(`MoveFocusToNextEditableField` / `MoveFocusToPreviousEditableField`)*

### P3-EDIT-3 — Type money into payment or deposit and have the other clear itself
Entering an amount in one money column empties the other, and if the entry is a
transfer its wording flips to match the direction the user just chose.
*(`TransactionAmountControl.SetEditFieldEmpty`, `GetOpposingField`,
`SwitchTransferCaption`)*

### P3-EDIT-4 — Pick a party, category or holding by typing part of it
Each of these fields narrows a long list as the user types, matching anywhere in the
name — and for holdings, in the ticker as well.
*(`FilteringComboBox` with `ComboBoxForPayee_FilterChanged`,
`ComboBoxForCategory_FilterChanged`, `ComboBoxForSymbols_FilterChanged`)*

### P3-EDIT-5 — Create a category on the spot when the one they want doesn't exist
Typing a category that isn't there yet offers to create it rather than silently
discarding what was typed, and the new category is filled in when they're done.
*(`ComboBoxForCategory_PreviewLostKeyboardFocus` → `EnterNewCategoryAsync`)*

### P3-EDIT-6 — Have the classification filled in from what they did last time
When the user enters a party they've dealt with before and hasn't chosen a category
yet, the product fills in the category — or the whole itemisation — from the previous
similar entry.
*(`AutoPopulateCategory` → `AutoCategorization.AutoCategoryMatch`,
`MyMoney.CopyCategory`)*

### P3-EDIT-7 — Hop between the two fields they use most
While editing, one keystroke moves between the party field and the amount field
without tabbing through everything in between.
*(Ctrl+P toggles, Ctrl+PageUp → Payee, Ctrl+PageDown → Payment)*

### P3-EDIT-8 — Finish an entry and move on in one keystroke
One keystroke fills in a category if one can be inferred, marks the entry as reviewed,
and moves to the next row ready for the next entry.
*(Ctrl+Enter branch in `OnDataGridPreviewKeyDown`)*

### P3-EDIT-9 — Do arithmetic inside a money field
Where an amount is entered, the user can work the number out in place rather than
reaching for a calculator.
*(`NumericTextBoxStyle` with `CalculatorPopup.CalculatorEnabled` — used by the
transaction, securities, currencies, balancing and retirement surfaces)*

### P3-EDIT-10 — Be told when a value won't be accepted, without losing the row
A value the model rejects marks the row with a warning that explains why on hover, and
the row stays in edit so the user can correct it rather than losing what they typed.
*(`RowValidationErrorTemplate`, `ValidationErrorGetErrorMessageConverter`,
`TransactionNumericColumn.CommitCellEdit`, `TransactionAmountControl.OnValidationError`)*

### P3-EDIT-11 — Not be able to edit rows that aren't really entries
The synthetic rows that represent one line of an itemised transaction inside a
by-category or by-payee list are read-only, and are shown greyed out.
*(`OnBeginEdit` cancelling for `t.IsReadOnly`, `UpdateForeground`'s disabled brush)*

### P3-EDIT-12 — Have a newly named holding pick up its price from the trade
When the user records a trade in a holding the product has no price for, the trade's
own unit price becomes that holding's price, and its price history starts downloading.
*(`OnDataGridCommit` seeding `Security.Price`, `pending` → `BeginUpdateHistory`)*

---

## 3.3 Breaking an entry into parts, in the list

### P3-SPLIT-1 — Turn the selected entry into an itemised one
The user chooses to itemise an entry and is given a first line pre-filled with the
entry's whole amount and category, ready to be broken up.
*(`CommandSplits`, `OnCommandSplits`)*

### P3-SPLIT-2 — Open and close the itemisation from a marker on the amount
An itemised entry carries a small marker on its amount showing how many lines it has;
clicking it opens the lines beneath the row and clicking again closes them. The marker
changes colour when the lines don't add up.
*(`ButtonStyleSplitAmount` bound to `Splits.Count` and `Splits.HasUnassigned`,
`OnButtonSplitClicked` → `ToggleSplitDetails`)*

### P3-SPLIT-3 — Edit the lines without leaving the list
The lines appear as their own small grid beneath the entry, each with its own party,
category, payment, deposit and note, all editable in place.
*(`myDataGridDetailView`, `TheGridForAmountSplit`)*

### P3-SPLIT-4 — See what's left to allocate and assign it with one key
While itemising, the user sees in plain language how much of the total hasn't been
allocated, and one keystroke pushes that remainder into the line they're on.
*(`NonNullSplits.SplitsBalanceMessage`, F6 handler in `OnDataGrid_KeyDown`,
`Splits.Rebalance`)*

### P3-SPLIT-5 — See every itemisation at once rather than opening each
The user can switch the whole list into a mode where every itemised entry shows its
lines inline, read-only, with any unallocated remainder called out.
*(`ViewAllSplits`, `CommandViewToggleAllSplits` (Alt+T), `myDataGridDetailMiniView`)*

### P3-SPLIT-6 — Insert a line in the middle of an itemisation
Pressing Insert while on a line adds a new one directly after it, inheriting the
entry's category, rather than only ever appending at the end.
*(`InsertNewSplit`)*

### P3-SPLIT-7 — Copy a whole itemisation onto another entry
The breakdown built on one entry can be copied and pasted onto another, and individual
lines can be copied, pasted and deleted on their own.
*(`CommandCopySplits`/`CommandPasteSplits`, `SplitViewContextMenu`,
`Splits.Serialize`/`DeserializeInto`)*

### P3-SPLIT-8 — Not be left with the empty line they started
Closing an itemisation cleans away lines that were begun and left blank.
*(`RestoreSplitViewMode` → `Splits.RemoveEmptySplits`)*

### P3-SPLIT-9 — Have sales tax kept out of what the lines must total
When an itemised entry also records sales tax, the lines are balanced against the
amount excluding that tax, so the user isn't asked to account for tax twice.
*(`OnDataGridCellEditEnding` computing `NonNullSplits.AmountMinusSalesTax`)*

---

## 3.4 Transfers, from inside the list

### P3-XFER-1 — Make a transfer by naming one of their own accounts
The party field offers the user's own accounts alongside their payees, phrased as
transfers, so recording a movement between accounts is the same gesture as recording a
payment.
*(`PayeesAndTransferNames` combining `Payees` with `Transaction.GetTransferCaption`
both ways)*

### P3-XFER-2 — Have the wording follow the direction they entered
Whether the transfer reads as money going to or coming from the other account follows
which money column the amount was typed into.
*(`TransactionAmountControl.SwitchTransferCaption`)*

### P3-XFER-3 — Jump to the other side of a transfer
One keystroke takes the user to the matching entry in the other account, whether the
link is on the entry itself or on one of its itemised lines.
*(`CommandGotoRelatedTransaction` (F12), `GotoRelated`, `IViewNavigator.NavigateToTransaction`)*

### P3-XFER-4 — Be offered a match when an entry looks like half a transfer
Asking to go to the related entry of something that isn't yet a transfer makes the
product hunt for an opposite-signed entry of the same amount in another account within
a few days, show the user what it found, and offer to link the two. If nothing is
found the user is offered a progressively wider search; if several match, they're shown
the candidates rather than having one picked for them.
*(`AttemptToMatchAndConvertPossibleTransfer`, `Settings.TransferSearchDays`,
`TransformTwoTransactionIntoTransfer`)*

### P3-XFER-5 — Be told why both sides of a transfer can't be itemised
Trying to make an itemised line transfer to an account whose entry is itself already
transferring from an itemised line gets a plain explanation and an offer to undo the
other side rather than a failure.
*(`ComboBoxForPayee_PreviewLostKeyboardFocus` split-transfer branch)*

### P3-XFER-6 — Review and repair transfers with a missing other half
When the product finds transfers whose other side has gone, the user is asked whether
to fix them and, if so, is shown exactly those entries in date order as their own list
to work through.
*(`CheckTransfers`, `ShowDanglingTransfers`, `TransactionFixedSelector`)*

---

## 3.5 Spotting and combining duplicates

### P3-DUP-1 — Be shown a likely duplicate as a visible link between the two rows
When the selected entry looks like a duplicate of a nearby one, a bracket is drawn in
the margin joining the two rows, so the user can see at a glance which pair is being
suggested — and it stays correct as they scroll, including when one end is off screen.
*(`TransactionConnector`, `TransactionConnectorAdorner`, `TransactionAnchor`,
`Transactions.FindPotentialDuplicate`, `Settings.DuplicateRange`)*

### P3-DUP-2 — Combine the pair with one click
The bracket carries a button that merges the two entries into one.
*(`TransactionConnectorAdorner.MergeButton` → `OnMergeButtonClick` → `Merge`)*

### P3-DUP-3 — Say they're not duplicates and stop being asked
Dismissing the suggestion records that these two really are separate events, so the
pair isn't flagged again.
*(`OnConnectorClosed` setting `NotDuplicate` on both)*

### P3-DUP-4 — Combine two entries by dragging one onto the other
The user can drag an entry onto another of the same amount to merge them; rows that
aren't a valid target don't accept the drop, and investment entries only accept a drop
from a matching kind of trade in the same holding for the same number of units.
*(`OnDataGridRowDragEnter`/`DragDrop`, `InvestmentsCanMerge`, `TransactionDropTarget`
highlight)*

### P3-DUP-5 — Have the better record kept automatically
When two entries are merged the product decides which one survives — preferring the
reconciled one, then the one that's part of a transfer, then the one with investment
detail, then the already-approved one — and moves any paperwork onto the survivor.
*(`Merge`'s swap logic, `AttachmentManager.MoveAttachments`)*

---

## 3.6 Paperwork attached to an entry

### P3-ATT-1 — See at a glance which entries have paperwork
A column at the left of the list shows a marker on every entry that has a document
filed against it.
*(`TransactionAttachmentColumn`, `TransactionAttachmentIcon`)*

### P3-ATT-2 — File a document by dropping it on the entry
The user drags files from their desktop straight onto a row; the row highlights as a
valid target and anything that fails is reported rather than silently dropped.
*(`OnDataGridRowDragDrop` file branch, `AttachmentDropTarget` border,
`AttachmentManager.GetUniqueFileName`)*

### P3-ATT-3 — Paste a picture straight onto an entry
An image on the clipboard is filed against the selected entry, and is animated shrinking
into that entry's paperclip so the user can see where it went.
*(`PasteAttachment`, the `MatrixAnimationUsingPath` storyboard)*

### P3-ATT-4 — Open the paperwork for an entry from its own row
The marker column doubles as a button that opens the attachment workspace for that
entry.
*(`TransactionAttachmentColumn.GenerateEditingElement` → `CommandScanAttachment` →
`AttachmentDialog.ScanAttachments`; the dialog itself is Phase 4/8)*

---

## 3.7 Narrowing what the list shows

### P3-FIND-1 — Narrow the current list by typing
A search box above the list filters it down as the user types, matching across the
party, category, note, reference, amounts and — for investment lists — the holding's
name, ticker and price, including inside itemised lines.
*(`QuickFilterControl`, `TransactionCollection.IsMatch`,
`Transactions.IsAnyFieldsMatching`/`IsSplitsMatching`)*

### P3-FIND-2 — Search beyond the current account with one character
Starting the search with an asterisk widens it from the current list to every
transaction the user has.
*(`QuickFilter` setter's `*` branch → `ViewTransactions(myMoney.Transactions.Items)`)*

### P3-FIND-3 — Limit the list to entries in a particular state
A single picker limits the list to everything, or only reconciled, only unreconciled,
only reviewed, only awaiting review, only classified, or only unclassified entries.
While balancing, the reconciled/unreconciled choices keep the current statement's
entries visible so they can still be unticked.
*(`TransactionViewMode` combo, `TransactionFilter`, `TransactionFilterSelector.
GetTransactionIncludePredicate`)*

### P3-FIND-4 — Build a precise search row by row
The user opens a form beneath the list and builds up conditions — a field, a
comparison and a value — combined with "and"/"or", adding and deleting rows, then runs
it. Incomplete rows are ignored, the conditions can be copied and pasted, and the form
stays filled in so the search can be re-run or adjusted.
*(`QueryViewControl`, `ListOfFields`/`ListOfOperations`/`ListOfConjunctions`,
`GetQuery`, `TransactionQuerySelector`; the Search button and the panel's show/hide is
P2-CMD-7)*

### P3-FIND-5 — Stack one narrowing on top of another
Narrowings compose: the user can be looking at one account, then narrow to a category,
then to a date range, then to a state, and each is applied on top of the last rather
than replacing it.
*(`TransactionSelector` chain — `TransactionAccountSelector`, `...PayeeSelector`,
`...CategorySelector`, `...SecuritySelector`, `...RangeSelector`, `...QuerySelector`,
`...FilterSelector`, `...RentalSelector`, `...FixedSelector`; `AddCategoryFilter`,
`AddHistoryFilter` used by the chart drill-down of P2-PANE-2)*

### P3-FIND-6 — Come back to the same list after a restart
The account, category, party, holding or property being viewed, the state filter, the
search text, the row/compact setting, the selected row and the whole chain of
narrowings are all remembered and restored.
*(`TransactionViewState`, `TransactionSelector` XML serialization, `UpdateView`)*

---

## 3.8 Investment surfaces

### P3-INV-1 — Switch an investment account between its activity and its holdings
An investment account offers two views of itself: the list of trades and cash activity,
and a portfolio showing what is currently held. The user switches between them with
tabs, and non-investment accounts don't show the tabs at all.
*(`InvestmentAccountTabs`, `SetActiveAccount`, `CashTab`/`PortfolioTab`)*

### P3-INV-2 — Record a trade in the same list as everything else
In an investment account the user records the kind of activity, which holding, how many
units and at what price alongside the ordinary date, party and amount, rather than in a
separate screen. Typing an unknown ticker creates the holding.
*(`myTemplateActivityEdit`, `myTemplateSymbolEdit`, `Activities`, `Securities`,
`ComboBoxSymbol_PreviewLostKeyboardFocus`)*

### P3-INV-3 — Follow one holding across every account
Looking at a single holding, the user sees every trade in it in date order with the
units as entered, the units restated for any stock splits, the running number held, the
price as entered and restated, the running value, and a visual indication of which
purchase each sale drew down.
*(`TheGrid_BySecurity`, `Transactions.UpdateStockUnitsAndRoutingPath`, `RoutingLines`
converter, `CurrentUnitStyle`/`RunningUnitStyle`)*

### P3-INV-4 — Expand or collapse the portfolio's detail
The portfolio can be read as headline groups or expanded to show every position, from
a single toggle.
*(`ToggleExpandAll`, `PortfolioReport.ExpandAll`/`CollapseAll`)*

### P3-INV-5 — Drill into a group of holdings
Clicking through a group in the portfolio replaces it with a report for just that
group.
*(`OnReportDrillDown`, `PortfolioReport.SelectedGroup`; report content is Phase 5)*

### P3-INV-6 — Reconcile what was held as at the statement date
While balancing an investment account, the portfolio is shown as at the statement date
rather than today, so holdings can be checked against the statement.
*(`GetReconciledExclusiveEndDate`, `CreatePortfolioReport`'s `reportDate`)*

---

## 3.9 Balancing an account against a statement

### P3-RECON-1 — Work through a statement beside the entries it covers
Starting to balance an account puts a panel beside the register showing which account,
the previous statement, the new statement's date and balance, the current reconciled
balance and how far off it is — while the register itself switches to compact rows and
to the unreconciled entries for that period.
*(`BalanceControl.Reconcile`, `TransactionsView.OnStartReconcile`,
`SetReconcileDateRange`)*

### P3-RECON-2 — Have the next statement's date and balance guessed for them
Opening a new statement proposes the month after the last one — handling month-end
correctly — and proposes the balance the account is expected to have on that date,
unless a balance was already saved for it.
*(`GetNextStatementDate`, `MyMoney.EstimatedBalance`,
`StatementManager.GetStatementBalance`)*

### P3-RECON-3 — Tick entries off, and untick them
Marking an entry as appearing on the statement records both that it's reconciled and
which statement it was cleared in; unticking puts it back to whatever it was before.
Optionally it is also treated as reviewed at the same time.
*(`ReconcileThisTransaction`, `ToggleTransactionStateReconciled`, Ctrl+Space,
`Settings.AcceptReconciled`)*

### P3-RECON-4 — Know exactly when they're done
The panel shows the difference between the statement and the account continuously and
only enables "done" when it reaches zero, with a large visual confirmation when it
does.
*(`CheckDone`, `Delta`, `CongratsButton`, `Done.IsEnabled`)*

### P3-RECON-5 — Be caught out by a sign error rather than hunting for it
If the balance is right but entered with the wrong sign, the user is offered a one-click
fix instead of being left with a mysterious doubled discrepancy.
*(`CheckDone`'s `YourNewBalance == -NewBalance` branch, `ValueSign` button)*

### P3-RECON-6 — Add the month's interest without leaving the panel
Typing the interest earned creates the matching transaction in the account (or updates
the one already there), and clearing it back to zero removes the one the panel added.
*(`TextBoxInterestEarned_LostFocus`, `FindInterestTransaction`, `interestCategory`)*

### P3-RECON-7 — Go back and fix an earlier statement
The user can select a previous statement date and see exactly the entries reconciled in
it, so a mistake in an old reconciliation can be found and corrected — including
swapping out a reconciled entry for another of the same amount, which is otherwise
blocked.
*(`ComboBoxPreviousReconcileDates`, `ShowReconciledState`, `IsLatestStatement`)*

### P3-RECON-8 — Keep the statement document with the statement
The user can point the panel at the statement file they balanced against, browse for
it, and have it saved with the account so any entry from that statement can open it
later.
*(`StatementFileName`, `OnBrowseStatement`, `StatementManager.AddStatement`/
`UpdateStatement`, `SetHasStatement`; see P3-REG-22)*

### P3-RECON-9 — Back out of balancing without damage
Cancelling — by button or by pressing Escape in either of the panel's main fields —
puts every entry touched during the session back to the status it had, and the register
back to the view the user was in before.
*(`Cancel_Click`, `ChildKeyDown`, `OnEndReconcile`, `SetReconciledState(cancelled)`,
`beforeState`)*

### P3-RECON-10 — Understand each field as they reach it
Moving through the panel shows an explanation of the field the user is on, and each
explanation can also be pinned by clicking its information button.
*(`TooltipTracker`, `TextBlockMessage`, the three `*Help_Click` handlers)*

---

## 3.10 The accounts panel

### P3-ACCT-1 — See all their accounts grouped by what kind they are
Accounts are listed under headings — everyday banking, credit, brokerage, retirement,
assets, loans — each heading carrying that group's total, with the user's overall net
worth on the panel's own header.
*(`AccountsControl.Rebind`, `BundleAccount`, `AccountSectionHeader`,
`IContainerStatus.SetTextBlock`; the header figure itself is P2-NAV-4)*

### P3-ACCT-2 — Spot the accounts that need attention
An account with entries awaiting review is shown in bold and in a distinct colour, and
an account that hasn't been balanced for longer than the user allowed carries a warning
whose explanation says how many months it's been.
*(`AccountItemViewModel.FontWeight`/`NameForeground`, `WarningIconVisibility`,
`WarningIconTooltip`, `Account.ReconcileWarning`)*

### P3-ACCT-3 — See a foreign-currency account for what it is
An account held in another currency shows that currency's flag and code beside it, and
its balance in the conventions of that currency — and the whole display can be switched
on or off from preferences.
*(`CountryFlag`, `CurrencyNormalized`, `ShowCurrency`, `DatabaseSettings.ShowCurrency`)*

### P3-ACCT-4 — Show or hide accounts they've closed
Closed accounts are hidden by default and can be brought back into the list; the menu
item says which way it will go. Selecting a closed account from elsewhere makes them
visible automatically so the selection isn't lost.
*(`DisplayClosedAccounts`, `CommandToggleClosedAccounts`, `UpdateContextMenuView`,
`SelectedAccount` setter)*

### P3-ACCT-5 — Set up and maintain accounts from the list
From the list the user opens an account's settings, creates a new account, creates a
loan, or sets up a new download connection.
*(`CommandFileImport` ("Properties"), `CommandNewAccount`, `CommandAddNewLoanAccount`,
`CommandDownloadAccounts`; the dialogs are Phase 4)*

### P3-ACCT-6 — Delete an account and land somewhere sensible
Deleting asks for confirmation, warns that it can't be undone, and then selects the
neighbouring account so the user isn't left staring at an empty screen.
*(`DeleteAccount`, `RaiseSelectionEvent(force: true)`)*

### P3-ACCT-7 — Start balancing an account from the list
Balancing is one keystroke or one menu item away from the account itself.
*(`CommandBalance` (Ctrl+B), `BalanceAccount` event → `MainWindow.BalanceAccount`)*

### P3-ACCT-8 — Fetch an account's statements from the list
Accounts that are linked to an institution can be synchronised from here; accounts that
aren't don't offer it.
*(`CommandSynchronize`, `CanSynchronizeAccount` requiring `OnlineAccount`; the download
itself is Phase 8)*

### P3-ACCT-9 — See what moved into an account from elsewhere
The user can list every transfer whose other side lands in the selected account.
*(`CommandViewTransfers` → `ShowTransfers` → `TransactionsView.ViewTransfers`)*

### P3-ACCT-10 — Take an account, or the whole list, out of the product
One account can be written out with its entries — including in a tax-software format
for capital gains — and the list of accounts itself can be written out as a
spreadsheet and opened.
*(`CommandExportAccount`, `Export`, `TxfExporter.ExportCapitalGains`,
`CommandExportList`, `ExportList`; formats are Phase 6/7)*

### P3-ACCT-11 — Bring a spreadsheet into one account and teach it the columns
The user can import a spreadsheet into the selected account, is shown the progress, and
can review or change the mapping from that file's columns to the product's fields; the
mapping is remembered per account. A file that names accounts of its own is rejected
with an explanation pointing at the general import instead.
*(`CommandImportCsv`, `ImportCsv`, `CommandEditCsvMapping`, `CsvMap` saved per account;
the importer is Phase 6)*

### P3-ACCT-12 — Copy an account and paste it back
An account can be copied to the clipboard and pasted in, with a plain message if the
clipboard doesn't hold one.
*(`IClipboardClient` on `AccountsControl`, `Account.Serialize`,
`XmlImporter.ImportAccount`)*

### P3-ACCT-13 — Get back to the whole account by clicking it again
Clicking the account the user is already on clears any filtering or custom list they'd
arrived at from a report, returning them to the plain register.
*(`listBox1_PreviewMouseDown` → delayed `OnShowAllTransactions` →
`RaiseSelectionEvent(force: true)`)*

### P3-ACCT-14 — Get from a group of investment accounts to the report about them
Clicking the brokerage or retirement heading opens the investment report for them.
*(`AccountSectionHeader.Clicked` → `AppCommands.CommandReportInvestment`; the report is
Phase 5)*

---

## 3.11 The categories panel

### P3-CAT-1 — Browse categories as a tree, grouped by what kind of flow they are
Categories appear beneath four headings — income, expense, investments and
unclassified — each expandable into the full hierarchy the user built.
*(`CategoriesControl.UpdateRoots`, `CategoryGroup`, `CategoryBalance`)*

### P3-CAT-2 — Find a category by typing, without losing where it sits
Typing narrows the tree to the branches that contain a match, keeping the parents so
the user can still see where the matching category lives.
*(`Filter`, `GetDeepFilteredRootCategories`, `AddRootIfOneOrMoreChildMatchFilder`)*

### P3-CAT-3 — Rename a category in place
The user renames a category by editing its name directly in the tree; Enter keeps the
change, Escape abandons it, clicking away keeps it, and a name that would collide with
an existing sibling is refused with an explanation.
*(F2 / `CommandRenameCategory`, `Category.IsEditing` swapping the template,
`OnRenameNode_CommitAndStopEditing`)*

### P3-CAT-4 — Re-home a category by dragging it
Dragging a category onto another makes it a subcategory of that one, and dragging it
out to the top level promotes it — with everything filed under it following.
*(`DragAndDrop` with `DragDropEffects.Move` → `MoveCategory` → `Merge`)*

### P3-CAT-5 — Fold one category into another
The user merges a category into another either by dragging with the copy gesture or by
choosing merge from the menu; every transaction and itemised line filed under the old
one is moved and the old one disappears.
*(`CommandMergeCategory`, `OnDragDropSourceOnTarget` copy branch, `Merge` →
`Transaction.ReCategorize`; the picker dialog is Phase 4)*

### P3-CAT-6 — Delete a category, and be made to say where its history goes
Deleting a category that nothing uses simply removes it. Deleting one that still has
transactions doesn't silently orphan them — the user is made to choose a category to
move them to first.
*(`CategoriesControl.Delete`, `GetTransactionsByCategory` count check,
`MergeCategoryDialog`)*

### P3-CAT-7 — Add a category, or edit what one means
New categories are created from the tree, pre-filled as a child of whatever was
selected, and any category's full settings can be opened from here.
*(`CommandAddCategory`, `CommandCategoryProperties` → `CategoryDialog`; the dialog is
Phase 4)*

### P3-CAT-8 — Open out or fold up the whole tree
The user expands or collapses everything at once rather than clicking through levels.
*(`CommandExpandAll`, `CommandCollapseAll`, `ExpandAll`)*

### P3-CAT-9 — See each category's colour where they manage them
Every category shows the colour it uses in charts, inherited from its parent unless it
has one of its own — and that colour travels with the drag image while re-homing.
*(`InheritedColor` + `CategoryToBrush`, `CreateDragVisual`)*

### P3-CAT-10 — Copy a category out
A category can be copied to the clipboard.
*(`CategoriesControl.Copy` → `Category.Serialize`)*

---

## 3.12 The payees panel

### P3-PAYEE-1 — Browse and search everyone they deal with
Payees are listed alphabetically and narrowed by typing; those with entries still
awaiting review or still unclassified are shown in bold.
*(`PayeesControl.GetAllPayees`, `Filter`, `NonzeroToFontBoldConverter` on `Payee.Flags`)*

### P3-PAYEE-2 — Rename a party from the list
Renaming opens the rename flow for that party, and if nothing refers to the old name
afterwards it disappears from the list immediately.
*(`OnMenuItem_Rename` → `Rename` → `RenamePayeeDialog`, `GetUsedPayees` cleanup; the
dialog is Phase 4)*

### P3-PAYEE-3 — Fold two parties together by dragging
Dragging one party onto another opens the rename flow pre-filled to merge the first
into the second.
*(`DragAndDrop` → `OnDropSourceOnTarget` → `Rename(from, to)`)*

### P3-PAYEE-4 — Delete a party, safely
Deleting a party nothing uses removes it; deleting one still in use redirects to the
rename flow so its history is re-pointed rather than orphaned.
*(`DeletePayee`)*

### P3-PAYEE-5 — Copy and paste parties
A party can be cut or copied out and pasted back, and pasting a name that already
exists offers to select the existing one instead of creating a second.
*(`IClipboardClient` on `PayeesControl`, `Payee.Serialize`/`Deserialize`)*

### P3-PAYEE-6 — Use the mouse's own back and forward buttons
The side buttons on a mouse move back and forward through the user's navigation while
they're in this panel.
*(`OnMouseUp` → `MouseButtonBackwardChanged`/`MouseButtonForwardChanged`; the history
itself is P2-HIST-1)*

---

## 3.13 The holdings panel and the holdings list

### P3-SEC-1 — Browse and search holdings from the navigation
The holdings panel lists each holding with its ticker beside it, narrowed by typing
against either, with the full identifiers on hover.
*(`SecuritiesControl`, `Securities.GetSecuritiesAsList(filter)`)*

### P3-SEC-2 — Fold two holdings together by dragging
Dragging one holding onto another asks for confirmation and then moves every trade from
the first to the second and removes the first.
*(`SecuritiesControl.OnDropSourceOnTarget` → `Rename` → `MyMoney.SwitchSecurities`)*

### P3-SEC-3 — Delete a holding, unless it's in use
Pressing Delete removes a holding nothing refers to, and tells the user how many trades
are in the way when it can't.
*(`CommandDeleteSecurity` (Del), `DeleteSecurity`)*

### P3-SEC-4 — Maintain the full details of every holding in one table
A dedicated list shows each holding's name, ticker, identifier, kind, whether its income
is taxable, its current and previous price, the change between them and the date of the
price — all editable in place and sortable by any of them.
*(`SecuritiesView`, `SecuritiesDataGrid`, `SecurityTypes`, `TaxableTypes`)*

### P3-SEC-5 — Narrow to what they still hold
A toggle switches between every holding ever traded and just the ones with a position
still open.
*(`ViewAllSecurities`, `ToggleShowAllSecurities`, `MyMoney.GetOwnedSecurities`)*

### P3-SEC-6 — Record a holding's stock splits inline
Each holding opens out into its own small grid of splits — date, and the ratio as two
numbers — added and edited in place, with empty rows cleaned up on close.
*(`StockSplitDetailView`, `myTemplateSplitButton`, `OnButtonSplitClicked`,
`RestoreSplitViewMode` → `RemoveEmptySplits`)*

### P3-SEC-7 — See every holding's splits at once
A toggle shows a compact read-only summary of each holding's splits inline for the
whole list.
*(`ViewAllSplits`, `CommandToggleAllSplits` (Alt+T), `StockSplitMiniView`)*

### P3-SEC-8 — Be stopped from entering a ticker that can't work
A ticker containing spaces is refused with an explanation and the cell stays in edit.
*(`OnDataGridCellEditEnding` → `ValidateSymbol`)*

### P3-SEC-9 — Fetch prices and splits for a holding, or for all of them
The user can ask for one holding's price history, or for the stock splits of every
equity they hold, from this list.
*(`CommandUpdateHistory` (F5), `CommandUpdateStockSplits`, `StockQuoteManager`; the
fetching is Phase 8)*

### P3-SEC-10 — Jump from a holding to its trades
One keystroke takes the user from a holding to every transaction involving it.
*(`CommandShowRelatedTransactions` (F12) → `SecurityNavigated` →
`IViewNavigator.ViewTransactionsBySecurity`)*

### P3-SEC-11 — Be warned before deleting a holding that's in use
Removing a row whose holding still has trades asks whether to go and look at them
instead of deleting.
*(`SecurityCollection.RemoveItem`)*

### P3-SEC-12 — Come back to the same holding, filter and sort
The selected holding, the held-only toggle, the show-all-splits toggle and the sorted
column are remembered across sessions.
*(`SecuritiesViewState`)*

---

## 3.14 Rental property

### P3-RENT-1 — Browse properties, years and cost classes as one tree
Each property shows its total profit; opening it shows each year with that year's
profit, income and expense; opening a year shows each class of cost with its total.
*(`RentsControl` hierarchical templates over `RentBuilding` → `RentalBuildingSingleYear`
→ `RentalBuildingSingleYearSingleDepartment`)*

### P3-RENT-2 — Drill from a number to the transactions behind it
Selecting a cost class for a year lists exactly the transactions that make it up.
*(`SelectionChanged` → `TransactionsView.ViewTransactionRentalBuildingSingleYearDepartment`,
`TransactionRentalSelector`)*

### P3-RENT-3 — Add, edit and remove properties and years
A new property is created and its details filled in before it's added; an existing
property's details can be reopened; a property or a single year can be deleted after
confirmation; and the tree can be rebuilt on demand.
*(`OnMenuNewRental_Click`, `OnMenuItem_Edit` → `RentalDialog`, `OnMenuItem_Delete`,
`OnMenuRefresh_Click`; the dialog is Phase 4)*

### P3-RENT-4 — Read a property's profitability at a glance
A summary screen shows a property's — or one year's — income, each class of expense,
the resulting profit, and each owner's share of that profit by their percentage.
*(`RentSummaryView`, `SetViewToRentBuilding`, `SetViewToRentalBuildingSingleYear`)*

---

## 3.15 Rename rules

### P3-ALIAS-1 — See and edit every rename rule in one table
The rules are listed as pattern, kind of match and the party they rename to, each
editable in place, sortable, and with the party picked from a filtered list of existing
parties or typed in to create a new one.
*(`AliasesView`, `AliasDataGrid`, `AliasTypes`, `PayeeList`,
`ComboBoxForPayee_PreviewLostKeyboardFocus`)*

### P3-ALIAS-2 — Have redundant rules cleaned up as they go
Finishing a rule that makes narrower existing rules pointless removes those narrower
ones rather than leaving overlapping rules behind.
*(`AliasDataGrid_RowEditEnding` → delayed `FindConflicts` → `MyMoney.FindSubsumedAliases`)*

### P3-ALIAS-3 — See which rules are on their way out
A rule marked for deletion is shown struck through with an explanation, until the work
is saved.
*(`StrikeThroughConverter`, `DeletedAliasToolTipConverter`, `GetAliases(true)`)*

### P3-ALIAS-4 — Search the rules
Typing narrows the list against the pattern, the kind of match and the party.
*(`AliasCollection.IsMatch`, `QuickFilterControl`)*

---

## 3.16 Currencies

### P3-CUR-1 — Maintain the currencies they deal in as a table
Each currency is a row with its three-letter code, full name, regional formatting, its
current rate and the previous one, all editable in place and sortable.
*(`CurrenciesView`, `CurrenciesDataGrid`, `CurrencyCollection`)*

### P3-CUR-2 — Pick a real currency rather than typing one
Code, name and regional formatting are each chosen from a list of real world currencies
showing the symbol, the name, the country's flag and the locale code, searchable by any
of them.
*(`CulturePickerComboStyle`, `CultureHelpers.CurrencyCultures`, `CulturePickerConverter`)*

### P3-CUR-3 — Have the rest of the row filled in from the code
Once a currency code is entered, the name and regional formatting lists narrow to what
actually matches it and are pre-filled with the obvious answer.
*(`ComboBoxForName_GotFocus`, `ComboBoxForCultureCode_GotFocus` using the uncommitted
symbol)*

### P3-CUR-4 — Get the current exchange rate without looking it up
Entering a currency code fetches that currency's current rate and fills it in.
*(`ComboBoxForSymbol_LostFocus` → `ExchangeRateService.CreateOrUpdate`; the service is
Phase 8)*

---

## 3.17 A loan's payment schedule

### P3-LOAN-1 — See a loan as a schedule of payments
A loan account is shown as its payments in date order, each with where it came from,
the payment, the principal, the interest, the implied rate and the balance still owed
afterwards. Amounts that are zero are dimmed so the eye goes to what matters.
*(`LoansView`, `Loan.Payments`, `LoanPaymentAggregation`, `ZeroToOpacityConverter`)*

### P3-LOAN-2 — Add a payment that isn't in any account
The user adds rows by hand for payments made outside the accounts they track; a new row
is dated a month after the one before it.
*(`TheDataGrid_InitializingNewItem`, `LoanPayementManualEntry`)*

### P3-LOAN-3 — Edit one part of a payment and have the rest follow
Changing the principal works out the interest, changing the interest works out the
principal, and changing the rate re-derives both — for that payment and every one after
it. The balance owed is then recalculated down the whole schedule.
*(`TheDataGrid_RowEditEnding` → `Rebalance`, `Loan.Rebalance`)*

### P3-LOAN-4 — Jump to the transaction a payment came from
For a payment derived from a real transaction, one keystroke takes the user to it in
its own account.
*(`CommandGotoRelatedTransaction` (F12), `IViewNavigator.NavigateToTransaction`)*

### P3-LOAN-5 — Delete only the rows that are really theirs to delete
Deleting a hand-entered payment asks for confirmation; rows derived from real
transactions can't be deleted from here.
*(`TheDataGrid_PreviewExecuted`, `TheDataGrid_KeyDown`, `LoanPaymentAggregation.IsReadOnly`)*

### P3-LOAN-6 — Take the schedule out of the product
The whole schedule can be written out to a file.
*(`CommandViewExport` → `Exporters.ExportPrompt`; formats are Phase 6)*

### P3-LOAN-7 — Know how many payments they're looking at
The status line reports the number of payments in the schedule.
*(`CreateStatusText`)*

---

## 3.18 Task panels beside the working area

### P3-PANEL-1 — Adjust a report's terms beside the report itself
A report can put its own controls in the navigation panel — the date, a start and end
date, a financial year, the currency to express everything in, the kind of report and
the interval to group by — and the report redraws as each is changed. Only the controls
that apply to the current report are shown.
*(`ReportsControl`, its `Show*`/`Hide*` methods and per-control change events; which
report uses which is Phase 5)*

### P3-PANEL-2 — Take a report out as a spreadsheet
Where a report supports it, a button beside it writes it out for use in a spreadsheet.
*(`ExportButton`, `ReportExport` event; format is Phase 6)*

### P3-PANEL-3 — Enter the assumptions behind a retirement plan
Beside the retirement projection the user enters their age and their spouse's, the age
they plan to retire and the age to plan to, their tax filing status and state,
inflation, how tax brackets move, the return they expect, the income they want, how
they intend to draw down tax-deferred savings and over how long, and what social
security they and their spouse expect and from what age — and can switch the projection
between stacked and side-by-side bars.
*(`RetirementControl` and its per-field committed events; the projection is Phase 5)*

### P3-PANEL-4 — Not be asked questions that don't apply to them
A user filing singly isn't asked about a spouse, and the detail of a draw-down strategy
only appears once a strategy other than "none" is chosen.
*(`RetirementControl.UpdateVisibility`)*

---

## 3.19 What every working surface does the same way

### P3-VIEW-1 — Always know which surface they're on
Each working surface names itself, and the window's title says which one is showing.
*(`IView.Caption`; the window title is P2-WORK-4 — but see Open Question 3)*

### P3-VIEW-2 — Return to a surface as they left it
Each surface remembers its own settings — which row was selected, how it was sorted,
which toggles were on — and restores them when the user comes back, within the session
and across restarts.
*(`IView.ViewState`/`DeserializeViewState`, `TransactionViewState`,
`SecuritiesViewState`, `CurrenciesViewState`, `ViewStateForLoan` — but see Open
Question 2)*

### P3-VIEW-3 — Reach a surface's own actions by right-click or by keyboard
Everything a surface can do is on its own context menu, and the ones used most carry a
keyboard shortcut shown next to them.
*(each view's `ContextMenu` + `CommandBindings` + `InputBindings`)*

### P3-VIEW-4 — Get help about the surface they're on, not the product in general
Help is tied to the surface and even to what's selected within it — a credit card
account, a bank account, an investment account, an asset, a loan, the splits grid, the
portfolio — so the page that opens is about what the user is actually doing.
*(`HelpService.HelpKeyword` on each view, `SwitchLayout`'s per-layout keyword,
`AccountsControl.SetHelpKeywordForSelectedItem`)*

### P3-VIEW-5 — Not lose a half-finished edit by navigating away
Leaving a surface, switching what it shows or running a command commits whatever was
being edited first.
*(`IView.Commit`, the `Commit()` calls at the head of every `View*` method and
`SwitchLayout`)*

### P3-VIEW-6 — Have lists keep up with change without losing their place
When data changes underneath a list — a download arriving, a category renamed, an
account deleted — the list catches up, keeping the user's selection and scroll position
and coalescing a storm of changes into one update rather than redrawing repeatedly.
*(`OnMoneyChanged` → `pendingUpdates` → delayed `HandleChanges`, `InvalidateDisplay`,
`DelayedActions` in every panel)*

### P3-VIEW-7 — Search from any surface with the same keystroke
Every surface that can be searched puts the cursor in its own search box on the same
keystroke, and a surface that can't simply does nothing.
*(`IView.FocusQuickFilter`; Ctrl+F is P2-CMD-6 — but see Open Question 4)*

---

## Phase 3 coverage checklist

Every file and top-level type in `Views/` and `View Selectors/`, plus
`Controls/QueryViewControl.*` which Phase 2 deferred here. "Not user-facing" entries
are plumbing, performance workarounds or dead code the user never perceives.

### `Views/` — files

| File | Status |
|---|---|
| `TransactionsView.xaml` + `.xaml.cs` | P3-REG-*, P3-EDIT-*, P3-SPLIT-*, P3-XFER-*, P3-DUP-*, P3-ATT-*, P3-FIND-*, P3-INV-1…P3-INV-6 |
| `SecuritiesView.xaml` + `.xaml.cs` | P3-SEC-4…P3-SEC-12 |
| `CurrenciesView.xaml` + `.xaml.cs` | P3-CUR-1…P3-CUR-4 — see Open Questions 3 and 4 |
| `AliasesView.xaml` + `.xaml.cs` | P3-ALIAS-1…P3-ALIAS-4 |
| `LoansView.xaml` + `.xaml.cs` | P3-LOAN-1…P3-LOAN-7 |
| `RentSummaryView.xaml` + `.xaml.cs` | P3-RENT-4 |
| `RentPayementsView.xaml` + `.xaml.cs` (class `RentInputControl`) | **Not user-facing** — never instantiated anywhere in the product; see Open Question 1 |
| `FlowDocumentView.xaml` + `.xaml.cs` | P3-INV-4, P3-INV-5 as the portfolio host; otherwise the shell for every report (Phase 5). Its own affordances — copy, select all, find next, export HTML, expand/collapse detail, close — are P3-VIEW-3/P3-VIEW-7 and Phase 5 |
| `TransactionSelectors.cs` | P3-FIND-5, P3-FIND-6 — the composable narrowings |
| `GraphGenerators.cs` | **Not user-facing on its own** — computes the series behind the charts of P2-PANE-1; what is plotted is Phase 5 |
| `ChangeTracker.cs` | P2-FILE-5 (already Phase 2); the `Changed*`/`Deleted*`/`Inserted*` accessors are **dead code** — see Open Question 6 |
| `FindManager.cs` | P3-VIEW-7 — the text search inside a report document |
| `IView.cs` (`IView`, `IViewNavigator`, `AfterViewStateChangedEventArgs`) | P3-VIEW-1, P3-VIEW-2, P3-VIEW-5, P3-VIEW-7; `IViewNavigator` is how a surface asks the shell to go elsewhere (P2-NAV-5) |
| `ViewState.cs` | **Not user-facing** — an empty base class each view subclasses |
| `TransactionPropertyChangeSubscription.cs` | **Not user-facing** — shared subscribe/unsubscribe bookkeeping so the hand-written cells update on the interface thread |

### `TransactionsView.xaml.cs` — the types it defines

| Type | Status |
|---|---|
| `TransactionsView` | the surface itself; see the groups above |
| `TransactionFilter` (enum) | P3-FIND-3 |
| `TransactionViewName` (enum) | P3-REG-2, P3-FIND-6 — which kind of list is showing |
| `TransactionSelection` (enum) | **Not user-facing** — which row to land on after a list is rebuilt (first/last/same/specific); its *effect* is P3-FIND-6 |
| `TransactionViewState` | P3-FIND-6, P3-VIEW-2 |
| `TransactionCollection` | P3-REG-1, P3-REG-13, P3-FIND-1 — the live, filtered, editable list |
| `TypeToFind` / `TransactionTypeToFind` | P3-REG-7 |
| `TransactionCell` | P3-REG-5 — row appearance; a hand-written replacement for a template that was too slow |
| `TransactionAttachmentIcon`, `TransactionAttachmentColumn` | P3-ATT-1, P3-ATT-4 |
| `TransactionNumberColumn`, `TransactionDateColumn`, `TransactionNumericColumn` | P3-REG-2, P3-REG-6, P3-EDIT-10 |
| `TransactionPayeeCategoryMemoColumn` / `...Field` | P3-REG-3, P3-EDIT-1 |
| `TransactionCategoryColorColumn` | P3-REG-5 |
| `TransactionStatusColumn` / `TransactionStatusButton` | P3-REG-10 |
| `TransactionAmountColumn` / `TransactionAmountControl` | P3-EDIT-3, P3-SPLIT-2, P3-EDIT-10 |
| `TransactionTextField` | **Not user-facing** — the read-only label behind each editable field; a performance workaround |
| `TransactionAnchorColumn` / `TransactionAnchor` | **Not user-facing on its own** — the invisible spot the duplicate bracket attaches to (P3-DUP-1) |
| `TransactionConnector` / `TransactionConnectorAdorner` | P3-DUP-1, P3-DUP-2, P3-DUP-3 |
| `SymbolIconHackery` | **Not user-facing** — reaches a non-public font size on an icon control |
| `TransactionFilterToBooleanConverter` | **Not user-facing** — declared but referenced by no XAML in the product |
| `ValidationErrorGetErrorMessageConverter` | P3-EDIT-10 |
| `ShowSplitForDebit` / `ShowSplitForCredit` | **Not user-facing** — superseded by `TransactionAmountControl.UpdateButton`; neither converter is referenced by any XAML |
| `OnCommandViewSimilarTransactions` / `CommandViewSimilarTransactions` | **Not user-facing** — the handler is empty and its menu item is commented out; see Open Question 5 |
| `CanExecute_Budgeted`, `CanExecute_AllTransactions`, `CanExecute_UnacceptedTransactions`, `CanExecute_UnreconciledTransactions`, `CanExecute_BudgetedTransactions`, `CanExecute_UncategorizedTransactions`, `CanExecute_DeleteThisTransaction` | **Not user-facing** — declared but bound to no command; leftovers from a previous menu structure |
| `OnImageAnimationComplete`, `OnLostFocus`, `OnLostKeyboardFocus` | **Not user-facing** — empty overrides/handlers |

### `TransactionSelectors.cs`

| Type | Status |
|---|---|
| `TransactionSelectorContext` | **Not user-facing** — carries the balancing state a narrowing needs |
| `TransactionSelector` (base) | P3-FIND-5 |
| `TransactionAccountSelector` | P3-REG-1, P3-REG-23 |
| `TransactionPayeeSelector` | P3-REG-23 |
| `TransactionCategorySelector` | P3-REG-23, P3-FIND-5 |
| `TransactionSecuritySelector` | P3-INV-3, P3-SEC-10 |
| `TransactionRentalSelector` | P3-RENT-2 |
| `TransactionRangeSelector` | P3-FIND-5 (the history-chart drill-down of P2-PANE-2) |
| `TransactionQuerySelector` | P3-FIND-4 |
| `TransactionFilterSelector` | P3-FIND-3, P3-RECON-1 |
| `TransactionFixedSelector` | P3-XFER-6, P3-DUP-*, and the report drill-downs that can't be described any other way |

### `View Selectors/` — panels

| File | Status |
|---|---|
| `AccountsControl.xaml` + `.xaml.cs` | P3-ACCT-1…P3-ACCT-14 — but see Open Questions 7 and 8 |
| `AccountViewModel` / `AccountItemViewModel` / `AccountSectionHeader` | P3-ACCT-1, P3-ACCT-2, P3-ACCT-3, P3-ACCT-14 |
| `CategoriesControl.xaml` + `.xaml.cs` | P3-CAT-1…P3-CAT-10 |
| `CategoryGroup` / `CategoryBalance` | P3-CAT-1 — but `CategoryBalance.Balance` is computed and never displayed; see Open Question 9 |
| `PayeesControl.xaml` + `.xaml.cs` | P3-PAYEE-1…P3-PAYEE-6 |
| `SecuritiesControl.xaml` + `.xaml.cs` | P3-SEC-1, P3-SEC-2, P3-SEC-3 |
| `RentsControl.xaml` + `.xaml.cs` | P3-RENT-1, P3-RENT-2, P3-RENT-3 |
| `BalanceControl.xaml` + `.xaml.cs` | P3-RECON-1…P3-RECON-10 |
| `BalanceEventArgs` | **Not user-facing** — tells the shell whether balancing finished and whether a statement was filed |
| `ReportsControl.xaml` + `.xaml.cs` | P3-PANEL-1, P3-PANEL-2 |
| `RetirementControl.xaml` + `.xaml.cs` | P3-PANEL-3, P3-PANEL-4 |

### Panel members that are not user-facing

| Member | Reason |
|---|---|
| `AccountsControl.OnListBoxMouseDoubleClick` | Double-click-to-open-properties is **broken**: the hit-test result is discarded and the local it tests is always null, so the branch never runs. See Open Question 7 |
| `AccountsControl.OnAddNewAccount` / `OnAddNewLoanAccount` "select the new account" step | **Broken**: assigns an `Account` to a list whose items are view models, so it selects nothing. See Open Question 8 |
| `PayeesControl.OnBalanceChanged`, `SecuritiesControl.OnBalanceChanged` | Empty handlers marked "TODO" |
| `SecuritiesControl.OnMenuItem_Rename`, `SecuritiesControl.Paste`, `SecuritiesControl.GetAllSecurities()` (no-arg) | Dead: the rename menu item is commented out in the XAML, paste is entirely commented out, and the overload is never called |
| `CategoriesControl.menuItemRename_Click`, `ReverseTransfer`, `OnSelectedTransactionChanged` / `SelectedTransactionChanged` / `SelectedTransactions` | Dead: the rename handler was replaced by a command, `ReverseTransfer` is never called, and the transaction-selection event is never raised so nothing can consume it |
| `CategoriesControl.Cut` / `Paste` | Deliberately empty — copy works, cut and paste don't |
| `RentsControl.OnSelectionChanged`, `Cut`/`Copy`/`Delete`/`Paste` | Empty bodies marked "To Do"; clipboard is advertised as available but does nothing |
| `RentsControl.Selected` setter | Records the value but the line that would actually select it is commented out |
| `BalanceControl.TextBoxStatementBalance_KeyDown` | Empty handler |
| `CurrenciesView.OnMoneyChanged`, `TearDownGrid`, `FindDataGridContainingFocus`, `SelectedRowId` | Empty or never called |
| `SecuritiesView.FindDataGridContainingFocus` used via `IsKeyboardFocusInsideSplitsDataGrid` | Infrastructure — keeps the outer grid's keys from firing while editing a split |
| `TransactionsView.SetupContextMenuBinding` | Declared, never called |

### `Controls/QueryViewControl` (deferred here by Phase 2)

| Member | Status |
|---|---|
| The four-column grid (And/Or, Field, Operation, Value) with add and delete rows | P3-FIND-4 |
| `ListOfFields` | P3-FIND-4 — account, party, category, note, reference, date, payment, deposit, sales tax, status, accepted and budgeted. See Open Question 10 |
| `ListOfOperations` | P3-FIND-4 — contains, equals, greater/less than (and or-equal), not contains, not equals, and a pattern match |
| `OnDataGrid1_CurrentCellChanged` | P3-FIND-4 — one click starts editing a cell rather than two |
| `GetQuery` | P3-FIND-4 — incomplete rows are ignored rather than rejected |
| `Copy` / `Paste` | P3-FIND-4 — conditions can be copied between searches |
| `Cut` | **Broken** — removes the selected rows but always writes an *empty* query to the clipboard. See Open Question 11 |
| `ReadXml` / `WriteXml` | **Not user-facing** — the interface is implemented with empty bodies; the query is actually persisted through `TransactionViewState` |
| `GetQueryRow(DataRow)` | **Not user-facing** — never called |

---

## Open questions from Phase 3

Things a human should double-check, because the call was a judgement rather than
obvious from the code — and, where noted, because they look like real defects.

1. **A whole view exists that nothing can open.** `Views/RentPayementsView.xaml`
   (class `RentInputControl`) is a complete, working surface for entering rent
   received per unit per month, grouped by month with per-month totals — and it is
   never constructed anywhere in the product. Rental tracking otherwise has no way to
   record what each tenant actually paid. Treated as not user-facing and given no
   scenario. A human should decide whether this is a half-finished feature to
   complete or dead code to delete; if the redesign wants per-unit rent entry, the
   intent is already written down here.
2. **Most surfaces don't actually remember where the user was.** Only the transaction
   register and the holdings list implement view state properly. The loan schedule
   builds a state object but its `DeserializeViewState` returns a bare `ViewState`, so
   the loan account is never restored. The rename-rules, rental-summary and
   rent-input views return `null` from both. P3-VIEW-2 therefore describes the
   *intent*; in practice it only holds for two of the surfaces. Worth confirming
   before a redesign promises it everywhere.
3. **The currencies list calls itself "Securities".** `CurrenciesView.Caption` returns
   the string `"Securities"`, so the window title is wrong whenever the user is
   looking at currencies. Its `ViewState` is also written entirely in terms of
   `Security` — it saves `SelectedSecurity` and looks the row back up with
   `Securities.FindSecurity` — against a grid whose rows are `Currency` objects, so
   the selection is never restored. Both read as copy-paste from `SecuritiesView`.
   Flagged as defects rather than capabilities.
4. **The currencies list has no search box.** `CurrenciesView` implements a
   `QuickFilter` property and its row collection implements matching, but there is no
   `QuickFilterControl` in its XAML and `FocusQuickFilter()` is empty — so the
   plumbing exists and nothing sets it. P3-VIEW-7 says a surface that can't be
   searched does nothing, which is technically what happens; whether currencies
   *should* be searchable is a design decision. (`LoansView`, `RentSummaryView` and
   `RentInputControl` are in the same position.)
5. **"View similar transactions" is built but switched off.** The command, its
   can-execute handler and its binding all exist in `TransactionsView`; the execute
   handler is empty and the menu item is commented out. Given no scenario. If the
   redesign wants "show me everything like this", it is currently an empty shell, not
   a feature to resurface.
6. **`ChangeTracker`'s per-type accessors are dead and would crash if used.**
   `ChangedAccounts`, `DeletedTransactions`, `ChangedSplits` and the rest are public
   and referenced nowhere. Two of the three helpers they rely on are also wrong —
   `GetChanged<T>` and `GetDeleted<T>` iterate a `HashSet<object>` of domain objects
   as `foreach (ChangeList a in ...)`, which would throw an `InvalidCastException` on
   the first element. Only the summary UI (P2-FILE-5) and the change count are live.
   Flagged because anyone reusing this class in a redesign would hit it immediately.
7. **Double-clicking an account does nothing.** `AccountsControl.
   OnListBoxMouseDoubleClick` declares `object item = null`, calls
   `GetElementFromPoint(...)` and throws the result away, then tests `item`, which is
   always null — so the branch that opens the account's properties is unreachable.
   Properties is still reachable from the context menu (P3-ACCT-5), so the capability
   isn't lost, but the obvious gesture for it is broken.
8. **A newly created account isn't selected.** Both `OnAddNewAccount` and
   `OnAddNewLoanAccount` finish with `listBox1.SelectedItem = a` where `a` is an
   `Account`, but the list's items are `AccountViewModel` objects — so nothing is
   selected and the user has to find their new account themselves. P3-ACCT-5 describes
   only the creation.
9. **The categories tree computes a total it never shows.** `CategoryBalance` carries
   a `Balance` that `UpdateRoots`/`UpdateBalance` keep current, but the data template
   for it renders only the word "Total"; the same is true of each group heading, which
   shows no figure. P3-CAT-1 therefore doesn't claim the user sees category totals in
   the tree. Either the display was dropped or it was never finished — worth deciding,
   since the accounts panel does show group totals (P3-ACCT-1) and the asymmetry is
   visible to a user.
10. **The advanced search offers a "budgeted" condition that the model can't answer
    for a whole transaction.** `Field.Budgeted` is in the query builder's field list,
    but Phase 1 Open Question 5 established that only an itemised *line* carries
    budget participation. Captured under P3-FIND-4 as one of the offered fields
    without claiming it works; a human should check whether searching on it returns
    anything useful.
11. **Cutting rows out of a search copies nothing.** `QueryViewControl.Cut` removes the
    selected conditions but builds its clipboard payload from an `ArrayList` it never
    adds to, and guards that with `if (qrows.Count >= 0)` — always true — so cutting
    always overwrites the clipboard with an empty query. Copy works correctly. P3-FIND-4
    describes copy and paste only.
12. **The boundary with Phase 4 is "the door, not the room".** Fifteen or so modal
    flows are reachable from these surfaces — account, loan, online-account, category,
    merge-category, recategorize, rename-payee, rental, tax-report, pick-year and
    attachment windows, plus the native file pickers. Each is captured here only as
    the affordance that opens it. If Phase 4 finds a capability that only exists
    inside one of those windows, it belongs there, not here.
13. **The boundary with Phase 5 runs through `FlowDocumentView`.** That view is both
    the host of the investment portfolio *inside* the transaction register (so its
    expand/collapse and drill-down are P3-INV-4/P3-INV-5) and the host of every
    standalone report (Phase 5). Its own controls — text search within the document,
    copy, select-all, export as a web page, and the close box — are catalogued here
    only in passing under P3-VIEW-3 and P3-VIEW-7; if Phase 5 would rather own the
    report viewer's affordances as a whole, those are the ones to revisit.
14. **`GraphGenerators.cs` lives in `Views/` but plots nothing itself.** It computes
    the running-balance, brokerage-market-value and price-history series that the
    chart strip draws. Classified as not user-facing here on the basis that Phase 5
    owns what a chart says; flagging it because its location makes it easy to assume
    Phase 3 covered it.
15. **Duplicate detection is disabled while balancing.** The bracket of P3-DUP-1 is
    only offered when the user is *not* reconciling (`!this.IsReconciling`). That is
    probably deliberate — a statement legitimately contains repeated identical
    charges — but it means the moment a user is most likely to notice a duplicate is
    the moment the product stops pointing them out. Worth a deliberate decision rather
    than inheriting it.

---

# Phase 4: Dialogs

**Source examined (in full):** every file in `Source/WPF/MyMoney/Dialogs/` — 53 files,
29 classes, ~7,700 lines.

| File(s) | Lines |
|---|---|
| `AccountDialog.xaml` + `.xaml.cs` | 135 + 463 |
| `AddLoginDialog.xaml` + `.xaml.cs` | 32 + 111 |
| `AttachmentDialog.xaml` + `.xaml.cs` | 121 + 1,277 |
| `AuthTokenDialog.cs` | 54 |
| `BaseDialog.cs` | 12 |
| `CategoryDialog.xaml` + `.xaml.cs` | 109 + 365 |
| `ChangePasswordDialog.cs` | 131 |
| `CsvImportDialog.xaml` + `.xaml.cs` | 86 + 99 |
| `FreeStyleQueryDialog.xaml` + `.xaml.cs` | 46 + 61 |
| `LoanDialog.xaml` + `.xaml.cs` | 108 + 153 |
| `MergeCategoryDialog.xaml` + `.xaml.cs` | 48 + 64 |
| `MfaChallengeDialog.cs` | 74 |
| `MoneyFileImportDialog.xaml` + `.xaml.cs` | 54 + 437 |
| `NewSqlServerDatabaseDialog.xaml` + `.xaml.cs` | 22 + 36 |
| `NewSqliteDatabaseDialog.xaml` + `.xaml.cs` | 23 + 44 |
| `OfxLoginDialog.cs` | 80 |
| `OnlineAccountDialog.xaml` + `.xaml.cs` | 180 + 1,575 |
| `OnlineServiceDialog.xaml` + `.xaml.cs` | 83 + 236 |
| `OpenDatabaseDialog.xaml` + `.xaml.cs` | 13 + 37 |
| `PasswordWindow.xaml` + `.xaml.cs` | 62 + 248 |
| `PickDateDialog.xaml` + `.xaml.cs` | 33 + 56 |
| `RecategorizeDialog.xaml` + `.xaml.cs` | 55 + 63 |
| `RenamePayeeDialog.xaml` + `.xaml.cs` | 97 + 318 |
| `RentalDialog.xaml` + `.xaml.cs` | 150 + 72 |
| `ReportRangeDialog.xaml` + `.xaml.cs` | 87 + 115 |
| `SaCredentialDialog.xaml` + `.xaml.cs` | 14 + 23 |
| `SampleDatabaseOptions.xaml` + `.xaml.cs` | 77 + 123 |
| `SelectAccountDialog.xaml` + `.xaml.cs` | 55 + 112 |
| `TaxReportDialog.xaml` + `.xaml.cs` | 43 + 78 |

> **Path notes.** The "24 files in `Dialogs/`" figure in this repo's own docs is stale —
> there are **53** (29 `.cs` files declaring 29 classes, 24 of them with a `.xaml`
> partner). The number 24 happens to be the count of `.xaml` files, which may be where
> the stale figure came from.
> Two naming traps of the kind Phase 3 flagged:
> - **`PickDateDialog.xaml` / `PickDateDialog.xaml.cs` declare a class called
>   `PickYearDialog`**, whose window title is "Select Date" and whose content is a
>   full date picker, not a year picker. Neither the filename nor the class name
>   describes it; searching for either one alone misses it.
> - `MergeCategoryDialog.xaml.cs`, `LoanDialog.xaml.cs`, `RentalDialog.xaml.cs`,
>   `ReportRangeDialog.xaml.cs`, `RecategorizeDialog.xaml.cs` and
>   `AttachmentDialog.xaml.cs` all carry copy-pasted `<summary>Interaction logic for
>   …</summary>` comments naming a *different* dialog (`MoveMergeCategoryDialog`,
>   `AccountDialog` ×2, `RenamePayeeDialog`, `CategoryTransferDialog`, `ScanDialog`).
>   Doc comments are not a reliable index here.
>
> Four of the classes are **not** `Window`-derived dialogs in the usual sense:
> `BaseDialog` is the shared base, and `OfxLoginDialog`, `AuthTokenDialog`,
> `MfaChallengeDialog` and `ChangePasswordDialog` are code-only subclasses of
> `PasswordWindow` that reshape it rather than declaring their own XAML.

**What this phase covers and deliberately does not.** Phase 3 catalogued the *doors* —
"the user can open the account window from here". This section catalogues the *rooms*:
what each modal (or, in three cases, modeless) window lets the user create, edit,
confirm or choose, and what it validates or refuses. Where a dialog merely collects
options that a report or an importer then acts on, the *collecting* is here and the
*acting* is Phase 5 (reports/charts) or Phase 6 (import/export). Native Windows file
and folder pickers are used throughout and are not catalogued individually; they are
the platform's, not the product's.

---

## 4.1 Setting up and maintaining an account

### P4-ACCT-1 — Describe an account in one place
The user gives an account its name, the number the bank knows it by, a separate
identifier for downloads if the bank formats it differently, a free-text description,
what kind of account it is, its tax treatment, its opening balance, its currency, the
web site they visit for it, and how many months they'll allow between balancing it.
*(`AccountDialog`)*

### P4-ACCT-2 — Be stopped from naming an account something the product can't handle
Certain characters aren't allowed in an account name. Typing one turns the name field
red, explains why, and prevents the user from confirming. A blank name also blocks
confirmation.
*(`AccountDialog.OnNameChanged`, `Accounts.InvalidNameChars`, `CheckButtonStates`)*

### P4-ACCT-3 — Abandon account edits without consequence
Everything the user types is applied to a working copy of the account, and only
copied onto the real account when they confirm. Backing out leaves the account
exactly as it was.
*(`Account.ShallowCopy` into `editingAccount`, `HandleOk` copying field-by-field —
but see Open Question 2)*

### P4-ACCT-4 — Mark an account closed, or reopen it
Closing an account is a single toggle in its own window rather than a separate
command.
*(`AccountDialog` closed checkbox → `Account.IsClosed`)*

### P4-ACCT-5 — Jump to the bank's web site from the account
The user can open the site they've recorded for an account directly. If they haven't
recorded one, or what they typed isn't a usable address, they're told so rather than
being taken nowhere. A bare address without `http`/`https` is accepted and completed
for them.
*(`AccountDialog.OnButtonGoToWebSite` — the loan equivalent is broken; see Open
Question 3)*

### P4-ACCT-6 — Teach the product other names this account is known by
The user can record extra identifiers that downloads and imports use for this
account, add a new one by typing it and pressing Enter, and remove one from the list.
Typing an identifier already assigned to another account moves it to this one.
*(`AccountDialog` aliases combo, `AccountAliases.AddAlias`/`RemoveAlias`,
`AccountAlias` — but see Open Question 2)*

### P4-ACCT-7 — See what a foreign-currency account is worth in home terms
Choosing a currency other than the home one shows the current exchange rate beside
it, fetching it if the product doesn't already have it, and keeps that figure current
if rates change while the window is open.
*(`AccountDialog.OnCurrencyChanged`/`UpdateRateText`, `ExchangeRateService.CreateOrUpdate`)*

### P4-ACCT-8 — Have a currency created for them
If the user picks a currency the product has never seen, it's added to their list of
currencies with its proper name and regional formatting worked out from the code,
rather than failing or leaving a blank entry.
*(`AccountDialog.HandleOk`, `CultureHelpers.CurrencyCultures`)*

### P4-ACCT-9 — Attach the account to an institution without leaving the window
From the account's own window the user picks which stored institution connection this
account downloads from, clears it, or opens the institution window to set up a new
one — and the choice they make there comes back into the account window.
*(`AccountDialog.comboBoxOnlineAccount`, `OnButtonOnlineAccountDetails_Click`)*

### P4-ACCT-10 — Set up a loan account with its principal and interest categories
A loan is set up through its own window rather than the ordinary account one: name,
reference number, description, the two categories that represent principal and
interest, currency, web site and whether it's closed. The categories are chosen by
typing part of their full path.
*(`LoanDialog`, `Account.CategoryForPrincipal`/`CategoryForInterest` — but see Open
Questions 3 and 4)*

### P4-ACCT-11 — Choose which of their accounts an unrecognised one is
When something arriving from outside names an account the product doesn't recognise,
the user is shown their accounts — name, number and current balance, with closed ones
pushed to the bottom — and picks the right one, creates a new account there and then,
or abandons the whole import.
*(`SelectAccountDialog`, `AccountHelper.PickAccount`)*

### P4-ACCT-12 — Only have to answer that question once
Once the user has said which of their accounts a foreign identifier belongs to, that
mapping is remembered, so the same identifier is routed automatically next time
instead of asking again.
*(`AccountHelper.PickAccount` creating an `AccountAlias`)*

---

## 4.2 Connecting an account to a financial institution

### P4-ONLINE-1 — Find their bank in a list rather than typing its settings
The user picks their institution from a list of thousands that the product downloads
and caches, narrowing it by typing part of the name. Institutions they already have a
connection for are shown in bold at the top of that same list.
*(`OnlineAccountDialog`, `OfxInstitutionInfo.GetCachedBankList`/`GetRemoteBankList`)*

### P4-ONLINE-2 — Have the connection settings filled in for them
Choosing an institution fills in its identifiers, its download address, the protocol
version it wants, its logo and its web site, so the user doesn't have to find any of
that themselves. If they already have a connection to that institution, their stored
sign-in details come across too.
*(`OnlineAccountDialog.UpdateInstitutionInfo`)*

### P4-ONLINE-3 — Set up an institution the list doesn't know about
The user can type a name the list doesn't contain and fill in the technical details by
hand — institution name, identifier, bank/branch/broker identifiers, address, protocol
version, application identity. Only the fields that make sense for that kind of
account are shown.
*(`OnlineAccountDialog.ShowHideFieldsForAccountType`, `OnGotProfile` adding a new
entry — but see Open Question 5)*

### P4-ONLINE-4 — Test the connection before committing to it
The user clicks to connect, and the product talks to the institution and reports back
in the window: what it found, or the exact error, or the bank's own HTML error page
rendered as the bank wrote it. The connect action isn't offered until the minimum
details are present.
*(`OnButtonVerify` → `StartVerify` → `HandleProfileResponse`, `ShowError`,
`ShowHtmlError`, `UpdateButtonState`)*

### P4-ONLINE-5 — Check they're talking to the right organisation before handing over credentials
Before the password prompt appears, the user is shown the name, postal address, phone
number, email and logo the institution returned, and asked to confirm it's who they
think it is.
*(`HandleProfileResponse`, `GetAddressParagraph`)*

### P4-ONLINE-6 — Be told how to enrol if they don't have online banking yet
If the user isn't signed up for online banking, the window relays the institution's own
enrolment instructions, a link to its enrolment page, or its customer-service number —
whichever the institution supplied — rather than just failing to sign them in.
*(`HandleProfileResponse` enrolment branch)*

### P4-ONLINE-7 — Be told plainly when the bank wants security the product doesn't do
If the institution demands a security level the product doesn't implement, the user
gets a plain statement of that, plus a link to the raw exchange so it can be reported.
*(`HandleProfileResponse` security-level check)*

### P4-ONLINE-8 — See which of their accounts the institution offers, and decide about each
After signing in, the user gets the list of accounts the institution says they have,
each with a single toggle that cycles through connect / skip / disconnect. Nothing is
connected until they confirm the window.
*(`ShowResult`, `FindMatchingOnlineAccount`, `OnIconButtonClick`, `OnButtonOk`)*

### P4-ONLINE-9 — Resolve an account the institution knows about but the product doesn't
Where the institution reports an account number the user has no account for, they're
offered their existing accounts to match it to, or the chance to create a new account
for it — and can undo that choice before confirming.
*(`OnIconButtonClick` new-account branch → `AccountHelper.PickAccount`,
`PlaceHolder` undo)*

### P4-ONLINE-10 — Be told when they've filed an account under the wrong kind
If the institution says an account is a chequing account and the user has it as
savings, they're told which account and both opinions, and offered a one-click
correction.
*(`FindMatchingOnlineAccount` type mismatch, `OnIconButtonClick` warning branch)*

### P4-ONLINE-11 — Have a working institution remembered for next time
When a connection succeeds, whatever the user corrected — name, identifiers, protocol
version, address — is saved back against that institution along with the date it last
worked, so the next account they set up starts from settings that are known to work.
*(`OnGotProfile`, `OfxInstitutionInfo.SaveList`)*

### P4-ONLINE-12 — Rename an institution to something they recognise
The user can type over the institution's official name and the connection is stored
under their own wording.
*(`OnComboBoxNameChanged` null-provider allowance, `OnButtonOk` name copy-back)*

---

## 4.3 Proving who they are to an institution

### P4-AUTH-1 — Enter online-banking credentials in a window that explains itself
The credential prompt states which institution is asking, names the exact server the
details will be sent to and that it will be sent securely, and tells the user to back
out if that address isn't one they recognise.
*(`OfxLoginDialog`, `PasswordWindow`)*

### P4-AUTH-2 — Supply whatever extra credentials their bank asks for
Where an institution requires additional fields beyond user name and password, they
appear with the bank's own labels rather than as generic boxes, and the user's answers
are kept with that institution's connection.
*(`PasswordWindow.AddUserDefinedField`/`GetUserDefinedField`,
`OfxSignOnInfo.UserCredentialLabel1`/`2`)*

### P4-AUTH-3 — Answer the bank's extra identity questions
When an institution demands multi-factor authentication, the user is shown its
questions — in the bank's own wording where it supplied one, or the product's
translation of a known question code — with an explanation of why they're being asked.
A question the product can't translate is shown as an unknown question with its code,
so the user can ring the bank rather than being stuck.
*(`MfaChallengeDialog`, `GetPhrasePrompt`, `MfaPhrases.xml`)*

### P4-AUTH-4 — Supply a one-off authentication token
Where the institution requires a token, the user is told what a token is for and given
the institution's own link or phone number for obtaining one, and can enter it. If the
token was rejected, they're told that specifically.
*(`AuthTokenDialog` — but see Open Question 6)*

### P4-AUTH-5 — Change an online-banking password when the bank insists
If the institution requires a new password, the user enters and confirms one; it's
checked against the institution's own length rules before anything is sent. The
product then changes the password with the institution and tells the user not to close
the window until it hears back, disabling both buttons so a half-finished change can't
be abandoned at the dangerous moment. If the institution rejects it, the reason is
shown and they can try again.
*(`ChangePasswordDialog`, `DisableButtons`/`EnableButtons`)*

### P4-AUTH-6 — Retry rather than start over when sign-in fails
An invalid sign-in, an expired token or a demand for extra authentication re-prompts
for just that one thing and then continues where it left off, instead of throwing away
everything the user had entered.
*(`HandleSignOnErrors` continuation actions)*

### P4-AUTH-7 — See at a glance whether the password they typed is the right one
While typing a password the product already knows, the icon beside the box changes to
show matched, not-yet-matched or rejected, and an incorrect entry is called out in
words rather than only by refusing.
*(`PasswordWindow.OnPasswordChanged`, `PasswordFailure`, `RealPassword`)*

---

## 4.4 Market data and exchange-rate services

### P4-QUOTES-1 — Choose which service supplies prices and exchange rates
The user picks from the services the product knows how to talk to, sees the address
each one lives at, and can open that address to sign up.
*(`OnlineServiceDialog`, `IOnlineService.GetDefaultSettingsList`)*

### P4-QUOTES-2 — Enter an access key and be told immediately whether it works
Shortly after the user finishes typing an access key, the product tries it against the
service and reports "Service is working" or the exact failure, without the user having
to confirm and find out later.
*(`OnPasswordChanged` → `CheckApiKey`, `TestApiKeyAsync`)*

### P4-QUOTES-3 — Record what their subscription is allowed to do
The user records how many requests per minute, per day and per month their plan
permits, and whether it includes price history and stock splits, so the product stays
inside the limits they're paying for.
*(`OnlineServiceSettings.ApiRequestsPerMinuteLimit`/`PerDay`/`PerMonth`,
`HistoryEnabled`, `SplitHistoryEnabled`)*

### P4-QUOTES-4 — Turn a service off without deleting its settings
Clearing the access key disables the service while leaving everything else recorded;
there's a button that does exactly that.
*(`OnDisable`)*

### P4-QUOTES-5 — Be warned about having two services doing the same job
If the user enables more than one service of the same kind, they're told which two and
that it isn't recommended.
*(`CheckMultiple` — but see Open Question 7)*

---

## 4.5 Categories

### P4-CAT-1 — Create or edit a category and everything that goes with it
In one window the user names a category (typing a full path creates the levels above
it), says whether it's income, expense, savings or investment, how often it recurs,
what colour it gets in charts, which tax line it feeds, and what it's for.
*(`CategoryDialog`, `Categories.GetOrCreateCategory`)*

### P4-CAT-2 — Pick an existing category from an indented list
Existing categories are offered as an indented tree so the user can see where each one
sits, and they can type to narrow.
*(`CategoryDialog.GetListLabel`, `RefreshCategories`)*

### P4-CAT-3 — Have a change of kind cascade to the categories beneath
Changing a category from, say, expense to savings changes everything underneath it too,
so a branch doesn't end up half one thing and half another.
*(`PropagateCategoryTypeToChildren`)*

### P4-CAT-4 — Find the right tax line by searching either its form or its name
Rather than scrolling a long list of tax lines, the user types part of a form name or
part of a line's description and sees only the matches. An empty entry at the top lets
them clear the tax association entirely.
*(`ComboBoxForTaxCategory_FilterChanged`, `TaxCategoryCollection`)*

### P4-CAT-5 — Pick a colour from a picker rather than typing one
The colour is chosen visually, with the chosen colour shown on the button, and it's
registered so that everywhere else in the product that draws this category uses it.
*(`ColorPickerPanel`, `ColorAndBrushGenerator.SetNamedColor`)*

### P4-CAT-6 — See a category inherit its parent's colour
A category with no colour of its own opens showing the colour it inherits, so the user
can see what it will actually look like before deciding to override it.
*(`Category.InheritedColor` in `SetCategory`)*

### P4-CAT-7 — Be shown, in the same window, that they've chosen a transfer instead
The list of categories also offers "transfer to/from" each open account. Choosing one
switches the window into a mode where type and description don't apply and aren't
offered, because a transfer isn't a category.
*(`SetTransfer`, `Add` early return)*

### P4-CAT-8 — Be told why the window opened
The window can carry a one-line message at the top explaining the context it was opened
in — for example, that the category they typed doesn't exist yet — and highlight the
part of the name that needs their attention.
*(`CategoryDialog.Message`, `Select(string)`)*

### P4-CAT-9 — Say where a deleted category's history should go
Deleting a category that still has transactions doesn't just remove it: the user is
told how many transactions use it and made to choose the category those transactions
move to before the deletion proceeds.
*(`MergeCategoryDialog`, `CategoriesControl.Delete` → `Merge` — but see Open
Question 8)*

### P4-CAT-10 — Fold one category into another
The same window serves a deliberate merge: pick the category to fold this one into,
narrowing a long list by typing part of the full path. Confirming isn't possible until
a destination is chosen.
*(`MergeCategoryDialog.OnCategorySelected`, `ComboBoxForCategory_FilterChanged`)*

### P4-CAT-11 — Recategorize everything they're currently looking at
The user can move every transaction in the list they have on screen to a different
category in one step, with the category they're moving away from shown, read-only, so
they can see what they're about to change.
*(`RecategorizeDialog`, `TransactionsView` recategorize-all command)*

---

## 4.6 Renaming payees and building rename rules

### P4-PAYEE-1 — Rename a party, or fold it into another
The user says what to rename from and what to rename to, choosing the destination from
their existing parties or typing a new name that gets created for them. Every matching
transaction is moved across in one operation.
*(`RenamePayeeDialog`, `MyMoney.ApplyAlias`)*

### P4-PAYEE-2 — Match a family of messy names with one rule
Rather than an exact name, the user can tick a box to treat what they typed as a
pattern, so one rule can catch all the variants a bank sends.
*(`checkBoxUseRegex`, `AliasType.Regex` — but see Open Question 9)*

### P4-PAYEE-3 — Decide whether this is a one-off or an ongoing rule
The user chooses whether the rename applies only to what's already recorded, or is kept
as a standing rule that also renames future downloads.
*(`checkBoxAuto` → `Aliases.AddAlias` vs a throwaway `Alias`)*

### P4-PAYEE-4 — See which existing rules a new one would swallow
As the user types an ongoing rule, the product lists the existing rules that the new
one would make redundant, so they can see the consequence before committing.
*(`CheckConflicts`, `MyMoney.FindSubsumedAliases`)*

### P4-PAYEE-5 — Be warned when a broad rule would sweep up unrelated parties
If the rules being swallowed point at several *different* parties, the user is told how
many and named some of them, and has to confirm that they really mean to collapse them
all into one.
*(`OnOkButton_Click` conflict confirmation)*

### P4-PAYEE-6 — Be warned when a rule matches nothing
If nothing at all would change, the user is told and asked whether to keep the rule
anyway, rather than silently saving a rule that does nothing.
*(`MyMoney.FindAliasMatches` empty-result confirmation)*

### P4-PAYEE-7 — Have a rule applied to what they're editing, not just what's saved
Transactions currently open and being edited on screen are included in what the rule
matches and renames, so the user doesn't get inconsistent results depending on what
they happen to have open.
*(`TransactionCollection` concatenated into the candidate set)*

### P4-PAYEE-8 — Clean up a shouty bank name with one click
A button turns the messy source name into ordinary capitalisation and puts it in the
"rename to" box, so the common case takes one click instead of retyping.
*(`CamelCaseButton_Click`, `string.CamelCase()` — but see Open Question 10)*

---

## 4.7 Paperwork attached to a transaction

### P4-ATT-1 — Keep receipts and documents with the transaction they belong to
The user opens a window showing everything filed against the selected transaction —
photographs and scans as images, typed or pasted notes as editable rich text, and
anything else as a file with its own icon.
*(`AttachmentDialog`, `AttachmentDialogImageItem`/`DocumentItem`/`FileItem`)*

### P4-ATT-2 — Keep the paperwork window open while working through the register
The attachments window isn't modal: it stays open beside the register and follows the
user's selection, so they can walk down a statement filing receipts without reopening
it for each row. Minimising or closing the main window takes it with them.
*(`ScanAttachments`, `OnSelectionChanged`, `OnAppWindowStateChanged`, `OnAppClosed`)*

### P4-ATT-3 — File a document by dropping it on the window
Dropping files onto the window files them against the current transaction and marks
that transaction as having paperwork. Anything that can't be copied is reported by name
rather than failing silently.
*(`OnDrop` — but see Open Question 11)*

### P4-ATT-4 — Paste in whatever is on the clipboard
The user can paste a screenshot, a picture, formatted text or plain text straight into
the window and have it become an attachment.
*(`Paste`)*

### P4-ATT-5 — Tidy up a photographed receipt
The user can rotate a crooked scan either way, drag a frame to crop it, or let the
product find the document's edges within the photograph and propose the crop itself.
*(`RotateLeft`/`RotateRight`, `Resizer`, `AutoCrop`, `CannyEdgeDetector`)*

### P4-ATT-6 — Read small print by zooming in
The user can magnify and shrink what they're looking at, with the crop frame staying
attached to the right part of the image as they do.
*(`ZoomIn`/`ZoomOut`, `MoveResizer`)*

### P4-ATT-7 — Type a note as an attachment and edit it in place
A pasted or created text attachment is editable right there in the window, and can be
made wider or narrower so it reads the way the user wants.
*(`AttachmentDialogDocumentItem`, `LiveResizable`)*

### P4-ATT-8 — Print an attachment
The user can send the selected attachment to a printer through the normal print dialog.
*(`Print`, `CloneContent`)*

### P4-ATT-9 — Move, copy or remove an attachment
Cut, copy, paste and delete work on the selected attachment by menu, toolbar or the
usual keystrokes, so paperwork can be moved between transactions.
*(the window's command bindings — but see Open Question 12)*

### P4-ATT-10 — Not lose edits by closing the window
Rotations and crops are written back automatically when the window closes, and the
transaction's paperwork indicator is refreshed at the same moment.
*(`OnClosing` → `Save`, `AttachmentManager.FindAttachments`)*

### P4-ATT-11 — Open an attachment in the application that owns it
Double-clicking an attachment opens it in whatever program normally handles that kind
of file, so a PDF statement opens in a PDF reader rather than being shown as an icon.
*(`OnDoubleClickItem`)*

---

## 4.8 Giving a transaction a date for tax purposes

### P4-TAXDATE-1 — Record a different date for tax than the one it happened on
The user picks a date on a calendar to use for tax purposes on the selected
transaction, with the window's title and prompt explaining what's being set.
*(`PickYearDialog` in `PickDateDialog.xaml`, `SetTitle`/`SetPrompt`)*

### P4-TAXDATE-2 — Take that override off again
A "remove" button clears the chosen date so the transaction goes back to using its real
date, distinguishing "no override" from "cancel".
*(`OnRemove` clearing the picker, `SelectedDate` returning null)*

---

## 4.9 Rental property

### P4-RENT-1 — Describe a rental property
The user records a property's name, address and free-text notes.
*(`RentalDialog`)*

### P4-RENT-2 — Record who owns it and in what shares
Two owners and their percentages are recorded on their own tab.
*(`RentBuilding.OwnershipName1`/`2`, `OwnershipPercentage1`/`2`)*

### P4-RENT-3 — List the units within the property and who rents each
A table on its own tab holds each unit's number, its current renter and a note.
*(`RentBuilding.Units`, `RentUnit` — but see Open Question 13)*

### P4-RENT-4 — Say which categories make up the property's economics
On a third tab the user nominates the categories that represent the property's income
and its taxes, interest, repairs, maintenance and management, choosing "not applicable"
for any that don't apply.
*(`RentalDialog.Categories` with a synthetic not-applicable entry)*

---

## 4.10 Where the data lives, and who can open it

### P4-DB-1 — Create a new local file for their books
The user gives the new file a display name and a location — typed or chosen with a file
browser — and is told if either is missing rather than getting a half-made file.
*(`NewSqliteDatabaseDialog`)*

### P4-DB-2 — Keep their books on a database server instead
The user names a server (picking from servers already known, or typing a new one) and a
catalog, plus the display name they'll see it under.
*(`NewSqlServerDatabaseDialog`)*

### P4-DB-3 — Reopen a set of books they've used before
The user picks from a list of the databases they've registered, most recently used
first, and can open one by double-clicking it.
*(`OpenDatabaseDialog`, `DatabaseRegistry`)*

### P4-DB-4 — Get a new server ready for the product
If the product doesn't yet know how to reach a server, it asks the user for the
server administrator's password — explaining which server and why — and sets the server
up rather than just failing.
*(`SaCredentialDialog`, `ISaCredentialPrompt`, `SqlServerBootstrapper`)*

### P4-DB-5 — Let someone else sign in to shared books
The user creates an additional sign-in for a server-hosted set of books, entering a
user name and a password twice. Mismatched or empty entries are refused with an
explanation, and a rejection from the server is shown in its own words.
*(`AddLoginDialog`, `SqlServerDatabase.AddLogin`)*

### P4-DB-6 — Protect their data with a password, and prove it later
The same window both sets the password that protects a set of books and asks for it on
the way in, showing whether what's been typed matches and refusing an empty one unless
the password is explicitly optional.
*(`PasswordWindow`, `Optional`, `RealPassword`)*

---

## 4.11 Bringing data in from elsewhere

### P4-IMPORT-1 — Merge another copy of their books into this one
The user selects one or more other data files and the product works through them
account by account, matching each incoming transaction against what's already there and
either filling in the gaps on the existing record or adding it as new.
*(`MoneyFileImportDialog.Import`/`ImportAccount`, `Transaction.Merge`)*

### P4-IMPORT-2 — Watch the merge happen and see what changed where
While it runs, the user sees a row per account with its own progress bar and a running
count of how many rows that account gained or had updated, and a tick when it's done.
*(`AccountImportState`, the account list template)*

### P4-IMPORT-3 — Jump from an account's result to the transactions it changed
Clicking an account in that list takes the user to exactly the transactions the merge
touched, so they can check the work rather than trusting the count.
*(`OnRowSelected` → `IViewNavigator.ViewTransactions`)*

### P4-IMPORT-4 — Open a password-protected file they're importing
If the incoming file is protected, the user is prompted for its credentials, with the
prompt worded for the file being imported rather than for their own data.
*(`ProcessFile` reusing `PasswordWindow` with a rewritten prompt)*

### P4-IMPORT-5 — Stop a long merge, and be asked before losing one
Closing the window while a merge is running asks whether to cancel; saying no leaves it
running. Closing the main window closes the merge window with it.
*(`OnClosing`, `CancellationTokenSource`, `OnOwnerClosed`)*

### P4-IMPORT-6 — Bring the paperwork and statements across too
Attachments and statement documents belonging to the incoming transactions are brought
over alongside the transactions themselves.
*(`AttachmentManager.ImportAttachments`, `StatementManager.ImportStatements` — but
see Open Question 14)*

### P4-IMPORT-7 — Teach the product what a spreadsheet's columns mean
When importing a delimited file, the user is shown each column heading as it appears in
their file and chooses which piece of a transaction it represents, narrowing a list of
possible fields by typing.
*(`CsvImportDialog`, `CsvMap`, `CsvFieldMap`)*

### P4-IMPORT-8 — Be caught out before importing a broken mapping
Mapping two columns to the same thing is refused with the offending field named.
Leaving some fields unmapped is allowed, but only after the user confirms they meant to.
*(`CsvImportDialog.Validate`)*

### P4-IMPORT-9 — Flip the sign of a file that records spending as positive
A single toggle handles the common case of a bank that exports amounts the opposite way
round from the product's convention.
*(`CsvMap.Negate`)*

### P4-IMPORT-10 — Not have to redo the mapping every time
A mapping the user has already built for a given source is loaded back into the window
so they only have to adjust it, not rebuild it.
*(`CsvImportDialog.SetMap`, `WpfBusinessLayerUiCallback.PromptForCsvFieldMapping`)*

---

## 4.12 Saying what a report or export should cover

### P4-REPORT-1 — Choose the period a chart covers
The user picks a start and end date for the history chart beside their account, and,
where it applies, whether the period is broken down by day, month or year.
*(`ReportRangeDialog`, opened from the trend chart's own menu — but see Open
Question 16)*

### P4-REPORT-2 — Not be asked about a breakdown that doesn't apply
Where the thing being configured has no notion of intervals, that choice is hidden
rather than shown and ignored.
*(`ReportRangeDialog.ShowInterval` — but see Open Question 15)*

### P4-REPORT-3 — Limit the period to the categories they care about
The window can offer a tick-list of categories, all ticked to begin with, so the user
can exclude the ones they don't want included. *No scenario claims this reaches the
user today* — nothing switches it on; see Open Question 16.
*(`EnableCategoriesSelection`, `Categories`, `CheckItem`)*

### P4-REPORT-4 — Say which tax year an export is for
The user enters the year, in full or as two digits. Anything that isn't a number
prevents them confirming rather than producing an empty export.
*(`TaxReportDialog.Year`, `YearText_TextChanged`)*

### P4-REPORT-5 — Work to a tax year that doesn't start in January
The user picks the month their financial year starts, pre-set to whatever they've
configured for their books.
*(`TaxReportDialog.Month`, `FiscalStartMonthCombo`)*

### P4-REPORT-6 — Choose how investment sales are grouped for tax
The user says whether holdings should be consolidated by the date they were acquired or
the date they were sold, and can limit the export to investment activity only.
*(`ConsolidateSecuritiesOnDateSold`, `InvestmentsOnly`)*

---

## 4.13 Working directly with the stored data

### P4-SQL-1 — Run a query against their own data and see the rows
A power user can type a query, run it, and get the resulting rows in a table, with the
query and the results each taking as much of the window as they drag them to. A blank
query, or one the database rejects, is reported rather than swallowed.
*(`FreeStyleQueryDialog` — but see Open Question 17)*

### P4-SQL-2 — See what the product last wrote to the database
The same window is reused, pre-filled, to show the log of recent changes the storage
layer has recorded.
*(`MainWindow.OnCommandShowLastUpdate`, `IDatabase.GetLog`)*

### P4-SQL-3 — Save query results for use elsewhere
The rows that came back can be written to a file, schema included, so they can be taken
into another tool.
*(`OnMenuItemSave_Clicked`, `DataSet.WriteXml`)*

---

## 4.14 Trying the product out with realistic data

### P4-SAMPLE-1 — Generate a believable set of books to explore
Before the product builds sample data, the user says which spending profile to base it
on, the name of the fictitious employer, the size of the fortnightly pay cheque, an
annual inflation rate and how many years to simulate — so the sample resembles their own
situation closely enough to be worth exploring.
*(`SampleDatabaseOptions`, `SampleDatabase`)*

### P4-SAMPLE-2 — Be told which entry is wrong before generating anything
Each value is checked as it's typed and the single reason confirmation is unavailable
is shown in words — a missing profile file, a non-numeric year count, an unparseable pay
cheque or inflation figure.
*(`EnableButtons`, the message line)*

---

## 4.15 What every dialog does the same way

### P4-DLG-1 — Have windows look like part of the product
Dialogs take the product's own colours, including in dark mode, rather than the
system's defaults.
*(`BaseDialog` — but see Open Question 18)*

### P4-DLG-2 — Get help about the window they're in
The windows that cover a substantial subject carry their own help topic, so pressing
help in an account, online-banking, attachment or sample-data window lands on the page
about that subject rather than on the product's front page.
*(`HelpService.HelpKeyword` on `AccountDialog`, `OnlineAccountDialog`,
`AttachmentDialog`, `SampleDatabaseOptions` — but see Open Question 19)*

### P4-DLG-3 — Have a dialog open over the window it came from
Dialogs open centred on the window that opened them and stay in front of it, rather
than appearing elsewhere on the desktop or behind the main window.
*(`Owner` assignment at every call site, plus
`WindowStartupLocation="CenterOwner"` on 14 of the 24 windows with XAML — but see
Open Question 30)*

### P4-DLG-4 — Not have dialogs clutter the taskbar
Subordinate windows don't appear as separate taskbar entries, so the product reads as
one application.
*(`ShowInTaskbar="False"` on 13 windows plus the attachments window in code — see
Open Question 30)*

### P4-DLG-5 — Confirm or back out with the keyboard
Enter confirms and Escape backs out without reaching for the mouse.
*(`IsDefault`/`IsCancel`, `RecategorizeDialog.OnPreviewKeyDown`,
`AttachmentDialog.OnPreviewKeyDown` — but see Open Question 20)*

### P4-DLG-6 — Keep typing while the window catches up
Choosing a category, a tax line, a party, a holding or an institution is done by typing
part of what they want and seeing the list narrow, rather than scrolling thousands of
entries.
*(`FilteringComboBox` + a `FilterChanged` handler in `CategoryDialog`, `LoanDialog`,
`MergeCategoryDialog`, `RecategorizeDialog`, `CsvImportDialog`, and the hand-rolled
equivalent in `OnlineAccountDialog`)*

### P4-DLG-7 — Have a dialog notice the data changing underneath it
Where something can change while a dialog is open — a new institution connection, a new
exchange rate, a new party — the dialog updates itself rather than showing stale
choices.
*(`AccountDialog.OnMoneyChanged`, `RenamePayeeDialog.OnPayees_Changed`)*

---

## Phase 4 coverage checklist

Every file in `Source/WPF/MyMoney/Dialogs/`. "Not user-facing" entries are shared
bases, view models, plumbing or dead code the user never perceives.

### Dialog classes

| File(s) | Class | Status |
|---|---|---|
| `AccountDialog.xaml` + `.xaml.cs` | `AccountDialog` | P4-ACCT-1…P4-ACCT-9 |
| `LoanDialog.xaml` + `.xaml.cs` | `LoanDialog` | P4-ACCT-10 — see Open Questions 3, 4 |
| `SelectAccountDialog.xaml` + `.xaml.cs` | `SelectAccountDialog` | P4-ACCT-11 |
| `SelectAccountDialog.xaml.cs` | `AccountHelper` | P4-ACCT-11, P4-ACCT-12 — the static "pick an account, or make one" helper every importer and the online-account flow share |
| `OnlineAccountDialog.xaml` + `.xaml.cs` | `OnlineAccountDialog` | P4-ONLINE-1…P4-ONLINE-12 |
| `OnlineAccountDialog.xaml.cs` | `AccountListItem` | P4-ONLINE-8…P4-ONLINE-10 — the per-account row's view model; **its `ToolTipMessage` setter is broken**, see Open Question 21 |
| `PasswordWindow.xaml` + `.xaml.cs` | `PasswordWindow` | P4-AUTH-1, P4-AUTH-2, P4-AUTH-7, P4-DB-6, P4-IMPORT-4 — the shared credential window every other sign-in window is built from |
| `PasswordWindow.xaml.cs` | `OkEventArgs` | **Not user-facing** — how a subclass vetoes confirmation and supplies the reason |
| `OfxLoginDialog.cs` | `OfxLoginDialog` | P4-AUTH-1, P4-AUTH-2 |
| `MfaChallengeDialog.cs` | `MfaChallengeDialog` | P4-AUTH-3 |
| `AuthTokenDialog.cs` | `AuthTokenDialog` | P4-AUTH-4 — see Open Question 6 |
| `ChangePasswordDialog.cs` | `ChangePasswordDialog` | P4-AUTH-5 |
| `OnlineServiceDialog.xaml` + `.xaml.cs` | `OnlineServiceDialog` | P4-QUOTES-1…P4-QUOTES-5 — see Open Question 7 |
| `CategoryDialog.xaml` + `.xaml.cs` | `CategoryDialog` | P4-CAT-1…P4-CAT-8 |
| `MergeCategoryDialog.xaml` + `.xaml.cs` | `MergeCategoryDialog` | P4-CAT-9, P4-CAT-10 — see Open Question 8 |
| `RecategorizeDialog.xaml` + `.xaml.cs` | `RecategorizeDialog` | P4-CAT-11 |
| `RenamePayeeDialog.xaml` + `.xaml.cs` | `RenamePayeeDialog` | P4-PAYEE-1…P4-PAYEE-8 — see Open Questions 9, 10 |
| `AttachmentDialog.xaml` + `.xaml.cs` | `AttachmentDialog` | P4-ATT-1…P4-ATT-11 |
| `AttachmentDialog.xaml.cs` | `AttachmentDialogItem` (abstract) | **Not user-facing** — the contract each kind of attachment implements |
| `AttachmentDialog.xaml.cs` | `AttachmentDialogImageItem` | P4-ATT-1, P4-ATT-5, P4-ATT-8 |
| `AttachmentDialog.xaml.cs` | `AttachmentDialogDocumentItem` | P4-ATT-1, P4-ATT-4, P4-ATT-7 |
| `AttachmentDialog.xaml.cs` | `AttachmentDialogFileItem` | P4-ATT-1, P4-ATT-11 — **but its `Save` and `Copy` are empty**, see Open Question 12 |
| `AttachmentDialog.xaml.cs` | `WiaErrorCode` (enum) | **Not user-facing** — scanner error codes for a scan feature that is entirely commented out; see Open Question 22 |
| `PickDateDialog.xaml` + `.xaml.cs` | `PickYearDialog` | P4-TAXDATE-1, P4-TAXDATE-2 — note the class/file name mismatch |
| `RentalDialog.xaml` + `.xaml.cs` | `RentalDialog` | P4-RENT-1…P4-RENT-4 — see Open Questions 13, 23 |
| `NewSqliteDatabaseDialog.xaml` + `.xaml.cs` | `NewSqliteDatabaseDialog` | P4-DB-1 |
| `NewSqlServerDatabaseDialog.xaml` + `.xaml.cs` | `NewSqlServerDatabaseDialog` | P4-DB-2 — see Open Question 24 |
| `OpenDatabaseDialog.xaml` + `.xaml.cs` | `OpenDatabaseDialog` | P4-DB-3 |
| `SaCredentialDialog.xaml` + `.xaml.cs` | `SaCredentialDialog` | P4-DB-4 |
| `AddLoginDialog.xaml` + `.xaml.cs` | `AddLoginDialog` | P4-DB-5 |
| `MoneyFileImportDialog.xaml` + `.xaml.cs` | `MoneyFileImportDialog` | P4-IMPORT-1…P4-IMPORT-6 — see Open Questions 14, 25 |
| `MoneyFileImportDialog.xaml.cs` | `AccountImportState` | P4-IMPORT-2, P4-IMPORT-3 — the per-account row's view model, including the timer that throttles progress updates |
| `MoneyFileImportDialog.xaml.cs` | `DemoList` | **Not user-facing** — five fruit-named placeholder rows so the list has content in the visual designer; cleared in the constructor before the window is ever shown |
| `CsvImportDialog.xaml` + `.xaml.cs` | `CsvImportDialog` | P4-IMPORT-7…P4-IMPORT-10 |
| `ReportRangeDialog.xaml` + `.xaml.cs` | `ReportRangeDialog` | P4-REPORT-1, P4-REPORT-2 — despite the name, its only caller is the history chart, not any report; see Open Questions 15, 16 |
| `ReportRangeDialog.xaml.cs` | `CheckItem` | **Not user-facing** — a tickable wrapper round a category, for a list nothing populates |
| `TaxReportDialog.xaml` + `.xaml.cs` | `TaxReportDialog` | P4-REPORT-4…P4-REPORT-6 |
| `FreeStyleQueryDialog.xaml` + `.xaml.cs` | `FreeStyleQueryDialog` | P4-SQL-1…P4-SQL-3 — see Open Questions 17, 26 |
| `SampleDatabaseOptions.xaml` + `.xaml.cs` | `SampleDatabaseOptions` | P4-SAMPLE-1, P4-SAMPLE-2 — see Open Question 27 |
| `BaseDialog.cs` | `BaseDialog` | P4-DLG-1 — **not itself user-facing**; a 12-line `Window` subclass whose entire job is to take the product's page background and text brushes |

### Dialog members that are not user-facing

| Member | Reason |
|---|---|
| `AttachmentDialog.Scan`, `GetScannerAsync`, the `WIA.Device`/`CommonDialog` statics | **Dead** — the whole scanner feature is commented out, including its toolbar button |
| `AttachmentDialog.GetWiaErrorMessage` | **Dead** — live code, but its only caller was the commented-out `Scan` method; nothing calls it now |
| `AttachmentDialog.Browse` | **Dead** — a folder browser for changing the attachment directory that no button or menu item invokes |
| `AttachmentDialog.ClearSelection`, `ItemCount` | Internal bookkeeping around save and selection |
| `CategoryDialog.comboBoxType_SelectionChanged` | **Dead** — an empty handler; the XAML wires no `SelectionChanged` on that control |
| `CategoryDialog.ColorPicker` | **Not user-facing** — reaches a non-public `Content` property on the colour flyout by reflection, the same class of workaround as Phase 3's `SymbolIconHackery` |
| `LoanDialog.onlineAccounts`, `newOnlineAccounts`, `InsertAccount`, `GetMatchingOnlineAccount` | **Dead** — the loan window builds and sorts a list of institution connections that no control in its XAML is bound to (the grid cell where one would go is `Visibility="Collapsed"` and empty), so `OnCancel`'s cleanup loop is always over an empty list |
| `AccountDialog.GetMatchingOnlineAccount(string name)` | Its `name` parameter is ignored — the body compares `editingAccount.OnlineAccount.Name` instead. Harmless today because the only call site passes exactly that, but it is not the function its signature claims |
| `OnlineAccountDialog.OnShowXml` | **Dead** — a hyperlink handler no XAML references |
| `OnlineAccountDialog.MeasureListString`, `typeface` | **Dead** — a text-measuring helper nothing calls; the column sizing is done by `ResizeListColumns` instead |
| `OnlineAccountDialog.ResizeListColumns` | **Not user-facing** — a documented workaround for a list-column sizing bug |
| `OnlineServiceDialog.Apply` | **Deliberately empty**, with a comment saying so — see Open Question 7 |
| `OnlineServiceDialog.Settings`, `SelectedSettings` | Read by the caller after the window closes; not things the user perceives |
| `RentalDialog.UpdateUI` | **Dead** — an empty method called twice |
| `FreeStyleQueryDialog.myMoney` | **Dead** — the constructor takes and stores the whole financial model and never reads it |
| `MoneyFileImportDialog.ShowMessage`/`ShowOutput`/`ShowProgress` | **Not user-facing** — the status-service interface implemented with empty bodies so the storage layer has somewhere to report to while loading the incoming file; the window shows its own progress instead |
| `PasswordWindow.OnCancel` | Calls `Hide()` without setting a result; backing out works because the button is also marked as the window's cancel button. Fragile rather than wrong |
| `AttachmentDialog.OnItemChanged` | Dereferences the *selected* item rather than the item that changed; only ever called for the selected one today |

---

## Open questions from Phase 4

Things a human should double-check, because the call was a judgement rather than
obvious from the code — and, where noted, because they look like real defects.

1. **Nine dialogs, one shared credential window — and it is the riskiest thing here.**
   `PasswordWindow` is simultaneously the "set a password on my data" window, the
   "prove you know it" window, the online-banking sign-in, the multi-factor
   questionnaire, the authentication-token prompt and the change-password flow, each
   subclass bending it by adding fields and rewriting its prose at runtime. It is
   captured as several scenarios because those are genuinely different user goals, but
   a redesign should decide deliberately whether they stay one window.

2. **The account window's aliases are edited live, not on a working copy.** Everything
   else in `AccountDialog` is edited on a copy and only committed on OK (P4-ACCT-3),
   but adding an alias calls `money.AccountAliases.AddAlias` immediately and the little
   close box calls `container.RemoveAlias` immediately. Cancelling the window does not
   undo either. P4-ACCT-6 therefore describes what the user can do, not a
   cancel-safe edit. Worth confirming this is intentional; it is a visible
   inconsistency within one window.

3. **The loan window's web-site button throws on an empty address.** `LoanDialog.
   ButtonGoToWebSite` calls `this.editingAccount.WebSite.ToLower()` *before* its
   `string.IsNullOrWhiteSpace` guard — the two statements are in the opposite order
   from the working version in `AccountDialog.OnButtonGoToWebSite`. Clicking `>>` on a
   loan with no web site recorded is a null reference, i.e. the app's unhandled-exception
   path. P4-ACCT-5 describes the account window's behaviour only.

4. **The loan window validates nothing.** Unlike the account window (P4-ACCT-2), it has
   no name check at all, so OK will happily write an empty name onto the account, and
   it has no help topic. It also copies `OpeningBalance` on OK while offering no field
   to edit it. It reads as an older copy of `AccountDialog` that stopped being
   maintained.

5. **The institution window shows fields that don't apply for half the account types.**
   `ShowHideFieldsForAccountType` has cases for chequing/savings/money-market/credit-line,
   credit card, brokerage and retirement — and no `default`. For a cash, asset, loan or
   any other account type, the bank, branch *and* broker identifier fields all stay
   visible with their XAML defaults. P4-ONLINE-3 claims only that the fields are
   tailored where the product tailors them.

6. **The authentication-token prompt can end up with a blank label.** `AuthTokenDialog`
   computes a fallback label ("Authentication token") when the institution supplies
   none — and then passes `info.AuthTokenLabel` (the empty one) to the field anyway,
   never using `label`. A bank that sends no label gives the user an unlabelled box.
   One-line copy-paste bug; P4-AUTH-4 describes the intent.

7. **Cancelling the market-data settings window keeps the changes.** `OnlineServiceDialog.
   Apply()` is empty with the comment *"apply has nothing to do since we don't yet have a
   proper cancel that restores the edited settings on cancel"*, and `OnCancel` just closes.
   Access keys, request limits and the two capability toggles are written straight to the
   live settings objects as the user edits them. So Cancel is a lie in this one window.
   Flagged rather than captured as a scenario.

8. **The merge-category window's OK button is declared as its Cancel button.** Both
   buttons in `MergeCategoryDialog.xaml` carry `IsCancel="True"` and neither carries
   `IsDefault`, so Escape is ambiguous and Enter confirms nothing. Its two call sites in
   `CategoriesControl` also test the outcome differently — the delete path uses
   `ShowDialog() == false || SelectedCategory == null`, the merge path uses
   `ShowDialog() == true && SelectedCategory != null` — which is the shape you get when
   one of them was written around a result that couldn't be relied on. Worth someone
   running both paths: if the framework's cancel handling wins over the click handler,
   confirming a category merge silently does nothing.

9. **The known malformed-pattern crash in the rename window is confirmed here.**
   `CLAUDE.md` already records that `Alias`'s setters build the regular expression
   eagerly. `RenamePayeeDialog.CheckConflicts` does exactly that
   (`new Alias() { Pattern = …, AliasType = … }`) and, worse, runs it from a delayed
   timer callback 50 ms after the user types — so a half-typed pattern with "use regular
   expressions" ticked throws from a timer, not from a button press, with nothing in the
   dialog catching it. P4-PAYEE-2 describes the capability; this remains an open,
   unfixed robustness gap.

10. **Two buttons in the rename window both claim to be the default.** `CamelCaseButton`
    and `okButton` are both `IsDefault="True"`. The same pattern appears in
    `OnlineAccountDialog` (Connect *and* OK) and three times over in
    `OnlineServiceDialog` (Browse, Disable *and* OK). What Enter does in those windows
    is at best unpredictable and at worst destructive — Disable clears an access key.
    P4-DLG-5 describes the intent; this is the counter-example.

11. **Dropping several files at once files the first one N times.**
    `AttachmentDialog.OnDrop` iterates `foreach (var file in files)` but then copies
    `files[0]` on every pass — it takes the *extension* from `file` and the *content*
    from `files[0]`. Dropping three receipts gives three copies of the first, two of
    them with the wrong extension. P4-ATT-3 describes single-file drops honestly.

12. **Saving an attachment destroys any non-image, non-text attachment.**
    `AttachmentDialog.Save` runs over *every* item whenever anything is dirty (including
    automatically on close, P4-ATT-10). For each it asks for a fresh unique file name —
    which, because the old file still exists, is always a *different* name — deletes the
    old file, then calls `item.Save(newName)`. `AttachmentDialogFileItem.Save` is an empty
    method. So a PDF or Word statement filed against a transaction is deleted and not
    rewritten the first time the user rotates any other attachment in that window.
    `AttachmentDialogFileItem.Copy` is also empty, and `Cut` is implemented as copy-then-delete —
    so cutting one of those attachments deletes it and puts nothing on the clipboard.
    Confirmed by reading `AttachmentManager.GetUniqueFileName` (returns the first
    non-existent name) and `TempFilesManager.DeleteFile` (deletes immediately). This is
    the most serious thing Phase 4 found and is worth verifying by hand before anyone
    trusts the attachments feature with a PDF.

13. **Unit edits in the rental window survive Cancel.** `RentBuilding.ShallowCopy` is a
    `MemberwiseClone`, so the working copy and the real property share the *same* list of
    units. The units table is bound to that shared list, and `ButtonOk` copies name,
    address, note, ownership and the six categories back — but never the units, because
    it doesn't need to. The consequence is that editing a renter's name and then
    cancelling keeps the change, while editing the property's name and cancelling
    discards it. P4-RENT-3 describes the capability without claiming Cancel undoes it.

14. **Imported attachments are filed against the wrong transaction.**
    `MoneyFileImportDialog.ImportAccount` calls `myAttachments.ImportAttachments(t, …)`
    where `t` is the transaction from the *incoming* file. In the merge branch that is
    harmless (the two share an id). In the new-transaction branch the local transaction
    `u` has a different id, so the attachment is copied into the user's attachment folder
    under the incoming file's transaction id and the new transaction never shows it; the
    `HasAttachment` flag is also set on the throwaway source object rather than on `u`.
    P4-IMPORT-6 describes the intent.

15. **Re-showing the report interval doesn't re-show the control.**
    `ReportRangeDialog.ShowInterval`'s `false` branch hides both the label and the combo;
    its `true` branch shows only the label. Since the constructor makes both visible, this
    only bites a caller that sets it false then true on the same instance — but the
    asymmetry is plainly unintended.

16. **The "Report" window is not used by any report, and half of it is unreachable.**
    `ReportRangeDialog` has exactly **one** caller in the whole product —
    `TrendGraph.OnSetRange`, the range command on the history chart strip — which
    retitles it "Graph Range" and sets `ShowInterval = false`. No report opens it; reports
    configure themselves in the side panel instead (Phase 3's P3-PANEL-1). And its
    category tick-list is dead in both directions: `CategoriesPicker` is collapsed in
    XAML, `EnableCategoriesSelection` is never set by anything, and the `Categories`
    setter that would populate the list is never called — so the list is simultaneously
    hidden and empty. P4-REPORT-1 and P4-REPORT-2 describe the chart-range use;
    P4-REPORT-3 is explicitly marked as not reaching the user. The window's name, its
    title, its help-free state and its dead half all point at a feature that was
    repurposed and never tidied.

17. **The Adhoc SQL Query menu item does nothing on a SQLite file.**
    `MainWindow.OnCommandAdhocQuery` casts the open database to `SqlServerDatabase` and
    returns silently if it isn't one — and SQLite is the default local format.
    `MenuQueryAdhoc` has no `CanExecute`, so the item is always enabled and simply
    doesn't respond. Same shape as Phase 2's unreachable Backup command. (The adjacent
    "Show Last Update" item works on any database.) P4-SQL-1 is therefore only reachable
    for server-hosted books.

18. **Four windows don't inherit the product's theme.** `SaCredentialDialog`,
    `OpenDatabaseDialog`, `NewSqliteDatabaseDialog` and `NewSqlServerDatabaseDialog`
    derive from `Window`, not `BaseDialog` — so in dark mode they appear as white system
    dialogs. These are also the four newest dialogs in the folder (the database-registry
    work), which suggests the convention wasn't obvious to whoever added them. Directly
    relevant to a UI redesign.

19. **Only four windows have a help topic.** `AccountDialog`, `OnlineAccountDialog`,
    `AttachmentDialog` and `SampleDatabaseOptions` carry a `HelpKeyword`; the other
    twenty-five do not, including the loan, category, rename-payee and online-service
    windows, which are at least as likely to need explaining. P4-DLG-2 describes what
    exists, not a general guarantee.

20. **Enter and Escape are handled four different ways.** Some windows use
    `IsDefault`/`IsCancel`; `RecategorizeDialog` and `AttachmentDialog` intercept key
    presses themselves; `AccountDialog` has to swallow Enter in its alias box so it
    doesn't close the window; and the windows in Open Question 10 have several competing
    defaults. `RecategorizeDialog`'s interception is window-wide, so pressing Enter while
    still typing into its category box commits the whole recategorize-everything
    operation. Worth settling as one convention in a redesign.

21. **The online-account list's tooltips never update.**
    `AccountListItem.ToolTipMessage`'s setter raises a change notification for
    `"WarningMessage"` — a property that does not exist. The list's tooltip binding is to
    `ToolTipMessage`, so it never refreshes. `OnIconButtonClick` works around this by
    assigning `e.ToolTip` directly on three of its five branches, which is presumably how
    it went unnoticed.

22. **Document scanning was built and then switched off.** The scan toolbar button, the
    scanner-selection code and the acquire-image flow are all commented out in
    `AttachmentDialog`, but the WIA error-code enumeration and the error-message
    translator survive as live, uncalled code. Given the dialog's own class comment still
    says "Interaction logic for ScanDialog.xaml", scanning receipts was clearly the
    original point of this window. If the redesign wants it back, the intent is written
    down; if not, there is a block of dead code to remove.

23. **The rental window can't edit half of what the model stores.** Phase 1's P1-RENT-1
    records purchase date, purchase price, land value and current estimated value on a
    property; `RentalDialog` offers none of them. Either they're maintained somewhere
    else, or they can only be set by importing. Worth checking before a redesign assumes
    the existing window is the complete property editor.

24. **The "Test database" tick-box is hidden for SQLite and not for SQL Server.**
    `NewSqliteDatabaseDialog` collapses it under `#if !DEBUG`; `NewSqlServerDatabaseDialog`
    shows it always and reads it always. So a release build shows end users a developer
    affordance in one of the two new-database windows and not the other.

25. **"Money File Import" only imports one kind of money file.** Despite the name and the
    multi-file selection, `ProcessFile` accepts only `.db`/`.mmdb` and answers anything
    else with "Import only supports sqllite money files" (sic) before stopping the whole
    batch. Captured under P4-IMPORT-1 without claiming broader reach; the wording and the
    scope are both worth revisiting.

26. **Six of the eight menu items in the query window do nothing.** `FreeStyleQueryDialog`'s
    File menu offers New, Open…, (a blank item), Save, Save as…, Export… and Exit, of
    which only Save has a handler; the others have no `Click` and no `Command`. P4-SQL-3
    covers Save only.

27. **The sample-data window opens with OK disabled about half the time.** Its profile
    field defaults to the bare relative name `SampleData.xml`, which `File.Exists` resolves
    against the working directory, so the window shows "Template file not found" and a
    disabled OK unless the caller supplies a real path first. The one live caller
    (`SampleDatabase`) does extract the embedded file and set the path, so the real flow is
    fine — but the default is misleading and the dialog is unusable if opened any other way.

28. **The boundary with Phases 5 and 6.** `TaxReportDialog` and `ReportRangeDialog` are
    captured here as *option collection* (P4-REPORT-*); what the resulting tax export or
    chart actually contains is Phase 7 and Phase 5. `CsvImportDialog` and
    `MoneyFileImportDialog` are captured here as the *conversation with the user*; the
    parsing, matching and writing behind them is Phase 6. `OnlineAccountDialog`'s protocol
    exchange with a bank is Phase 6 or Phase 8; only what the user sees and decides is here.
    If a later phase finds a user-visible decision that only exists inside one of these
    windows, it belongs here.

29. **Modal windows also live outside `Dialogs/`.** This phase's scope was that one folder,
    but the product puts modal UI elsewhere too — `MessageBoxEx` (the product's own
    replacement for the system message box, already referenced by Phase 2), the print
    dialog, and the native file and folder pickers used by at least eight of these windows.
    None is catalogued here. `MessageBoxEx` in particular carries real product behaviour
    (Phase 3 and `CLAUDE.md` both record that it posts rather than blocks) and is a
    cross-cutting Phase 8 item.

30. **None of the shared dialog conventions is actually shared.** Counted across the 29
    classes: the themed base class is used by 24 of the 28 that aren't `BaseDialog`
    itself, a help topic by 4, `CenterOwner` by 14
    of the 24 with XAML, `ShowInTaskbar="False"` by 13 (plus one set in code), and
    Enter/Escape handling is done four different ways (Open Question 20). Each of
    P4-DLG-1 through P4-DLG-5 is therefore a description of the *majority* convention,
    not an invariant — a dialog picked at random has a fair chance of missing two or
    three of them. This is directly relevant to the redesign that this catalog exists to
    drive: the cheapest single improvement available in this folder is one real shared
    base that carries all five. It is also the same class of problem as the recently
    fixed "two dialogs opening disconnected from the app window" on this branch, which
    suggests the gaps are still being found one at a time.

---

# Phase 5: Reports + Charts

**Source examined (in full):** every file in `Source/WPF/MyMoney/Reports/` (14 files,
~5,400 lines) and `Source/WPF/MyMoney/Charts/` (13 code/markup files plus 5 icon
assets, ~3,000 lines), plus the two things Phase 3 explicitly deferred here —
`Views/GraphGenerators.cs` and the report-hosting half of `Views/FlowDocumentView.xaml`
+ `.xaml.cs`.

| File(s) | Lines |
|---|---|
| `Reports/IReport.cs` | 193 |
| `Reports/Reports.cs` (the `Report` base class) | 296 |
| `Reports/NetWorthReport.cs` | 733 |
| `Reports/AccountSummaryReport.cs` | 314 |
| `Reports/CashFlowReport.cs` | 724 |
| `Reports/PortfolioReport.cs` | 1,014 |
| `Reports/TaxReport.cs` | 711 |
| `Reports/W2Report.cs` | 454 |
| `Reports/FutureBillsReport.cs` | 217 |
| `Reports/UnacceptedReport.cs` | 152 |
| `Reports/RetirementPlan.cs` | 1,572 |
| `Reports/FlowDocumentReportWriter.cs` | 542 |
| `Reports/HtmlDocumentReportWriter.cs` | 270 |
| `Reports/CsvReportWriter.cs` | 220 |
| `Charts/AnimatingBarChart.xaml` + `.xaml.cs` | 12 + 900 |
| `Charts/AnimatingPieChart.xaml` + `.xaml.cs` | 11 + 535 |
| `Charts/AreaChart.cs` | 613 |
| `Charts/CategoryChart.xaml` + `.xaml.cs` | 32 + 458 |
| `Charts/CategoryData.cs` | 120 |
| `Charts/ChartData.cs` | 200 |
| `Charts/ChartLegend.xaml` + `.xaml.cs` | 70 + 222 |
| `Charts/HistoryBarChart.xaml` + `.xaml.cs` | 29 + 489 |
| `Charts/LoanChart.xaml` + `.xaml.cs` | 35 + 146 |
| `Charts/RentalChart.xaml` + `.xaml.cs` | 13 + 95 |
| `Charts/RentalChartColumn.xaml` + `.xaml.cs` | 101 + 120 |
| `Charts/RentalData.cs` | 28 |
| `Charts/FormattingConverter.cs` | 44 |
| `Charts/Styles.cs` | 43 |
| `Views/GraphGenerators.cs` (deferred here by Phase 3) | 425 |
| `Views/FlowDocumentView.xaml` + `.xaml.cs` (report-hosting path) | 126 + 385 |

**Supporting files read but owned elsewhere**, because a scenario here can't be
written honestly without them: `Controls/TrendGraph.xaml` + `.xaml.cs` (the control
that hosts `AreaChart` and owns the chart strip's own menu),
`View Selectors/ReportsControl.xaml` + `.xaml.cs` (the report options panel — Phase 3's
P3-PANEL-1/2), `MainWindow.xaml.cs`'s "Reports Menu" region and `ReportEventHandler`
(which report each menu item opens and where each drill-down lands), and
`MyMoney.Business/Payments.cs` (the recurring-payment detection behind the future-bills
report — see Open Question 1).

> **Path/naming notes.**
> - There is **no `Charts/styles.xaml`**, despite `Charts/Styles.cs` existing to load
>   one. See Open Question 19.
> - `Reports/W2Report.cs` declares its class in namespace **`Walkabout.Taxes`**, not
>   `Walkabout.Reports` — the only file in the folder that does. Searching the
>   `Walkabout.Reports` namespace misses it.
> - `Reports/RetirementPlan.cs` declares class **`RetirementPlanReport`** (plus a
>   nested `Simulation` and a `RetirementFunds` model) — filename and class don't match,
>   the same trap Phases 3 and 4 flagged.
> - `Charts/RentalChart.xaml.cs` carries the doc comment *"Interaction logic for
>   OvertimeChartControl.xaml"* and `Charts/RentalChartColumn.xaml.cs` *"…for
>   ProfitLossColumn.xaml"*; `Charts/CategoryChart.xaml.cs` says *"…for
>   ExpensesCategoryView.xaml"* and `Charts/ChartLegend.xaml.cs` *"…for
>   UserControl1.xaml"*. Four more copy-pasted `<summary>` comments naming classes that
>   don't exist.
> - `Charts/RentalChart.xaml` names its root grid **`MaingGrid`** (sic), which the
>   code-behind relies on.
> - Two chart controls (`AnimatingBarChart`, `AnimatingPieChart`, plus `AreaChart` and
>   `PieSlice`) live in namespace **`LovettSoftware.Charts`**, not `Walkabout.Charts` —
>   they read as a general-purpose charting library dropped into the product.

**What this phase covers and deliberately does not.** This section catalogues the
*questions a user asks of their own data and the answers the product gives back* — the
standalone reports reached from the Reports menu, the charts that sit beside the
register, and the one shared viewer they are all read in. Where a dialog collects the
options a report runs with, the collecting is Phase 4 (P4-REPORT-*) and the resulting
report is here. The **tax-form semantics** — which category maps to which line of which
IRS form, what a `.txf` file contains, how capital gains are classified — are Phase 7;
what is captured here is the tax *report* as a thing the user reads on screen and the
affordance that exports it. The protocol and file work behind importing is Phase 6.
`Setup/ChangeInfoReport.cs` (the "what's new in this version" page) is also rendered in
this same viewer but belongs to Phase 2's P2-HELP-4 and Phase 8.

---

## 5.1 Reading, steering and taking away a report

### P5-VIEW-1 — Read any report as one continuous document
Every report the product produces is shown the same way: as a single scrolling
document that takes over the whole working area, with headings, sub-headings, tables
of figures and, where it helps, charts embedded between the tables. The user reads it
top to bottom rather than paging through it.
*(`FlowDocumentView`, `FlowDocumentReportWriter`)*

### P5-VIEW-2 — Change the report's terms beside the report and watch it redraw
While a report is on screen, a panel appears in the navigation area carrying only the
settings that report actually has — the date it is struck at, a start and end date, a
financial year, whether to group by year or by month, which kind of accounts to cover,
how to group investment sales, and which currency to express everything in. Changing
any of them regenerates the report in place.
*(`ReportsControl` — already captured as an affordance in Phase 3's P3-PANEL-1; what
each report puts in it is described per report below)*

### P5-VIEW-3 — Find a word inside a long report
The user types into the search box above the report and the view jumps to and
highlights the next place that text occurs; typing again or asking for "find next"
moves on to the one after. There is no way to see how many matches there are or to
step backwards.
*(`FlowDocumentView.QuickFilter`, `FindManager`, the "Find Next" context-menu item
bound to F3 — the same mechanism Phase 3 catalogued as P3-VIEW-7)*

### P5-VIEW-4 — Copy part of a report out
The user can select text anywhere in the report, or select the whole thing, and copy
it. What lands on the clipboard keeps its formatting as well as its plain text, so it
can be pasted into a document or an email and still look like a table.
*(`FlowDocumentView.Copy` writes both text and rich text; the Copy / Select All
context-menu items)*

### P5-VIEW-5 — Show or hide the detail beneath every summary line at once
Reports that summarise — a category with individual lines under it, a holding with its
separate purchase lots, a tax line fed by several categories — start collapsed, with a
small expander on each summary row. One button above the report opens or closes every
group at once, and it is disabled on reports that have nothing to expand.
*(`IReportWriter.StartExpandableRowGroup`, `FlowDocumentView.ToggleExpandAll`,
`FlowDocumentReportWriter.ExpandAll`/`CollapseAll`)*

### P5-VIEW-6 — Save a report as a web page
Any report on screen can be written out as an HTML file the user chooses the name of,
so it can be kept, mailed or opened in a browser. Charts, colour swatches and the
drill-down links do not survive the trip; the tables and the numbers do.
*(`AppCommands.CommandExportHtml` on the report's context menu,
`HtmlDocumentReportWriter`. See Open Questions 2 and 3 — for several reports this
produces a truncated or failed file, and the styling depends on fetching a stylesheet
from the internet.)*

### P5-VIEW-7 — Take a report's figures away as a spreadsheet
Two of the reports — the cash-flow report and the investment portfolio — offer an
Export button in their options panel that writes the figures to a comma-separated file
and then opens it, so the user can carry on in a spreadsheet. The other reports have no
such button.
*(`ReportsControl.ShowExportButton`, `Report.ExportReportAsCsv`, `CsvReportWriter`,
`CashFlowReport.Export`, `PortfolioReport.Export`. See Open Question 4.)*

### P5-VIEW-8 — Follow a figure back to the transactions behind it
Most numbers in most reports are live: clicking one takes the user to a transaction
list containing exactly the entries that produced it. A cash-flow cell opens that
category's entries for that column's period; a tax line opens the entries filed under
it; a payee or category in the future-bills report opens everything for that payee or
category; an account name in the account summary selects that account; a holding's name
opens every trade in it. This is the main way a user gets from "that number looks wrong"
to "here is why".
*(`IReport.OnMouseLeftButtonClick` + `IViewNavigator.ViewTransactions`/
`ViewTransactionsBySecurity`; `CashFlowReport`, `W2Report`, `PortfolioReport`,
`FutureBillsReport`, `AccountSummaryReport`)*

### P5-VIEW-9 — Drill from a summary report into a report about one slice of it
Clicking a slice of the net-worth pie, or a group in the portfolio summary, doesn't
just filter — it opens a fresh report about that slice alone, struck at the same date,
which itself has a chart and its own drill-downs. The user can keep going inwards.
*(`NetWorthReport.SecurityDrillDown`/`CashBalanceDrillDown`, `PortfolioReport.DrillDown`,
`MainWindow.OnReportDrillDown`/`OnReportCashDrillDown`; `SecurityGroup`, `AccountGroup`)*

### P5-VIEW-10 — Close a report and get back to work
A close box beside the report returns the user to the transaction register they came
from.
*(`FlowDocumentView.Closed` → `MainWindow.OnFlowDocumentViewClosed`)*

### P5-VIEW-11 — Come back to a report set up the way it was left
The settings a user chose for a report — its date, its year, its interval, its
groupings — are remembered per report alongside their data file, so reopening that
report later starts from the same terms rather than from defaults. Settings that can't
be written down are silently allowed to lapse rather than failing.
*(`Report.LoadState`/`SaveState`/`DelaySaveState` writing one XML file per report type
into a `Reports` folder next to the money file; `IReportState`. Note the deliberate
swallow-everything `try`/`catch` on both, and see Open Question 5.)*

### P5-VIEW-12 — See every figure in one currency
Where a report deals in money that could be in several currencies, the user picks a
currency in the options panel and every figure is converted into it, with a note at the
bottom stating the rate used. If the chosen currency isn't one they've set up, the
report says so in place rather than silently showing wrong numbers.
*(`Report.DefaultCurrency`/`SetDefaultCurrency`/`GetFormattedNormalizedAmount`/
`WriteTrailer`; `ReportsControl`'s currency picker)*

### P5-VIEW-13 — Know when the report was struck
Every report ends with a line saying which date it was generated for, so a printout or
an exported copy can't be mistaken for a current one.
*(`Report.WriteTrailer`)*

> **Not available: printing.** Nothing in the product prints a report. The only print
> affordance anywhere is in the attachment window (Phase 4's P4-ATT-8). See Open
> Question 6.

---

## 5.2 What am I worth?

### P5-NET-1 — See everything they own and owe, totalled, as at a date they choose
The user asks for a net-worth statement as at any date — today or years ago — and gets
one figure for their overall position, built up from named groups: cash in everyday
accounts, cash sitting in investment accounts, cash in tax-deferred and tax-free
accounts, their investments broken down by kind of holding, the assets they own
outright, the loans owed to them, and then their liabilities.
*(`NetWorthReport`)*

### P5-NET-2 — Have tax treatment kept visibly separate
Tax-deferred and tax-free holdings are listed under their own headings rather than
lumped in with ordinary taxable ones, and those headings only appear if the user
actually has accounts of that kind, so the report doesn't invent structure they don't
need.
*(`NetWorthReport.WriteSecurities` by `TaxStatus`)*

### P5-NET-3 — See the same picture as a pie
Beside the table is a pie chart of the same figures, colour-matched to a swatch on
every row, so the user can see at a glance what their wealth is mostly made of.
Hovering a slice names it and gives its value.
*(`AnimatingPieChart` embedded via `IReportWriter.WriteElement`)*

### P5-NET-4 — Click a slice or a row to go deeper
Clicking a slice of the pie, or the name of a group in the table, opens a report about
just that group — the individual accounts making up a cash total, or the individual
holdings making up a class of investment — struck at the same date.
*(`OnPieSliceClicked` → `SecurityDrillDown`/`CashBalanceDrillDown` → a `PortfolioReport`)*

### P5-NET-5 — See whether net worth has been going up or down over the years
Under the statement is a bar chart of the user's net worth on the same date in each
previous year, going back to their earliest transaction, filled in progressively as the
figures are computed so the report is readable immediately. Each bar names its date and
value on hover.
*(`PopulateHistoricalNetWorth` on a background thread appending to the chart's series;
`AnimatingBarChart`)*

### P5-NET-6 — Jump the whole report to a year they see in the history
Clicking a bar in the history chart re-strikes the entire net-worth statement as at that
year's date, so the user can move back through their history without touching the date
picker.
*(`OnBarChartColumnClicked`)*

### P5-NET-7 — Take the net-worth history away as a spreadsheet
Right-clicking the history chart offers to export just that series — year and value per
row — to a file of the user's choosing.
*(`ExportHistoryClick`)*

### P5-NET-8 — Be told when a holding hasn't been classified
If any holding has no kind recorded against it, the report marks the affected line and
says, in the report itself, where to go and fix it — rather than quietly filing it under
nothing.
*(the `SecurityType.None` asterisk and the closing note)*

---

## 5.3 What is each account worth?

### P5-ACCT-1 — See a one-line balance for every account, grouped by kind of account
The user gets a simple list of all their accounts with what each is worth as at a chosen
date, grouped under headings for each kind of account, with investment accounts valued
at market rather than just their cash. Accounts worth nothing are left out so the list
stays short.
*(`AccountSummaryReport`)*

### P5-ACCT-2 — See which currency each account is really in
Each row states the currency its figure is in, so a foreign-currency account isn't
mistaken for a home-currency one — and a grand total is only offered when every account
shares one currency.
*(`WriteAccountSummary`'s `various` handling. See Open Questions 7 and 8.)*

### P5-ACCT-3 — Click an account to go and work in it
Clicking an account's name in the report selects that account and opens its register.
*(`SelectAccount` → `MainWindow`'s accounts panel)*

---

## 5.4 Where did the money go?

### P5-FLOW-1 — See income, spending and investing side by side across time
The user gets a grid whose columns are periods and whose rows are their top-level
categories, split into Income, Expenses, Investments and an Unknown group for anything
uncategorised, with a total row underneath and a plain-language statement of the net
cash flow for the whole span.
*(`CashFlowReport`)*

### P5-FLOW-2 — Choose the span and the granularity
The user sets a start and an end date and chooses whether each column is a year or a
month; the report defaults to the last five years by year. Column headings follow the
user's financial year when they have set one.
*(`ReportsControl`'s start/end date rows and "By Years"/"By Month" interval;
`DatabaseSettings.FiscalYearStart`)*

### P5-FLOW-3 — Have categories roll up only as far as makes sense
Amounts roll up to the highest-level category they can without changing character — a
sub-category that is income stays under income even if its parent is treated as
investing — so a single parent category containing both fees and dividends doesn't
misreport either.
*(`TallyCategory`/`IsSimilarCategory`)*

### P5-FLOW-4 — Open out a group to see the categories inside it
Each of the four groups starts collapsed and can be opened to show the individual
categories that make it up, either one group at a time or all at once.
*(`StartExpandableRowGroup`, and P5-VIEW-5)*

### P5-FLOW-5 — Click any cell to see the entries behind it
Every figure that has entries behind it is clickable and opens exactly those entries —
including individual lines of itemised transactions, which are counted against their own
category rather than the whole transaction's.
*(`CashFlowCell.Data`, `OnMouseLeftButtonClick` → `IViewNavigator.ViewTransactions`)*

### P5-FLOW-6 — Have transfers, voids and asset accounts kept out of the picture
Money moved between the user's own accounts, voided entries and anything in an asset
account are excluded, so a transfer from savings to chequing doesn't read as income.
Sales tax is excluded from the category amount as well.
*(`GenerateColumn`'s filters, `Transaction.AmountMinusTax`)*

### P5-FLOW-7 — Take the grid away as a spreadsheet
An Export button writes the same grid out as a comma-separated file with expenses
flipped to positive numbers, and opens it.
*(`CashFlowReport.Export`/`GenerateCsvGroup`. See Open Question 9 — the exported file
is not the same report.)*

---

## 5.5 How are my investments doing?

### P5-PORT-1 — See everything they hold, what it cost and what it's worth now
The user gets a portfolio report listing every holding still owned, with quantity,
current price, market value, average unit cost, cost basis, gain or loss and gain as a
percentage — subtotalled per kind of holding and totalled overall.
*(`PortfolioReport`)*

### P5-PORT-2 — See taxable, tax-deferred and tax-free investments separately
The summary splits the portfolio three ways by tax treatment, each with its own subtotal,
and gains inside tax-free accounts are deliberately shown as nothing rather than as
taxable gains.
*(`WriteSummary` per `TaxStatus`)*

### P5-PORT-3 — Open a holding to see the individual lots behind it
Each holding is one summary line that opens to show every separate purchase still held —
the date it was acquired, what was paid, and what that particular lot is worth now — so
the user can see which lots carry the gain.
*(`WriteSecurities`'s expandable group over `SecurityPurchase` lots)*

### P5-PORT-4 — Value the portfolio as at any date
Changing the report date re-prices every holding using the price history the product has
for that date, so the user can ask what the portfolio was worth at the end of last year.
*(`CostBasisCalculator` + `StockQuoteCache.GetSecurityMarketPrice` at `ReportDate`)*

### P5-PORT-5 — Look at one account, one class of holding, or everything
The same report serves as the whole portfolio, as the portfolio of a single investment
account (reached from the register), and as a report about one class of holding reached
by drilling into the net-worth or portfolio pie — with the heading saying which of those
the user is looking at, and the single-account version also counting that account's cash.
*(`Account`/`SelectedGroup`/`AccountGroup` on `PortfolioReport`; also the in-register
portfolio view Phase 3 captured as P3-INV-4/P3-INV-5)*

### P5-PORT-6 — See a cash-balances report for a group of accounts
Drilling into a cash slice of the net-worth pie produces a plain list of the accounts in
that group with each one's cash balance and a pie of the same, so "where is my cash" is
one click from the net-worth statement.
*(`WriteCashBalanceSummary`. See Open Question 10.)*

### P5-PORT-7 — Be warned about sales the product can't account for
If a sale can't be matched to holdings the product knows about, the report says so
explicitly — naming the account, the holding, the units and the date — instead of
quietly producing wrong cost bases.
*(`CostBasisCalculator.GetPendingSales`, the "Pending Sales" section)*

### P5-PORT-8 — Jump from a holding to its trades
Clicking a holding's name anywhere in the report opens every transaction in that holding
across all accounts.
*(`OnReportCellMouseDown` → `IViewNavigator.ViewTransactionsBySecurity`)*

### P5-PORT-9 — Take the portfolio away as a spreadsheet
An Export button writes the whole report — summary and per-lot detail — to a
comma-separated file and opens it.
*(`PortfolioReport.Export` via `CsvReportWriter`)*

---

## 5.6 What do I need for my taxes?

> Boundary: this group covers the tax reports as **documents the user reads**. What a
> tax category means, how the product decides short versus long term, and the contents of
> the exported file are Phase 7.

### P5-TAX-1 — See a year's tax-relevant totals in one place
The user picks a financial year and gets every category they have associated with a tax
line, grouped by tax line and sub-totalled, plus the year's sales tax as its own figure
and a column separating amounts that came from tax-exempt holdings.
*(`TaxReport.GenerateCategories`, `GetSalesTax`)*

### P5-TAX-2 — See every investment sale in the year with its gain or loss
Below the categories, short-term and long-term sales are listed separately, each row
carrying the holding, quantity, date acquired (or "VARIOUS" for a consolidated lot),
acquisition price, cost basis, date sold, sale price, proceeds and the resulting gain,
with a total. Sales inside tax-deferred or tax-free accounts are excluded because they
aren't reportable.
*(`GenerateCapitalGains`, `CapitalGainsTaxCalculator`. See Open Question 11.)*

### P5-TAX-3 — Be shown sales the product can't compute a cost basis for
Sales with no known cost basis are listed in their own section rather than being folded
in with a made-up basis, so the user knows exactly what they have to look up themselves.
*(the "Capital Gains with Unknown Cost Basis" section)*

### P5-TAX-4 — Choose whether to report on everything or on investments only
A report-type choice limits the report to investment activity, for users whose only
tax-relevant records are trades.
*(`ReportTypeAllAccounts`/`ReportTypeInvestmentsOnly`. See Open Question 12.)*

### P5-TAX-5 — Choose how sales are consolidated
The user chooses whether sales are grouped by the date they were acquired or the date
they were sold, which changes how many rows a partial sale of a long-held position
produces.
*(the consolidation row in the options panel → `CapitalGainsTaxCalculator`)*

### P5-TAX-6 — Work to a financial year that isn't the calendar year
The year picker is built from the years the user actually has data for, labelled "FY"
where they have set a non-January financial year start, and the report follows that
definition throughout.
*(`SetStartDate`, `AddFiscalYearItems`, `Transactions.GetTaxYearRange`)*

### P5-TAX-7 — Hand the year's figures to tax software
A button at the top of the tax report writes the year out in the file format consumer
tax software imports, named after the year, with any failure reported plainly rather
than silently.
*(`CreateExportTxfButton`, `TxfExporter` — the file's contents are Phase 7)*

### P5-TAX-8 — See an estimated W-2 built from their own pay deposits
Separately, the user can ask the product to reconstruct what their pay records add up to
on each tax form, by reading the itemised lines of their paycheque deposits: each form
gets its own table, each tax line its own figure, and where several of their categories
feed one tax line the line opens to show them individually.
*(`W2Report`)*

### P5-TAX-9 — Be told plainly when no categories are associated with tax lines
Both tax reports, rather than showing an empty table, say in words that no categories
have been associated with tax lines and where to go to do it.
*(`W2Report.Generate`'s `empty` branch, `TaxReport.GenerateCategories`'s `null` branch)*

### P5-TAX-10 — Click a tax figure to see the pay entries behind it
Every figure in the W-2 report opens the transactions that produced it.
*(`AddHyperlink` → `IViewNavigator.ViewTransactions`)*

---

## 5.7 What's coming, and what needs my attention?

### P5-BILLS-1 — See what their regular bills will cost over the coming year
The product looks at the last five years of spending, works out by itself which
payee-and-category pairs the user pays on a regular rhythm, and lists the next twelve
months a month at a time with each predicted bill's date, payee, category and amount,
headed by the total it expects them to spend.
*(`FutureBillsReport`, `Payments.FindRecurringPayments`/`ComputeRecurrence`)*

### P5-BILLS-2 — Have the rhythm worked out rather than declared
The user doesn't set up a schedule. A payment is treated as recurring when its amounts
sit close enough to a straight line (allowing for inflation) and its dates are evenly
enough spaced, and the spacing is then named — weekly, fortnightly, monthly, quarterly
and so on. Payments that stopped happening are dropped rather than predicted forever.
*(`ComputeRecurrence`'s linear regression, standard-error thresholds and
`AllowedMissedPayments`. See Open Question 1.)*

### P5-BILLS-3 — Be told plainly when nothing recurring was found
If the product can't find a rhythm in the data it says so in one line instead of showing
twelve empty months.
*(the `total == 0` branch)*

### P5-BILLS-4 — Click a predicted bill to see its history
The payee and category in each predicted row open that payee's or category's
transactions, so the user can check the prediction against what actually happened.
*(`PayeeSelected`/`CategorySelected`)*

### P5-ATTN-1 — See everything downloaded but not yet approved, across all accounts
The user gets one list of every entry that arrived from a download and hasn't been
accepted yet, grouped by account with its date, payee and amount, memo on its own line,
and a count at the end. Closed accounts are left out.
*(`UnacceptedReport`)*

---

## 5.8 Will my money last?

### P5-PLAN-1 — Model whether their savings will see them through retirement
The user enters their situation — current age, a spouse's age, filing status, which
state they pay tax in, the age they intend to retire, the age to plan to, the income
they want each year, an expected rate of return, an inflation rate and an assumed rate
at which tax brackets move — and the product simulates every year from now to the end,
starting from what they actually hold today.
*(`RetirementPlanReport`, `RetirementControl` — the entry panel is Phase 3's P3-PANEL-3)*

### P5-PLAN-2 — See the answer as three figures before any chart
The plan opens with what's left at the planning age, split into taxable, tax-deferred
and tax-free, and the total tax paid across the whole retirement — the two numbers the
whole exercise exists to produce.
*(`Simulation.Render`'s summary table)*

### P5-PLAN-3 — Watch the simulation run rather than stare at a frozen window
Because the simulation is slow, the report says it is running and fills itself in when
the figures are ready, leaving the rest of the product usable meanwhile.
*(`Generate`'s "Running simulation..." placeholder and the background `Simulate`)*

### P5-PLAN-4 — See net worth, income and tax year by year as charts
Three charts follow the summary: assets by tax treatment at each age, where each year's
income was drawn from (including the extra that had to be withdrawn purely to pay the
tax on the withdrawal), and the tax paid to produce that income split into federal,
state and capital-gains. Each carries its own legend, and hovering a bar names the age
and amount.
*(`Render`'s three `AnimatingBarChart`s and `CreateLegend`)*

### P5-PLAN-5 — Switch between stacked and side-by-side bars
A single toggle changes every chart in the plan between stacking the parts of each year
on top of each other and setting them side by side, without re-running the simulation.
*(`OnStackedBarsChanged`)*

### P5-PLAN-6 — Ask what converting to a tax-free account would do
Where the user has tax-deferred savings, they can model spreading a conversion into
tax-free savings over a number of years, and a fourth chart runs the whole simulation
sixteen times — no conversion, then one year through fifteen — so they can see the
number of years that leaves them best off and what each costs in tax.
*(`TaxDeferredStrategyRoth`, `RunRothSimulation`)*

### P5-PLAN-7 — Have the rules of retirement applied for them
The simulation takes required minimum distributions once the user reaches the age their
birth year obliges, taxes the taxable share of their social security, draws income in a
deliberate order — dividends, required distributions, social security, taxable savings,
then tax-deferred, leaving tax-free savings until last — and sells the highest-cost lots
first to limit capital gains. The user isn't asked to understand any of that.
*(`RetirementFunds`, `GetMinimumDistribution`, `CalcSocialSecurityTax`,
`SellTaxableAmount`, `CreateSortedHoldings`. See Open Questions 13, 14 and 15.)*

### P5-PLAN-8 — Record social security at more than one claiming age
The user can record what they'd receive at 62, at 67 and at 70, and switching the
claiming age brings back the amount they recorded for it rather than making them retype
it; a spousal benefit is estimated from the primary one when they haven't a figure of
their own.
*(`SaveSocialSecurityAmount`/`FindSocialSecurityAmount`, `ComputeSpousalSocialSecurity`)*

---

## 5.9 The picture beside the numbers

> Phase 2 captured the chart strip as an affordance (P2-PANE-1 … P2-PANE-5). This group
> captures what each of those pictures actually says.

### P5-TREND-1 — Watch a balance rise and fall over the entries they're looking at
Whatever list of entries the user has in front of them, a shaded area graph beneath it
plots the running total across those entries in date order. What is being totalled
follows the list: an account's running balance, a category's spending, a payee's total,
or — for a list of one holding — that holding's value.
*(`TransactionGraphGenerator`, `AreaChart`, `TrendGraph`)*

### P5-TREND-2 — See spending trend upwards rather than downwards
For credit accounts and expense categories, where every entry is negative, the graph is
flipped so that "more" is up — matching what the user means by spending more.
*(`IGraphGenerator.IsFlipped`)*

### P5-TREND-3 — See what an investment account was really worth on every day of its life
For a brokerage or retirement account the graph doesn't plot cash: it re-prices every
holding for every single day from the account's first transaction onwards, applies stock
splits as their dates pass, adds the cash balance, and draws the result — so the shape of
the line is the shape of the market, not of the deposits.
*(`BrokerageAccountGraphGenerator`, `StockQuoteCache`)*

### P5-TREND-4 — See a holding's own price history
Looking at a single holding, a separate graph shows that security's closing price over
time, independent of how much of it the user owns.
*(`SecurityGraphGenerator`, the "Stock" tab)*

### P5-TREND-5 — Point at the graph to read off a value
Moving the pointer over the graph puts a marker on the nearest point and shows its date
and value; the point under the marker becomes the graph's selection.
*(`AreaChart.UpdatePointer`, `Selected`)*

### P5-TREND-6 — Move the graph through time
The graph can be set to the year to date, to the whole history, or stepped forwards and
backwards one period at a time; the period itself can be zoomed in or out from days
through to years, with keyboard shortcuts for stepping and zooming. A custom start and
end date can also be typed in.
*(`TrendGraph`'s Year to date / Show all / Next / Previous / Zoom in / Zoom out /
Custom range commands; the custom range window is Phase 4's P4-REPORT-1)*

### P5-TREND-7 — Compare this period with the ones before it
The user can add further series to the graph, each drawing the same measure over the
immediately preceding period of the same length in a different colour, so this year's
spending can be laid over last year's and the year before.
*(`OnAddSeries`/`OnRemoveSeries`, `TrendGraphSeries`, the eight themed series colours)*

### P5-TREND-8 — Take the plotted figures away
A menu item on the graph exports the plotted series to a spreadsheet file and opens it.
*(`OnExportData` → `ChartData.Export`. See Open Question 16.)*

### P5-HIST-1 — See the same measure bucketed by year, month or day
A history chart shows whatever the user is currently looking at as bars — one per year,
month or day, chosen from a dropdown on the chart — over the last couple of dozen
periods, with the bars coloured from the category's or payee's own colour.
*(`HistoryBarChart`, `HistoryChartColumn`)*

### P5-HIST-2 — See whether the trend is up or down
A regression line is computed across the bars so the user can see the direction of
travel rather than judging it by eye.
*(`ComputeLinearRegression`. See Open Question 17.)*

### P5-HIST-3 — Have spending shown as positive bars
When most of the periods are negative — as they are for any expense — every bar is
flipped so the chart reads as "how much", not "how far below zero".
*(`ComputeInversion`/`InvertColumns`)*

### P5-HIST-4 — Click a bar to narrow the list to that period
Clicking a year, month or day filters the transaction list above to exactly that period,
which is the fastest route from "that month looks expensive" to the entries that made it
so.
*(`SelectionChanged` → `TransactionsView.AddHistoryFilter`; Phase 3's P3-FIND-5)*

### P5-HIST-5 — Turn the chart on its side, or take it away
A right-click offers to rotate the bars between vertical and horizontal, and to export
the plotted values to a spreadsheet.
*(`Rotate`, `OnExport`)*

### P5-PIE-1 — See what a period's spending, or income, was mostly made of
Two pie charts summarise the entries currently listed — one of expenses, one of income —
by top-level category, largest first, with the net total stated beside them.
*(`CategoryChart` with `CategoryType` Expense / Income)*

### P5-PIE-2 — Read a legend with every category, its colour and its total
Beside each pie is a legend listing every slice with its colour, name and amount.
*(`ChartLegend`)*

### P5-PIE-3 — Hide a category to see the rest more clearly
Clicking a colour swatch in the legend takes that category out of the pie and out of the
stated total, so one dominant category can be set aside to see what's underneath.
*(`ChartLegend.Toggled` → `CategoryChart.FilterChartData`)*

### P5-PIE-4 — Click into a category to see its sub-categories
Clicking a slice, or its legend entry, re-draws the pie for the categories inside that
one, so the user can work down a category tree visually.
*(`PieSliceClicked`/`Selected` → `CategoryChart.Selection` → `MainWindow.
PieChartSelectionChanged`)*

### P5-PIE-5 — Have transfers and unassigned amounts shown honestly
Money moved to or from the user's own accounts appears as its own slice rather than
being hidden, and the unallocated remainder of an itemised transaction appears as
"Unassigned" rather than being dropped.
*(`Tally`'s `transferredIn`/`transferredOut`/`unassigned` buckets. See Open Question 18.)*

### P5-PIE-6 — Take the breakdown away as a spreadsheet
A right-click exports the pie's categories and amounts to a spreadsheet file.
*(`CategoryChart.OnExport` → `ChartData.Export`)*

### P5-LOAN-1 — See how much of a loan went on interest, year by year
For a loan, a chart shows each year's payments split into principal and interest as two
bars, with the totals for the whole loan stated in the corner — the single most
persuasive picture of what borrowing actually cost.
*(`LoanChart`, `CumulatePayementsPerYear`)*

### P5-RENT-1 — See a rental property's income against its costs, year by year
For rental property, a chart shows each year as an income bar beside an expenses bar,
with the expenses bar itself divided into taxes, repairs, maintenance, management and
interest, each segment naming itself and its amount.
*(`RentalChart`, `RentalChartColumn`, `RentalData`)*

### P5-RENT-2 — Re-stack the cost breakdown to compare one cost class across years
Clicking the chart rotates which class of expense sits at the bottom of every bar, so
whichever cost the user is interested in can be brought to a common baseline and compared
across years.
*(`RentalChart.MaingGrid_MouseLeftButtonUp` → `SetExpensesDistribution`)*

---

## 5.10 What every chart does the same way

### P5-CHART-1 — Point at any part of a chart and be told what it is
Every chart in the product — pie, bar and area — shows a tooltip after a short hover
naming the thing under the pointer and its value, and each report supplies its own
wording rather than showing a raw number.
*(`ToolTipGenerator` on `AnimatingPieChart`, `AnimatingBarChart` and `AreaChart`)*

### P5-CHART-2 — Have charts animate rather than jump
Slices grow into place and change colour smoothly, and bars ripple up rather than
appearing, so a chart that redraws after a settings change is followed by eye rather than
re-read from scratch.
*(the animation-duration properties on both animating charts)*

### P5-CHART-3 — Have negative values drawn sensibly
A pie draws a negative amount by its size and tells the truth about its sign in the
tooltip, rather than refusing to draw it or drawing nothing.
*(`AnimatingPieChart.UpdateChart`)*

### P5-CHART-4 — Have colours chosen for them, consistently
Categories keep a stable colour derived from their name, so the same category is the
same colour everywhere it appears; where a report has no colour to offer, one is picked
so that slices remain distinguishable, and legend text flips between black and white to
stay readable on its swatch.
*(`CategoryData.GetColorFromCategoryName`, `ColorAndBrushGenerator`, the random-colour
helpers in the reports, `ChartLegend`'s luminance test. See Open Question 20.)*

### P5-CHART-5 — Have charts keep up with the data without being asked
Charts redraw themselves when the underlying list changes, when the window is resized
and when their tab is brought to the front, and defer work while they are not visible.
*(`IsVisibleChanged` + `DelayedActions` in every chart; `MainWindow.SetChartsDirty`)*

---

## Phase 5 coverage checklist

Every file in `Reports/` and `Charts/`, plus the two `Views/` files Phase 3 deferred
here. "Not user-facing" entries are plumbing, shared bases or dead code the user never
perceives.

### `Reports/` — reports

| File | Class | Status |
|---|---|---|
| `NetWorthReport.cs` | `NetWorthReport` | P5-NET-1…P5-NET-8, P5-VIEW-9 — see Open Questions 21, 22 |
| `NetWorthReport.cs` | `AccountGroup` | P5-NET-4, P5-PORT-6 — the "these accounts, on this date" bundle a cash drill-down carries |
| `AccountSummaryReport.cs` | `AccountSummaryReport` | P5-ACCT-1…P5-ACCT-3 — see Open Questions 7, 8 |
| `CashFlowReport.cs` | `CashFlowReport` | P5-FLOW-1…P5-FLOW-7 |
| `CashFlowReport.cs` | `CashFlowCell`, `CashFlowColumns` | P5-FLOW-1, P5-FLOW-5 — the per-cell figure plus the entries behind it |
| `PortfolioReport.cs` | `PortfolioReport` | P5-PORT-1…P5-PORT-9, and the in-register portfolio of P3-INV-4/5 — see Open Questions 10, 23, 24 |
| `TaxReport.cs` | `TaxReport` | P5-TAX-1…P5-TAX-7 — see Open Questions 11, 12 |
| `W2Report.cs` | `W2Report` (namespace `Walkabout.Taxes`) | P5-TAX-8, P5-TAX-9, P5-TAX-10 |
| `FutureBillsReport.cs` | `FutureBillsReport` | P5-BILLS-1…P5-BILLS-4 |
| `UnacceptedReport.cs` | `UnacceptedReport` | P5-ATTN-1 |
| `RetirementPlan.cs` | `RetirementPlanReport` | P5-PLAN-1…P5-PLAN-8 |
| `RetirementPlan.cs` | `Simulation` (nested) | P5-PLAN-2…P5-PLAN-7 — the year-by-year model and the charts it renders |
| `RetirementPlan.cs` | `RetirementFunds` (nested) | P5-PLAN-7 — the simulated pot of money and the tax rules applied to it; see Open Questions 13, 14, 15 |
| `RetirementPlan.cs` | `RetirementPlanState`, `SocialSecurityAmount` | P5-VIEW-11, P5-PLAN-8 |
| `RetirementPlan.cs` | `TableHelper` | **Not user-facing** — a two-cell row helper for the summary table |
| `Reports.cs` | `Report` (abstract base) | P5-VIEW-11, P5-VIEW-12, P5-VIEW-13, P5-VIEW-7 — state persistence, currency normalisation, the trailer and the shared CSV-export flow; see Open Question 25 |
| `IReport.cs` | `IReport`, `IReportState`, `IReportWriter`, `StateSource` | **Not user-facing** — the contract every report and every output format implements |
| `IReport.cs` | `ReportInterval` (enum) | **Not user-facing here** — its only consumer is `ReportRangeDialog`'s interval combo (Phase 4's P4-REPORT-2), which that dialog's only caller hides. No report uses it; the cash-flow report has its own two strings. See Open Question 32 |
| `IReport.cs` | `NullReportWriter` | **Not user-facing** — lets a report be run purely to compute totals; used by the future-bills report to get its headline figure before writing anything |

### `Reports/` — output formats

| File | Class | Status |
|---|---|---|
| `FlowDocumentReportWriter.cs` | `FlowDocumentReportWriter` | P5-VIEW-1, P5-VIEW-5 — the on-screen format, including the expandable groups and a manual column-width fix-up |
| `FlowDocumentReportWriter.cs` | `NestedTableState`, `ColumnWidthExtensions` | **Not user-facing** — nested-table bookkeeping and per-column min/max width carriers |
| `HtmlDocumentReportWriter.cs` | `HtmlDocumentReportWriter` | P5-VIEW-6 — see Open Questions 2, 3 |
| `CsvReportWriter.cs` | `CsvReportWriter` | P5-VIEW-7 (portfolio only — the cash-flow report writes its own CSV by hand) |

### `Charts/`

| File | Status |
|---|---|
| `AnimatingPieChart.xaml` + `.xaml.cs` (`LovettSoftware.Charts`) | P5-NET-3, P5-PORT-1, P5-PIE-1, P5-CHART-1…P5-CHART-3 |
| `AnimatingBarChart.xaml` + `.xaml.cs` (`LovettSoftware.Charts`) | P5-NET-5, P5-PLAN-4, P5-PLAN-5, P5-LOAN-1, P5-HIST-1, P5-HIST-5, P5-CHART-1, P5-CHART-2 — see Open Question 26 |
| `AreaChart.cs` (`LovettSoftware.Charts`) | P5-TREND-1…P5-TREND-7 — the shaded area renderer behind the trend graph |
| `CategoryChart.xaml` + `.xaml.cs` | P5-PIE-1…P5-PIE-6 — see Open Questions 18, 20 |
| `CategoryData.cs` | P5-PIE-1, P5-CHART-4 — the per-slice view model and the name-derived colour |
| `ChartData.cs` (`ChartData`, `ChartDataSeries`, `ChartDataValue`) | the shape every chart consumes; `ChartData.Export` is P5-TREND-8, P5-HIST-5, P5-PIE-6 — see Open Question 16 |
| `ChartData.cs` (`ChartCategory`) | **Not user-facing on its own** — the per-series name and colour the trend graph attaches and the area chart reads back when drawing each band and its legend label |
| `ChartLegend.xaml` + `.xaml.cs` | P5-PIE-2, P5-PIE-3, P5-PIE-4 |
| `HistoryBarChart.xaml` + `.xaml.cs` (`HistoryBarChart`, `HistoryChartColumn`, `HistoryDataValue`, `ColumnLabel`, `HistoryRange`) | P5-HIST-1…P5-HIST-5 — see Open Question 17 |
| `LoanChart.xaml` + `.xaml.cs` | P5-LOAN-1; its `OnColumnClicked` and `OnColumnHover` are **empty**, one with a "todo: any kind of drill down or pivot possible here?" comment — the loan chart is the one chart you cannot click into |
| `RentalChart.xaml` + `.xaml.cs` | P5-RENT-1, P5-RENT-2 |
| `RentalChartColumn.xaml` + `.xaml.cs` | P5-RENT-1, P5-RENT-2 — one year's income bar and segmented expense bar |
| `RentalData.cs` | P5-RENT-1 — one year's income and its five expense classes |
| `FormattingConverter.cs` (`NumberConverter`) | **Not user-facing** — formats the trend graph's axis labels as currency without a symbol |
| `Styles.cs` (`StyleResources`) | **Dead** — loads a `Charts/styles.xaml` that does not exist, and nothing calls it. See Open Question 19 |
| `Charts/Icons/Area.png`, `Table.png` | **Dead** — declared as resources in the project file, referenced by no code or markup; they look like the icons of a chart-type switcher that no longer exists |
| `Charts/Icons/grab.cur`, `grabbing.cur` | **Dead** — embedded cursors referenced nowhere; no chart supports dragging |
| `Charts/Icons/Excel.png` | **Dead copy** — the export button actually uses `Icons/Excel.png` at the project root; this second copy is referenced by nothing |

### `Views/` files deferred here by Phase 3

| File / member | Status |
|---|---|
| `GraphGenerators.cs` — `TransactionGraphGenerator` | P5-TREND-1, P5-TREND-2 |
| `GraphGenerators.cs` — `BrokerageAccountGraphGenerator` | P5-TREND-3 — the day-by-day historical market value, including stock splits and cash-in-lieu rounding |
| `GraphGenerators.cs` — `SecurityGraphGenerator` | P5-TREND-4 |
| `FlowDocumentView` — the report-hosting path | P5-VIEW-1, P5-VIEW-3…P5-VIEW-6, P5-VIEW-10 |
| `FlowDocumentView.ViewState` / `ReportViewState` | P5-VIEW-11 in intent only — it reconstructs a report by type and re-applies its state, but `DeserializeViewState` returns a bare `ViewState` and `ReportState` is marked not-to-be-serialized, so nothing survives a restart this way; the per-report XML files of P5-VIEW-11 are the mechanism that works. See Open Question 5 |
| `FlowDocumentView.AddControl` / `AddWidget` | P5-TAX-7 — how a report puts its own button into the strip above the document |
| `FlowDocumentView.Commit`, `Caption`, `SelectedRow`, `ActivateView`, `BeforeViewStateChanged`, `IsQueryPanelDisplayed` | **Not user-facing** — interface members implemented as no-ops or plain storage; `Caption` returns empty so the report view contributes no window title, and `BeforeViewStateChanged` is declared but never raised |

### Supporting files owned by other phases

| File | Status |
|---|---|
| `View Selectors/ReportsControl.*` | Phase 3's P3-PANEL-1/P3-PANEL-2; what each report shows in it is recorded per report above. See Open Question 27 |
| `View Selectors/RetirementControl.*` | Phase 3's P3-PANEL-3/P3-PANEL-4; what the plan does with those answers is P5-PLAN-* |
| `Controls/TrendGraph.*` | P5-TREND-1…P5-TREND-8 for its behaviour; the control itself is `Controls/` and was reached through Phase 2's P2-PANE-1/2 |
| `MyMoney.Business/Payments.cs` | P5-BILLS-1, P5-BILLS-2 — **not covered by Phase 1**, which read only `Money.cs` and `Money_Loans.cs`. See Open Question 1 |
| `Setup/ChangeInfoReport.cs` | **Out of scope here** — a report-shaped page rendered in the same viewer, belonging to Phase 2's P2-HELP-4 / Phase 8 |
| `Taxes/` (`TaxCategoryCollection`, `CapitalGainsTaxCalculator`, `TxfExporter`, `FederalTaxes`, `StateTaxes`) | **Phase 7** — read only far enough to describe what the tax and retirement reports show |

---

## Open questions from Phase 5

Things a human should double-check, because the call was a judgement rather than
obvious from the code — and, where noted, because they look like real defects.

1. **Setting a category's frequency removes it from the future-bills report entirely.**
   `Payments.ComputeRecurrence` starts `this.Frequency = this.Category.Frequency;` and
   then does all of its work inside `if (this.Frequency == CalendarRange.None)`. Every
   other path falls through to the final `return false`. So a category the user has
   explicitly marked as monthly or quarterly (Phase 1's P1-BUDGET-2) is *excluded* from
   the report, while one left unmarked is included if the maths says so — the exact
   opposite of what the guard two lines above (`if (Transactions.Count < 3 && Frequency
   == None) return false;`) implies was intended. P5-BILLS-2 describes the detection that
   actually runs. Also in the same method: the "remove outliers and recompute the cleaner
   standard deviation" block recomputes from `this.Transactions`, the *unfiltered* list,
   so it always produces exactly the same numbers and the outlier removal has no effect
   on the result. This whole file sits in `MyMoney.Business` and was not in Phase 1's
   scope (which read only `Money.cs` and `Money_Loans.cs`), so this is the first phase to
   have looked at it.

2. **"Export HTML" does not wait for the report to finish writing.**
   `FlowDocumentView.GenerateHtmlReport` calls `this.report.Generate(htmlWriter)` without
   awaiting the returned task, then immediately calls `Close()` and lets the `using` block
   dispose the underlying writer. Four of the nine reports — net worth, account summary,
   portfolio and the retirement plan — do real asynchronous work inside `Generate` (they
   await stock prices), so the file is closed while they are still writing to it. The
   identical bug was already found and fixed in `PortfolioReport.Export`, which now
   carries an explicit comment explaining why it blocks instead (*"rather than discarding
   the Task, which let the `using` block close the writer before InternalGenerate finished
   writing to it"*) — the viewer's own copy of the pattern was not fixed. P5-VIEW-6
   describes the intent. Worth exporting a net-worth report by hand to see what actually
   lands on disk.

3. **An exported report needs the internet to look right, and loses its charts.**
   `HtmlDocumentReportWriter`'s `<head>` links Bootstrap from a CDN, so an exported file
   opened offline is unstyled. `WriteElement` is a no-op, so every chart, and every colour
   swatch beside a net-worth row, is simply absent; `WriteHyperlink` writes a plain `span`
   with a comment saying *"not supported since there is no where to link to"*; and the
   expandable groups are no-ops, so the detail rows of the cash-flow, W-2 and portfolio
   reports come out shifted one column left (the expander cell the on-screen writer
   inserts for them is never written). A redesign that wants a shareable report should
   treat this as a rewrite, not a tweak.

4. **Only two of the nine reports can be exported to a spreadsheet, and the rest don't say
   so.** `ShowExportButton` is called only by `CashFlowReport` and `PortfolioReport`.
   Every other report inherits `Report.Export`, which throws `NotImplementedException`,
   or overrides it to throw explicitly (`FutureBillsReport`, `UnacceptedReport`,
   `W2Report`). Because the button is never shown for them the exception is unreachable
   today — but see Open Question 21 for the one place where that is a single character
   away from being reachable.

5. **A report is not restored on restart, despite two mechanisms that look like they
   should.** `Report.LoadState`/`SaveState` do work — one XML file per report type in a
   `Reports` folder beside the money file — and are what P5-VIEW-11 describes. The other
   mechanism, `FlowDocumentView.ViewState`/`ReportViewState`, is a dead end: the class
   carries a `// Todo: serialize this if we can, so restart can show the same report`
   comment, its only property is `[XmlIgnore]`, and `DeserializeViewState` returns a bare
   `ViewState`. So the app never reopens on the report the user was reading, which is the
   same "most surfaces don't remember where the user was" problem Phase 3 raised as its
   Open Question 2. Additionally, `PortfolioReport`'s state holds `Predicate<Account>`
   values that cannot be serialized at all — acknowledged in a comment in the code,
   which relies on `SaveState`'s catch-everything to swallow the failure.

6. **Nothing prints a report.** The only `Print` command binding in the product is in
   `AttachmentDialog`. There is no print menu item, button, keyboard shortcut or
   context-menu entry for reports, and `FlowDocumentView` adds none. (The underlying WPF
   document viewer has its own built-in print handling, so Ctrl+P with focus inside the
   document may do something, but the product neither advertises nor tests it.) For a
   personal-finance product whose reports are mostly things you hand to an accountant,
   this is a conspicuous gap and a deliberate decision for the redesign.

7. **The account-summary report prints every balance with the local currency symbol.**
   `AccountSummaryReport.WriteRow` formats with `balance.ToString("C2")`, which uses the
   machine's current culture, while printing the account's real currency code in a
   separate column — so a euro account reads `$1,234.00  EUR`. Every other report routes
   money through `Report.GetFormattedNormalizedAmount`, which uses the chosen currency's
   culture. P5-ACCT-2 describes the currency column, not the symbol.

8. **A mixed-currency account type silently contributes nothing to the total.** In
   `WriteAccountSummary`, if two accounts of the same kind report different currency
   symbols the local `various` flag is set and the method returns `0` instead of its real
   subtotal — so that whole account type drops out of the grand total with no warning,
   and because `commonSymbol` is a field shared across types, one odd account anywhere
   can suppress the "All Accounts" row for everything. Worth deciding what the report
   *should* say; it currently says less than it knows.

9. **The cash-flow CSV is not the cash-flow report.** `Export` writes three groups —
   Income, Expenses, Investments — and omits the Unknown group and the Total row that the
   on-screen report shows, and it flips the sign of expenses. So the exported file and
   the report on screen do not agree, and the exported file does not balance. P5-FLOW-7
   records that an export exists without claiming it matches.

10. **The cash-balances drill-down writes a malformed table.**
    `PortfolioReport.WriteSummaryRow` puts its final `writer.EndCell()` *inside*
    `if (col3 != null)`, so whenever the third column is empty — which is every row of the
    cash-balances report, since `WriteCashBalanceSummary` passes `null` — a cell is opened
    and never closed. It is survivable in the on-screen writer (which treats end-of-row as
    end-of-cell) but it is plainly not what was meant, and the CSV and HTML writers both
    count cells. P5-PORT-6 describes the intent.

11. **The tax report closes one table too many when there are no long-term sales.**
    `GenerateCapitalGains` ends the short-term table inside its own `if`, opens the
    long-term table inside the next `if`, and then calls `writer.EndTable()`
    unconditionally at the end. A tax year with short-term sales but no long-term ones
    therefore ends a table that isn't open; a year with neither ends one that was never
    started. Harmless in the on-screen writer, which resets its state, but it is an
    unbalanced document and the HTML writer counts depth and throws *"You closed too many
    tags"*. Combined with Open Question 2, "export this tax report as a web page" is
    unlikely to work.

12. **Restoring the tax report's saved settings puts the wrong value in the dropdown.**
    `TaxReport.ApplyState` sets the *report type* combo (All Accounts / Investments Only)
    from `this.consolidateOnDateSold`, the *consolidation* flag — `box.SelectedIndex =
    this.consolidateOnDateSold ? 0 : 1;`. It should be reading `investmentsOnly`, and the
    index is inverted relative to the order the items are added. So reopening the tax
    report can show a report type the user didn't choose, and the consolidation setting it
    did restore isn't reflected in its own combo at all. P5-TAX-4 and P5-TAX-5 describe
    the settings, not their restoration.

13. **The retirement plan's required-minimum-distribution age and its distribution table
    disagree.** `Simulation` computes `rmdAge` as 73 or 75 from the user's birth year and
    uses it to decide *when* distributions start, but the table that decides *how much*
    lives on `RetirementFunds`, whose own `RmdAge` field is hard-coded to 75 and never set
    from the simulation's value. For anyone born before 1960 the simulation therefore
    triggers a distribution at 73 and then computes it as zero, for two years. P5-PLAN-7
    describes the intent.

14. **A state-tax figure in the retirement plan is computed from the wrong amount.**
    In `PayIncomeTaxRecursively`'s tax-deferred branch, the federal tax is computed on
    `amount` (the extra withdrawal being made) but the state tax on the same line is
    computed on `income` — the original parameter — so the state tax charged on a
    gross-up withdrawal is wrong in both directions depending on the sizes involved. One
    identifier; adjacent lines. Given the whole point of the Roth comparison (P5-PLAN-6)
    is comparing total tax paid, this is worth checking before anyone acts on the answer.

15. **Closed brokerage and money-market accounts are still counted in the retirement
    plan.** `CalculatePortfolioBalance` tests
    `!account.IsClosed && account.Type == Retirement || account.Type == Brokerage ||
    account.Type == MoneyMarket` — which, by C# precedence, is
    `(!closed && Retirement) || Brokerage || MoneyMarket`. The closed check only applies
    to retirement accounts. The same expression appears twice in the method, once for cash
    and once for holdings. So a closed brokerage account's balance is included in the
    money the plan assumes the user has.

16. **Exporting a multi-series chart can fail outright.** `ChartData.Export` takes its row
    count from the *first* series (`this.Series[0].Values.Count`) and then indexes every
    other series with the same index. The trend graph's comparison series (P5-TREND-7)
    are built from different date ranges and routinely have different lengths, so
    exporting a trend graph with more than one series is an index-out-of-range exception
    with nothing catching it. (The same method also calls `Path.GetTempFileName()` and
    then appends `.csv` to the name, so it leaves an orphaned empty temp file behind on
    every export.)

17. **The history chart's trend line never skips the incomplete first period.**
    `ComputeLinearRegression` reads `if ((c == last || c == last) && ...)` — the same
    comparison twice, where the local `first`, assigned two lines earlier and otherwise
    unused, was obviously meant. The stated intent in its own comment is to ignore the
    first *and* last bucket when they look short on data; only the last one is ignored.
    A partial first year therefore drags the trend line. P5-HIST-2 describes the feature,
    not the arithmetic.

18. **Transfers inside an itemised transaction are charted with the wrong amount.** In
    both `CategoryChart.Tally` and its must-be-kept-in-sync twin `ComputeNetAmount`, the
    split loop passes `amount` — the running unallocated remainder — instead of
    `subtotal` when the split line is a transfer, while every other branch passes the
    line's own figure. So a transaction split between a transfer and ordinary categories
    contributes the wrong number to the transfers slice. The two methods also carry
    explicit "ALERT: this method has to be kept in sync with…" comments in both
    directions, which is itself a redesign signal.

19. **A chart style sheet is loaded that does not exist.** `Charts/Styles.cs` builds a
    `ResourceDictionary` from `pack://application:,,,/MyMoney;component/Charts/styles.xaml`
    inside a `try`/`catch` that comments the failure as *"not a WPF app"* — but there is
    no `Charts/styles.xaml` anywhere in the repository, so the dictionary is always null
    and `GetResource` would throw on any call. Nothing calls it. Pure dead code, listed
    because its presence implies chart styling lives somewhere it does not.

20. **Drawing the expenses pie chart can make the data file dirty.**
    `CategoryChart.Tally` assigns `c.Root.Color = cd.Color.ToString()` for any category
    that has no colour of its own — a write to the persisted model performed as a side
    effect of rendering a chart. The user can therefore be told they have unsaved changes
    purely because they looked at a chart. Whether persisting the generated colours is
    desirable is a design decision (it does make P5-CHART-4's "same category, same colour"
    stick across sessions); doing it from a draw path is not.

21. **The net-worth report's export hook is subscribed with the wrong operator, which is
    the only thing keeping it from crashing.** `NetWorthReport.Register()` ends with
    `this.panel.ReportExport -= this.OnReportExport;` — an unsubscribe where every other
    report writes `+=` — and `OnReportExport` is a one-line
    `throw new NotImplementedException();`. The report also never calls
    `ShowExportButton()`, so the button isn't there to press. Two independent accidents
    are hiding one unfinished feature. Fixing either one alone would crash the app.

22. **The net-worth pie shows liabilities as positive slices while saying it doesn't.**
    The code carries the comment *"liabilities are not included in the pie chart because
    that would be confusing"* and indeed omits credit-card balances — but the very next
    call, `WriteLoanAccountRows(writer, data, color, true)`, adds every liability loan to
    the same pie data with `Math.Abs(balance)`. So a mortgage appears as a positive slice
    of what the chart presents as net worth. Also in the same report: asset and loan rows
    are added to the pie with no drill-down attached, so those slices are the only ones
    that do nothing when clicked, with no indication of the difference.

23. **The portfolio report's "as of" line is a day earlier than its heading.**
    `InternalGenerate` writes the heading with `ReportDate` and then, whenever that isn't
    today, a sub-heading reading *"As of "* plus `ReportDate.AddDays(-1)`. The two lines
    of the same report disagree by a day. Whether the holdings are as at the start or the
    end of the chosen date is a real question a user would ask, and the report answers it
    twice, differently.

24. **A date picker the portfolio report can embed in its own heading is unreachable.**
    `InternalGenerate` builds an inline `DatePicker` (with an automation name, suggesting
    it was once tested) under `if (this.flowwriter != null && this.panel == null)` — but
    `panel` is assigned in `OnSiteChanged`, which runs whenever the report is given a
    service provider, i.e. always. Dead in practice. The date is set from the options
    panel instead (P5-VIEW-2).

25. **The shared CSV export reports failures under the wrong name.**
    `Report.ExportReportAsCsv` offers a `.csv` save dialog, and on failure shows a message
    box titled *"Error Exporting .txf"* — copied from the tax report's TurboTax export
    next to it. A user exporting the cash-flow report to a spreadsheet who hits an error
    is told about a file format they've never heard of. It also opens the finished file
    via `InternetExplorer.OpenUrl`, which is a differently-named helper for the same shell
    "open this file" the charts use via `NativeMethods.ShellExecute` — two ways of doing
    one thing.

26. **The bar chart throws a bare exception on data it doesn't like.**
    `AnimatingBarChart.OnDataChanged` throws `new Exception(...)` if the series it is
    given have different lengths or different labels. Every current caller happens to
    satisfy it, but this is a UI control throwing an unhandled, untyped exception from a
    property assignment — the same class of thing as the crash Phase 3's Open Question
    and `CLAUDE.md` record for `AccountsControl`. Any new report that feeds it uneven
    series takes the app down.

27. **The reports options panel's help topic is the wrong one.**
    `ReportsControl.xaml` declares `help:HelpService.HelpKeyword="Accounts/BalancingAccounts/"`
    — the balancing-an-account topic, copied from `BalanceControl`. Pressing F1 in the
    report settings panel opens documentation about reconciling a statement. Relatedly,
    of the nine reports only seven set a help topic when they open: the cash-flow and
    unaccepted reports set none, so F1 there falls back to whatever the shell offers.
    Same convention-drift pattern Phase 4 documented for dialogs.

28. **A discarded report writer sits in the unaccepted-transactions command.**
    `MainWindow.OnCommandReportUnaccepted` constructs a `FlowDocumentReportWriter` over the
    view's document and then never uses it — `GenerateReport` creates its own. It is the
    only one of the nine report commands that does this. Harmless, but it clears the
    document twice and reads as a half-finished edit.

29. **The report options panel is rebuilt every time a report is generated.**
    `MainWindow.GetService(typeof(ReportsControl))` calls `ShowReportsPanel()`, which
    disposes the old panel and constructs a new one — so asking for the panel is what
    makes it appear, and every report gets a fresh one. That is why the panel has
    `Hide…` methods but no matching `Show…` for the date and currency rows: each report
    hides what doesn't apply to it and relies on the next report getting a clean panel.
    It works, but "getting a service has a visible side effect" is a pattern a redesign
    should not inherit, and it is the reason every report must re-register its event
    handlers on every generation (which all of them do by unsubscribing twice first —
    `Unregister(); … Unregister(); Register();` appears verbatim in four reports).

30. **Where the Phase 7 line was drawn.** `TaxReport` and `W2Report` live in `Reports/`
    and are captured here as documents the user reads (P5-TAX-*). Everything behind them —
    `Taxes/TaxCategoryCollection`, `CapitalGainsTaxCalculator`, `TxfExporter`,
    `FederalTaxes`, `StateTaxes` and the state/federal rate tables the retirement plan
    also uses — is Phase 7 and was read only far enough to describe what appears on
    screen. If Phase 7 finds a user-visible decision that only exists inside those
    classes, it belongs there. The same line runs through P5-PLAN-7: the retirement plan's
    tax modelling is described as "rules applied for them", not enumerated.

31. **Where the Phase 6 line was drawn.** Nothing in this phase imports anything, but three
    things write files — the CSV exports (P5-VIEW-7), the HTML export (P5-VIEW-6) and the
    chart exports (P5-TREND-8) — and all three are captured here as report affordances
    rather than as export formats, on the basis that Phase 6's scope is `Importers/`,
    `Ofx/` and the storage formats. The `.txf` tax export (P5-TAX-7) is the one case where
    the file's contents genuinely matter to a user, and those contents are Phase 7's.

32. **There are four overlapping notions of "a period" in this one feature area, and the
    one in the shared interface is the least used.** `IReport.cs` defines
    `enum ReportInterval { Days, Months, Years }`, which reads like the intended shared
    vocabulary. No report uses it — its only consumer is `ReportRangeDialog`'s interval
    combo, which that dialog's sole caller collapses (Phase 4's Open Question 16), so it
    is reachable in name only. Meanwhile the cash-flow report buckets by two hard-coded
    strings, the history chart has `HistoryRange` (the same three members, different
    semantics), and the trend graph has `CalendarRange` (ten members). A redesign that
    wants "choose a period" to mean one thing has four definitions to reconcile.

---

# Phase 6: Import/Export

**Source examined (in full):** every file in `Source/WPF/MyMoney.Business/Importers/`
(10 files, ~2,600 lines) and `Source/WPF/MyMoney.Business/Ofx/` (8 code files plus 7
data/resource files, ~10,000 lines of code), plus the one OFX file that stayed behind
in the WPF project, `Source/WPF/MyMoney/Ofx/OfxDownloadController.cs`.

> **Path correction — `CLAUDE.md` and this catalogue's own phase table are both stale.**
> Neither `Importers/` nor the bulk of `Ofx/` lives under `Source/WPF/MyMoney/` any more.
> `Importers/` is now `Source/WPF/MyMoney.Business/Importers/`, and `Ofx/` is
> `Source/WPF/MyMoney.Business/Ofx/` — **except** `OfxDownloadController.cs`, which is
> the only file left in `Source/WPF/MyMoney/Ofx/` because it owns WPF dialogs. A `Glob`
> for `Source/WPF/MyMoney/Importers/*` returns nothing at all. This is the same
> `MyMoney.Business` extraction Phase 1 flagged for `Money.cs`, carried further.

| File(s) | Lines |
|---|---|
| `Importers/Importer.cs` (abstract base + transfer matching) | 147 |
| `Importers/CsvImporter.cs` (`CsvMap`, `CsvTransactionImporter`, `TransactionCache`) | 613 |
| `Importers/CsvImportController.cs` | 140 |
| `Importers/CsvDocument.cs` (the delimited-file parser, namespace `Walkabout.Utilities`) | 193 |
| `Importers/CsvTransactionFormat.cs` (namespace `Walkabout.Data`) | 151 |
| `Importers/QifImporter.cs` | 451 |
| `Importers/XmlImporter.cs` | 289 |
| `Importers/Exporters.cs` | 343 |
| `Importers/DownloadData.cs` (`DownloadData`, `DownloadEventArgs`) | 243 |
| `Importers/IImportProgressReporter.cs` | 18 |
| `Ofx/Ofx.cs` (`OfxThread`, `OfxRequest`, `OfxException`, `OfxMfaChallengeRequest`) | 3,744 |
| `Ofx/OfxObjectModel.cs` (the deserialised profile/sign-on/MFA model) | 781 |
| `Ofx/OfxInstitutionInfo.cs` (the bank directory) | 713 |
| `Ofx/OfxErrorCode.cs` | 105 |
| `Ofx/OfxStrings.Designer.cs` + `.resx` (96 user-facing OFX error messages) | 891 |
| `Ofx/SgmlParser.cs` + `Ofx/SgmlReader.cs` (`Walkabout.Sgml`) | 1,801 + 1,964 |
| `Ofx/HtmlResponseException.cs` | 20 |
| `Ofx/` data: `OfxProviderList.xml`, `MfaPhrases.xml`, `OfxErrorTemplate.htm`, `ofx160.dtd`, `ofx201.dtd`, `OFX 2.1.1.pdf`, `ofx16.pdf` | — |
| `MyMoney/Ofx/OfxDownloadController.cs` | 364 |

**Supporting files read but owned elsewhere**, because a scenario here can't be written
honestly without them: `MainWindow.xaml.cs`'s "Importing" region and its
`OnCommandFileImport`/`ImportQif`/`ImportOfx`/`ImportXml`/`ImportCsv`/`ImportMoneyFile`/
`ExportCsv`/`OnCommandFileExportAccountMap`/`DoSync`/`SyncAccount` members (Phase 2's
P2-FILE-11, P2-CMD-11, P2-START-3); `Dialogs/MoneyFileImportDialog.xaml.cs`'s
`ProcessFile`/`ImportMoneyFile`/`ImportAccount` (Phase 4's P4-IMPORT-1…6 captured the
conversation, the matching is here); `Dialogs/CsvImportDialog.xaml.cs` and
`WpfBusinessLayerUiCallback.cs` (Phase 4's P4-IMPORT-7…10); `MyMoney.Data/CsvStore.cs`
(the "Save As .csv" destination and a second, dead CSV importer);
`View Selectors/AccountsControl.xaml.cs`'s `Export`/`ExportList`/`Paste`;
`Views/TransactionsView.xaml.cs`'s `CopySelection`/`PasteSelection`/`OnCommandViewExport`;
`Views/LoansView.xaml.cs`'s export; `Controls/DownloadControl.xaml.cs` and
`DownloadControlProgressReporter.cs` (Phase 2's P2-PANE-5);
`Dialogs/SelectAccountDialog.xaml.cs`'s `AccountHelper.PickAccount`;
`MyMoney.Business/Utilities/ProcessHelper.cs`'s `IsFileQIF`/`IsFileOFX`/
`ImportFileListFolder`.

> **Naming notes.**
> - `Importers/CsvDocument.cs` declares its class in namespace **`Walkabout.Utilities`**,
>   and `Importers/CsvTransactionFormat.cs` in **`Walkabout.Data`** — neither is in
>   `Walkabout.Importers` like the rest of the folder.
> - `Ofx/SgmlParser.cs` and `Ofx/SgmlReader.cs` are in namespace **`Walkabout.Sgml`** and
>   are a vendored 2002 general-purpose SGML-to-XML reader ("An XmlReader implementation
>   for loading HTML as if it was XHTML", Chris Lovett), not OFX code at all. They are
>   3,765 of the 10,000 lines in `Ofx/`.
> - `Controls/DownloadControl.xaml.cs` carries the doc comment *"Interaction logic for
>   OfxDownloadControl.xaml"* — another copy-pasted `<summary>` naming a file that
>   doesn't exist, the same trap Phases 3, 4 and 5 each hit.
> - `Ofx/Ofx.cs` is a 3,744-line file holding four unrelated public classes; there is no
>   `Ofx` type in it.

**What this phase covers and deliberately does not.** This section catalogues *how data
gets into and out of the product through files and the bank's own servers* — what
real-world artefact each route connects to, what the user has to do, what matching and
duplicate decisions they are given, and what happens when the data is wrong. The
*dialogs* that collect the answers (account picker, column mapping, passwords, MFA
questions, login) are Phase 4 and are referenced, not re-described; the *panel* that
shows progress is Phase 2's P2-PANE-5. Report exports (CSV/HTML/chart) stayed with
Phase 5 as report affordances, and the `.txf` TurboTax export is Phase 7 (see Open
Question 22). Setting up an online account's credentials is Phase 4's P4-ONLINE-*; what
happens to what comes back is here.

---

## 6.1 Deciding what to bring in

### P6-ENTRY-1 — Hand the product a file and have it work out the rest
The user picks one or more files of almost any supported kind in a single dialog — a
bank's OFX or QFX download, a spreadsheet, a Quicken-style QIF export, the product's own
XML interchange file, or another copy of their own data file — and each is routed to the
right treatment without the user having to say which is which. Anything unrecognised is
named back to them with the list of kinds that are understood.
*(`MainWindow.OnCommandFileImport`, the per-extension switch; the file-type filters in
`MyMoney.Business/Properties/Resources.resx`. **See Open Question 1 — selecting more
than one file re-imports the earlier ones.**)*

### P6-ENTRY-2 — Double-click a downloaded statement and have it land in the right place
A statement file saved from a bank's website can be opened from the desktop and goes
into the running copy of the product rather than starting a second one. The product
watches a shared hand-off list for files queued this way and picks them up a moment
later, so several files opened in quick succession arrive as one batch.
*(`ProcessHelper.ImportFileListFolder`'s `imports.xml`, `MainWindow.importWatcher` →
`LoadImportFiles`, the 250ms debounce; Phase 2's P2-START-3 covers the launch half.
**Only `.qif`, `.ofx` and `.qfx` are recognised on this route** — `ProcessHelper.IsFileQIF`/
`IsFileOFX`.)*

### P6-ENTRY-3 — Make the product the default for statement files
The user can ask once for statement files to open with this product from then on, and is
told which kinds were claimed.
*(`MainWindow.OnCommandFileExtensionAssociation` registering `.qif`, `.qfx`, `.ofx`,
`.mmdb`)*

### P6-ENTRY-4 — Watch each file or account arrive, and open what it brought
Every import and every download reports itself as a row, one per file or per account,
with a spinner while it is working, a tick when it succeeds, a count of what it added,
and an error with a "Details…" link when it doesn't. Clicking a row that added anything
jumps straight to exactly those entries.
*(`DownloadData`/`DownloadEventArgs`, `IImportProgressReporter`, `DownloadControl`;
the panel itself is Phase 2's P2-PANE-5)*

### P6-ENTRY-5 — Read a full technical report of a failure they can pass on
When an import or download fails, the "Details…" link opens a formatted page naming the
institution, the address contacted, the message, the raw response and the HTTP headers —
the thing a user is asked to attach to a bug report. Credentials are blanked out of the
saved copies first.
*(`OfxDownloadController.OnDetailsClicked` + `Ofx/OfxErrorTemplate.htm`;
`OfxRequest.SaveLog` masking `USERID`/`USERPASS`/`USERKEY`/`SESSCOOKIE`/`USERCRED1`/
`USERCRED2`/`MFAPHRASEA`/`ACCESSKEY`/`AUTHTOKEN`. **See Open Question 13 — account
numbers are not masked.**)*

---

## 6.2 Fetching statements from the bank

### P6-OFX-1 — Ask every connected institution for what's new, at once
One command reaches out to every institution the user has finished setting up and
fetches each one's statements in parallel, so a household with eight banks waits for the
slowest rather than for the sum. Institutions that aren't fully configured are silently
left out, and if none are ready the user is told where to go and set one up.
*(`MainWindow.OnSynchronizeOnlineAccounts` requiring an address, user id and password;
`OfxThread.Synchronize` starting one task per institution)*

### P6-OFX-2 — Ask just one institution
From the accounts panel the user can refresh a single account rather than everything.
What is actually fetched is every account held at that same institution, because the
conversation with a bank covers all of them at once.
*(`MainWindow.SyncAccount` → `DoSync` with that account's online account;
`OfxThread.SyncAccount` gathering every account attached to it)*

### P6-OFX-3 — Only ask for what they don't already have
Each account remembers when it was last refreshed and the product asks the bank only for
the period since then, with ten days of overlap in case the bank posts something late. An
account that has never been refreshed gets the last thirty days.
*(`OfxRequest.GetStatementRequestRange`, `Account.LastSync`)*

### P6-OFX-4 — Have everyday, card and investment accounts each asked for properly
The product groups the accounts it is asking about by what kind they are and phrases a
different request for each — one for chequing/savings/money-market/credit-line, one for
credit cards, one for brokerage and retirement — so each institution is asked in the form
it expects. Account kinds that no institution can serve statements for are refused up
front with the kind named.
*(`OfxRequest.GetRequestType`, `GetBankRequest`/`GetCreditRequest`/`GetInvestmentRequest`)*

### P6-OFX-5 — Not be stopped by an institution on an older protocol
If an institution rejects the request, the product silently retries in the other protocol
generation before giving up, so a user who guessed wrong when setting the institution up
still gets their statement.
*(`OfxRequest.SendOfxRequest`'s version-flip retry, `OfxRequest.Signup`'s equivalent.
**See Open Question 14 — the flip is kept even when the retry also fails.**)*

### P6-OFX-6 — Be told, in words, why the bank refused
Every refusal the standard defines has a plain-English explanation, so the user reads
"your password has expired" rather than a number. Where the product can do something
about it, the error row turns into an action: get an authorisation token, answer extra
identity questions, change the password, or log in again — and once that is done, the row
becomes "Try Download Again".
*(96 messages in `Ofx/OfxStrings.resx` keyed by `OfxErrorCode`;
`OfxDownloadController.OnDetailsClicked`'s four recoverable cases → `AuthTokenDialog`,
`MfaChallengeDialog`, `ChangePasswordDialog`, `OfxLoginDialog` — all Phase 4)*

### P6-OFX-7 — Have the machine answer the identity questions it can
When an institution's extra identity questions are about the computer rather than the
user — its name, address, operating system, the time — the product answers them itself and
only puts the genuinely personal ones in front of the user.
*(`OfxMfaChallengeRequest.HandleChallenge`'s built-in answers for MFA101…MFA107.
**See Open Questions 15 and 16.**)*

### P6-OFX-8 — Recognise the account even when the bank writes its number differently
An incoming statement is matched to the user's account by the account number the bank
quotes; if that doesn't match exactly, the product also tries the aliases it has been
taught, and then tries matching a partly-masked number (`XXXX1234`) against the tail of
the numbers it knows. Only if all of that fails is the user asked.
*(`OfxRequest.FindAccountByOfxId`, `AccountIdFuzzyMatch`, `MyMoney.AccountAliases`;
the ambiguity guard returns nothing rather than guessing)*

### P6-OFX-9 — Be asked once about an account they don't recognise, and remembered
If a statement names an account the product can't place, the user is shown the number and
picks an existing account or creates a new one; an answer of "skip" is remembered for the
rest of that import so they aren't asked again about the same number. Choosing a
different account than the number suggests records an alias, so the question never comes
back.
*(`OfxRequest.CheckAccountId`, `skippedAccounts`, `AccountHelper.PickAccount` recording an
`AccountAlias` — Phase 1's P1-ALIAS-8)*

### P6-OFX-10 — Decide about every account before any of them are written
All of the "is this the right account?" questions for one response are asked first and
the statements are only applied afterwards, so a user who realises half-way through that
something is wrong can cancel without having had part of the import already committed.
*(the `pending` list in `ProcessBankResponse`/`ProcessCreditCardResponse`/
`ProcessInvestmentResponse`, with the explicit comment saying why)*

### P6-OFX-11 — Be stopped from pouring a statement into the wrong kind of account
If the statement that comes back is for a credit card and the account it is aimed at is a
chequing account (or vice versa), the import of that statement stops with both named
rather than writing card transactions into a bank register.
*(the account-type guards at the top of each `Process*Response`,
`Properties.Resources.AccountTypeMismatch`)*

### P6-OFX-12 — Have downloaded entries arrive already matched against what's recorded
Each incoming entry is matched against what the account already holds before it is added,
so a payment the user typed in by hand and the bank's own copy of it become one entry
rather than two. Anything genuinely new arrives marked as unapproved and electronic, so
the register shows at a glance what still needs looking at.
*(`ProcessStatement` → `Transactions.Merge` — Phase 1's P1-IMPORT-1/2;
`TransactionStatus.Electronic`, `Unaccepted`, `IsDownloaded`)*

### P6-OFX-13 — Have the bank's messy payee text turned into the name they use
Where a statement gives a payee name the user has already taught the product to rename,
the rename is applied on the way in and the bank's original wording is kept in the memo
instead of being lost. Bill-payment lines that bury the cheque number in the memo are
unpicked into a payee and a number.
*(`ProcessStatement`'s `Aliases.FindMatchingAlias` and its `"Bill Payment "` unpicking;
`NAME`/`PAYEE`/`PAYEE2` and `MEMO`/`MEMO2` fallbacks; the `"N/A"` memo suppression)*

### P6-OFX-14 — Not be shown the bank's own filler entries
Zero-value entries on a credit-card statement — the checks some issuers post as
placeholders — are dropped rather than cluttering the register.
*(`ProcessStatement`'s `amount == 0 && Type == Credit` skip)*

### P6-OFX-15 — Have the running balance recomputed when a statement lands
Every account that received anything is rebalanced at the end of the import so the
figures on screen are right immediately.
*(`MyMoney.Rebalance` at the end of `ProcessStatement` and `ProcessInvestmentResponse`)*

---

## 6.3 Reading a statement file saved from the bank's website

### P6-FILE-1 — Import a statement file without setting up a connection at all
A `.ofx` or `.qfx` file downloaded by hand from a bank's website imports through exactly
the same machinery as a live download — same account matching, same duplicate matching,
same unapproved-entry marking — without the user ever configuring credentials.
*(`OfxThread.LoadImports` reusing `OfxRequest.ProcessResponse`; `Account` resolved purely
by the account number in the file, since there is no request to correlate against)*

### P6-FILE-2 — Have a badly-formed statement file read anyway
Statement files in the wild break the rules constantly, and the product tries hard before
giving up: it reads the file's own declared character set, salvages a file whose opening
tag isn't on its own line, patches a known malformation from one large broker, falls back
from strict XML to a lenient reader, and can be told globally to just treat every
statement file as modern text when a bank mislabels its own encoding.
*(`OfxRequest.ParseOfxResponse`, the `"?<OFX>"` fix, the `SgmlReader` fallback against the
bundled `ofx160.dtd`/`ofx201.dtd`, `Settings.ImportOFXAsUTF8` — Phase 2's P2-PREF-*.
**See Open Question 11 — the declared-encoding read never actually looks at the file.**)*

### P6-FILE-3 — Be told plainly when the "statement" is really a web page
When a bank's server answers with a login page or an error page instead of a statement —
the most common failure of the whole feature — the product recognises it as such and
shows the page it got back, rather than reporting a parse error.
*(`HtmlResponseException` raised on `<html>`/`<!DOCTYPE html`, rendered by
`OfxDownloadController`'s details page)*

### P6-FILE-4 — Be told plainly when a statement file is a kind that isn't supported
A file that announces a protocol generation, a data format, a security scheme or a
compression the product doesn't implement is refused by name rather than half-read.
*(`ParseOfxResponse`'s `OFXHEADER`/`DATA`/`VERSION`/`SECURITY`/`COMPRESSION` header checks)*

### P6-FILE-5 — Keep a copy of every conversation for when something goes wrong
Every request sent and every response received is written to a log folder beside the
program, with credentials blanked, and the details page links to it. A statement file
imported from disk gets the same treatment, so the user always has something to hand over.
*(`OfxRequest.SaveLog`, `OfxRequest.OfxLogPath`, the per-institution unique log names)*

---

## 6.4 Investment statements

### P6-INV-1 — Have trades arrive as trades, not as unexplained amounts
A brokerage statement's purchases, sales, transfers in and out, income, expenses, margin
interest and cash movements each become the corresponding kind of entry with its own
holding, quantity, unit price, commission, fees, taxes and load filled in, and an
investment category chosen to match what was traded — shares, funds, bonds, options or
other.
*(`ProcessInvestmentTransactionList` and its per-element handlers; `InvestmentType`,
`InvestmentTradeType`, the `Categories.Investment*` set)*

### P6-INV-2 — Have a reinvested dividend recorded as both halves
When income is reinvested, the product records both the purchase and the matching cash
receipt, so the holding grows and the income still shows up as income.
*(`ProcessInvestmentTransactionList`'s `REINVEST` case creating a second, paired entry)*

### P6-INV-3 — Have holdings named the way the user names them
Securities in a statement are matched to the ones already held by ticker first, then by
the institution's own identifier, then by name, and a holding the user has never seen
before is created. Where the security's name is one the user has taught the product to
rename, the rename applies to the holding itself and not just to that one entry.
*(`ReadSecurityInfo`, `ProcessSecId`, the alias handling in
`ProcessInvestmentTransactionList` — Phase 1's P1-ALIAS-* reused for securities)*

### P6-INV-4 — Get today's prices out of the statement too
The prices and holdings the institution reports alongside the statement update the
product's own record of what each security is worth and when that price was struck, and a
trade that arrives without a price borrows the one the statement just supplied.
*(`ProcessInvestmentPositions`, `ReadSecurityInfo`'s `UNITPRICE`, the price back-fill in
`ProcessInvestmentTransactionList`)*

### P6-INV-5 — Not be given entries that mean nothing
An entry that neither moves cash nor changes a holding is dropped rather than added as a
zero row.
*(`ProcessInvestmentTransactionList`'s `Amount == 0 && Units == 0` skip)*

### P6-INV-6 — Have a purchase count as money out whichever way the broker writes it
Institutions disagree about the sign of a purchase total; the product forces a buy to
reduce cash and a sale to increase it regardless of how the statement expressed it.
*(`ProcessInvestmentBuy`'s forced negative, with the two contradictory real-world examples
recorded in the code)*

---

## 6.5 Spreadsheets and CSV statements

### P6-CSV-1 — Import a spreadsheet the bank produced, whatever its columns are called
A delimited file exported from a bank or a spreadsheet program can be imported without
the user editing it first: they are shown their own file's column headings and say which
piece of a transaction each one is, once, and the answer is remembered for that account.
*(`CsvImportController`, `CsvTransactionImporter`, `CsvMap` saved as one file per account
under a `CsvMaps` folder beside the data file; the mapping conversation is Phase 4's
P4-IMPORT-7…10)*

### P6-CSV-2 — Be asked again only when the file's shape changes
The remembered mapping is reused silently as long as the incoming file's headings still
match it exactly; the moment a bank changes its export format the user is asked to
re-map, rather than the import quietly landing in the wrong fields.
*(`CsvTransactionImporter.HeadersMatch` comparing heading-for-heading in order)*

### P6-CSV-3 — Import a file covering several accounts at once
A spreadsheet that names an account per row — the usual shape of a brokerage's
whole-portfolio export — is split by account, each group matched to one of the user's
accounts by number, and each imported separately with its own result row. The mapping
worked out for the first group is offered to the rest, so the user answers once.
*(`CsvTransactionImporter.GroupCsvByAccount`, `CsvImportController.ImportCsv`'s
`"Account Number"` branch. **See Open Questions 2 and 3.**)*

### P6-CSV-4 — Bring in trades from a broker's spreadsheet
For a brokerage or retirement account the mapping offers the extra columns a trade needs —
the symbol, the quantity, the unit price and the kind of trade — and the product works out
whether each row is a purchase, a sale, a transfer of shares in or out, a dividend or
interest from the amounts and the wording. A row that gives only two of quantity, price
and total has the third worked out for it.
*(`CsvTransactionImporter.BrokerageAccountFields`, `AddInvestmentInfo`, the
quantity/price back-calculation in `ImportRow`)*

### P6-CSV-5 — Have a missing trade price filled in from market history
When a spreadsheet gives a quantity but no price, the product looks up what the security
was worth on that date and fills it in, so the holding's cost isn't left at zero.
*(`CsvTransactionImporter.LookupUnitPrice` → `StockQuoteCache.GetSecurityMarketPrice`)*

### P6-CSV-6 — Not get a second copy of everything on every import
Spreadsheets rarely carry a stable identifier for a transaction, so the product matches
each incoming row against what the account already holds on the same date with the same
party, amount and holding, and updates the existing entry rather than adding a duplicate.
That is what makes re-importing an overlapping export safe.
*(`CsvTransactionImporter.TransactionCache`, indexed by date for speed;
`FindMatch`/`IsMatch`)*

### P6-CSV-7 — Have the sign flipped for a bank that records spending as positive
One toggle in the mapping handles the common case of a file whose amounts run the
opposite way round from the product's convention.
*(`CsvMap.Negate` — Phase 4's P4-IMPORT-9.
**See Open Question 4 — bracketed negatives are read as positive regardless.**)*

### P6-CSV-8 — Have the file's rubbish rows ignored
Rows that are just a disclaimer line, rows the bank posted before they have a date
(pending transactions), and the trailing notes banks tack onto their exports are skipped
rather than becoming entries.
*(`ImportRow`'s single-value and `Date == MinValue` guards)*

### P6-CSV-9 — Have quoted, comma-bearing and escaped fields read correctly
The file's own quoting is honoured, including doubled quotes inside a quoted field and
whitespace around delimiters, so a payee with a comma in its name survives the import.
*(`CsvDocument.ReadRecord`)*

### P6-CSV-10 — Be told when the file has nothing in it
An empty or header-only file is refused with a plain message rather than reporting a
successful import of nothing.
*(`CsvDocument.Read`'s `".csv file is empty"`)*

---

## 6.6 Quicken and Microsoft Money exports (QIF)

### P6-QIF-1 — Bring in a history exported from the product they're leaving
A QIF file exported from Quicken or Microsoft Money imports with its dates, amounts,
cheque numbers, payees, memos, categories, cleared marks and itemised breakdowns intact —
the route by which years of history move into this product.
*(`QifImporter.ImportQif`'s field handling; the two format references cited in the code)*

### P6-QIF-2 — Have the account created, or be asked which one to merge into
The product proposes an account named after the file and offers to create it; if the user
declines, or an account of that name already exists with entries in it, they are asked
whether to merge the file into the account they currently have open instead.
*(`QifImporter.Import`'s create/merge prompts. **See Open Question 5 — the prompt has a
typo, and the caller has already asked the same question.**)*

### P6-QIF-3 — Have the account's kind taken from the file
For a brand new account, what kind of account it is — everyday, savings, cash, credit card
or brokerage — is read out of the file rather than having to be set by hand afterwards.
*(the `!Type:` header mapping, including Microsoft Money's "Other" being treated as
everyday. **See Open Question 6 — the same check refuses correct merges and permits
wrong ones.**)*

### P6-QIF-4 — Have transfers between accounts reconnected on the way in
Where the file records a movement to another account, the product looks for the other half
among what it already has — matching on the same day, the same party and the opposite
amount, including inside itemised breakdowns — and links the two into a single transfer
rather than two unrelated entries. The account on the other end is created if it isn't
there yet.
*(`Importer.FindMatchingTransfer`/`FindSplitTransfer`/`FindMatchingSplitTransfer`, the
`'L'` and `'S'` handling in `ImportQif`)*

### P6-QIF-5 — Have the starting balance recognised as a starting balance
A file's opening-balance line becomes the account's opening balance rather than a
transaction on day one.
*(`ImportQif`'s self-transfer + "Opening Balance" payee case setting `Account.OpeningBalance`)*

### P6-QIF-6 — Have voided entries come in as voided
An entry the source system marked void arrives void, with its payee intact.
*(the `'P'` handler's `VOID` prefix)*

### P6-QIF-7 — Not inherit the old product's reconciliation state
Entries the source marked reconciled arrive merely cleared, deliberately, so that
balancing an account against a statement starts from a clean slate in this product rather
than inheriting the other product's idea of what was reconciled.
*(the `'C'` handler's explicit comment; `TransactionStatus.Cleared`)*

### P6-QIF-8 — Import into an account that already has entries without duplicating them
When the destination already holds transactions, each incoming entry is matched against
them first and merged where it matches, and everything that arrives is marked unapproved
so the user reviews it.
*(`ImportQif`'s `merge` mode → `Transactions.Merge`, `t.Unaccepted = true`.
**See Open Question 7 — a first-time import marks nothing unapproved.**)*

---

## 6.7 Merging another copy of the same books

### P6-MERGE-1 — Fold a second copy of their own data back into the main one
A user who has been working in a copy of their data file elsewhere — a laptop, a second
machine — can merge that copy back in. The product works through it account by account and,
for each entry, either updates the one already there with whatever the copy knows and it
doesn't, or adds it as new.
*(`MoneyFileImportDialog.ImportAccount`, `Transaction.Merge` — Phase 4's P4-IMPORT-1…6
covers what the user sees)*

### P6-MERGE-2 — Be warned that this only works between copies of the same file
Before anything happens the user is told, in so many words, that merging only works if
both files started from the same state — because entries are paired by the internal
identity they were given when first recorded, not by looking anything like each other.
*(`MainWindow.ImportMoneyFile`'s confirmation;
`MoneyFileImportDialog.ImportAccount`'s `FindTransactionById(t.Id)` — that identity match
is the reason for the warning)*

### P6-MERGE-3 — Bring the paperwork and the statements across with the entries
Attachments belonging to the incoming entries and the statement documents belonging to
the incoming accounts are copied across alongside the transactions.
*(`AttachmentManager.ImportAttachments`, `StatementManager.ImportStatements`;
Phase 4's P4-IMPORT-6 and its Open Question 14, now filed as
[markabrandjord/MyMoney.Net#54](https://github.com/markabrandjord/MyMoney.Net/issues/54) —
the attachments are filed against the incoming entry rather than the surviving one)*

### P6-MERGE-4 — Open a protected copy
If the incoming file is password-protected the user is prompted for its credentials, with
the prompt worded for the file being imported rather than for their own.
*(`MoneyFileImportDialog.ProcessFile`; only SQLite data files are accepted — anything else
is refused with *"Import only supports sqllite money files"*)*

---

## 6.8 Copying, pasting and the product's own interchange format

### P6-XML-1 — Copy entries out and paste them back somewhere else
A transaction, an itemised line or a whole row can be copied and pasted into another
account, carrying its party, category, amount, memo, holding and breakdown with it. The
pasted copy is a genuinely new entry — it gets its own identity, loses the original's
cleared and reconciled state, and loses any "these are not duplicates" mark, so the
destination account's balancing history isn't contaminated by the copy.
*(`TransactionsView.CopySelection`/`PasteSelection`, `Exporters.ExportString`,
`XmlImporter.ImportObjects`/`AddTransaction`)*

### P6-XML-2 — Move an entry to a different account rather than copying it
Cutting rather than copying moves the original entry — with its paperwork — to the
destination instead of duplicating it, and refuses to move one that has already been
reconciled.
*(`XmlImporter.ImportObjects`'s `"cut"` action, `IBusinessLayerUiCallback.MoveAttachments`)*

### P6-XML-3 — Copy an account definition and paste it back
An account's own settings can be copied to the clipboard and pasted in as a new account,
matched by name so pasting an account that already exists doesn't create a second one.
*(`AccountsControl.Copy`/`Paste`, `Account.Serialize`, `XmlImporter.ImportAccount` →
`MergeAccount`. **See Open Question 8 — a successful paste always reports failure.**)*

### P6-XML-4 — Write an account out as a file and read it back
A single account and its entries can be written out as the product's own XML interchange
file and imported into another copy of the product, which recreates the account if it
isn't there and adds the entries to it.
*(`AccountsControl.Export` → `Exporters.Export`'s `.xml` branch;
`MainWindow.ImportXml` → `XmlImporter.ImportXml`)*

### P6-XML-5 — Be stopped before entries land in nowhere
An interchange file whose entries name an account that neither exists nor is described in
the file itself is refused with an explanation, rather than the entries being dropped
silently.
*(`XmlImporter.AddTransaction`'s *"Cannot add transactions before we find account
information in the imported xml file"*; `Exporters.ExportToXml` writing the referenced
accounts — including both ends of a transfer — ahead of the entries for exactly this reason)*

---

## 6.9 Sending data back out

### P6-OUT-1 — Take whatever is on screen away as a spreadsheet
Whatever list the user is looking at — a filtered register, a search result, a loan's
payment schedule — can be written out as a delimited file and opens in their spreadsheet
program straight away. The columns adapt to the content: trade details appear only if
there are trades in the list, sales tax only if any row has it, and a currency column only
if the rows span more than one currency.
*(`TransactionsView.OnCommandViewExport`, `LoansView`'s export, `Exporters.ExportPrompt`/
`ExportToCsv`, `CsvTransactionFormat`. **See Open Questions 9 and 10 — the amounts are
written in a form the product's own importer reads back with the wrong sign, and XML is
never offered here.**)*

### P6-OUT-2 — Take one account away in full
A single account can be written out with all its entries, in the product's own interchange
format or — for a brokerage account — in a tax-software format.
*(`AccountsControl.OnExportAccount` → `Export`; the `.txf` branch is Phase 7's
`TxfExporter`, see Open Question 22)*

### P6-OUT-3 — Take the list of accounts away
The accounts themselves — kind, name, whether closed, currency, balance and when it was
last balanced — can be written out as a spreadsheet and opened.
*(`AccountsControl.OnExportAccountList`/`ExportList`; internal category-fund accounts are
left out. **See Open Question 10 for the amount formatting.**)*

### P6-OUT-4 — Save the whole file in a different format
The user can save their entire data file as a different kind of file — a different
database engine, a plain-text or compact XML file, or a spreadsheet — which is how they
change storage choice or take a readable snapshot away.
*(`MainWindow.OnCommandFileSaveAs`'s per-extension switch; Phase 4's P4-DATA-* owns the
conversation. **See Open Question 12 — the spreadsheet choice saves the current view, not
the file.**)*

### P6-OUT-5 — See a picture of how money moves between their accounts
The user can produce a diagram of their accounts with an arrow between any two that money
has been transferred between, labelled with the total moved and colour-coded by account
kind, and it opens in whatever program handles that kind of diagram.
*(`Exporters.ExportDgmlAccountMap`, `MainWindow.OnCommandFileExportAccountMap`;
`SimpleGraph`. **See Open Question 17.**)*

---

## 6.10 Finding the institution to download from

### P6-BANKS-1 — Pick their bank from a list rather than typing its address
When setting up a download the user searches a directory of thousands of institutions and
picks theirs, and the address, organisation identifier, broker identifier and protocol
generation are filled in for them.
*(`OfxInstitutionInfo.GetCachedBankList` seeded from the bundled `Ofx/OfxProviderList.xml`
(~4,400 lines); `OnlineAccountDialog` is Phase 4's P4-ONLINE-*)*

### P6-BANKS-2 — Have that list stay current without an upgrade
The directory is refreshed from a public registry in the background and merged with both
the copy that shipped with the product and the user's own local copy, with the most
recently-changed value for each field winning — so a user's own hand-typed correction
isn't overwritten by stale registry data, and vice versa.
*(`GetRemoteBankList`, `MergeProviderList`, `OfxInstitutionInfo.Merge`/`SetIfNewer`,
`ChangeTrackedField`. **See Open Questions 18 and 19.**)*

### P6-BANKS-3 — Have the product learn what an institution actually supports
Before downloading, the product asks the institution itself what it can do and what it
needs — which identity fields it wants, whether it requires a device identifier, whether a
password must be changed first, whether it asks extra identity questions — and caches the
answer so it doesn't have to ask again. If the institution can't be reached the cached
answer is used.
*(`OfxRequest.GetProfile`/`LoadCachedProfile`/`GetSignonInfo`, `OfxObjectModel`'s
`OfxSignOnInfo`; the "profile unchanged" fast path)*

---

## 6.11 What every import does the same way

### P6-ALL-1 — Have a whole import land as one change, not hundreds
Everything an import writes is treated as a single update, so lists don't churn, totals
don't flicker part-way through, and one undo-sized change appears rather than a row per
entry.
*(`MyMoney.BeginUpdate`/`EndUpdate` around `ProcessStatement`,
`ProcessResponse`'s per-message-set scope, `CsvTransactionImporter.Commit`,
`XmlImporter.ImportObjects`. **See Open Question 20 — the QIF importer is the exception.**)*

### P6-ALL-2 — Have what arrived marked so it can be reviewed
Everything an import or download adds is flagged as freshly downloaded, so the register
can show the user exactly this batch and they can approve it entry by entry or all at once
afterwards.
*(`Transaction.IsDownloaded`, `DownloadData.AddItem` — Phase 1's P1-TXN-10, Phase 3's
P3-REG-11)*

### P6-ALL-3 — Have the party names cleaned up on the way in, by the same rules everywhere
The rename rules the user has built are applied by every route in — bank download,
spreadsheet, investment statement — and where a rule fires, the source's original wording
is preserved in the memo rather than discarded.
*(`Aliases.FindMatchingAlias` in `ProcessStatement`,
`ProcessInvestmentBankTransaction`, `ProcessInvestmentTransactionList` and
`CsvTransactionImporter.MapField` — Phase 1's P1-ALIAS-*)*

### P6-ALL-4 — Carry on after one file, or one account, goes wrong
A failure reading one file, or one account inside one file, is reported against that row
and the rest of the batch continues, rather than the whole import stopping.
*(the per-file `try`/`catch` in `OfxThread.LoadImports` and `MainWindow.ImportQif`, the
per-statement `continue`s in `Process*Response`, the per-transaction `try`/`catch` in
`MoneyFileImportDialog.ImportAccount`)*

### P6-ALL-5 — Stop an import that's taking too long or going wrong
A download in progress can be cancelled, which stops every institution's request at once
and clears the results panel.
*(`OfxDownloadController.Cancel`, `OfxThread.Stop`, `OfxRequest.Cancel`'s cancellation
token; `UserCanceledException` unwinding a CSV import quietly)*

---

## Phase 6 coverage checklist

Every file in `MyMoney.Business/Importers/` and `MyMoney.Business/Ofx/`, plus
`MyMoney/Ofx/`. "Not user-facing" entries are plumbing, vendored libraries or dead code
the user never perceives.

### `Importers/`

| File | Type | Status |
|---|---|---|
| `Importer.cs` | `Importer` (abstract) | P6-QIF-4 — the transfer-reconnection helpers; the base `Import(string)` returns 0 and is overridden only by `XmlImporter` |
| `CsvImporter.cs` | `CsvTransactionImporter` | P6-CSV-1…P6-CSV-8, P6-ALL-1, P6-ALL-3 — see Open Questions 2, 3, 4 |
| `CsvImporter.cs` | `CsvMap`, `CsvFieldMap` | P6-CSV-1, P6-CSV-2, P6-CSV-7 — the remembered per-account column mapping, stored as XML in a `CsvMaps` folder |
| `CsvImporter.cs` | `TransactionCache` (nested) | P6-CSV-6 — the date-indexed duplicate check that makes re-importing an overlapping export safe |
| `CsvImporter.cs` | `TBag` (nested) | **Not user-facing** — one parsed row before it becomes a transaction |
| `CsvImporter.cs` | `UserCanceledException` | P6-ALL-5 — how cancelling the column-mapping dialog unwinds a CSV import without an error |
| `CsvImportController.cs` | `CsvImportController` | P6-CSV-1, P6-CSV-3, P6-CSV-5 — see Open Question 3 |
| `CsvDocument.cs` | `CsvDocument` (namespace `Walkabout.Utilities`) | P6-CSV-9, P6-CSV-10 — the delimited-file parser |
| `CsvTransactionFormat.cs` | `CsvTransactionFormat` (namespace `Walkabout.Data`) | P6-OUT-1, P6-OUT-3 — the shared row/column writer; also used by `MyMoney.Data`'s `CsvStore.Save`. See Open Question 9 |
| `QifImporter.cs` | `QifImporter` | P6-QIF-1…P6-QIF-8 — see Open Questions 5, 6, 7, 20 |
| `XmlImporter.cs` | `XmlImporter` | P6-XML-1…P6-XML-5 — see Open Question 8 |
| `Exporters.cs` | `Exporters` | P6-OUT-1, P6-OUT-2, P6-OUT-5, P6-XML-1, P6-XML-4 — see Open Questions 9, 10, 17, 21 |
| `DownloadData.cs` | `DownloadData` | P6-ENTRY-4, P6-OFX-6 — one row of the results panel, thread-safe so background downloads can update it; its `OfxError` setter is what turns an error row into an action link |
| `DownloadData.cs` | `DownloadEventArgs` | P6-ENTRY-4 — the collection of those rows a download or import publishes |
| `DownloadData.cs` | `DownloadProgress` (delegate) | **Not user-facing** — the progress callback shape |
| `IImportProgressReporter.cs` | `IImportProgressReporter` | **Not user-facing** — the seam that lets the QIF and CSV importers live in the business layer while the WPF project supplies the panel |

### `MyMoney.Business/Ofx/`

| File | Type | Status |
|---|---|---|
| `Ofx.cs` | `OfxThread` | P6-OFX-1, P6-OFX-2, P6-FILE-1, P6-ALL-4, P6-ALL-5 — the parallel download/import driver |
| `Ofx.cs` | `OfxRequest` | P6-OFX-3…P6-OFX-15, P6-FILE-1…P6-FILE-5, P6-INV-1…P6-INV-6, P6-BANKS-3, P6-ALL-1…P6-ALL-4 — see Open Questions 11, 13, 14, 16, 23 |
| `Ofx.cs` | `OfxMfaChallengeRequest` | P6-OFX-7 — see Open Questions 15, 16 |
| `Ofx.cs` | `OfxException` | P6-ENTRY-5, P6-OFX-6 — carries the code, raw response and headers the details page prints |
| `Ofx.cs` | `LogFileInfo` (internal) | P6-FILE-5 — remembers which log file a parsed response came from so the details page can link it |
| `OfxObjectModel.cs` | `OFX` + ~40 response types | P6-BANKS-3, P6-OFX-6, P6-OFX-7 — the deserialised sign-on, profile, sign-up and MFA responses. Individually **not user-facing**; what they enable is captured above |
| `OfxErrorCode.cs` | `OfxErrorCode` | P6-OFX-6 — the codes the 96 messages are keyed by |
| `OfxStrings.resx` + `.Designer.cs` | `OfxStrings` | P6-OFX-6 — the plain-English text for every refusal the standard defines |
| `OfxInstitutionInfo.cs` | `OfxInstitutionInfo` | P6-BANKS-1, P6-BANKS-2 — see Open Questions 18, 19 |
| `OfxInstitutionInfo.cs` | `ChangeTrackedField` | P6-BANKS-2 — the per-field "when was this last changed" stamp the merge decides on |
| `OfxInstitutionInfo.cs` | `LoadProviderList` | **Dead** — never called; `MergeProviderList` is what actually loads a directory document |
| `OfxInstitutionInfo.cs` | `ParseMoneyDancePythonScript` | **Dead** — a one-off scraper for another product's institution list, reachable from no UI or command. See Open Question 19 |
| `HtmlResponseException.cs` | `HtmlResponseException` | P6-FILE-3 |
| `SgmlParser.cs`, `SgmlReader.cs` | `Walkabout.Sgml.*` | **Not user-facing** — a vendored 2002 general-purpose SGML-to-XML reader, used only as the lenient fallback behind P6-FILE-2. 3,765 of the folder's ~10,000 lines |
| `ofx160.dtd`, `ofx201.dtd` | — | P6-FILE-2 — the grammars that lenient fallback reads against |
| `OfxProviderList.xml` | — | P6-BANKS-1 — the ~4,400-line institution directory that ships with the product |
| `MfaPhrases.xml` | — | P6-OFX-7 — the standard's identity-question wordings, read by Phase 4's `MfaChallengeDialog` |
| `OfxErrorTemplate.htm` | — | P6-ENTRY-5 — the failure report's layout |
| `OFX 2.1.1.pdf`, `ofx16.pdf` | — | **Not user-facing** — the protocol specifications, checked in as developer reference |

### `MyMoney/Ofx/`

| File | Type | Status |
|---|---|---|
| `OfxDownloadController.cs` | `OfxDownloadController` | P6-OFX-1, P6-OFX-2, P6-OFX-6, P6-ENTRY-5, P6-ALL-5 — the bridge between the results panel and the four recovery dialogs |

### Supporting files owned by other phases

| File / member | Status |
|---|---|
| `MainWindow.xaml.cs` import/export region | Phase 2's P2-FILE-11 / P2-START-3 / P2-CMD-11; the behaviour is P6-ENTRY-1…3, P6-OFX-1/2, P6-OUT-4/5. **See Open Questions 1 and 12** |
| `Dialogs/MoneyFileImportDialog.xaml.cs` | Phase 4's P4-IMPORT-1…6 for the conversation; the matching and writing is P6-MERGE-1…4 |
| `Dialogs/CsvImportDialog.xaml.cs` | Phase 4's P4-IMPORT-7…10; what the mapping is then used for is P6-CSV-1/2 |
| `WpfBusinessLayerUiCallback.cs` / `IBusinessLayerUiCallback.cs` | **Not user-facing** — the seam through which the business-layer importers reach dialogs, file pickers and the shell |
| `Controls/DownloadControl.xaml.cs` + `DownloadControlProgressReporter.cs` | Phase 2's P2-PANE-5; what it shows is P6-ENTRY-4. **See Open Question 24** |
| `Dialogs/SelectAccountDialog.xaml.cs` (`AccountHelper.PickAccount`) | Phase 4's account picker; P6-OFX-9 and P6-CSV-3 are what it is asked for here |
| `MyMoney.Data/CsvStore.cs` — `Save` | P6-OUT-4 — the "Save As .csv" destination. See Open Question 12 |
| `MyMoney.Data/CsvStore.cs` — `ImportCsv` (static) | **Dead** — a second, stricter CSV importer demanding exactly three ISO-dated columns, called from nowhere. See Open Question 21 |
| `MyMoney.Data/XmlStore.cs`, `BinaryXmlStore` | Phase 4's P4-DATA-* / Phase 8 — whole-file storage formats rather than interchange; referenced by P6-OUT-4 only |
| `MyMoney.Business/Utilities/ProcessHelper.cs` | **Not user-facing** — where the hand-off list and the log folder live; P6-ENTRY-2 |
| `Taxes/TxfExporter` | **Phase 7** — read only far enough to say an account can be exported that way (P6-OUT-2). See Open Question 22 |
| `Transactions.Merge`, `Transaction.Merge`, `FindPotentialDuplicate` | **Phase 1** (P1-IMPORT-1…6) — the matching rules every route here relies on |

---

## Open questions from Phase 6

Things a human should double-check, because the call was a judgement rather than obvious
from the code — and, where noted, because they look like real defects.

1. **Importing more than one file at a time imports the earlier ones repeatedly.**
   `MainWindow.OnCommandFileImport` builds four accumulating lists (`qifFiles`,
   `ofxFiles`, `csvFiles`, `moneyFiles`) *outside* its `foreach (string file in
   openFileDialog1.FileNames)` loop but drains them *inside* it, and never clears them.
   Select three `.qif` files and the first is imported three times, the second twice, the
   third once — six imports for three files. The same applies to `.ofx`, `.csv` and
   `.mmdb`: three data files selected together means the "merging only works if both
   started with the same state" warning and the merge window appear six times. The account
   picker is re-prompted on each repeat, and the "Loaded N transactions" total is wrong.
   Duplicate matching absorbs most of the damage for OFX (stable identifiers) and CSV
   (date/payee/amount), and QIF merge mode absorbs some, but a *first* QIF import into an
   empty account is not in merge mode (Open Question 7) and so genuinely doubles. The file
   dialog sets `Multiselect = true` deliberately, so this is a supported path, not an edge
   case. Moving the four drains after the loop is the whole fix.

2. **A multi-account spreadsheet strips the wrong columns off every row but the first.**
   `CsvTransactionImporter.GroupCsvByAccount` removes the "Account Number" and "Account"
   columns from each row as it groups, and adjusts `accountNameIndex` to account for the
   first removal — but `accountNameIndex` is declared outside the row loop, so the
   adjustment happens again on every row and walks the index down until it collides with
   `accountNumberIndex`. It happens to be harmless when the two columns are adjacent
   (which is the common layout, and presumably why it has survived), but with any column
   between them every row after the first has an innocent column deleted instead of the
   account name, shifting everything to its right by one. P6-CSV-3 describes the intent.

3. **Rows in a multi-account spreadsheet that don't name an account are silently
   dropped**, and **cancelling the account picker crashes the import.** In
   `GroupCsvByAccount`, a row whose account-number cell is empty falls off the end of the
   `if (!string.IsNullOrEmpty(accountNumber))` with no `else` — no count, no warning, no
   error row; the user just gets fewer transactions than the file had. Separately, in
   `CsvImportController.ImportCsvForAccount`, the account the user picked is dereferenced
   (`acct.Type`) with no null check, while the caller three lines later writes
   `if (count > 0 && acct != null)` — so cancelling the picker (which
   `AccountHelper.PickAccount` signals by returning null) throws a `NullReferenceException`
   that the outer `catch (Exception)` reports to the user as *"Object reference not set to
   an instance of an object"* under the title "Import Error".

4. **Accounting-style negative amounts import as positive.**
   `CsvTransactionImporter.MapField`'s amount case matches `([+-]?[\d,.]+)` and parses the
   captured group, so `($1,234.56)` — the form many banks and every `ToString("C2")` in
   this codebase produce for a negative — yields `1234.56`, positive. The `Negate` toggle
   (P6-CSV-7) is per-file, not per-row, so it can't rescue a file that mixes both. This
   compounds with Open Question 9: the product's own CSV export cannot be re-imported
   without every expense flipping sign.

5. **The QIF importer's own account prompt is misspelled, and it is the second time the
   user has been asked.** `QifImporter.Import` shows *"The following account does not
   currently exit"* (for "exist"). More substantially, `MainWindow.ImportQif` has already
   shown a full account picker for the same file immediately before calling the importer,
   so the user picks an account and is then asked whether to create a *different* account
   named after the file, and then — if they say no — asked a third time whether to merge
   into the one they already picked. Three questions for one file. The importer also
   decides everyday-vs-savings for a "Bank" file by testing whether the *file path*
   contains the word "Checking", which is not something a user could be expected to know.

6. **The QIF account-type check refuses correct merges and permits wrong ones.**
   `QifImporter.ImportQif` computes `bool accountTypeMismatch = at != a.Type;` **before**
   the `switch` that works out what `at` should be, while `at` is still its initialiser,
   `AccountType.Checking`. So for every file type except `!Type:Invst` (which recomputes
   the flag correctly) the comparison is against Checking rather than against the file's
   real type. Merging a credit-card QIF into a credit-card account therefore throws
   *"Account type Credit in QIF doesn't match selected account type Credit"* — a message
   that contradicts itself — while merging that same credit-card QIF into a *chequing*
   account passes the check and proceeds. One statement, two lines too early. P6-QIF-3
   describes the intent. The `default:` arm of the same `switch` also reports the
   destination account's type (`a.Type`) in its "not supported" message instead of the
   unsupported type read from the file, so the user is told their own account type is
   unsupported.

7. **A first-time QIF import marks nothing for review.** `ImportQif` sets
   `t.Unaccepted = true` only inside `if (merge)`, i.e. only when the destination account
   already had transactions. Importing years of history into a fresh account therefore
   lands it all pre-approved, while importing the same file into an account with one
   transaction in it lands it all needing approval. Every other route in — OFX, CSV,
   investment — marks what it adds unapproved unconditionally. P6-QIF-8 and P6-ALL-2
   describe the convention; QIF is the exception.

8. **Pasting an account always reports that it failed.** `AccountsControl.Paste` calls
   `XmlImporter.ImportAccount(xml)` and then tests `importer.LastAccount == null` to
   decide whether to show *"Clipboard doesn't seem to contain valid account information"*.
   But `LastAccount` is only ever assigned by `ImportXml` (the file path);
   `ImportAccount` returns the account and never touches the field, and the importer is
   freshly constructed on the line above. So the field is unconditionally null and the
   error dialog fires on every paste, successful or not — the account *is* created, and
   the user is told it wasn't. Phase 3's P3-ACCT-12 recorded the intended behaviour;
   this is what runs.

9. **The product's own CSV export cannot be read back by the product's own CSV importer.**
   `CsvTransactionFormat.WriteTransaction` formats every amount with `ToString("C2")`,
   which on a typical en-US machine writes negatives as `($1,234.56)`. Combined with
   Open Question 4, every expense round-trips as income. It also means the exported file
   carries the *machine's* currency symbol rather than the transaction's own, even though
   the writer has a dedicated Currency column for exactly that (the same class of bug
   Phase 5 recorded as its Open Question 7 for the account-summary report). Dates are
   written with `ToShortDateString()`, i.e. in the machine's own format, so the file is
   not portable between locales either. P6-OUT-1 records that an export exists without
   claiming it round-trips.

10. **`AccountsControl.ExportList` formats balances with `ToString("C3")`** — a currency
    symbol and *three* decimal places — while printing the account's real currency in a
    separate column right beside it, so a euro account reads `$1,234.000  EUR`. Three
    decimals appears nowhere else in the product. Same shape as Open Question 9 and as
    Phase 5's Open Question 7.

11. **The statement parser never reads the encoding the file declares.**
    `OfxRequest.ParseOfxResponse`'s XML branch builds a regular expression for the
    `encoding=` attribute and then runs it against a **hard-coded sample string**,
    `string test = "<?xml version='1.0' encoding='utf-8'?>";`, instead of against
    `content`. The match always succeeds and always yields `utf-8`, so an OFX 2.x
    statement declaring `windows-1252` or any other encoding is decoded as UTF-8 and any
    accented payee name is mangled. One identifier. The existence of the
    `ImportOFXAsUTF8` preference (Phase 2's P2-PREF-*) suggests this class of problem has
    been worked around from the outside rather than diagnosed. P6-FILE-2 describes the
    intent.

12. **"Save As" a spreadsheet does not save the file; it saves whatever list is on
    screen.** `MainWindow.OnCommandFileSaveAs`'s `.csv` case calls
    `ExportCsv` → `new CsvStore(filename, this.TransactionView.Rows)`, which writes the
    current transaction view's rows and nothing else — no accounts, no categories, no
    payees, no securities, and none of the transactions in accounts the user isn't looking
    at. It sits in the same menu, and the same switch statement, as three choices
    (`.sdf`, `.mmdb`, `.xml`/`.bxml`) that genuinely do save the whole file. A user who
    picks it as a way of taking a copy away gets a fraction of their data with no
    indication. P6-OUT-4 describes the choice; P6-OUT-1 is what it actually does.

13. **Saved conversation logs mask credentials but not account numbers.**
    `OfxRequest.SaveLog` blanks nine credential-bearing elements, which is the hard part
    done — but the `BANKID`, `ACCTID` and `BROKERID` values (and every transaction in the
    response) are written to `OfxLogs\` in the clear, and P6-ENTRY-5's whole purpose is to
    get the user to hand that file to someone else. Worth a decision about what the
    details page should offer to share. (The same method also assigns
    `string value = e.Value;` and never uses it.)

14. **A failed protocol-version retry leaves the institution mis-configured.**
    `OfxRequest.SendOfxRequest(XDocument)` flips `onlineAccount.OfxVersion` between "1"
    and "2" before retrying and never restores it if the retry also fails — so an
    institution that was correctly set to version 2 and had a transient outage is left
    recorded as version 1, and the next download starts from the wrong guess.
    `OfxRequest.Signup`, five hundred lines away, does the same retry and *does* restore
    the original on failure, with a comment saying why. Two copies of one pattern, one of
    them fixed. P6-OFX-5 describes the intent.

15. **An institution that asks only machine-identifying questions fails the download.**
    `OfxMfaChallengeRequest.HandleChallenge` answers the seven standard machine questions
    itself and then, if *no* questions were left for the user (`userChallenges.Count > 0`
    is false), falls straight through to
    `OnError(new OfxException("Server returned unexpected response from MFA Challenge
    Request"))` — discarding the answers it just worked out. The successful case is only
    reachable when at least one question needs a human. P6-OFX-7 describes the intent;
    `OfxDownloadController.OnChallengeCompleted` has a matching `else` branch for
    "built-in answers only" that can therefore never be reached.

16. **One of the built-in identity answers is in 12-hour time.** The same method answers
    the standard's "current date and time, formatted YYYYMMDDHHMMSS" question with
    `DateTime.Now.ToString("yyyyMMddhhmmss")` — lowercase `hh`, which is 12-hour and drops
    the AM/PM. Every answer given after midday is off by twelve hours. An institution
    that validates the value will reject the sign-on for a reason nothing in the product
    can explain.

17. **The account-relationship diagram only draws one direction and silently skips
    accounts.** `Exporters.ExportDgmlAccountMap` adds a link only where
    `t.TransferTo != null && t.Amount < 0`, so each transfer pair contributes one arrow
    (which is arguably the point), but it also iterates *every* transaction in the file
    including deleted ones, dereferences `t.Account.Type` with no null guard, and wraps
    the label formatting in a bare `catch {}` that swallows any failure — so a link whose
    total can't be converted keeps whatever raw value it had. It is also the only export
    in the product with no error handling around the file write at all: a failure here
    propagates out of `OnCommandFileExportAccountMap` unhandled. P6-OUT-5 describes the
    feature.

18. **The institution directory is fetched over plain HTTP.** Both
    `OfxHomeProviderList` (`http://www.ofxhome.com/api.php?all=yes`) and
    `OfxHomeProviderInfo` (`http://www.ofxhome.com/api.php?lookup={0}`) are `http://`, not
    `https://`. What comes back includes the URL the product will subsequently post the
    user's banking credentials to, and it is merged into the user's local directory and
    saved. `OfxRequest.SendOfxRequest` does upgrade a bare institution address to `https://`
    before posting, which limits the damage, but an institution entry whose stored URL
    already begins `http://` is left alone. This is the most security-relevant thing in
    the phase and deserves a deliberate decision rather than inheritance.

19. **The directory merge can fold two institutions into one, and re-saves on every
    read.** `UpdateCachedProfile` matches an institution against the cached list with
    `string.Compare(item.MoneyDanceId, profile.MoneyDanceId) == 0 ||
    string.Compare(item.OfxHomeId, profile.OfxHomeId) == 0`, which is **true when both
    sides are empty** — so a profile with no identifiers merges into the first cached
    entry that also has none. The file has a helper written for exactly this,
    `IsMergable`, which returns "not enough information" for the empty case and is used by
    `MergeProviderList` two hundred lines away, but not here. Separately,
    `GetCachedBankList` calls `SaveList` unconditionally, so simply *reading* the
    directory rewrites the ~4,400-entry file on disk — a get with a visible side effect,
    the same pattern Phase 5 raised as its Open Question 29. And `SetValue`'s
    change-detection compares `object` references (`if (field.Value != value)`), so equal
    non-interned strings register as changes and bump the "last changed" stamp the whole
    merge policy depends on. Also in this file: `ParseMoneyDancePythonScript`, a scraper
    for a competitor's institution list, is dead code reachable from no command.

20. **The QIF importer has no update scope and no rollback.** Every other route in wraps
    its writes in `BeginUpdate`/`EndUpdate` (P6-ALL-1); `QifImporter.ImportQif` does not.
    Combined with its own design — it `throw`s on the *first* field code it doesn't
    recognise (`"Unknown format '{0}' on line {1}"`), on a malformed status line, on a
    split without an amount, and on an illegal self-transfer — a file that is 90% valid
    leaves 90% of its transactions committed, no way to tell which, and a message box
    naming a line number. Every list churns a row at a time while it runs. `QIF` also has
    no notion of the "!Account"/"!Type:Cat"/"!Type:Class" sections real exports contain, so
    a whole-file Microsoft Money export fails on its first line with *"Account type Cat not
    supported"*. Whether QIF import is worth keeping at all is a redesign question; if it
    is, this is where to start.

21. **Two dead CSV importers and an export format nobody can reach.**
    `MyMoney.Data/CsvStore.ImportCsv` is a complete second CSV importer — stricter,
    demanding exactly three ISO-dated `Date,Payee,Amount` columns — called from nowhere;
    it reads as the ancestor of the mapping-based one. And `Exporters.SupportXml`, the
    flag that decides whether the export file dialog offers XML alongside CSV, is assigned
    in exactly one place in the codebase (`LoansView`, to `false`) and never to `true`, so
    `ExportPrompt` always offers CSV only. XML export is reachable only through
    `AccountsControl`'s own save dialog, which bypasses `ExportPrompt` and passes a
    filename directly. `Exporters.ExportAccount` also dedupes against an instance field
    (`this.accounts`) that is never cleared, so reusing one `Exporters` for two exports
    would silently omit the accounts from the second — currently unreachable because every
    call site constructs a fresh one, but a trap of the same shape as Phase 5's Open
    Question 21.

22. **Where the Phase 7 line was drawn.** The brief asked whether `.txf` *file-writing
    mechanics* belong here. They don't, on the evidence: `TxfExporter` lives in `Taxes/`,
    not in `Importers/` or `Ofx/`, nothing in either of this phase's folders writes or
    reads `.txf`, and the format is a tax-data serialisation rather than a general
    interchange format — the record layout and what goes in each field *is* the tax
    semantics. Phase 6 therefore records only that an account can be exported that way
    (P6-OUT-2) and that the export is gated by a date-range dialog; everything inside
    `TxfExporter` is Phase 7's, consistent with Phase 5's Open Question 30. The same line
    means Phase 6 does not describe what `ExportCapitalGains` classifies as short- or
    long-term.

23. **Nothing checks the currency of anything that is downloaded.** There are two guards
    for this and neither runs. `OfxRequest.CheckUSD` — called before every bank, card and
    investment statement is applied — has its entire body commented out and
    `return true;` left behind, so the statement's declared currency is read and
    discarded. `ProcessCurrency`, which compares a *transaction's* currency against the
    account's, is written as
    `if (this.currencyErrors.Contains(symbol)) { …AddError…; this.currencyErrors.Add(symbol); }`
    — the guard is inverted, testing the "already reported this one" set for presence
    instead of absence and adding to it inside the branch that can therefore never run. A
    set that starts empty and is only added to inside a branch gated on it being non-empty
    stays empty forever, so a foreign-currency transaction is booked into a
    local-currency account with no error row, no log line and no warning. (It is also
    only wired into the investment path, never the bank or card ones.) For a product that
    models currencies as thoroughly as Phase 1's P1-CUR-* describes, the import side
    knows nothing about them. Nearby in the same file, `ProcessSecId` reads
    `if (string.IsNullOrEmpty(uniqueId)) { idType = "CUSIP"; }` — testing `uniqueId`
    where `idType` was obviously meant, on a local that is then never used at all.

24. **Where the Phase 8 line was drawn, and one loop that never ends.** Attachments and
    statement documents are brought across by a data-file merge (P6-MERGE-3), and the
    stock-price cache is consulted by the CSV importer (P6-CSV-5) — both are captured here
    only as far as "the import brings them", with the managers themselves left to Phase 8.
    The results panel is Phase 2's. One thing found in it worth noting regardless:
    `DownloadControl.SelectEntry` re-posts itself every 100ms until it finds a tree item
    for the entry it was given, with no attempt limit and no timeout, so an entry that
    never appears in the tree leaves a timer firing for the life of the session. Also in
    `MainWindow`: `DoSync` sets `isSynchronizing = true` and clears it in a `finally` that
    runs before the download it started has done anything, so
    `CanSynchronizeOnlineAccounts`'s guard against starting a second download never
    engages — the same is true of the `MenuSync.IsEnabled` pair around it.

---

# Phase 7: Taxes

**Source examined (in full):** every file in `Source/WPF/MyMoney.Business/Taxes/`
(5 code files, 2 data files, 1 embedded specification — ~1,000 lines of code plus
~4,500 lines of data), plus `Source/WPF/MyMoney.Business/CostBasis.cs`, the lot-matching
engine the capital-gains calculator derives from, which no earlier phase owned.

> **Path correction — the brief's guess and `CLAUDE.md` are both wrong, again.**
> There is no `Source/WPF/MyMoney/Taxes/` folder; a `Glob` for it returns nothing. The
> tax code lives at **`Source/WPF/MyMoney.Business/Taxes/`**, the same
> `MyMoney.Business` extraction Phases 1 and 6 flagged for `Money.cs`, `Importers/` and
> `Ofx/`. The two files that *read* like tax code and are named as such —
> `Reports/TaxReport.cs` and `Reports/W2Report.cs` — are **not** in this folder and are
> Phase 5's.

| File | Lines | What it is |
|---|---|---|
| `Taxes/TaxCategory.cs` | 401 | `TaxForm`, `TaxCategory`, `TaxCategoryCollection`, `SortOrder` — the tax-line catalogue and the grouping of a user's transactions under it |
| `Taxes/CapitalGains.cs` | 121 | `CapitalGainsTaxCalculator` — short/long-term classification on top of `CostBasisCalculator` |
| `Taxes/TxfExporter.cs` | 247 | the `.txf` writer for consumer tax software |
| `Taxes/FederalTaxes.cs` | 211 | `FederalTaxes`, `TaxFilingStatus`, `StandardDeduction`, `Brackets`, `Bracket` — federal bracket arithmetic |
| `Taxes/StateTaxes.cs` | 209 | `StateTaxes`, `StateData`, `CapitalGains` (the state one) — per-state bracket and capital-gains arithmetic |
| `Taxes/FederalTaxes.json` | 182 | the federal standard deduction, income brackets and capital-gains brackets |
| `Taxes/StateTaxes.json` | 2,691 | 51 jurisdictions (50 states + DC): tax system, brackets, standard deduction, capital-gains treatment |
| `Taxes/TxfSpec.txt` | 1,626 | the TXF format specification, embedded as a resource; its category table is also the product's tax-line catalogue |
| `MyMoney.Business/CostBasis.cs` | 885 | `SecurityPurchase`, `SecuritySale`, `SecurityGroup`, `SecurityFifoQueue`, `AccountHoldings`, `CostBasisCalculator` — FIFO lot matching. See Open Question 24 |

**Supporting files read but owned elsewhere**, because a scenario here can't be written
honestly without them: `Reports/TaxReport.cs` (what it asks `Taxes/` for and how it
presents the answer — the screen is Phase 5's P5-TAX-1…7), `Reports/W2Report.cs` (same,
P5-TAX-8…10), `Dialogs/CategoryDialog.xaml.cs` + `.xaml` (the tax-line picker — the
dialog is Phase 4's P4-CAT-1/P4-CAT-4), `View Selectors/AccountsControl.xaml.cs`'s `Export`
(the per-account `.txf` route — Phase 3's P3-ACCT-10, Phase 6's P6-OUT-2),
`View Selectors/RetirementControl.xaml.cs` (where filing status and state of residence
are entered — Phase 3's P3-PANEL-3, Phase 5's P5-PLAN-1), `Reports/RetirementPlan.cs`'s
`FindState`/`PayIncomeTaxRecursively` call sites (the only consumer of the bracket
arithmetic), and `Money.cs`'s `Category.TaxRefNum`, `Categories.ReParent` and
`ImportCategory` (Phase 1's P1-TAX-1).

> **Naming notes.**
> - `Taxes/TxfExporter.cs` declares its class in namespace **`Walkabout.Importers`**,
>   not `Walkabout.Taxes` — it is the only file in the folder that does, and it is the
>   mirror image of `Reports/W2Report.cs`, which is in `Walkabout.Taxes` while sitting in
>   `Reports/`. The two tax-adjacent files each live in the other's namespace.
> - `Taxes/CapitalGains.cs` contains no type called `CapitalGains`; the type is
>   `CapitalGainsTaxCalculator`. Meanwhile `Taxes/StateTaxes.cs` *does* declare a public
>   type called `CapitalGains` (a state's capital-gains rules), so the name and the
>   filename belong to different files.
> - `TaxCategory.Groups`' doc comment says *"Generated by
>   `TaxCategoryCollection.GenerateReport`"*. There is no `GenerateReport`; the method is
>   `GenerateGroups`. `TaxCategory.Level`'s comment reads *"A valud from 0-2"*.
> - `MyMoney.csproj` still carries `<None Remove="Reports\FederalTaxes.json" />` for a
>   file that has never existed at that path.

**What this phase covers and deliberately does not.** This section catalogues the
*tax meaning* the product attaches to a user's ordinary records: what a tax line is and
how a user's own categories get attached to one, how the product decides that a sale
produced a short- or long-term gain and what it cost, what actually goes into the file
handed to tax software, and what the bracket tables let the product estimate. The
*screens* that present any of this are Phase 5's (P5-TAX-*, P5-PLAN-*) and the *dialogs*
that collect the options are Phase 4's (P4-CAT-1/4, P4-REPORT-4…6); both are referenced, not
re-described. The two `TaxReport` defects Phase 5 found are already GitHub issue #55 and
are not re-litigated here. The retirement simulation's own logic — RMD ages, the
withdrawal order, social-security taxation — is Phase 5's P5-PLAN-7 and its bugs are in
issue #55 too; what is captured here is only the bracket arithmetic in `Taxes/` that it
calls into. Sales-tax totals, the fiscal-year definition and the per-transaction tax
date are Phase 1's P1-TAX-2/3/5 as model capabilities and appear here only where the tax
code consumes them.

---

## 7.1 Telling the product which records are tax-relevant

### P7-LINE-1 — Say which line of which tax form a category feeds
For any category they have created, the user can nominate the specific line of a
specific tax form that spending or income filed under it belongs on — "Schedule A,
state income taxes", "1099-INT, interest income", "W-2, federal tax withheld". They do
this once, on the category, and never classify an individual transaction for tax
purposes again.
*(`Category.TaxRefNum` ← `TaxCategory.RefNum`, set from `CategoryDialog`'s tax-category
picker)*

### P7-LINE-2 — Choose from the set of lines consumer tax software actually understands
The list the user picks from is not invented by this product: it is the published
catalogue of tax lines that consumer tax software imports, covering 35 forms and
schedules — Form 1040, Schedules A, B, C, C-EZ, D, E, F and H, the W-2 and W-2G, the
1099 family (INT, DIV, MISC, R, G, Q, SA, OID), a Schedule K-1 worksheet, and
special-purpose forms for childcare, moving, casualty losses, home office, education and
adoption — with roughly 380 individual lines beneath them.
*(`TaxCategoryCollection.Load` reading the category table out of the embedded
`TxfSpec.txt`. See Open Question 10 — the catalogue is from tax year 2004.)*

### P7-LINE-3 — Find the right line by typing rather than scrolling
Because there are hundreds of lines, the user narrows them by typing: matching either
the form's name or the line's own wording, so "sched a" and "mortgage" both get them
somewhere useful.
*(`CategoryDialog.ComboBoxForTaxCategory_FilterChanged` matching on `FormName` and
`Name`)*

### P7-LINE-4 — Take a tax association off again
The picker carries a blank entry at the top, so a category that was wrongly associated
can be returned to "not tax-relevant" without deleting and recreating it.
*(the empty `TaxCategory` inserted at index 0; `TaxRefNum = 0` means none)*

### P7-LINE-5 — Point several of their own categories at one tax line
The user's own category tree does not have to be shaped like a tax form. Several
different categories — three different charities, or several employers' withholding —
can all feed the same line, and the product adds them together for that line while still
being able to show them separately.
*(the one-to-many `TaxCategory` → `List<Category>` map built by
`TaxCategoryCollection.GenerateGroups` and by `W2Report.GenerateForm`)*

### P7-LINE-6 — Keep the association when the category moves or is brought in from elsewhere
Reorganising the category tree, or merging in another copy of the books, carries the tax
association along with the category rather than quietly dropping it, so the work of
classifying is not repeated.
*(`Categories.ReParent` and `Categories.ImportCategory` both copying `TaxRefNum`)*

---

## 7.2 Turning a year's records into tax-form figures

### P7-SUM-1 — Get one figure per tax line for a chosen year
For a year the user picks, every category they have associated with a tax line is
gathered, every transaction filed under it in that year is added up, and the result is
one figure per tax line — the number they would copy onto the form.
*(`TaxCategoryCollection.GenerateGroups`)*

### P7-SUM-2 — Have each line broken down the way that form expects it
Some tax lines need naming the party involved (who paid the interest, who received the
donation), some need naming the asset or account, and some are just a total. The
catalogue records which, and the product sub-totals each line accordingly — by payee, by
account, or by the user's own category — rather than making the user work out what
detail the form wants.
*(`SortOrder` from the catalogue's sort column, applied per tax line in
`GenerateGroups`. See Open Question 8.)*

### P7-SUM-3 — Have sheltered accounts left out without being asked
Activity inside retirement and other tax-advantaged accounts is excluded from the tax
figures automatically, because it isn't reportable. The user marks the account's tax
status once and never thinks about it again.
*(`Account.IsTaxDeferred`/`IsTaxFree` filtered in `GenerateGroups`; Phase 1's P1-TAX-4)*

### P7-SUM-4 — Have the year decided per transaction, not just by the calendar
A transaction the user has given a separate tax date — a December payment that counts
for the following year, say — falls into the tax year its tax date says, not the one its
calendar date says.
*(`Transaction.TaxDate` used as the range test throughout `GenerateGroups`; Phase 1's
P1-TAX-3. See Open Question 22.)*

### P7-SUM-5 — Have income and deductions carry the sign the form expects
The catalogue records whether a line is income or an expense, and the figure is presented
in the direction the form wants — a deduction shown as a positive amount to enter, not as
the negative number it is in the register.
*(`TaxCategory.DefaultSign` from the catalogue's sign column. **See Open Question 6 — the
report and the export disagree about this.**)*

### P7-SUM-6 — Separate tax-exempt income from taxable income on the same line
Where a line's transactions include income from holdings the user has marked as not
taxable — municipal-bond interest and the like — that part is reported as its own figure
beside the taxable part rather than silently folded in.
*(`Security.Taxable` tested per transaction in `TaxReport.GenerateCategories`. **See
Open Question 6 — the exported file does not make this distinction.**)*

### P7-SUM-7 — Narrow the exercise to investment activity only
A user whose only tax-relevant records are trades can limit the whole exercise to
transactions with an investment attached, so a decade of ordinary household categories
doesn't have to be classified first.
*(the `investmentsOnly` flag through `GenerateGroups`; the choice itself is Phase 5's
P5-TAX-4)*

### P7-SUM-8 — Be told they haven't started rather than shown an empty form
If no category has been associated with any tax line, the product says so and says where
to go and do it, instead of producing a blank report that looks like a zero-tax year.
*(`GenerateGroups` returning `null` when the map is empty, handled by both tax reports —
Phase 5's P5-TAX-9)*

---

## 7.3 Working out what an investment sale did for tax purposes

### P7-GAIN-1 — Have shares sold matched against shares bought, oldest first
When the user sells part of a holding they have built up over years, the product decides
which shares those were — the oldest ones — and therefore what they originally cost. A
single sale can produce several tax rows if it drew on several purchases at different
prices.
*(`SecurityFifoQueue` and `CostBasisCalculator`; the assumption is FIFO and the user is
not offered an alternative)*

### P7-GAIN-2 — Have what they actually paid count as the cost, not the sticker price
The cost the gain is measured against is what left their account — commissions, fees and
load included — and the proceeds are what actually arrived, net of the costs of selling,
so the gain is the real one rather than a price-times-quantity approximation.
*(`Investment.OriginalCostBasis` preferring the transaction amount over unit price ×
units; Phase 1's P1-INV-6)*

### P7-GAIN-3 — Have stock splits folded in without re-entering history
A holding that split while it was held keeps the same total cost spread over the new
number of shares, and the original purchase date, so the gain and the holding period
come out right without the user restating old purchases.
*(`CostBasisCalculator.ApplySplit`; Phase 1's P1-INV-10)*

### P7-GAIN-4 — Have each sale classified as short-term or long-term
Every reportable sale is sorted into the two buckets tax law treats differently — held
about a year or less, and held longer — because they are taxed at different rates and
reported on different lines.
*(`CapitalGainsTaxCalculator.CalculateCapitalGains`. **See Open Question 2 — the
boundary is a day out in leap years.**)*

### P7-GAIN-5 — Have many small lots collapsed into a reportable number of rows
Where a sale drew on many purchases at the same price, or where the same holding was sold
repeatedly, the rows are combined so the user files a handful of lines rather than
hundreds. A combined row that no longer has a single purchase date is reported as
acquired on various dates, which is what the form allows.
*(`CapitalGainsTaxCalculator.Consolidate`, `SecuritySale.Consolidate`, the "VARIOUS"
convention)*

### P7-GAIN-6 — Choose whether lots are combined by when they were bought or when they were sold
The user picks which of the two the rows are grouped on, which changes how a partial sale
of a long-held position is presented.
*(the `consolidateOnDateSold` flag; the choice itself is Phase 5's P5-TAX-5)*

### P7-GAIN-7 — Have cost basis follow shares moved between their own accounts
Moving a holding from one brokerage to another is not a sale. The original purchase dates
and costs travel to the receiving account, so a later sale there is still measured against
what was originally paid and still counts as long-held.
*(the transfer branch of `CostBasisCalculator.Calculate`)*

### P7-GAIN-8 — Have sales inside sheltered accounts left out
Sales inside retirement and tax-free accounts produce no reportable gain and are excluded
from both the report and the exported file.
*(`ignoreTaxDeferred` in the calculator plus the explicit `IsTaxFree`/`IsTaxDeferred`
filters in `TaxReport` and `TxfExporter`)*

### P7-GAIN-9 — Be shown the sales the product can't work out a cost for
Where shares were sold that the records never show being bought — transferred in long ago,
inherited, or simply missing — the product is supposed to list those sales separately,
with everything it does know, so the user knows exactly which figures they have to look up
themselves rather than being handed a fabricated cost of zero.
*(`CapitalGainsTaxCalculator.Unknown`, the "Capital Gains with Unknown Cost Basis"
section, TXF record 673. **See Open Question 3 — none of this can currently happen.**)*

### P7-GAIN-10 — Have the rounding go against the taxpayer, deliberately
Fractional pennies are rounded so that income is never understated, on the stated
reasoning that a few cents in the tax authority's favour is cheaper than filing a rounding
adjustment.
*(`TxfExporter.Round`, `TaxReport.GiveUpTheFractionalPennies`)*

---

## 7.4 Handing the year over to tax software

### P7-TXF-1 — Produce a file their tax software can read
Rather than retyping figures, the user writes the year out in the interchange format
consumer tax packages import, and opens it there. The file names the product and the date
that produced it so the tax package can say where the figures came from.
*(`TxfExporter.Export`, `WriteHeader`; the on-screen affordance is Phase 5's P5-TAX-7 and
Phase 6's P6-OUT-2)*

### P7-TXF-2 — Have each tax line written in the shape that line expects
A line that just needs a total is written as one record; a line that needs the parties
named is written as one record per party plus a total. The user doesn't choose — the
catalogue says which each line is.
*(`TaxCategory.RecordFormat` driving `WriteRecordFormat1`/`WriteRecordFormat3`. **See Open
Question 7 — most record shapes are unimplemented.**)*

### P7-TXF-3 — Have every reportable sale written out individually
Each short-term and long-term sale becomes its own record carrying the quantity and
holding, the acquisition date (or "various"), the date sold, the cost and the proceeds —
the columns the capital-gains schedule asks for.
*(`WriteRecordFormat4` with the short-term and long-term line numbers)*

### P7-TXF-4 — Export either the whole year or one account's trades
The whole year's tax picture can be exported from the tax report, and — separately —
a single investment account's capital gains can be exported on its own from the account
list, for a user who only needs to hand over one brokerage's trades.
*(`TxfExporter.Export` vs `ExportCapitalGains(Account, …)` from
`AccountsControl.Export`. **See Open Question 4 — the per-account file is missing its
header.**)*

### P7-TXF-5 — Say which year, and how sales are grouped, before exporting
Both routes ask the user which tax year to export and how sales should be consolidated
before writing anything, and both honour a financial year that doesn't start in January.
*(`TaxReportDialog` — Phase 4's P4-REPORT-4…6 — and the tax report's own year picker)*

### P7-TXF-6 — Be told plainly if the export fails
A file that can't be written — a locked path, a full disk — produces a plain message
naming the problem rather than a silent no-op or a crash.
*(`TaxReport.OnExportTaxInfoAsTxf`'s `try`/`catch`)*

---

## 7.5 Reconstructing the pay records behind a tax form

### P7-W2-1 — Have the itemised lines of a paycheque read as tax-form boxes
A paycheque the user records as one deposit broken into gross pay, federal tax withheld,
social security, medicare, state tax and pre-tax deductions can be read back as the boxes
of the corresponding tax form, so what their employer will report can be checked against
what they actually banked.
*(`W2Report.Summarize` walking each transaction's splits; the screen is Phase 5's
P5-TAX-8)*

### P7-W2-2 — Have every form they have touched appear, and no others
Each of the 35 forms in the catalogue is tried in turn, and a form appears only if the
user has records that feed at least one of its lines — so a user with only a W-2 and
some bank interest sees two tables, not thirty-five empty ones.
*(`W2Report.Generate` looping `GetForms()`, `GenerateForm` returning false when nothing
matched)*

### P7-W2-3 — See a rolled-up line open into the categories behind it
Where several of the user's own categories feed one line of the form, the line shows the
combined figure and opens to show each category's share — and each share leads back to
the transactions that produced it.
*(`W2Report.GenerateForm`'s expandable row group; the navigation is Phase 5's P5-TAX-10)*

---

## 7.6 Estimating what would actually be owed

> Boundary: these are capabilities of the bracket tables in `Taxes/`. The only thing that
> calls them today is the retirement projection (Phase 5's P5-PLAN-*), and they are worth
> cataloguing separately because they are a general "what would this cost in tax"
> capability that happens to have exactly one consumer.

### P7-EST-1 — Have tax estimated from real progressive brackets, not a flat guess
Income tax is worked out the way it really works — a standard deduction first, then each
successive band of what's left taxed at its own rate — rather than by applying one
average rate, so the answer is right at the edges as well as in the middle.
*(`FederalTaxes.GetIncomeTax`, `StateData.GetIncomeTax`. See Open Question 16.)*

### P7-EST-2 — Ask what one more chunk of income would cost
The product can answer "given I have already taken this much this year, what does taking
this much more cost me in tax?" — the question behind deciding how much to withdraw, and
the reason a withdrawal has to be grossed up to cover its own tax.
*(the `baseIncome` + `paycheck` shape of both `GetIncomeTax` methods)*

### P7-EST-3 — Say how they file
Single, married filing jointly, married filing separately or head of household, with the
matching brackets and standard deduction applied.
*(`TaxFilingStatus`; entered in `RetirementControl`. **See Open Question 14 — two of the
four statuses are approximations.**)*

### P7-EST-4 — Have long-held gains taxed at their own lower rates
Long-term capital gains are estimated against their own preferential rate bands rather
than as ordinary income, because that is the whole point of holding something for more
than a year.
*(`FederalTaxes.GetCapitalGainsTax`. **See Open Question 1 — the bands are applied to the
gains alone, which is not how they work.**)*

### P7-EST-5 — Have where they live taken into account
The user names the state they pay tax in and its own rules apply on top of the federal
ones: its brackets, its standard deduction, and its own treatment of capital gains.
*(`StateTaxes`/`StateData`, 51 jurisdictions; chosen in `RetirementControl`)*

### P7-EST-6 — Have states that simply don't tax something handled as such
A state with no income tax contributes no income tax, and a state that doesn't tax capital
gains contributes none — without the user having to know which states those are.
*(`StateData.NoCapitalGainsTax`, the empty bracket tables carried for the nine
no-income-tax states)*

### P7-EST-7 — Have a state's own capital-gains scheme applied
States vary: some tax gains exactly as income, some at a single flat rate above a large
exemption, and some add a surcharge above a threshold. All three shapes are modelled.
*(`StateData.GetCapitalGainsTax`'s fixed-rate, surcharge and taxed-as-income branches.
**See Open Questions 11 and 15.**)*

### P7-EST-8 — Have brackets drift upward over a long projection
Because a thirty-year projection in today's brackets would show everyone drifting into the
top rate, the user can say how fast they expect brackets to move and have the tables
widened year by year.
*(`FederalTaxes.InflateBrackets`, `StateData.InflateBrackets`. **See Open Question 12 —
only some of the tables move.**)*

---

## Phase 7 coverage checklist

Every file in `MyMoney.Business/Taxes/`, plus the one supporting file no earlier phase
owned. "Not user-facing" entries are parsing and plumbing the user never perceives
directly.

### `MyMoney.Business/Taxes/`

| File | Type | Status |
|---|---|---|
| `TaxCategory.cs` | `TaxForm` | P7-LINE-2, P7-W2-2 — one form or schedule and the lines under it |
| `TaxCategory.cs` | `TaxCategory` | P7-LINE-1…P7-LINE-5, P7-SUM-2, P7-SUM-5, P7-TXF-2 — one line of one form, and everything the catalogue knows about how to report it |
| `TaxCategory.cs` | `TaxCategoryCollection` | P7-LINE-2, P7-LINE-3, P7-SUM-1…P7-SUM-8 — the loaded catalogue and the grouping pass. See Open Questions 8, 9 |
| `TaxCategory.cs` | `SortOrder` | P7-SUM-2 — whether a line is broken down by payee, by account or by category |
| `TaxCategory.cs` | `TaxCategory.Parse`/`GetTokens`/`ParseInt`/`ParseSign`/`ParseSortOrder` | **Not user-facing** — the fixed-width/quoted-token reader for the catalogue table. Exercised directly by `UnitTests/TaxCategoryTests.cs` |
| `CapitalGains.cs` | `CapitalGainsTaxCalculator` | P7-GAIN-4, P7-GAIN-5, P7-GAIN-6, P7-GAIN-8, P7-GAIN-9 — see Open Questions 2, 3 |
| `TxfExporter.cs` | `TxfExporter` (namespace `Walkabout.Importers`) | P7-TXF-1…P7-TXF-4, P7-GAIN-10 — see Open Questions 4, 5, 6, 7 |
| `FederalTaxes.cs` | `FederalTaxes` | P7-EST-1, P7-EST-2, P7-EST-4, P7-EST-8 — see Open Questions 1, 12, 13, 16 |
| `FederalTaxes.cs` | `TaxFilingStatus` | P7-EST-3 — see Open Question 14 |
| `FederalTaxes.cs` | `StandardDeduction`, `Brackets`, `Bracket` | **Not user-facing** — the deserialised shape of the bracket tables; `Bracket.Max` is never read by any calculation (Open Question 16) |
| `StateTaxes.cs` | `StateTaxes` | P7-EST-5 — the loaded 51-jurisdiction table |
| `StateTaxes.cs` | `StateData` | P7-EST-1, P7-EST-5, P7-EST-6, P7-EST-7, P7-EST-8 — see Open Questions 11, 12, 15 |
| `StateTaxes.cs` | `CapitalGains` (the state one) | P7-EST-7 — a state's capital-gains scheme. Three of its seven fields are never read (Open Question 11) |
| `FederalTaxes.json` | — | P7-EST-1, P7-EST-4 — 2026-shaped federal figures, with no year recorded anywhere in the file (Open Question 13) |
| `StateTaxes.json` | — | P7-EST-5…P7-EST-8 — 51 jurisdictions, self-described as "2026 effective rates", version 2026.05.25. See Open Questions 11, 17 |
| `TxfSpec.txt` | — | P7-LINE-2, P7-SUM-2, P7-SUM-5, P7-TXF-2 — embedded as a resource; its category table between the `^ START OF TABLE`/`^ END OF TABLE` markers is the product's entire tax-line catalogue (35 forms, ~380 lines). The other ~1,150 lines are the format specification itself and are **not user-facing**. See Open Question 10 |

### Supporting file no earlier phase owned

| File | Type | Status |
|---|---|---|
| `MyMoney.Business/CostBasis.cs` | `CostBasisCalculator` | P7-GAIN-1, P7-GAIN-3, P7-GAIN-7 — also the engine behind Phase 5's portfolio report. See Open Question 24 |
| `CostBasis.cs` | `SecurityPurchase` | P7-GAIN-1, P7-GAIN-2 — one surviving purchase lot |
| `CostBasis.cs` | `SecuritySale` | P7-GAIN-1, P7-GAIN-5, P7-GAIN-9 — one matched sale, its cost and its gain. Its `Error` field is never assigned by anything (Open Question 3) |
| `CostBasis.cs` | `SecurityFifoQueue` (internal) | P7-GAIN-1 — the oldest-first queue per holding per account. See Open Question 3 |
| `CostBasis.cs` | `AccountHoldings` | P7-GAIN-1, P7-GAIN-7 — one account's lots |
| `CostBasis.cs` | `SecurityGroup` | **Phase 5** — the portfolio report's grouping, not tax |
| `CostBasis.cs` | `ComputeEstimateDividendYield`, `ComputeEstimatedAnnualDividends` | **Phase 5** — feeds the retirement projection's dividend income, not tax |

### Supporting files owned by other phases

| File / member | Status |
|---|---|
| `Reports/TaxReport.cs` | Phase 5's P5-TAX-1…7 for the screen; what it asks `Taxes/` for is P7-SUM-*, P7-GAIN-*, P7-TXF-1. Its two known defects are GitHub issue #55 |
| `Reports/W2Report.cs` (namespace `Walkabout.Taxes`) | Phase 5's P5-TAX-8…10 for the screen; the tax meaning is P7-W2-1…3. See Open Questions 22, 23 |
| `Dialogs/CategoryDialog.xaml.cs` + `.xaml` | Phase 4's P4-CAT-1/P4-CAT-4 for the dialog; the tax-line picker's meaning is P7-LINE-1…4 |
| `View Selectors/AccountsControl.xaml.cs` — `Export` | Phase 3's P3-ACCT-10 / Phase 6's P6-OUT-2 for the affordance; what the `.txf` contains is P7-TXF-3/4. See Open Question 4 |
| `View Selectors/RetirementControl.xaml.cs` | Phase 3's P3-PANEL-3 / Phase 5's P5-PLAN-1 for the entry panel; the tax rules behind the two tax fields are P7-EST-3, P7-EST-5 |
| `Reports/RetirementPlan.cs` | **Phase 5** (P5-PLAN-*) and GitHub issue #55; the only consumer of P7-EST-*. See Open Questions 20, 24 |
| `Money.cs` — `Category.TaxRefNum`, `Transaction.TaxDate`, `Account.TaxStatus`, `Security.Taxable` | **Phase 1** (P1-TAX-1…5) — the model side of everything in 7.1 |
| `Dialogs/TaxReportDialog.xaml.cs` | Phase 4's P4-REPORT-4…6 — the year/consolidation prompt in front of P7-TXF-4 |
| `UnitTests/TaxTests.cs`, `UnitTests/TaxCategoryTests.cs`, `UnitTests/CostBasisTests.cs` | **Not user-facing** — characterization tests over this phase's code; `TaxTests` notably *documents* Open Question 15 rather than fixing it |

---

## Open questions from Phase 7

Things a human should double-check, because the call was a judgement rather than obvious
from the code — and, where noted, because they look like real defects. This is
tax-calculation and tax-filing code, so the first nine are ordered by how much money a
user could get wrong by trusting them.

1. **Federal capital-gains rates are applied to the gains alone, ignoring all other
   income.** `FederalTaxes.GetCapitalGainsTax` builds its running total as
   `baseGains + gains` and walks the 0% / 15% / 20% bands against that. Those bands are
   thresholds of *taxable income*, not of gains — long-term gains stack on top of ordinary
   income. A single filer with $200,000 of salary and $10,000 of long-term gain is
   estimated at **0%** on the gain (because $10,000 is under the $49,450 top of the 0%
   band) when the real rate is 15%. The state version of the same method takes a
   `baseIncome` parameter and does the right thing with it for taxed-as-income states
   (`GetIncomeTax(status, baseIncome + baseGains, gains)`); the federal one takes no such
   parameter at all, so the caller cannot fix it from outside. In a retirement projection
   that runs 30+ years and draws heavily on taxable accounts, this understates lifetime
   tax by a large margin and the resulting "your money lasts until age N" answer is
   optimistic. The same method's `_ =>` fallback arm also returns `this.IncomeBrackets
   .Single` — the *income* brackets — rather than a capital-gains table; unreachable while
   `TaxFilingStatus` has four members, but wrong if a fifth is ever added.

2. **The long-term boundary is a day out across a leap year.**
   `CapitalGainsTaxCalculator.CalculateCapitalGains` classifies a sale as long-term when
   `(DateSold - DateAcquired).Days > 365`. The rule is "held **more than one year**", and a
   year is 366 days when the holding period spans 29 February. Buy on 1 January 2024 and
   sell on 1 January 2025 and the difference is 366 days, so the product reports a
   long-term gain on a position held exactly one year — which is short-term, and taxed as
   ordinary income. It errs in the taxpayer's favour, which is the direction that gets
   noticed by the tax authority rather than by the user. Comparing
   `DateAcquired.AddYears(1) < DateSold` is the calendar-correct test.

3. **The "I don't know what this cost you" path is unreachable, and the sales it was
   meant to catch vanish instead.** `CapitalGainsTaxCalculator` sorts a sale into its
   `Unknown` list when `sale.Error != null`. **`SecuritySale.Error` is never assigned
   anywhere in the codebase** — a repository-wide search for assignments to it returns
   nothing. Three things follow. `TaxReport`'s "Capital Gains with Unknown Cost Basis"
   section (Phase 5's P5-TAX-3) can never render; its "Errors Found" section, which
   filters the same field, can never render either; and `TxfExporter`'s entire refnum-673
   branch — the unknown-basis record type, with its own empty-date and empty-cost
   formatting — is dead code. What actually happens to a sale with no matching purchase is
   that `SecurityFifoQueue.Sell` parks it in a private `pending` list, from which it is
   only ever recovered if a *later* purchase turns up; if none does, the sale appears in
   neither the tax report nor the exported file. A user who transferred a holding in from
   a broker they never tracked, or inherited shares, sells them, and gets a tax report
   with **no row at all** for that sale — not a flagged row, not a zero-basis row. Two
   smaller things in the same method: the shortfall test is `Math.Floor(units) > 0`, so a
   shortfall of less than one unit is dropped without even becoming pending; and a sale
   recovered later by `ProcessPendingSales` is matched against a purchase made *after* it,
   producing a negative holding period that lands in short-term by arithmetic accident
   rather than by rule.

4. **The per-account `.txf` export writes no file header, so tax software will likely
   reject it.** `TxfExporter.Export` (the whole-year route from the tax report) calls the
   private `WriteHeader`, which emits the format version, the producing application and
   the date — the three records the format requires first. `AccountsControl.Export`, the
   "export this account as `.txf`" route, opens its own `StreamWriter` and calls the
   public `ExportCapitalGains(Account, TextWriter, …)` directly, which starts straight in
   on transaction records. The file it produces has no version record at all. Two entry
   points to one exporter, one of which skips the required preamble; the fix is to make
   the public overload write the header, or to have the account route call through a
   method that does.

5. **The exported file's dates are written in the machine's local format.**
   `WriteRecordFormat4` writes acquisition and sale dates with `ToShortDateString()`. On a
   UK or German machine that produces `01/02/2025` or `01.02.2025` for what the format
   expects as a US-style date, so either the import fails or — worse — the day and month
   silently swap and a January sale is filed as February. Exactly the same class of defect
   as Phase 6's Open Question 9 (`ToString("C2")` in the CSV export), in a file that goes
   to a tax authority rather than to a spreadsheet.

6. **The tax report and the exported file disagree about the same figure, in both sign
   and amount.** Two independent discrepancies, both in code that reads the same
   `GenerateGroups` output:
   - *Sign.* `TaxReport.GenerateCategories` flips the sign of any line the catalogue marks
     as an expense (`if (tc.DefaultSign < 0) value *= -1;`), so a deduction reads as a
     positive amount to enter on the form. `TxfExporter.WriteRecordFormat1`/`3` never flip
     anything; they only prepend a `"+"` when `DefaultSign == -1 && total > 0`, which for
     expenses stored as negative amounts is never. The screen says `$4,200` of mortgage
     interest; the file says `$-4200`.
   - *Amount.* The report splits each line into a taxable figure and a tax-exempt figure
     (P7-SUM-6) and puts only the taxable part in the line total. The exporter sums
     `t.Amount` over every transaction in the group, tax-exempt ones included. A user with
     municipal-bond interest exports a taxable interest figure that includes their
     tax-exempt interest. Whichever of the two is right, they cannot both be, and nothing
     tells the user they differ.

   `WriteRecordFormat1` also writes `total.ToString()` raw while every other record goes
   through `Round`, so the summary record can carry more decimal places than the detail
   records it totals.

7. **Five of the seven record shapes are unimplemented, so ~56 tax lines export as
   nothing.** `TxfExporter.ExportCategories` switches on `TaxCategory.RecordFormat` and
   handles only `1` and `3`. Of the 376 selectable lines in the catalogue, 279 are format
   1 and 37 are format 3 (both handled), 4 are format 4 (handled separately by the
   capital-gains path), and **56 are formats 0, 2, 5 or 6 and fall through the switch with
   no `default:` arm** — no record, no warning, no count. Among them are the quarterly
   federal and state estimated-tax payments (refnums 521 and 522, format 6), which are
   exactly the kind of thing a user would carefully classify and then expect to see in
   their tax software. The line still appears correctly on the on-screen tax report, which
   makes the omission harder to notice, not easier.

8. **A tax-relevant transaction with no payee is silently dropped from its own tax
   line.** `GenerateGroups` builds a group key from the payee, the account or the category
   depending on the line's sort order, and then adds the transaction only
   `if (!string.IsNullOrEmpty(group))`. For the majority of lines (sort order "none" or
   "by payee") the key is the payee name, so a transaction with an empty payee — routine
   for a manually entered adjustment or an opening entry — contributes to no group, no
   total and no exported record, with no count of what was skipped. The user sees a tax
   line whose total is quietly short.

9. **`TaxCategory` instances are process-wide statics that reports write their results
   onto.** `TaxCategoryCollection` caches the parsed catalogue in two `static` dictionaries
   and every instance is populated from them, so every `TaxCategoryCollection` anywhere in
   the process — the one in `CategoryDialog`, the one in `W2Report`, the one `TxfExporter`
   creates, and the *second* one `GenerateGroups` gratuitously constructs inside itself —
   shares the same `TaxCategory` objects. `GenerateGroups` then assigns each run's results
   to `tc.Groups` on those shared objects. Today nothing reads a stale `Groups` (both
   consumers iterate only the freshly returned list), so this is latent rather than live,
   but it means every transaction matched by the last tax report stays reachable from a
   static for the life of the process, and any future caller that iterates the collection
   rather than the returned list will silently read last year's figures. `GenerateGroups`
   building its own `TaxCategoryCollection` when it is already an instance method on one is
   a straightforward redundancy.

10. **The tax-line catalogue is from tax year 2004 and the specification from 2006.**
    `TxfSpec.txt` is version 041, dated 6/16/06, and every line in its category table
    carries an IRS reference of the form `2004:1040:21`. Form 1040 was restructured in
    2018 into a short form plus Schedules 1–3; many of the line numbers the user is
    picking from no longer exist, some refnums have been retired, and newer concepts
    (HSA lines, qualified business income, the current 1099-NEC) are absent entirely. The
    product will happily export a refnum that current tax software rejects or maps
    somewhere unexpected. For a redesign this is the single most consequential piece of
    stale data in the phase: the whole of 7.1 is built on it, and there is nothing in the
    product that tells a user which tax year the catalogue is for.

11. **Three states' capital-gains exclusions are in the data but no code reads them, and
    two more states spell the field wrong.** `StateTaxes.CapitalGains` declares
    `deductionPercentage` and `deductionForStateOwnedAssetsOnly`; neither is read anywhere
    in the codebase. The data sets `deductionPercentage` for Idaho (60), South Carolina
    (44) and Wisconsin (30) — substantial exclusions that the projection therefore ignores,
    overstating those users' state capital-gains tax by up to 60%. Arizona and Arkansas
    carry the same concept under the key `"deduction"` (25 and 50), which matches no
    property on the class at all, so it isn't even deserialised. `deductionForStateOwnedAssetsOnly`
    is set for Arkansas, Colorado and Idaho and likewise ignored. Either the fields should
    be implemented or they should come out of the data, but a half-populated table that
    silently over-taxes three states is the worst of both.

12. **Bracket inflation moves some tables and not others.** `FederalTaxes.InflateBrackets`
    widens the Single, Married and Head income brackets. It does **not** touch the
    `Separate` income brackets, **any** of the four capital-gains bracket tables, or the
    standard deduction. `StateData.InflateBrackets` widens Single and Married and leaves a
    `// todo` asking whether the capital-gains surcharge brackets should move too. Over a
    30-year projection with 2% assumed bracket inflation, income brackets end up ~80%
    wider while the standard deduction and the 0%/15%/20% capital-gains thresholds are
    still in today's dollars — so the simulation drifts steadily toward over-taxing gains
    and under-crediting the deduction, in a compounding way that is invisible in any single
    year. The inconsistency matters more than the direction.

13. **Nothing records which tax year the federal figures are for.**
    `StateTaxes.json` at least carries `"name": "US State Income Tax Rates Dataset 2026"`,
    a `datePublished` and a `version`, and names its source. `FederalTaxes.json` is a bare
    object with no year, no source and no published date — the figures in it (a $16,100
    single standard deduction, a $640,600 top-bracket threshold) are 2026-shaped, but
    nothing in the file or the UI says so. There is no staleness check, no "these rules are
    from year N" line on the projection, and no mechanism for updating either file short of
    shipping a new build. A user running the plan in 2030 gets 2026 rules with no
    indication.

14. **Two of the four filing statuses are approximations the code itself is unsure
    about.** `FederalTaxes.GetIncomeTax` maps married-filing-separately to the **Single**
    income brackets and the **Single** standard deduction, with the comments *"are there
    income brackets for this?"* and *"todo"*. That is close for the lower bands and wrong
    at the top (the separate top-bracket threshold is half the joint one, not the single
    one). The state data has no head-of-household or separate tables at all, so
    `StateData.GetIncomeTax` maps head-of-household to Single as well, noted as *"state data
    doesn't have this category"*. A head-of-household user gets a correct federal estimate
    and a state estimate computed on the wrong table, and nothing in the UI says the state
    figure is an approximation.

15. **A state capital-gains "tax" can come out negative.** `StateData.GetCapitalGainsTax`
    has no `if (gains < 0) return 0;` guard, unlike `FederalTaxes.GetCapitalGainsTax` and
    both `GetIncomeTax` methods, which all have one. Once `baseGains` exceeds the state's
    exemption, passing a negative incremental gain (a realised loss) produces a negative
    tax — a refund that no state offers on an unrealised-loss basis. This one is already
    *documented* rather than fixed: `UnitTests/TaxTests.cs` asserts
    `Is.EqualTo(-3500.0M)` for Washington with a $50,000 loss, with a comment explaining
    that the clamp is missing. Worth a decision about whether the characterization test is
    ratifying a bug.

16. **The bracket walk loses a cent per band, and the `max` column is decorative.** Both
    `GetIncomeTax` implementations set `income = bracket.Min - 0.01M` after consuming a
    band, so each lower band is computed one cent narrow — arithmetically trivial (a few
    thousandths of a cent of tax per band) but it means the function is not exactly
    reproducible against a published bracket table, which makes it hard to test against a
    known-good figure. More substantially, every `Bracket` carries a `Max` and **no
    calculation ever reads it**: the bands are defined purely by their `Min` and the
    implicit assumption that the array is sorted ascending and gapless. A data file with
    an out-of-order or overlapping entry would produce silently wrong tax with no
    validation anywhere. The federal file is correctly ordered today; 51 hand-maintained
    state tables are a lot of places for that to stop being true.

17. **`StateTaxes.json` contains a trailing comma.** California's `capitalGains` object
    ends `"taxedAsIncome": true,` before its closing brace. Newtonsoft.Json tolerates it;
    `System.Text.Json` does not, and the rest of the codebase is being modernised. If that
    file is ever loaded by a stricter parser it will throw at startup of
    `RetirementControl`'s constructor, which loads the state list eagerly and has no
    try/catch — taking the whole panel with it.

18. **Both tax data files are loaded from disk beside the executable, not embedded.**
    `FederalTaxes.Load` and `StateTaxes.Load` do
    `Path.Combine(Path.GetDirectoryName(assembly.Location), "Taxes", "<file>.json")` and
    `File.ReadAllText` with no existence check and no error handling, relying on
    `CopyToOutputDirectory=Always` in the project file. `TxfSpec.txt`, in the same folder,
    is an `EmbeddedResource` instead. Three data files, two different delivery mechanisms,
    and the two that can go missing are the two with no fallback. `assembly.Location` is
    also empty under single-file publishing, which would make `Path.Combine` throw. Worth
    settling on one mechanism during the redesign.

19. **Two crossed namespaces and a filename that names another file's class.** Recorded
    under "Naming notes" above and repeated here because it is the fourth phase in a row
    to hit this: `Taxes/TxfExporter.cs` is in `Walkabout.Importers`, `Reports/W2Report.cs`
    is in `Walkabout.Taxes`, and `Taxes/CapitalGains.cs` contains no `CapitalGains` type
    while `Taxes/StateTaxes.cs` does. Searching either namespace or either filename for
    "the tax code" misses part of it.

20. **An unrecognised state name silently becomes Alabama.** `RetirementPlan.FindState`
    and `FindStateName` both end `return stateTaxes.Data[0];` — the first entry, Alabama —
    when the saved state name matches neither an abbreviation nor a full name. A settings
    file written by an older build, or a state renamed in the data, produces a full
    projection computed on the wrong state's rules with the panel showing a state the user
    never chose. This lives in `Reports/RetirementPlan.cs` (Phase 5's file) but is purely a
    consequence of how `Taxes/StateTaxes` is looked up, so it is recorded here.

21. **User-visible spelling.** The tax report's third column header reads **"Tax
    Excempt"**, and the export button's tooltip reads *"Export .txf file format for
    **TuboTax**"*. Both are on the screen a user reaches at tax time. Trivial to fix,
    embarrassing to ship.

22. **The sales-tax figure ignores the tax-date override every other figure honours.**
    `TaxReport.GetSalesTax` filters on `t.Date`, while `GenerateGroups`, `W2Report` and the
    fiscal-year range all filter on `t.TaxDate`. A user who has moved transactions between
    tax years (Phase 1's P1-TAX-3, Phase 4's P4-TAXDATE-1) gets a sales-tax total for one
    year sitting at the top of a report whose every other figure is for another. One
    property, one line.

23. **The reconstructed W-2 doesn't exclude sheltered accounts and rounds to whole
    dollars.** `W2Report.GenerateForm` filters out transfers, deleted and void
    transactions but — unlike `GenerateGroups`, which is doing the same job for the other
    tax report — does **not** exclude tax-deferred or tax-free accounts, so a paycheque
    deposited straight into a retirement account contributes to the estimated form. It also
    formats every figure with `ToString("N0")`, i.e. rounded to the nearest dollar with no
    currency symbol, in a report whose entire purpose is to be compared line-by-line
    against a document from an employer. Whether either is deliberate is worth asking; both
    are inconsistent with the other tax report a menu item away.

24. **Where the `CostBasis.cs` line was drawn.** `CostBasisCalculator` and the FIFO
    machinery under it are not in `Taxes/` — they are at `MyMoney.Business/CostBasis.cs` —
    and no earlier phase's coverage checklist claims them: Phase 1 covered only `Money.cs`
    and `Money_Loans.cs`, and Phase 5 named `CostBasisCalculator` in traceability notes for
    the portfolio report without owning the file. Because "how the product decides what a
    sale cost and how long it was held" *is* the tax semantics this phase was asked for,
    and because Open Question 3 is only visible by reading it, the file is claimed here and
    listed in full in the checklist — with its two dividend-estimation methods and
    `SecurityGroup` marked as Phase 5's, since they feed the portfolio and retirement
    reports and have nothing to do with tax. If Phase 8 would rather own the whole file as
    a cross-cutting investment concern, only the ownership label changes; the scenarios
    stand.

25. **Where the Phase 8 line was drawn.** Three things in this phase's territory are left
    to Phase 8. The fiscal-year start is a database-level setting (`DatabaseSettings
    .FiscalYearStart`) that both tax reports read but neither owns, and settings as a whole
    are Phase 8's — P7-SUM-4 and P7-TXF-5 describe only the tax consequence of it. Printing
    a tax report is the shared report-printing path. And the two `.json` data files'
    delivery, updating and staleness (Open Questions 13 and 18) is really a
    "how does this product ship reference data" question, which touches the stock-quote and
    institution-directory caches too; it is recorded here because the money at stake is
    tax money, but the answer probably belongs with those.

---

# Phase 8: Cross-Cutting

**Source examined (in full):**

| File(s) | Lines |
|---|---|
| `Source/WPF/MyMoney/Attachments/AttachmentManager.cs` (`AttachmentManager`, `AttachmentWatcher`) | 692 |
| `Source/WPF/MyMoney/Attachments/StatementManager.cs` (`StatementManager`, `StatementIndex`, `StatementItem`) | 676 |
| `Source/WPF/MyMoney.Business/StockQuotes/` — `StockQuoteManager.cs`, `IStockQuoteService.cs`, `StockQuoteCache.cs`, `ExchangeRateService.cs`, `IOnlineService.cs`, `IStockDownloadLog.cs`, `StockQuoteThrottle.cs`, `ThrottledStockQuoteService.cs`, `Yahoo.cs`, `Polygon.cs`, `TwelveData.cs`, `MarketStack.cs` | ~4,600 |
| `Source/WPF/MyMoney/Utilities/Settings.cs` (`Settings`, `GraphState`, `FileAssociation`) | 1,206 |
| `Source/WPF/MyMoney.Business/DatabaseSettings.cs` | 144 |
| `Source/WPF/MyMoney/Utilities/MessageBox/MessageBoxEx.xaml` + `.xaml.cs` | full |
| `Source/WPF/MyMoney/Setup/` — `ChangeInfoReport.cs`, `ChangeListRequest.cs`, `DirectorySetup.cs`, `changes.xml`, `LatestVersion.xslt` | 250 + 120 + 85 + data |
| `Source/WPF/MyMoney.Business/Utilities/Logger.cs` (`ILogger`, `Log`, `CrashReport`) | 239 |
| `Source/WPF/MyMoney.Business/Utilities/QuickFilterParser.cs` (`QuickFilterParser<T>`, `Filter<T>` and its five subclasses, `FilterLiteral`) | 558 |
| `Source/WPF/MyMoney.Business/AutoCategorization.cs` + `Utilities/KNearestNeighbor.cs` | 165 + 122 |
| `Source/WPF/MyMoney.Business/Encryption.cs` | 159 |
| `Source/WPF/MyMoney.Business/UsHolidays.cs` | 260 |
| `Source/WPF/MyMoney.Business/SampleDataLoader.cs`, `SampleDataGenerator.cs`; `MyMoney/Database/SampleDatabase.cs` | 60 + 842 + 98 |
| `Source/WPF/MyMoney/Utilities/` — `AppTheme.cs`, `TempFileCollection.cs`, `Clipboard.cs`, `ClipboardClients.cs`, `ClipboardMonitor.cs`, `DragAndDrop.cs`, `Sounds.cs`, `FileHash.cs`, `FileHelpers.cs`, `FileIcons.cs`, `InternetExplorer.cs`, `ApplicationDeployment.cs`, `EdgeDetector.cs`, `Colors.cs`, `HlsColor.cs`, `AxisTicker.cs`, `XamlHelpers.cs`, `adorners.cs`, `MouseUtilities.cs`, `PerfTimer.cs`, `CompiledPropertySetter.cs`, `WpfAnnotations.cs`, `UiThreadEventHandler.cs`, `ExponentialDoubleAnimation.cs`, `PointCollectionAnimation.cs` | ~2,900 |
| `Source/WPF/MyMoney/Themes/` — `Light.xaml`, `Dark.xaml`, `Compact.xaml`, `generic.xaml` | markup |
| `Source/WPF/MyMoney/Controls/` — everything no earlier phase claimed: `Calculator/` (`CalculatorPopup`, `CalculatorControl`, `Parser`), `WpfConverters.cs`, `MoneyDataGrid.cs`, `MoneyDatePicker.cs`, `FilteringComboBox.cs`, `ColorPickerPanel`, `PasswordControl`, `HandyTextBox.cs`, `HandyFlowDocumentScrollViewer.cs`, `SingleLineTextBlock.cs`, `StackedBar.cs`, `Resizer.cs`, `RoundedButton`, `CustomizableButton.cs`, `ProgressDots`, `TabCloseBox` | ~3,900 |
| `Source/WPF/MyMoney/Interop/MoneyDataObject.cs` | 107 |
| `Source/WPF/MyMoney/Database/` — `DataEngineStartup.cs`, `SecurityService.cs`, `WpfDataLayerUiCallback.cs`, `SampleData.xml`, `SampleStockQuotes.zip`, `StripSampleStockQuotes.xslt` | 73 + 18 + 13 + data |
| `Source/WPF/MyMoney.Business/` — `DatabaseLifecycle.cs`, `IDatabase.cs`, `Mapping.cs`, `AsyncSqlQuery.cs`, `ISettingsMigrationSource.cs`, `Query.cs` | ~1,200 |
| `Source/WPF/MyMoney.Business/Utilities/` — `Dispatcher.cs`, `DelayedAction.cs`, `EventHandlerCollection.cs`, `FilteredObservableCollection.cs`, `Hashset.cs`, `IStatusService.cs`, `MathHelpers.cs`, `NativeMethods.cs`, `ProcessHelper.cs`, `SimpleGraph.cs`, `StringHelpers.cs`, `DescendingComparer.cs`, `UiThreadHandler.cs`, `UiThreadPropertyChangedHandler.cs`, `XmlHelpers.cs` | ~1,700 |
| `Source/WPF/MyMoney/Design/`, `Icons/`, `Properties/`, `GlobalSuppressions.cs`, `dataengine.config.template.json` | assets/build |

> **Path/naming notes, continuing the run every phase has hit.**
> - `AttachmentManager.cs` declares **two** classes: the manager and `AttachmentWatcher`,
>   the background scanner `CLAUDE.md` warns about. Searching for a file named after the
>   watcher finds nothing.
> - `Settings.cs` is in `Utilities/` but declares `namespace Walkabout.Configuration`, and
>   it also contains `FileAssociation` — the class that registers the product as the handler
>   for `.qif`/`.qfx`/`.ofx`/`.mmdb` — which has nothing to do with settings.
> - `TempFileCollection.cs` contains no type called `TempFileCollection`; the class is
>   `TempFilesManager`. Same trap as Phase 7's `CapitalGains.cs`.
> - `Logger.cs` lives in `MyMoney.Business/Utilities/` (not `MyMoney/Utilities/`, where
>   `CLAUDE.md`'s `AppCrashGuard` note might lead you), and declares `namespace
>   Walkabout.Utilities` alongside a `CrashReport` type.
> - `Interop/MoneyDataObject.cs` declares `namespace Walkabout.Database` while the folder
>   called `Database/` declares `namespace Walkabout.Data`, `Walkabout.Assistance` and
>   `Walkabout.Setup`. Neither folder name matches its namespace.
> - `EdgeDetector.cs` in `Utilities/` is a full Canny edge-detection implementation; its
>   only live use is the attachment window's auto-crop button.
> - `Encryption.cs` and `UsHolidays.cs` sit at the root of `MyMoney.Business` with no folder
>   suggesting they are the file-password and market-calendar implementations.

**What this phase covers and deliberately does not.** Phases 1–7 each owned a feature
area. This phase owns everything those areas *rest on*: where documents and statements
are filed, where prices and exchange rates come from, where preferences live, how the
product asks a question or reports a failure, how it is themed, how it keeps itself
up to date, and the shared conveniences that appear in many places at once. The rule
used throughout is the same one earlier phases used for shared code: if a capability is
only visible *through* a surface an earlier phase catalogued, that phase keeps the
scenario and this phase records the mechanism; if the capability is only describable by
reading the shared code, it gets a scenario here.

---

## Part 1: the completeness sweep

Before writing anything new, every top-level folder under `Source/WPF/MyMoney/` and
`Source/WPF/MyMoney.Business/` was listed and cross-referenced against what Phases 1–7
say they examined. The result:

| Folder | Claimed by | Phase 8 action |
|---|---|---|
| `MyMoney/Views/` | Phase 3 (and Phase 5 for `GraphGenerators.cs`, `FlowDocumentView`) | nothing left |
| `MyMoney/View Selectors/` | Phase 3 | nothing left |
| `MyMoney/Dialogs/` | Phase 4 | nothing left |
| `MyMoney/Reports/` | Phase 5 | **except `Reports/`-adjacent `Setup/ChangeInfoReport.cs`** — claimed here |
| `MyMoney/Charts/` | Phase 5 | nothing left |
| `MyMoney/Ofx/` | Phase 6 | nothing left |
| `MyMoney/Commands/` | Phase 2 (`AppCommands`) | nothing left |
| `MyMoney/Controls/` | **partially** — Phase 2 took `Accordion`, `AppSettings`, `QuickFilterControl`, `OutputPane`, `CloseBox`, `DownloadControl`; Phase 3 took `QueryViewControl`; Phase 5 took `TrendGraph`. Phase 2's checklist explicitly pushed the rest to "Phase 3/4", and **neither took them** | **claimed here** — the calculator, money grid, date picker, filtering combo, colour picker, password box, stacked bar, resizer, progress dots, tab close box, the button/text primitives and `WpfConverters.cs` |
| `MyMoney/Attachments/` | **nobody** | **claimed here** |
| `MyMoney/Utilities/` | **partially** — Phase 2 took `RecentFilesMenu`, `HelpService`, `AppTheme` (named only), `AnimatedMessage`, `UndoManager`; Phase 4 referenced `MessageBoxEx` and pushed it here | **claimed here** — the other 26 files |
| `MyMoney/Setup/` | **nobody** (Phase 2 and Phase 5 both bounced `ChangeInfoReport.cs` here) | **claimed here** |
| `MyMoney/Themes/` | **nobody** | **claimed here** |
| `MyMoney/Database/` | **nobody** (`SampleDatabase` was named in Phase 4's P4-SAMPLE traceability but the folder was never examined) | **claimed here** |
| `MyMoney/Interop/` | **nobody** | **claimed here** |
| `MyMoney/Design/` | **nobody** | **not user-facing** — 16 mock-up screenshots and a `.pptx`/`.pdf` of an abandoned Metro-style redesign, referenced by no code or markup. Historical design material, not product behaviour. Directly interesting to the redesign this catalog exists for, but not a scenario |
| `MyMoney/Icons/` | **nobody** | mixed: `Flags/` (271 country flags) is user-facing and claimed here; `App.ico`, `setup.ico`, `Excel.png`, `TurboTax.png`, `Ding.wav` are assets supporting scenarios recorded here and in Phases 5–7; `Icon.pptx` is **build/design material** |
| `MyMoney/Properties/` | **nobody** | **not user-facing / build infrastructure** — `AssemblyInfo.cs` and ClickOnce `PublishProfiles/` |
| `MyMoney/` root files | Phase 2 (`App.xaml.cs`, `MainWindow.*`), Phase 6 (`WpfBusinessLayerUiCallback.cs`) | `GlobalSuppressions.cs` and `dataengine.config.template.json` are **build/configuration infrastructure**; `App.xaml` is markup for the theming of 8.12 |
| `MyMoney.Business/Importers/`, `Ofx/` | Phase 6 | nothing left |
| `MyMoney.Business/Taxes/` | Phase 7 | nothing left |
| `MyMoney.Business/StockQuotes/` | **nobody** (Phase 1 and Phase 5 both said "acquisition is Phase 8") | **claimed here** |
| `MyMoney.Business/Utilities/` | **nobody** (Phase 6 named `ProcessHelper.cs` in passing) | **claimed here** |
| `MyMoney.Business/Properties/` | **nobody** | **not user-facing / generated** — `Resources.Designer.cs` is generated from `Resources.resx`; the strings it holds are quoted by scenarios here and in Phase 6 |
| `MyMoney.Business/` root files | Phase 1 (`Money.cs`, `Money_Loans.cs`), Phase 5 (`Payments.cs`), Phase 6 (`IBusinessLayerUiCallback.cs`), Phase 7 (`CostBasis.cs`) | **claimed here**: `AutoCategorization.cs`, `DatabaseSettings.cs`, `DatabaseLifecycle.cs`, `Encryption.cs`, `IDatabase.cs`, `ISettingsMigrationSource.cs`, `Mapping.cs`, `AsyncSqlQuery.cs`, `Query.cs`, `SampleDataGenerator.cs`, `SampleDataLoader.cs`, `UsHolidays.cs` |

**Two things the sweep found that nobody had guessed at.** First, **`Controls/` fell
through a hand-off**: Phase 2 explicitly listed sixteen controls as "Phase 3/4's", and
Phase 3's checklist claims exactly one of them. Second, **the quick-filter search box has
a real expression language** — `and`/`or`/`not`, `&`/`|`/`!`, parentheses, quoted
phrases, sign-insensitive amounts and date matching — implemented in
`MyMoney.Business/Utilities/QuickFilterParser.cs`, which no phase examined; Phase 3's
P3-FIND-1 describes it only as "filters it down as the user types". That is now
P8-FIND-1…P8-FIND-5.

**One deliberate boundary.** `Source/WPF/MyMoney.Data/` is a *third* project and was
outside the sweep's stated scope (`MyMoney/` and `MyMoney.Business/`). Phase 4 owns the
windows that choose a storage location (P4-DB-*), Phase 6 owns `CsvStore`, and Phase 6
marked `XmlStore`/`BinaryXmlStore` as "Phase 4 / Phase 8". This phase therefore claims
the *storage-choice concepts a user perceives* (8.6) and reads `XmlStore`'s encryption
path, but does **not** claim the ~10,000 lines of engine implementation in
`SqlDatabase.cs`, `SqlServerStoredProcDatabase.cs`, `SqliteDatabase.cs` and
`SqlCeDatabase.cs`. See Open Question 25 — that is the one place where "all source
processed" is scoped rather than absolute.

---

## 8.1 Paperwork filed against a transaction

### P8-ATT-1 — Keep receipts and documents with the transaction they belong to
The user files scanned receipts, photographs, statements and documents against individual
transactions, and the product keeps them so that opening that transaction again always
brings back the same paperwork.
*(`AttachmentManager`, the per-transaction file naming in `GetUniqueFileName`)*

### P8-ATT-2 — Have paperwork stored beside the financial file, not inside it
Documents live in an ordinary folder next to the user's financial file, named after it, so
they can be backed up, browsed and copied with normal tools rather than being locked
inside the product.
*(`AttachmentManager.SetupAttachmentDirectory` → `<database>.Attachments`)*

### P8-ATT-3 — Keep paperwork organised by account
Within that folder, each account gets its own subfolder, so a user looking at the files
directly can tell which account a document belongs to.
*(`GetUniqueFileName`/`FindAttachments` combining the attachment directory with the
account name)*

### P8-ATT-4 — Put the paperwork somewhere else if they want to
The user can choose a different location for their documents. If documents already exist,
they are offered the chance to move them rather than being stranded; if the new location
doesn't exist, they are offered the chance to create it.
*(`AttachmentManager.MoveAttachments(string)`, the two confirmations it raises. **See Open
Question 3** — those confirmations don't always reach the user.)*

### P8-ATT-5 — Have paperwork follow the transaction when it moves
When the user turns a transaction into a transfer, re-points a transfer at a different
account, or moves a transaction between accounts, its documents move with it rather than
being orphaned in the old account's folder.
*(`MoveAttachments(Transaction, Transaction)`, `MoveAttachments(Transaction, Account)`,
`OnBeforeTransferChanged`, `OnBeforeSplitTransferChanged`)*

### P8-ATT-6 — Have paperwork follow an account rename
Renaming an account renames its document folder too, so nothing is lost and nothing has to
be re-filed.
*(`AttachmentWatcher.OnRenameAccount`, the remembered original-name map)*

### P8-ATT-7 — See the paperclip appear without having to do anything
The product notices documents that were dropped into the folder from outside, or that
arrived with an import, and marks the affected transactions as having paperwork — in the
background, without blocking whatever the user is doing.
*(`AttachmentWatcher.ScanDirectory`/`QueueAccount`/`QueueTransaction` on a background
task; `CLAUDE.md` records the lag this causes)*

### P8-ATT-8 — Not end up with two copies of the same receipt
When documents arrive with imported data, one that is byte-for-byte identical to a
document already filed against that transaction is recognised and skipped.
*(`ImportAttachments` with `HashedFile.HashEquals`/`DeepEquals`)*

### P8-ATT-9 — Have files that can't be deleted right now cleaned up later
A document that is locked by another program when the product tries to remove it is
remembered and deleted on the next opportunity, including the next time the product
starts, rather than being left behind forever.
*(`TempFilesManager` — the delayed retry, the saved list and the startup sweep)*

---

## 8.2 Bank statements filed against an account

### P8-STMT-1 — Keep the statement itself, not just the closing balance
When the user balances an account, they can file the actual statement document alongside
the date and closing balance, so the evidence for a reconciliation is kept with it.
*(`StatementManager.AddStatement`/`UpdateStatement`, `StatementItem`)*

### P8-STMT-2 — Look back at what any past statement said
For any account the user can retrieve the statement of a given date — its closing balance
and the document it came from.
*(`GetStatements`, `GetStatement`, `GetStatementBalance`, `GetStatementFullPath`)*

### P8-STMT-3 — Have statements stored beside the financial file, per account
Statements live in their own folder next to the financial file, one subfolder per account,
each with a small index recording the date, balance and document for every statement.
*(`SetupStatementsDirectory` → `<database>.Statements`, `index.xml`, `StatementIndex`)*

### P8-STMT-4 — Not store the same statement twice when one covers several accounts
When a bank issues one document covering several accounts, filing it against each of them
keeps a single copy and points every account at it, rather than duplicating it.
*(`FindBundledStatement`, `IsBundledStatement`, content hashing)*

### P8-STMT-5 — Have statements follow an account rename
Renaming an account renames its statement folder and repairs every reference from other
accounts that pointed into it.
*(`OnRenameAccountFolder`, `UpdateBundledPointers`)*

### P8-STMT-6 — Be told when a filed statement has been changed underneath them
The product records a fingerprint of each filed statement and notices when the file on
disk has been modified since it was filed.
*(`CheckFileHashes`, `StatementItem.Hash`/`FileModified`)*

### P8-STMT-7 — Bring statements across when merging another copy of the books
When the user merges a second financial file, the statements recorded in it are brought
across to the matching accounts.
*(`ImportStatements`. **See Open Question 2** — what arrives today is the dates and
balances without the documents.)*

---

## 8.3 Getting prices, splits and exchange rates from outside

### P8-QUOTE-1 — Have holdings priced without typing prices in
The user's holdings are valued using market prices the product fetches for them, so
balances, net worth and the portfolio are current without manual entry.
*(`StockQuoteManager.UpdateQuotes` → `Security.Price`/`PriceDate`)*

### P8-QUOTE-2 — Choose where market data comes from, and prove the key works
The user picks among several market-data providers, supplies the credential that provider
issued them, and can test it before relying on it.
*(`StockQuoteManager.GetServiceForSettings` over `YahooFinance`, `PolygonStocks`,
`TwelveData`, `MarketStack`; `TestApiKeyAsync`. The window that collects this is Phase 4's
P4-QUOTES-*)*

### P8-QUOTE-3 — Not be asked to configure anything they haven't asked for
If no provider has been configured, nothing is fetched and the user is told plainly that a
market-data service needs setting up, rather than seeing silent failures.
*(`GetQuoteService` returning nothing → the "configure a stock quote service" message)*

### P8-QUOTE-4 — Have new holdings priced automatically as they are created
When a holding with a ticker is added or its ticker is corrected, the product fetches a
price for it without being asked again.
*(`StockQuoteManager.OnMoneyChanged` tracking inserted/changed securities)*

### P8-QUOTE-5 — Not re-fetch what was already fetched today
The product remembers what it has already asked for and does not ask again for the same
trading day, so a user who opens the product repeatedly isn't burning through their
provider's quota.
*(`DownloadLog`, `DownloadInfo.Downloaded`, the most-recent-trading-day comparison)*

### P8-QUOTE-6 — Stop asking for a ticker that doesn't exist
A symbol the provider says it has never heard of is remembered as unknown and not
requested again; the user is told which symbols those were, in one message rather than one
per symbol.
*(`OnSymbolNotFound`, `DownloadInfo.NotFound`, the combined "unknown stock quotes"
message)*

### P8-QUOTE-7 — Get a second chance after switching providers
Changing to a different market-data provider clears the "this symbol doesn't exist"
verdicts, because the new provider may well know the symbol the old one didn't.
*(`ActiveStockServiceChanged` resetting `history.NotFound`)*

### P8-QUOTE-8 — Build up a price history, not just today's price
Beyond the latest price, the product accumulates a day-by-day price history for each
holding and fills in the gaps it notices, which is what makes historical valuations and
price charts possible.
*(`HistoryDownloader.BeginFetchHistory`, `StockQuoteHistory`,
`GetMissingDataRanges`, `MergeQuote`)*

### P8-QUOTE-9 — Keep that history usable offline
Price history is kept on disk beside the financial file and read back from there, so
charts and valuations still work with no connection and without re-downloading.
*(`StockQuoteHistory.Load`/`Save` per symbol, `DownloadLog.Load`/`Save`)*

### P8-QUOTE-10 — Ask what a holding was worth on a particular past date
Anything that needs a historical valuation can ask what one share was worth on a given
day; if the market was shut that day, the most recent prior trading day is used, and if
there is no downloaded price at all, the user's own recorded trade prices stand in.
*(`StockQuoteCache.GetSecurityMarketPrice`, `StockQuoteIndex.GetQuote` walking back up to
a month, `LoadIndexFromHistory` seeding from the user's own trades)*

### P8-QUOTE-11 — Get the right historical price despite stock splits
Downloaded histories are expressed in today's share terms; the product converts them back
to what a share actually cost on the day, using the splits it knows about, so a historical
valuation matches what the user actually paid.
*(the split-reversal loop in `StockQuoteIndex.GetQuote`; the splits themselves are Phase
1's P1-INV-10)*

### P8-QUOTE-12 — Have stock splits discovered rather than hand-entered
Where the provider supports it, the product looks up the splits for a holding and records
them, one holding at a time so that expanding a whole list doesn't flood the service.
*(`StockSplitDownloader`, `IStockQuoteService.UpdateStockSplits`)*

### P8-QUOTE-13 — Not be punished for a provider's rate limit
Requests are paced against the provider's stated per-minute, per-day and per-month
allowances. A per-minute limit makes the product wait and tell the user it is waiting; a
daily or monthly allowance that has run out stops the fetch with a plain explanation
rather than a stream of failures. The counts survive restarting the product, so closing
and reopening it doesn't hand the user a fresh allowance they don't have.
*(`StockQuoteThrottle.GetSleep`, `StockQuoteThrottledException`, the saved throttle file,
`ThrottledStockQuoteService`, the `Suspended` event and its "Zzzz!" progress message)*

### P8-QUOTE-14 — Have foreign-currency amounts converted at a current rate
Exchange rates are refreshed daily from an online rate service and applied to the
currencies the user holds, so foreign accounts and holdings total correctly without
the user maintaining rates by hand.
*(`ExchangeRateService.UpdateRates`/`UpdateCurrencyInfo` → `Currency.Ratio`; the currency
concept itself is Phase 1's P1-CUR-*. **See Open Question 12.**)*

### P8-QUOTE-15 — Have a new currency's rate filled in as soon as it is named
When the user starts using a currency they haven't used before, its rate is filled in from
the rates already fetched rather than being left at nothing.
*(`ExchangeRateService.CreateOrUpdate`)*

### P8-QUOTE-16 — Have "up to date" mean the last day the market was actually open
Freshness is judged against the most recent trading day — weekends, public holidays, Good
Friday and a list of known unscheduled closures all counted — so the product doesn't
report itself stale on a Sunday or chase prices that were never published.
*(`UsHolidays`, `StockQuoteHistory.IsMarketOpen`, `KnownClosures`)*

### P8-QUOTE-17 — Ask a provider only for what it is good at
Each configured provider records whether it should be used for price history and for
split history at all, so a provider the user keeps only for latest prices isn't asked for
things it charges extra for or does badly.
*(`OnlineServiceSettings.HistoryEnabled`/`SplitHistoryEnabled`)*

---

## 8.4 Remembering how the user likes to work

### P8-PREF-1 — Have the product come back the way they left it
Window position and size, panel widths and heights, which screen was showing, what was
selected on it, the chart set-up and the last search are all remembered between sessions
without the user saving anything.
*(`Settings` and its per-view state slots; what is restored is Phase 2's P2-START-1)*

### P8-PREF-2 — Keep preferences in one place, separate from the financial data
Preferences live in their own file, so they survive switching between financial files,
and a financial file carries no trace of one machine's window layout.
*(`Settings.Load`/`Save` over an XML settings file, `Settings.TheSettings`)*

### P8-PREF-3 — Not have a preference file lose settings it doesn't understand
Settings written by a newer or older build, including the saved state of screens that
weren't opened this session, are carried through untouched rather than being dropped on
the next save.
*(`viewStateNodes` round-tripping unrecognised view state, the `map`-based reader)*

### P8-PREF-4 — Have old preferences carried forward when something is renamed or moved
When the product reorganises where a setting lives, the user's existing value is migrated
rather than reset — including settings that moved from being per-user to being per
financial file.
*(`AttachmentDialogSize` absorbing the old `ReceiptDialogSize`, `Connection` being split
into server/database/user, `MigrateFiscalYearStart`/`MigrateRentalManagement`)*

### P8-PREF-5 — Not have their password sitting in a settings file
A password stored by an older build is discarded when the settings file is read, and never
written back.
*(the `"Password"` case in `Settings.ReadXml`, which reads and drops it)*

### P8-PREF-6 — Say how far back to look for duplicates and transfers
The user controls how many days either side the product searches when it tries to match a
transfer, and how far back it looks when deciding whether something is a duplicate.
*(`Settings.TransferSearchDays`, `Settings.DuplicateRange`; the matching itself is Phase
1's P1-IMPORT-*)*

### P8-PREF-7 — Say whether closed accounts and reconciled entries stay in view
Two standing choices — whether closed accounts appear in lists, and whether already
reconciled entries can be accepted — apply everywhere rather than having to be set per
screen.
*(`Settings.DisplayClosedAccounts`, `Settings.AcceptReconciled`)*

### P8-PREF-8 — Turn confirmation sounds on or off
The user chooses whether the product makes a sound when something finishes.
*(`Settings.PlaySounds`, `Sounds.PlaySound` over the bundled chime)*

### P8-PREF-9 — Start with a clean slate when something goes wrong
The user can start the product without loading their saved preferences at all, which is
the way out of a layout or state that has become unusable.
*(`Settings(bool save)` with persistence off — the `/nosettings` switch of Phase 2's
P2-START-4)*

---

## 8.5 Settings that belong to one set of books

### P8-BOOK-1 — Have per-file choices travel with the file
Choices that describe the books themselves rather than the person — the fiscal year, the
currency figures are shown in, whether rental tracking is switched on — are stored beside
the financial file, so opening those books on another machine or alongside another set of
books gives the same answers.
*(`DatabaseSettings` written to `<database>.settings`)*

### P8-BOOK-2 — Say when their financial year starts
The user sets the month their financial year begins, and every figure that is reported
"for a year" — tax summaries, the reconstructed pay record, the cash-flow report, the
history chart's year buckets, the tax-year a transaction falls in — follows that choice
rather than assuming January.
*(`DatabaseSettings.FiscalYearStart`, consumed by `TaxReport`, `W2Report`,
`CashFlowReport`, `HistoryBarChart`, `TransactionExtras.MigrateTaxYears` and the `.txf`
export; Phase 7's P7-SUM-4/P7-TXF-5 describe the tax consequence)*

### P8-BOOK-3 — Choose the currency everything is totalled in
The user picks the currency their combined figures are expressed in, and can choose
whether the currency is spelled out alongside amounts at all.
*(`DatabaseSettings.DisplayCurrency`/`ShowCurrency` → `Currencies.DefaultCurrency`)*

### P8-BOOK-4 — Switch off a whole feature area they don't use
A user with no rental property can turn rental tracking off, and the parts of the product
that serve it stop appearing.
*(`DatabaseSettings.RentalManagement` → `MainWindow.UpdateRentalManagement`)*

### P8-BOOK-5 — Have a change to these take effect at once and be kept
Changing one of these settings is reflected immediately — charts re-bucket, reports
regenerate, totals re-denominate — and the change is written out without the user
saving.
*(`DatabaseSettings.PropertyChanged` → `MainWindow.DatabaseSettings_PropertyChanged` and
its delayed save)*

---

## 8.6 Where the data lives, and who can open it

### P8-STORE-1 — Not have to care what the data is stored in
The user works with "my financial file"; whether that is a local file, a file in an older
format or a database on a server is a detail the product resolves from what they picked.
*(`DatabaseLifecycle`/`IDatabaseFactory` choosing an engine from the path, `DbFlavor`)*

### P8-STORE-2 — Keep several sets of books and move between them
The product remembers every set of books the user has opened, which engine each one uses
and when it was last used, so switching between them is a choice from a list rather than a
re-setup.
*(`DatabaseRegistry`; the windows that present it are Phase 4's P4-DB-3 and Phase 2's
P2-FILE-3)*

### P8-STORE-3 — Put a password on a financial file
The user can protect an exported financial file with a password, without which its
contents cannot be read.
*(`Encryption.EncryptFile`/`DecryptFile` behind `XmlStore`; the prompt is Phase 4's
P4-DB-6. **See Open Question 9.**)*

### P8-STORE-4 — Have their documents and statements found automatically for a file
Opening a set of books locates its documents and statements folders from the file's own
name and location, so those never have to be re-pointed when switching files.
*(`AttachmentManager.SetupAttachmentDirectory`, `StatementManager.SetupStatementsDirectory`)*

### P8-STORE-5 — Have the product's own files kept somewhere predictable
Logs, crash reports, downloaded price histories, the hand-off list used when a file is
opened from outside and the list of files still waiting to be deleted all live in known
per-user locations rather than scattered.
*(`ProcessHelper` for the application data and log folders, `TempFilesManager.TempFileList`,
`Log`'s folder, the stock-quote log path)*

### P8-STORE-6 — Be told, not crashed at, when storage isn't available
A server that can't be reached, a login that has been revoked or a set of books that has
been removed since it was last used leaves the product running and able to open something
else.
*(`DataEngineStartup.TryAutoLoad`'s fall-through, `IDataLayerUiCallback.ShowWarning`.
**See Open Question 24** — this particular path only exists in a debug build.)*

### P8-STORE-7 — Run a query against the stored data without freezing the product
Where the books are held on a server, a long-running query runs in the background and can
be abandoned, rather than locking the window.
*(`AsyncSqlQuery`; the window is Phase 4's P4-SQL-1)*

### P8-STORE-8 — Be helped past a permissions problem rather than stopped by it
When the account a server runs under can't write to the folder the user chose, the user is
told plainly what permission is missing and given the chance to grant it.
*(`DirectorySetup.AddWritePermission` via `SecurityService`. **See Open Question 6** — this
is currently advice, not action.)*

---

## 8.7 Being told something, and being asked

### P8-TELL-1 — Get the product's own message, not the system's
Questions, warnings and failures are presented in the product's own window, matching its
appearance and dimming what's behind it, rather than in a system dialog that looks like it
came from somewhere else.
*(`MessageBoxEx` and the main window's dimming overlay; Phase 2's P2-STATUS-4 is the
dimming, Phase 4's Open Question 29 pushed the window itself here)*

### P8-TELL-2 — Read a long explanation without it filling the screen
A message never grows beyond two-thirds of the screen, and a message with a technical
explanation behind it keeps that explanation folded away until the user asks for it.
*(the max-width/max-height clamp, the "details" disclosure and its text box)*

### P8-TELL-3 — Take a message away with them
The user can copy any message, with its title, to the clipboard — which is what makes it
possible to paste a failure into a bug report or a search.
*(the Ctrl+C / Ctrl+Insert handler in `MessageBoxEx`)*

### P8-TELL-4 — Follow a link out of a message
Where a message offers a link, following it opens the page in the user's browser.
*(the hyperlink branch of `CreateMessage` → `InternetExplorer.OpenUrl`)*

### P8-TELL-5 — Tell at a glance what kind of message this is
Every message carries a mark that distinguishes a question from a warning, a failure or a
plain notice, and a question that was raised without one is given one.
*(`SetImageStyle`, the `MessageBoxImage.Question` default for yes/no messages)*

### P8-TELL-6 — Hear when something long has finished
Where the user has asked for it, a completed save or download is confirmed by a sound as
well as by the status line.
*(`Sounds.PlaySound` gated on `Settings.PlaySounds`)*

---

## 8.8 Narrowing a list by typing

### P8-FIND-1 — Ask for more than one thing at once
The search box above a list understands "this and that", "this or that" and "not this" —
in words or as `&`, `|` and `!` — so a user can narrow to exactly what they mean in one
expression rather than searching twice.
*(`QuickFilterParser`, `FilterAnd`/`FilterOr`/`FilterNot`)*

### P8-FIND-2 — Group parts of a search
Parentheses let the user say "this and either of those" without ambiguity.
*(`FilterParens` and the parser's precedence handling)*

### P8-FIND-3 — Search for a phrase, including one containing a keyword
Putting words in quotes searches for them together, which is also how a user searches for
a party whose name happens to contain the word "and" or "or".
*(the quoted-literal branch of `GetFilterTokens`; words are only treated as operators
where an operator could legally appear)*

### P8-FIND-4 — Type an amount without worrying about the sign
Typing a figure finds it whether it was money in or money out, so the user doesn't have to
remember which way round the entry was recorded.
*(`FilterLiteral.MatchDecimal` comparing both signs. **See Open Question 10** for a date
counterpart that doesn't behave consistently.)*

### P8-FIND-5 — Keep typing without the search breaking
A half-typed expression — a dangling "and", an unclosed bracket — narrows the list as best
it can instead of showing an error or emptying the list.
*(the parser's error-recovery `Combine`, and the null-tolerant `IsMatch` on every filter
node)*

---

## 8.9 Moving things around inside the product

### P8-CLIP-1 — Copy and paste between the product and everything else
Whatever the user has selected — text in a field, a row in a list, a report — copies to
the clipboard in a form other programs can read, and text can be pasted in from them.
*(`IClipboardClient` and its per-control implementations; the routing is Phase 2's
P2-CMD-5)*

### P8-CLIP-2 — Copy a record and paste it back as a real record
A copied account, party or category carries the product's own description of itself
alongside the plain text, so pasting it back into the product recreates the record rather
than a line of text.
*(`MoneyDataObject` offering the same record as text, as XML and as itself.
**See Open Question 21.**)*

### P8-CLIP-3 — Have paste offered only when there is something to paste
The paste command reflects whether the clipboard actually holds something the current
place can accept, updating as the clipboard changes outside the product.
*(`ClipboardMonitor` raising clipboard-changed notifications)*

### P8-CLIP-4 — Drag something onto where it belongs
The user drags a record onto another record to reorganise — a category under another
category, a transaction onto an account — and sees while dragging whether the drop will be
accepted.
*(`DragAndDrop` with its validate/complete callbacks and `AdornerDropTarget` feedback)*

---

## 8.10 Staying up to date

### P8-VER-1 — Be told when a newer version exists
The product checks for a newer release and tells the user when one is available, rather
than leaving them on an old build indefinitely.
*(`ChangeListRequest.BeginGetChangeList` comparing the published version with the running
one; the toolbar affordance is Phase 2's P2-STATUS-7)*

### P8-VER-2 — Read what actually changed before updating
The user is shown the list of changes, version by version with dates, split into what they
already have and what the update would bring — so "should I update" is an informed
decision rather than a leap.
*(`ChangeInfoReport.Generate` over `changes.xml`; the two headings and the
already-installed/now-available split)*

### P8-VER-3 — Install the update from where they read about it
The offer to install sits directly in the change list the user is reading, rather than
sending them elsewhere to find it.
*(`ChangeInfoReport`'s install button and the event it raises)*

### P8-VER-4 — Not be nagged about changes they already have
The install offer only appears when the running build is genuinely behind.
*(`HasLatestVersion`, `IsSameOrOlder`. **See Open Question 22.**)*

### P8-VER-5 — Have a check for updates fail quietly
No connection, a missing change list or an unreadable one leaves the product working
normally with no error in the user's way.
*(the catch-everything paths in `GetChangeList`/`GetDocument`)*

### P8-VER-6 — Open their financial files by double-clicking them
The product registers itself as the program that opens the file types it understands, so a
statement downloaded from a bank or a financial file in a folder opens straight into it.
*(`FileAssociation.Associate` for `.qif`, `.qfx`, `.ofx` and `.mmdb`; what happens next is
Phase 2's P2-START-3)*

---

## 8.11 When something goes wrong

### P8-DIAG-1 — Have the product keep a record of what it did
The product writes a dated log of what it was doing, so a problem that only shows up
occasionally can still be investigated afterwards.
*(`Log` writing one file per day to the per-user log folder, off the interface thread)*

### P8-DIAG-2 — Be told where that record is when it matters
When the user is asked to report a problem, the message tells them where the logs are and
where to send them, rather than assuming they know.
*(`Log.ReportLogging`, quoted by the failure paths)*

### P8-DIAG-3 — Have a crash survive the crash
When the product fails in a way it can't recover from, the details are written out before
it goes, and shown to the user the next time it starts.
*(`Log.FatalUnhandledException` → `CrashReport.Save`; `CrashReport.Load` on the next start
is Phase 2's P2-STATUS-5. **See Open Question 7.**)*

### P8-DIAG-4 — See what a failed download actually said
Failures from the outside world keep the raw response alongside the plain-English summary,
so a user or a helper can see what the other end really replied.
*(the log files each market-data and statement service writes to the log folder; the
statement side is Phase 6's P6-FILE-5)*

### P8-DIAG-5 — Not lose work to a background failure
Work happening in the background — scanning for documents, fetching prices, loading
statement indexes — fails silently and locally rather than taking the product down or
interrupting the user.
*(the guarded background paths in `AttachmentWatcher.ScanDirectory`,
`StatementManager.LoadIndexFile`, `StockSplitDownloader.ProcessPending`. **See Open
Question 23** — "silently" is doing real work in that sentence.)*

---

## 8.12 How the product looks

### P8-LOOK-1 — Choose a light or dark appearance
The user switches the whole product between a light and a dark appearance, and the choice
is remembered.
*(`AppTheme.SetTheme` over `Themes/Light.xaml`/`Themes/Dark.xaml`, `Settings.Theme`; the
switch itself is Phase 2's P2-PREF-1)*

### P8-LOOK-2 — Have the switch take effect everywhere at once
Changing appearance re-colours everything, including the parts of the product that were
drawn in code rather than described in markup, without reopening anything.
*(the new dictionary being merged before the old one is removed;
`AppTheme.GetThemedBrush`/`UpdateDynamicBrushes` for code-created visuals)*

### P8-LOOK-3 — Get one consistent look rather than a system default
A single shared set of styles defines how every list, grid, button, field and scrollbar in
the product looks, so surfaces built years apart still match.
*(`Themes/generic.xaml` merged at application start)*

### P8-LOOK-4 — Recognise a category by its colour, consistently
A category keeps the same colour everywhere it appears — the tree, the register stripe,
the pie chart — including categories the user never picked a colour for, which are given a
stable one derived from their name.
*(`Colors`/`HlsColor`/`CategoryToBrush`, `CategoryData`'s name-derived colour; Phase 5's
P5-CHART-4 is the chart half)*

### P8-LOOK-5 — Recognise a currency or a foreign account by its country
Where a currency is shown, the flag of the country it belongs to is shown with it.
*(`WpfConverters`' flag-path converter and `AccountsControl`'s, over the 271 bundled flag
images)*

---

## 8.13 Trying the product out with realistic data

### P8-SAMPLE-1 — See what the product looks like with a life's worth of data in it
A new user can fill an empty set of books with a realistic synthetic history — accounts,
a salary, recurring bills, investments with real price histories, a rental property — so
they can judge the product without entering their own data first.
*(`SampleDataGenerator.Create` driven by `SampleData.xml`; the options window is Phase 4's
P4-SAMPLE-*)*

### P8-SAMPLE-2 — Shape the sample to something like their own situation
The user says how many years of history, what inflation to assume, who their employer is
and what a paycheque looks like, so the sample resembles a plausible life rather than a
fixed demo.
*(the years/inflation/employer/paycheque inputs passed to `SampleDataGenerator.Create`)*

### P8-SAMPLE-3 — Have sample holdings priced like real ones
Sample investments come with genuine historical prices bundled with the product, so
sample charts and portfolio figures look like real ones rather than flat lines.
*(`SampleDataLoader.LoadEmbeddedSampleData` extracting the bundled price archive into the
download log)*

---

## 8.14 Putting something on paper

### P8-PRINT-1 — Print a receipt or document filed against a transaction
The user can print an attached document, choosing a printer as they would from any other
program.
*(the one `Print` command in `AttachmentDialog`)*

### P8-PRINT-2 — *(not reached)* Print anything else
There is no print command for a report, a register, a statement or a chart anywhere in the
product. Recorded as an absence rather than a capability. **See Open Question 20.**

---

## 8.15 Small conveniences that show up everywhere

### P8-AID-1 — Work out a number where the number goes
Anywhere an amount is entered, the user can type an expression instead of a figure and
have it worked out in place, with a calculator appearing to show what is happening.
*(`CalculatorPopup`/`CalculatorControl`/`Parser`; the surfaces that enable it are Phase 3's
P3-EDIT-9. **See Open Question 17.**)*

### P8-AID-2 — Type a date the short way and get the date they meant
Date fields accept the user's own regional separators, fill in the month from a bare day
number, and — when a partial date would otherwise land in the future — assume the user
meant the year just gone, which is what someone finishing December's entries in January
actually means.
*(`MoneyDatePicker.AutoCompleteDate`)*

### P8-AID-3 — Pick from a long list by typing
Lists of parties, categories and holdings narrow as the user types, so choosing from
thousands of entries is a few keystrokes.
*(`FilteringComboBox`)*

### P8-AID-4 — Have a category's colour chosen visually
Where a colour is set, the user picks it from a palette rather than typing a code.
*(`ColorPickerPanel`)*

### P8-AID-5 — Type a password without it being read over their shoulder
Credential fields mask what is typed, and offer a deliberate way to reveal it when the
user needs to check it.
*(`PasswordControl`; the windows that host it are Phase 4's P4-AUTH-*)*

### P8-AID-6 — Straighten and trim a photographed receipt
A photographed document can have its edges found automatically so the user crops to the
receipt rather than to the table it was lying on.
*(`CannyEdgeDetector` behind the attachment window's auto-crop; `Resizer` for the handles)*

### P8-AID-7 — See a document they can't display as the file it is
A filed document the product can't render inline is shown with the icon the system uses
for that file type, so the user can still tell a PDF from a spreadsheet.
*(`FileIcons.Extract`)*

### P8-AID-8 — Have a long list stay responsive
Lists of tens of thousands of entries scroll, sort and filter without the product
stalling, and a background change to the data updates the list without the user losing
their place.
*(`MoneyDataGrid`, `FilteredObservableCollection`, `DelayedActions` coalescing bursts of
changes, `UiThreadHandler`/`UiThreadPropertyChangedHandler` marshalling background updates)*

---

## Phase 8 coverage checklist

Every file and top-level type in the folders Part 1's sweep assigned to this phase.
"Not user-facing" entries are plumbing, shared primitives, build infrastructure or dead
code the user never perceives.

### `MyMoney/Attachments/`

| Type | Status |
|---|---|
| `AttachmentManager` | P8-ATT-1…P8-ATT-6, P8-ATT-8 — and the "move my attachments folder" flow of P8-ATT-4. See Open Questions 3, 13, 14 |
| `AttachmentWatcher` | P8-ATT-7, P8-ATT-6 — the background scan, the queues and the account-rename folder move |
| `StatementManager` | P8-STMT-1…P8-STMT-7. See Open Questions 1, 2, 14 |
| `StatementIndex` / `StatementItem` | P8-STMT-3, P8-STMT-6 — the per-account index and one statement's date, balance, document and fingerprint |

### `MyMoney.Business/StockQuotes/`

| File | Type | Status |
|---|---|---|
| `StockQuoteManager.cs` | `StockQuoteManager` | P8-QUOTE-1…P8-QUOTE-7, P8-QUOTE-12 |
| `StockQuoteManager.cs` | `DownloadLog`, `DownloadInfo` | P8-QUOTE-5, P8-QUOTE-6, P8-QUOTE-9. **See Open Question 5** |
| `StockQuoteManager.cs` | `HistoryDownloader` | P8-QUOTE-8 — one symbol at a time, newest request first |
| `StockQuoteManager.cs` | `StockSplitDownloader` | P8-QUOTE-12 |
| `IStockQuoteService.cs` | `IStockQuoteService`, `DownloadCompleteEventArgs` | **Not user-facing** — the contract every provider implements; its guarantees are what P8-QUOTE-11 relies on |
| `IStockQuoteService.cs` | `StockQuote`, `StockQuoteHistory`, `DateRange` | P8-QUOTE-8, P8-QUOTE-9, P8-QUOTE-16 — one price, one symbol's history, and the gap-finding. **See Open Question 11** |
| `StockQuoteCache.cs` | `StockQuoteCache`, `StockQuoteIndex` | P8-QUOTE-10, P8-QUOTE-11 |
| `ExchangeRateService.cs` | `ExchangeRateService`, `CurrencyCode`, `FastForexResponse` | P8-QUOTE-14, P8-QUOTE-15. **See Open Question 12** |
| `IOnlineService.cs` | `OnlineServiceSettings`, `IOnlineService` | P8-QUOTE-2, P8-QUOTE-13, P8-QUOTE-17 — the per-provider name, address, credential, allowances and capability switches |
| `StockQuoteThrottle.cs` | `StockQuoteThrottle`, `StockQuoteThrottledException` | P8-QUOTE-13 |
| `ThrottledStockQuoteService.cs` | `ThrottledStockQuoteService` | P8-QUOTE-13 — the shared queue, retry and suspend behaviour every provider inherits |
| `Yahoo.cs`, `Polygon.cs`, `TwelveData.cs`, `MarketStack.cs` | the four providers | P8-QUOTE-2 — individually **not user-facing** beyond being a name in a list; what they enable is captured above |
| `IStockDownloadLog.cs` | `IStockDownloadLog` | **Not user-facing** — the seam that lets the cache read histories without knowing where they came from |
| `Design.dgml` | — | **Not user-facing** — a developer diagram of this folder |

### `MyMoney/Utilities/`

| File | Status |
|---|---|
| `Settings.cs` — `Settings` | P8-PREF-1…P8-PREF-9 |
| `Settings.cs` — `GraphState` | P8-PREF-1 — the remembered chart set-up (Phase 2's P2-PANE-3) |
| `Settings.cs` — `FileAssociation` | P8-VER-6 |
| `MessageBox/MessageBoxEx.xaml` + `.xaml.cs` | P8-TELL-1…P8-TELL-5. **See Open Questions 3 and 4** |
| `AppTheme.cs` | P8-LOOK-1, P8-LOOK-2 |
| `Colors.cs` (`CategoryToBrush` + the palette), `HlsColor.cs` | P8-LOOK-4 |
| `TempFileCollection.cs` (`TempFilesManager`) | P8-ATT-9. **See Open Question 8** |
| `Clipboard.cs` (`IClipboardClient`), `ClipboardClients.cs` | P8-CLIP-1 |
| `ClipboardMonitor.cs` | P8-CLIP-3 |
| `DragAndDrop.cs`, `adorners.cs` (`AdornerDropTarget`) | P8-CLIP-4 |
| `Sounds.cs` | P8-PREF-8, P8-TELL-6 |
| `FileHash.cs` (`HashedFile`) | P8-ATT-8 |
| `FileHelpers.cs` | P8-STMT-3, P8-STMT-5 — relative paths and file comparison behind the statement index |
| `FileIcons.cs` | P8-AID-7 |
| `InternetExplorer.cs` | P8-TELL-4 — and every other "open this in my browser"; badly named, it shells out to the user's default handler. Phase 5's Open Question 25 already flagged the duplication with `NativeMethods.ShellExecute` |
| `ApplicationDeployment.cs` | P8-VER-1 — how the running build reports its own version when installed as a click-once deployment |
| `EdgeDetector.cs` (`CannyEdgeDetector`, `EdgeDetectedEventArgs`) | P8-AID-6 |
| `HelpService.cs`, `RecentFilesMenu.cs`, `AnimatedMessage.cs`, `UndoManager.cs` | **Phase 2** (P2-HELP-1, P2-FILE-3, P2-STATUS-2, P2-HIST-1) |
| `AxisTicker.cs` (`AxisTickSpacer`) | **Not user-facing on its own** — chooses round numbers for chart axis labels; the axes are Phase 5's |
| `XamlHelpers.cs` (`WpfHelper`), `MouseUtilities.cs`, `CompiledPropertySetter.cs`, `UiThreadEventHandler.cs`, `WpfAnnotations.cs` | **Not user-facing** — visual-tree search, a documented workaround for unreliable drag-time mouse positions, a reflection-free property setter, an event-marshalling wrapper, and a marker that tells the unused-style scanner a resource is used from code |
| `PerfTimer.cs` | **Not user-facing** — developer timing |
| `ExponentialDoubleAnimation.cs`, `PointCollectionAnimation.cs` | **Not user-facing** — animation primitives used by the charts (Phase 5) |

### `MyMoney/Setup/`

| File | Status |
|---|---|
| `ChangeInfoReport.cs` | P8-VER-2, P8-VER-3, P8-VER-4 — deferred here by both Phase 2 and Phase 5. **See Open Question 22** |
| `ChangeListRequest.cs` | P8-VER-1, P8-VER-5 |
| `changes.xml` | P8-VER-2 — the shipped change list, ~300 entries newest first |
| `DirectorySetup.cs` | P8-STORE-8. **See Open Question 6** |
| `LatestVersion.xslt` | **Not user-facing** — a stylesheet for rendering the deployment's version document; referenced by no code in this project |

### `MyMoney/Themes/` and `App.xaml`

| File | Status |
|---|---|
| `Light.xaml`, `Dark.xaml` | P8-LOOK-1, P8-LOOK-2 — the two appearances the user can choose |
| `generic.xaml` | P8-LOOK-3 — the shared control styles, merged at startup |
| `Compact.xaml` | **Not reachable** — a density style sheet, commented as imported from another project, that nothing merges and no setting selects. See Open Question 18 |

### `MyMoney/Controls/` (the part no earlier phase claimed)

| File | Status |
|---|---|
| `Calculator/CalculatorPopup.cs`, `CalculatorControl.xaml` + `.xaml.cs`, `Parser.cs` | P8-AID-1 (behaviour: Phase 3's P3-EDIT-9). **See Open Question 17** |
| `Calculator/states.dgml` | **Not user-facing** — a developer diagram of the expression parser |
| `MoneyDatePicker.cs` | P8-AID-2 |
| `FilteringComboBox.cs` | P8-AID-3 |
| `ColorPickerPanel.xaml` + `.xaml.cs` | P8-AID-4 |
| `PasswordControl.xaml` + `.xaml.cs` | P8-AID-5 — including the show/hide toggle and the automation identity that follows it |
| `Resizer.cs` | P8-AID-6 — the drag handles used to crop an attachment |
| `MoneyDataGrid.cs` | P8-AID-8 |
| `WpfConverters.cs` | P8-LOOK-5 (the flag path) plus the display formatting every list relies on — **individually not user-facing**, collectively the reason amounts, dates and states read the way they do |
| `HandyTextBox.cs`, `HandyFlowDocumentScrollViewer.cs`, `SingleLineTextBlock.cs`, `CustomizableButton.cs`, `RoundedButton.xaml` + `.xaml.cs`, `ProgressDots.xaml` + `.xaml.cs`, `TabCloseBox.xaml` + `.xaml.cs`, `StackedBar.cs` | **Not user-facing individually** — presentation primitives; their visible effects belong to the surfaces that host them (Phase 2's P2-STATUS-3 for the progress dots, Phase 3's P3-ACCT-1 for the stacked bar) |
| `Accordion`, `AppSettings`, `QuickFilterControl`, `OutputPane`, `CloseBox`, `DownloadControl`, `DownloadControlProgressReporter` | **Phase 2** |
| `QueryViewControl` | **Phase 3** |
| `TrendGraph` | **Phase 5** |

### `MyMoney/Interop/`, `MyMoney/Database/`, `MyMoney/Icons/`, `MyMoney/Design/`, `MyMoney/Properties/`

| File | Status |
|---|---|
| `Interop/MoneyDataObject.cs` | P8-CLIP-2. **See Open Question 21** |
| `Database/SampleDatabase.cs` | P8-SAMPLE-1, P8-SAMPLE-2, P8-SAMPLE-3 |
| `Database/SampleData.xml`, `SampleStockQuotes.zip` | P8-SAMPLE-1, P8-SAMPLE-3 — the bundled template and its price archive |
| `Database/StripSampleStockQuotes.xslt` | **Not user-facing** — a build-time helper for trimming the bundled price archive |
| `Database/SecurityService.cs`, `WpfDataLayerUiCallback.cs` | P8-STORE-6, P8-STORE-8 — the two places the storage layer reaches back into the interface |
| `Database/DataEngineStartup.cs` | P8-STORE-6. **Debug builds only** — see Open Question 24 |
| `Icons/Flags/` (271 images) | P8-LOOK-5 |
| `Icons/App.ico`, `setup.ico`, `Ding.wav`, `Excel.png`, `TurboTax.png` | Supporting assets for P8-VER-3, P8-TELL-6, Phase 5's P5-VIEW-7 and Phase 7's P7-TXF-1 |
| `Icons/Icon.pptx` | **Not user-facing / design material** |
| `Design/` (16 images, `Map.pdf`, `Icon`/`Map` source files) | **Not user-facing / design material** — mock-ups of a Metro-style redesign that was never built, referenced by no code or markup. Of historical interest to this catalog's purpose; no scenario |
| `Properties/AssemblyInfo.cs`, `Properties/PublishProfiles/` | **Not user-facing / build infrastructure** |
| `GlobalSuppressions.cs`, `dataengine.config.template.json` | **Not user-facing / build and deployment configuration** |

### `MyMoney.Business/` root files claimed here

| File | Type | Status |
|---|---|---|
| `AutoCategorization.cs` | `AutoCategorization` | The engine behind Phase 3's P3-EDIT-7 auto-fill; **claimed here** because no phase owned the file. Its one capability not visible from Phase 3's description is that it learns across *all* accounts a party has appeared in, not just the current one — see P8-AID list note below |
| `Utilities/KNearestNeighbor.cs` | `KNearestNeighbor<T>` | **Not user-facing** — the nearest-amount lookup `AutoCategorization` uses |
| `DatabaseSettings.cs` | `DatabaseSettings` | P8-BOOK-1…P8-BOOK-5 |
| `ISettingsMigrationSource.cs` | `ISettingsMigrationSource` | P8-PREF-4 — the seam the one-time migration uses |
| `DatabaseLifecycle.cs` | `DatabaseConnectionInfo`, `IDatabaseFactory`, `DatabaseLifecycle` | P8-STORE-1 |
| `IDatabase.cs` | `IDatabase`, `DbFlavor`, `IAggregateRoot` | P8-STORE-1 — **the interface itself is not user-facing**; `DbFlavor` is the set of storage choices a user can end up with |
| `Mapping.cs` | `TableMapping`, `ColumnMapping`, `MappingEngine`, `SqlAscii` | **Not user-facing** — how the object graph maps onto stored columns, including the schema upgrade it drives |
| `AsyncSqlQuery.cs` | `AsyncSqlQuery`, `SqlQueryResultArgs` | P8-STORE-7 |
| `Encryption.cs` | `Encryption` | P8-STORE-3. **See Open Question 9** |
| `Query.cs` | `QueryRow`, `Field`, `Operation`, `Conjunction` | The vocabulary behind Phase 3's P3-FIND-4 and Phase 1's P1-FIND-1/2; **claimed here** because no phase owned the file. `CLAUDE.md` already records two of its surprises (an inclusive "greater than", a payment always compared as positive) |
| `SampleDataGenerator.cs` | `SampleDataGenerator`, `SampleData`, `SampleSecurity`, … | P8-SAMPLE-1, P8-SAMPLE-2 |
| `SampleDataLoader.cs` | `SampleDataLoader` | P8-SAMPLE-3 |
| `UsHolidays.cs` | `UsHolidays` | P8-QUOTE-16 |

### `MyMoney.Business/Utilities/`

| File | Status |
|---|---|
| `QuickFilterParser.cs` (`QuickFilterParser<T>`, `Filter<T>`, `FilterKeyword`, `FilterAnd`, `FilterOr`, `FilterNot`, `FilterParens`, `FilterLiteral`) | P8-FIND-1…P8-FIND-5. **See Open Question 10** |
| `Logger.cs` (`ILogger`, `Log`, `CrashReport`) | P8-DIAG-1, P8-DIAG-2, P8-DIAG-3. **See Open Questions 7 and 23** |
| `Dispatcher.cs` (`UiDispatcher`) | **Not user-facing** — but it is *why* Open Questions 3 and 23 behave as they do: it runs a posted action immediately when already on the interface thread and posts it otherwise |
| `DelayedAction.cs` (`DelayedActions`) | P8-AID-8, P8-BOOK-5 — coalescing bursts of work so the product doesn't thrash |
| `FilteredObservableCollection.cs` | P8-AID-8, P8-FIND-1 — the list shape every quick filter runs against |
| `UiThreadHandler.cs`, `UiThreadPropertyChangedHandler.cs`, `EventHandlerCollection.cs` | **Not user-facing** — the cross-thread event marshalling that keeps background work from corrupting the interface |
| `ProcessHelper.cs` | P8-STORE-5 — where the product's own folders and embedded resources come from |
| `NativeMethods.cs` | P8-ATT-3, P8-STMT-3 (turning an account name into a safe folder name), plus the shell "open this file" used throughout |
| `IStatusService.cs` | **Phase 2** (P2-STATUS-2/3) — the contract behind the status line |
| `StringHelpers.cs`, `MathHelpers.cs`, `XmlHelpers.cs`, `Hashset.cs`, `DescendingComparer.cs`, `SimpleGraph.cs` | **Not user-facing** — parsing, rounding, XML and collection helpers. `SimpleGraph` also serialises a parsed search expression for diagnostics |

### `MyMoney.Business/Properties/`

| File | Status |
|---|---|
| `Resources.resx` | The user-visible strings quoted by P8-QUOTE-3, P8-QUOTE-6 and P8-QUOTE-13, and by Phase 6's P6-OFX-6 |
| `Resources.Designer.cs` | **Not user-facing / generated** |

### Supporting files owned by other phases

| File / member | Status |
|---|---|
| `Dialogs/AttachmentDialog.xaml.cs` | **Phase 4** (P4-ATT-*) for the window; what it files and where is P8-ATT-*. The data-loss defect in it is GitHub issue #54 and is deliberately not re-litigated here |
| `View Selectors/BalanceControl.xaml.cs` | **Phase 3** (P3-RECON-*) for the balancing panel; the statement it files is P8-STMT-1 |
| `Controls/AppSettings.xaml.cs` | **Phase 2** (P2-PREF-*) for the panel; what it writes to is P8-PREF-* and P8-BOOK-* |
| `Dialogs/OnlineServiceDialog.xaml.cs` | **Phase 4** (P4-QUOTES-*) for the window; what it configures is P8-QUOTE-2/13/17 |
| `Dialogs/SampleDatabaseOptions.xaml.cs` | **Phase 4** (P4-SAMPLE-*) for the window; what it generates is P8-SAMPLE-* |
| `MainWindow.xaml.cs` — `SetupOnlineServices`, `UpdateCurrencyRates`, `CheckLastVersion`, `ShowChangeInfo`, `OnCommandBackup` | **Phase 2** for the shell's side; the behaviour is P8-QUOTE-*, P8-VER-* |
| `MyMoney.Data/XmlStore.cs`, `SqliteDatabase.cs`, `SqlDatabase.cs`, `SqlServerStoredProcDatabase.cs`, `SqlCeDatabase.cs`, `DatabaseRegistry.cs`, `DatabaseFactory.cs`, `SqlServerBootstrapper.cs`, `DataEnginePasswordGenerator.cs`, `DatabaseSecurityPasswordStore.cs` | **Scoped out** — see Part 1's boundary note and Open Question 25. `XmlStore`'s encryption path is read and captured as P8-STORE-3; `DatabaseRegistry` is captured as P8-STORE-2 |
| `MyMoney.Data/CsvStore.cs` | **Phase 6** |

---

## Open questions from Phase 8

Things a human should double-check, because the call was a judgement rather than obvious
from the code — and, where noted, because they look like real defects. The first nine are
ordered by how much a user could lose by trusting them.

1. **Filing a second statement with the same file name silently overwrites the first.**
   `StatementManager.GetUniqueStatementName` is meant to find a name that isn't taken. It
   builds `fullPath` from the target directory, then loops testing
   `if (!File.Exists(fileName))` — `fileName` being the bare *parameter*, a leaf name like
   `statement.pdf` resolved against the process's working directory, which is never the
   statements folder. That test is therefore effectively always true, so the method returns
   `<dir>\statement0.pdf` on its first iteration **every time**, no matter how many times
   that name has already been used. `ComputeHash` then calls
   `File.Copy(statementFile, newName, true)` — overwrite enabled. The consequence: a user
   who downloads their statements from a bank that names every one `statement.pdf` (which
   is most of them) files the first as `statement.pdf`, the second as `statement0.pdf`,
   and the **third overwrites the second** — while the second statement's index entry
   still points at that file and still carries the second statement's fingerprint. The
   user has lost a document, the index now lies about what is in it, and nothing says so.
   Testing `fullPath` instead of `fileName` is the whole fix. This is the most serious
   thing Phase 8 found and is the same family as the attachment data loss already filed as
   issue #54.

2. **Merging another copy of the books brings statement dates across but silently drops
   every statement document.** `StatementManager.ImportStatements` passes `item.Filename`
   — a path *relative to the other file's index* — as the `statementFile` argument to
   `AddStatement`. `ComputeHash` guards on `File.Exists(statementFile)`, which resolves
   that relative path against the working directory and fails, so the whole body is
   skipped: the new `StatementItem` is added with its date and balance but with no
   document and no fingerprint at all. There is no error, no count and no warning. A user
   merging a laptop copy into a desktop copy keeps their reconciliation history and loses
   the evidence behind it.

3. **A question asked from a background thread answers itself.** `MessageBoxEx.Show`
   builds and shows the window inside `UiDispatcher.BeginInvoke` and then does
   `return result;`. On the interface thread `BeginInvoke` runs the delegate synchronously,
   so this works. Off it, `BeginInvoke` *posts* and `Show` returns
   `MessageBoxResult.None` immediately — before the user has seen the window, let alone
   answered it. Every `MessageBoxEx.Show(...) == MessageBoxResult.OK` test in the product
   is therefore a coin toss decided by which thread the caller happens to be on. The
   confirmations of P8-ATT-4 ("would you like to move the existing attachments
   directory?", "the storage location does not exist, would you like to create it?") and
   P8-STORE-8 are all of this shape. `CLAUDE.md` already records the non-blocking
   behaviour as the reason a crash dialog doesn't halt a FlaUI test; this is the same
   mechanism doing something worse.

4. **The product's own message box makes a hidden button the default.**
   `SetButtonVisibility` works out which button should be the default, stores it in a local
   called `defaultButton` — and then never uses it, unconditionally setting
   `this.ButtonYes.IsDefault = true` and giving `ButtonYes` the accent background. For an
   OK-only or OK/Cancel message that button is collapsed, so the emphasis lands on nothing
   the user can see and Enter's behaviour is at best undefined. Separately, Escape always
   produces a "cancel" answer even on a message whose only button is OK. Three lines from
   the local that was meant to be used.

5. **Today's price is never merged into the stored history, and the code that would do it
   would crash if it ran.** `DownloadLog.OnQuoteAvailable` records a downloaded quote with
   `this._downloadedQuotes.TryUpdate(quote.Symbol, quote, existing)` — `TryUpdate` only
   updates a key that already exists, and nothing ever adds one, so the dictionary stays
   permanently empty and every call returns false. The only reader is in `GetHistory`,
   which does `if (this._downloadedQuotes.TryGetValue(symbol, out var quote)) {
   if (history.MergeQuote(quote)) … }` — and sits *after* the branch that leaves `history`
   null when no history file exists. So the feature is dead, and the accident that killed
   it is also what stops it dereferencing null. The visible effect is that a chart or
   historical valuation for today's date falls back to the security's current price
   (P8-QUOTE-10's `!found` branch) rather than using the quote that was just downloaded.

6. **The "grant the server permission to this folder" step doesn't grant anything.**
   `DirectorySetup.AddWritePermission` ends by building a `FileSystemAccessRule` and
   calling `security.AddAccessRule(...)` — which mutates an in-memory `FileSecurity`
   object. It never calls `SetAccessControl`, so nothing is written to the folder. (It also
   constructs a `FileSecurity` for what is a *directory* path.) What actually happens today
   is the fallback: a message box asking the user to go and set the permission themselves
   in Explorer — and the commented-out recursive re-check right after it shows the author
   knew the verification was missing. P8-STORE-8 describes the intent.

7. **A second crash report corrupts the first.** `CrashReport.Save` opens the file with
   `File.OpenWrite`, which does not truncate. Serializing a *shorter* report over a longer
   one leaves the tail of the previous one behind, producing invalid XML.
   `CrashReport.Load` then throws, swallows the exception and deletes the file — so the
   user is never shown the crash that just happened, and the previous one is gone too.
   `File.Create` is the one-word fix. (`Save` also writes to
   `Path.GetDirectoryName(folder)` — the *parent* of the log folder — which is deliberate
   but reads as a bug.)

8. **Removing a file from the pending-delete list throws.**
   `TempFilesManager.RemoveTempFile` iterates `Instance.files` with `foreach` and calls
   `Instance.files.Remove(path)` inside the loop, which invalidates the enumerator — so
   every call that actually *finds* a match raises `InvalidOperationException`. Its two
   callers are in the attachment window, on the path where a temporary file has just been
   promoted to a real attachment. Related, in the same class: `SaveTempFileList` does
   `doc.Save(TempFileList)` with no `Directory.CreateDirectory` first, so on a machine where
   that folder doesn't yet exist, shutdown throws.

9. **File encryption uses a fixed salt, a fixed initialisation vector, SHA-1 and two
   iterations.** `Encryption` hard-codes `saltValue = "Money Rocks"` and
   `initVector = "*B5good!+027XYZ."`, derives the key with `PasswordDeriveBytes` (the
   deprecated PBKDF1) over SHA-1 with `passwordIterations = 2`, and comments that these
   "cannot change". Every encrypted file the product has ever written therefore shares one
   salt and one IV, and the key derivation offers essentially no resistance to a
   brute-force attempt. A user who password-protects an exported financial file is getting
   much less protection than the feature implies. Changing it breaks every existing file,
   which is presumably why the comment is there — a versioned format is the way out.
   Security exposure of the same class as the findings already filed as issue #56.

10. **The very first row a search is tested against is judged by a different rule from
    every other row.** `FilterLiteral` caches whether the typed text parses as a date. On
    the *first* call, `MatchDate` finds `_date` null, tries to parse, fails, sets
    `notDate = true` and returns `false`. On every call after that, the `notDate` branch at
    the top returns `MatchSubstring(dateTime.ToShortDateString())` instead — i.e. it
    matches the date *as text*. Since one `FilterLiteral` is deliberately shared across the
    whole filtering pass, searching a register for "25" can miss the first row in the list
    on the strength of its date and match the rest. Low impact, trivially fixable, and
    exactly the kind of thing that makes a user distrust a search box.

11. **A hard-coded debugging branch shipped inside the price-history gap finder.**
    `StockQuoteHistory.GetMissingDataRanges` contains
    `if (current.Start.Year == 2025 && current.Start.Month == 1 && current.Start.Day == 8
    && this.Symbol == "MSFT") { Debug.WriteLine("???"); }`. Harmless at runtime, but it is
    live code in a shipped release and a marker of where someone was last debugging this
    algorithm. The same method also silently caps itself at ten ranges and falls back to
    one whole-history request past five, which is a real behavioural rule with no user
    explanation attached.

12. **Exchange rates are anchored to the US dollar whatever the user's own currency is.**
    `ExchangeRateService` requests `fetch-all?from=USD` and stores `Currency.Ratio = 1 /
    rate`, so every currency is expressed against the dollar. P8-BOOK-3 lets the user pick
    any currency to see their totals in, and `DatabaseSettings.DisplayCurrency` defaults to
    `"USD"`. Whether a non-US user's figures come out right depends entirely on how
    `Currency.Ratio` is interpreted downstream (Phase 1's P1-CUR-*), and nothing in either
    place states the convention. Worth someone with two real currencies checking a total by
    hand. Related: the service's name, address and monthly allowance are hard-coded in
    `GetDefaultSettings` and the constructor overwrites whatever address was saved, so
    unlike the four market-data providers this one cannot be pointed anywhere else.

13. **Deleting a transaction deletes its paperwork with no warning.**
    `AttachmentManager.OnMoneyChanged`'s delete branch calls `DeleteAttachments(t)` and
    carries the comment *"todo: would be nice to warn the user they are losing them…"* —
    twice, in two different handlers. Deleting a transaction that has a receipt filed
    against it destroys the receipt immediately and silently. Since the product otherwise
    treats deleting a transaction as an easily-made, low-stakes action, this is a real
    asymmetry a redesign should decide about deliberately.

14. **Documents and statements are filed by account *name*, and two accounts can collide.**
    Both managers build a folder path from `NativeMethods.GetValidFileName(account.Name)`.
    Renames are handled (P8-ATT-6, P8-STMT-5), but two accounts whose names differ only in
    characters that get stripped — `"Visa: Joint"` and `"Visa / Joint"`, say — resolve to
    the same folder, and every document in it is then matched to transactions by id across
    both accounts. `StatementManager` additionally keys its whole in-memory index by the raw
    account name, so two accounts with the *same* name (which the model permits) share one
    statement index outright. Using the account's identifier rather than its name would
    remove a whole class of problem, at the cost of folders a human can no longer read —
    which is exactly the trade-off P8-ATT-3 exists to describe.

15. **Preferences are written by a reflection-driven reader that throws on anything it
    doesn't recognise.** `Settings.ReadXml`/`WriteXml` walk the class's own properties by
    name and handle nine specific types, ending in `throw new Exception("…encountered
    unsupported property type…")`. Adding a preference of any other shape breaks reading
    *and* writing the whole file. There is also no try/catch around `Settings.Load` in this
    class, so a truncated or hand-edited preferences file is a startup failure rather than a
    reset. P8-PREF-3 describes the round-tripping that does work; this is the edge it sits
    on.

16. **`Settings.BackupPath` is a live, saved preference for a command the user cannot
    invoke.** Phase 2's Open Question 5 established that backup is implemented and bound but
    reachable from no menu, button or gesture. This phase confirms the other half: the
    preference behind it is read, written and migrated like any other. So the product
    persistently remembers where the user's backups should go and offers no way to make
    one. Given that P8-ATT-2 and P8-STMT-3 put irreplaceable documents in folders *beside*
    the financial file, "there is no backup" is a bigger gap here than it looks from
    Phase 2.

17. **The in-place calculator doesn't appear for the minus key most people press.**
    `CalculatorPopup.TextBoxPreviewKeyDown` opens the popup for `Add`, `Subtract`,
    `Multiply`, `Divide` (all numeric-keypad keys) and `OemPlus`, but **not** `OemMinus` —
    the `-` on the main keyboard row. Typing `100-20` therefore computes correctly on Enter
    (the handler is attached to the text box regardless of the popup) but shows no
    calculator, while `100+20` shows one. Cosmetic, but it makes the feature look
    unreliable. In the same area, `CalculatorControl.OnKeyDown` is a fourteen-case switch in
    which **every case is an empty `break`** — pure dead code.

18. **A third appearance exists that nothing can select.** `Themes/Compact.xaml` is a
    complete density style sheet, commented as imported from another project, that no code
    merges and no setting names; only `Light.xaml` and `Dark.xaml` are reachable
    (`MainWindow` hard-codes the two paths). Phase 3 recorded that the transaction register
    has its own row-height setting; whether a product-wide compact mode was intended and
    abandoned, or is simply vendored material, is worth establishing before a redesign
    invents one.

19. **Theming is only complete for code that asks for it.** `AppTheme.GetThemedBrush`
    exists because a brush obtained normally is frozen and won't follow a theme change;
    anything drawn in code that *doesn't* go through it keeps its original colour until
    restart. `MessageBoxEx.SetImageStyle` does exactly that, with the comment *"these are
    blending colors so they don't need to be themed"*. `UpdateDynamicBrushes` also throws
    outright if a themed resource turns out not to be a solid colour, and writes a debug
    line — not a user-visible warning — when a brush named by code is missing from the new
    theme. A redesign introducing a third theme would find these one at a time.

20. **Nothing prints, and this phase confirms it from the other side.** The only
    `PrintDialog` in the entire product is in `AttachmentDialog`. There is no print
    command, no print infrastructure, no page setup and no print-specific layout anywhere
    else — not for a report, a register, a statement, a balance or a chart. Phase 5's Open
    Question 6 raised this from the reports side; the cross-cutting sweep finds no shared
    printing layer that a redesign could build on. This is a build-from-nothing item, not a
    wiring-up item.

21. **The product's own clipboard format can be read but not written.**
    `MoneyDataObject` implements every `SetData` overload as
    `throw new NotImplementedException()`. It works today because it is only ever
    constructed around an already-serialized record, but it is a public
    `IDataObject` implementation that throws on half its interface — and it means the
    format is one-directional by construction. Relevant to Phase 3's finding that several
    panels advertise cut and paste and implement neither.

22. **The update check only ever looks at the newest entry.**
    `ChangeInfoReport.HasLatestVersion` opens a `foreach` over every change entry and
    `return`s unconditionally at the end of the first iteration — so the loop is a
    convoluted way of reading `Root.Elements("change").First()`. That is probably the
    intended behaviour given the file is newest-first, but written this way it is
    impossible to tell intent from accident, and it silently assumes an ordering the file
    format doesn't enforce.

23. **Background failures are swallowed completely, including into an empty `catch`.**
    `AttachmentWatcher.ScanDirectory` wraps its entire body in `try { … } catch { }` with
    no logging at all; `StatementManager.LoadIndexFile` catches, writes to the debugger and
    carries a `// TODO: fix corrupt files?`; `StockSplitDownloader.ProcessPending` catches
    everything and comments that it should perhaps record the failure. So a permissions
    problem on the attachments folder, a corrupted statement index or a provider that
    consistently fails for one holding all present to the user as "the feature quietly
    doesn't work". Since P8-DIAG-1 gives the product a perfectly good log, routing these
    into it costs three lines and would turn three invisible failures into diagnosable
    ones.

24. **Reconnecting to a server database on startup is a debug-build-only feature.** The
    whole of `DataEngineStartup` is inside `#if DEBUG`. A release build therefore never
    auto-reopens a SQL Server set of books; the user must go through the File menu every
    time. Whether that is a deliberate safety measure or an unfinished feature that never
    graduated is worth establishing — P8-STORE-6 describes the behaviour the code
    implements, which most users will never see.

25. **Where the `MyMoney.Data` line was drawn — the one scoped-out area.** This phase's
    sweep was scoped by its brief to `MyMoney/` and `MyMoney.Business/`. The third project,
    `MyMoney.Data`, holds the five storage engines (~10,400 lines across `SqlDatabase.cs`,
    `SqlServerStoredProcDatabase.cs`, `SqliteDatabase.cs`, `SqlCeDatabase.cs`,
    `XmlStore.cs`) plus the registry, factory, bootstrapper and password-store files. Phase
    4 owns the windows that drive them, Phase 6 owns `CsvStore`, and this phase owns the
    user-perceivable concepts (P8-STORE-1…P8-STORE-3, P8-STORE-7). **What is not covered
    anywhere is the engines' internal behaviour** — schema upgrade, save batching,
    concurrency conflict detection, and the SQL Server bootstrap sequence. The judgement is
    that almost none of that is user-facing (the user perceives "my work was saved", which
    is Phase 1's P1-WHOLE-2/P1-WHOLE-4), and that the area already has its own design
    document and its own completed work stream — `docs/superpowers/specs/
    2026-09-16-persistence-concurrency-design.md`, issues #20/#26/#27/#41. **A human
    should confirm that judgement**, because it is the only place in this catalog where
    "all source processed" means "assessed and deliberately scoped out" rather than "read
    in full". If a design panel wants the storage layer's user-visible edges — what happens
    when two copies of the product have the same file open, what a schema upgrade looks
    like to the user, what a failed save leaves behind — that is a short Phase 9 over one
    folder, not a re-run of anything here.

26. **Two capabilities in this phase are described by Phase 3 but only visible here.**
    `AutoCategorization` searches the *current* account first and then every other account
    the same party has appeared in (via the payee index), so the product's category
    suggestion can come from a completely different account — which Phase 3's P3-EDIT-7
    doesn't say and a user would not guess. And `Query.cs`'s vocabulary (the fields,
    comparisons and conjunctions of Phase 3's P3-FIND-4) is where `CLAUDE.md`'s two
    documented surprises live. Both files are claimed here without new scenarios, on the
    judgement that the *user goal* is already catalogued and only the mechanism was
    unowned; if a redesign wants "why did it suggest that category", the cross-account
    behaviour is the answer and it should get a scenario of its own.

---

# Phase 9: Data Engine Internals

**Source examined (in full):** every file under `Source/WPF/MyMoney.Data/` — 49 files,
16,572 lines, enumerated with `Glob` rather than taken from Phase 8's list, which named
five files and turned out to be missing eleven more `.cs` files and all 30 SQL scripts.

| File(s) | Lines | Top-level types |
|---|---|---|
| `SqlDatabase.cs` | 3,991 | `ConnectMode`, `DatabaseSecurity`, `SqlServerDatabase`, `BackupResults`, `BackupStatus`, `ExecutionStatus`, `DataError` |
| `SqlServerStoredProcDatabase.cs` | 3,075 | `SqlServerStoredProcDatabase` |
| `SqliteDatabase.cs` | 2,720 | `SqliteDatabase` |
| `XmlStore.cs` | 1,641 | `XmlStore`, `BinaryXmlStore`, `XmlNodeTypeToken`, `BinaryXmlWriter`, `BinaryXmlReader` |
| `Utilities/XmlCsvReader.cs` | 1,058 | `State`, `XmlCsvReader`, `CsvReader` |
| `SqlCeDatabase.cs` | 541 | `SqlCeEngine`, `SqlCeFactory`, `SqlCeDatabase` |
| `Utilities/Credential.cs` | 361 | `CredentialType`, `CredentialFlags`, `CREDENTIAL_ATTRIBUTE`, `CredentialPersistence`, `Credential` |
| `SqlServerBootstrapper.cs` | 276 | `SqlServerBootstrapper` |
| `CsvStore.cs` | 203 | `CsvStore` |
| `DatabaseRegistry.cs` | 173 | `DataEngineType`, `DatabaseRole`, `DatabaseCredential`, `DatabaseServerEntry`, `DatabaseEntry`, `DatabaseRegistry` |
| `DatabaseFactory.cs` | 106 | `DatabaseFactory` |
| `DataEnginePasswordGenerator.cs` | 62 | `DataEnginePasswordGenerator` |
| `SqlServerConnectionFactory.cs` | 31 | `SqlServerConnectionFactory` |
| `DatabaseSecurityPasswordStore.cs` | 21 | `DatabaseSecurityPasswordStore` |
| `IDataLayerUiCallback.cs`, `IDirectorySecurity.cs`, `ISaCredentialPrompt.cs` | 42 | three one-method interfaces |
| `MyMoney.Data.csproj`, `Properties/AssemblyInfo.cs` | 31 | build |
| `SqlScripts/Access/*.sql` (16 files) | 1,762 | the stored-procedure surface the SQL Server engine is restricted to |
| `SqlScripts/Bootstrap/*.sql` (2 files) | 137 | `MyMoney_BootstrapServer`, `MyMoney_CreateCatalog` |
| `SqlScripts/Migrations/*.sql` (1 file) | 78 | the `Version` column |
| `SqlScripts/Test/*.sql` (11 files) | 198 | `_Test_Reset` wipe procedures |

> **Path/naming notes, continuing the run every phase has hit — and this folder is the
> worst offender yet.**
> - **`SqlDatabase.cs` declares no type called `SqlDatabase`.** The class is
>   `SqlServerDatabase`, and the file also carries the Windows Credential Manager wrapper
>   (`DatabaseSecurity`), the load-time integrity-error record (`DataError`) and three
>   leftover backup types. Same trap as Phase 8's `TempFileCollection.cs` and Phase 7's
>   `CapitalGains.cs`.
> - **`SqliteDatabase`, `SqlCeDatabase` and `SqlServerStoredProcDatabase` all inherit from
>   `SqlServerDatabase`.** The class named after one product is the shared base for every
>   engine, including the two with nothing to do with SQL Server. Roughly a third of
>   `SqlDatabase.cs` is generic code that only runs for SQLite.
> - **`DbFlavor.SqlServer` builds `SqlServerDatabase`, not `SqlServerStoredProcDatabase`.**
>   The 3,075-line stored-procedure engine is never reached through `DatabaseFactory` at
>   all; its only production entry point is `SqlServerConnectionFactory.Connect`. See Open
>   Question 4.
> - `Utilities/XmlCsvReader.cs` declares two unrelated classes; only one is named after the
>   file. `DatabaseRegistry.cs` carries five other types that its own comment says moved in
>   from a since-deleted `DataEngineConfig.cs`.
> - `IDirectorySecurity.cs`, `Utilities/Credential.cs` and `Utilities/XmlCsvReader.cs`
>   declare `namespace Walkabout.Utilities`; every other file in the folder is
>   `Walkabout.Data`.
> - `CsvStore.DbFlavor` returns `DbFlavor.Xml`, with a `// BugBug:` comment marking it.
> - The design documents refer to a `SqlScripts/Schema/` folder. There isn't one — schema
>   comes from reflection over `[TableMapping]`, not from scripts.

**Does Phase 8's "almost entirely not user-facing" judgement hold up?** **Mostly, but not
entirely, and the exceptions matter.** The bulk of the folder really is plumbing:
connection lifecycle, SQL text assembly, ADO.NET parameter marshalling and reader loops
account for something like 11,000 of the 16,572 lines and support no user goal that Phase 1
(P1-WHOLE-2 "have work saved", P1-WHOLE-4 "be warned when someone else changed the same
record") and Phase 8 (P8-STORE-1…8) have not already stated. But four things in here are
genuinely user-facing and were catalogued nowhere:

1. **Standing a database server up from nothing** — prompting for the administrator
   password once, inventing and remembering three service logins, creating a catalog,
   deploying its procedures. `SqlServerBootstrapper` + `DatabaseRegistry` + the two
   `Bootstrap/` scripts. Phase 4 owns the windows; nothing owned the capability.
2. **Bringing an older file's shape up to date on open** — `CreateOrUpdateTable`'s
   add/rename/drop/retype logic and SQLite's whole-table rebuild. Phase 8 marked
   `Mapping.cs` "not user-facing — including the schema upgrade it drives", so this
   silently fell between the two phases.
3. **What each storage choice can and cannot do** — user logins, passwords, changing a
   password, per-record saving, conflict detection and direct querying are each supported
   by some engines and not others, and the product mostly does not say which.
4. **What a *failed* save leaves behind** — the part of P1-WHOLE-2 nobody had looked at,
   and the single most serious thing this phase found.

So: 20 scenarios, not zero and not a hundred. The coverage checklist below marks the rest
"not user-facing", each with the concrete reason the code gave.

---

## 9.1 Standing up a place to keep the books

### P9-SERVER-1 — Point the product at a bare database server and have it made ready
The user names a database server they have administrative access to, supplies the
administrator credentials once, and the product does everything else needed to make that
server a place their books can live — creating the accounts it will use day to day and
installing the routines it needs.
*(`SqlServerBootstrapper.BootstrapServerIfNeeded`, `ISaCredentialPrompt`,
`SqlScripts/Bootstrap/MyMoney_BootstrapServer.sql`; the window is Phase 4's P4-DB-*)*

### P9-SERVER-2 — Never have to invent, type or remember the day-to-day logins
The accounts the product uses to read and write the books are created for the user with
strong passwords they never see and never need to know, and are remembered so that opening
those books later is not a sign-in.
*(`DataEnginePasswordGenerator.Generate`, `DatabaseRegistry.Servers`/`Save`. **See Open
Questions 5 and 6.**)*

### P9-SERVER-3 — Add another set of books to a server that is already set up
Once a server has been prepared, creating a second, third or later set of books on it is a
one-step operation — no repeat of the administrator step and no re-entry of credentials.
*(`SqlServerBootstrapper.CreateCatalog`, `MyMoney_CreateCatalog.sql`, the registry's
per-database entries)*

### P9-SERVER-4 — Keep a throwaway set of books that can be safely wiped
The user can mark a set of books as a scratch copy. Only a set of books marked that way
gains the ability to be emptied wholesale, so the real books can never be cleared by the
same action.
*(`DatabaseEntry.TestDatabase`, `SqlServerBootstrapper.CreateCatalog`'s `testDatabase`
branch deploying `SqlScripts/Test/*_Test_Reset` only into a flagged catalog)*

### P9-SERVER-5 — Tell the product where each set of books is, once
Every set of books the user has ever set up is recorded with which engine holds it, which
server and catalog it lives on and when it was last used, in one plain, human-readable file
they can inspect or hand-edit.
*(`DatabaseRegistry` and its JSON shape; the list the user picks from is Phase 8's
P8-STORE-2. **See Open Question 5.**)*

---

## 9.2 Keeping an existing file readable as the product changes

### P9-UPGRADE-1 — Open a file made by an older version and just carry on
When the product has learned to record something new since the user's file was written,
opening that file brings its shape up to date automatically. The user is not asked, is not
made to convert anything, and does not have to know it happened.
*(`SqlServerDatabase.LazyCreateTables`/`CreateOrUpdateTable` and
`SqliteDatabase.CreateOrUpdateTable`, both invoked on open)*

### P9-UPGRADE-2 — Not lose anything when the stored shape changes
Bringing a file up to date preserves what is in it: information that moved to a differently
named place is carried across rather than dropped, and each record keeps the identity and
edit history it already had.
*(`ColumnMapping.OldColumnName` handling — add new column, copy, drop old; SQLite's
rebuild path explicitly carrying the version column across. **See Open Question 9.**)*

### P9-UPGRADE-3 — Be asked before a one-way conversion of the file itself
Where bringing a file up to date means rewriting the file in a format an older version of
the product could no longer open, the user is told what is about to happen and can decline
and leave the file alone.
*(`IDatabase.UpgradeRequired`/`Upgrade` → `DatabaseLifecycle.Open`'s confirmation.
**See Open Question 10** — only one of the five engines ever asks.)*

### P9-UPGRADE-4 — Be told plainly when a file needs something that isn't installed
If a file is in a format that needs a component the machine doesn't have, the user gets an
explanation naming what is missing and where to get it, rather than a failure.
*(`SqlCeDatabase.Connect`'s guidance message and `DatabaseFactory`'s equivalent for the
older format. **See Open Question 11** — one of the two names the wrong product.)*

---

## 9.3 What a save actually guarantees

### P9-SAVE-1 — Have a whole-file save land completely or not at all
When the user saves everything, the result is either all of their changes recorded or none
of them — never a file holding half an edit, an account without its transactions, or a
transfer with only one side.
*(`SqlServerDatabase.Save`'s single transaction around every collection; the stored-proc
engine's `BeginScope` enlisting in it. **See Open Question 1.**)*

### P9-SAVE-2 — Have one edit saved on its own, without rewriting everything
Changing one transaction, one category or one payee writes just that record, so saving
stays instant no matter how many years of history the file holds.
*(`SaveOne`/`SaveTransfer`/`SaveBatch` on `SqliteDatabase` and
`SqlServerStoredProcDatabase`, and the `*_SaveBatch` procedures behind them.
**See Open Question 12** — three of the five engines cannot do this at all.)*

### P9-SAVE-3 — Have a refused save leave the work still there to retry
If a save cannot go through — because the connection dropped, because the file is locked,
or because someone else changed the same record first — the user's unsaved work is still
unsaved rather than quietly discarded, and saving again after fixing the problem writes
exactly what it should have written the first time.
*(the deferred `postCommitActions` design in `SqliteDatabase.SaveBatch` and
`SqlServerStoredProcDatabase.SaveBatch`, which only marks records saved once the write has
actually committed. **See Open Question 1** — the whole-file save path does the opposite,
and this is the most serious defect this phase found.)*

### P9-SAVE-4 — Have a change made in one place be visible as a change everywhere
When the same set of books is open in more than one place, a save made in one of them
leaves a mark the others can see, so a second copy working from an out-of-date picture is
caught rather than allowed to overwrite.
*(the per-row version counter: `Version`/`RowVersion` written by the `*_SaveBatch`
procedures and by SQLite's version-checked updates. The warning the user sees is Phase 1's
P1-WHOLE-4. **See Open Question 2** — the whole-file save path does not leave that mark.)*

---

## 9.4 Reading and checking what is actually stored

### P9-QUERY-1 — Ask a question of the stored data in its own terms
For books kept in a real database, the user can run their own query against the stored data
and see the answer as a table.
*(`QueryDataSet` on each engine; the window is Phase 4's P4-SQL-1 and Phase 8's P8-STORE-7.
**See Open Question 13** — for the file-based formats the query text is ignored entirely.)*

### P9-QUERY-2 — See exactly what the product did to their data
The user can look at the actual sequence of operations the product performed on the last
load or save, which is what makes "it didn't save what I expected" a question someone can
answer.
*(`GetLog`, fed by every engine's own statement log, surfaced in the query window.
**See Open Question 14** — the two engines most likely to be involved return nothing.)*

### P9-QUERY-3 — Have problems in stored data found on the way in
Loading a file checks the relationships between records — that both sides of every transfer
exist, that no record claims two conflicting transfers, that no itemised line is orphaned —
and gathers up what it finds.
*(`DataError` and the error list `ReadTransactions` builds on all three engines; the user
goal is Phase 1's P1-WHOLE-7 and P1-XFER-8. **See Open Question 3** — nothing ever looks at
that list, and `DataError.Heal` is an empty method.)*

---

## 9.5 What each storage choice can and cannot do

### P9-CHOICE-1 — Share one set of books with another person
Where the books are kept on a server, the user can grant another person access to them
under their own sign-in. Where the books are a file, there is no such thing to grant, and
the product doesn't offer it.
*(`IDatabase.SupportsUserLogin` driving the menu item's visibility,
`SqlServerDatabase.AddLogin`; the dialog is Phase 4's P4-DB-5. **See Open Question 7.**)*

### P9-CHOICE-2 — Put a password on the file, and have it mean something
Choosing a password-protected format means the file on disk genuinely cannot be read
without the password; choosing a format that does not support one means the product does
not pretend otherwise.
*(`BinaryXmlStore`'s encrypt/decrypt around save and load, `SqliteDatabase`'s
password-carrying connection; the prompt is Phase 4's P4-DB-6 and the concept is Phase 8's
P8-STORE-3. **See Open Questions 8 and 15.**)*

### P9-CHOICE-3 — Change that password later
A user who wants to change the password on their books can, for the formats where that is
possible, and gets told plainly rather than silently failing where it isn't.
*(`SqliteDatabase.OnPasswordChanged` rekeying the file in place;
`SqlServerDatabase.OnPasswordChanged`'s explicit "not supported, use Save As instead"
message)*

### P9-CHOICE-4 — Keep the books as one ordinary file they can copy
For every storage choice except a server database, the books are a single file the user can
copy, move, put on a drive or hand to someone, with no export step and nothing left behind
that the copy needs.
*(`SqliteDatabase`/`SqlCeDatabase`/`XmlStore`'s file-based `Exists`/`Delete`/`Backup`;
SQLite's sidecar files are cleaned up alongside the main file on delete and folded back into
it before a copy. **See Open Question 16.**)*

---

## Coverage checklist

### The five storage engines

| File / type | Status |
|---|---|
| `SqlDatabase.cs` — `SqlServerDatabase` (the class, as the shared base) | P9-UPGRADE-1, P9-UPGRADE-2, P9-SAVE-1, P9-QUERY-1, P9-QUERY-2, P9-CHOICE-1, P9-CHOICE-3 |
| `SqlDatabase.cs` — `DatabaseSecurity` | P9-SERVER-2 — the Windows Credential Manager entry that makes a password-protected file open without re-prompting |
| `SqlDatabase.cs` — `DataError` | P9-QUERY-3. `Heal(MyMoney)` is an **empty method body** with a `// heal thyself!` comment — dead. See Open Question 3 |
| `SqlDatabase.cs` — `BackupResults`, `BackupStatus`, `ExecutionStatus` | **Not user-facing / dead** — the return types of a SQL Agent backup-job feature that exists only as ~80 lines of commented-out code (`CheckBackupJob`, `RemoveBackupJob`, `GetBackupStatus`). Nothing constructs `BackupResults` |
| `SqlDatabase.cs` — `ConnectMode` enum | **Dead** — declared, never referenced anywhere in the solution |
| `SqlDatabase.cs` — `DBString`, `DBDateTime`, `DBDecimal`, `DBGuid` and the per-table `UpdateXxx` string-building branches | **Not user-facing on their own** — how a row becomes SQL text for the two engines that don't use parameters. But see Open Questions 17 and 18: this path corrupts line breaks and can fail outright on an apostrophe |
| `SqlDatabase.cs` — `IsSqlExpressInstalled`, `IsSqlLocalDbInstalled`, `Attach`, `DropTable` | **Not user-facing** — machine probing and a table-drop helper called from nowhere. `DropTable` emits `DROP TABLE 'name'`, which is not valid T-SQL; it has no callers, so nothing has ever run it |
| `SqlDatabase.cs` — `LoadTableMetadata`, `GetTableSchema`, `ReadSqlDataType`, `ReadYesNoAsBoolean`, `GetCreateTableScript`, `GetAddForeignKeyScripts`, `GetCreateIndexScripts` | **Not user-facing** — the reflection-to-DDL machinery behind P9-UPGRADE-1 |
| `SqliteDatabase.cs` — `SqliteDatabase` | P9-UPGRADE-1, P9-UPGRADE-2, P9-SAVE-2, P9-SAVE-3, P9-SAVE-4, P9-CHOICE-3, P9-CHOICE-4. The default local format |
| `SqliteDatabase.cs` — `ParseColumnSql`/`ParseSqlType` | **Not user-facing** — re-parsing SQLite's stored `CREATE TABLE` text to work out what the file's current shape is, because SQLite has no schema catalog to ask |
| `SqlCeDatabase.cs` — `SqlCeDatabase` | P9-UPGRADE-3, P9-UPGRADE-4, P9-CHOICE-4 — a **legacy-only** engine: `DatabaseFactory` will open an existing `.sdf`, and `MainWindow`'s Save As still lists it, but no "create new" path offers it |
| `SqlCeDatabase.cs` — `SqlCeEngine`, `SqlCeFactory` | **Not user-facing** — a hand-built reflection shim that loads the SQL CE assembly lazily so the installer doesn't require it. Its only user-visible consequence is P9-UPGRADE-4's message |
| `SqlServerStoredProcDatabase.cs` | P9-SAVE-1, P9-SAVE-2, P9-SAVE-3, P9-SAVE-4 — the engine used when the books are on a shared server, restricted to stored procedures so the day-to-day login has no direct table rights |
| `XmlStore.cs` — `XmlStore` | P9-CHOICE-4, P9-QUERY-1 (as the limitation). The plain-XML format. **See Open Questions 8, 13, 15, 19** |
| `XmlStore.cs` — `BinaryXmlStore` | P9-CHOICE-2, P9-CHOICE-4 — the only format that actually encrypts. **See Open Question 8** |
| `XmlStore.cs` — `BinaryXmlWriter`, `BinaryXmlReader`, `XmlNodeTypeToken` (lines 303–1641) | **Not user-facing / dead in the product** — a complete binary XML serializer, 1,338 lines, whose own header says *"currently not in use, but may come in handy in the future"*. Both call sites in `BinaryXmlStore` are commented out, with a note that it was no faster than gzipped text. Referenced only by `UnitTests/DataTests.cs`. **81% of `XmlStore.cs` is this** |
| `CsvStore.cs` | **Phase 6** (P6-OUT-4; `ImportCsv` already recorded there as dead). Re-read here and confirmed: `DbFlavor` returns `Xml`, `Backup` reports *"XML Backup is not implemented"*, and `ImportCsv` parses dates and amounts with the machine's current culture despite demanding ISO-8601 input — moot only because nothing calls it |

### Setting up and finding a database

| File / type | Status |
|---|---|
| `DatabaseFactory.cs` | P9-UPGRADE-4, P9-CHOICE-2 — turns a chosen storage kind into a live engine. **See Open Questions 4 and 11** |
| `DatabaseRegistry.cs` — `DatabaseRegistry`, `DatabaseEntry`, `DatabaseServerEntry`, `DatabaseCredential`, `DatabaseRole`, `DataEngineType` | P9-SERVER-2, P9-SERVER-3, P9-SERVER-4, P9-SERVER-5; Phase 8's P8-STORE-2. **See Open Question 5** |
| `SqlServerBootstrapper.cs` | P9-SERVER-1, P9-SERVER-3, P9-SERVER-4. **See Open Question 6** |
| `SqlServerConnectionFactory.cs` | P9-SERVER-5 — resolves a name from the registry into an open connection. Also the only production path that reaches `SqlServerStoredProcDatabase` (Open Question 4) |
| `DataEnginePasswordGenerator.cs` | P9-SERVER-2. **See Open Question 20** |
| `DatabaseSecurityPasswordStore.cs` | P9-SERVER-2 — a three-line adapter over `DatabaseSecurity`, existing only because the business layer cannot reference this assembly |
| `IDatabaseFactory`/`IDatabasePasswordStore` implementations generally | **Not user-facing** — layering seams; the decision logic they serve is Phase 8's `DatabaseLifecycle` |

### Interfaces, utilities and build files

| File / type | Status |
|---|---|
| `IDataLayerUiCallback.cs` | **Not user-facing** — lets the WPF-free data layer raise a warning (the mixed-mode-logins notice in P9-CHOICE-1) without referencing WPF |
| `IDirectorySecurity.cs` | **Not user-facing** — the seam behind Phase 8's P8-STORE-8; the implementation is `MyMoney/Setup/DirectorySetup.cs`, already covered there |
| `ISaCredentialPrompt.cs` | P9-SERVER-1 — the "ask for the administrator password" seam |
| `Utilities/Credential.cs` — `Credential` and its four supporting types | P9-SERVER-2, P9-CHOICE-2 — the Windows Credential Manager P/Invoke. **See Open Question 21** |
| `Utilities/XmlCsvReader.cs` — `XmlCsvReader`, `CsvReader`, `State` | **Not user-facing / dead in the product** — a 2001–2005 Microsoft sample (`Copyright (c) 2001-2005 Microsoft Corporation`, "Chris Lovett") that presents a CSV file as an `XmlReader`. `XmlCsvReader` is referenced only by `UnitTests/CsvTest.cs`; `CsvReader` only by `CsvStore.ImportCsv`, which Phase 6 already established is called from nowhere. **The whole 1,058-line file is unreachable from the product** |
| `MyMoney.Data.csproj`, `Properties/AssemblyInfo.cs` | **Not user-facing / build infrastructure.** Note the project declares **two** SQLite packages (`SQLitePCLRaw.bundle_e_sqlite3` and `System.Data.SQLite`) and ships no `<Content>` item for `SqlScripts/` — see Open Question 22 |

### The checked-in SQL

| File(s) | Status |
|---|---|
| `SqlScripts/Bootstrap/MyMoney_BootstrapServer.sql` | P9-SERVER-1, P9-SERVER-2 — creates or re-passwords the three logins, idempotently |
| `SqlScripts/Bootstrap/MyMoney_CreateCatalog.sql` | P9-SERVER-3 — creates one catalog by name, with the catalog name quoted specifically against injection |
| `SqlScripts/Migrations/2026-09-17-add-version-column.sql` | P9-SAVE-4 — adds the per-row version counter to the eleven aggregate-root tables. **See Open Question 23** |
| `SqlScripts/Access/*_AccessProcs.sql` (16 files) | P9-SAVE-1, P9-SAVE-2, P9-SAVE-4 — the complete set of operations the day-to-day login is permitted. **Not user-facing individually**; their user-visible consequence is that a shared server cannot be damaged by the running application beyond these operations. **See Open Question 2** |
| `SqlScripts/Test/*_TestProcs.sql` (11 files) | P9-SERVER-4 — the wholesale-wipe procedures, deployed only into a catalog flagged as a test database |

---

## Open questions from Phase 9

Ordered, as Phase 8's were, by how much a user could lose by trusting them. The first four
look like real defects rather than judgement calls.

1. **A whole-file save marks everything as saved *before* it commits, so a save that fails
   at the last step destroys the work it failed to write.** `SqlServerDatabase.Save(MyMoney)`
   opens one transaction, calls `UpdateOnlineAccounts`, `UpdateCategories`, … in turn, and
   only then calls `tran.Commit()`. But **every one of those `UpdateXxx` methods ends with
   `foreach (… x) x.OnUpdated();` followed by `xs.RemoveDeleted()`** — clearing each
   record's pending-change marker and purging deleted records from memory, while the
   transaction is still open. If the commit then throws (a dropped connection, a deadlock, a
   full disk) the `using` block rolls the database back — and the in-memory model now
   believes it has no pending changes at all. The user's edits are gone from the database
   *and* from the product's idea of what still needs saving; saving again writes nothing.
   The same applies if any `UpdateXxx` throws part-way: the collections already processed
   have been marked clean and will not be retried. This is precisely the failure mode
   `SqliteDatabase.SaveBatch` and `SqlServerStoredProcDatabase.SaveBatch` were carefully
   built to avoid — both defer every in-memory side effect into a `postCommitActions` list
   with a long comment explaining why — so the correct pattern already exists in the same
   folder and is simply not used by the whole-file path. **This is the most serious thing
   Phase 9 found**, it affects every engine, and it belongs with the "the product says it
   saved and it didn't" family already filed as #54.

2. **Saving the whole file does not bump the version counter, so the protection against two
   copies overwriting each other is switched off for exactly the operation most likely to
   need it.** The per-record path (`*_SaveBatch`) checks `ExpectedVersion` and writes
   `Version = Version + 1`. The per-row procedures the whole-file save uses —
   `Transactions_Update`, `Categories_Update`, `Payees_Update` and the other thirteen — do
   neither: no version check, no increment. Two consequences, both silent. A second copy of
   the product holding a stale record still sees the version it remembers, so its next save
   overwrites the first user's work with no conflict raised (Phase 1's P1-WHOLE-4 simply
   does not fire). And a full save can overwrite a record another copy changed a moment
   earlier without noticing. The SQLite whole-file path has the same gap for the same
   reason: `Save(MyMoney)` routes through the inherited `UpdateXxx` methods, not through
   `SaveBatch`. Worth deciding deliberately, because the design document this work came from
   treats the version column as the guarantee.

3. **Every integrity problem found while loading a file is collected and then thrown
   away.** All three engines' `ReadTransactions` build an `ArrayList` of `DataError` records
   — "other side of split transfer not found", "transaction is marked as a transfer, but
   other side of transfer was not found", "already have a transfer for this transaction, so
   transfer N is a duplicate of transfer M" — and return it. **Every production caller
   discards the return value**: `SqlServerDatabase.Load` and
   `SqlServerStoredProcDatabase.Load` both call `this.ReadTransactions(…)` as a bare
   statement. The only code that reads the list is the unit tests. `DataError.Heal(MyMoney)`
   — the method whose name says it was meant to repair these — has an empty body and a
   `// heal thyself!` comment. So Phase 1's P1-XFER-8 ("have broken transfers found and
   reported") is half true: they are found, on every load, and reported to nobody. Surfacing
   them costs about as much as Phase 8's Open Question 23 logging suggestion and would turn
   a class of silent corruption into something a user could act on.

4. **The 3,075-line stored-procedure engine is unreachable through the normal
   open-a-database path.** `DatabaseFactory.CreateDatabase`'s `DbFlavor.SqlServer` case
   constructs `SqlServerDatabase` — the raw-SQL engine, which needs direct table rights
   the `MyMoneyUser` login is deliberately not granted. `SqlServerStoredProcDatabase` is
   constructed in exactly one production place, `SqlServerConnectionFactory.Connect`, which
   is reached only through the registry-driven path. So the engine that the whole
   `SqlScripts/Access/` surface, the bootstrapper and the concurrency design exist to serve
   is not what you get by opening a SQL Server database through `DatabaseLifecycle`. Either
   the factory case is wrong or `DbFlavor` needs to distinguish the two; a human should say
   which, because it determines whether any of Open Questions 2, 17 and 18 can actually be
   hit in production.

5. **The three generated server passwords are written to an unprotected plain-text file.**
   `DatabaseRegistry.Save` writes `%AppData%\…\dataengine.config.json` with
   `File.WriteAllText` — no encryption, no DPAPI, no ACL tightening — and that file holds
   `MyMoneyAdmin`, `MyMoneyUser` and `MyMoneyTest` with their passwords in the clear.
   `MyMoneyAdmin` holds the `dbcreator` server role. The product already has a perfectly
   good secret store for exactly this (`DatabaseSecurity`, the Windows Credential Manager,
   used for the file password) and does not use it here. This is the same "security
   exposure" family as the import/export findings filed as #56, and it sits next to the
   hardcoded-salt/IV encryption bug already tracked in #52.

6. **A bootstrap that fails half way leaves logins on the server whose passwords nobody
   knows.** `BootstrapServerIfNeeded` generates three passwords, deploys and *executes*
   `MyMoney_BootstrapServer` (which creates the three logins with those passwords), then
   deploys `MyMoney_CreateCatalog` — and only writes the passwords into the registry after
   all of that succeeds. If the second deploy throws, the method returns `false` and the
   passwords are lost, while the logins exist on the server. It recovers in practice only
   because `MyMoney_BootstrapServer` is written with `ALTER LOGIN` in its `ELSE` branch, so
   a retry re-passwords the existing logins rather than failing — which also means each
   retry silently rotates credentials any other machine may be holding. Saving the registry
   *before* executing, or writing it in the same failure path, would make this explicit
   rather than accidental.

7. **Adding a user to a shared database makes them a server administrator, via
   string-concatenated SQL.** `SqlServerDatabase.AddLogin` runs
   `sp_addLogin '<user>','<password>'` followed by
   `sp_addsrvrolemember '<user>','sysadmin'` — both built by concatenating the values
   straight into the statement with no escaping of any kind, and the second one grants the
   new person full control of the entire SQL Server instance, not just these books. The
   menu item that reaches it (`File` ▸ `Add User`, Phase 4's P4-DB-5) is shown whenever the
   books are on SQL Server. A password containing an apostrophe breaks the statement; one
   crafted deliberately runs whatever it likes as the administrator who is doing the adding.
   P9-CHOICE-1 describes the intent; this is what the code does.

8. **The plain-XML format accepts a password and silently ignores it.** `XmlStore` has a
   `Password` property, `DatabaseFactory` passes one into its constructor *and* sets the
   property, and `DatabaseLifecycle.Open` saves it to the credential store — but
   `XmlStore.Save` and `XmlStore.Load` never encrypt or decrypt anything. Only
   `BinaryXmlStore` does. Today's `Save As` flow happens to pass `null` for `.xml` and only
   prompts for `.bxml`, so no user is currently handed an unencrypted file they think is
   encrypted; but every other layer is written as though `.xml` supported a password, and
   one changed line at the call site would make it a real exposure. Separately: while a
   `.bxml` file is being read or written, the **fully decrypted contents of the user's
   entire financial history exist as a plain file in `%TEMP%`**, and on the read path
   `File.Delete(tempPath)` sits after the parse rather than in a `finally` — so any load
   failure leaves that plaintext copy behind indefinitely.

9. **Bringing an older file up to date can rebuild a table, and the SQLite rebuild has one
   unguarded step.** `SqliteDatabase.CreateOrUpdateTable`'s `newTable` path creates
   `NEW_<Table>`, copies the rows across, `DROP TABLE`s the original and renames — all
   inside one transaction, which is right. Two notes: the copy list is driven by the *old*
   table's columns, so a genuinely new column is simply absent from the insert and takes its
   default, which is intended; but the guard `if (first) throw new Exception("Invalid table
   definition, no columns found in the old table")` is the only thing standing between a
   mis-parsed `CREATE TABLE` (see `ParseColumnSql`, which reads SQLite's stored DDL text
   with a hand-written scanner) and a `DROP TABLE` of real data. The parser is the weakest
   link in P9-UPGRADE-2 and has no test coverage named in this folder.

10. **Only one of the five engines ever asks permission to upgrade.**
    `IDatabase.UpgradeRequired` is `true` only for `SqlCeDatabase`.
    `SqlServerDatabase.UpgradeRequired` is written as
    `try { this.Connect(); return false; } catch { return false; }` — both branches return
    `false`, so the `try`/`catch` is decorative and the method's only real effect is to open
    a connection as a side effect. SQLite and XML return `false` outright, and
    `SqliteDatabase.Upgrade()` is an empty method marked `// TBD`. Which is fine, because
    those engines' upgrades are non-destructive and happen silently on open (P9-UPGRADE-1) —
    but it means P9-UPGRADE-3's confirmation is dead for four engines out of five, and a
    reader of `IDatabase` would not guess that.

11. **The "you need to install something" message names the wrong product.**
    `DatabaseFactory`'s `DbFlavor.SqlCE` branch throws *"SQL Express does not appear to be
    installed any more so we can't open your existing database"* and then links to the SQL
    Server **Compact** Edition download. `SqlCeDatabase.Connect`'s own message for the same
    condition gets it right. A user told to install SQL Express will install the wrong thing
    and the file still won't open. Both messages also point at `microsoft.com/download`
    URLs for a product retired years ago.

12. **Three of the five engines cannot save a single record.** `SaveOne`, `SaveTransfer` and
    `SaveBatch` throw `NotImplementedException` on `SqlServerDatabase` (the base, and so on
    `SqlCeDatabase`), on `XmlStore`/`BinaryXmlStore` (with an explicit "see the design
    spec's Non-goals") and on `CsvStore`. Only `SqliteDatabase` and
    `SqlServerStoredProcDatabase` implement them. P9-SAVE-2 describes a real capability, but
    it is a property of the storage choice, and nothing tells the user that picking `.xml`
    means every edit rewrites the whole file. Worth stating in whatever replaces the
    storage-choice window.

13. **Running a query against an XML file ignores the query.**
    `XmlStore.QueryDataSet(string cmd)` never looks at `cmd`; it calls
    `result.ReadXml(this.filename)` and returns the entire file as a dataset.
    `CsvStore.QueryDataSet` returns an empty dataset. So the query window (P4-SQL-1) accepts
    whatever the user types against those formats and answers something unrelated, with no
    indication that the question was not asked.

14. **The "what did the product just do" log is empty for the engines most likely to need
    it.** `XmlStore.GetLog()` and `CsvStore.GetLog()` return `""`. More surprisingly,
    `SqlServerStoredProcDatabase` inherits `GetLog` but never writes to the log — its
    `ExecuteProc`/`ExecuteSaveBatchProc` helpers bypass the base class's logging
    `ExecuteScalar`/`ExecuteNonQuery` entirely. So for a shared-server database, the
    diagnostic window shows the log of whatever ran through the *base* class, which on that
    engine is nothing.

15. **`SqlCeDatabase` builds its connection string with SQL Server's builder.**
    `GetConnectionString` uses `Microsoft.Data.SqlClient.SqlConnectionStringBuilder` — a
    different product's class — to produce a string handed to SQL CE, and ignores its own
    `includeDatabase` parameter, with the original logic left behind as ten lines of
    commented-out string concatenation. It works only because `Data Source` and `Password`
    happen to mean the same thing in both. `SqliteDatabase.GetConnectionString` also ignores
    `includeDatabase`, and `SqliteDatabase.Create()` assigns a connection string to a local
    that is never used.

16. **`Backup` copies without permission to overwrite, on three engines.**
    `SqliteDatabase.Backup`, `SqlCeDatabase.Backup` and `XmlStore.Backup` all call
    `File.Copy(source, backupPath)` with no `overwrite` argument, so backing up twice to the
    same name throws `IOException`. `MainWindow.OnCommandBackup` deletes the target first,
    which papers over it — but it also means that **between the delete and the copy the
    user has no backup at all**, and if the copy fails they have lost the previous one.
    `SqlServerDatabase.Backup` has a different sharp edge: it flips the database to `SIMPLE`
    recovery, checkpoints, flips back to `FULL` and then backs up `WITH INIT`, which breaks
    any existing log-backup chain and unconditionally overwrites the target. Phase 2's Open
    Question 5 already notes the backup command is unreachable from the UI; if it is
    restored, these are the behaviours being restored.

17. **The non-parameterized save path turns line breaks in notes into literal `\r` and
    `\n` text.** `SqlServerDatabase.DBString` does
    `s.Replace("'", "''").Replace("\r", "\\r").Replace("\n", "\\n")`. The first replacement
    is the correct T-SQL escape; the other two are C-style escapes that **T-SQL does not
    interpret** — SQL Server string literals have no backslash escapes. So a multi-line memo
    or account description saved through this path is stored with the two-character
    sequences `\r` and `\n` where the line breaks were, and reads back that way forever. The
    path is used by `SqlServerDatabase` and `SqlCeDatabase` (`SupportsParameterizedUpdate`
    is `false` for both); SQLite is immune because it opts into parameters. Whether this is
    live in production depends on Open Question 4.

18. **One field on that same path forgets to escape at all, so an apostrophe breaks the
    save.** `UpdateLoanPayments`' change branch writes
    `sb.Append(string.Format(",Memo='{0}'", i.Memo))` — the raw value, with no `DBString`
    call — while the *insert* branch three lines below does use `DBString`. A loan-payment
    memo containing an apostrophe therefore saves fine when first entered and fails with a
    raw SQL syntax error the next time it is edited. The same omission, on fields less
    likely to contain quotes, appears in `UpdateCategories` (`Color`, `TaxRefNum`),
    `UpdateSecurities` (`CuspId`) and `UpdateAliases`/`UpdateAccountAliases` (`AccountId`).
    `SqlServerDatabase.Create` and `Delete` interpolate the database name into
    `Create Database {0}` / `DROP DATABASE {0}` unbracketed for good measure, so a file whose
    name contains a space or a hyphen fails to create with a syntax error.

19. **Saving the plain-XML format leaves the document still marked as unsaved.** Every other
    engine's `Save` ends by calling `money.OnSaved()` — `SqlServerDatabase.Save` after its
    commit, `BinaryXmlStore.Save` after its write. `XmlStore.Save` does not, and
    `MyMoney.Save(IDatabase)` does not do it for them. So after saving to `.xml` the product
    still believes there are unsaved changes. `XmlStore` is asymmetric in two more ways worth
    checking together: its `Load` constructs the serializer **without**
    `MyMoney.GetKnownTypes()` while its `Save` constructs one **with** them, and its `Load`
    calls `PostDeserializeFixup()` but not `OnLoaded()`, where `BinaryXmlStore.Load` calls
    both.

20. **The generated-password routine has a small modulo bias.**
    `DataEnginePasswordGenerator.PickRandomChar` draws four random bytes and takes
    `value % charset.Length` without rejection sampling, and `Shuffle` does the same. With
    a 24-character password drawn from a 68-character alphabet the practical effect is
    negligible, and the source of randomness is a proper CSPRNG — noting it only because
    it is the kind of thing a security review will flag and it is a three-line fix.

21. **Looking up a saved file password signals "there isn't one" by throwing.**
    `DatabaseSecurity.LoadDatabasePassword` calls `Credential.Load()`, which throws
    `Win32Exception("No credential exists with the specified TargetName")` when nothing is
    stored — the normal first-run case. `MainWindow` catches it and treats it as "prompt the
    user", so the behaviour is correct; but the sibling `SaveDatabasePassword` swallows
    *all* exceptions into `Debug.WriteLine`, so a password that fails to save fails
    invisibly and the user is silently re-prompted next time. Exception-as-control-flow on
    one side and exception-suppression on the other, in a fourteen-line class.

22. **Nothing in this project copies the SQL scripts to the output, and the project
    references two different SQLite packages.** `SqlServerBootstrapper` takes a
    `sqlScriptsRoot` and reads `Bootstrap/`, `Migrations/`, `Access/` and `Test/` off disk at
    run time, but `MyMoney.Data.csproj` has no `<Content>`/`<None CopyToOutputDirectory>`
    item for `SqlScripts/`; the plan documents say the copy lives in `MyMoney.csproj` and is
    **DEBUG-only**. If that is still true, a release build cannot bootstrap a server or
    create a catalog at all — the same shape as Phase 8's Open Question 24, where the SQL
    Server reconnect path also turned out to be debug-only. Separately, the project
    references both `SQLitePCLRaw.bundle_e_sqlite3` and `System.Data.SQLite`; only the
    latter is used in code.

23. **The version-column migration names one specific catalog and covers eleven tables.**
    `2026-09-17-add-version-column.sql` opens with `USE MyMoney;` — the bootstrapper strips
    any batch starting `USE`, so creating a differently-named catalog still works, but a
    human running the file by hand as its own header instructs would target the wrong
    database. The script adds `Version` to the eleven aggregate-root tables and deliberately
    not to `Splits`, `Investments`, `RentUnits`, `AccountAliases` or `TransactionExtras`,
    which are saved as part of their parent. That is the design, but it means those five
    tables' rows are protected only by their parent's counter, and `RentUnits` in particular
    is written by `SaveRentUnitsForBuilding` with no version check of any kind — including
    after the parent building's own `DELETE` has already run.

24. **`SqlServerStoredProcDatabase.ReadStockSplits` is declared `new`, not `override`.**
    The base member is `protected virtual`; the subclass declares
    `public new void ReadStockSplits(…)`, which hides rather than overrides it. It works
    today only because this class also overrides `Load` and calls the method through a
    statically-typed `this`. Any future code that reads stock splits through an `IDatabase`
    or a `SqlServerDatabase` reference would silently get the base class's raw-SQL version,
    which the day-to-day login has no rights to execute. `SqliteDatabase` overrides the same
    member correctly, which is what makes the difference look accidental.

---

# Catalog complete

All nine phases are done. **Every file in all three source projects** —
`Source/WPF/MyMoney/`, `Source/WPF/MyMoney.Business/` and `Source/WPF/MyMoney.Data/` — has
now been read in full and either mapped to a scenario or recorded as not user-facing with a
concrete reason. Phase 8's Open Question 25 named `MyMoney.Data/` as the one place where
"all source processed" meant "assessed and scoped out" rather than "read"; Phase 9 closed
that, and the qualifier no longer applies anywhere in this catalog.

**What is here**

| | |
|---|---|
| Scenarios | **811** across nine phases — 161 (core model), 70 (shell), 153 (views), 106 (dialogs), 90 (reports and charts), 71 (import/export), 41 (taxes), 99 (cross-cutting), 20 (data engine internals) |
| Open questions | **193** — judgement calls, boundary decisions and suspected defects a human should confirm |
| Phases | 9 of 9 complete |

**Where the bugs went.** This audit was a source-code read, not a test pass, but it kept
finding things that were plainly wrong rather than merely undecided. Those have been
triaged into GitHub issues so that a design panel reading this catalog isn't also acting
as a bug tracker:

- **[#52](https://github.com/markabrandjord/MyMoney.Net/issues/52)** — general findings
  (dead commands, unreachable UI, convention drift, "nothing prints a report")
- **[#53](https://github.com/markabrandjord/MyMoney.Net/issues/53)** — an entire
  rent-entry surface that nothing in the product can open
- **[#54](https://github.com/markabrandjord/MyMoney.Net/issues/54)** — *high priority*:
  saving an attachment destroys non-image attachments
- **[#55](https://github.com/markabrandjord/MyMoney.Net/issues/55)** — *high priority*:
  retirement-projection and tax-report calculation defects
- **[#56](https://github.com/markabrandjord/MyMoney.Net/issues/56)** — *high priority*:
  import/export data corruption and security exposure
- **[#57](https://github.com/markabrandjord/MyMoney.Net/issues/57)** — *high priority*:
  tax calculation errors

Phase 8's own first-ranked findings — statement documents being silently overwritten
(Open Question 1) and silently dropped on merge (Open Question 2) — belong with #54 as
the same "paperwork the user believes is filed is not" family, and are not yet filed.

Phase 9's are also unfiled, and two of them outrank most of what is already tracked. Its
**Open Question 1** — a whole-file save clears every record's pending-change marker
*before* the transaction commits, so a save that fails at the last step destroys the work
it failed to write, on every engine — belongs with #54. Its **Open Questions 2, 5 and 7**
— full saves bypassing the version counter that the concurrency protection depends on,
three server passwords stored in clear text in `%AppData%`, and "add a user" granting
`sysadmin` through string-concatenated SQL — belong with #56, alongside the encryption
bug already noted in #52. Its **Open Question 3** — every integrity error found while
loading a file is collected and then discarded, with the method meant to repair them left
empty — is the cheapest of the lot to fix and turns silent corruption into something a
user can see.

**How to read this catalog.** Each scenario is a *user goal*, deliberately written without
naming a control, a class or a screen, so that a redesign can satisfy it any way it likes.
The italic note under each one is traceability back to the source, for anyone who needs to
check what the current product actually does — it is not part of the requirement. The
per-phase coverage checklists are the proof of completeness; the per-phase open questions
are where the current product's intent is genuinely unclear, and are the shortest list of
decisions a redesign has to make before it starts.
