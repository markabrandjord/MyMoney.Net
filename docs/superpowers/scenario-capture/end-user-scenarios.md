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
| 5 | Reports + charts | `Reports/`, `Charts/` | Not started |
| 6 | Import/export | `Importers/`, `Ofx/`, CSV/QIF/XML storage formats | Not started |
| 7 | Taxes | `Taxes/` | Not started |
| 8 | Cross-cutting | Online banking, attachments, printing, settings | Not started |

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
