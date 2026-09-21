using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// Put the store into a known state, BELOW the object graph and BELOW the version check.
    /// These exist so a test can reach a state the domain rules would not permit, or would only
    /// permit through a sequence of calls that is itself under test. Using the production API to
    /// build the fixture for a test of the production API is how a test ends up asserting that a
    /// bug is consistent with itself. Spec section 2.6.3.
    ///
    /// THREE LADDERS, and only the data one decomposes (spec section 2.6.1):
    ///   schema - ResetSchema(N): destroys and rebuilds every object
    ///   data   - ClearAllData -> ClearTables -> ClearTable: rows only; every schema object,
    ///            including __SchemaHistory, survives
    ///   row    - CaptureRow / DeleteRow / RestoreRow / RemoveRow
    ///
    /// IDEMPOTENCY (spec section 2.6.8): ResetSchema, ClearAllData, ClearTables, ClearTable and
    /// DeleteRow are safe to call any number of times. RestoreRow deliberately is NOT - it is the
    /// single-shot inverse half of a capture-then-delete pair, and a second call is a primary-key
    /// violation by design.
    ///
    /// EVERY operation here is invisible to any live object graph or cached store state. After any
    /// of them, the caller re-reads.
    ///
    /// Seed is deliberately ABSENT: seeding writes THROUGH IMoneyStore, so a seeded fixture has
    /// exercised the real write path, the real version assignment and the real FK enforcement.
    /// Spec section 2.6.2.
    /// </summary>
    public interface IMoneyStoreTestControl : IDisposable
    {
        // ---- Schema ladder ----

        /// <summary>DropAll() then ApplyTo(targetVersion) - nuke and pave through section 1.5's machinery.</summary>
        void ResetSchema(int targetVersion);

        int CurrentSchemaVersion { get; }

        // ---- Data ladder ----

        ClearResult ClearAllData(ClearOptions options = null);

        ClearResult ClearTables(IReadOnlyCollection<TableRef> tables, ClearOptions options = null);

        ClearResult ClearTable(TableRef table, ClearOptions options = null);

        ClearResult ClearTable<TRoot>(ClearOptions options = null) where TRoot : IAggregateRoot;

        // ---- Row ladder ----

        /// <summary>Throws StoreTestControlException if the row is absent.</summary>
        RowSnapshot CaptureRow(TableRef table, long id);

        /// <summary>Null if the row is absent.</summary>
        RowSnapshot TryCaptureRow(TableRef table, long id);

        /// <summary>
        /// No version check, no cascade. Succeeds as a no-op when id is already absent, so a reset
        /// call site can call it unconditionally and stay idempotent - spec section 2.6.8.
        /// </summary>
        void DeleteRow(TableRef table, long id);

        /// <summary>Re-inserts verbatim: same Id, same Version, same every column.</summary>
        void RestoreRow(RowSnapshot row);

        /// <summary>Capture + delete; Dispose restores byte for byte and throws on failure.</summary>
        IRowScope RemoveRow(TableRef table, long id);

        // ---- Introspection ----

        /// <summary>Data tables only - never views, and never __SchemaHistory.</summary>
        IReadOnlyList<TableRef> Tables { get; }

        long RowCount(TableRef table);
    }
}
