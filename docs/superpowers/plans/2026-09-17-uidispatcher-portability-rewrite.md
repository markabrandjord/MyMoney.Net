# UiDispatcher Portability Rewrite (Issue #7) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove `MyMoney.Business`'s dependency on `WindowsBase` (`System.Windows.Threading.Dispatcher`/`System.Windows.DependencyObject`) so it has zero references to any WPF-family assembly, unblocking true portability to the Uno/Xamarin thin clients (item #5), while preserving every WPF call site's existing UI-thread-delivery behavior exactly.

**Architecture:** `UiDispatcher` wraps `System.Threading.SynchronizationContext` instead of `Dispatcher`. `EventHandlerCollection<T,Q>.RaiseEvent` drops its `DependencyObject` auto-marshaling check entirely and becomes a pure synchronous multicast. Two new portable wrapper classes, `UiThreadHandler` (for `EventHandler<ChangeEventArgs>`) and `UiThreadPropertyChangedHandler` (for `PropertyChangedEventHandler`), let WPF call sites opt in to UI-thread marshaling explicitly, each wrapping its inner handler exactly once so subscribe/unsubscribe always match. 30 real WPF subscriptions across 13 files migrate to use them.

**Tech Stack:** C#/.NET 10.0, WPF, NUnit.

**Spec:** `docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md`

## Global Constraints

