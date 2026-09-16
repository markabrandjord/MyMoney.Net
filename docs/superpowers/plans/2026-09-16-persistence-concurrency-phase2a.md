# Persistence Concurrency — Phase 2a (SaveOne/SaveTransfer/SaveBatch Contract + MockDatabase) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the new `IDatabase.SaveOne<T>`/`SaveTransfer`/`SaveBatch` write primitives (R2) to the
`IDatabase` contract, additively, and give them a real, fully-tested implementation for
`MockDatabase` — including genuine `RowVersion` optimistic-concurrency conflict detection (R4) — so
the contract itself is proven correct before the two real storage engines implement it for real.

**Architecture:** This is Phase 2 split into three parts, not one giant plan, because
`SaveOne`/`SaveTransfer`/`SaveBatch` need to land for three genuinely different engines
(`MockDatabase`, `SqliteDatabase`, `SqlServerStoredProcDatabase`), each a large, mostly-mechanical
but high-stakes retrofit in its own right (real transactions, per-row `RowVersion` WHERE clauses,
WAL/`busy_timeout` tuning). Splitting lets each land as its own reviewable, independently-testable
PR:

- **Phase 2a (this plan):** the `IDatabase` contract itself, plus a real `MockDatabase`
  implementation. Every other existing `IDatabase` implementation (`SqlServerDatabase` — the base
  class `SqliteDatabase`/`SqlCeDatabase`/`SqlServerStoredProcDatabase` all inherit from — plus
  `XmlStore`/`CsvStore`) gets a compiling stub that throws `NotImplementedException`, so the
  solution builds and nothing regresses. Nothing here touches `Save(MyMoney)` or any existing
  call site — purely additive, matching R1's phasing (retiring `Save(MyMoney)` is Phase 3).
- **Phase 2b (follow-on plan, not written yet):** real `SqliteDatabase` implementation — WAL mode,
  `busy_timeout`, per-row `RowVersion` WHERE clauses for all 13 tables `Save(MyMoney)` already
  writes.
- **Phase 2c (follow-on plan, not written yet):** real `SqlServerStoredProcDatabase`
  implementation — actual `BEGIN/END TRAN` spanning each primitive's stored-proc calls (closes
  issue #27), same per-row `RowVersion` WHERE clause via `ROWVERSION`/`@ExpectedRowVersion`.

**Key design fact this plan discovered and depends on:** every aggregate-root `PersistentObject`
sits inside a `PersistentContainer` whose own `.Parent` is the owning `MyMoney` (e.g.
`Account.Parent` is the `Accounts` container; `Accounts.Parent` is the `MyMoney` that owns it — see
`Money.cs:789`, `this.Accounts = new Accounts(this)`). So `root.Parent?.Parent as MyMoney` finds a
root's owning graph *generically*, for any aggregate-root type, with no per-type code — this is
what lets `MockDatabase`'s `SaveBatch` stay a single, type-agnostic method instead of a 13-way
dispatch. `Transaction.MyMoney` (`Money.cs:11450`) already does exactly this walk today, just with
a friendlier name on one type.

**Tech Stack:** .NET 10 (`net10.0-windows7.0`), NUnit 4.6.1. No new external dependencies.

**Spec:** `docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md` (R2's three
primitives, R3's atomicity framing, R4's `RowVersion`/`ConcurrencyConflictException` contract —
Phase 2a implements R4's *detection* mechanism for one engine; R3's *real* per-engine atomicity is
Phase 2b/2c). Builds on `docs/superpowers/plans/2026-09-16-persistence-concurrency-phase1.md`
(already merged — added `PersistentObject.RowVersion`, the per-flavor `RowVersion`/`Version` schema
column, and FK/index auto-derivation).

## Global Constraints

- Additive only: `IDatabase.Save(MyMoney money)` and every existing `UpdateXxx`/`ReadXxx` override
  are untouched. No UI/business call site is rewired in this plan — that is Phase 3 (R1).
- Every `IDatabase` implementation must still compile after Task 1: `SqlServerDatabase` (and its
  subclasses `SqliteDatabase`/`SqlCeDatabase`/`SqlServerStoredProcDatabase`, via inheritance),
  `XmlStore`, `CsvStore`, and `MockDatabase` each need a body for the three new interface members,
  even if that body is just `throw new NotImplementedException(...)` for now.
