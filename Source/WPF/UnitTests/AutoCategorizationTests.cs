using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class AutoCategorizationTests
    {
        private static Account CreateAccount(MyMoney money)
        {
            return money.Accounts.AddAccount("Checking");
        }

        [Test]
        public void AutoCategoryMatch_KnownPayeeHistory_ReturnsMatchingTransaction()
        {
            var money = new MyMoney();
            var account = CreateAccount(money);
            var category = money.Categories.GetOrCreateCategory("Food:Groceries", CategoryType.Expense);
            var payee = money.Payees.AddPayee(1);
            payee.Name = "SAFEWAY #4455";

            var history = new Transaction(money.Transactions);
            history.Account = account;
            history.Date = new DateTime(2026, 1, 3);
            history.Amount = -55.00M;
            history.Payee = payee;
            history.Category = category;
            money.Transactions.AddTransaction(history);

            var newTxn = new Transaction(money.Transactions);
            newTxn.Account = account;
            newTxn.Date = new DateTime(2026, 2, 3);
            newTxn.Amount = -55.00M;

            object result = AutoCategorization.AutoCategoryMatch(newTxn, "SAFEWAY #4455");

            Assert.That(result, Is.Not.Null, "Expected a match from known payee history.");
            var matched = result as Transaction;
            Assert.That(matched, Is.Not.Null, "Expected the match to be a plain (non-split) Transaction.");
            Assert.That(matched.Category, Is.SameAs(category));
        }

        [Test]
        public void AutoCategoryMatch_NoPriorHistoryForPayee_ReturnsNull()
        {
            var money = new MyMoney();
            var account = CreateAccount(money);

            var newTxn = new Transaction(money.Transactions);
            newTxn.Account = account;
            newTxn.Date = new DateTime(2026, 2, 3);
            newTxn.Amount = -55.00M;

            object result = AutoCategorization.AutoCategoryMatch(newTxn, "NEVER SEEN BEFORE INC");

            Assert.That(result, Is.Null, "Expected no match when the payee has no transaction history at all.");
        }

        [Test]
        public void AutoCategoryMatch_ZeroAmount_FallsBackToClosestByDate()
        {
            var money = new MyMoney();
            var account = CreateAccount(money);
            var payee = money.Payees.AddPayee(1);
            payee.Name = "ACME PAYROLL";
            var oldCategory = money.Categories.GetOrCreateCategory("Income:Salary-Old", CategoryType.Income);
            var newCategory = money.Categories.GetOrCreateCategory("Income:Salary-New", CategoryType.Income);

            var older = new Transaction(money.Transactions);
            older.Account = account;
            older.Date = new DateTime(2025, 6, 1);
            older.Amount = 1000.00M;
            older.Payee = payee;
            older.Category = oldCategory;
            money.Transactions.AddTransaction(older);

            var mostRecent = new Transaction(money.Transactions);
            mostRecent.Account = account;
            mostRecent.Date = new DateTime(2026, 1, 1);
            mostRecent.Amount = 1200.00M;
            mostRecent.Payee = payee;
            mostRecent.Category = newCategory;
            money.Transactions.AddTransaction(mostRecent);

            var newTxn = new Transaction(money.Transactions);
            newTxn.Account = account;
            newTxn.Date = new DateTime(2026, 1, 15);
            newTxn.Amount = 0M;

            object result = AutoCategorization.AutoCategoryMatch(newTxn, "ACME PAYROLL");

            var matched = result as Transaction;
            Assert.That(matched, Is.Not.Null);
            Assert.That(matched, Is.SameAs(mostRecent), "With a zero amount, the closest-by-date transaction should win, not amount-based nearest-neighbor.");
        }
    }
}
