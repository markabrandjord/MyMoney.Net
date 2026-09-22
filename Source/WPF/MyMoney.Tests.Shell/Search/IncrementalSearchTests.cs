using System.Collections.Generic;
using MyMoney.Shell.Search;
using NUnit.Framework;

namespace MyMoney.Tests.Shell.Search;

[TestFixture]
public class IncrementalSearchTests
{
    private static readonly IReadOnlyList<string> Items = new[] { "Checking", "Savings", "Visa", "Vacation Fund" };

    [Test]
    public void FindFirst_NoMatch_ReturnsNegativeOne()
    {
        Assert.That(IncrementalSearch.FindFirst(Items, "Brokerage"), Is.EqualTo(-1));
    }

    [Test]
    public void FindFirst_EmptyQuery_ReturnsNegativeOne()
    {
        Assert.That(IncrementalSearch.FindFirst(Items, ""), Is.EqualTo(-1));
    }

    [Test]
    public void FindFirst_SingleMatch_ReturnsItsIndex()
    {
        Assert.That(IncrementalSearch.FindFirst(Items, "Visa"), Is.EqualTo(2));
    }

    [Test]
    public void FindFirst_IsCaseInsensitive()
    {
        Assert.That(IncrementalSearch.FindFirst(Items, "visa"), Is.EqualTo(2));
    }

    [Test]
    public void FindFirst_MatchesAsSubstring()
    {
        Assert.That(IncrementalSearch.FindFirst(Items, "acation"), Is.EqualTo(3));
    }

    [Test]
    public void FindFirst_MultipleMatches_ReturnsTheEarliestIndex()
    {
        // "Checking" and "Savings" both contain no shared substring, but "Vacation Fund" and
        // "Savings" both contain "a" - use a query that matches two items to prove FindFirst
        // picks the first one, not just any one.
        Assert.That(IncrementalSearch.FindFirst(Items, "a"), Is.EqualTo(1), "Savings is the first item containing 'a'.");
    }

    [Test]
    public void FindNext_FromNoSelection_BehavesLikeFindFirst()
    {
        Assert.That(IncrementalSearch.FindNext(Items, currentIndex: -1, "a"), Is.EqualTo(1));
    }

    [Test]
    public void FindNext_AdvancesToTheNextMatchAfterCurrent()
    {
        // "a" matches Savings(1), Visa(2) and Vacation Fund(3) - Checking(0) does not.
        Assert.That(IncrementalSearch.FindNext(Items, currentIndex: 1, "a"), Is.EqualTo(2),
            "After Savings (index 1), the next item containing 'a' is Visa (index 2).");
    }

    [Test]
    public void FindNext_WrapsAroundToTheFirstMatchPastTheEnd()
    {
        Assert.That(IncrementalSearch.FindNext(Items, currentIndex: 3, "a"), Is.EqualTo(1),
            "Past the last match (Vacation Fund, index 3), it wraps back to Savings (index 1).");
    }

    [Test]
    public void FindNext_OnlyOneMatch_KeepsReturningIt()
    {
        Assert.That(IncrementalSearch.FindNext(Items, currentIndex: 2, "Visa"), Is.EqualTo(2));
    }

    [Test]
    public void FindNext_NoMatch_ReturnsNegativeOne()
    {
        Assert.That(IncrementalSearch.FindNext(Items, currentIndex: 0, "Brokerage"), Is.EqualTo(-1));
    }

    [Test]
    public void FindPrevious_FromNoSelection_WrapsToTheLastMatch()
    {
        Assert.That(IncrementalSearch.FindPrevious(Items, currentIndex: -1, "a"), Is.EqualTo(3));
    }

    [Test]
    public void FindPrevious_RetreatsToThePreviousMatchBeforeCurrent()
    {
        // "a" matches Savings(1), Visa(2) and Vacation Fund(3).
        Assert.That(IncrementalSearch.FindPrevious(Items, currentIndex: 3, "a"), Is.EqualTo(2),
            "Before Vacation Fund (index 3), the previous item containing 'a' is Visa (index 2).");
    }

    [Test]
    public void FindPrevious_WrapsAroundToTheLastMatchBeforeTheStart()
    {
        Assert.That(IncrementalSearch.FindPrevious(Items, currentIndex: 1, "a"), Is.EqualTo(3),
            "Before the first match (Savings, index 1), it wraps around to Vacation Fund (index 3).");
    }

    [Test]
    public void FindPrevious_NoMatch_ReturnsNegativeOne()
    {
        Assert.That(IncrementalSearch.FindPrevious(Items, currentIndex: 0, "Brokerage"), Is.EqualTo(-1));
    }
}