- `Split`/`Investment` (owned by `Transaction`) and `RentUnit` (owned by `RentBuilding`, following
  exactly the same owned-child shape as `Split`/`Transaction` — confirmed via
  `Save(MyMoney)`'s `UpdateBuildings` calling `UpdateRentUnits`+`UpdateRentBuildings` as one unit,
  the same pattern `UpdateTransactions` uses for `Split`) never implement `IAggregateRoot`. They
  only ever commit as part of their owning aggregate's `SaveOne`/`SaveBatch` call.
- `AccountAlias`/`TransactionExtra` are deliberately **excluded** from `IAggregateRoot` in this
  plan even though `Save(MyMoney)` also calls `UpdateAccountAliases`/`UpdateTransactionExtras` for
  them — the design spec's Group-1 aggregate-root list (Background section) never names them.
  Adding `IAggregateRoot` to a type later costs nothing (it's a zero-behavior marker), so this is a
  safe deferral, not a real gap; revisit only if Phase 3's call-site wiring needs them.
- Every task must leave `dotnet build Source/WPF/MyMoney.sln` at 0 errors and
  `dotnet test Source/WPF/MyMoney.sln -m:1` green.

---

### Task 1: Add the `SaveOne`/`SaveTransfer`/`SaveBatch` contract to `IDatabase`

**Files:**
- Modify: `Source/WPF/MyMoney.Business/IDatabase.cs`
- Modify: `Source/WPF/MyMoney.Business/Money.cs` (10 aggregate-root class declarations, plus a new
  `ConcurrencyConflictException` near the existing `MoneyException`/`TransactionException`)
- Modify: `Source/WPF/MyMoney.Business/Money_Loans.cs` (`LoanPayment` class declaration)
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (`SqlServerDatabase` — stub only)
- Modify: `Source/WPF/MyMoney.Data/XmlStore.cs` (stub only)
- Modify: `Source/WPF/MyMoney.Data/CsvStore.cs` (stub only)
- Modify: `Source/WPF/MyMoney.TestSupport/MockDatabase.cs` (stub only — `MockDatabase` implements
  `IDatabase` directly, not via `SqlServerDatabase`, so it needs its own stub or the solution
  won't build after this task; Task 2 replaces these three stub bodies with the real
  implementation)
- Test: `Source/WPF/UnitTests/DataTests.cs`

**Interfaces:**
- Consumes: `PersistentObject`/`PersistentObject.RowVersion` (Phase 1, `Money.cs:389`/`:468`).
- Produces: `IAggregateRoot` (`Money.cs`... actually declared in `IDatabase.cs` — a marker
  interface with one member, `long Id { get; }`), `IDatabase.SaveOne<T>(T root) where T :
  PersistentObject, IAggregateRoot`, `IDatabase.SaveTransfer(Transaction from, Transaction to)`,
  `IDatabase.SaveBatch(IEnumerable<PersistentObject> roots)`, `ConcurrencyConflictException(
  PersistentObject root, long expectedRowVersion, long actualRowVersion)` — Task 2 (`MockDatabase`)
  and the not-yet-written Phase 2b/2c plans all implement against these exact signatures.

- [ ] **Step 1: Write the failing test**

Add to `Source/WPF/UnitTests/DataTests.cs`, right after the existing
`PersistentObject_RowVersion_DefaultsToZeroAndIsSettable` test (around line 182), and add
`using System;` to the file's existing using block (needed for `Type`):

```csharp
        [Test]
        public void AggregateRootTypes_ImplementIAggregateRoot()
        {
            Type[] expectedRoots =
            {
                typeof(Account), typeof(Category), typeof(Payee), typeof(Currency),
                typeof(Security), typeof(Alias), typeof(OnlineAccount), typeof(StockSplit),
                typeof(RentBuilding), typeof(LoanPayment), typeof(Transaction)
            };

            foreach (Type t in expectedRoots)
            {
                Assert.That(typeof(IAggregateRoot).IsAssignableFrom(t), Is.True,
                    t.Name + " should implement IAggregateRoot");
            }

            // Owned children never get their own commit boundary - only their owning
            // aggregate's SaveOne/SaveBatch call commits them.
            Assert.That(typeof(IAggregateRoot).IsAssignableFrom(typeof(Split)), Is.False);
            Assert.That(typeof(IAggregateRoot).IsAssignableFrom(typeof(Investment)), Is.False);
            Assert.That(typeof(IAggregateRoot).IsAssignableFrom(typeof(RentUnit)), Is.False);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~AggregateRootTypes_ImplementIAggregateRoot"`
Expected: FAIL with a compile error — `IAggregateRoot` doesn't exist yet.

- [ ] **Step 3: Add `IAggregateRoot` and the three new `IDatabase` members**

In `Source/WPF/MyMoney.Business/IDatabase.cs`, change:

```csharp
using System.Data;
using Walkabout.Utilities;

namespace Walkabout.Data
{
```

to:

```csharp
using System.Collections.Generic;
using System.Data;
using Walkabout.Utilities;

namespace Walkabout.Data
{
```

Then insert the new marker interface right after the `DbFlavor` enum's closing brace (before the
`/// <summary>\n    /// Interface for talking to different types of money storage.` comment):

