using System;
using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Utilities;
using Walkabout.Views;

namespace Walkabout.Tests
{
    [TestFixture]
    public class ExecuteQueryTests
    {
        private static MyMoney BuildMoneyWithCategorizedTransactions()
        {
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var groceries = money.Categories.GetOrCreateCategory("Food:Groceries", CategoryType.Expense);
            var fuel = money.Categories.GetOrCreateCategory("Auto:Fuel", CategoryType.Expense);

            for (int i = 0; i < 3; i++)
            {
                var t = new Transaction(money.Transactions);
                t.Account = account;
                t.Date = new DateTime(2026, 1, 1 + i);
                t.Amount = -20.00M - i;
                t.Category = groceries;
                money.Transactions.AddTransaction(t);
            }

            var fuelTxn = new Transaction(money.Transactions);
            fuelTxn.Account = account;
            fuelTxn.Date = new DateTime(2026, 1, 10);
            fuelTxn.Amount = -40.00M;
            fuelTxn.Category = fuel;
            money.Transactions.AddTransaction(fuelTxn);

            return money;
        }

        [Test]
        public void ExecuteQuery_SingleFieldEquals_ReturnsExactCount()
        {
            MyMoney money = BuildMoneyWithCategorizedTransactions();

            var query = new[]
            {
                new QueryRow { Field = Field.Category, Operation = Operation.Equals, Value = "Food:Groceries" }
            };

            IList<Transaction> result = money.Transactions.ExecuteQuery(query);

            Assert.That(result.Count, Is.EqualTo(3), "Expected exactly the 3 seeded Groceries transactions.");
        }

        [Test]
        public void ExecuteQuery_NoMatchingValue_ReturnsEmpty()
        {
            MyMoney money = BuildMoneyWithCategorizedTransactions();

            var query = new[]
            {
                new QueryRow { Field = Field.Category, Operation = Operation.Equals, Value = "Nonexistent:Category" }
            };

            IList<Transaction> result = money.Transactions.ExecuteQuery(query);

            Assert.That(result.Count, Is.EqualTo(0));
        }

        [Test]
        public void ExecuteQuery_TwoRowsWithAndConjunction_NarrowsResult()
        {
            MyMoney money = BuildMoneyWithCategorizedTransactions();

            // Transaction.Matches(QueryRow) converts Field.Payment to a positive value
            // (-Amount, only when Amount <= 0), and QueryRow.Matches(decimal)'s GreaterThan
            // case is actually implemented as ">=" (confirmed by reading Query.cs - not a
            // guess). The 3 seeded Groceries transactions have Payment 20.00/21.00/22.00
            // (Amount -20/-21/-22), so ">21.00" (really ">=21.00") matches the 21.00 AND
            // 22.00 rows - 2 transactions, not 1.
            var query = new[]
            {
                new QueryRow { Field = Field.Category, Operation = Operation.Equals, Value = "Food:Groceries" },
                new QueryRow { Field = Field.Payment, Operation = Operation.GreaterThan, Value = "21.00", Conjunction = Conjunction.And }
            };

            IList<Transaction> result = money.Transactions.ExecuteQuery(query);

            Assert.That(result.Count, Is.EqualTo(2), "Expected the Groceries transactions with Payment >= 21.00 (the -21.00 and -22.00 rows).");
        }

