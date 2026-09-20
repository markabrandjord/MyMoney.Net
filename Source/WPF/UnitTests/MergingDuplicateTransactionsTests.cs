using System;
using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class MergingDuplicateTransactionsTests
    {
        [Test]
        public void FindPotentialDuplicate_SameAmountAndCloseDate_ReturnsMatch()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var payee = money.Payees.AddPayee(1);
            payee.Name = "TARGET T-1234";

            var existing = new Transaction(money.Transactions);
            existing.Account = account;
            existing.Date = new DateTime(2026, 1, 20);
            existing.Amount = -11.73M;
            existing.Payee = payee;
            existing.FITID = "FID-A";
            money.Transactions.AddTransaction(existing);

            var incoming = new Transaction(money.Transactions);
            incoming.Account = account;
            incoming.Date = new DateTime(2026, 1, 20);
            incoming.Amount = -11.73M;
            incoming.Payee = payee;
            incoming.FITID = "FID-B";

            // FindPotentialDuplicate does NOT scan the whole candidate list for anything
            // matching t - it requires t ITSELF to already be an element of tc (via
            // `int i = tc.IndexOf(t); if (i > 0) { ... }`), then only looks at t's immediate
            // list-neighbors (i-1, i+1, i-2, i+2, ...) for a match. The plan's original example
            // built `candidates = { existing }` (incoming not in the list at all) - that would
            // have returned null unconditionally, before comparing anything. Confirmed by
            // reading the implementation; incoming must be in the list, at an index > 0.
            var candidates = new List<Transaction> { existing, incoming };
            Transaction match = Transactions.FindPotentialDuplicate(incoming, candidates, TimeSpan.FromDays(3));

            Assert.That(match, Is.SameAs(existing));
        }

        [Test]
        public void FindPotentialDuplicate_TransactionNotInCandidateList_ReturnsNull()
        {
            // Direct documentation of the IndexOf-based contract above: a caller that (like the
            // plan's original example) passes a candidate list NOT containing the transaction
            // itself gets null unconditionally, even when an exact-match duplicate is right
            // there in the list - not because nothing matched, but because the search never
            // started (`tc.IndexOf(t)` returns -1, so `i > 0` is false).
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var payee = money.Payees.AddPayee(1);
            payee.Name = "TARGET T-1234";

            var existing = new Transaction(money.Transactions);
            existing.Account = account;
            existing.Date = new DateTime(2026, 1, 20);
            existing.Amount = -11.73M;
            existing.Payee = payee;
            money.Transactions.AddTransaction(existing);

            var incoming = new Transaction(money.Transactions);
            incoming.Account = account;
            incoming.Date = new DateTime(2026, 1, 20);
            incoming.Amount = -11.73M;
            incoming.Payee = payee;
            // incoming deliberately NOT added to money.Transactions or to candidates below.

            var candidates = new List<Transaction> { existing };
            Transaction match = Transactions.FindPotentialDuplicate(incoming, candidates, TimeSpan.FromDays(3));

            Assert.That(match, Is.Null);
        }

        [Test]
        public void FindPotentialDuplicate_NoNearbyMatch_ReturnsNull()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var payee = money.Payees.AddPayee(1);
            payee.Name = "TARGET T-1234";
            var otherPayee = money.Payees.AddPayee(2);
            otherPayee.Name = "SOMEWHERE ELSE";

            var unrelated = new Transaction(money.Transactions);
            unrelated.Account = account;
            unrelated.Date = new DateTime(2026, 1, 1);
            unrelated.Amount = -5.00M;
            unrelated.Payee = otherPayee;
            money.Transactions.AddTransaction(unrelated);

            var incoming = new Transaction(money.Transactions);
            incoming.Account = account;
            incoming.Date = new DateTime(2026, 1, 20);
            incoming.Amount = -11.73M;
            incoming.Payee = payee;
            money.Transactions.AddTransaction(incoming);

            var candidates = new List<Transaction> { unrelated, incoming };
            Transaction match = Transactions.FindPotentialDuplicate(incoming, candidates, TimeSpan.FromDays(3));

            Assert.That(match, Is.Null);
        }

        [Test]
        public void Merge_PreservesCategoryFromOneSideAndMemoFromOther()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var category = money.Categories.GetOrCreateCategory("Shopping", CategoryType.Expense);

            var survivor = new Transaction(money.Transactions);
            survivor.Account = account;
            survivor.Date = new DateTime(2026, 1, 20);
            survivor.Amount = -11.73M;
            survivor.Memo = "Original memo";
            money.Transactions.AddTransaction(survivor);

            var duplicate = new Transaction(money.Transactions);
            duplicate.Account = account;
            duplicate.Date = new DateTime(2026, 1, 20);
            duplicate.Amount = -11.73M;
            duplicate.Category = category;
            money.Transactions.AddTransaction(duplicate);

            bool changed = survivor.Merge(duplicate);

            Assert.That(changed, Is.True);
            Assert.That(survivor.Category, Is.SameAs(category), "Category should be copied from the duplicate since the survivor had none.");
            Assert.That(survivor.Memo, Is.EqualTo("Original memo"), "Memo should be preserved from the survivor since it already had one.");
        }

        [Test]
        public void Merge_BothSidesAreTransfersToDifferentAccounts_ThrowsApplicationException()
        {
            // A real, documented exception path in Transaction.Merge (Money.cs): merging two
            // transactions that are each already a transfer, but to DIFFERENT target accounts,
            // is treated as an unresolvable conflict rather than silently picking one side.
            var money = new MyMoney();
            var checking = money.Accounts.AddAccount("Checking");
            var savings = money.Accounts.AddAccount("Savings");
            var creditCard = money.Accounts.AddAccount("Credit Card");

            var survivor = new Transaction(money.Transactions);
            survivor.Account = checking;
            survivor.Date = new DateTime(2026, 1, 20);
            survivor.Amount = -50.00M;
            money.Transactions.AddTransaction(survivor);
            money.Transfer(survivor, savings);

            var duplicate = new Transaction(money.Transactions);
            duplicate.Account = checking;
            duplicate.Date = new DateTime(2026, 1, 20);
            duplicate.Amount = -50.00M;
            money.Transactions.AddTransaction(duplicate);
            money.Transfer(duplicate, creditCard);

            Assert.That(() => survivor.Merge(duplicate),
                Throws.TypeOf<ApplicationException>()
                    .With.Message.EqualTo("Cannot merge when both transactions are transferred to a different place"));
        }

        [Test]
        public void Merge_DuplicateCategoryIsTransferToDeletedAccountPlaceholder_ReturnsFalseWithoutMerging()
        {
            // Transaction.Merge's first check: if the incoming duplicate's category is the
            // synthetic "Xfer to/from Deleted Account" placeholder (assigned by RemoveTransfer/
            // CheckTransfers when a transfer's target account was deleted), Merge bails out
            // immediately and returns false - it doesn't merge any fields at all, not even ones
            // the survivor is missing. Confirmed by reading Merge's first few lines.
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");

            var survivor = new Transaction(money.Transactions);
            survivor.Account = account;
            survivor.Date = new DateTime(2026, 1, 20);
            survivor.Amount = -50.00M;
            money.Transactions.AddTransaction(survivor);

            var duplicate = new Transaction(money.Transactions);
            duplicate.Account = account;
            duplicate.Date = new DateTime(2026, 1, 20);
            duplicate.Amount = -50.00M;
            duplicate.Memo = "Should not be copied";
            duplicate.Category = money.Categories.TransferToDeletedAccount;
            money.Transactions.AddTransaction(duplicate);

            bool changed = survivor.Merge(duplicate);

            Assert.That(changed, Is.False);
            Assert.That(survivor.Memo, Is.Null.Or.Empty, "Nothing should have been copied over once the deleted-account-transfer guard tripped.");
        }
    }
}
