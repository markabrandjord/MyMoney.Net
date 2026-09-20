using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class BasicsFixtureBuilderTests
    {
        private static Category FindCategoryByName(MyMoney money, string name)
        {
            foreach (Category c in money.Categories)
            {
                if (c.Name == name)
                {
                    return c;
                }
            }
            return null;
        }

        private static int CountItems<T>(System.Collections.Generic.IEnumerable<T> items)
        {
            int count = 0;
            foreach (T item in items)
            {
                count++;
            }
            return count;
        }

        [Test]
        public void Build_ReturnsExpectedCategoryShape()
        {
            MyMoney money = BasicsFixtureBuilder.Build();

            // Category.Name stores the full colon-separated path (e.g. "Fun:Movies"), not just
            // the leaf segment - Category.Label derives the leaf from Name's last ':' segment.
            // Confirmed by reading Money.cs during Task 1's implementation.
            Category fun = FindCategoryByName(money, "Fun");
            Assert.That(fun, Is.Not.Null, "Expected a top-level 'Fun' category.");

            Category movies = FindCategoryByName(money, "Fun:Movies");
            Assert.That(movies, Is.Not.Null, "Expected 'Fun:Movies' child category.");
            Assert.That(movies.Label, Is.EqualTo("Movies"));
            Assert.That(movies.ParentCategory, Is.SameAs(fun));

            Category videos = FindCategoryByName(money, "Fun:Videos");
            Assert.That(videos, Is.Not.Null, "Expected 'Fun:Videos' child category.");
            Assert.That(videos.ParentCategory, Is.SameAs(fun));

            int moviesTransactionCount = money.Transactions.GetTransactionsByCategory(movies, null).Count;
            Assert.That(moviesTransactionCount, Is.EqualTo(1), "Expected exactly 1 seeded transaction under Fun:Movies.");
        }

        [Test]
        public void Build_ReturnsExpectedPayeeAndAliasShape()
        {
            MyMoney money = BasicsFixtureBuilder.Build();

            Assert.That(CountItems<Payee>(money.Payees), Is.EqualTo(4), "Expected exactly 4 seeded payees (movie, Alaska, duplicate-target, grocery).");
            Assert.That(CountItems<Alias>(money.Aliases), Is.EqualTo(3), "Expected exactly 3 seeded aliases (1 plain + 2 narrow, for the regex-consolidation scenario).");
        }

        [Test]
        public void Build_IsFreshEveryCall()
        {
            MyMoney first = BasicsFixtureBuilder.Build();
            MyMoney second = BasicsFixtureBuilder.Build();

            Assert.That(first, Is.Not.SameAs(second), "Each call must return an independent MyMoney graph.");

            Category firstFun = FindCategoryByName(first, "Fun");
            firstFun.Name = "Mutated";

            Category funInSecond = FindCategoryByName(second, "Fun");
            Assert.That(funInSecond, Is.Not.Null, "Mutating one Build() result must not affect another - each call is a fresh graph.");
        }
    }
}
