using System;
using System.Collections.Generic;
using System.Data.SQLite;
using MyMoney.TestKit.Contracts;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Data.Sqlite.TestTier
{
    /// <summary>
    /// The SQLite facade, for real. Every operation here runs BELOW the object graph and BELOW the
    /// version check, so a test can put the store into a state the domain rules would not permit.
    /// Spec section 2.6.3.
    ///
    /// Obtain it only through SqliteTestControlFactory.Acquire - that is the one guarded
    /// acquisition point (spec section 2.6.4: the check goes where the instance is handed out, not
    /// in twenty methods where the twenty-first is the one that forgets).
    ///
    /// Data ladder: Task 19. Row ladder: Task 20.
    /// </summary>
    public sealed class SqliteMoneyStoreTestControl : IMoneyStoreTestControl
    {
        private readonly SqliteMoneyStoreProvisioner provisioner;

        internal SqliteMoneyStoreTestControl(SqliteMoneyStoreProvisioner provisioner)
        {
            this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        }

        private SQLiteConnection Connection => this.provisioner.Connection;

        public int CurrentSchemaVersion => this.provisioner.CurrentVersion();

        public void ResetSchema(int targetVersion)
        {
            // Unconditionally drop then apply. Neither half is conditioned on "was this already
            // reset", which is what makes it idempotent by construction - spec section 2.6.8.
            this.provisioner.DropAll();
            this.provisioner.ApplyTo(targetVersion);
        }

        public ClearResult ClearAllData(ClearOptions options = null) => throw new NotImplementedException("Task 19.");

        public ClearResult ClearTables(IReadOnlyCollection<TableRef> tables, ClearOptions options = null) =>
            throw new NotImplementedException("Task 19.");

        public ClearResult ClearTable(TableRef table, ClearOptions options = null) =>
            throw new NotImplementedException("Task 19.");

        public ClearResult ClearTable<TRoot>(ClearOptions options = null) where TRoot : IAggregateRoot =>
            this.ClearTable(TableRef.Of<TRoot>(), options);

        public RowSnapshot CaptureRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public RowSnapshot TryCaptureRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public void DeleteRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public void RestoreRow(RowSnapshot row) => throw new NotImplementedException("Task 20.");

        public IRowScope RemoveRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public IReadOnlyList<TableRef> Tables => throw new NotImplementedException("Task 19.");

        public long RowCount(TableRef table) => throw new NotImplementedException("Task 19.");

        public void Dispose()
        {
            // The connection belongs to the provisioner, which belongs to the fixture.
        }
    }
}