- `MyMoney.Business` must end this plan with zero references to `PresentationFramework`, `PresentationCore`, or `WindowsBase` — verified by the tightened `LayerBoundaryTests` in the final task.
- Every migrated call site's *observable behavior* must be identical before and after migration — this is a plumbing rewrite, not a behavior change. Do not "fix" the two pre-existing anomalies documented below; migrate them exactly as they are today.
- `dotnet build Source/WPF/MyMoney.sln` and `dotnet test Source/WPF/UnitTests/UnitTests.csproj` must both be clean at the end of every task in this plan — the solution must never be left in a broken intermediate state between tasks (Task 1 lands the core rewrite and the 5 `UiDispatcher.CurrentContext` setter call sites together for exactly this reason: removing the old `CurrentDispatcher` property breaks the build everywhere it's referenced).
- Pre-existing anomalies to preserve as-is, not fix: `Views/AliasesView.xaml.cs`'s constructor subscribes (`+=`) on `Unloaded` where `-=` looks intended; `View Selectors/BalanceControl.xaml.cs`'s `Transactions.Changed` re-subscribe pattern lives outside a property setter, inside a method that runs unconditionally every call.
- `UiThreadHandler`/`UiThreadPropertyChangedHandler` instances must be constructed exactly once per logical subscription (typically a `readonly` field) and their `.Handler` property used for both `+=` and `-=` — never re-constructed at each use, or subscribe/unsubscribe will stop matching (the exact bug class this whole rewrite exists to prevent).

---

### Task 1: Core rewrite — `UiDispatcher`, `EventHandlerCollection`, new wrapper classes, and the 5 WPF setter call sites

**Files:**
- Modify: `Source/WPF/MyMoney.Business/Utilities/Dispatcher.cs`
- Modify: `Source/WPF/MyMoney.Business/Utilities/EventHandlerCollection.cs`
- Create: `Source/WPF/MyMoney.Business/Utilities/UiThreadHandler.cs`
- Create: `Source/WPF/MyMoney.Business/Utilities/UiThreadPropertyChangedHandler.cs`
- Modify: `Source/WPF/UnitTests/CrossThreadEventMarshalingTests.cs`
- Modify: `Source/WPF/MyMoney/MainWindow.xaml.cs` (3 call sites, in the `MainWindow()` and `MainWindow(Settings)` constructors)
- Modify: `Source/WPF/PerformanceViewer/MainWindow.xaml.cs` (1 call site, its constructor)
- Modify: `Source/WPF/UIControlsTest/MainWindow.xaml.cs` (1 call site, its constructor)

**Interfaces:**
- Produces: `UiDispatcher.CurrentContext` (`SynchronizationContext`, replaces `CurrentDispatcher`), `UiDispatcher.BeginInvoke(Delegate, params object[])` / `UiDispatcher.Invoke(Delegate, params object[])` (signatures unchanged). `UiThreadHandler(EventHandler<ChangeEventArgs> inner)` with `.Handler` property of type `EventHandler<ChangeEventArgs>`. `UiThreadPropertyChangedHandler(PropertyChangedEventHandler inner)` with `.Handler` property of type `PropertyChangedEventHandler`. All later tasks consume these two wrapper classes.

This task must land atomically — the solution will not build between these individual file changes, only once all of them are done together.

- [ ] **Step 1: Rewrite `Dispatcher.cs`**

Replace the entire contents of `Source/WPF/MyMoney.Business/Utilities/Dispatcher.cs` with:

```csharp
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
```

- [ ] **Step 2: Rewrite `EventHandlerCollection.cs`'s `RaiseEvent`**

In `Source/WPF/MyMoney.Business/Utilities/EventHandlerCollection.cs`, remove the `using System.Windows;` line, and replace the `RaiseEvent` method body with:

```csharp
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
```

The `protected virtual void Invoke(Delegate d, object[] args) => d.Method.Invoke(d.Target, args);` method below it is unchanged.

- [ ] **Step 3: Create `UiThreadHandler.cs`**

```csharp
using System;

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
```

- [ ] **Step 4: Create `UiThreadPropertyChangedHandler.cs`**

```csharp
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
```

- [ ] **Step 5: Rewrite `CrossThreadEventMarshalingTests.cs`**

Replace the entire contents of `Source/WPF/UnitTests/CrossThreadEventMarshalingTests.cs` with:

```csharp
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
```

- [ ] **Step 6: Update the 5 WPF-side `UiDispatcher` setter call sites**

In `Source/WPF/MyMoney/MainWindow.xaml.cs`, `Source/WPF/PerformanceViewer/MainWindow.xaml.cs`, and `Source/WPF/UIControlsTest/MainWindow.xaml.cs`, every occurrence of:

```csharp
UiDispatcher.CurrentDispatcher = this.Dispatcher;
```

(and `MainWindow.xaml.cs`'s one fully-qualified `Walkabout.Utilities.UiDispatcher.CurrentDispatcher = this.Dispatcher;`) becomes:

```csharp
UiDispatcher.CurrentContext = new System.Windows.Threading.DispatcherSynchronizationContext(this.Dispatcher);
```

(fully-qualify the same way the original line did, i.e. keep `Walkabout.Utilities.UiDispatcher.CurrentContext = ...` for the one occurrence that was already fully qualified).

- [ ] **Step 7: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build, 0 errors. (If any project still references `UiDispatcher.CurrentDispatcher`, the build will fail there — that means a 6th call site was missed; find it with `grep -rn "UiDispatcher.CurrentDispatcher" Source/WPF --include=*.cs` and update it the same way as Step 6.)

- [ ] **Step 8: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~CrossThreadEventMarshalingTests"`
Expected: all 6 tests pass (`RaiseEvent_DependencyObjectListener_NoLongerAutoMarshals_RunsSynchronouslyOnCallingThread`, `RaiseEvent_PlainListener_RunsSynchronouslyOnCallingThread`, `UiThreadHandler_Unsubscribe_ActuallyStopsDelivery`, `UiThreadHandler_StillMarshalsToUiThreadWhileSubscribed`, `RaiseEvent_FromThreadHoldingALockTheUiThreadWants_DoesNotDeadlock`).

Then run the full suite: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass (0 failed), matching the pre-existing baseline count plus the one net-new test (6 tests in this file now, vs. 3 before).

- [ ] **Step 9: Commit**

```bash
git add Source/WPF/MyMoney.Business/Utilities/Dispatcher.cs Source/WPF/MyMoney.Business/Utilities/EventHandlerCollection.cs Source/WPF/MyMoney.Business/Utilities/UiThreadHandler.cs Source/WPF/MyMoney.Business/Utilities/UiThreadPropertyChangedHandler.cs Source/WPF/UnitTests/CrossThreadEventMarshalingTests.cs Source/WPF/MyMoney/MainWindow.xaml.cs Source/WPF/PerformanceViewer/MainWindow.xaml.cs Source/WPF/UIControlsTest/MainWindow.xaml.cs
git commit -m "Rewrite UiDispatcher to wrap SynchronizationContext, remove DependencyObject auto-marshal"
```

---

### Task 2: Migrate `Views/TransactionsView.xaml.cs` — outer `Money` property

**Files:**
- Modify: `Source/WPF/MyMoney/Views/TransactionsView.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

- [ ] **Step 1: Add the field and update the `Money` property setter**

Add a field near `private MyMoney myMoney;` (around line 100):

```csharp
private readonly UiThreadHandler onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
```

Wait — a field initializer cannot reference `this.OnMoneyChanged` as `this` isn't available until after the constructor runs its base call. Declare it without an initializer and assign it where the field is declared using a constructor-less pattern instead: since `TransactionsView`'s constructor is at line 412 and the field is far above it, initialize in the constructor. Add this as the **first line** of the `TransactionsView()` constructor (before `this.InitializeComponent();` or immediately after — either is fine since it doesn't depend on XAML-loaded state):

```csharp
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
```

and declare the field (non-`readonly`, since it's now constructor-assigned) near `private MyMoney myMoney;`:

```csharp
private UiThreadHandler onMoneyChangedUi;
```

Then change the `Money` property setter (around lines 1330-1338):

```csharp
// Before:
if (this.myMoney != null)
{
    this.myMoney.Changed -= new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
}
this.myMoney = value;
if (value != null)
{
    this.myMoney.Changed += new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
}

// After:
// Wrapped once in the ctor and reused for both subscribe and unsubscribe below - see
// UiThreadHandler's doc comment for why a bare re-wrap or a mismatched raw method group here
// would silently break unsubscription (a mistake a Roslyn analyzer could catch at compile time
// if this class of bug recurs - deferred for now, see issue #49).
if (this.myMoney != null)
{
    this.myMoney.Changed -= this.onMoneyChangedUi.Handler;
}
this.myMoney = value;
if (value != null)
{
    this.myMoney.Changed += this.onMoneyChangedUi.Handler;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Views/TransactionsView.xaml.cs
git commit -m "Migrate TransactionsView.xaml.cs's Money.Changed subscription to UiThreadHandler"
```

---

### Task 3: Migrate `Views/TransactionsView.xaml.cs` — 5 nested cell/field classes via `TransactionPropertyChangeSubscription`

**Files:**
- Create: `Source/WPF/MyMoney/Views/TransactionPropertyChangeSubscription.cs`
- Modify: `Source/WPF/MyMoney/Views/TransactionsView.xaml.cs` (5 nested classes: `TransactionCell`, `TransactionAttachmentIcon`, `TransactionTextField`, `TransactionStatusButton`, `TransactionAmountControl`)

**Interfaces:**
- Consumes: `UiThreadPropertyChangedHandler` (Task 1).
- Produces: `TransactionPropertyChangeSubscription(PropertyChangedEventHandler onPropertyChanged)` with `.Subscribe(Transaction to)` / `.Unsubscribe(Transaction from)` methods — used only within this file, no other task depends on it.

- [ ] **Step 1: Create `TransactionPropertyChangeSubscription.cs`**

```csharp
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
```

- [ ] **Step 2: Migrate `TransactionCell` (around line 5488)**

Add a field next to `private Transaction context;` (around line 5567):

```csharp
private readonly TransactionPropertyChangeSubscription propertyChangeSubscription;
```

Initialize it in the constructor (`public TransactionCell()`, around line 5534), as its first line:

```csharp
this.propertyChangeSubscription = new TransactionPropertyChangeSubscription(this.OnPropertyChanged);
```

Then replace each of the 3 subscribe/unsubscribe call sites:

`OnUnloaded` (around line 5547-5554):
```csharp
// Before:
private void OnUnloaded()
{
    // stop listening
    if (this.context != null)
    {
        this.context.PropertyChanged -= this.OnPropertyChanged;
    }
}

// After:
private void OnUnloaded()
{
    // stop listening
    this.propertyChangeSubscription.Unsubscribe(this.context);
}
```

`OnLoaded` (around line 5556-5565):
```csharp
// Before:
private void OnLoaded()
{
    // start listening
    if (this.context != null)
    {
        this.context.PropertyChanged -= this.OnPropertyChanged;
        this.context.PropertyChanged += this.OnPropertyChanged;
        this.UpdateUI();
    }
}

// After:
private void OnLoaded()
{
    // start listening
    if (this.context != null)
    {
        this.propertyChangeSubscription.Unsubscribe(this.context);
        this.propertyChangeSubscription.Subscribe(this.context);
        this.UpdateUI();
    }
}
```

`SetContext` (around line 5641-5653):
```csharp
// Before:
private void SetContext(Transaction transaction)
{
    if (this.context != null)
    {
        this.context.PropertyChanged -= this.OnPropertyChanged;
    }
    this.context = transaction;
    if (this.context != null)
    {
        this.context.PropertyChanged -= this.OnPropertyChanged;
        this.context.PropertyChanged += this.OnPropertyChanged;
    }
}

// After:
private void SetContext(Transaction transaction)
{
    this.propertyChangeSubscription.Unsubscribe(this.context);
    this.context = transaction;
    this.propertyChangeSubscription.Subscribe(this.context);
}
```

- [ ] **Step 3: Migrate `TransactionAttachmentIcon` (around line 5867)**

Same shape as `TransactionCell`, using `this.OnContextPropertyChanged` (not `this.OnPropertyChanged`) as the wrapped handler. Add the field next to `private Transaction context;` (around line 5869), initialize in the constructor (around line 5871) with `new TransactionPropertyChangeSubscription(this.OnContextPropertyChanged)`, and replace the 3 call sites in `OnUnloaded` (~5884-5891), `OnLoaded` (~5893-5902), and `SetContext` (~5913-5925) exactly as in Step 2, substituting `this.OnContextPropertyChanged` for `this.OnPropertyChanged` in the constructor call only (the `Subscribe`/`Unsubscribe` call shapes are identical).

- [ ] **Step 4: Migrate `TransactionTextField` (around line 6923)**

Same shape, `this.OnPropertyChanged`. Add the field next to `private Transaction context;` (around line 6925). This class's constructor (around line 6930) already calls `this.SetContext(dataItem as Transaction);` as its first statement — the `propertyChangeSubscription` field must be initialized **before** that call, so add `this.propertyChangeSubscription = new TransactionPropertyChangeSubscription(this.OnPropertyChanged);` as the very first line of the constructor, before `this.getter = getter;`. Replace the 3 call sites in `OnUnloaded` (~6954-6961), `OnLoaded` (~6963-6972), and `SetContext` (~6993-7006) exactly as in Step 2.

- [ ] **Step 5: Migrate `TransactionStatusButton` (around line 7110)**

Same shape, `this.OnPropertyChanged`. Add the field next to `private Transaction context;` (around line 7114), initialize as the first line of the constructor (around line 7120). Replace the 3 call sites in `OnUnloaded` (~7138-7145), `OnLoaded` (~7147-7156), and `SetContext` (~7188-7200) exactly as in Step 2.

- [ ] **Step 6: Migrate `TransactionAmountControl` (around line 7340)**

Same shape, `this.OnPropertyChanged`. Add the field next to `private Transaction context;` (around line 7346), initialize as the first line of the constructor (around line 7355), before the `grid` is constructed. Replace the 3 call sites in `OnUnloaded` (~7379-7386), `OnLoaded` (~7388-7398), and `SetContext` (~7489-7502) exactly as in Step 2.

- [ ] **Step 7: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 8: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 9: Commit**

```bash
git add Source/WPF/MyMoney/Views/TransactionPropertyChangeSubscription.cs Source/WPF/MyMoney/Views/TransactionsView.xaml.cs
git commit -m "Migrate TransactionsView.xaml.cs's 5 nested PropertyChanged subscribers via TransactionPropertyChangeSubscription"
```

---

### Task 4: Migrate `Views/SecuritiesView.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/Views/SecuritiesView.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

- [ ] **Step 1: Add the field and update the `Money` (or equivalently-named) property setter**

Add a field next to `private MyMoney money;` (around line 469):

```csharp
private UiThreadHandler onMoneyChangedUi;
```

Find this class's constructor and add as its first line:

```csharp
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
```

Then update the property setter (around lines 477-486):

```csharp
// Before:
if (this.money != null)
{
    this.money.Changed -= this.OnMoneyChanged;
}
this.money = value;
if (this.money != null)
{
    this.money.Changed += this.OnMoneyChanged;
}

// After:
if (this.money != null)
{
    this.money.Changed -= this.onMoneyChangedUi.Handler;
}
this.money = value;
if (this.money != null)
{
    this.money.Changed += this.onMoneyChangedUi.Handler;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Views/SecuritiesView.xaml.cs
git commit -m "Migrate SecuritiesView.xaml.cs's Money.Changed subscription to UiThreadHandler"
```

---

### Task 5: Migrate `Views/CurrenciesView.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/Views/CurrenciesView.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This file has **three** call sites all referencing the same handler: the constructor's `Unloaded` lambda (line 43), and the `Money` property setter's unsubscribe (line 470) and subscribe (line 475). All three must use the same field.

- [ ] **Step 1: Add the field and update all three call sites**

Add a field next to `private MyMoney money;` (around line 459):

```csharp
private UiThreadHandler onMoneyChangedUi;
```

In the constructor (around line 35-46), add as the first line:

```csharp
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
```

before `this.InitializeComponent();`. Then update the `Unloaded` lambda (around line 39-45):

```csharp
// Before:
Unloaded += (s, e) =>
{
    if (this.money != null)
    {
        this.money.Changed -= new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
    }
};

// After:
Unloaded += (s, e) =>
{
    if (this.money != null)
    {
        this.money.Changed -= this.onMoneyChangedUi.Handler;
    }
};
```

Then the `Money` property setter (around lines 467-478):

```csharp
// Before:
if (this.money != null)
{
    this.money.Changed -= new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
}
this.money = value;
if (this.money != null)
{
    this.money.Changed += new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
    this.ShowCurrencies();
}

// After:
if (this.money != null)
{
    this.money.Changed -= this.onMoneyChangedUi.Handler;
}
this.money = value;
if (this.money != null)
{
    this.money.Changed += this.onMoneyChangedUi.Handler;
    this.ShowCurrencies();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Views/CurrenciesView.xaml.cs
git commit -m "Migrate CurrenciesView.xaml.cs's Money.Changed subscriptions to UiThreadHandler"
```

---

### Task 6: Migrate `Views/AliasesView.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/Views/AliasesView.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

**Preserve the pre-existing anomaly exactly**: the constructor's `Unloaded` handler uses `+=`, not `-=`, today. Do not change the operator — only replace the raw method group with the wrapper's `.Handler`.

- [ ] **Step 1: Add the field and update both call sites**

Add a field next to `private MyMoney money;` (around line 213):

```csharp
private UiThreadHandler onMoneyChangedUi;
```

In the constructor (around line 24-37), add as the first line:

```csharp
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
```

before `this.InitializeComponent();`. Then update the `Unloaded` lambda (around line 30-36) — **keep the `+=` exactly as it is today, this is a pre-existing anomaly, not something to fix**:

```csharp
// Before:
Unloaded += (s, e) =>
{
    if (this.money != null)
    {
        this.money.Changed += this.OnMoneyChanged;
    }
};

// After:
Unloaded += (s, e) =>
{
    if (this.money != null)
    {
        this.money.Changed += this.onMoneyChangedUi.Handler;
    }
};
```

Then the `Money` property setter (around lines 220-233):

```csharp
// Before:
if (this.money != null)
{
    this.money.Changed -= this.OnMoneyChanged;
}

this.money = value;

if (this.money != null)
{
    this.money.Changed += this.OnMoneyChanged;
    this.ShowAliases();
}

// After:
if (this.money != null)
{
    this.money.Changed -= this.onMoneyChangedUi.Handler;
}

this.money = value;

if (this.money != null)
{
    this.money.Changed += this.onMoneyChangedUi.Handler;
    this.ShowAliases();
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Views/AliasesView.xaml.cs
git commit -m "Migrate AliasesView.xaml.cs's Money.Changed subscriptions to UiThreadHandler"
```

---

### Task 7: Migrate `View Selectors/AccountsControl.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/View Selectors/AccountsControl.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This file has 3 subscriptions in the `MyMoney` property setter: `Accounts.Changed`, `Changed`, and `Rebalanced`. All three handler methods differ, so this needs 3 separate `UiThreadHandler` fields.

- [ ] **Step 1: Add the 3 fields and update the `MyMoney` property setter**

Add fields near `private MyMoney myMoney;`:

```csharp
private UiThreadHandler onAccountsChangedUi;
private UiThreadHandler onMoneyChangedUi;
private UiThreadHandler onBalanceChangedUi;
```

Find this class's constructor and add as its first lines:

```csharp
this.onAccountsChangedUi = new UiThreadHandler(this.OnAccountsChanged);
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
this.onBalanceChangedUi = new UiThreadHandler(this.OnBalanceChanged);
```

Then update the `MyMoney` property setter (around lines 117-136):

```csharp
// Before:
if (this.myMoney != null)
{
    this.myMoney.Accounts.Changed -= new EventHandler<ChangeEventArgs>(this.OnAccountsChanged);
    this.myMoney.Changed -= new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
    this.myMoney.Rebalanced -= new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);
}
this.myMoney = value;
this.Select(null);

if (value != null)
{
    this.myMoney.Accounts.Changed += new EventHandler<ChangeEventArgs>(this.OnAccountsChanged);
    this.myMoney.Changed += new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
    this.myMoney.Rebalanced += new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);
    this.OnAccountsChanged(this, new ChangeEventArgs(this.myMoney.Accounts, null, ChangeType.Reloaded));
}

// After:
if (this.myMoney != null)
{
    this.myMoney.Accounts.Changed -= this.onAccountsChangedUi.Handler;
    this.myMoney.Changed -= this.onMoneyChangedUi.Handler;
    this.myMoney.Rebalanced -= this.onBalanceChangedUi.Handler;
}
this.myMoney = value;
this.Select(null);

if (value != null)
{
    this.myMoney.Accounts.Changed += this.onAccountsChangedUi.Handler;
    this.myMoney.Changed += this.onMoneyChangedUi.Handler;
    this.myMoney.Rebalanced += this.onBalanceChangedUi.Handler;
    this.OnAccountsChanged(this, new ChangeEventArgs(this.myMoney.Accounts, null, ChangeType.Reloaded));
}
```

(Do not touch `this.databaseSettings.PropertyChanged` in the same file — its source, `DatabaseSettings`, implements `INotifyPropertyChanged` directly and never goes through `EventHandlerCollection`; it's unaffected by this rewrite. Do not touch `AccountItemViewModel`'s `this.account.PropertyChanged` either — that class is a plain, non-`DependencyObject` view-model, already synchronous today, out of scope.)

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add "Source/WPF/MyMoney/View Selectors/AccountsControl.xaml.cs"
git commit -m "Migrate AccountsControl.xaml.cs's Changed/Rebalanced subscriptions to UiThreadHandler"
```

---

### Task 8: Migrate `View Selectors/BalanceControl.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/View Selectors/BalanceControl.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

**Preserve the pre-existing anomaly exactly**: this subscription is not in a property setter — it's inside a method that runs an unconditional `-=` immediately followed by `+=` every time that method is called (a defensive re-subscribe pattern used for several unrelated events in the same method). The `UiThreadHandler` field must be a class-level field constructed **once**, not re-constructed inside that method, or each call would produce a different wrapper and the pair would stop matching.

- [ ] **Step 1: Add the field and update the two call sites**

Add a field next to `private MyMoney myMoney;` (around line 22):

```csharp
private UiThreadHandler onTransactionsChangedUi;
```

Find this class's constructor and add as its first line:

```csharp
this.onTransactionsChangedUi = new UiThreadHandler(this.Transactions_Changed);
```

Then update the two lines inside the method that does the defensive re-subscribe (around lines 152-153):

```csharp
// Before:
this.myMoney.Transactions.Changed -= new EventHandler<ChangeEventArgs>(this.Transactions_Changed);
this.myMoney.Transactions.Changed += new EventHandler<ChangeEventArgs>(this.Transactions_Changed);

// After:
this.myMoney.Transactions.Changed -= this.onTransactionsChangedUi.Handler;
this.myMoney.Transactions.Changed += this.onTransactionsChangedUi.Handler;
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add "Source/WPF/MyMoney/View Selectors/BalanceControl.xaml.cs"
git commit -m "Migrate BalanceControl.xaml.cs's Transactions.Changed subscription to UiThreadHandler"
```

---

### Task 9: Migrate `View Selectors/PayeesControl.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/View Selectors/PayeesControl.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This file has 3 subscriptions in the `MyMoney` property setter: `Payees.Changed`, `Rebalanced`, `Transactions.Changed`.

- [ ] **Step 1: Add the 3 fields and update the `MyMoney` property setter**

Add fields near `private MyMoney myMoney;` (around line 25):

```csharp
private UiThreadHandler onPayeesChangedUi;
private UiThreadHandler onBalanceChangedUi;
private UiThreadHandler onTransactionsChangedUi;
```

Find this class's constructor and add as its first lines:

```csharp
this.onPayeesChangedUi = new UiThreadHandler(this.OnPayeesChanged);
this.onBalanceChangedUi = new UiThreadHandler(this.OnBalanceChanged);
this.onTransactionsChangedUi = new UiThreadHandler(this.OnTransactionsChanged);
```

Then update the `MyMoney` property setter (around lines 28-49):

```csharp
// Before:
if (this.myMoney != null)
{
    this.myMoney.Payees.Changed -= new EventHandler<ChangeEventArgs>(this.OnPayeesChanged);
    this.myMoney.Rebalanced -= new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);
    this.myMoney.Transactions.Changed -= new EventHandler<ChangeEventArgs>(this.OnTransactionsChanged);
}
this.myMoney = value;
if (value != null)
{
    this.myMoney.Payees.Changed += new EventHandler<ChangeEventArgs>(this.OnPayeesChanged);
    this.myMoney.Rebalanced += new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);
    this.myMoney.Transactions.Changed += new EventHandler<ChangeEventArgs>(this.OnTransactionsChanged);

    this.OnPayeesChanged(this, new ChangeEventArgs(this.myMoney.Payees, null, ChangeType.Reloaded));
}

// After:
if (this.myMoney != null)
{
    this.myMoney.Payees.Changed -= this.onPayeesChangedUi.Handler;
    this.myMoney.Rebalanced -= this.onBalanceChangedUi.Handler;
    this.myMoney.Transactions.Changed -= this.onTransactionsChangedUi.Handler;
}
this.myMoney = value;
if (value != null)
{
    this.myMoney.Payees.Changed += this.onPayeesChangedUi.Handler;
    this.myMoney.Rebalanced += this.onBalanceChangedUi.Handler;
    this.myMoney.Transactions.Changed += this.onTransactionsChangedUi.Handler;

    this.OnPayeesChanged(this, new ChangeEventArgs(this.myMoney.Payees, null, ChangeType.Reloaded));
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add "Source/WPF/MyMoney/View Selectors/PayeesControl.xaml.cs"
git commit -m "Migrate PayeesControl.xaml.cs's Changed/Rebalanced subscriptions to UiThreadHandler"
```

---

### Task 10: Migrate `View Selectors/CategoriesControl.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/View Selectors/CategoriesControl.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

- [ ] **Step 1: Add the field and update the `MyMoney` property setter**

Add a field next to `private MyMoney money;` (this class's field is named `money`, not `myMoney` — confirm the exact field name in the file before editing):

```csharp
private UiThreadHandler onMoneyChangedUi;
```

Find this class's constructor and add as its first line:

```csharp
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
```

Then update the `MyMoney` property setter (around lines 87-103):

```csharp
// Before:
if (this.money != null)
{
    this.money.Changed -= new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
}
this.money = value;
if (value != null)
{
    this.money.Changed += new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
    this.OnMoneyChanged(this, new ChangeEventArgs(this.money.Categories, null, ChangeType.Reloaded));
}

// After:
if (this.money != null)
{
    this.money.Changed -= this.onMoneyChangedUi.Handler;
}
this.money = value;
if (value != null)
{
    this.money.Changed += this.onMoneyChangedUi.Handler;
    this.OnMoneyChanged(this, new ChangeEventArgs(this.money.Categories, null, ChangeType.Reloaded));
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add "Source/WPF/MyMoney/View Selectors/CategoriesControl.xaml.cs"
git commit -m "Migrate CategoriesControl.xaml.cs's Money.Changed subscription to UiThreadHandler"
```

---

### Task 11: Migrate `View Selectors/SecuritiesControl.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/View Selectors/SecuritiesControl.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This file has 3 subscriptions in the `MyMoney` property setter: `Securities.Changed`, `Rebalanced`, `Transactions.Changed`.

- [ ] **Step 1: Add the 3 fields and update the `MyMoney` property setter**

Add fields next to `private MyMoney myMoney;` (around line 27):

```csharp
private UiThreadHandler onSecuritiesChangedUi;
private UiThreadHandler onBalanceChangedUi;
private UiThreadHandler onTransactionsChangedUi;
```

Find this class's constructor and add as its first lines:

```csharp
this.onSecuritiesChangedUi = new UiThreadHandler(this.OnSecuritiesChanged);
this.onBalanceChangedUi = new UiThreadHandler(this.OnBalanceChanged);
this.onTransactionsChangedUi = new UiThreadHandler(this.OnTransactionsChanged);
```

Then update the `MyMoney` property setter (around lines 29-50):

```csharp
// Before:
if (this.myMoney != null)
{
    this.myMoney.Securities.Changed -= new EventHandler<ChangeEventArgs>(this.OnSecuritiesChanged);
    this.myMoney.Rebalanced -= new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);
    this.myMoney.Transactions.Changed -= new EventHandler<ChangeEventArgs>(this.OnTransactionsChanged);
}
this.myMoney = value;
if (value != null)
{
    this.myMoney.Securities.Changed += new EventHandler<ChangeEventArgs>(this.OnSecuritiesChanged);
    this.myMoney.Rebalanced += new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);
    this.myMoney.Transactions.Changed += new EventHandler<ChangeEventArgs>(this.OnTransactionsChanged);

    this.OnSecuritiesChanged(this, new ChangeEventArgs(this.myMoney.Securities, null, ChangeType.Reloaded));
}

// After:
if (this.myMoney != null)
{
    this.myMoney.Securities.Changed -= this.onSecuritiesChangedUi.Handler;
    this.myMoney.Rebalanced -= this.onBalanceChangedUi.Handler;
    this.myMoney.Transactions.Changed -= this.onTransactionsChangedUi.Handler;
}
this.myMoney = value;
if (value != null)
{
    this.myMoney.Securities.Changed += this.onSecuritiesChangedUi.Handler;
    this.myMoney.Rebalanced += this.onBalanceChangedUi.Handler;
    this.myMoney.Transactions.Changed += this.onTransactionsChangedUi.Handler;

    this.OnSecuritiesChanged(this, new ChangeEventArgs(this.myMoney.Securities, null, ChangeType.Reloaded));
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add "Source/WPF/MyMoney/View Selectors/SecuritiesControl.xaml.cs"
git commit -m "Migrate SecuritiesControl.xaml.cs's Changed/Rebalanced subscriptions to UiThreadHandler"
```

---

### Task 12: Migrate `View Selectors/RentsControl.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/View Selectors/RentsControl.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This file was missing from the original (incomplete) audit — it has only one subscription, `Rebalanced`, no `.Changed`.

- [ ] **Step 1: Add the field and update the `MyMoney` property setter**

Add a field next to `private MyMoney myMoney;` (around line 29):

```csharp
private UiThreadHandler onBalanceChangedUi;
```

In the constructor (around lines 18-23), add as the first line:

```csharp
this.onBalanceChangedUi = new UiThreadHandler(this.OnBalanceChanged);
```

before `this.InitializeComponent();`. Then update the `MyMoney` property setter (around lines 31-59):

```csharp
// Before:
if (this.myMoney != null)
{
    this.myMoney.Rebalanced -= new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);
}

this.myMoney = value;

if (this.myMoney == null)
{
    // TO DO - clear the list of existing building if any
}
else
{
    this.myMoney.Rebalanced += new EventHandler<ChangeEventArgs>(this.OnBalanceChanged);

    // Fire initial change to display the Buildings in they new Money db
    this.OnBalanceChanged(value.Buildings, new ChangeEventArgs(value.Buildings, null, ChangeType.Reloaded));
}

// After:
if (this.myMoney != null)
{
    this.myMoney.Rebalanced -= this.onBalanceChangedUi.Handler;
}

this.myMoney = value;

if (this.myMoney == null)
{
    // TO DO - clear the list of existing building if any
}
else
{
    this.myMoney.Rebalanced += this.onBalanceChangedUi.Handler;

    // Fire initial change to display the Buildings in they new Money db
    this.OnBalanceChanged(value.Buildings, new ChangeEventArgs(value.Buildings, null, ChangeType.Reloaded));
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add "Source/WPF/MyMoney/View Selectors/RentsControl.xaml.cs"
git commit -m "Migrate RentsControl.xaml.cs's Rebalanced subscription to UiThreadHandler"
```

---

### Task 13: Migrate `Dialogs/AccountDialog.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/Dialogs/AccountDialog.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This dialog is transient (opened/closed repeatedly) — exactly the scenario the concurrency panelist flagged as at risk with a bare wrapping function. `money` is a constructor parameter here, not a field; the wrapper must be stored in a new field so the same instance is available in both the subscribe line and the `Unloaded` lambda's unsubscribe line.

- [ ] **Step 1: Add the field and update both call sites**

Add a field to the class (near other private fields):

```csharp
private UiThreadHandler onMoneyChangedUi;
```

In the constructor, before the existing subscribe line (around line 91), add:

```csharp
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);
```

Then update both call sites (around lines 91-96):

```csharp
// Before:
money.Changed += new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);

Unloaded += (s, e) =>
{
    money.Changed -= new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);
};

// After:
money.Changed += this.onMoneyChangedUi.Handler;

Unloaded += (s, e) =>
{
    money.Changed -= this.onMoneyChangedUi.Handler;
};
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/AccountDialog.xaml.cs
git commit -m "Migrate AccountDialog.xaml.cs's Money.Changed subscription to UiThreadHandler"
```

---

### Task 14: Migrate `Dialogs/RenamePayeeDialog.xaml.cs`

**Files:**
- Modify: `Source/WPF/MyMoney/Dialogs/RenamePayeeDialog.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This file already has an existing `readonly EventHandler<ChangeEventArgs> handler` field, assigned once in the constructor and consistently reused for both subscribe and unsubscribe — this is already exactly the "wrap once, reuse the same reference" shape this design needs. Migrate by changing the field's type, not by adding a new field.

- [ ] **Step 1: Change the field's declared type and initializer**

```csharp
// Before (around line 18):
private readonly EventHandler<ChangeEventArgs> handler;

// After:
private readonly UiThreadHandler handler;
```

```csharp
// Before (around line 126, in the constructor):
this.handler = new EventHandler<ChangeEventArgs>(this.OnPayees_Changed);

// After:
this.handler = new UiThreadHandler(this.OnPayees_Changed);
```

- [ ] **Step 2: Update both usages in the `MyMoney` property setter**

```csharp
// Before (around lines 27-37):
set
{
    if (this.money != null)
    {
        this.money.Payees.Changed -= this.handler;
    }
    this.money = value;
    if (this.money != null)
    {
        this.money.Payees.Changed -= this.handler;
        this.money.Payees.Changed += this.handler;
    }
    this.LoadPayees();
}

// After:
set
{
    if (this.money != null)
    {
        this.money.Payees.Changed -= this.handler.Handler;
    }
    this.money = value;
    if (this.money != null)
    {
        this.money.Payees.Changed -= this.handler.Handler;
        this.money.Payees.Changed += this.handler.Handler;
    }
    this.LoadPayees();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 4: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/RenamePayeeDialog.xaml.cs
git commit -m "Migrate RenamePayeeDialog.xaml.cs's handler field to UiThreadHandler"
```

---

### Task 15: Migrate `MainWindow.xaml.cs`'s `OnChangedUI` subscription

**Files:**
- Modify: `Source/WPF/MyMoney/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `UiThreadHandler` (Task 1).

This handler (`OnChangedUI`) is referenced at 3 line locations, all in `MainWindow.xaml.cs`: an unconditional unsubscribe in `StopTracking()` (around line 585), and an unsubscribe-then-subscribe pair elsewhere (around lines 597-598).

- [ ] **Step 1: Add the field and update all 3 call sites**

Add a field near other private fields in `MainWindow`:

```csharp
private UiThreadHandler onChangedUiHandler;
```

This class has two constructors, `MainWindow()` and `MainWindow(Settings settings)` — add this as the first executable statement in **both** (each constructor already has one or more `UiDispatcher.CurrentContext = ...` assignments from Task 1; this new line doesn't need to be adjacent to those, just present once per constructor, before anything that could raise `Changed`):

```csharp
this.onChangedUiHandler = new UiThreadHandler(this.OnChangedUI);
```

Then update `StopTracking()` (around line 585):

```csharp
// Before:
this.myMoney.Changed -= new EventHandler<ChangeEventArgs>(this.OnChangedUI);

// After:
this.myMoney.Changed -= this.onChangedUiHandler.Handler;
```

And the other pair (around lines 597-598):

```csharp
// Before:
this.myMoney.Changed -= new EventHandler<ChangeEventArgs>(this.OnChangedUI);
this.myMoney.Changed += new EventHandler<ChangeEventArgs>(this.OnChangedUI);

// After:
this.myMoney.Changed -= this.onChangedUiHandler.Handler;
this.myMoney.Changed += this.onChangedUiHandler.Handler;
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 3: Test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/MainWindow.xaml.cs
git commit -m "Migrate MainWindow.xaml.cs's OnChangedUI subscription to UiThreadHandler"
```

---

### Task 16: Characterization tests for `MathHelpers` (write against current, unmigrated behavior)

**Files:**
- Create: `Source/WPF/UnitTests/MathHelpersTests.cs`

**Interfaces:**
- Consumes: `Walkabout.Utilities.MathHelpers` (existing, unmigrated — still `System.Windows.Point`-based at this point in the plan).
- Produces: a fixed set of numeric expectations that Task 17 must continue to satisfy after migrating the type.

**Context**: Task 16's original attempt (tightening `LayerBoundaryTests`) discovered mid-implementation
that `MyMoney.Business` still needs `WindowsBase` for an unrelated reason — `System.Windows.Point` in
`MathHelpers.cs`/`NativeMethods.cs`. See `docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md`'s
"Characterization tests for `MathHelpers`" section for the full rationale: no test coverage exists
anywhere for this code today, despite `Payments.cs` depending on it for real bill/loan amortization
outlier detection. This task writes that coverage **before** Task 17 changes the type, so the
migration can be verified as behavior-preserving.

- [ ] **Step 1: Write the characterization tests**

```csharp
using NUnit.Framework;
using System.Windows;
using Walkabout.Utilities;

namespace Walkabout.Tests
{
    public class MathHelpersTests
    {
        [Test]
        public void LinearRegression_PerfectlyLinearSeries_ReturnsExactSlopeAndIntercept()
        {
            // x implied as 1..N; y = 1 + 2x for x=1..5 gives y = [3, 5, 7, 9, 11].
            MathHelpers.LinearRegression(new double[] { 3, 5, 7, 9, 11 }, out double a, out double b);

            Assert.That(a, Is.EqualTo(1.0).Within(0.0001));
            Assert.That(b, Is.EqualTo(2.0).Within(0.0001));
        }

        [Test]
        public void LinearRegression_NoisySeries_ReturnsHandComputedSlopeAndIntercept()
        {
            // x implied as 1..5, y = [2, 4, 5, 4, 5]. Hand-computed OLS: meanX=3, meanY=4,
            // covariance-sum=6, variance-sum(x)=10, b=6/10=0.6, a=4-0.6*3=2.2.
            MathHelpers.LinearRegression(new double[] { 2, 4, 5, 4, 5 }, out double a, out double b);

            Assert.That(a, Is.EqualTo(2.2).Within(0.0001));
            Assert.That(b, Is.EqualTo(0.6).Within(0.0001));
        }

        [Test]
        public void Covariance_PerfectlyLinearPoints_ReturnsHandComputedSum()
        {
            // (1,3), (2,5), (3,7): meanX=2, meanY=5. Covariance is a raw sum of products of
            // deviations (not divided by count, per this method's own implementation) =
            // (-1*-2) + (0*0) + (1*2) = 4.
            var points = new[] { new Point(1, 3), new Point(2, 5), new Point(3, 7) };

            Assert.That(MathHelpers.Covariance(points), Is.EqualTo(4.0).Within(0.0001));
        }

        [Test]
        public void LinearRegression_PointBasedOverload_MatchesHandComputedValues()
        {
            // Same (1,3), (2,5), (3,7) - perfectly linear y = 1 + 2x, so a=1, b=2.
            var points = new[] { new Point(1, 3), new Point(2, 5), new Point(3, 7) };

            MathHelpers.LinearRegression(points, out double a, out double b);

            Assert.That(a, Is.EqualTo(1.0).Within(0.0001));
            Assert.That(b, Is.EqualTo(2.0).Within(0.0001));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they pass against the CURRENT (unmigrated) code**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~MathHelpersTests"`
Expected: 4/4 pass. If any fail, the hand-computed expected values above are wrong — recompute them,
do not adjust the assertions to match whatever the code currently outputs (that would validate
nothing).

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions (4 new tests added to the prior total).

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/UnitTests/MathHelpersTests.cs
git commit -m "Add characterization tests for MathHelpers before migrating its Point usage"
```

---

### Task 17: Migrate `System.Windows.Point` to `(double X, double Y)`

**Files:**
- Modify: `Source/WPF/MyMoney.Business/Utilities/MathHelpers.cs`
- Modify: `Source/WPF/MyMoney.Business/Utilities/NativeMethods.cs`
- Modify: `Source/WPF/MyMoney/Charts/HistoryBarChart.xaml.cs`
- Modify: `Source/WPF/MyMoney/Utilities/DragAndDrop.cs`
- Modify: `Source/WPF/UnitTests/MathHelpersTests.cs` (from Task 16 — update inputs only, not expected outputs)

**Interfaces:**
- Consumes: Task 16's characterization tests as the correctness oracle for this migration.
- Produces: `MathHelpers.Covariance(IEnumerable<(double X, double Y)>)`,
  `MathHelpers.LinearRegression(IEnumerable<(double X, double Y)>, out double, out double)`,
  `NativeMethods.GetMousePosition()` returning `(double X, double Y)` — no later task consumes these
  further within this plan, but this is the actual completion of issue #7's stated goal.

Full exact code for every file is in `docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md`'s
"Design: `System.Windows.Point` → `(double X, double Y)` migration" section — read it before starting.

- [ ] **Step 1: Re-verify the migration's scope before touching anything**

The spec's own account of which files/callers are affected was itself discovered by exploration, not
planned from the start — re-confirm it rather than assume it's exhaustive:

Run: `grep -rln "System.Windows.Point\|using System.Windows;" Source/WPF/MyMoney.Business --include=*.cs`
Expected: exactly `Utilities/MathHelpers.cs` and `Utilities/NativeMethods.cs`. If anything else
appears, stop and report it before proceeding — it means the scope is larger than this task assumes.

Run: `grep -rn "NativeMethods.GetMousePosition\(\)" Source/WPF --include=*.cs`
Expected: exactly one call site, `Source/WPF/MyMoney/Utilities/DragAndDrop.cs`.

Run: `grep -rn "MathHelpers\.\(Covariance\|LinearRegression\)" Source/WPF --include=*.cs`
Expected: `Payments.cs` (double-only overload, 2 sites) and `HistoryBarChart.xaml.cs` (`Point`-based
overload, 1 site). `Covariance` itself should have no external callers (only used internally by the
`Point`-based `LinearRegression` overload).

- [ ] **Step 2: Migrate `MathHelpers.cs`**

Remove `using System.Windows;`. Change `Covariance(IEnumerable<Point> pts)` to
`Covariance(IEnumerable<(double X, double Y)> pts)`:

```csharp
public static double Covariance(IEnumerable<(double X, double Y)> pts)
{
    double xsum = 0;
    double ysum = 0;
    double count = 0;
    foreach (var d in pts)
    {
        xsum += d.X;
        ysum += d.Y;
        count++;
    }
    if (count == 0)
    {
        return 0;
    }

    double xMean = xsum / count;
    double yMean = ysum / count;
    double covariance = 0;
    foreach (var d in pts)
    {
        covariance += (d.X - xMean) * (d.Y - yMean);
    }
    return covariance;
}
```

Change the double-only `LinearRegression` overload's internal point list:

```csharp
public static void LinearRegression(IEnumerable<double> pts, out double a, out double b)
{
    List<(double X, double Y)> pts2 = new List<(double X, double Y)>(pts.Count());
    double x = 1;
    foreach (double y in pts)
    {
        pts2.Add((x++, y));
    }
    LinearRegression(pts2, out a, out b);
}
```

Change the `Point`-based `LinearRegression` overload's signature only (body is unchanged — the LINQ
projections `from p in pts select p.X` work identically on a named tuple):

```csharp
public static void LinearRegression(IEnumerable<(double X, double Y)> pts, out double a, out double b)
{
    double xMean = Mean(from p in pts select p.X);
    double yMean = Mean(from p in pts select p.Y);
    double xVariance = Variance(from p in pts select p.X);
    double yVariance = Variance(from p in pts select p.Y);
    double covariance = Covariance(pts);
    if (xVariance == 0)
    {
        a = yMean;
        b = 1;
    }
    else
    {
        b = covariance / xVariance;
        a = yMean - (b * xMean);
    }
}
```

- [ ] **Step 3: Migrate `NativeMethods.cs`**

```csharp
// Before:
public static System.Windows.Point GetMousePosition()
{
    NativeMethods.POINT p;
    if (!NativeMethods.GetCursorPos(out p))
    {
        return new System.Windows.Point(0, 0);
    }

    // Convert pixels to device independent WPF coordinates
    return new System.Windows.Point(ConvertPixelsToDeviceIndependentPixels(p.X), ConvertPixelsToDeviceIndependentPixels(p.Y));
}

// After:
public static (double X, double Y) GetMousePosition()
{
    NativeMethods.POINT p;
    if (!NativeMethods.GetCursorPos(out p))
    {
        return (0, 0);
    }

    // Convert pixels to device independent WPF coordinates
    return (ConvertPixelsToDeviceIndependentPixels(p.X), ConvertPixelsToDeviceIndependentPixels(p.Y));
}
```

- [ ] **Step 4: Update `HistoryBarChart.xaml.cs`'s `ComputeLinearRegression()`**

`points` here is a purely local variable feeding `MathHelpers.LinearRegression` — never used for WPF
rendering (confirmed by reading the full method during this plan's design):

```csharp
// Before:
double x = 0;
List<Point> points = new List<Point>();
foreach (HistoryChartColumn c in this.collection)
{
    if ((c == last || c == last) && c.Values.Count() < (avg / 2))
    {
        // skip it.
        continue;
    }

    points.Add(new Point(x++, (double)c.Amount));
}

// After:
double x = 0;
List<(double X, double Y)> points = new List<(double X, double Y)>();
foreach (HistoryChartColumn c in this.collection)
{
    if ((c == last || c == last) && c.Values.Count() < (avg / 2))
    {
        // skip it.
        continue;
    }

    points.Add((x++, (double)c.Amount));
}
```

- [ ] **Step 5: Update `DragAndDrop.cs`'s `UpdateWindowLocation()`**

```csharp
// Before:
private void UpdateWindowLocation()
{
    if (this.dragdropWindow != null)
    {
        Point pos = NativeMethods.GetMousePosition();
        this.dragdropWindow.Left = pos.X + 10;
        this.dragdropWindow.Top = pos.Y + 10;
    }
}

// After:
private void UpdateWindowLocation()
{
    if (this.dragdropWindow != null)
    {
        var pos = NativeMethods.GetMousePosition();
        this.dragdropWindow.Left = pos.X + 10;
        this.dragdropWindow.Top = pos.Y + 10;
    }
}
```

- [ ] **Step 6: Update `MathHelpersTests.cs`'s inputs (Task 16) — expected outputs do not change**

```csharp
// Before:
using System.Windows;
...
var points = new[] { new Point(1, 3), new Point(2, 5), new Point(3, 7) };

// After: remove the `using System.Windows;` line entirely, and everywhere `points` is
// constructed in this file:
var points = new[] { (1.0, 3.0), (2.0, 5.0), (3.0, 7.0) };
```

Apply this to both `Covariance_PerfectlyLinearPoints_ReturnsHandComputedSum` and
`LinearRegression_PointBasedOverload_MatchesHandComputedValues`. Do not change any `Assert.That(...)`
expected value in this file — identical results after this migration is exactly what proves it
preserved behavior.

- [ ] **Step 7: Add a minimal sanity test for `NativeMethods.GetMousePosition()`'s new signature**

This method reads the real OS mouse position, so only its shape (doesn't throw, returns a
well-formed pair) can be meaningfully asserted — not exact coordinates:

```csharp
[Test]
public void GetMousePosition_ReturnsWithoutThrowing()
{
    Assert.DoesNotThrow(() => NativeMethods.GetMousePosition());
}
```

Add this to `MathHelpersTests.cs` (add `using Walkabout.Utilities;` if not already present via
`MathHelpers`'s own namespace — confirm `NativeMethods` is in the same namespace before assuming no
new `using` is needed).

- [ ] **Step 8: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build. (`MyMoney.Business.csproj` still carries its `FrameworkReference` at this
point — that's Task 18 — so this build should succeed without any csproj changes yet.)

- [ ] **Step 9: Run the full test suite**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass. The 4 characterization tests from Task 16 must produce **identical** results to
before this migration — that is the proof this change is behavior-preserving. The new
`GetMousePosition_ReturnsWithoutThrowing` test also passes.

- [ ] **Step 10: Commit**

```bash
git add Source/WPF/MyMoney.Business/Utilities/MathHelpers.cs Source/WPF/MyMoney.Business/Utilities/NativeMethods.cs Source/WPF/MyMoney/Charts/HistoryBarChart.xaml.cs Source/WPF/MyMoney/Utilities/DragAndDrop.cs Source/WPF/UnitTests/MathHelpersTests.cs
git commit -m "Migrate System.Windows.Point usage to (double X, double Y) tuples"
```

---

### Task 18: Remove `MyMoney.Business.csproj`'s WPF `FrameworkReference` and guard targets

**Files:**
- Modify: `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`

**Interfaces:**
- Consumes: Task 17's completion (nothing in `MyMoney.Business` references `System.Windows.Point`
  anymore) plus Tasks 1-15's completion (nothing references `Dispatcher`/`DependencyObject` anymore).

- [ ] **Step 1: Re-verify nothing else in `MyMoney.Business` needs `WindowsBase`**

Run: `grep -rln "System.Windows" Source/WPF/MyMoney.Business --include=*.cs`
Expected: no output. If anything appears, stop — this task cannot proceed until that reference is
also resolved (it means the scope mapped by this plan and its spec was incomplete).

- [ ] **Step 2: Remove the FrameworkReference and both guard targets**

In `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`, remove:

1. The `<ItemGroup>` containing `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" />`.
2. The doc comment immediately above the `RemoveWpfViewRenderingAssemblies` target, and the target
   itself:
   ```xml
   <Target Name="RemoveWpfViewRenderingAssemblies" BeforeTargets="ResolveAssemblyReferences" AfterTargets="ResolveFrameworkReferences">
     <ItemGroup>
       <Reference Remove="@(Reference)" Condition="$([System.String]::Copy('%(Reference.FileName)').StartsWith('PresentationCore')) Or $([System.String]::Copy('%(Reference.FileName)').StartsWith('PresentationFramework')) Or $([System.String]::Copy('%(Reference.FileName)').StartsWith('PresentationUI'))" />
     </ItemGroup>
   </Target>
   ```
3. The doc comment immediately above the `VerifyNoWpfViewRenderingAssemblies` target (it references
   issue #11 as its own rationale — leave issue #11 itself alone, it documented why the self-test
   existed; the self-test's job is now `LayerBoundaryTests`' job), and the target itself:
   ```xml
   <Target Name="VerifyNoWpfViewRenderingAssemblies" AfterTargets="ResolveAssemblyReferences">
     <ItemGroup>
       <_LeakedWpfViewRenderingAssembly Include="@(ReferencePath)" Condition="$([System.String]::Copy('%(ReferencePath.FileName)').StartsWith('PresentationCore')) Or $([System.String]::Copy('%(ReferencePath.FileName)').StartsWith('PresentationFramework')) Or $([System.String]::Copy('%(ReferencePath.FileName)').StartsWith('PresentationUI'))" />
     </ItemGroup>
     <Error Condition="'@(_LeakedWpfViewRenderingAssembly)' != ''"
            Text="MyMoney.Business must never reference WPF UI-rendering assemblies, but RemoveWpfViewRenderingAssemblies failed to strip: @(_LeakedWpfViewRenderingAssembly). This target's Remove condition may no longer match this SDK's ResolveAssemblyReferences/ResolveFrameworkReferences target ordering." />
   </Target>
   ```

After removal, `MyMoney.Business.csproj` should have no `FrameworkReference` `ItemGroup` and no
`Target` elements at all — structurally identical to `MyMoney.Data.csproj` in this respect (confirm
this by reading `Source/WPF/MyMoney.Data/MyMoney.Data.csproj` for comparison if it exists in this
solution).

- [ ] **Step 3: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build. If this fails with a missing-type error referencing anything in
`WindowsBase`/`PresentationCore`/`PresentationFramework`, Step 1's verification missed something —
do not re-add the `FrameworkReference` as a workaround; find and fix the actual remaining reference.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, no regressions.

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney.Business/MyMoney.Business.csproj
git commit -m "Remove MyMoney.Business's now-dead WPF FrameworkReference and guard targets"
```

---

### Task 19: Tighten `LayerBoundaryTests`, final full regression, and manual smoke-test callout

**Files:**
- Modify: `Source/WPF/UnitTests/LayerBoundaryTests.cs`

**Interfaces:**
- Consumes: Tasks 1-18's combined completion — this is the acceptance gate for the whole plan.

- [ ] **Step 1: Verify there is no remaining WPF-family reference in `MyMoney.Business`**

Before editing the test, confirm the claim it's about to assert is actually true. This should now
genuinely pass, having failed at this exact point once already (Task 17/18 exist because of that):

Run: `grep -rln "System.Windows" Source/WPF/MyMoney.Business --include=*.cs`
Expected: no output. If anything appears, stop — Tasks 1-18 are not actually complete.

- [ ] **Step 2: Tighten `MyMoneyBusiness_HasNoUiRenderingAssemblyReference`**

In `Source/WPF/UnitTests/LayerBoundaryTests.cs`, update the class doc comment and the test itself:

```csharp
// Before:
// MyMoney.Business is allowed WindowsBase (the domain model's
// event-marshaling backbone has a load-bearing dependency on
// System.Windows.Threading.Dispatcher/DependencyObject -- Ruling,
// 2026-09-15, see the plan's Global Constraints) but never the two
// true UI-rendering assemblies. MyMoney.Data forbids all three --
// nothing in it needs WindowsBase even transitively through its own
// direct references (it only reaches MyMoney.Business's WindowsBase
// dependency via the ProjectReference, which GetReferencedAssemblies
// does not surface -- that method returns only an assembly's own
// direct references).

// After:
// MyMoney.Business and MyMoney.Data both forbid all three WPF-family
// assemblies. MyMoney.Business's former WindowsBase dependency (the
// domain model's event-marshaling backbone had a load-bearing
// dependency on System.Windows.Threading.Dispatcher/DependencyObject)
// was removed by the issue #7 rewrite - see
// docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md.
// UiDispatcher now wraps System.Threading.SynchronizationContext, and
// EventHandlerCollection has no UI-framework awareness of any kind.
```

```csharp
// Before:
[Test]
public void MyMoneyBusiness_HasNoUiRenderingAssemblyReference()
{
    var assembly = typeof(Walkabout.Data.MyMoney).Assembly;
    var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
    CollectionAssert.IsEmpty(
        referenced.Where(n => UiRenderingAssemblyNames.Contains(n)).ToList(),
        $"MyMoney.Business referenced a UI-rendering assembly: {string.Join(", ", referenced)}");
}

// After:
[Test]
public void MyMoneyBusiness_HasNoWpfAssemblyReference()
{
    var assembly = typeof(Walkabout.Data.MyMoney).Assembly;
    var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
    CollectionAssert.IsEmpty(
        referenced.Where(n => AllWpfAssemblyNames.Contains(n)).ToList(),
        $"MyMoney.Business referenced a WPF assembly: {string.Join(", ", referenced)}");
}
```

(Renaming the test from `..._HasNoUiRenderingAssemblyReference` to `..._HasNoWpfAssemblyReference` to accurately describe what it now checks, matching `MyMoneyData_HasNoWpfAssemblyReference`'s naming.) The now-unused `UiRenderingAssemblyNames` field can be removed since only `AllWpfAssemblyNames` is referenced anywhere in the file after this change — confirm with `grep -n "UiRenderingAssemblyNames" Source/WPF/UnitTests/LayerBoundaryTests.cs` before deleting it.

- [ ] **Step 3: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: clean build.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: full pass, 0 failed — this is the automated proof that issue #7's goal is achieved.

- [ ] **Step 5: Full-solution regression**

Run: `dotnet test Source/WPF/MyMoney.sln` (or the individual project commands this session has established as its baseline pattern — `MyMoney.TestSupport`, etc.)
Expected: matches the established pre-existing baseline (no new failures; `ScenarioTest`'s own known flakiness, if it recurs, is pre-existing and unrelated to this change).

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/UnitTests/LayerBoundaryTests.cs
git commit -m "Tighten LayerBoundaryTests: MyMoney.Business now has zero WPF-family references"
```

- [ ] **Step 7: Manual interactive smoke pass (human checkpoint — cannot be automated)**

This step cannot be completed by an agent without a real interactive desktop session. Before this branch is merged, a human must:

1. Run `MyMoney.exe` interactively.
2. Exercise every migrated view/dialog and confirm UI updates still arrive correctly: Accounts, Transactions (including editing a transaction's status/amount/attachment to exercise the 5 `TransactionsView` nested-class migrations from Task 3), Securities, Currencies, Aliases, Categories, Payees, Rents, the rename-payee dialog, and the account dialog.
3. Specifically watch for any missed UI update (a value that used to refresh automatically but now doesn't) — the concrete failure mode a missed or mismatched `UiThreadHandler` wrap would produce.
4. Confirm the startup version-check/changelog notification path (exercises `ChangeListRequest.Completed`) still shows correctly without needing a second app restart.
5. Confirm a stock-quote download still completes and updates the UI correctly (exercises `StockQuoteManager.DownloadComplete`).

Do not consider this plan complete, and do not merge, until this manual pass has been done and confirmed.
