using System;
using System.Collections.Generic;

namespace Walkabout.Utilities
{
    /// <summary>
    /// This event handler collection can be used to store event handlers and invoke them.
    /// It is assumed that the event handler is of type EventHandler&lt;T&gt;  This is used 
    /// in the implementation of an event like this:
    /// <code language="C#">
    /// EventHandlerCollection&lt;EventArgs&gt; handlers = new EventHandlerCollection&lt;EventArgs&gt;();
    /// event EventHandler Changed 
    /// {
    ///     add { handlers.Add(value); }
    ///     remove { handlers.Remove(value); }
    /// }
    /// </code>
    /// Then later in the code you can raise this event by doing this:
    /// void OnChanged(object sender, EventArgs e) 
    /// {
    ///     handlers.RaiseEvent(sender, e);
    /// }
    /// </summary>
    /// <typeparam name="T">The type of EventHandlerArgs</typeparam>
    public class EventHandlerCollection<T, Q> where T : Delegate where Q : EventArgs
    {
        private readonly List<T> list = new List<T>();

        public void AddHandler(T h)
        {
            this.list.Add(h);
        }

        public void RemoveHandler(T h)
        {
            this.list.Remove(h);
        }

        public bool HasListeners => this.list.Count > 0;

        public int ListenerCount => this.list.Count;

        /// <summary>
        /// The owner of the event uses this method to raise the event to the registered event handlers.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        // This collection has no concept of UI threads or marshaling - every handler is invoked
        // synchronously, directly, on whichever thread raises the event. A listener that needs
        // UI-thread delivery must opt in explicitly via UiThreadHandler/UiThreadPropertyChangedHandler
        // (see Utilities/UiThreadHandler.cs) at its own subscription site; this class deliberately
        // does not - and must not - try to detect that need itself (see
        // docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md for why an
        // automatic per-listener check was removed from here).
        //
        // If bugs recur from callers forgetting to wrap a UI-bound subscription in one of those
        // wrappers, or from unsubscribing with a different delegate than was subscribed, consider a
        // Roslyn analyzer over a runtime check here - both mistakes are structural and syntactic
        // (visible in the subscription code itself), which is exactly what an analyzer is
        // well-suited to catch at compile time instead of at whatever point in the future the
        // mistake happens to surface at runtime. Deferred for now - see issue #49.
        public void RaiseEvent(object sender, Q args)
        {
            object[] array = new object[] { sender, args };
            foreach (Delegate d in this.list)
            {
                try
                {
                    this.Invoke(d, array);
                }
                catch (System.Reflection.TargetInvocationException e)
                {
                    if (e.InnerException != null)
                    {
                        throw e.InnerException;
                    }
                    throw;
                }
            }
        }

        protected virtual void Invoke(Delegate d, object[] args)
        {
            d.Method.Invoke(d.Target, args);
        }
    }

    public class EventHandlerCollection<Q> : EventHandlerCollection<EventHandler<Q>, Q>
        where Q : EventArgs
    {
    }
}