```csharp
    /// <summary>
    /// Marker for PersistentObject types that own their own commit boundary - a whole database
    /// row of their own - and are therefore valid targets of IDatabase.SaveOne/SaveTransfer/
    /// SaveBatch. Owned children (Split/Investment owned by Transaction; RentUnit owned by
    /// RentBuilding) never implement this - they only ever commit as part of their owning
    /// aggregate's SaveOne/SaveBatch call. See
    /// docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R2.
    /// </summary>
    public interface IAggregateRoot
    {
        long Id { get; }
    }

```

Then change:

```csharp
        bool Exists { get; }
        void Create();
        MyMoney Load(IStatusService status);
        void Save(MyMoney money);
        void Backup(string path);
```

to:

```csharp
        bool Exists { get; }
        void Create();
        MyMoney Load(IStatusService status);
        void Save(MyMoney money);

        /// <summary>
        /// Atomically commit a single aggregate root (e.g. Account, Category, Transaction with
        /// its owned Splits/Investment). Implementations throw ConcurrencyConflictException if
        /// root.RowVersion no longer matches what's actually committed. See the design spec's
        /// R2/R3/R4.
        /// </summary>
        void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot;

        /// <summary>
        /// Atomically commit exactly two peer Transactions that must co-commit together (the
        /// TransformTwoTransactionIntoTransfer shape). See the design spec's R2.
        /// </summary>
        void SaveTransfer(Transaction from, Transaction to);

        /// <summary>
        /// Atomically commit a heterogeneous batch of aggregate roots together (reconciliation,
        /// merges, recategorize, and first-time population of a brand-new database). See the
        /// design spec's R2.
        /// </summary>
        void SaveBatch(IEnumerable<PersistentObject> roots);

        void Backup(string path);
```

- [ ] **Step 4: Tag the 11 aggregate-root classes with `IAggregateRoot`**

In `Source/WPF/MyMoney.Business/Money.cs`, make these 10 exact one-line changes (each class
already has a public `int Id { get; ... }` property today, so no new members are needed — this
is purely a marker):

| Line | From | To |
|---|---|---|
| 2640 | `public class Account : PersistentObject` | `public class Account : PersistentObject, IAggregateRoot` |
| 3476 | `public class OnlineAccount : PersistentObject` | `public class OnlineAccount : PersistentObject, IAggregateRoot` |
| 4376 | `public class Currency : PersistentObject` | `public class Currency : PersistentObject, IAggregateRoot` |
| 5172 | `public class Payee : PersistentObject` | `public class Payee : PersistentObject, IAggregateRoot` |
| 5290 | `public class Alias : PersistentObject` | `public class Alias : PersistentObject, IAggregateRoot` |
| 6031 | `public class RentBuilding : PersistentObject` | `public class RentBuilding : PersistentObject, IAggregateRoot` |
| 7648 | `public class Category : PersistentObject` | `public class Category : PersistentObject, IAggregateRoot` |
| 8720 | `public class Security : PersistentObject` | `public class Security : PersistentObject, IAggregateRoot` |
| 10788 | `public class Transaction : PersistentObject` | `public class Transaction : PersistentObject, IAggregateRoot` |
| 15027 | `public class StockSplit : PersistentObject` | `public class StockSplit : PersistentObject, IAggregateRoot` |

