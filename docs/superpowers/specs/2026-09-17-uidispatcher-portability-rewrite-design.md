# UiDispatcher Portability Rewrite (Issue #7) — Design

## Context

`MyMoney.Business`'s change-notification backbone (`PersistentObject`/`PersistentContainer`,
`EventHandlerCollection<T,Q>`, `UiDispatcher`) has a load-bearing dependency on
`System.Windows.Threading.Dispatcher`/`System.Windows.DependencyObject` (`WindowsBase`). This was a
deliberate, documented stopgap made during item #1's layer-extraction work (`LayerBoundaryTests`
allows `MyMoney.Business` to reference `WindowsBase` but forbids `PresentationFramework`/
`PresentationCore`), explicitly meant to be revisited once testing improved enough to validate a
rewrite safely (issue #7). It is the last real blocker to `MyMoney.Business` running on the
Uno/Xamarin thin clients (item #5) planned in `Source/Uno`/`Source/Xamarin`.

Issue #20's three slices closed that precondition: a shared `IDatabase` test suite + `MockDatabase`,
dual-engine parity (#19), and — most directly relevant here — real cross-thread event-marshaling test
coverage (`Source/WPF/UnitTests/CrossThreadEventMarshalingTests.cs`, merged via PR #48).

### The deadlock found while building that precondition

While designing PR #48's third test (a regression guard for the documented reason `UiDispatcher`
uses `BeginInvoke`, not `Invoke`: "if this is a background thread with a lock() on a money data
object ... then UI thread might be blocked on trying to get that lock and this dispatcher invoke
would therefore create a deadlock"), a **real, unrecoverable process deadlock was reproduced**, not
just hypothesized. The first draft used a raw `lock` for a competing "UI thread" action simulating
that exact scenario. Manually reverting `UiDispatcher.BeginInvoke` to call `dispatcher.Invoke`
instead produced a genuine deadlock between the test's two threads — `testhost.exe` hung
indefinitely and had to be killed externally (`Stop-Process -Force`, process found via
`Get-CimInstance Win32_Process`); it never would have recovered on its own, because a real OS-level
deadlock via `lock` cannot be safely interrupted from managed code (no `Thread.Abort` in modern
.NET, and marking the thread `IsBackground = true` only stops it from blocking *process exit* — it
does not unstick the thread).

Fix: the competing action was rewritten to use a bounded `Monitor.TryEnter(gate, contentionTimeout)`
instead of a raw `lock`, and the test asserts on **timing** (the call must return in well under the
contention timeout) rather than eventual completion — this distinguishes fire-and-forget
`BeginInvoke` from a blocking `Invoke` regression without ever risking an unbounded hang. Re-running
the same mutation afterward failed cleanly in ~2 seconds with a precise message instead of hanging.

This matters directly for this rewrite: any regression that reintroduces blocking dispatch semantics
in the new `SynchronizationContext`-backed implementation is a **real deadlock risk in code that
manages the user's live financial data**, not an abstract concern. The three landed tests (rewritten
per this spec, see below) remain the acceptance test for this property.

### The open design question, and how it was settled

Issue #7's own text names the one real unresolved question: once `UiDispatcher` no longer wraps a
WPF `Dispatcher`, what replaces `EventHandlerCollection<T,Q>.RaiseEvent`'s
`d.Target is DependencyObject` check — the signal that decides whether a listener needs UI-thread
marshaling, or can run synchronously? ("No drop-in replacement was identified at design time.")

Three options were identified and reviewed by four independent panelists (concurrency/correctness,
cross-platform portability, API design/maintainability, migration risk), each evaluating all three
options through one lens with no visibility into the others' conclusions — full writeup posted to
issue #7. Summary:

- **Option A** — always marshal via `SynchronizationContext.Post`, deleting the type-check entirely.
  Rejected: breaks the *existing* synchronous-delivery contract for real non-UI subscribers
  (`StockQuoteManager`, `AttachmentManager`, `StatementManager`, `ChangeTracker` — confirmed via grep
  to be plain, non-`DependencyObject` classes), and independently causes `PersistentContainer`'s
  already-unconditional `RaisePropertyChangeEvents` marshaling to fan out into ordering-free bursts
  on batched `Save()`/`EndUpdate` paths.
- **Option B** — explicit opt-in wrapper at the subscription site. 3 of 4 panelists recommended this:
  it's the only option making `MyMoney.Business` truly zero-opinion about UI threading (best fit for
  Uno/Xamarin, whose idiomatic dispatch mechanisms differ from WPF's), has the best per-call-site
  locality, and rolls out incrementally.
- **Option C** — a marker interface (`IRequiresUiThreadDispatch`) checked by type instead of
  `DependencyObject`. The concurrency panelist recommended this instead, having found a real,
  concrete defect in a *bare* Option B: a plain wrapping **function** (`UiDispatcher.Marshal(handler)`)
  allocates a new closure on every call, so `EventHandlerCollection.RemoveHandler`'s
  reference/equality-based `list.Remove(h)` can never find a match — `Changed -= this.OnChanged` (or
  even `Changed -= UiDispatcher.Marshal(this.OnChanged)` again) silently fails to unsubscribe. Not
  theoretical: `AccountDialog`/`RenamePayeeDialog` are transient, opened and closed repeatedly, so
  this would leak object graphs and produce phantom callbacks against closed windows.

**Decision: Option B, refined** — not a bare wrapping function. A follow-up experiment (throwaway
test file, since deleted; results posted to issue #7) confirmed both halves of this empirically
against the real `Categories.Changed` event machinery:

1. The concurrency panelist's concern is real: a naive `NaiveMarshal(handler)` function fails to
   unsubscribe via either the plain original handler or a re-wrapped call, in both cases the listener
   kept firing after "unsubscribing."
2. Wrapping the handler **exactly once, at construction**, in a small object that exposes a *fixed*
   delegate reference via a property closes the gap completely: `Changed -= wrapper.Handler` reliably
   removes it (same object reference both times), while a companion test confirmed the wrapped
   handler still delivers correctly while subscribed (ruling out a trivial "never delivers anything"
   implementation passing by accident). Stable across 4 consecutive full runs, no flakiness.

This design — `UiThreadHandler`/`UiThreadPropertyChangedHandler`, see below — is what this spec
implements. It keeps every one of Option B's real wins (zero portability assumptions in
`MyMoney.Business`, per-call-site locality, incremental rollout) while structurally closing the
concurrency panelist's defect, rather than relying on developer discipline to avoid it.

The ergonomics panelist's proposed Roslyn analyzer (compile-time detection of a forgotten wrap) is
**explicitly out of scope for this work** — deferred as a separate, self-contained follow-up. A
missed wrap fails loudly via WPF's own cross-thread exception during ordinary interactive use, which
is an acceptable interim safety net for this solo-maintainer codebase.

## Goals

- `MyMoney.Business` has zero references to any WPF-family assembly (`PresentationFramework`,
  `PresentationCore`, `WindowsBase`) — matching `MyMoney.Data`'s existing constraint exactly.
- `EventHandlerCollection<T,Q>.RaiseEvent` becomes a pure, UI-agnostic synchronous multicast: no
  type-sniffing, no marshaling decision of any kind inside `MyMoney.Business`.
- Every WPF call site that today relies on `EventHandlerCollection`'s automatic `DependencyObject`
  marshaling continues to receive UI-thread delivery, unchanged in observable behavior, via an
  explicit `UiThreadHandler`/`UiThreadPropertyChangedHandler` wrapper it owns.
- The three landed cross-thread tests (PR #48) continue to prove the same properties they prove
  today (UI-thread marshaling, synchronous non-marshaled delivery, deadlock-avoidance) against the
  new mechanism, plus new coverage for the wrapper's unsubscribe correctness (promoted directly from
  the validation experiment).

## Non-goals

- The Roslyn analyzer (deferred, see above).
- Any change to `Source/Uno`/`Source/Xamarin` themselves — this spec only removes the blocker in
  `MyMoney.Business`; actually wiring up a thin client is separate, future work.
- Any change to `PersistentContainer.RaisePropertyChangeEvents`'s existing unconditional-marshal
  behavior — it doesn't use `EventHandlerCollection`'s per-listener check today and isn't part of
  this design question; it keeps calling `UiDispatcher.BeginInvoke` exactly as today, just backed by
  `SynchronizationContext` instead of `Dispatcher` underneath (see below).

## Design: `UiDispatcher` (`Source/WPF/MyMoney.Business/Utilities/Dispatcher.cs`)

Replace the stored `Dispatcher` with a `SynchronizationContext`:

```csharp
using System;
using System.Threading;

namespace Walkabout.Utilities
{
    public static class UiDispatcher
    {
        private static SynchronizationContext context;
        private static int uiThreadId;

        public static SynchronizationContext CurrentContext
        {
            get => context;
            set
            {
                uiThreadId = Thread.CurrentThread.ManagedThreadId;
                context = value;
            }
        }

        public static object BeginInvoke(Delegate d, params object[] args)
        {
            if (context != null && Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                // Post, not Send: fire-and-forget avoids the same deadlock class documented
                // above and proven via PR #48's regression test.
                context.Post(_ => d.DynamicInvoke(args), null);
                return null;
            }
            else
            {
                return d.DynamicInvoke(args);
            }
        }

        public static object Invoke(Delegate d, params object[] args)
        {
            if (context != null && Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                object result = null;
                context.Send(_ => result = d.DynamicInvoke(args), null);
                return result;
            }
            else
            {
                return d.DynamicInvoke(args);
            }
        }
    }
}
```

The same-thread-bypass logic (capture `uiThreadId` at set-time, compare
`Thread.CurrentThread.ManagedThreadId`) is unchanged — only the marshaled payload changes from
`Dispatcher.BeginInvoke`/`Invoke` to `SynchronizationContext.Post`/`Send`. `System.Windows.Threading`
is no longer referenced anywhere in this file.

**Callers of the setter** (all in WPF-family projects, never `MyMoney.Business` itself, so
referencing WPF types here is fine): `Source/WPF/MyMoney/MainWindow.xaml.cs` (3 call sites, in the
`MainWindow()` and `MainWindow(Settings)` constructors), `Source/WPF/PerformanceViewer/MainWindow.xaml.cs`
(1 call site, its constructor), `Source/WPF/UIControlsTest/MainWindow.xaml.cs` (1 call site, its
constructor) — each currently `UiDispatcher.CurrentDispatcher = this.Dispatcher;`. Update to:

```csharp
UiDispatcher.CurrentContext = new System.Windows.Threading.DispatcherSynchronizationContext(this.Dispatcher);
```

Construct the `DispatcherSynchronizationContext` explicitly rather than reading
`SynchronizationContext.Current` — at these call sites (inside a `Window` constructor, before
`Application.Run()`'s message loop is necessarily pumping), WPF is not guaranteed to have already
installed one as ambient `SynchronizationContext.Current`. Explicit construction has no such timing
dependency and is unambiguous.

## Design: `EventHandlerCollection<T,Q>` (`Source/WPF/MyMoney.Business/Utilities/EventHandlerCollection.cs`)

Remove `using System.Windows;` and the `DependencyObject` branch entirely. `RaiseEvent` becomes:

```csharp
// This collection has no concept of UI threads or marshaling - every handler is invoked
// synchronously, directly, on whichever thread raises the event. A listener that needs UI-thread
// delivery must opt in explicitly via UiThreadHandler/UiThreadPropertyChangedHandler (see
// Utilities/UiThreadHandler.cs) at its own subscription site; this class deliberately does not -
// and must not - try to detect that need itself (see
// docs/superpowers/specs/2026-09-17-uidispatcher-portability-rewrite-design.md for why an automatic
// per-listener check was removed from here).
//
// If bugs recur from callers forgetting to wrap a UI-bound subscription in one of those wrappers, or
// from unsubscribing with a different delegate than was subscribed, consider a Roslyn analyzer over
// a runtime check here - both mistakes are structural and syntactic (visible in the subscription
// code itself), which is exactly what an analyzer is well-suited to catch at compile time instead of
// at whatever point in the future the mistake happens to surface at runtime. Deferred for now - see
// issue #49.
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
            if (e.InnerException != null) { throw e.InnerException; }
            throw;
        }
    }
}
```

Always synchronous, always direct — zero UI awareness. `protected virtual void Invoke(Delegate d,
object[] args) => d.Method.Invoke(d.Target, args);` is unchanged.

## Design: `UiThreadHandler` and `UiThreadPropertyChangedHandler` (new files, `Source/WPF/MyMoney.Business/Utilities/`)

A full audit of every `EventHandlerCollection`-backed event across `MyMoney.Business` (see "Migration:
WPF call sites" below) found exactly two distinct delegate shapes with real, live subscribers needing
this wrapping — `EventHandler<ChangeEventArgs>` (`PersistentObject.Changed`,
`PersistentContainer.Changed`, `MyMoney.Rebalanced`) and `PropertyChangedEventHandler`
(`PersistentObject`'s actual `PropertyChanged` implementation). These are two concrete classes, not
one generic class: `PropertyChangedEventHandler` predates generic `EventHandler<T>` in .NET and isn't
assignable to/from `EventHandler<PropertyChangedEventArgs>` despite the matching signature, so a
single `UiThreadHandler<T>` couldn't cover both shapes without an awkward adapter. (A third
`EventHandlerCollection`-backed surface exists — `AsyncSqlQuery.Completed`,
`EventHandler<SqlQueryResultArgs>` — but `AsyncSqlQuery` has zero subscribers anywhere in the
solution; see below. No wrapper is needed for it.)

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

## Migration: WPF call sites

A complete audit of every `EventHandlerCollection`-backed event in `MyMoney.Business` — not just
`.Changed`, which an initial narrower grep had assumed was the only one — found four real event
surfaces:

- `PersistentContainer.Changed` / `PersistentObject.Changed` — `EventHandler<ChangeEventArgs>`.
- `MyMoney.Rebalanced` — `EventHandler<ChangeEventArgs>`, same shape as `Changed`, easy to miss by
  grepping for `.Changed` specifically.
- `PersistentObject.PropertyChanged` (the actual `INotifyPropertyChanged` implementation for
  `Account`/`Transaction`/etc.) — `PropertyChangedEventHandler`. Note this is *not* the same thing as
  WPF's own internal data-binding subscription to `PropertyChanged` (via
  `PropertyChangedEventManager`, a `WeakEventManager`/`DispatcherObject` — not a `DependencyObject`),
  which was never affected by the `DependencyObject` check in the first place and needs no migration;
  only *manual*, explicit `.PropertyChanged +=` subscriptions from genuinely `DependencyObject`-derived
  code are in scope.
- `AsyncSqlQuery.Completed` — `EventHandler<SqlQueryResultArgs>`. Confirmed via
  `grep -rln "AsyncSqlQuery" Source/WPF` to have **zero subscribers anywhere in the solution** — dead
  code. No migration needed; noted here only for completeness of the audit.

Grepping the WPF app for subscriptions to the first three surfaces (`.Changed +=`, `.Rebalanced +=`,
and `.PropertyChanged +=` where the *subscribing* class — not the event source — is genuinely
`DependencyObject`-derived) found **30 real subscriptions across 13 files**. Every `.PropertyChanged`
hit was individually checked against its enclosing class: e.g. `AccountsControl.xaml.cs`'s
`this.account.PropertyChanged += this.OnPropertyChanged;` looked like a match but its enclosing class,
`AccountItemViewModel`, is a plain (non-`DependencyObject`) view-model class with its own independent
`INotifyPropertyChanged` implementation, so that one specific subscription is correctly excluded.
Likewise `Settings`/`DatabaseSettings`/`OnlineServiceSettings`'s `PropertyChanged` subscriptions
(`OnlineServiceDialog.xaml.cs`, `MainWindow.xaml.cs`'s `databaseSettings`, `StockQuoteManager.cs`'s
`ss`) are unaffected regardless of subscriber type, since those classes implement
`INotifyPropertyChanged` directly and never go through `EventHandlerCollection` at all.

Of the real subscriptions, 7 are on plain (non-`DependencyObject`) classes and need **no change**,
since they already run synchronously today and this rewrite doesn't change that:

| File | Subscriptions |
|---|---|
| `Attachments/StatementManager.cs` | 2 |
| `Attachments/AttachmentManager.cs` | 3 |
| `StockQuotes/StockQuoteManager.cs` | 1 |
| `Views/ChangeTracker.cs` | 1 |

The remaining 30 subscriptions are on WPF `Window`/`UserControl`/`Border`/`TextBlock`-derived
(`DependencyObject`) targets and must migrate to `UiThreadHandler` (for `.Changed`/`.Rebalanced`) or
`UiThreadPropertyChangedHandler` (for `.PropertyChanged`) to keep today's UI-thread delivery:

| File | `.Changed` | `.Rebalanced` | `.PropertyChanged` | Total |
|---|---|---|---|---|
| `Views/TransactionsView.xaml.cs` (outer `Money` property) | 1 | — | — | 1 |
| `Views/TransactionsView.xaml.cs` (5 nested cell/field classes: `TransactionCell`, `TransactionAttachmentIcon`, `TransactionTextField`, `TransactionStatusButton`, `TransactionAmountControl`) | — | — | 10 | 10 |
| `Views/SecuritiesView.xaml.cs` | 1 | — | — | 1 |
| `Views/CurrenciesView.xaml.cs` | 1 | — | — | 1 |
| `Views/AliasesView.xaml.cs` | 2 | — | — | 2 |
| `View Selectors/AccountsControl.xaml.cs` | 2 | 1 | — | 3 |
| `View Selectors/BalanceControl.xaml.cs` | 1 | — | — | 1 |
| `View Selectors/PayeesControl.xaml.cs` | 2 | 1 | — | 3 |
| `View Selectors/CategoriesControl.xaml.cs` | 1 | — | — | 1 |
| `View Selectors/SecuritiesControl.xaml.cs` | 2 | 1 | — | 3 |
| `View Selectors/RentsControl.xaml.cs` | — | 1 | — | 1 |
| `Dialogs/AccountDialog.xaml.cs` | 1 | — | — | 1 |
| `Dialogs/RenamePayeeDialog.xaml.cs` | 1 | — | — | 1 |
| `MainWindow.xaml.cs` | 1 | — | — | 1 |
| **Total** | **16** | **4** | **10** | **30** |

`TransactionsView.xaml.cs`'s 5 nested classes (`TransactionCell : Border`,
`TransactionAttachmentIcon : Border`, `TransactionTextField : TextBlock`,
`TransactionStatusButton : Border`, `TransactionAmountControl : UserControl`) each follow the exact
same shape: a `private Transaction context;` field, an `OnLoaded`/`OnUnloaded` pair (subscribe on
load, unsubscribe on unload) and a `SetContext` method (unsubscribe from the old context, subscribe to
the new one) — 2-3 call sites per class, all referencing the same handler method
(`this.OnPropertyChanged` or `this.OnContextPropertyChanged` depending on the class).

### `TransactionPropertyChangeSubscription` (new file, `Source/WPF/MyMoney/Views/TransactionPropertyChangeSubscription.cs`)

Given the identical shape repeated 5 times above, extract a small composition helper rather than
migrating each class's subscribe/unsubscribe dance by hand 5 times over — more repetition of this
exact bookkeeping is more surface for the subscribe/unsubscribe-identity bug class this whole design
exists to prevent. A shared *base class* doesn't fit (the 5 classes have three different, unrelated
WPF base types — `Border`, `TextBlock`, `UserControl` — and C# has single inheritance, already spent);
composition does:

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

Each of the 5 classes gets one `private readonly TransactionPropertyChangeSubscription
propertyChangeSubscription = new TransactionPropertyChangeSubscription(this.OnPropertyChanged);` field
(or `this.OnContextPropertyChanged` for `TransactionAttachmentIcon`), and every existing
`if (this.context != null) { this.context.PropertyChanged -= this.OnPropertyChanged; }` /
`+= this.OnPropertyChanged` pair becomes `this.propertyChangeSubscription.Unsubscribe(this.context);` /
`this.propertyChangeSubscription.Subscribe(this.context);` — the `context` field itself, and every
other place that reads it, is untouched.

### Migration shape (non-`TransactionsView` sites)

Example from `AccountDialog.xaml.cs`:

```csharp
// Before:
money.Changed += new EventHandler<ChangeEventArgs>(this.OnMoneyChanged);

// After:
// Wrapped once in the ctor and reused for both subscribe and unsubscribe below - see
// UiThreadHandler's doc comment for why a bare re-wrap or a mismatched raw method group here would
// silently break unsubscription (a mistake a Roslyn analyzer could catch at compile time if this
// class of bug recurs - deferred for now, see issue #49).
this.onMoneyChangedUi = new UiThreadHandler(this.OnMoneyChanged);   // field, ctor-initialized
money.Changed += this.onMoneyChangedUi.Handler;
```

Include a short version of that comment (pointing back to `UiThreadHandler`'s or
`UiThreadPropertyChangedHandler`'s own doc comment rather than repeating it in full) at each of the 30
migrated call sites — not just this example — so a future reader at any one of them has the same
context without needing to already know to go look at `UiThreadHandler.cs`.

Any existing `-=` unsubscription for the same handler (check each site individually — not all
subscribe sites currently unsubscribe) must use the same wrapper field's `.Handler`, never the bare
original method group, or it will silently fail to unsubscribe (this is precisely the bug class this
design closes, but only for code written the new way).

**Two pre-existing anomalies found during the audit — migrate as-is, do not "fix" them** (out of scope
for this rewrite; behavior preservation is the goal):
- `Views/AliasesView.xaml.cs`'s constructor has an `Unloaded += (s, e) => { ... this.money.Changed +=
  this.OnMoneyChanged; ...}` — this reads like it should be `-=` (clean up on unload) but is actually
  `+=` today. Migrate the operator exactly as-is (`+=`, using the new wrapper's `.Handler`); do not
  silently change it to `-=` as part of this rewrite.
- `View Selectors/BalanceControl.xaml.cs`'s `this.myMoney.Transactions.Changed` subscription isn't in
  a property setter — it's inside a method that runs an unconditional `-=` immediately followed by
  `+=` every time that method is called (a defensive re-subscribe-without-duplicating pattern used
  repeatedly throughout that method for several unrelated events). The `UiThreadHandler` instance for
  this one must be a class-level field constructed once (not re-constructed inside that method), or
  each call would produce a different wrapper and the `-=`/`+=` pair would stop matching — exactly the
  bug class this whole design exists to avoid.

`RenamePayeeDialog.xaml.cs` already has an existing `readonly EventHandler<ChangeEventArgs> handler`
field, assigned once in the constructor (`this.handler = new EventHandler<ChangeEventArgs>(this.OnPayees_Changed);`)
and consistently reused for both subscribe (`+=`) and unsubscribe (`-=`) in the `MyMoney` property
setter — this is already exactly the "wrap once, reuse the same reference" shape this design needs.
Migrate by changing the field's declared type to `UiThreadHandler` and its initializer to
`new UiThreadHandler(this.OnPayees_Changed)`, then use `this.handler.Handler` at both the `+=` and
`-=` call sites in place of the bare field.

## Test changes

`Source/WPF/UnitTests/CrossThreadEventMarshalingTests.cs` (PR #48) must be updated in the **same
commit** that removes the `DependencyObject` check from `EventHandlerCollection` — never leave CI red
for an unaddressed reason (migration panelist's finding):

- `RaiseEvent_DependencyObjectListener_RunsOnDispatcherThreadNotCallingThread` — obsolete (asserts
  automatic `DependencyObject`-based marshaling, which no longer exists). Replace with
  `RaiseEvent_DependencyObjectListener_NoLongerAutoMarshals_RunsSynchronouslyOnCallingThread`: subscribe
  a raw `DependencyObject`-derived listener *directly* (no wrapper), and assert it now runs on the
  *calling* thread, not the dispatcher thread — the direct, meaningful proof that the automatic
  `DependencyObject` check is gone.
- `RaiseEvent_PlainListener_RunsSynchronouslyOnCallingThread` — still valid, now trivially true for
  *every* unwrapped listener (not just non-`DependencyObject` ones); keep as basic coverage of the
  base synchronous-by-default behavior.
- `RaiseEvent_FromThreadHoldingALockTheUiThreadWants_DoesNotDeadlock` — update its subscription to use
  `new UiThreadHandler(listener.OnChanged).Handler` instead of subscribing the raw `DependencyObject`
  listener directly (which would no longer be marshaled at all under the new design); the deadlock-
  avoidance property itself is unchanged, still `Post`-based, still non-blocking.
- **New**: promote the validation experiment's proof directly into permanent coverage —
  `UiThreadHandler_Unsubscribe_ActuallyStopsDelivery` (subscribe and unsubscribe via the same
  `.Handler` reference; listener must not fire) and `UiThreadHandler_StillMarshalsToUiThreadWhileSubscribed`
  (a companion sanity check that it still delivers while subscribed, ruling out a trivial "never
  delivers anything" implementation passing by accident). This is the actual load-bearing property of
  the new production API and must be a real regression test, not just a deleted experiment.

## `LayerBoundaryTests` (`Source/WPF/UnitTests/LayerBoundaryTests.cs`)

Once the rewrite is complete, tighten `MyMoneyBusiness_HasNoUiRenderingAssemblyReference` (or add a
new test) to check against `AllWpfAssemblyNames` (all three: `PresentationFramework`,
`PresentationCore`, `WindowsBase`) instead of just `UiRenderingAssemblyNames` — matching
`MyMoneyData_HasNoWpfAssemblyReference`'s existing pattern exactly. This is the concrete, automated
acceptance criterion proving issue #7's goal achieved; update the class-level doc comment
accordingly (it currently documents the WindowsBase allowance as intentional).

## Verification

- `dotnet build Source/WPF/MyMoney.sln` — clean.
- `dotnet test Source/WPF/UnitTests/UnitTests.csproj` — full pass, including the tightened
  `LayerBoundaryTests` (this is the test that would fail if any WPF-only type leaked back into
  `MyMoney.Business`) and the rewritten `CrossThreadEventMarshalingTests`.
- Full-solution regression matching this session's established baseline.
- **Manual interactive smoke pass** through the main app (`MyMoney.exe`), specifically exercising
  every migrated view/dialog (Accounts, Transactions, Securities, Currencies, Aliases, Categories,
  Payees, Rents, rename-payee dialog, account dialog) to confirm UI updates still arrive correctly
  after migration — this is the migration panelist's explicit completion gate and cannot be done by
  an agent without a real interactive desktop; flag this to the user as a manual step before merge.

## Known deferred items

- Roslyn analyzer for compile-time detection of a forgotten `UiThreadHandler`/
  `UiThreadPropertyChangedHandler` wrap (ergonomics panelist's proposal) — separate follow-up, not
  part of this work. Tracked as issue #49 (low priority).
