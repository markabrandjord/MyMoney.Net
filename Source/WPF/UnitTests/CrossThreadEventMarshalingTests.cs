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
    /// Real cross-thread coverage for EventHandlerCollection.RaiseEvent's per-listener dispatch
    /// decision (d.Target is DependencyObject -> marshal via UiDispatcher; otherwise invoke
    /// directly) - the exact mechanism issue #7 wants to rewrite (UiDispatcher wrapping
    /// SynchronizationContext instead of Dispatcher). This is slice 3 of issue #20: a safety net
    /// for that rewrite, using a real Dispatcher on a dedicated thread (standing in for the WPF UI
    /// thread) and the domain model's own Category/Categories change-notification path (PersistentObject.OnInserted
    /// -> FireChangeEvent -> PersistentContainer.SendEvent -> handlers.RaiseEvent), not just the
    /// raw EventHandlerCollection in isolation.
    /// </summary>
    public class CrossThreadEventMarshalingTests
    {
        private Dispatcher previousDispatcher;
        private Dispatcher uiDispatcher;
        private Thread uiThread;

        [SetUp]
        public void SetUp()
        {
            this.previousDispatcher = UiDispatcher.CurrentDispatcher;

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
            UiDispatcher.CurrentDispatcher = this.uiDispatcher;
        }

        [TearDown]
        public void TearDown()
        {
            this.uiDispatcher?.InvokeShutdown();
            this.uiThread?.Join(TimeSpan.FromSeconds(5));
            UiDispatcher.CurrentDispatcher = this.previousDispatcher;
        }

        // A minimal stand-in for a real WPF UI element - DependencyObject is the exact type
        // EventHandlerCollection.RaiseEvent checks a listener's Target against to decide whether
        // to marshal onto the UI dispatcher.
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

        [Test]
        public void RaiseEvent_DependencyObjectListener_RunsOnDispatcherThreadNotCallingThread()
        {
            MyMoney money = new MyMoney();
            FakeUiElement listener = new FakeUiElement();
            money.Categories.Changed += listener.OnChanged;

            int callingThreadId = -1;
            Thread backgroundThread = new Thread(() =>
            {
                callingThreadId = Thread.CurrentThread.ManagedThreadId;
                Category category = new Category(money.Categories) { Name = "BackgroundCategory", Type = CategoryType.Expense };
                money.Categories.AddCategory(category);
            });
            // A stuck background thread must never keep the whole test process alive - if a
            // regression turns this into a real deadlock, the assertion below should fail the
            // test, not hang the test host forever.
            backgroundThread.IsBackground = true;
            backgroundThread.Start();
            Assert.That(backgroundThread.Join(TimeSpan.FromSeconds(5)), Is.True, "Background thread never finished AddCategory.");

            Assert.That(listener.Handled.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "DependencyObject listener was never invoked - marshaling to the UI dispatcher may be broken.");
            Assert.That(listener.HandlerThreadId, Is.EqualTo(this.uiThread.ManagedThreadId),
                "Listener ran on the wrong thread - expected the UI dispatcher thread.");
            Assert.That(listener.HandlerThreadId, Is.Not.EqualTo(callingThreadId),
                "A DependencyObject listener must not run on the calling (background) thread.");
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
                // A non-DependencyObject listener is invoked directly (no marshaling), so by the
                // time AddCategory returns the handler must already have run - no wait needed.
                Assert.That(listener.Handled, Is.True,
                    "Plain listener must be invoked synchronously, before AddCategory returns.");
            });
            backgroundThread.IsBackground = true;
            backgroundThread.Start();
            Assert.That(backgroundThread.Join(TimeSpan.FromSeconds(5)), Is.True, "Background thread never finished AddCategory.");

            Assert.That(listener.HandlerThreadId, Is.EqualTo(callingThreadId),
                "A non-DependencyObject listener must run on the calling thread, not be marshaled elsewhere.");
        }

        [Test]
        public void RaiseEvent_FromThreadHoldingALockTheUiThreadWants_DoesNotDeadlock()
        {
            // Regression guard for the documented reason BeginInvoke (not Invoke) is used in
            // UiDispatcher: "if this is a background thread with a lock() on a money data object
            // ... then UI thread might be blocked on trying to get that lock and this dispatcher
            // invoke would therefore create a deadlock." This reproduces exactly that shape.
            //
            // The competing "UI" action below uses Monitor.TryEnter with a bounded timeout, not a
            // raw lock, deliberately: a real regression to Dispatcher.Invoke here would otherwise
            // create a genuine, unkillable OS-level deadlock between this test's two threads (a
            // real one was hit and confirmed manually while designing this test - the process had
            // to be killed externally, it never recovered on its own). The bounded timeout means
            // even a regression fails this test quickly and cleanly instead of hanging the whole
            // test host. What's actually asserted is TIMING: BeginInvoke returns near-instantly
            // regardless of dispatcher contention; Invoke would block for roughly the full
            // contention timeout below before returning.
            MyMoney money = new MyMoney();
            FakeUiElement listener = new FakeUiElement();
            money.Categories.Changed += listener.OnChanged;

            object gate = new object();
            TimeSpan contentionTimeout = TimeSpan.FromSeconds(2);
            var stopwatch = new System.Diagnostics.Stopwatch();
            bool addCategoryReturned = false;

            Thread backgroundThread = new Thread(() =>
            {
                lock (gate)
                {
                    // Queue work on the "UI" dispatcher that also wants this exact lock, while
                    // still holding it - mirrors the documented scenario (a background thread
                    // holding a lock on a money data object while a UI listener needs it too).
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
                "to the contention timeout the way Dispatcher.Invoke would.");
            Assert.That(listener.Handled.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "Handler must still eventually run once the lock is released.");
        }
    }
}