In `Source/WPF/MyMoney.Business/Money_Loans.cs:464`, change:

```csharp
    public class LoanPayment : PersistentObject
```

to:

```csharp
    public class LoanPayment : PersistentObject, IAggregateRoot
```

- [ ] **Step 5: Add `ConcurrencyConflictException`**

In `Source/WPF/MyMoney.Business/Money.cs`, change:

```csharp
    public class MoneyException : Exception
    {
        public MoneyException(string message)
            : base(message)
        {
        }

    }

}
```

to:

```csharp
    public class MoneyException : Exception
    {
        public MoneyException(string message)
            : base(message)
        {
        }

    }

    /// <summary>
    /// Thrown by IDatabase.SaveOne/SaveTransfer/SaveBatch when a root's RowVersion no longer
    /// matches what's actually committed - i.e. someone else wrote this row since it was loaded.
    /// See docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R4.
    /// </summary>
    public class ConcurrencyConflictException : Exception
    {
        public PersistentObject Root { get; }
        public long ExpectedRowVersion { get; }
        public long ActualRowVersion { get; }

        public ConcurrencyConflictException(PersistentObject root, long expectedRowVersion, long actualRowVersion)
            : base(string.Format(
                "Concurrency conflict saving {0} (Id={1}): expected RowVersion {2} but the caller had {3}.",
                root.GetType().Name, ((IAggregateRoot)root).Id, expectedRowVersion, actualRowVersion))
        {
            this.Root = root;
            this.ExpectedRowVersion = expectedRowVersion;
            this.ActualRowVersion = actualRowVersion;
        }
    }

}
```

- [ ] **Step 6: Stub the three methods on every other `IDatabase` implementation**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, change (around line 736-741):

```csharp
        #endregion


        public void UpdateBuildings(RentBuildings buildings)
```

to:

```csharp
        #endregion

        public virtual void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            throw new NotImplementedException(string.Format(
                "SaveOne is not yet implemented for {0} - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.", this.DbFlavor));
        }

        public virtual void SaveTransfer(Transaction from, Transaction to)
        {
            throw new NotImplementedException(string.Format(
                "SaveTransfer is not yet implemented for {0} - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.", this.DbFlavor));
        }

        public virtual void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            throw new NotImplementedException(string.Format(
                "SaveBatch is not yet implemented for {0} - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.", this.DbFlavor));
        }

        public void UpdateBuildings(RentBuildings buildings)
```

(`System.Collections.Generic` and `System` are already imported at the top of this file — no
using changes needed here.) `SqliteDatabase`, `SqlCeDatabase`, and `SqlServerStoredProcDatabase`
all inherit this stub automatically since none of them currently override `Save`/define their own
`SaveOne`/`SaveTransfer`/`SaveBatch`.

In `Source/WPF/MyMoney.Data/XmlStore.cs`, change (around line 120-128):

```csharp
        public virtual DataSet QueryDataSet(string cmd)
        {
            DataSet result = new DataSet();
            result.ReadXml(this.filename);
            return result;
        }


        public virtual void Save(MyMoney money)
```

to:

```csharp
        public virtual DataSet QueryDataSet(string cmd)
        {
            DataSet result = new DataSet();
            result.ReadXml(this.filename);
            return result;
        }

        public virtual void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            throw new NotImplementedException("XmlStore does not support SaveOne - see the design spec's Non-goals.");
        }

        public virtual void SaveTransfer(Transaction from, Transaction to)
        {
            throw new NotImplementedException("XmlStore does not support SaveTransfer - see the design spec's Non-goals.");
        }

        public virtual void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            throw new NotImplementedException("XmlStore does not support SaveBatch - see the design spec's Non-goals.");
        }


        public virtual void Save(MyMoney money)
```

`XmlStore.cs` already imports `System`/`System.Collections.Generic` — no using changes needed.

In `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`, change:

```csharp
        public void Save(MyMoney money)
        {
            this.snapshot = Serialize(money);
        }
```

to:

