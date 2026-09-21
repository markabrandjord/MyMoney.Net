using System;
using System.Collections.Generic;
using System.Data;
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

        public ClearResult ClearTable(TableRef table, ClearOptions options = null) =>
            this.ClearTables(new[] { table }, options);

        public ClearResult ClearAllData(ClearOptions options = null) =>
            this.ClearTables(this.Tables, options);

        public ClearResult ClearTable<TRoot>(ClearOptions options = null) where TRoot : IAggregateRoot =>
            this.ClearTable(TableRef.Of<TRoot>(), options);

        /// <summary>
        /// DELETE FROM per table, in derived FK order, inside ONE transaction. Not TRUNCATE: it is
        /// refused on any table an FK references, so it cannot be applied uniformly, and parity
        /// beats a constant factor here (spec section 2.6.4). No constraint-disabling beyond the
        /// in-transaction defer_foreign_keys safety net - a clear that disables constraint
        /// checking can leave a state the schema forbids.
        ///
        /// Idempotent: a DELETE with no WHERE against an already-empty table deletes zero rows and
        /// succeeds; nothing conditions success on the table being non-empty first (spec 2.6.8).
        /// </summary>
        public ClearResult ClearTables(IReadOnlyCollection<TableRef> tables, ClearOptions options = null)
        {
            options ??= ClearOptions.Default;

            var requested = new HashSet<string>(StringComparer.Ordinal);
            foreach (TableRef table in tables)
            {
                requested.Add(this.Validate(table));
            }

            var deleted = new Dictionary<TableRef, long>();

            using (SQLiteTransaction tx = this.Connection.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    using (var pragma = new SQLiteCommand("PRAGMA defer_foreign_keys = ON;", this.Connection, tx))
                    {
                        pragma.ExecuteNonQuery();
                    }

                    foreach (string name in SqliteTableOrder.ForDelete(this.Connection))
                    {
                        if (!requested.Contains(name))
                        {
                            continue;
                        }

                        long rows;
                        using (var cmd = new SQLiteCommand($"DELETE FROM \"{name}\";", this.Connection, tx))
                        {
                            rows = cmd.ExecuteNonQuery();
                        }

                        if (options.ResetIdentity)
                        {
                            this.ResetIdentity(name, tx);
                        }

                        foreach (TableRef known in TableRef.All)
                        {
                            if (string.Equals(known.Name, name, StringComparison.Ordinal))
                            {
                                deleted[known] = rows;
                            }
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    throw new StoreTestControlException("Clearing tables failed and was rolled back.", ex);
                }
            }

            if (options.VerifyForeignKeysAfter)
            {
                this.VerifyForeignKeys();
            }

            return new ClearResult(deleted);
        }

        /// <summary>
        /// SQLite's INTEGER PRIMARY KEY is the rowid and allocates max(rowid)+1, so an emptied
        /// table starts again at 1 by itself - UNLESS the table is AUTOINCREMENT, in which case
        /// the high-water mark persists in sqlite_sequence and must be deleted explicitly. SQL
        /// Server's IDENTITY never rewinds without DBCC CHECKIDENT, which is why this option
        /// exists at all. Spec section 2.6.4.
        ///
        /// Reseeding a counter that is already at its reset value is itself a no-op, so this does
        /// not break ClearTables' idempotency.
        /// </summary>
        private void ResetIdentity(string table, SQLiteTransaction tx)
        {
            using (var exists = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_sequence';",
                this.Connection, tx))
            {
                if (Convert.ToInt64(exists.ExecuteScalar()) == 0)
                {
                    return;
                }
            }

            using (var cmd = new SQLiteCommand("DELETE FROM sqlite_sequence WHERE name = @t;", this.Connection, tx))
            {
                cmd.Parameters.AddWithValue("@t", table);
                cmd.ExecuteNonQuery();
            }
        }

        private void VerifyForeignKeys()
        {
            using (var cmd = new SQLiteCommand("PRAGMA foreign_key_check;", this.Connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    throw new StoreTestControlException(
                        $"foreign_key_check reported a violation in table '{reader.GetValue(0)}' after the clear.");
                }
            }
        }

        /// <summary>
        /// An identifier cannot be parameterized, so it is validated against the LIVE introspected
        /// table set before interpolation and rejected if it contains a double quote - the
        /// identifier rule under R-CRUD-1. A TableRef cannot be mistyped in the first place, so
        /// this catches the case where the schema no longer has the table.
        /// </summary>
        private string Validate(TableRef table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (table.Name.IndexOf('"') >= 0)
            {
                throw new StoreTestControlException($"Refusing to build SQL for the identifier {table.Name}.");
            }

            foreach (string name in SqliteTableOrder.DataTables(this.Connection))
            {
                if (string.Equals(name, table.Name, StringComparison.Ordinal))
                {
                    return name;
                }
            }

            throw new StoreTestControlException(
                $"'{table.Name}' is not a data table in this database at schema version "
                + $"{this.CurrentSchemaVersion}.");
        }

        public RowSnapshot CaptureRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public RowSnapshot TryCaptureRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public void DeleteRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public void RestoreRow(RowSnapshot row) => throw new NotImplementedException("Task 20.");

        public IRowScope RemoveRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public IReadOnlyList<TableRef> Tables
        {
            get
            {
                var refs = new List<TableRef>();
                foreach (string name in SqliteTableOrder.DataTables(this.Connection))
                {
                    foreach (TableRef known in TableRef.All)
                    {
                        if (string.Equals(known.Name, name, StringComparison.Ordinal))
                        {
                            refs.Add(known);
                        }
                    }
                }

                return refs;
            }
        }

        public long RowCount(TableRef table)
        {
            string name = this.Validate(table);
            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM \"{name}\";", this.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        public void Dispose()
        {
            // The connection belongs to the provisioner, which belongs to the fixture.
        }
    }
}
