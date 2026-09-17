using System;
using Walkabout.Data;

namespace Walkabout.Utilities
{
    /// <summary>
    /// Wraps a Changed/Rebalanced-event handler so it's delivered via UiDispatcher's marshaling
    /// instead of running synchronously on whichever thread raised the event. Wraps exactly once, at
    /// construction, and always exposes the same delegate reference - subscribing and
    /// unsubscribing with `Handler` therefore always match, unlike a bare wrapping function
    /// (which allocates a new closure per call and silently breaks unsubscribe - see
    /// docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md).
    ///
    /// Always store the UiThreadHandler instance (e.g. as a field) and subscribe/unsubscribe via
    /// its Handler property - never call `new UiThreadHandler(...)` a second time expecting the
    /// same Handler back, and never subscribe with this.Handler but unsubscribe with the bare
    /// original method group (or vice versa). Both mistakes are easy to make by hand and easy to
    /// miss in review; if they recur across enough call sites, a Roslyn analyzer flagging a
    /// subscribe/unsubscribe pair that doesn't reference the same UiThreadHandler instance would be
    /// more reliable than continuing to catch these by hand. Deferred for now - see issue #49.
    /// </summary>
    public sealed class UiThreadHandler
    {
        public EventHandler<ChangeEventArgs> Handler { get; }

        public UiThreadHandler(EventHandler<ChangeEventArgs> inner)
        {
            this.Handler = (sender, args) => UiDispatcher.BeginInvoke(inner, sender, args);
        }
    }
}