```csharp
        public void Save(MyMoney money)
        {
            this.snapshot = Serialize(money);
        }

        public void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            throw new NotImplementedException("SaveOne is not yet implemented for MockDatabase - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.");
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            throw new NotImplementedException("SaveTransfer is not yet implemented for MockDatabase - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.");
        }

        public void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            throw new NotImplementedException("SaveBatch is not yet implemented for MockDatabase - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.");
        }
```

Add `using System;` and `using System.Collections.Generic;` to `MockDatabase.cs`'s existing using
block (it currently only has `System.Data`/`System.IO`/`System.Runtime.Serialization`/`System.Xml`/
`Walkabout.Utilities`) — Task 2 needs both anyway, so add them here.

In `Source/WPF/MyMoney.Data/CsvStore.cs`, change:

```csharp
using System;
using System.Collections;
using System.Data;
```

to:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
```

and change:

```csharp
        public MyMoney Load(IStatusService status)
        {
            throw new NotImplementedException();
        }

        public void Save(MyMoney money)
```

to:

```csharp
        public MyMoney Load(IStatusService status)
        {
            throw new NotImplementedException();
        }

        public void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            throw new NotImplementedException("CsvStore does not support SaveOne - it is a write-only export format.");
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            throw new NotImplementedException("CsvStore does not support SaveTransfer - it is a write-only export format.");
        }

        public void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            throw new NotImplementedException("CsvStore does not support SaveBatch - it is a write-only export format.");
        }

        public void Save(MyMoney money)
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~AggregateRootTypes_ImplementIAggregateRoot"`
Expected: PASS.

- [ ] **Step 8: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors (this is the real proof every
`IDatabase` implementation still compiles with the three new members).
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green. Nothing calls the new methods yet,
so no behavior anywhere should change.

- [ ] **Step 9: Commit**

```bash
git add Source/WPF/MyMoney.Business/IDatabase.cs Source/WPF/MyMoney.Business/Money.cs Source/WPF/MyMoney.Business/Money_Loans.cs Source/WPF/MyMoney.Data/SqlDatabase.cs Source/WPF/MyMoney.Data/XmlStore.cs Source/WPF/MyMoney.Data/CsvStore.cs Source/WPF/UnitTests/DataTests.cs
git commit -m "Add SaveOne/SaveTransfer/SaveBatch to the IDatabase contract (additive, R2)"
```

---

### Task 2: Implement real `SaveOne`/`SaveTransfer`/`SaveBatch` for `MockDatabase`

**Files:**
- Modify: `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`
- Test: `Source/WPF/MyMoney.TestSupport/MockDatabaseContractTests.cs`

**Interfaces:**
- Consumes: `IAggregateRoot`, `ConcurrencyConflictException`, `IDatabase.SaveOne<T>`/
  `SaveTransfer`/`SaveBatch` (Task 1); `PersistentObject.Parent`/`PersistentContainer.Parent`
  (existing, `Money.cs:458`/`:170`) for the generic owning-graph walk.
- Produces: a working reference implementation of the three primitives against which Phase 2b/2c
  can be judged for behavioral parity (same conflict-detection semantics, same atomicity-on-error
  guarantee) even though their mechanism (real SQL transactions vs. one in-memory dictionary) is
  completely different.

- [ ] **Step 1: Write the failing tests**

