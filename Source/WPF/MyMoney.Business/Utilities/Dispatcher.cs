using System;
using System.Threading;

namespace Walkabout.Utilities
{
    /// <summary>
    /// During shutdown when window handles are not available, we cannot use ISynchronizeInvoke
    /// </summary>
    public static class UiDispatcher
    {
        private static SynchronizationContext context;
        private static int uiThreadId;

        public static SynchronizationContext CurrentContext
        {
            get
            {
                return context;
            }
            set
            {
                uiThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                context = value;
            }
        }

        /// <summary>
        /// Only use the SynchronizationContext if we are not already on the UI Thread.
        /// This avoids exceptions saying the context is unavailable.
        /// </summary>
        /// <param name="d">The delegate to invoke</param>
        /// <param name="args">Optional parameters</param>
        public static object BeginInvoke(System.Delegate d, params object[] args)
        {
            if (context != null && System.Threading.Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                // Note: we cannot use context.Send because that can lead to deadlocks.
                // For example, if this is a background thread with a lock() on a money data object
                // (like account.rebalance does) then UI thread might be blocked on trying to get
                // that lock and this dispatcher invoke would therefore create a deadlock.
                context.Post(_ => d.DynamicInvoke(args), null);
                return null;
            }
            else
            {
                // we are already on the UI thread so call the delegate directly.
                return d.DynamicInvoke(args);
            }
        }


        /// <summary>
        /// Only use the SynchronizationContext if we are not already on the UI Thread.
        /// This avoids exceptions saying the context is unavailable.
        /// </summary>
        /// <param name="d">The delegate to invoke</param>
        /// <param name="args">Optional parameters</param>
        public static object Invoke(System.Delegate d, params object[] args)
        {
            if (context != null && System.Threading.Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                // Note: must be careful using this method, it could deadlock the UI, so make sure
                // it cannot be interleaved with similar calls.
                object result = null;
                context.Send(_ => result = d.DynamicInvoke(args), null);
                return result;
            }
            else
            {
                // we are already on the UI thread so call the delegate directly.
                return d.DynamicInvoke(args);
            }
        }
    }
}
