using System;
using System.Linq;
using System.Windows.Controls;
using Walkabout.Data;

namespace Walkabout.Utilities
{
    public class RecentDatabaseEventArgs : EventArgs
    {
        public string DisplayName { get; set; }
    }

    /// <summary>
    /// A thin, pull-based view over DatabaseRegistry.Databases -- no
    /// separate storage of its own (replaces the old path-list/File.Exists
    /// pruning approach). Call Refresh(registry) after any registry
    /// mutation (a new database created, a database opened and its
    /// LastUsedUtc bumped) to keep the menu in sync.
    /// </summary>
    internal class RecentFilesMenu
    {
        private const int MaxRecentFiles = 10;
        private readonly MenuItem parent;

        public event EventHandler<RecentDatabaseEventArgs> RecentDatabaseSelected;

        public RecentFilesMenu(MenuItem parent)
        {
            this.parent = parent;
        }

        public void Refresh(DatabaseRegistry registry)
        {
            this.parent.Items.Clear();

            bool includeTest =
#if DEBUG
                true;
#else
                false;
#endif
            var entries = registry.Databases
                .Where(kv => includeTest || !kv.Value.TestDatabase)
                .OrderByDescending(kv => kv.Value.LastUsedUtc ?? DateTime.MinValue)
                .Take(MaxRecentFiles);

            foreach (var kv in entries)
            {
                string displayName = kv.Key;
                var item = new MenuItem { Header = displayName };
                item.Click += (s, e) => this.RecentDatabaseSelected?.Invoke(this, new RecentDatabaseEventArgs { DisplayName = displayName });
                this.parent.Items.Add(item);
            }
        }
    }
}
