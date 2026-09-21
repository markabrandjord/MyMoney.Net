using System;
using System.Collections.Generic;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// Every column of one row, verbatim - including Id and Version. RestoreRow re-inserts these
    /// exactly; it is not "insert a new row with the same values". A restored row that came back
    /// with a fresh Id or a bumped version would break every reference to it and make the
    /// version-conflict tests unreproducible. Spec section 2.6.5 point 2.
    /// </summary>
    public sealed record RowSnapshot(TableRef Table, long Id, IReadOnlyDictionary<string, object> Values);

    /// <summary>
    /// The scoped form, which is the shape tests should actually use. Dispose restores inside its
    /// own transaction and THROWS if the restore fails rather than swallowing - a silently-failed
    /// restore leaks state into every subsequent test in the fixture, the exact failure mode
    /// AppCrashGuard was written to stop tolerating elsewhere in this project. Spec 2.6.5 point 3.
    ///
    /// Singular RowSnapshot rather than the spec's RowSetSnapshot because only RemoveRow
    /// (singular) is built in Plan A - see the plan's D-4 deferral note.
    /// </summary>
    public interface IRowScope : IDisposable
    {
        RowSnapshot Removed { get; }
    }
}
