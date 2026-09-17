using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Utilities;

namespace Walkabout.Tests
{
    /// <summary>
    /// Real cross-thread coverage for EventHandlerCollection.RaiseEvent (now a pure synchronous
    /// multicast with no UI awareness) and UiThreadHandler (the explicit opt-in wrapper WPF call
    /// sites use for UI-thread delivery) - see
    /// docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md. Uses a real
    /// Dispatcher on a dedicated thread (standing in for the WPF UI thread) and the domain model's
    /// own Category/Categories change-notification path (PersistentObject.OnInserted ->
    /// FireChangeEvent -> PersistentContainer.SendEvent -> handlers.RaiseEvent), not just the raw
    /// EventHandlerCollection in isolation.
    /// </summary>
    public class CrossThreadEventMarshalingTests
    {
        private SynchronizationContext previousContext;
        private Dispatcher uiDispatcher;
        private Thread uiThread;

        [SetUp]
        public void SetUp()
        {
            this.previousContext = UiDispatcher.CurrentContext;

            Dispatcher captured = null;
            var ready = new ManualResetEventSlim(false);
            this.uiThread = new Thread(() =>
            {
                captured = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            this.uiThread.IsBackground = true;
            this.uiThread.SetApartmentState(ApartmentState.STA);
            this.uiThread.Start();
            Assert.That(ready.Wait(TimeSpan.FromSeconds(5)), Is.True, "UI dispatcher thread never started.");

            this.uiDispatcher = captured;
            UiDispatcher.CurrentContext = new DispatcherSynchronizationContext(this.uiDispatcher);
        }

        [TearDown]
        public void TearDown()
        {
            this.uiDispatcher?.InvokeShutdown();
            this.uiThread?.Join(TimeSpan.FromSeconds(5));
            UiDispatcher.CurrentContext = this.previousContext;
        }

        // A minimal stand-in for a real WPF UI element - still useful here to prove
        // EventHandlerCollection no longer auto-marshals based on DependencyObject-ness, and as the
        // listener type for UiThreadHandler-based tests below.
        private class FakeUiElement : DependencyObject
        {
            public volatile int HandlerThreadId = -1;
            public readonly ManualResetEventSlim Handled = new ManualResetEventSlim(false);

            public void OnChanged(object sender, ChangeEventArgs e)
            {
                this.HandlerThreadId = Thread.CurrentThread.ManagedThreadId;
                this.Handled.Set();
            }
        }

        private class PlainListener
        {
            public volatile int HandlerThreadId = -1;
            public volatile bool Handled;

            public void OnChanged(object sender, ChangeEventArgs e)
            {
                this.HandlerThreadId = Thread.CurrentThread.ManagedThreadId;
                this.Handled = true;
            }
        }

        private void AddCategoryFromBackgroundThread(MyMoney money, string name)
        {
            Thread backgroundThread = new Thread(() =>
            {
                Category category = new Category(money.Categories) { Name = name, Type = CategoryType.Expense };
                money.Categories.AddCategory(category);
            });
            backgroundThread.IsBackground = true;
            backgroundThread.Start();
            Assert.That(backgroundThread.Join(TimeSpan.FromSeconds(5)), Is.True, "Background thread never finished AddCategory.");
        }

        [Test]
        public void RaiseEvent_DependencyObjectListener_NoLongerAutoMarshals_RunsSynchronouslyOnCallingThread()
        {
            MyMoney money = new MyMoney();
            FakeUiElement listener = new FakeUiElement();
            money.Categories.Changed += listener.OnChanged; // raw subscription, no UiThreadHandler wrapper

            int callingThreadId = -1;
            Thread backgroundThread = new Thread(() =>
            {
                callingThreadId = Thread.CurrentThread.ManagedThreadId;
                Category category = new Category(money.Categories) { Name = "BackgroundCategory", Type = CategoryType.Expense };
                money.Categories.AddCategory(category);
                Assert.That(listener.Handled.IsSet, Is.True,
                    "Listener must already have run synchronously by the time AddCategory returns - " +
                    "EventHandlerCollection no longer auto-marshals based on DependencyObject-ness.");
            });
            backgroundThread.IsBackground = true;
            backgroundThread.Start();
            Assert.That(backgroundThread.Join(TimeSpan.FromSeconds(5)), Is.True, "Background thread never finished AddCategory.");

            Assert.That(listener.HandlerThreadId, Is.EqualTo(callingThreadId),
                "A raw DependencyObject listener (no UiThreadHandler wrapper) must run on the calling " +
                "thread now - the automatic DependencyObject-based marshaling this test used to prove " +
                "is gone.");
        }

        [Test]
        public void RaiseEvent_PlainListener_RunsSynchronouslyOnCallingThread()
        {
            MyMoney money = new MyMoney();
            PlainListener listener = new PlainListener();
            money.Categories.Changed += listener.OnChanged;

            int callingThreadId = -1;
            Thread backgroundThread = new Thread(() =>
            {
                callingThreadId = Thread.CurrentThread.ManagedThreadId;
                Category category = new Category(money.Categories) { Name = "BackgroundCategory2", Type = CategoryType.Expense };
                money.Categories.AddCategory(category);
                Assert.That(listener.Handled, Is.True,
                    "Plain listener must be invoked synchronously, before AddCategory returns.");
            });
            backgroundThread.IsBackground = true;
            backgroundThread.Start();
            Assert.That(backgroundThread.Join(TimeSpan.FromSeconds(5)), Is.True, "Background thread never finished AddCategory.");

            Assert.That(listener.HandlerThreadId, Is.EqualTo(callingThreadId),
                "A non-wrapped listener must run on the calling thread, not be marshaled elsewhere.");
        }

        [Test]
        public void UiThreadHandler_Unsubscribe_ActuallyStopsDelivery()
        {
            MyMoney money = new MyMoney();
            FakeUiElement listener = new FakeUiElement();
            UiThreadHandler wrapper = new UiThreadHandler(listener.OnChanged);

            money.Categories.Changed += wrapper.Handler;
            money.Categories.Changed -= wrapper.Handler; // same delegate object reference both times

            this.AddCategoryFromBackgroundThread(money, "ShouldNotBeSubscribed");

            Assert.That(listener.Handled.Wait(TimeSpan.FromSeconds(1)), Is.False,
                "The listener must not fire - wrapper.Handler is the same delegate object both " +
                "times, so RemoveHandler's list.Remove(h) correctly finds and removes it.");
        }

        [Test]
        public void UiThreadHandler_StillMarshalsToUiThreadWhileSubscribed()
        {
            MyMoney money = new MyMoney();
            FakeUiElement listener = new FakeUiElement();
            UiThreadHandler wrapper = new UiThreadHandler(listener.OnChanged);

            money.Categories.Changed += wrapper.Handler;

            int callingThreadId = -1;
            Thread backgroundThread = new Thread(() =>
            {
                callingThreadId = Thread.CurrentThread.ManagedThreadId;
                Category category = new Category(money.Categories) { Name = "ShouldStillBeSubscribed", Type = CategoryType.Expense };
                money.Categories.AddCategory(category);
            });
            backgroundThread.IsBackground = true;
            backgroundThread.Start();
            Assert.That(backgroundThread.Join(TimeSpan.FromSeconds(5)), Is.True, "Background thread never finished AddCategory.");

            Assert.That(listener.Handled.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "The wrapped handler must still fire while subscribed - this rules out a trivial " +
                "'never delivers anything' implementation passing the unsubscribe test by accident.");
            Assert.That(listener.HandlerThreadId, Is.EqualTo(this.uiThread.ManagedThreadId),
                "Must run on the UI dispatcher thread, not the calling thread.");
            Assert.That(listener.HandlerThreadId, Is.Not.EqualTo(callingThreadId));
        }

        [Test]
        public void RaiseEvent_FromThreadHoldingALockTheUiThreadWants_DoesNotDeadlock()
        {
            // Regression guard for the documented reason BeginInvoke (not Invoke/Send) is used in
            // UiDispatcher: "if this is a background thread with a lock() on a money data object
            // ... then UI thread might be blocked on trying to get that lock and this dispatcher
            // invoke would therefore create a deadlock." This reproduces exactly that shape.
            //
            // The competing "UI" action below uses Monitor.TryEnter with a bounded timeout, not a
            // raw lock, deliberately: a real regression here would otherwise create a genuine,
            // unkillable OS-level deadlock between this test's two threads (a real one was hit and
            // confirmed manually while designing this test - the process had to be killed
            // externally, it never recovered on its own). The bounded timeout means even a
            // regression fails this test quickly and cleanly instead of hanging the whole test host.
            // What's actually asserted is TIMING: BeginInvoke returns near-instantly regardless of
            // dispatcher contention; Send/Invoke would block for roughly the full contention timeout
            // below before returning.
            MyMoney money = new MyMoney();
            FakeUiElement listener = new FakeUiElement();
            money.Categories.Changed += new UiThreadHandler(listener.OnChanged).Handler;

            object gate = new object();
            TimeSpan contentionTimeout = TimeSpan.FromSeconds(2);
            var stopwatch = new System.Diagnostics.Stopwatch();
            bool addCategoryReturned = false;

            Thread backgroundThread = new Thread(() =>
            {
                lock (gate)
                {
                    this.uiDispatcher.BeginInvoke(new Action(() =>
                    {
                        Monitor.TryEnter(gate, contentionTimeout);
                    }));

                    stopwatch.Start();
                    Category category = new Category(money.Categories) { Name = "DeadlockRegressionCategory", Type = CategoryType.Expense };
                    money.Categories.AddCategory(category);
                    stopwatch.Stop();
                    addCategoryReturned = true;
                }
            });
            backgroundThread.IsBackground = true;
            backgroundThread.Start();
            bool joined = backgroundThread.Join(TimeSpan.FromSeconds(10));

            Assert.That(joined, Is.True, "Background thread never finished.");
            Assert.That(addCategoryReturned, Is.True);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(500)),
                $"AddCategory took {stopwatch.ElapsedMilliseconds}ms while the dispatcher was busy - " +
                "it must return near-instantly (BeginInvoke is fire-and-forget), not block for close " +
                "to the contention timeout the way a blocking Send/Invoke would.");
            Assert.That(listener.Handled.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "Handler must still eventually run once the lock is released.");
        }
    }
}
