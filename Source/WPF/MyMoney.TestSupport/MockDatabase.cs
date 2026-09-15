using System.Data;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// A fast, in-memory IDatabase implementation for tests. Performs a real
    /// DataContractSerializer round-trip against a MemoryStream on Save/Load
    /// (reusing XmlStore.PrepareSave for dirty-tracking bookkeeping) rather
    /// than passing the same MyMoney reference through, so tests exercise a
    /// genuine round-trip instead of passing on object-identity coincidence.
    /// See docs/superpowers/specs/2026-09-15-test-support-library-design.md.
    /// </summary>
    public class MockDatabase : IDatabase
    {
        private byte[] snapshot;

        public string Server => "mock";
        public string DatabasePath => "mock://in-memory";
        public string UserId => null;
        public string Password { get; set; }
        public string BackupPath => null;
        public bool SupportsUserLogin => false;
        public DbFlavor DbFlavor => DbFlavor.Mock;
        public bool Exists => this.snapshot != null;
        public bool UpgradeRequired => false;

        public void Create()
        {
            // Real engines (e.g. SqliteDatabase) create their on-disk store as
            // part of Create(), so Exists is true immediately afterward, even
            // before any explicit Save(). Match that here by establishing an
            // initial saved, empty snapshot.
            this.Save(new MyMoney());
        }

        public void Save(MyMoney money)
        {
            this.snapshot = Serialize(money);
        }

        private static byte[] Serialize(MyMoney money)
        {
            XmlStore.PrepareSave(money);
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
            using (MemoryStream stream = new MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream))
                {
                    serializer.WriteObject(writer, money);
                }
                return stream.ToArray();
            }
        }

        public MyMoney Load(IStatusService status)
        {
            if (this.snapshot == null)
            {
                return new MyMoney();
            }
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
            using (MemoryStream stream = new MemoryStream(this.snapshot))
            using (XmlReader reader = XmlReader.Create(stream))
            {
                MyMoney money = (MyMoney)serializer.ReadObject(reader);
                money.PostDeserializeFixup();
                money.OnLoaded();
                return money;
            }
        }

        public void Backup(string path)
        {
            if (this.snapshot != null)
            {
                File.WriteAllBytes(path, this.snapshot);
            }
        }

        public void Upgrade()
        {
        }

        public void Delete()
        {
            this.snapshot = null;
        }

        public string GetLog()
        {
            return string.Empty;
        }

        public DataSet QueryDataSet(string cmd)
        {
            return null;
        }

        public void Disconnect()
        {
        }
    }
}
