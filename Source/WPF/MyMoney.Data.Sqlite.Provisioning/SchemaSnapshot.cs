using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using Walkabout.Data;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// The whole schema reduced to an order-independent set of canonical one-line facts, captured
    /// by structured introspection (PRAGMA table_list/table_info/index_list/index_info/
    /// foreign_key_list plus sqlite_master for views and triggers) rather than by scraping
    /// sqlite_master.sql text the way TableExists does today - spec section 1.7.1.
    ///
    /// Views and triggers are included deliberately: a paved database missing a view must report
    /// drift, or the first symptom is an IMoneyQuery call failing at runtime (spec section 2.7.1).
    /// </summary>
    public sealed class SchemaSnapshot
    {
        private SchemaSnapshot(IReadOnlyList<string> facts)
        {
            this.Facts = facts;
        }

        public IReadOnlyList<string> Facts { get; }

        public static SchemaSnapshot Capture(SQLiteConnection connection)
        {
            var facts = new List<string>();

            foreach ((string table, int strict) in Tables(connection))
            {
                facts.Add($"table:{table}|strict={strict}");

                foreach (string column in Rows(connection,
                    "SELECT name || '|' || type || '|notnull=' || \"notnull\" || '|pk=' || pk || "
                    + "'|default=' || COALESCE(dflt_value, '<none>') FROM pragma_table_info(@t) ORDER BY name;",
                    table))
                {
                    facts.Add($"column:{table}.{column}");
                }

                foreach (string index in Rows(connection,
                    "SELECT il.name || '|unique=' || il.\"unique\" || '|origin=' || il.origin || '|cols=' || "
                    + "(SELECT group_concat(ii.name, ',') FROM pragma_index_info(il.name) ii) "
                    + "FROM pragma_index_list(@t) il ORDER BY il.name;",
                    table))
                {
                    facts.Add($"index:{table}.{index}");
                }

                foreach (string fk in Rows(connection,
                    "SELECT \"from\" || '->' || \"table\" || '.' || COALESCE(\"to\", 'rowid') "
                    + "FROM pragma_foreign_key_list(@t) ORDER BY \"from\";",
                    table))
                {
                    facts.Add($"fk:{table}.{fk}");
                }
            }

            foreach (string obj in Rows(connection,
                "SELECT type || ':' || name || '|' || COALESCE(sql, '') FROM sqlite_master "
                + "WHERE type IN ('view', 'trigger') ORDER BY type, name;", null))
            {
                facts.Add(Normalise(obj));
            }

            facts.Sort(StringComparer.Ordinal);
            return new SchemaSnapshot(facts);
        }

        public static IReadOnlyList<SchemaDifference> Diff(SchemaSnapshot expected, SchemaSnapshot actual)
        {
            var differences = new List<SchemaDifference>();

            foreach (string missing in expected.Facts.Except(actual.Facts, StringComparer.Ordinal))
            {
                differences.Add(new SchemaDifference(Kind(missing), Subject(missing), missing, "<absent>"));
            }

            foreach (string extra in actual.Facts.Except(expected.Facts, StringComparer.Ordinal))
            {
                differences.Add(new SchemaDifference(Kind(extra), Subject(extra), "<absent>", extra));
            }

            return differences;
        }

        private static IEnumerable<(string Name, int Strict)> Tables(SQLiteConnection connection)
        {
            var result = new List<(string, int)>();
            using (var cmd = new SQLiteCommand(
                "SELECT name, strict FROM pragma_table_list WHERE type = 'table' "
                + "AND name NOT LIKE 'sqlite_%' ORDER BY name;", connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add((reader.GetString(0), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture)));
                }
            }

            return result;
        }

        private static IEnumerable<string> Rows(SQLiteConnection connection, string sql, string table)
        {
            var result = new List<string>();
            using (var cmd = new SQLiteCommand(sql, connection))
            {
                if (table != null)
                {
                    cmd.Parameters.AddWithValue("@t", table);
                }

                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            result.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Collapse whitespace so a reformatted CREATE VIEW is not reported as drift.</summary>
        private static string Normalise(string text) =>
            string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        private static string Kind(string fact) => fact.Substring(0, fact.IndexOf(':'));

        private static string Subject(string fact)
        {
            string rest = fact.Substring(fact.IndexOf(':') + 1);
            int bar = rest.IndexOf('|');
            return bar < 0 ? rest : rest.Substring(0, bar);
        }
    }
}
