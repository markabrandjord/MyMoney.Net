using System.IO;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class SampleDataRegressionTests
    {
        private MyMoney GenerateSample(int seed)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "MyMoneyTests", "SampleDataRegressionTests", seed.ToString());
            var (data, quotes) = SampleDataLoader.LoadEmbeddedSampleData(typeof(Walkabout.MainWindow).Assembly, tempDir);

            var money = new MyMoney();
            new SampleDataGenerator(money, quotes, randomSeed: seed).Create(data, inflation: 0.02, years: 10, employer: "ACME inc", paycheck: 2000m);
            return money;
        }

        [Test]
        public void Invariant_SplitsSumToTransactionAmount()
        {
            var money = this.GenerateSample(seed: 42);
            foreach (var t in money.Transactions)
            {
                if (t.IsSplit)
                {
                    decimal splitTotal = t.Splits.GetSplits().Sum(s => s.Amount);
                    Assert.That(splitTotal, Is.EqualTo(t.Amount).Within(0.01m),
                        $"Transaction {t.Id} splits sum to {splitTotal} but transaction amount is {t.Amount}");
                }
            }
        }

        [Test]
        public void Invariant_TransfersBalance()
        {
            var money = this.GenerateSample(seed: 42);
            foreach (var t in money.Transactions)
            {
                if (t.Transfer != null && !t.IsDeleted)
                {
                    var other = t.Transfer.Transaction;
                    Assert.That(other, Is.Not.Null, $"Transaction {t.Id} has a Transfer with no linked Transaction");
                    Assert.That(other.Amount, Is.EqualTo(-t.Amount).Within(0.01m),
                        $"Transfer pair {t.Id}/{other.Id} does not balance: {t.Amount} vs {other.Amount}");
                }
            }
        }

        [Test]
        public void Invariant_CategoryTotalsReconcileWithTransactions()
        {
            var money = this.GenerateSample(seed: 42);

            // "Expected" side: a declarative LINQ query. Each transaction is unrolled into
            // its chargeable units - itself if it's a plain transaction, or one unit per
            // split if it's split - then grouped by category. This is the only place
            // GroupBy is used, and it is the only place split amounts are attributed via
            // a projection rather than an explicit loop.
            var expectedByCategory = money.Transactions.GetAllTransactions()
                .Where(t => !t.IsDeleted)
                .SelectMany(t => t.IsSplit
                    ? t.Splits.GetSplits().Select(s => (Category: s.Category, Amount: s.Amount))
                    : new[] { (Category: t.Category, Amount: t.Amount) })
                .Where(x => x.Category != null)
                .GroupBy(x => x.Category)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            foreach (var category in money.Categories)
            {
                if (expectedByCategory.TryGetValue(category, out decimal expectedTotal))
                {
                    // "Actual" side: an independent imperative accumulation over the same
                    // raw transaction/split data. Deliberately not LINQ and deliberately not
                    // sharing any code with the query above, so a bug introduced into either
                    // implementation (e.g. mishandling of the Splits collection, an off-by-one,
                    // or a wrong category comparison) shows up as a mismatch between the two,
                    // rather than being invisible because both sides ran the same query.
                    decimal actualTotal = 0m;
                    foreach (var t in money.Transactions.GetAllTransactions())
                    {
                        if (t.IsDeleted)
                        {
                            continue;
                        }

                        if (t.IsSplit)
                        {
                            foreach (var s in t.Splits.GetSplits())
                            {
                                if (s.Category == category)
                                {
                                    actualTotal += s.Amount;
                                }
                            }
                        }
                        else if (t.Category == category)
                        {
                            actualTotal += t.Amount;
                        }
                    }

                    Assert.That(actualTotal, Is.EqualTo(expectedTotal).Within(0.01m));
                }
            }
        }

        [Test]
        public void Canary_Seed42NetWorth_IsStable()
        {
            var money = this.GenerateSample(seed: 42);
            decimal netWorth = money.Accounts.GetAccounts(filterOutClosed: true).Sum(a => a.Balance);
            Assert.That(netWorth, Is.EqualTo(289954.787838155m).Within(0.01m),
                "Net worth for seed 42 changed - a subsystem migration may have altered behavior, not just location");
        }
    }
}