Add to `Source/WPF/MyMoney.TestSupport/MockDatabaseContractTests.cs` (it's already a concrete,
non-abstract `[TestFixture]`, so new `[Test]` methods can go directly on it):

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    [TestFixture]
    public class MockDatabaseContractTests : DatabaseContractTests
    {
        protected override IDatabase CreateDatabase()
        {
            return new MockDatabase();
        }

        private static MyMoney BuildOneAccountMoney(out Account account)
        {
            MyMoney money = new MyMoney();
            account = money.Accounts.AddAccount("Checking");
            return money;
        }

        [Test]
        public void SaveOne_NewAccount_PersistsAndSetsRowVersionToOne()
        {
            MyMoney money = BuildOneAccountMoney(out Account account);

            this.Database.SaveOne(account);

            Assert.That(account.RowVersion, Is.EqualTo(1));
            Assert.That(account.IsInserted, Is.False);
            Assert.That(account.IsChanged, Is.False);

            MyMoney reloaded = this.Database.Load(null);
            Account found = reloaded.Accounts.FindAccount("Checking");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void SaveOne_UpdateAfterReload_IncrementsRowVersion()
        {
            MyMoney money = BuildOneAccountMoney(out Account account);
            this.Database.SaveOne(account);

            MyMoney reloaded = this.Database.Load(null);
            Account found = reloaded.Accounts.FindAccount("Checking");
            found.Description = "Updated";
            this.Database.SaveOne(found);

            Assert.That(found.RowVersion, Is.EqualTo(2));

            MyMoney reloadedAgain = this.Database.Load(null);
            Account foundAgain = reloadedAgain.Accounts.FindAccount("Checking");
            Assert.That(foundAgain.Description, Is.EqualTo("Updated"));
            Assert.That(foundAgain.RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void SaveOne_StaleRowVersion_ThrowsConcurrencyConflictException()
        {
            MyMoney money = BuildOneAccountMoney(out Account account);
            this.Database.SaveOne(account);

            // Two independent readers both load the same committed row.
            MyMoney readerA = this.Database.Load(null);
            MyMoney readerB = this.Database.Load(null);

            Account accountA = readerA.Accounts.FindAccount("Checking");
            accountA.Description = "From A";
            this.Database.SaveOne(accountA);

            Account accountB = readerB.Accounts.FindAccount("Checking");
            accountB.Description = "From B";
            Assert.Throws<ConcurrencyConflictException>(() => this.Database.SaveOne(accountB));

            // A's write must still be the one that stuck.
            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Accounts.FindAccount("Checking").Description, Is.EqualTo("From A"));
        }

        [Test]
        public void SaveTransfer_CommitsBothTransactionsTogether()
        {
            MyMoney money = new MyMoney();
            Account checking = money.Accounts.AddAccount("Checking");
            Account savings = money.Accounts.AddAccount("Savings");
            Transaction from = money.Transactions.NewTransaction(checking);
            from.Amount = -100m;
            money.Transactions.AddTransaction(from);
            Transaction to = money.Transactions.NewTransaction(savings);
            to.Amount = 100m;
            money.Transactions.AddTransaction(to);

            this.Database.SaveTransfer(from, to);

            Assert.That(from.RowVersion, Is.EqualTo(1));
            Assert.That(to.RowVersion, Is.EqualTo(1));

            MyMoney reloaded = this.Database.Load(null);
            Assert.That(reloaded.Transactions.GetTransactionsFrom(reloaded.Accounts.FindAccount("Checking")).Count, Is.EqualTo(1));
            Assert.That(reloaded.Transactions.GetTransactionsFrom(reloaded.Accounts.FindAccount("Savings")).Count, Is.EqualTo(1));
        }

        [Test]
        public void SaveBatch_OneStaleRootAmongMany_CommitsNoneOfThem()
        {
            MyMoney money = new MyMoney();
            Account a = money.Accounts.AddAccount("A");
            Account b = money.Accounts.AddAccount("B");
            this.Database.SaveBatch(new PersistentObject[] { a, b });

            MyMoney reader = this.Database.Load(null);
            Account staleA = reader.Accounts.FindAccount("A");
            Account staleB = reader.Accounts.FindAccount("B");

            // Someone else updates A first, so staleA's RowVersion (1) is now behind.
            MyMoney otherWriter = this.Database.Load(null);
            Account freshA = otherWriter.Accounts.FindAccount("A");
            freshA.Description = "Changed elsewhere";
            this.Database.SaveOne(freshA);

            staleA.Description = "Attempted A";
            staleB.Description = "Attempted B";
            Assert.Throws<ConcurrencyConflictException>(
                () => this.Database.SaveBatch(new PersistentObject[] { staleA, staleB }));

            MyMoney reloaded = this.Database.Load(null);
            // B must NOT have been committed even though only A conflicted.
            Assert.That(reloaded.Accounts.FindAccount("B").Description, Is.Not.EqualTo("Attempted B"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~MockDatabaseContractTests"`
Expected: FAIL — Task 1 left `MockDatabase.SaveOne`/`SaveTransfer`/`SaveBatch` throwing
`NotImplementedException`, so all 5 new tests fail with that exception (the pre-existing
`DatabaseContractTests` tests in this same fixture still pass, since they only exercise
`Save`/`Load`, untouched so far).

- [ ] **Step 3: Implement `SaveOne`/`SaveTransfer`/`SaveBatch` and fix `Load`/`Delete`**

In `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`, first add the version-tracking field. Change:

```csharp
    public class MockDatabase : IDatabase
    {
        private byte[] snapshot;
```

to:

```csharp
    public class MockDatabase : IDatabase
    {
        private byte[] snapshot;

        // RowVersion is [XmlIgnore] (persistence-concurrency Phase 1), so it never round-trips
        // through the DataContractSerializer snapshot below. This is MockDatabase's own
        // last-committed-version bookkeeping, restored onto each object by Load() (see
        // RestoreRowVersions) so a later SaveOne/SaveBatch call has something real to compare
        // against. Key is (root's concrete type, its Id) since Id alone isn't unique across
        // tables (an Account and a Category can share Id=1).
        private readonly Dictionary<(Type RootType, long Id), long> committedVersions = new Dictionary<(Type RootType, long Id), long>();
```

Change (replacing the three stub bodies Task 1 added, right after `Save(MyMoney)`):

```csharp
        public void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            throw new NotImplementedException("SaveOne is not yet implemented for MockDatabase - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.");
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            throw new NotImplementedException("SaveTransfer is not yet implemented for MockDatabase - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.");
        }

        public void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            throw new NotImplementedException("SaveBatch is not yet implemented for MockDatabase - see docs/superpowers/plans/2026-09-16-persistence-concurrency-phase2a.md.");
        }
```

to:

```csharp
        public void SaveOne<T>(T root) where T : PersistentObject, IAggregateRoot
        {
            this.SaveBatch(new PersistentObject[] { root });
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            this.SaveBatch(new PersistentObject[] { from, to });
        }

        public void SaveBatch(IEnumerable<PersistentObject> roots)
        {
            List<PersistentObject> list = new List<PersistentObject>(roots);
            if (list.Count == 0)
            {
                return;
            }

            // Validate every root against its last-committed version before mutating anything,
            // so a conflict on root N of N leaves nothing committed (R3's atomicity requirement).
            foreach (PersistentObject root in list)
            {
                if (!(root is IAggregateRoot identity))
                {
                    throw new ArgumentException(string.Format(
                        "{0} is not a valid SaveBatch root - it must implement IAggregateRoot.", root.GetType().Name));
                }

                long committed = this.committedVersions.TryGetValue((root.GetType(), identity.Id), out long v) ? v : 0;
                if (committed != root.RowVersion)
                {
                    throw new ConcurrencyConflictException(root, committed, root.RowVersion);
                }
            }

            // MockDatabase has no per-row storage, so it re-serializes the whole owning graph on
            // every call - unlike SqliteDatabase/SqlServerStoredProcDatabase (Phase 2b/2c), which
            // only ever touch the given root(s)' own rows. This is a deliberate simplification
            // for a test double (see
            // docs/superpowers/specs/2026-09-16-persistence-concurrency-design.md, R2's exemption
            // of MockDatabase from real transaction semantics): MockDatabase alone cannot catch a
            // caller relying on unrelated dirty state getting flushed along with it - that check
            // belongs to the real-engine contract tests landing in Phase 2b/2c, not here.
            MyMoney owner = list[0].Parent?.Parent as MyMoney;
            if (owner == null)
            {
                throw new InvalidOperationException("SaveBatch root is not attached to a loaded MyMoney graph.");
            }
            this.Save(owner);

            foreach (PersistentObject root in list)
            {
                IAggregateRoot identity = (IAggregateRoot)root;
                (Type, long) key = (root.GetType(), identity.Id);
                if (root.IsDeleted)
                {
                    this.committedVersions.Remove(key);
                }
                else
                {
                    long newVersion = root.RowVersion + 1;
                    this.committedVersions[key] = newVersion;
                    root.RowVersion = newVersion;
                    root.OnUpdated();
                }
            }
        }
```

Change:

```csharp
        public MyMoney Load(IStatusService status)
        {
            if (this.snapshot == null)
            {
                return new MyMoney();
            }
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
            using (MemoryStream stream = new MemoryStream(this.snapshot))
            using (XmlReader reader = XmlReader.Create(stream))
            {
                MyMoney money = (MyMoney)serializer.ReadObject(reader);
                money.PostDeserializeFixup();
                money.OnLoaded();
                return money;
            }
        }
```

to:

```csharp
        public MyMoney Load(IStatusService status)
        {
            if (this.snapshot == null)
            {
                return new MyMoney();
            }
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
            using (MemoryStream stream = new MemoryStream(this.snapshot))
            using (XmlReader reader = XmlReader.Create(stream))
            {
                MyMoney money = (MyMoney)serializer.ReadObject(reader);
                money.PostDeserializeFixup();
                money.OnLoaded();
                this.RestoreRowVersions(money);
                return money;
            }
        }

        private void RestoreRowVersions(MyMoney money)
        {
            this.ApplyVersions(money.Accounts);
            this.ApplyVersions(money.Categories);
            this.ApplyVersions(money.Payees);
            this.ApplyVersions(money.Currencies);
            this.ApplyVersions(money.Securities);
            this.ApplyVersions(money.Aliases);
            this.ApplyVersions(money.OnlineAccounts);
            this.ApplyVersions(money.StockSplits);
            this.ApplyVersions(money.Buildings);
            this.ApplyVersions(money.LoanPayments);
            this.ApplyVersions(money.Transactions);
        }

        private void ApplyVersions(IEnumerable<PersistentObject> roots)
        {
            foreach (PersistentObject root in roots)
            {
                if (root is IAggregateRoot identity &&
                    this.committedVersions.TryGetValue((root.GetType(), identity.Id), out long version))
                {
                    root.RowVersion = version;
                }
            }
        }
```

Change:

```csharp
        public void Delete()
        {
            this.snapshot = null;
        }
```

to:

```csharp
        public void Delete()
        {
            this.snapshot = null;
            this.committedVersions.Clear();
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "FullyQualifiedName~MockDatabaseContractTests"`
Expected: PASS — all 5 new tests plus every pre-existing `DatabaseContractTests` test (still
exercising the untouched `Save(MyMoney)`/`Load` path).

- [ ] **Step 5: Full build and regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` — expect 0 errors.
Run: `dotnet test Source/WPF/MyMoney.sln -m:1` — expect green, including
`MockDatabaseSmokeTests` and every other consumer of `MockDatabase` (none of them call the new
methods, so `Load`'s added `RestoreRowVersions` call — a no-op when `committedVersions` is empty —
is the only path that touches existing behavior; confirm nothing regresses).

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.TestSupport/MockDatabase.cs Source/WPF/MyMoney.TestSupport/MockDatabaseContractTests.cs
git commit -m "Implement SaveOne/SaveTransfer/SaveBatch with real RowVersion conflict detection for MockDatabase"
```

---

## What's next

Phase 2a stops at `MockDatabase` deliberately — it proves the `IDatabase` contract (shapes,
exception semantics, atomicity-on-conflict behavior) is right before committing two real storage
engines to it. **Phase 2b** (not yet planned) gives `SqliteDatabase` a real implementation: WAL
mode + `busy_timeout` (neither set today — confirmed via `SqliteDatabase.Connect()`, which only
sets `PRAGMA foreign_keys = ON`), and per-row `RowVersion` WHERE clauses added to all 13 tables'
insert/update/delete SQL (`SqlServerDatabase.UpdateAccounts` et al., inherited by `SqliteDatabase`
today with no version check at all). Once Phase 2b lands, the five new tests in
`MockDatabaseContractTests` should be promoted into the shared `DatabaseContractTests` base class
so `SqliteDatabaseContractTests` inherits and re-runs them for real. **Phase 2c** (not yet planned)
does the same for `SqlServerStoredProcDatabase`, additionally wrapping each primitive's
stored-proc calls in one real `SqlTransaction` (closing issue #27 — today's `UpdateAccounts`
override there opens a brand-new `SqlConnection` per call with no transaction at all). Phase 3
(already scoped in the Phase 1 plan) wires the ~20 real UI/business call sites to whichever of
these three engines they're pointed at, and only then retires `IDatabase.Save(MyMoney)`.
