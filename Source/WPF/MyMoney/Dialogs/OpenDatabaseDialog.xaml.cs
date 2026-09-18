using System;
using System.Linq;
using System.Windows;
using Walkabout.Data;

namespace Walkabout.Dialogs
{
    public partial class OpenDatabaseDialog : Window
    {
        public string SelectedDisplayName { get; private set; }

        public OpenDatabaseDialog(DatabaseRegistry registry)
        {
            this.InitializeComponent();

            bool includeTest =
#if DEBUG
                true;
#else
                false;
#endif
            var entries = registry.Databases
                .Where(kv => includeTest || !kv.Value.TestDatabase)
                .OrderByDescending(kv => kv.Value.LastUsedUtc ?? DateTime.MinValue)
                .ToList();

            foreach (var kv in entries)
            {
                this.ListBoxDatabases.Items.Add(kv);
            }
        }

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            if (this.ListBoxDatabases.SelectedItem is System.Collections.Generic.KeyValuePair<string, DatabaseEntry> kv)
            {
                this.SelectedDisplayName = kv.Key;
                this.DialogResult = true;
            }
        }
    }
}
