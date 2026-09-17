using System.ComponentModel;

namespace Walkabout.Utilities
{
    /// <summary>
    /// Same shape and same rationale as UiThreadHandler, for PersistentObject.PropertyChanged
    /// subscribers instead of Changed/Rebalanced subscribers - PropertyChangedEventHandler is a
    /// distinct delegate type from EventHandler&lt;ChangeEventArgs&gt;, not interchangeable with it,
    /// so this needs its own concrete wrapper rather than reusing UiThreadHandler.
    /// </summary>
    public sealed class UiThreadPropertyChangedHandler
    {
        public PropertyChangedEventHandler Handler { get; }

        public UiThreadPropertyChangedHandler(PropertyChangedEventHandler inner)
        {
            this.Handler = (sender, args) => UiDispatcher.BeginInvoke(inner, sender, args);
        }
    }
}
