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
| 3 | Views | `Views/` — the grids, trees and panes the user works in | Not started |
| 4 | Dialogs | `Dialogs/` — modal flows, wizards, editors | Not started |
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
