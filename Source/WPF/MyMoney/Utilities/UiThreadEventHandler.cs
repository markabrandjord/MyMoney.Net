using System;
using Walkabout.Utilities;

namespace Walkabout.Utilities
{
    /// <summary>
    /// Same wrap-once-reuse-the-same-reference design as MyMoney.Business's UiThreadHandler
    /// (Source/WPF/MyMoney.Business/Utilities/UiThreadHandler.cs), for EventHandler&lt;TArgs&gt;
    /// shapes other than ChangeEventArgs. Lives in this WPF app project, not MyMoney.Business,
    /// because both of its current uses (ChangeListRequest.Completed,
    /// StockQuoteManager.DownloadComplete) are WPF-app-internal events with no portability
    /// requirement - the original issue #7 audit only covered MyMoney.Business's own
    /// EventHandlerCollection usages, but EventHandlerCollection is a public class also used
    /// here; this closes that gap.
    /// </summary>
    public sealed class UiThreadEventHandler<TArgs> where TArgs : EventArgs
    {
        public EventHandler<TArgs> Handler { get; }

        public UiThreadEventHandler(EventHandler<TArgs> inner)
        {
            this.Handler = (sender, args) => UiDispatcher.BeginInvoke(inner, sender, args);
        }
    }
}
