using System.ComponentModel;
using Walkabout.Data;
using Walkabout.Utilities;

namespace Walkabout.Views
{
    /// <summary>
    /// Shared subscribe/unsubscribe bookkeeping for TransactionsView's several small per-cell
    /// controls (TransactionCell, TransactionAttachmentIcon, TransactionTextField,
    /// TransactionStatusButton, TransactionAmountControl), each of which tracks a `Transaction`
    /// context and needs its PropertyChanged notifications marshaled to the UI thread. Wraps the
    /// given handler exactly once, in a UiThreadPropertyChangedHandler - the caller still owns its
    /// own `Transaction` field (this class only manages the subscription, not the reference), so
    /// existing code that reads the current Transaction elsewhere in these classes needs no change.
    /// </summary>
    internal sealed class TransactionPropertyChangeSubscription
    {
        private readonly UiThreadPropertyChangedHandler handler;

        public TransactionPropertyChangeSubscription(PropertyChangedEventHandler onPropertyChanged)
        {
            this.handler = new UiThreadPropertyChangedHandler(onPropertyChanged);
        }

        public void Unsubscribe(Transaction from)
        {
            if (from != null)
            {
                from.PropertyChanged -= this.handler.Handler;
            }
        }

        public void Subscribe(Transaction to)
        {
            if (to != null)
            {
                to.PropertyChanged += this.handler.Handler;
            }
        }
    }
}
