namespace MyMoney.Shell.Search;

/// <summary>Which selection move a search interaction requests - see <see cref="ISearchableView"/>.</summary>
public enum SearchStep
{
    /// <summary>Re-apply the filter for a new/changed query and select its first visible row.</summary>
    First,
    Next,
    Previous,
}

/// <summary>
/// D-9's "surfaces opt in via a predicate" capability: a content view that supports the shell's
/// search box implements this. The shell (MainWindow) never knows what a view's rows look like -
/// it only calls <see cref="ApplySearch"/> with the current query box text and what the user
/// asked for (first, next, previous), and disables the search box entirely (D-9's "nothing to
/// search here") when the currently hosted view does not implement this interface at all.
/// </summary>
public interface ISearchableView
{
    /// <summary>
    /// Filters the view's grid down to rows matching <paramref name="query"/> (a null/empty
    /// query clears the filter back to the full list) and moves the selection per
    /// <paramref name="step"/>. Returns true if a row is selected afterwards, false if the query
    /// is non-empty but matches nothing.
    /// </summary>
    bool ApplySearch(string query, SearchStep step);
}
