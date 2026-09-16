using System;
using System.Collections.Generic;
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

        // RowVersion is [XmlIgnore] (persistence-concurrency Phase 1), so it never round-trips
        // through the DataContractSerializer snapshot below. This is MockDatabase's own
        // last-committed-version bookkeeping, restored onto each object by Load() (see
        // RestoreRowVersions) so a later SaveOne/SaveBatch call has something real to compare
        // against. Key is (root's concrete type, its Id) since Id alone isn't unique across
        // tables (an Account and a Category can share Id=1).
        private readonly Dictionary<(Type RootType, long Id), long> committedVersions = new Dictionary<(Type RootType, long Id), long>();

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

        public void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            this.SaveBatch(new PersistentObject[] { root });
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            this.SaveBatch(new PersistentObject[] { from, to });
        }

        public void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            List<PersistentObject> list = new List<PersistentObject>(roots);
            if (list.Count == 0)
            {
                return;
            }

            // Validate every root against its last-committed version before mutating anything,
            // so a conflict on root N of N leaves nothing committed (R3's atomicity requirement).
            foreach (PersistentObject root in list)
            {
                if (!(root is IAggregateRoot identity))
                {
                    throw new ArgumentException(string.Format(
                        "{0} is not a valid SaveBatch root - it must implement IAggregateRoot.", root.GetType().Name));
                }

                long committed = this.committedVersions.TryGetValue((root.GetType(), identity.Id), out long v) ? v : 0;
                if (committed != root.RowVersion)
                {
                    throw new ConcurrencyConflictException(root, committed, root.RowVersion);
                }
            }

            // MockDatabase has no per-row storage, so it re-serializes the whole owning graph on
            // every call - unlike SqliteDatabase/SqlServerStoredProcDatabase (Phase 2b/2c), which
            // only ever touch the given root(s)' own rows. This is a deliberate simplification
            // for a test double (see
            // docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R2's exemption
            // of MockDatabase from real transaction semantics): MockDatabase alone cannot catch a
            // caller relying on unrelated dirty state getting flushed along with it - that check
            // belongs to the real-engine contract tests landing in Phase 2b/2c, not here.
            MyMoney owner = list[0].Parent?.Parent as MyMoney;
            if (owner == null)
            {
                throw new InvalidOperationException("SaveBatch root is not attached to a loaded MyMoney graph.");
            }
            this.Save(owner);

            foreach (PersistentObject root in list)
            {
                IAggregateRoot identity = (IAggregateRoot)root;
                (Type, long) key = (root.GetType(), identity.Id);
                if (root.IsDeleted)
                {
                    this.committedVersions.Remove(key);
                }
                else
                {
                    long newVersion = root.RowVersion + 1;
                    this.committedVersions[key] = newVersion;
                    root.RowVersion = newVersion;
                    root.OnUpdated();
                }
            }
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
                this.RestoreRowVersions(money);
                return money;
            }
        }

        private void RestoreRowVersions(MyMoney money)
        {
            this.ApplyVersions(money.Accounts);
            this.ApplyVersions(money.Categories);
            this.ApplyVersions(money.Payees);
            this.ApplyVersions(money.Currencies);
            this.ApplyVersions(money.Securities);
            this.ApplyVersions(money.Aliases);
            this.ApplyVersions(money.OnlineAccounts);
            this.ApplyVersions(money.StockSplits);
            this.ApplyVersions(money.Buildings);
            this.ApplyVersions(money.LoanPayments);
            this.ApplyVersions(money.Transactions);
        }

        private void ApplyVersions(IEnumerable<PersistentObject> roots)
        {
            foreach (PersistentObject root in roots)
            {
                if (root is IAggregateRoot identity &&
                    this.committedVersions.TryGetValue((root.GetType(), identity.Id), out long version))
                {
                    root.RowVersion = version;
                }
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
            this.committedVersions.Clear();
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
