using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Walkabout.Data
{
    public class DataEngineCredential
    {
        public string UserId { get; set; }
        public string Password { get; set; }
    }

    public class DataEngineCredentialStore
    {
        private readonly string path;

        public DataEngineCredentialStore(string path)
        {
            this.path = path;
        }

        public static string GetDefaultPath()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".secrets", "MyMoney", "dataengine.credentials.json");
        }

        public Dictionary<string, DataEngineCredential> Load()
        {
            if (!File.Exists(this.path))
            {
                return new Dictionary<string, DataEngineCredential>();
            }

            string json = File.ReadAllText(this.path);
            var result = JsonConvert.DeserializeObject<Dictionary<string, DataEngineCredential>>(json);
            return result ?? new Dictionary<string, DataEngineCredential>();
        }

        public void Save(Dictionary<string, DataEngineCredential> credentials)
        {
            string dir = Path.GetDirectoryName(this.path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonConvert.SerializeObject(credentials, Formatting.Indented);
            File.WriteAllText(this.path, json);
        }

        public DataEngineCredential GetCredential(string accountName)
        {
            var all = this.Load();
            if (all.TryGetValue(accountName, out DataEngineCredential credential))
            {
                return credential;
            }

            throw new InvalidOperationException(
                $"No credential found for account '{accountName}' in {this.path}. Run MyMoneyAdmin to bootstrap the SQL Server database first.");
        }
    }
}
