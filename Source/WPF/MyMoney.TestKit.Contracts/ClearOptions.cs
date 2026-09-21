using System.Collections.Generic;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// A sealed record CLASS, not the spec's record struct: default(ClearOptions) on a record
    /// struct ignores parameter defaults and would silently give ResetIdentity == false, the exact
    /// opposite of the intended default on the exact option spec 2.6.4 calls the cross-engine trap.
    /// </summary>
    public sealed record ClearOptions
    {
        /// <summary>
        /// After DELETE FROM, the next Id a fresh insert receives is NOT the same on the two
        /// engines - SQLite's INTEGER PRIMARY KEY rewinds to 1 by itself unless AUTOINCREMENT
        /// persisted a high-water mark in sqlite_sequence; SQL Server's IDENTITY never rewinds
        /// without DBCC CHECKIDENT. A test asserting Id == 1 after a clear would pass on one
        /// engine and fail on the other for reasons having nothing to do with what it tests.
        /// Spec section 2.6.4.
        /// </summary>
        public bool ResetIdentity { get; init; } = true;

        /// <summary>PRAGMA foreign_key_check after the clear.</summary>
        public bool VerifyForeignKeysAfter { get; init; } = true;

        public static ClearOptions Default { get; } = new ClearOptions();
    }

    public sealed record ClearResult(IReadOnlyDictionary<TableRef, long> RowsDeleted);
}
