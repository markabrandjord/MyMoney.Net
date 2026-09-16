using System.Collections.Generic;
using System.Data;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// Identifies which storage engine an IDatabase implementation talks to.
    /// Moved here (out of Database/SqlDatabase.cs, a SQL-Server-engine-specific
    /// file that stays out of MyMoney.Business) because it is part of the
    /// IDatabase contract itself, not any one engine's implementation detail.
    /// </summary>
    public enum DbFlavor
    {
        None,
        SqlServer,
        SqlCE,
        Sqlite,
        Xml,
        BinaryXml,
        Mock
    }

    /// <summary>
    /// Marker for PersistentObject types that own their own commit boundary - a whole database
    /// row of their own - and are therefore valid targets of IDatabase.SaveOne/SaveTransfer/
    /// SaveBatch. Owned children (Split/Investment owned by Transaction; RentUnit owned by
    /// RentBuilding) never implement this - they only ever commit as part of their owning
    /// aggregate's SaveOne/SaveBatch call. See
    /// docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R2.
    /// </summary>
    public interface IAggregateRoot
    {
        long Id { get; }
    }

    /// <summary>
    /// Interface for talking to different types of money storage.
    /// </summary>
    public interface IDatabase
    {
        string Server { get; }
        string DatabasePath { get; }
        string UserId { get; }
        string Password { get; set; }
        string BackupPath { get; }
        bool SupportsUserLogin { get; }

        DbFlavor DbFlavor { get; }

        bool Exists { get; }
        void Create();
        MyMoney Load(IStatusService status);
        void Save(MyMoney money);

        /// <summary>
        /// Atomically commit a single aggregate root (e.g. Account, Category, Transaction with
        /// its owned Splits/Investment). Implementations throw ConcurrencyConflictException if
        /// root.RowVersion no longer matches what's actually committed. See the design spec's
        /// R2/R3/R4.
        /// </summary>
        void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot;

        /// <summary>
        /// Atomically commit exactly two peer Transactions that must co-commit together (the
        /// TransformTwoTransactionIntoTransfer shape). See the design spec's R2.
        /// </summary>
        void SaveTransfer(Transaction from, Transaction to);

        /// <summary>
        /// Atomically commit a heterogeneous batch of aggregate roots together (reconciliation,
        /// merges, recategorize, and first-time population of a brand-new database). See the
        /// design spec's R2.
        /// </summary>
        void SaveBatch(IEnumerable<PersistentObject> roots);

        void Backup(string path);
        bool UpgradeRequired { get; }
        void Upgrade();
        void Delete();

        string GetLog();
        DataSet QueryDataSet(string cmd);
        void Disconnect();
    }
}
