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
| 2 | Navigation & shell | Main window, view selectors, command routing | Not started |
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
