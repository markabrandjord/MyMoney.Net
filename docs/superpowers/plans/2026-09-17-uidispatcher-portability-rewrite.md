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

### Task 16: Tighten `LayerBoundaryTests`, final full regression, and manual smoke-test callout

**Files:**
- Modify: `Source/WPF/UnitTests/LayerBoundaryTests.cs`

**Interfaces:**
- Consumes: nothing new — this is the acceptance gate for the whole plan.

- [ ] **Step 1: Verify there is no remaining WPF-family reference in `MyMoney.Business`**

Before editing the test, confirm the claim it's about to assert is actually true:

Run: `grep -rln "System.Windows" Source/WPF/MyMoney.Business --include=*.cs`
Expected: no output (or only files unrelated to `WindowsBase`/`DependencyObject`/`Dispatcher` — inspect any hits before proceeding; a real remaining WPF-family reference here means a call site was missed in Tasks 1-15 and must be fixed before this task can proceed).

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

Do not consider this plan complete, and do not merge, until this manual pass has been done and confirmed.
