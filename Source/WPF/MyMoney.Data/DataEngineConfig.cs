using System;
using System.IO;
using Newtonsoft.Json;

namespace Walkabout.Data
{
    // DataEngineType moved to DatabaseRegistry.cs (issue #32) -- this class
    // itself is retired in a later task of that plan, but until then it
    // still needs the enum, now defined there instead of here.

    public class DataEngineConfig
    {
        public DataEngineType Engine { get; set; } = DataEngineType.Sqlite;
        public string Server { get; set; }
        public string Database { get; set; }

        public static DataEngineConfig Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new DataEngineConfig();
            }

            string json = File.ReadAllText(path);
            RawDataEngineConfig raw = JsonConvert.DeserializeObject<RawDataEngineConfig>(json);
            if (raw == null)
            {
                return new DataEngineConfig();
            }

            var config = new DataEngineConfig
            {
                Server = raw.Server,
                Database = raw.Database,
                Engine = string.Equals(raw.Engine, "SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? DataEngineType.SqlServer
                    : DataEngineType.Sqlite
            };

            return config;
        }

        private class RawDataEngineConfig
        {
            public string Engine { get; set; }
            public string Server { get; set; }
            public string Database { get; set; }
        }
    }
}
