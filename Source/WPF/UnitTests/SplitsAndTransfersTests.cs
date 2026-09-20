using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class SplitsAndTransfersTests
    {
        [Test]
        public void Split_NotSummingToTotal_ReportsUnassignedAmount()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var category = money.Categories.GetOrCreateCategory("Fun", CategoryType.Expense);

            var t = new Transaction(money.Transactions);
            t.Account = account;
            t.Date = System.DateTime.Now;
            t.Amount = -100.00M;
            var splits = t.NonNullSplits;
            var s = splits.AddSplit();
            s.Category = category;
            s.Amount = -40.00M;
            money.Transactions.AddTransaction(t);

            // Nothing recomputes Unassigned automatically from AddSplit/Split.Amount in this
            // headless (no UI databinding) scenario - Split.OnAmountChanged is empty, and
            // Splits.AddSplit's InsertItem only fires property-changed notifications, not a
            // Rebalance(). Real UI code relies on WPF's grid/property-changed plumbing to
            // trigger it eventually; a direct business-layer caller must call it explicitly.
            splits.Rebalance();

            Assert.That(splits.HasUnassigned, Is.True);
            Assert.That(splits.Unassigned, Is.EqualTo(-60.00M));
        }

        [Test]
        public void Transfer_CreatesLinkedTransactionInTargetAccount()
        {
            var money = new MyMoney();
            var source = money.Accounts.AddAccount("Checking");
            var target = money.Accounts.AddAccount("Savings");

            var t = new Transaction(money.Transactions);
            t.Account = source;
            t.Date = System.DateTime.Now;
            t.Amount = -200.00M;
            money.Transactions.AddTransaction(t);

            money.Transfer(t, target);

            Assert.That(t.Transfer, Is.Not.Null, "Source transaction should now have a Transfer link.");
            Transaction linked = t.Transfer.Transaction;
            Assert.That(linked.Account, Is.SameAs(target));
            Assert.That(linked.Amount, Is.EqualTo(200.00M), "The linked transaction in the target account should carry the opposite sign.");
        }

        [Test]
        public void RemoveTransfer_UnreconciledBothSides_RemovesTargetSideOnly()
        {
            var money = new MyMoney();
            var source = money.Accounts.AddAccount("Checking");
            var target = money.Accounts.AddAccount("Savings");

            var t = new Transaction(money.Transactions);
            t.Account = source;
            t.Date = System.DateTime.Now;
            t.Amount = -200.00M;
            money.Transactions.AddTransaction(t);
            money.Transfer(t, target);
            Transaction linked = t.Transfer.Transaction;

            money.RemoveTransfer(t);

            // Real behavior, confirmed by reading MyMoney.RemoveTransfer(Transfer): it only
            // calls RemoveTransaction on the "target" (the OTHER side of the transfer) - the
            // side RemoveTransfer was called ON just has its own Transfer link cleared and
            // survives as a plain, non-transfer transaction. The plan's original assumption
            // that BOTH sides get deleted was wrong; corrected here after tracing the real
            // source/target roles in RemoveTransfer(Transfer t).
            Assert.That(t.IsDeleted, Is.False, "The side RemoveTransfer was called on should survive as a plain transaction.");
            Assert.That(t.Transfer, Is.Null, "Its Transfer link should be cleared.");
            Assert.That(linked.IsDeleted, Is.True, "The other side of the transfer should be removed.");
        }

        // Per the plan's Step 4: trace the real enforcement point for "can't remove a transfer
        // whose partner is reconciled" rather than assume one. Found in RemoveTransfer(Transfer
        // t): when the OTHER side (the "target") is TransactionStatus.Reconciled, it throws
        // MoneyException("Transfer is reconciled on the other side and cannot be modified
        // outside of balancing the target account.") - a real exception, not a bool result or
        // silent no-op - and this check happens before any removal, so neither side is touched.
        [Test]
        public void RemoveTransfer_PartnerIsReconciled_ThrowsAndLeavesBothSidesIntact()
        {
            var money = new MyMoney();
            var source = money.Accounts.AddAccount("Checking");
            var target = money.Accounts.AddAccount("Savings");

            var t = new Transaction(money.Transactions);
            t.Account = source;
            t.Date = System.DateTime.Now;
            t.Amount = -200.00M;
            money.Transactions.AddTransaction(t);
            money.Transfer(t, target);
            Transaction linked = t.Transfer.Transaction;
            linked.Status = TransactionStatus.Reconciled;

            Assert.That(() => money.RemoveTransfer(t), Throws.TypeOf<MoneyException>()
                .With.Message.EqualTo("Transfer is reconciled on the other side and cannot be modified outside of balancing the target account."));

            Assert.That(t.IsDeleted, Is.False);
            Assert.That(linked.IsDeleted, Is.False);
            Assert.That(t.Transfer, Is.Not.Null, "The transfer link should remain intact after the blocked removal.");
        }
    }
}