        [Test]
        public void ExecuteQuery_CategoryMatchesOnlyASplitNotTheWholeTransaction_ReturnsFakeSplitTransaction()
        {
            // ExecuteQuery's split-row branch (Money.cs's Transactions.ExecuteQuery): when the
            // whole transaction doesn't match, but it IsSplit, each Split is tried via
            // Split.Matches(QueryRow) (which supports Category/Memo/Payee but explicitly NOT
            // Payment/Deposit - "too confusing if we match amount on splits", per its own
            // comment) and a matching split gets wrapped in a synthetic, read-only
            // Transaction(t, split) - confirmed by reading that constructor: Category/Amount/
            // Memo/Payee are all proxied from the split, not the parent transaction.
            var money = new MyMoney();
            var account = money.Accounts.AddAccount("Checking");
            var groceries = money.Categories.GetOrCreateCategory("Food:Groceries", CategoryType.Expense);
            var fuel = money.Categories.GetOrCreateCategory("Auto:Fuel", CategoryType.Expense);

            var splitTxn = new Transaction(money.Transactions);
            splitTxn.Account = account;
            splitTxn.Date = new DateTime(2026, 1, 15);
            splitTxn.Amount = -70.00M;
            splitTxn.Category = money.Categories.Split; // matches production's OnCommandSplits convention
            var fuelSplit = splitTxn.NonNullSplits.AddSplit();
            fuelSplit.Category = fuel;
            fuelSplit.Amount = -40.00M;
            var groceriesSplit = splitTxn.NonNullSplits.AddSplit();
            groceriesSplit.Category = groceries;
            groceriesSplit.Amount = -30.00M;
            money.Transactions.AddTransaction(splitTxn);

            var query = new[]
            {
                new QueryRow { Field = Field.Category, Operation = Operation.Equals, Value = "Food:Groceries" }
            };

            IList<Transaction> result = money.Transactions.ExecuteQuery(query);

            Assert.That(result.Count, Is.EqualTo(1), "Expected exactly the one matching split, not the whole (non-matching-category) parent transaction.");
            Transaction fake = result[0];
            Assert.That(fake.IsReadOnly, Is.True, "Split-row query results are synthetic read-only proxies.");
            Assert.That(fake.Category, Is.SameAs(groceries));
            Assert.That(fake.Amount, Is.EqualTo(-30.00M), "The fake transaction's Amount should be the split's amount, not the parent's total.");
        }
    }

    [TestFixture]
    public class QuickFilterParserTests
    {
        private MyMoney money;
        private Account account;
        private Transaction costcoShopping;
        private Transaction chevronFuel;
        private Transaction costcoGasStation;
        private Transaction amazonOrder;
        private Transaction gasThenCostcoMemo;
        private TransactionCollection collection;

        [SetUp]
        public void SetUp()
        {
            this.money = new MyMoney();
            this.account = this.money.Accounts.AddAccount("Checking");
            var shoppingCostco = this.money.Categories.GetOrCreateCategory("Shopping:Costco", CategoryType.Expense);
            var autoFuel = this.money.Categories.GetOrCreateCategory("Auto:Fuel", CategoryType.Expense);
            var shoppingMisc = this.money.Categories.GetOrCreateCategory("Shopping:Misc", CategoryType.Expense);
            var shoppingAmazon = this.money.Categories.GetOrCreateCategory("Shopping:Amazon", CategoryType.Expense);

            var costcoPayee = this.money.Payees.AddPayee(1);
            costcoPayee.Name = "COSTCO WHOLESALE";
            var chevronPayee = this.money.Payees.AddPayee(2);
            chevronPayee.Name = "CHEVRON";
            var costcoGasPayee = this.money.Payees.AddPayee(3);
            costcoGasPayee.Name = "COSTCO GAS STATION";
            var amazonPayee = this.money.Payees.AddPayee(4);
            amazonPayee.Name = "AMAZON";
            var randomPayee = this.money.Payees.AddPayee(5);
            randomPayee.Name = "RANDOM SHOP";

            this.costcoShopping = this.NewTransaction(costcoPayee, shoppingCostco, "");
            this.chevronFuel = this.NewTransaction(chevronPayee, autoFuel, "");
            this.costcoGasStation = this.NewTransaction(costcoGasPayee, autoFuel, "");
            this.amazonOrder = this.NewTransaction(amazonPayee, shoppingAmazon, "online order");
            // Contains "costco" and "gas" as separate words, but never the literal
            // substring "costco &gas" - the case that distinguishes a quoted literal
            // (treats "&" as plain text) from unquoted "costco & gas" (treats "&" as AND).
            this.gasThenCostcoMemo = this.NewTransaction(randomPayee, shoppingMisc, "gas then costco run");

            // Pass an already-materialized list, matching every real call site of
            // TransactionCollection's constructor (e.g. TransactionsView.xaml.cs's
            // GetSelectedTransactions()/GetTransactionsFrom() call sites) - never the raw
            // Transactions container itself. Money.cs's Transactions.CopyTo(Transaction[], int)
            // is an unimplemented ICollection<Transaction> member (throws
            // NotImplementedException); ObservableCollection<T>'s constructor uses
            // ICollection<T>.CopyTo when the source is one, so passing the container directly
            // throws immediately. No production code path is affected since nothing does that.
            IList<Transaction> allTransactions = this.money.Transactions.GetTransactionsFrom(this.account);
            this.collection = new TransactionCollection(this.money, this.account, allTransactions, false, false, null);
        }

