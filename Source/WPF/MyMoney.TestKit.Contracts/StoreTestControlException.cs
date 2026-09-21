using System;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// Wraps an engine error raised by a test-control operation - e.g. DeleteRow failing and
    /// rolling back because another table's foreign key references the row. DeleteRow does not
    /// cascade, deliberately: if it did, RestoreRow would be a lie, restoring the one row it
    /// captured while the rows the cascade took are gone for good. Spec section 2.6.5 point 1.
    /// </summary>
    public class StoreTestControlException : Exception
    {
        public StoreTestControlException(string message) : base(message)
        {
        }

        public StoreTestControlException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
