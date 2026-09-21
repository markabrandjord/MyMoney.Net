using System.ComponentModel;
using MyMoney.Shell.Controls;
using NUnit.Framework;

namespace MyMoney.Tests.Shell.Controls;

[TestFixture]
public class GridViewSorterTests
{
    [Test]
    public void NextSort_NoCurrentSort_SortsTheClickedColumnAscending()
    {
        var (column, direction) = GridViewSorter.NextSort(null, null, "Name");

        Assert.That(column, Is.EqualTo("Name"));
        Assert.That(direction, Is.EqualTo(ListSortDirection.Ascending));
    }

    [Test]
    public void NextSort_ClickingTheSameColumnAgain_TogglesToDescending()
    {
        var (column, direction) = GridViewSorter.NextSort("Name", ListSortDirection.Ascending, "Name");

        Assert.That(column, Is.EqualTo("Name"));
        Assert.That(direction, Is.EqualTo(ListSortDirection.Descending));
    }

    [Test]
    public void NextSort_ClickingTheSameColumnAThirdTime_TogglesBackToAscending()
    {
        var (column, direction) = GridViewSorter.NextSort("Name", ListSortDirection.Descending, "Name");

        Assert.That(column, Is.EqualTo("Name"));
        Assert.That(direction, Is.EqualTo(ListSortDirection.Ascending));
    }

    [Test]
    public void NextSort_ClickingADifferentColumn_SwitchesToItAscendingRegardlessOfThePreviousDirection()
    {
        var (column, direction) = GridViewSorter.NextSort("Name", ListSortDirection.Descending, "Currency");

        Assert.That(column, Is.EqualTo("Currency"));
        Assert.That(direction, Is.EqualTo(ListSortDirection.Ascending));
    }
}
