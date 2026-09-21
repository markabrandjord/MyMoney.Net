using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// The FK-safe order for emptying or dropping tables, DERIVED BY INTROSPECTION and never
    /// hand-written.
    ///
    /// This is the single most important implementation constraint in spec section 2.6.4, and the
    /// reason is issue #34's lesson applied where it would otherwise recur verbatim: a
    /// hand-maintained list has exactly #34's failure shape - someone adds a table in step 23,
    /// forgets the list, and every test from then on runs against a table that is never emptied,
    /// silently, with the symptom appearing somewhere else entirely. Deriving the list means a new
    /// table is handled the moment its step exists, by the same route that a new index is created
    /// the moment its step exists.
    /// </summary>
    public static class SqliteTableOrder
    {
        public const string HistoryTable = "__SchemaHistory";

        /// <summary>Data tables: every table except SQLite internals and the schema ledger.</summary>
        public static IReadOnlyList<string> DataTables(SQLiteConnection connection)
        {
            var tables = new List<string>();
            using (var cmd = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' "
                + "AND name <> @history ORDER BY name;", connection))
            {
                cmd.Parameters.AddWithValue("@history", HistoryTable);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }
            }

            return tables;
        }

        /// <summary>
        /// Reverse topological order over PRAGMA foreign_key_list: a table that REFERENCES another
        /// comes first, so its rows are gone before the referenced table is touched. A cycle (a
        /// self-referencing table, or a genuine loop) cannot be ordered, and the caller's
        /// PRAGMA defer_foreign_keys = ON inside the transaction is the safety net - the same
        /// pragma SqliteDatabase.SaveBatch already uses.
        /// </summary>
        public static IReadOnlyList<string> ForDelete(SQLiteConnection connection)
        {
            IReadOnlyList<string> tables = DataTables(connection);
            var dependsOn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string table in tables)
            {
                dependsOn[table] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (string table in tables)
            {
                using (var cmd = new SQLiteCommand("SELECT \"table\" FROM pragma_foreign_key_list(@t);", connection))
                {
                    cmd.Parameters.AddWithValue("@t", table);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string referenced = reader.GetString(0);
                            if (dependsOn.ContainsKey(referenced)
                                && !string.Equals(referenced, table, StringComparison.OrdinalIgnoreCase))
                            {
                                dependsOn[table].Add(referenced);
                            }
                        }
                    }
                }
            }

            // Depth-first: emit a table only after everything that references it, i.e. visit
            // referencing tables first. Equivalent to "children before parents".
            var ordered = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(string table)
            {
                if (!visited.Add(table))
                {
                    return;
                }

                foreach (string other in tables)
                {
                    if (dependsOn[other].Contains(table))
                    {
                        Visit(other);
                    }
                }

                if (!ordered.Contains(table, StringComparer.OrdinalIgnoreCase))
                {
                    ordered.Add(table);
                }
            }

            foreach (string table in tables)
            {
                Visit(table);
            }

            return ordered;
        }
    }
}
