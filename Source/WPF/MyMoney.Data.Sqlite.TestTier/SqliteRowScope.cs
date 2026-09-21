using MyMoney.TestKit.Contracts;

namespace Walkabout.Data.Sqlite.TestTier
{
    /// <summary>
    /// Capture + delete on construction; restore on Dispose, inside its own transaction, THROWING
    /// if the restore fails rather than swallowing - spec section 2.6.5 point 3.
    /// </summary>
    internal sealed class SqliteRowScope : IRowScope
    {
        private readonly IMoneyStoreTestControl control;
        private bool restored;

        internal SqliteRowScope(IMoneyStoreTestControl control, RowSnapshot removed)
        {
            this.control = control;
            this.Removed = removed;
        }

        public RowSnapshot Removed { get; }

        public void Dispose()
        {
            if (this.restored)
            {
                return;
            }

            this.restored = true;
            this.control.RestoreRow(this.Removed);
        }
    }
}
