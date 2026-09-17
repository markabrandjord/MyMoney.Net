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
            // This loop also guards two shapes a real SQL engine would naturally reject but an
            // in-memory whole-graph re-serialization wouldn't otherwise notice: roots drawn from
            // two different loaded MyMoney graphs (only the first root's graph actually gets
            // serialized below), and the same logical row appearing twice in one call (which
            // would otherwise double-bump its version for a single commit).
            MyMoney sharedOwner = null;
            HashSet<(Type RootType, long Id)> seen = new HashSet<(Type, long)>();
            foreach (PersistentObject root in list)
            {
                if (root == null)
                {
                    throw new ArgumentNullException(nameof(roots), "SaveBatch roots must not contain a null entry.");
                }

                if (!(root is IAggregateRoot identity))
                {
                    throw new ArgumentException(string.Format(
                        "{0} is not a valid SaveBatch root - it must implement IAggregateRoot.", root.GetType().Name));
                }

                (Type, long) key = (root.GetType(), identity.Id);
                if (!seen.Add(key))
                {
                    throw new InvalidOperationException(string.Format(
                        "Duplicate root in one SaveBatch call: {0} (Id={1}) appears more than once.",
                        root.GetType().Name, identity.Id));
                }

                MyMoney rootOwner = root.Parent?.Parent as MyMoney;
                if (sharedOwner == null)
                {
                    sharedOwner = rootOwner;
                }
                else if (rootOwner != sharedOwner)
                {
                    throw new InvalidOperationException("SaveBatch roots belong to different MyMoney graphs.");
                }

                long committed = this.committedVersions.TryGetValue(key, out long v) ? v : 0;
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
            if (sharedOwner == null)
            {
                throw new InvalidOperationException("SaveBatch root is not attached to a loaded MyMoney graph.");
            }

            // MarkOwnedChildrenClean's deleted-child removals must happen BEFORE the whole-graph
            // Save() below, not after: a deleted owned child (Split/RentUnit) has to actually be
            // gone from the graph by the time it's serialized, or the very next Load() will bring
            // it right back (still flagged IsDeleted, since IsDeleted isn't part of the contract
            // either - it would just look "undeleted" on reload).
            foreach (PersistentObject root in list)
            {
                MarkOwnedChildrenClean(root);
            }

            this.Save(sharedOwner);

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

        /// <summary>
        /// Real engines (SqliteDatabase) explicitly write and clean every owned child
        /// (Transaction.Splits/Investment, RentBuilding's sibling RentUnits) as part of committing
        /// their aggregate root - see the design spec's R2 ("a Transaction's splits are part of its
        /// aggregate, not peers"). MockDatabase persists them for free via the whole-graph
        /// re-serialize above, but without this, they'd stay marked IsInserted/IsChanged forever in
        /// memory even though they're genuinely saved, and a deleted child would never actually be
        /// removed from its container. Mirrors SqliteDatabase's SaveTransactionSplitsAndInvestment/
        /// SaveRentUnitsForBuilding post-commit side effects, minus the SQL.
        /// </summary>
        private static void MarkOwnedChildrenClean(PersistentObject root)
        {
            if (root is Transaction t)
            {
                if (t.Splits != null)
                {
                    // Not `new List<Split>(t.Splits)`: the List<T>(IEnumerable<T>) constructor
                    // special-cases ICollection<T> sources and calls CopyTo, which Splits (like
                    // several sibling containers) leaves as an unimplemented stub. A plain foreach
                    // copy avoids that path entirely.
                    List<Split> splitsSnapshot = new List<Split>();
                    foreach (Split s in t.Splits)
                    {
                        splitsSnapshot.Add(s);
                    }

                    foreach (Split s in splitsSnapshot)
                    {
                        if (s.IsDeleted)
                        {
                            s.Parent.RemoveChild(s, true);
                        }
                        else
                        {
                            s.OnUpdated();
                        }
                    }
                }

                if (t.Investment != null && !t.Investment.IsDeleted)
                {
                    t.Investment.OnUpdated();
                }
            }
            else if (root is RentBuilding r && r.Parent is RentBuildings buildings && buildings.Units != null)
            {
                List<RentUnit> unitsSnapshot = new List<RentUnit>();
                foreach (RentUnit u in buildings.Units)
                {
                    unitsSnapshot.Add(u);
                }

                foreach (RentUnit u in unitsSnapshot)
                {
                    if (u.Building != r.Id)
                    {
                        continue;
                    }

                    if (u.IsDeleted)
                    {
                        u.Parent.RemoveChild(u, true);
                    }
                    else
                    {
                        u.OnUpdated();
                    }
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

        // Single source of truth for which IAggregateRoot collections Load() restores
        // RowVersion onto. If a new IAggregateRoot type is ever added to Money.cs without a
        // matching entry here, that type's RowVersion never gets restored on Load(), causing a
        // permanent ConcurrencyConflictException on every save. Exposed internally so
        // MockDatabaseContractTests can assert (via reflection) that this array covers every
        // IAggregateRoot implementor in the MyMoney.Business assembly.
        internal static readonly (Type RootType, Func<MyMoney, IEnumerable<PersistentObject>> Collection)[] RestoredAggregateRootCollections =
        {
            (typeof(Account), m => m.Accounts),
            (typeof(Category), m => m.Categories),
            (typeof(Payee), m => m.Payees),
            (typeof(Currency), m => m.Currencies),
            (typeof(Security), m => m.Securities),
            (typeof(Alias), m => m.Aliases),
            (typeof(OnlineAccount), m => m.OnlineAccounts),
            (typeof(StockSplit), m => m.StockSplits),
            (typeof(RentBuilding), m => m.Buildings),
            (typeof(LoanPayment), m => m.LoanPayments),
            (typeof(Transaction), m => m.Transactions),
        };

        private void RestoreRowVersions(MyMoney money)
        {
            foreach ((Type RootType, Func<MyMoney, IEnumerable<PersistentObject>> Collection) entry in RestoredAggregateRootCollections)
            {
                this.ApplyVersions(entry.Collection(money));
            }
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
