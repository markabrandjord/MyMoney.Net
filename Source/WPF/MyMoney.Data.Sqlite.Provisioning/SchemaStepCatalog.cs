using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// The ordered step list, loaded once from embedded resources named
    /// Walkabout.Data.Sqlite.Provisioning.Schema.NNNN_name.sql.
    ///
    /// This is the SQLite analogue of SQL Server's deployed Schema/*.sql procs (spec section 1.5,
    /// S-5 step 2): the DDL lives in a versioned, auditable artifact rather than in string-building
    /// C#. What is NOT identical across engines is where the executor's control flow physically
    /// lives, and no amount of design makes that identical, because SQLite has no server.
    /// </summary>
    public static class SchemaStepCatalog
    {
        private const string ResourcePrefix = "Walkabout.Data.Sqlite.Provisioning.Schema.";

        public static IReadOnlyList<SchemaStep> All { get; } = Load();

        public static int LatestVersion { get; } = All.Count == 0 ? 0 : All[All.Count - 1].Version;

        private static IReadOnlyList<SchemaStep> Load()
        {
            Assembly assembly = typeof(SchemaStepCatalog).Assembly;
            var steps = new List<SchemaStep>();

            foreach (string name in assembly.GetManifestResourceNames()
                         .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                     && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = name.Substring(ResourcePrefix.Length);
                int underscore = fileName.IndexOf('_');
                if (underscore <= 0
                    || !int.TryParse(fileName.Substring(0, underscore), NumberStyles.None,
                                     CultureInfo.InvariantCulture, out int version))
                {
                    throw new InvalidOperationException(
                        $"Schema resource '{fileName}' is not named NNNN_description.sql.");
                }

                string sql;
                using (Stream stream = assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                {
                    sql = reader.ReadToEnd();
                }

                steps.Add(new SchemaStep(
                    version,
                    Path.GetFileNameWithoutExtension(fileName),
                    sql,
                    SchemaStep.ComputeChecksum(sql)));
            }

            steps.Sort((a, b) => a.Version.CompareTo(b.Version));

            // A gap or a duplicate means two branches both claimed the same step number, which is
            // the merge accident this numbering exists to make loud rather than silent.
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Version != i + 1)
                {
                    throw new InvalidOperationException(
                        $"Schema steps must be contiguous from 1. Expected step {i + 1} but found "
                        + $"{steps[i].Version} ('{steps[i].Name}').");
                }
            }

            return steps;
        }
    }
}
