using System;
using System.Collections.Generic;

namespace MyMoney.Shell.Search;

/// <summary>
/// The pure matching/navigation rule behind the shell's filter-and-select search (D-9): a
/// case-insensitive substring match (<see cref="Matches"/>), used both as the predicate a
/// searchable view filters its grid with, and - applied again over the resulting (already
/// all-matching) filtered rows - to find the first/next/previous selection, wrapping around at
/// the ends. Kept free of any WPF/ListView dependency so it's unit-testable directly - the WPF
/// wiring (applying the filter, reading the visible display order, setting
/// SelectedItem/ScrollIntoView) lives in each searchable view instead, the same split
/// GridViewSorter uses for its own toggle rule.
/// </summary>
public static class IncrementalSearch
{
    public static bool Matches(string item, string query) =>
        item != null && !string.IsNullOrEmpty(query) && item.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    public static int FindFirst(IReadOnlyList<string> items, string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return -1;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (Matches(items[i], query))
            {
                return i;
            }
        }

        return -1;
    }

    public static int FindNext(IReadOnlyList<string> items, int currentIndex, string query) =>
        FindInDirection(items, currentIndex, query, step: 1);

    public static int FindPrevious(IReadOnlyList<string> items, int currentIndex, string query) =>
        FindInDirection(items, currentIndex, query, step: -1);

    private static int FindInDirection(IReadOnlyList<string> items, int currentIndex, string query, int step)
    {
        if (string.IsNullOrEmpty(query) || items.Count == 0)
        {
            return -1;
        }

        // "No selection" (-1) must anchor on the correct side per direction: stepping forward
        // from -1 lands on 0 (matches FindFirst); stepping backward from "no selection" should
        // land on Count-1 (the last item), not literally step -1 from index -1, which would
        // wrap to Count-2 and skip a matching last item entirely.
        int anchor = currentIndex >= 0 ? currentIndex : (step > 0 ? -1 : items.Count);

        for (int i = 1; i <= items.Count; i++)
        {
            int index = Mod(anchor + (i * step), items.Count);
            if (Matches(items[index], query))
            {
                return index;
            }
        }

        return -1;
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
