using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MyMoney.Shell.Controls;

/// <summary>
/// Attaches click-to-sort behavior to any ListView/GridView: click a column header to sort by
/// it ascending; click the same header again to toggle descending, then back to ascending;
/// click a different header to switch columns, always starting ascending. Reusable across
/// every "simple table" screen in the Shell (no per-column configuration needed - it reads
/// each column's sort key straight off its own DisplayMemberBinding). Transactions will need
/// its own design once that screen exists - Splits/Investment/Transfer don't map to a single
/// flat column set the way Account's fields do.
/// </summary>
public static class GridViewSorter
{
    /// <summary>
    /// The pure toggle rule, kept separate from the WPF wiring below so it's unit-testable
    /// without a live ListView: same column clicked again flips direction; any other column
    /// (including the very first click, where currentColumn is null) resets to ascending.
    /// </summary>
    public static (string Column, ListSortDirection Direction) NextSort(
        string? currentColumn, ListSortDirection? currentDirection, string clickedColumn)
    {
        if (currentColumn == clickedColumn && currentDirection == ListSortDirection.Ascending)
        {
            return (clickedColumn, ListSortDirection.Descending);
        }

        return (clickedColumn, ListSortDirection.Ascending);
    }

    /// <summary>
    /// Wires the toggle rule above into a real ListView: applies <paramref name="defaultColumn"/>
    /// ascending once the view has loaded (so ItemsSource is guaranteed bound), then re-sorts on
    /// every GridViewColumnHeader click. The sort key for a clicked header is read from its
    /// column's own DisplayMemberBinding.Path - the same binding path that already renders the
    /// column's cells - so a new column needs zero extra wiring here, just a normal
    /// DisplayMemberBinding.
    /// </summary>
    public static void Attach(ListView listView, string defaultColumn, ListSortDirection defaultDirection = ListSortDirection.Ascending)
    {
        string? sortedColumn = null;
        ListSortDirection? sortedDirection = null;

        void ApplySort(string column, ListSortDirection direction)
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(column, direction));
            sortedColumn = column;
            sortedDirection = direction;
        }

        listView.Loaded += (_, _) => ApplySort(defaultColumn, defaultDirection);

        listView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is GridViewColumnHeader { Column.DisplayMemberBinding: Binding binding })
            {
                (string column, ListSortDirection direction) = NextSort(sortedColumn, sortedDirection, binding.Path.Path);
                ApplySort(column, direction);
            }
        }));
    }
}