        private Transaction NewTransaction(Payee payee, Category category, string memo)
        {
            var t = new Transaction(this.money.Transactions);
            t.Account = this.account;
            t.Date = new DateTime(2026, 1, 1);
            t.Amount = -10.00M;
            t.Payee = payee;
            t.Category = category;
            t.Memo = memo;
            this.money.Transactions.AddTransaction(t);
            return t;
        }

        private bool Matches(string quickFilter, Transaction t)
        {
            var parser = new QuickFilterParser<Transaction>();
            Filter<Transaction> expr = parser.Parse(quickFilter);
            return expr.IsMatch(this.collection, t);
        }

        [Test]
        public void Parse_LiteralMatch_MatchesOnlyTransactionsContainingTheWord()
        {
            Assert.That(this.Matches("costco", this.costcoShopping), Is.True, "Payee 'COSTCO WHOLESALE' should match literal 'costco'.");
            Assert.That(this.Matches("costco", this.costcoGasStation), Is.True, "Payee 'COSTCO GAS STATION' should match literal 'costco'.");
            Assert.That(this.Matches("costco", this.chevronFuel), Is.False, "Chevron/Auto:Fuel transaction has no 'costco' anywhere.");
            Assert.That(this.Matches("costco", this.amazonOrder), Is.False, "Amazon transaction has no 'costco' anywhere.");
        }

        [Test]
        public void Parse_And_MatchesOnlyTransactionsContainingBothWords()
        {
            Assert.That(this.Matches("costco and fuel", this.costcoGasStation), Is.True, "Payee 'COSTCO GAS STATION' + Category 'Auto:Fuel' contains both words.");
            Assert.That(this.Matches("costco and fuel", this.costcoShopping), Is.False, "Category 'Shopping:Costco' has 'costco' but no 'fuel'.");
            Assert.That(this.Matches("costco and fuel", this.chevronFuel), Is.False, "Payee 'CHEVRON' + Category 'Auto:Fuel' has 'fuel' but no 'costco'.");
        }

        [Test]
        public void Parse_Or_MatchesTransactionsContainingEitherWord()
        {
            Assert.That(this.Matches("costco or fuel", this.costcoShopping), Is.True);
            Assert.That(this.Matches("costco or fuel", this.chevronFuel), Is.True);
            Assert.That(this.Matches("costco or fuel", this.costcoGasStation), Is.True);
            Assert.That(this.Matches("costco or fuel", this.amazonOrder), Is.False, "Amazon transaction has neither word.");
        }

        [Test]
        public void Parse_QuotedLiteralContainingAmpersand_TreatsAmpersandAsLiteralTextNotAnd()
        {
            // Unquoted: "&" tokenizes as AND, so "costco &gas" == "costco and gas" -
            // matches gasThenCostcoMemo because its memo contains both words separately.
            Assert.That(this.Matches("costco &gas", this.gasThenCostcoMemo), Is.True,
                "Unquoted 'costco &gas' parses as AND(costco, gas); the memo 'gas then costco run' contains both words.");

            // Quoted: the whole "costco &gas" becomes one literal, matched as a substring.
            // gasThenCostcoMemo's memo ("gas then costco run") never contains that exact
            // contiguous substring, so it must NOT match.
            Assert.That(this.Matches("\"costco &gas\"", this.gasThenCostcoMemo), Is.False,
                "Quoted \"costco &gas\" is one literal substring; the memo doesn't contain it verbatim.");
        }
    }
}
