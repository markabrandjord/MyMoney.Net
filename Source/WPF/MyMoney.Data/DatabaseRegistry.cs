using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    // Moved here from the now-deleted DataEngineConfig.cs -- this is its
    // only home from this task onward.
    public enum DataEngineType
    {
        Sqlite,
        SqlServer
    }

    public enum DatabaseRole
    {
        Admin,
        User,
        Test
    }

    public class DatabaseCredential
    {
        public string UserId { get; set; }
        public string Password { get; set; }
    }

    public class DatabaseServerEntry
    {
        public string Comment { get; set; }
        public DatabaseCredential MyMoneyAdmin { get; set; }
        public DatabaseCredential MyMoneyUser { get; set; }
        public DatabaseCredential MyMoneyTest { get; set; }

        public DatabaseCredential GetCredential(DatabaseRole role)
        {
            switch (role)
            {
                case DatabaseRole.Admin: return this.MyMoneyAdmin;
                case DatabaseRole.User: return this.MyMoneyUser;
                case DatabaseRole.Test: return this.MyMoneyTest;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }
    }

    public class DatabaseEntry
    {
        public DataEngineType Engine { get; set; }
        public string Server { get; set; }
        public string Catalog { get; set; }
        public string Path { get; set; }

        // Explicit override: everything else in this schema serializes
        // camelCase (see DatabaseRegistry.JsonSettings below), but the user
        // specifically asked for this one field to read "TestDatabase" in
        // the hand-edited config file, not "testDatabase".
        [JsonProperty("TestDatabase")]
        public bool TestDatabase { get; set; }

        public DateTime? LastUsedUtc { get; set; }
        public string Comment { get; set; }
    }

    public class DatabaseRegistry
    {
        private readonly string path;

        public Dictionary<string, DatabaseServerEntry> Servers { get; private set; } = new Dictionary<string, DatabaseServerEntry>();
        public Dictionary<string, DatabaseEntry> Databases { get; private set; } = new Dictionary<string, DatabaseEntry>();

        // CamelCasePropertyNamesContractResolver's default NamingStrategy
        // camelCases dictionary KEYS too (ProcessDictionaryKeys=true) and
        // overrides explicit [JsonProperty] names (OverrideSpecifiedNames=
        // true) -- both wrong here: Servers/Databases dictionary keys are
        // meaningful data (server/display names), not structural property
        // names, and TestDatabase's explicit casing must survive. Configure
        // the naming strategy directly instead of using that resolver.
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessDictionaryKeys = false,
                    OverrideSpecifiedNames = false
                }
            },
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public DatabaseRegistry(string path)
        {
            this.path = path;
        }

        public static string GetDefaultPath()
        {
            return Path.Combine(ProcessHelper.AppDataPath, "dataengine.config.json");
        }

        public static DatabaseRegistry Load(string path)
        {
            var registry = new DatabaseRegistry(path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return registry;
            }

            string json = File.ReadAllText(path);
            RawRegistry raw = JsonConvert.DeserializeObject<RawRegistry>(json, JsonSettings);
            if (raw == null)
            {
                return registry;
            }

            registry.Servers = raw.Servers ?? new Dictionary<string, DatabaseServerEntry>();
            registry.Databases = raw.Databases ?? new Dictionary<string, DatabaseEntry>();
            return registry;
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(this.path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var raw = new RawRegistry { Servers = this.Servers, Databases = this.Databases };
            string json = JsonConvert.SerializeObject(raw, JsonSettings);
            File.WriteAllText(this.path, json);
        }

        public string BuildConnectionString(DatabaseEntry entry, DatabaseRole role)
        {
            if (entry.Engine != DataEngineType.SqlServer)
            {
                throw new InvalidOperationException("BuildConnectionString only applies to SqlServer entries.");
            }

            if (!this.Servers.TryGetValue(entry.Server, out DatabaseServerEntry server))
            {
                throw new InvalidOperationException($"No servers[\"{entry.Server}\"] entry in the registry at {this.path}.");
            }

            DatabaseCredential credential = server.GetCredential(role);
            if (credential == null)
            {
                throw new InvalidOperationException($"servers[\"{entry.Server}\"] has no {role} credential in the registry at {this.path}.");
            }

            return SqlServerDatabase.GetConnectionString(entry.Server, entry.Catalog, credential.UserId, credential.Password);
        }

        private class RawRegistry
        {
            public Dictionary<string, DatabaseServerEntry> Servers { get; set; }
            public Dictionary<string, DatabaseEntry> Databases { get; set; }
        }
    }
}
