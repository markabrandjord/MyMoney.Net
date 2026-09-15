using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class DataEngineCredentialStoreTests
    {
        private string tempPath;

        [SetUp]
        public void Setup()
        {
            this.tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "dataengine.credentials.json");
        }

        [TearDown]
        public void TearDown()
        {
            string dir = Path.GetDirectoryName(this.tempPath);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Load_MissingFile_ReturnsEmptyDictionary()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            var result = store.Load();
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SaveThenLoad_RoundTripsCredentials()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            var credentials = new Dictionary<string, DataEngineCredential>
            {
                ["MyMoneyUser"] = new DataEngineCredential { UserId = "MyMoneyUser", Password = "Sw0rdfish!23" }
            };

            store.Save(credentials);
            var loaded = store.Load();

            Assert.That(loaded.ContainsKey("MyMoneyUser"), Is.True);
            Assert.That(loaded["MyMoneyUser"].Password, Is.EqualTo("Sw0rdfish!23"));
        }

        [Test]
        public void Save_CreatesParentDirectoryIfMissing()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            store.Save(new Dictionary<string, DataEngineCredential>());
            Assert.That(File.Exists(this.tempPath), Is.True);
        }

        [Test]
        public void GetCredential_UnknownAccount_ThrowsWithGuidance()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            store.Save(new Dictionary<string, DataEngineCredential>());
            var ex = Assert.Throws<InvalidOperationException>(() => store.GetCredential("MyMoneyTest"));
            Assert.That(ex.Message, Does.Contain("MyMoneyTest"));
        }

        [Test]
        public void GetDefaultPath_EndsWithExpectedRelativeLayout()
        {
            string path = DataEngineCredentialStore.GetDefaultPath();
            Assert.That(path, Does.EndWith(Path.Combine(".secrets", "MyMoney", "dataengine.credentials.json")));
        }
    }
}
