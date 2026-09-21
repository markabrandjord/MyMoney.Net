# Data-Layer Foundation — Plan A (the SQLite vertical) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provision a SQLite database from a versioned schema ledger and add an `Account` to it headlessly — no UI — with the optimistic-concurrency conflict/retry path actually tested end to end.

**Architecture:** `IDatabase` is split along the privilege axis into three new ports — `IMoneyStore` (everyday writes + root reads), `IMoneyQuery` (projection reads), `IMoneyStoreProvisioner` (schema/backup/delete) — plus a never-shipped `IMoneyStoreTestControl`. Each tier of the SQLite engine becomes its own assembly, so privilege separation is *"the destructive code is not in the shipped bits"* rather than *"the destructive code is politely hidden."* Schema management becomes one versioned, checksummed, per-step-transactional executor (`ApplyTo`/`CurrentVersion`/`Verify`/`DropAll`) over a `__SchemaHistory` ledger, so creating a database, nuking-and-paving it, and the eventual production upgrade are literally the same operation started from different versions.

**Tech Stack:** C# / .NET 10 (`net10.0-windows7.0`), WPF-free class libraries, `System.Data.SQLite` 2.0.4 over `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 (SQLite engine 3.53.4), NUnit 4.6.1 + NUnit3TestAdapter 6.2.0, MSBuild `Directory.Build.targets`.

**Spec:** `docs/superpowers/specs/2026-09-20-data-layer-architecture-brainstorm.md` (revision 12). Plan A implements **slices 1 through 7** of that document's §5 table. Slices 8–10 (SQL Server parity, the T-1 wall-time gate, the `json_each` benchmark) are **Plan B and are out of scope here**.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Target framework for every new project: `net10.0-windows7.0`.** Not plain `net10.0` — `MyMoney.Business` is `net10.0-windows7.0`, and a `net10.0` project cannot reference a `net10.0-windows7.0` project.
- **`IAggregateRoot.Id` stays `long`.** Non-negotiable, already load-bearing (spec §5). A fresh schema must not narrow it to `INT`.
- **`Save(MyMoney)` and whole-graph `Load()` do not exist on any new port.** Revision 7: *"The query-based pattern replaces the whole-graph-in-memory pattern everywhere."*
- **R-CRUD-1 — every store write is parameterized.** No SQL built by string concatenation with a *value* in it. Table/column **identifiers** cannot be parameterized in any SQL dialect; where an identifier must be interpolated, it must first be validated against the live introspected object set and quoted with `"`, and any name containing `"` rejected.
- **R-CRUD-2 — every store write is atomic at the call boundary**, including its in-memory side effects. The `postCommitActions` deferral (nothing touches `RowVersion` or calls `OnUpdated()` until `Commit()` has returned) is carried forward verbatim from `SqliteDatabase.SaveBatch`.
- **R-CRUD-3 — tabular input is one statement, one transaction.** On SQLite that is `json_each` (spec §1.7.1, "adopt").
- **R-CRUD-4 — the conflict contract is engine-independent.** Zero rows affected by a version-checked `UPDATE`/`DELETE` ⇒ `ConcurrencyConflictException` carrying the store's actual current version.
- **R-CRUD-5 — everything the schema owns is a numbered step**: tables, columns, indexes, foreign keys, views.
- **S-3.1 — a step is never edited once applied anywhere.** `ChecksumHash` is populated **and checked** from the first commit, not "when it matters."
- **Tables are `STRICT`** (spec §1.7.1, "Adopt. Strong recommendation").
- **Money is stored as `INTEGER` ten-thousandths**, never `REAL` and never `TEXT`. This is exactly SQL Server's `money` representation (an `int64` of ten-thousandths, max 922,337,203,685,477.5807 = `Int64.MaxValue` / 10000), so it is exact, SQL-summable on both engines, and satisfies §6.8 rule 1 ("adopted on one engine must be matched on the other").
- **`DateTime` is stored as `TEXT`** in invariant `yyyy-MM-dd HH:mm:ss.fff` (lexicographically sortable, millisecond resolution matching SQL Server `datetime`). `Guid` as `TEXT` in `"D"` format.
- **Views never use `SELECT *`** (resolves at prepare time on SQLite, at create time on SQL Server — §2.7.1). Plan A creates no views; the rule is stated because slice-later steps will.
- **No `INSTEAD OF` triggers anywhere** (§1.7.1 as amended by revision 4; §2.7.3). Enforced by a Tier-2 test in Task 22.
- **No production assembly declares `InternalsVisibleTo`** (§6.2). If a test needs to reach something, the tier boundary was drawn wrong — move it, don't punch through it.
- **No production assembly references `*.TestKit*` or `*.TestTier*`** (§2.5). Enforced at build time *and* by a Tier-0 test.
- **`MyMoney.Tests.Business` may not reference the WPF UI project** (`MyMoney.csproj`) — §2.1 #2.
- **The existing shipped app is not modified.** The only permitted touches to existing files are: adding new projects to `Source/WPF/MyMoney.sln`, and adding a single `<IsProductionAssembly>true</IsProductionAssembly>` MSBuild property to `MyMoney.csproj`, `MyMoney.Business.csproj` and `MyMoney.Data.csproj` in Task 22. No existing C# file is edited by this plan.
- **Commit after every task.** Commit messages end with the line:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  ```
- **Branch:** all work lands on `rebuild/data-layer-foundation`.
- **Honest-claim rule (§6.1):** no task, comment, or commit message may claim the SQLite tier split gives "the same security as SQL Server roles." What it gives is: the destructive code is not in the shipped assembly set; production code cannot name the type; a build and a test fail if either becomes untrue. Nothing more.

---

## Decisions this plan makes that the spec deliberately left to implementation

The spec's revision-12 self-review named three loose ends and routed them here. All three are resolved below, with reasoning, before any task depends on them.

### D-1. Where `IMoneyQuery` lands, and what slice 3's read goes through

**The gap (spec §5, "One gap in this table"):** no slice implements `IMoneyQuery`, yet revision 7 made it the whole application's read path and revision 11 ruled out any SQL Server carve-out. §6.4 requires *"the contract suite must grow to cover `IMoneyQuery` in the same slice the port is introduced — not later."* And slice 3's `LoadAccounts` is the same question in miniature: the spec never pins whether simple reads go through `IMoneyStore` or `IMoneyQuery`.

**Resolution — both ports carry reads, and the split is by *what comes back*, not by *who asks*.** The spec already states this and it was never collected into one sentence: §2.7.2 point 3 says *"to write, you re-read the root through `IMoneyStore`, which hands you the authoritative version."*

| Port | Returns | Carries `RowVersion`? | Can be passed to a write method? |
|---|---|---|---|
| `IMoneyStore` | **aggregate roots** (`Account`, …) | yes | yes — this is the only legal source of a writable root |
| `IMoneyQuery` | **projections** (`AccountRow` : `IProjection`) | **no**, deliberately (§2.7.2 point 3) | **no — does not compile** (§2.7.2 point 2) |

So:

1. **Slice 3's `LoadAccounts` stays on `IMoneyStore`** — it returns roots, and a root read has to hand back the authoritative version or the next write cannot be version-checked. It is not a second read path competing with `IMoneyQuery`; it is the *write-preparation* read that §2.7.2 point 3 says must cost something explicit.
2. **`IMoneyQuery` is implemented on SQLite in the same slice (slice 3, Task 15)**, not later, with `ListAccounts` and `NextAccountId`. It has a real Plan-A consumer from slice 7 (`AddAccountService`'s duplicate-name check and Id allocation), so it is not a test-only type.
3. **Slice 4's `StoreContractTests` base covers `IMoneyStore` *and* `IMoneyQuery` from its first commit** (Task 17). That is the strongest available reading of §6.4: slice 1 "introduces the port" as a *type*, at a point where no engine and no contract suite exist, so "the same slice" can only mean "the suite covers it the moment the suite exists."
4. **`IMoneyQuery` declares only what Plan A implements** — `ListAccounts(AccountQuery)` and `NextAccountId()`. `Aggregate(AggregateQuery)` from §1 is **not** declared, because slices 1–7 stop at `Account` and there is nothing to aggregate. Declaring a member no engine implements would reintroduce exactly the `NotImplementedException`-as-a-capability-signal that §3.2 says this split finally kills. `Aggregate` arrives with the slice that brings `Transaction`.

### D-2. `DeleteRoot` on a never-persisted root (§1.6c's honest-costs list, routed to "the implementation slice should make [the call] deliberately")

**Decision, pinned by contract test in Task 14:**

- **`DeleteRoot(root)` where `root.RowVersion == 0` is a silent no-op.** No SQL is issued; the post-commit action runs `OnUpdated()` and nothing else. `RowVersion == 0` means "this root has never been written" — a root loaded from the store always has `Version >= 1` (the schema's `DEFAULT 1`), and a freshly-constructed one has the `long` default. This is the panel's own recommended answer, adopted.
- **`DeleteRoot` on an already-deleted, previously-persisted root throws `ConcurrencyConflictException`.** The version-checked `DELETE` matches zero rows and that is the honest report: the row genuinely is not there at the version the caller holds. Deliberately *not* softened to a no-op — the first delete succeeded, so the second call is either a caller bug or a real race, and both are exactly what the conflict exception plus §1.6a's retry loop exist for. The misleading-diagnosis worry §1.6c raised is addressed by the first rule, which covers the *common* accident (a root created and deleted before any save).
- Both cases are asserted, in both directions, in Task 14.

### D-3. §2.7.3's open views question — nothing in this plan depends on the answer

The owner has been asked to confirm whether the rule is **(a)** *"nothing in this codebase ever writes through a view"* or **(b)** *"the business layer never writes through a view; the data layer may use trigger machinery internally if a future spike proves it out."* The spec states nothing waits on it and the recommendation stands either way.

**Plan A creates no view and no trigger of any kind.** The one enforcement it builds — Task 22's Tier-2 `Schema_OwnsNoInsteadOfTrigger` — is correct and unchanged under both answers: under (a) it is permanent, under (b) it is the test someone must deliberately amend if the transfer-both-sides spike is ever taken, which is exactly the conversation §2.7.3 wants to force. **No task in this plan would need rework under either answer.**

### D-4. Deliberate deviations from the spec's literal text, with reasons

Listed so a reviewer does not have to rediscover them.

| Spec says | This plan does | Why |
|---|---|---|
| `StoreIdentity(… DataEngineType Engine …)` (§1.9.2) | `StoreIdentity(… DbFlavor Engine …)` | `DataEngineType` lives in `MyMoney.Data/DatabaseRegistry.cs`, in namespace `Walkabout.Data`. Declaring a second one in `MyMoney.Business` under the same namespace is a genuine duplicate-type ambiguity for anything referencing both. `DbFlavor` is already in `MyMoney.Business/IDatabase.cs`, already in that namespace, and its own comment already says it *"is part of the IDatabase contract itself."* Using it costs zero edits to existing code. |
| "`StoreIdentity` lands in slice 5b" (§1.9.9) | `StoreIdentity` lands in slice 1 (Task 1); the **guard** lands in slice 5b (Task 21) | `IMoneyStore.Identity` returns a `StoreIdentity`, so slice 3 cannot implement the port without the record existing. §1.9.9 groups them because they arrived in the same *revision*, not because of a dependency. |
| `ReleasePublishOutput_ContainsNoSqlServerEngineAssembly` asserts against a real `dotnet publish -c Release` directory (§2.5) | `ProductionReferenceClosure_ContainsNoSqlServerEngineAssembly` walks `GetReferencedAssemblies()` transitively from the UI assembly; the packaging guarantee is additionally enforced at **build time** by `Directory.Build.targets` (§2.5 layer 2) | A publish-output assertion inside a Debug test pass either shells out to a nested MSBuild (slow, fragile) or `Assert.Ignore`s itself (the "quietly deleted when inconvenient" failure §2.5 warns about). The reference-closure check is the pattern §2.5 itself calls *"the one the project already trusts."* In Plan A no `MyMoney.Data.SqlServer*` assembly exists yet, so this test cannot fail — it is built now as a **tripwire for Plan B's slice 8**, and the plan says so rather than implying it proves something today. |
| `ClearOptions` is a `readonly record struct` with `ResetIdentity = true` (§2.6.2) | `ClearOptions` is a `sealed record` **class** with `ResetIdentity { get; init; } = true` and a `ClearOptions.Default` | A `record struct`'s `default` value ignores its parameter defaults, so `ClearOptions options = default` would silently give `ResetIdentity == false` — the exact opposite of the spec's stated default, on the exact option §2.6.4 calls "the cross-engine trap." |
| `TableRef` is a `readonly record struct` with no public constructor (§2.6.6) | `TableRef` is a `sealed class` with a private constructor | A `record struct` always has a public parameterless constructor that cannot be removed, so `default(TableRef)` is a `TableRef` with a null name — precisely the "a typo cannot be constructed" property §2.6.6 is buying. |
| `IRowScope { RowSetSnapshot Removed { get; } }` (§2.6.2) | `IRowScope { RowSnapshot Removed { get; } }` | Only `RemoveRow` (singular) is built in Plan A; see the deferral note below. |
| §5 slice 2 lists no `Backup` | Backup lands in slice 2 (Task 9) | §1.8's third compatibility property is explicit: *"`Backup` must be in the provisioner contract and contract-tested from the start… A backup routine that only appears when production appears is a backup routine that has never been restored from."* §1.7.1's `VACUUM INTO` removes the cost argument for deferring it. |

**Deliberately deferred out of Plan A, though §2.6.2 designs them:** `Snapshot()` / `Restore(StoreSnapshot)`, `CaptureRows` / `RestoreRows` / `RemoveRows`, and `RowFilter`. No slice 1–7 deliverable needs them, and declaring interface members no tier implements contradicts the no-capability-holes rule. They are added with the first slice that needs them (the plural forms' natural consumer is a multi-row report fixture, which arrives with `Transaction`).

**Out of scope entirely (Plan B or unscheduled):** `MyMoney.Data.SqlServer{,.Provisioning,.TestTier}`, the T-1 wall-time gate, the `json_each` benchmark, `SampleDataFactory`/`SampleDataService`, `IDataFormat`/XML export, `MainWindow`'s decomposition, `ReportModel`/renderers.

---

## File Structure

### New projects (all added to `Source/WPF/MyMoney.sln`)

| Project | Kind | Shipped? | References |
|---|---|---|---|
| `Source/WPF/MyMoney.TestKit.Contracts/` | library | **never** | `MyMoney.Business` |
| `Source/WPF/MyMoney.Data.Sqlite.Provisioning/` | library | yes | `MyMoney.Business` |
| `Source/WPF/MyMoney.Data.Sqlite/` | library | yes | `MyMoney.Business` |
| `Source/WPF/MyMoney.Data.Sqlite.TestTier/` | library | **never** | `MyMoney.Business`, `MyMoney.TestKit.Contracts`, `MyMoney.Data.Sqlite.Provisioning` |
| `Source/WPF/MyMoney.TestKit/` | library, **no test-runner packages, no `[TestFixture]`** | **never** | `MyMoney.Business`, `MyMoney.TestKit.Contracts`, `MyMoney.Data.Sqlite`, `MyMoney.Data.Sqlite.Provisioning`, `MyMoney.Data.Sqlite.TestTier` |
| `Source/WPF/MyMoney.Tests.Architecture/` | NUnit test project | n/a | everything above + `MyMoney.Data`, `MyMoney.csproj`, `MyMoney.Tests.Business` |
| `Source/WPF/MyMoney.Tests.Business/` | NUnit test project | n/a | `MyMoney.Business`, `MyMoney.TestKit` — **never `MyMoney.csproj`** |
| `Source/WPF/MyMoney.Tests.Data/` | NUnit test project | n/a | `MyMoney.TestKit` + the three SQLite assemblies |

**Why `MyMoney.Data.Sqlite` does *not* reference `MyMoney.Data.Sqlite.Provisioning`:** §1's Recommendation is explicit — *"do not create `MyMoney.Data.Sqlite.Common`, and do not use `InternalsVisibleTo`. Instead, accept a small amount of duplication… the genuine overlap is connection-string construction and open/close, which is ~40 lines."* `SqliteConnectionFactory` is therefore written **twice**, once in each assembly, and Task 22 adds a Tier-0 test asserting the store assembly does not reference the provisioning one. The duplication is deliberate and enforced, not an oversight.

### New files, by responsibility

**`Source/WPF/MyMoney.Business/`** (namespace `Walkabout.Data` unless noted)

| File | Responsibility |
|---|---|
| `Store/IMoneyStore.cs` | `StoreIdentity`, `IMoneyStore` — the write surface plus root reads |
| `Store/MoneyStoreBase.cs` | `MoneyStoreBase` — the four non-virtual validated write methods over `protected abstract WriteRoots` |
| `Store/IMoneyQuery.cs` | `IProjection`, `AccountRow`, `AccountQuery`, `IMoneyQuery` |
| `Store/IMoneyStoreProvisioner.cs` | `IMoneyStoreProvisioner`, `SchemaDifference`, `SchemaVerifyResult`, `SchemaDriftException`, `SchemaTooNewException` |
| `Store/MoneyScale.cs` | pure `decimal` ↔ `long` ten-thousandths codec |
| `Store/TestDatabaseGuard.cs` | `TestDatabaseGuard`, `TestDatabaseRequiredException` (Task 21) |
| `AppServices/AddAccountService.cs` | the use case + `DuplicateAccountNameException` (namespace `Walkabout.Business.AppServices`) |

**`Source/WPF/MyMoney.TestKit.Contracts/`** (namespace `MyMoney.TestKit.Contracts`)

| File | Responsibility |
|---|---|
| `IMoneyStoreTestControl.cs` | the schema/data/row ladders |
| `TableRef.cs` | the closed, typo-proof table identifier |
| `ClearOptions.cs` | `ClearOptions`, `ClearResult` |
| `RowSnapshot.cs` | `RowSnapshot`, `IRowScope` |
| `StoreTestControlException.cs` | the tier's one exception type |

**`Source/WPF/MyMoney.Data.Sqlite.Provisioning/`** (namespace `Walkabout.Data.Sqlite.Provisioning`)

| File | Responsibility |
|---|---|
| `SqliteConnectionFactory.cs` | open a connection and apply the pragma sequence (copy A) |
| `SchemaStep.cs` | one numbered step + its checksum |
| `SchemaStepCatalog.cs` | load + order + validate the embedded `.sql` steps |
| `SqliteMoneyStoreProvisioner.cs` | `ApplyTo` / `CurrentVersion` / `Verify` / `DropAll` / `Backup` / `Delete` |
| `SchemaSnapshot.cs` | `PRAGMA`-driven introspection, canonical form, and diff |
| `SqliteTableOrder.cs` | FK-topological delete/drop order (shared with the test tier) |
| `Schema/0001_create_online_accounts.sql` … `Schema/0004_add_accounts_name_index.sql` | embedded resources |

**`Source/WPF/MyMoney.Data.Sqlite/`** (namespace `Walkabout.Data.Sqlite`)

| File | Responsibility |
|---|---|
| `SqliteConnectionFactory.cs` | copy B (see above) |
| `SqliteStoreOptions.cs` | display name, data source, test-database flag |
| `SqliteMoneyStore.cs` | `MoneyStoreBase` subclass: open-time version check, `WriteRoots`, `LoadAccounts` |
| `AccountRowCodec.cs` | `Account` ↔ SQL parameters / JSON array / reader row, in one place |
| `SqliteMoneyQuery.cs` | `IMoneyQuery` over parameterized SQL |

**`Source/WPF/MyMoney.Data.Sqlite.TestTier/`** (namespace `Walkabout.Data.Sqlite.TestTier`)

| File | Responsibility |
|---|---|
| `SqliteMoneyStoreTestControl.cs` | the three ladders |
| `SqliteTestControlFactory.cs` | the **one** guarded acquisition point (§2.6.4) |

**`Source/WPF/MyMoney.TestKit/`** (namespace `MyMoney.TestKit`)

| File | Responsibility |
|---|---|
| `InMemorySqliteStore.cs` | one `:memory:` store + provisioner + query + test control over one connection, disposed at teardown (T-1) |
| `RecordingStore.cs` | `IMoneyStore` decorator recording every call |
| `FaultInjectingStore.cs` | `IMoneyStore` decorator that throws on demand |
| `StoreContractTests.cs` | the abstract, engine-agnostic contract suite for `IMoneyStore` **and** `IMoneyQuery` |

**Test files** are named in each task.

---

## Task 1: `IMoneyStore`, `StoreIdentity`, and `MoneyStoreBase`'s validated write surface

**Files:**
- Create: `Source/WPF/MyMoney.Business/Store/IMoneyStore.cs`
- Create: `Source/WPF/MyMoney.Business/Store/MoneyStoreBase.cs`
- Create: `Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj`
- Test: `Source/WPF/MyMoney.Tests.Business/Store/MoneyStoreBaseTests.cs`

**Interfaces:**
- Consumes: `PersistentObject`, `IAggregateRoot`, `Account`, `Transaction`, `DbFlavor` (all `MyMoney.Business`, namespace `Walkabout.Data`).
- Produces:
  - `sealed record StoreIdentity(string DisplayName, DbFlavor Engine, bool IsTestDatabase, int SchemaVersion)`
  - `interface IMoneyStore : IDisposable` with `StoreIdentity Identity { get; }`, `void SaveRoot<TRoot>(TRoot root)`, `void DeleteRoot<TRoot>(TRoot root)` (both `where TRoot : PersistentObject, IAggregateRoot`), `void SaveRoots(IReadOnlyList<IAggregateRoot> roots)`, `void SaveTransfer(Transaction from, Transaction to)`, `IReadOnlyList<Account> LoadAccounts()`
  - `abstract class MoneyStoreBase : IMoneyStore` with `protected abstract void WriteRoots(IReadOnlyList<IAggregateRoot> roots)`

- [ ] **Step 1: Create the `MyMoney.Tests.Business` project**

Create `Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Walkabout.Tests.Business</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="NUnit" Version="4.6.1" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
    <PackageReference Include="NUnit.Analyzers" Version="4.14.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <!-- Deliberately NOT ..\MyMoney\MyMoney.csproj - see spec section 2.1 item 2.
         Task 22 adds TestsBusiness_DoesNotReferenceTheWpfUiProject to enforce this. -->
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
  </ItemGroup>

</Project>
```

Then register it:
```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj
```

- [ ] **Step 2: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Business/Store/MoneyStoreBaseTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Business.Store
{
    /// <summary>
    /// Pins the preconditions spec section 1.6c states for the four-method write surface.
    /// These live once, in MoneyStoreBase, so the two engines cannot disagree about what
    /// SaveRoot admits - spec section 1.6c, Developer note 2. The per-engine half (that a
    /// refused call writes nothing) is Tier 2 and lands in Task 17's contract suite.
    /// </summary>
    [TestFixture]
    public class MoneyStoreBaseTests
    {
        /// <summary>Records what reached WriteRoots without touching a database.</summary>
        private sealed class SpyStore : MoneyStoreBase
        {
            public List<IReadOnlyList<IAggregateRoot>> Writes { get; } = new List<IReadOnlyList<IAggregateRoot>>();

            public override StoreIdentity Identity { get; } =
                new StoreIdentity("spy", DbFlavor.Sqlite, true, 0);

            public override IReadOnlyList<Account> LoadAccounts() => Array.Empty<Account>();

            protected override void WriteRoots(IReadOnlyList<IAggregateRoot> roots) => this.Writes.Add(roots);

            public override void Dispose()
            {
            }
        }

        private static Account NewAccount()
        {
            // new Account(container) starts at ChangeType.Inserted - see Money.cs's
            // "private ChangeType change = ChangeType.Inserted;".
            return new Account(new Accounts((PersistentObject)null)) { Id = 1, Name = "Checking" };
        }

        private static Account ChangedAccount()
        {
            Account a = NewAccount();
            a.OnUpdated();          // Inserted -> None
            a.OnChanged("Name");    // None -> Changed
            return a;
        }

        private static Account CleanAccount()
        {
            Account a = NewAccount();
            a.OnUpdated();          // Inserted -> None
            return a;
        }

        private static Account DeletedAccount()
        {
            Account a = NewAccount();
            a.OnDelete();
            return a;
        }

        [Test]
        public void SaveRoot_AdmitsInsertedChangedAndNone_AndCallsWriteRootsOncePerCall()
        {
            var store = new SpyStore();

            store.SaveRoot(NewAccount());
            store.SaveRoot(ChangedAccount());
            store.SaveRoot(CleanAccount());

            Assert.That(store.Writes, Has.Count.EqualTo(3));
            Assert.That(store.Writes[0], Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveRoot_RefusesADeletedRoot_WithArgumentExceptionAndNoWrite()
        {
            var store = new SpyStore();

            Assert.Throws<ArgumentException>(() => store.SaveRoot(DeletedAccount()));
            Assert.That(store.Writes, Is.Empty);
        }

        [Test]
        public void DeleteRoot_AdmitsOnlyADeletedRoot()
        {
            var store = new SpyStore();

            store.DeleteRoot(DeletedAccount());

            Assert.That(store.Writes, Has.Count.EqualTo(1));
        }

        [TestCase("inserted")]
        [TestCase("changed")]
        [TestCase("none")]
        public void DeleteRoot_RefusesANonDeletedRoot_WithArgumentExceptionAndNoWrite(string state)
        {
            var store = new SpyStore();
            Account a = state switch
            {
                "inserted" => NewAccount(),
                "changed" => ChangedAccount(),
                _ => CleanAccount()
            };

            Assert.Throws<ArgumentException>(() => store.DeleteRoot(a));
            Assert.That(store.Writes, Is.Empty);
        }

        [Test]
        public void SaveRoots_AcceptsAMixedStateCollectionAndAssertsNothing()
        {
            var store = new SpyStore();

            store.SaveRoots(new IAggregateRoot[] { NewAccount(), ChangedAccount(), DeletedAccount() });

            Assert.That(store.Writes, Has.Count.EqualTo(1));
            Assert.That(store.Writes[0], Has.Count.EqualTo(3));
        }

        [Test]
        public void SaveRoots_OnAnEmptyCollection_DoesNotReachTheExecutor()
        {
            var store = new SpyStore();

            store.SaveRoots(Array.Empty<IAggregateRoot>());

            Assert.That(store.Writes, Is.Empty);
        }

        [Test]
        public void SaveRoot_OnNull_ThrowsArgumentNullException()
        {
            var store = new SpyStore();

            Assert.Throws<ArgumentNullException>(() => store.SaveRoot<Account>(null));
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj`
Expected: FAIL to **compile**, with `CS0246: The type or namespace name 'MoneyStoreBase' could not be found` (and the same for `StoreIdentity`).

- [ ] **Step 4: Write `IMoneyStore` and `StoreIdentity`**

Create `Source/WPF/MyMoney.Business/Store/IMoneyStore.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>
    /// The identity of an OPEN store handle. Carried on the store rather than passed alongside
    /// it, so a guard reads the flag off the same object the writes go to - spec section 1.9.2.
    /// Every failure mode of IsTestDatabase resolves to false (a missing JSON field, a casing
    /// regression, an unparsed registry), i.e. the guarded capability refuses. There is no way
    /// for the flag to break open.
    /// </summary>
    /// <param name="DisplayName">The registry entry's key, for plain-language messages.</param>
    /// <param name="Engine">Which engine this handle talks to.</param>
    /// <param name="IsTestDatabase">DatabaseEntry.TestDatabase, as of open.</param>
    /// <param name="SchemaVersion">Schema_CurrentVersion() as of open - spec section 1.8.</param>
    public sealed record StoreIdentity(
        string DisplayName,
        DbFlavor Engine,
        bool IsTestDatabase,
        int SchemaVersion);

    /// <summary>
    /// Everyday read/write access to the books. No DDL, no destructive bulk operations, no
    /// whole-graph Load() and no Save(MyMoney) - spec section 1.
    ///
    /// The write surface is EXACTLY four members (spec section 1.6c), pinned by the Tier-0 test
    /// StoreWriteSurface_IsExactlyTheFourNamedMethods. SaveRoot never removes the root's own row;
    /// DeleteRoot is the only way to do that, so "what in this codebase can remove an account?"
    /// has an answer you can grep for.
    ///
    /// LoadAccounts returns AGGREGATE ROOTS carrying RowVersion, which is what makes a later
    /// version-checked write possible. Reads that do not intend to write go through IMoneyQuery
    /// and come back as projections with no RowVersion - spec section 2.7.2 point 3.
    /// </summary>
    public interface IMoneyStore : IDisposable
    {
        StoreIdentity Identity { get; }

        /// <summary>Insert-or-update. Throws ArgumentException on a root marked deleted.</summary>
        void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot;

        /// <summary>Remove. Throws ArgumentException on a root NOT marked deleted.</summary>
        void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot;

        /// <summary>Mixed-state by definition (reconciliation, merges); asserts nothing.</summary>
        void SaveRoots(IReadOnlyList<IAggregateRoot> roots);

        /// <summary>Exactly two peer Transactions that must co-commit.</summary>
        void SaveTransfer(Transaction from, Transaction to);

        IReadOnlyList<Account> LoadAccounts();
    }
}
```

- [ ] **Step 5: Write `MoneyStoreBase`**

Create `Source/WPF/MyMoney.Business/Store/MoneyStoreBase.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>
    /// The four public write methods, written ONCE, over one engine-supplied executor.
    ///
    /// The preconditions live here rather than per engine for the reason spec section 1.6c's
    /// Developer note 2 gives: if each engine carried its own copy, the engines could disagree
    /// about what SaveRoot admits, which is the exact class of divergence the contract suite
    /// exists to police.
    ///
    /// The four methods are deliberately NON-VIRTUAL, and WriteRoots is protected, so nothing
    /// public reaches the executor except through one of the four. InternalsVisibleTo is
    /// explicitly not the mechanism - spec section 6.2.
    ///
    /// The named crack, stated rather than papered over (spec section 1.6c): SaveRoots asserts
    /// nothing, so SaveRoots(new[] { root }) is a one-line unvalidated route to the same
    /// executor. Three things keep it honest - the plural name makes a single-element call
    /// visibly odd at review, StoreWriteSurface_IsExactlyTheFourNamedMethods pins the member
    /// set, and only ONE distinction is guarded so there is exactly one thing to dodge.
    /// </summary>
    public abstract class MoneyStoreBase : IMoneyStore
    {
        public abstract StoreIdentity Identity { get; }

        public abstract IReadOnlyList<Account> LoadAccounts();

        /// <summary>
        /// The one insert/update/delete dispatch. Implementations own the transaction, the
        /// version check and R-CRUD-2's post-commit deferral - all written exactly once per
        /// engine, never once per public method.
        /// </summary>
        protected abstract void WriteRoots(IReadOnlyList<IAggregateRoot> roots);

        public abstract void Dispose();

        public void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            // ArgumentException, deliberately NOT ConcurrencyConflictException: this is a caller
            // bug, unconditionally reproducible, and if the business layer's retry loop caught it
            // the loop would spin forever on a call that can only fail identically. Spec 1.6a.
            if (root.IsDeleted)
            {
                throw new ArgumentException(
                    $"SaveRoot cannot be used on a {typeof(TRoot).Name} that is marked deleted - " +
                    "call DeleteRoot instead. (A root can become deleted behind a caller's back: " +
                    "PersistentObject.OnDelete() is a guardless soft delete.)",
                    nameof(root));
            }

            this.WriteRoots(new IAggregateRoot[] { root });
        }

        public void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (!root.IsDeleted)
            {
                throw new ArgumentException(
                    $"DeleteRoot requires a {typeof(TRoot).Name} that is marked deleted " +
                    "(call OnDelete() first). Deletion is always something a caller deliberately " +
                    "asked for, so this precondition is exact.",
                    nameof(root));
            }

            this.WriteRoots(new IAggregateRoot[] { root });
        }

        public void SaveRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            if (roots == null)
            {
                throw new ArgumentNullException(nameof(roots));
            }

            if (roots.Count == 0)
            {
                return;
            }

            this.WriteRoots(roots);
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            if (from == null)
            {
                throw new ArgumentNullException(nameof(from));
            }

            if (to == null)
            {
                throw new ArgumentNullException(nameof(to));
            }

            this.WriteRoots(new IAggregateRoot[] { from, to });
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj`
Expected: PASS — 9 tests (the `[TestCase]` trio counts as three).

- [ ] **Step 7: Commit**
```bash
git add Source/WPF/MyMoney.Business/Store Source/WPF/MyMoney.Tests.Business Source/WPF/MyMoney.sln
git commit -m "$(cat <<'EOF'
feat(data): IMoneyStore port and MoneyStoreBase's four validated write methods

Slice 1, task 1. SaveRoot/DeleteRoot/SaveRoots/SaveTransfer are non-virtual
members of MoneyStoreBase over one protected abstract WriteRoots, so the
insert/update/delete dispatch and R-CRUD-2's post-commit deferral stay written
exactly once per engine. SaveRoot refuses a root marked deleted (ArgumentException,
not ConcurrencyConflictException, so the business-layer retry loop cannot spin on
a caller bug); DeleteRoot requires one.

Spec: 2026-09-20-data-layer-architecture-brainstorm.md sections 1.6b, 1.6c, 1.6a.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `IMoneyQuery`, projections, and the three port-shape Tier-0 tests

**Files:**
- Create: `Source/WPF/MyMoney.Business/Store/IMoneyQuery.cs`
- Create: `Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
- Test: `Source/WPF/MyMoney.Tests.Architecture/PortShapeTests.cs`

**Interfaces:**
- Consumes: `IAggregateRoot`, `AccountType`, `IMoneyStore` (Task 1).
- Produces: `interface IProjection { }`; `sealed record AccountRow(long Id, string Name, AccountType Type, string Currency, decimal OpeningBalance, bool IsClosed) : IProjection`; `sealed record AccountQuery(IReadOnlyList<long> AccountIds, bool IncludeClosed)` with `static AccountQuery All`; `interface IMoneyQuery` with `IReadOnlyList<AccountRow> ListAccounts(AccountQuery query)` and `long NextAccountId()`.

- [ ] **Step 1: Create the `MyMoney.Tests.Architecture` project**

Create `Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Walkabout.Tests.Architecture</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="NUnit" Version="4.6.1" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
    <PackageReference Include="NUnit.Analyzers" Version="4.14.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <!-- This is the ONE test project allowed to reference the WPF UI project (Task 22 adds it),
         because inspecting production assemblies is its job. -->
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
  </ItemGroup>
</Project>
```

Register it:
```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj
```

- [ ] **Step 2: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Architecture/PortShapeTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0. Turns "the business layer cannot write view-backed data" and "a delete is
    /// nameable" from sentences in a design document into properties of the compiled assemblies.
    /// Spec sections 2.5, 2.7.2, 1.6c.
    /// </summary>
    [TestFixture]
    public class PortShapeTests
    {
        /// <summary>
        /// A "write member" is a public method on IMoneyStore taking at least one parameter that
        /// is an aggregate root, a generic parameter constrained to one, or a read-only list of
        /// them. Defined structurally rather than by name prefix so a method cannot dodge one of
        /// these two tests by being named to look like it belongs to the other. Identity (a
        /// property) and LoadAccounts (no parameters) are excluded by construction - spec 1.9.2
        /// relies on exactly that.
        /// </summary>
        private static bool IsRootShaped(Type t)
        {
            if (typeof(IAggregateRoot).IsAssignableFrom(t))
            {
                return true;
            }

            if (t.IsGenericParameter)
            {
                return t.GetGenericParameterConstraints().Any(c => typeof(IAggregateRoot).IsAssignableFrom(c));
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                return typeof(IAggregateRoot).IsAssignableFrom(t.GetGenericArguments()[0]);
            }

            return false;
        }

        private static IReadOnlyList<MethodInfo> WriteMembers()
        {
            return typeof(IMoneyStore)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetParameters().Any(p => IsRootShaped(p.ParameterType)))
                .ToList();
        }

        [Test]
        public void StoreWriteSurface_IsExactlyTheFourNamedMethods()
        {
            var names = WriteMembers().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.That(
                names,
                Is.EqualTo(new[] { "DeleteRoot", "SaveRoot", "SaveRoots", "SaveTransfer" }),
                "IMoneyStore's write surface changed. The benefit of naming the delete evaporates "
                + "the day someone re-adds a convenience Save<T> that does everything - spec 1.6c.");
        }

        [Test]
        public void StoreWriteMethods_AcceptOnlyAggregateRoots()
        {
            foreach (MethodInfo m in WriteMembers())
            {
                foreach (ParameterInfo p in m.GetParameters())
                {
                    Assert.That(
                        IsRootShaped(p.ParameterType),
                        Is.True,
                        $"IMoneyStore.{m.Name} takes '{p.Name}' of type {p.ParameterType.Name}, "
                        + "which is not an aggregate root. A write method that takes a DTO 'just "
                        + "for this one import path' is the hole this test keeps shut - spec 2.7.2.");
                }
            }
        }

        [Test]
        public void ProjectionTypes_DoNotImplementIAggregateRoot()
        {
            Assembly business = typeof(IMoneyStore).Assembly;
            var projections = business.GetTypes()
                .Where(t => typeof(IProjection).IsAssignableFrom(t) && !t.IsInterface)
                .ToList();

            Assert.That(projections, Is.Not.Empty, "No IProjection implementers - this would pass vacuously.");

            foreach (Type t in projections)
            {
                Assert.That(
                    typeof(IAggregateRoot).IsAssignableFrom(t),
                    Is.False,
                    $"{t.Name} is a projection and must not be an aggregate root, or store.SaveRoot(row) would compile.");

                Assert.That(
                    t.GetProperty("RowVersion", BindingFlags.Public | BindingFlags.Instance),
                    Is.Null,
                    $"{t.Name} exposes RowVersion. A projection carrying a version lets a stale "
                    + "version be smuggled from a report into a write - spec 2.7.2 point 3.");

                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    // Record positional members compile to init-only setters, which report
                    // CanWrite == true. An init-only setter carries the IsExternalInit modreq.
                    MethodInfo setter = p.SetMethod;
                    bool isInitOnly = setter != null
                        && setter.ReturnParameter.GetRequiredCustomModifiers()
                            .Any(x => x.FullName == "System.Runtime.CompilerServices.IsExternalInit");

                    Assert.That(
                        setter == null || isInitOnly,
                        Is.True,
                        $"{t.Name}.{p.Name} has a settable (not init-only) setter. Projections are read models.");
                }
            }
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
Expected: FAIL to compile — `CS0246: The type or namespace name 'IProjection' could not be found`.

- [ ] **Step 4: Write `IMoneyQuery` and the projections**

Create `Source/WPF/MyMoney.Business/Store/IMoneyQuery.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>
    /// Read-model marker, no members. Every row type IMoneyQuery returns implements it, and no
    /// implementer implements IAggregateRoot - which is what makes store.SaveRoot(row) fail to
    /// COMPILE rather than fail at runtime. Spec section 2.7.2.
    /// </summary>
    public interface IProjection
    {
    }

    /// <summary>
    /// A read-only view of one account. Deliberately carries NO RowVersion: to write, you re-read
    /// the root through IMoneyStore.LoadAccounts, which hands you the authoritative version. The
    /// round trip is possible; it just has to be explicit, and the explicitness is the feature.
    /// Spec section 2.7.2 point 3.
    /// </summary>
    public sealed record AccountRow(
        long Id,
        string Name,
        AccountType Type,
        string Currency,
        decimal OpeningBalance,
        bool IsClosed) : IProjection;

    /// <summary>
    /// A closed, typed filter - NOT IQueryable. The SQL Server tier is stored-proc-only, so an
    /// expression-tree provider is literally impossible there, and an IQueryable that silently
    /// materializes everything and filters in memory is worse than no abstraction. Spec section 1.
    /// </summary>
    /// <param name="AccountIds">Empty means all accounts.</param>
    public sealed record AccountQuery(IReadOnlyList<long> AccountIds, bool IncludeClosed)
    {
        public static AccountQuery All { get; } = new AccountQuery(Array.Empty<long>(), true);
    }

    /// <summary>
    /// The application's read path for everything that does not intend to write. Filtering and
    /// subtotaling happen in SQL, not in memory - spec section 3.3 stage 1.
    ///
    /// Plan A declares only what Plan A implements. Aggregate(AggregateQuery) from spec section 1
    /// arrives with the slice that brings Transaction; declaring it now would put a member on this
    /// port that no engine implements, which is precisely the
    /// NotImplementedException-as-a-capability-signal that splitting IDatabase was meant to kill.
    /// </summary>
    public interface IMoneyQuery
    {
        IReadOnlyList<AccountRow> ListAccounts(AccountQuery query);

        /// <summary>
        /// The next unused Account id. Id allocation stays a caller concern (it already is -
        /// Accounts.AddAccount's NextAccount counter); with no ambient graph this is where a
        /// caller gets it. A lost race is real and is handled: the store turns a primary-key
        /// collision on insert into ConcurrencyConflictException, so section 1.6a's retry loop
        /// covers it. See Task 12.
        /// </summary>
        long NextAccountId();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
Expected: PASS — 3 tests.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Business/Store/IMoneyQuery.cs Source/WPF/MyMoney.Tests.Architecture Source/WPF/MyMoney.sln
git commit -m "$(cat <<'EOF'
feat(data): IMoneyQuery read port, IProjection, and three port-shape Tier-0 tests

Slice 1, task 2. IMoneyQuery is the read path for everything that does not intend
to write; it returns projections that carry no RowVersion and do not implement
IAggregateRoot, so store.SaveRoot(row) does not compile. Aggregate() is
deliberately not declared until a slice implements it.

Resolves the spec's flagged scheduling gap: IMoneyQuery is implemented on SQLite
in slice 3 (task 15) and covered by the shared contract suite from its first
commit (task 17). See the plan's decision D-1.

Spec: sections 1, 2.5, 2.7.2, 3.3, 6.4.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: `IMoneyStoreProvisioner` and the never-shipped `MyMoney.TestKit.Contracts`

**Files:**
- Create: `Source/WPF/MyMoney.Business/Store/IMoneyStoreProvisioner.cs`
- Create: `Source/WPF/MyMoney.TestKit.Contracts/MyMoney.TestKit.Contracts.csproj`
- Create: `Source/WPF/MyMoney.TestKit.Contracts/{IMoneyStoreTestControl,TableRef,ClearOptions,RowSnapshot,StoreTestControlException}.cs`
- Modify: `Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
- Test: `Source/WPF/MyMoney.Tests.Architecture/TestKitContractsTests.cs`

**Interfaces:**
- Consumes: `StoreIdentity` (Task 1), `IAggregateRoot`, `Account`, `Category`, `OnlineAccount`, `Payee`.
- Produces: `IMoneyStoreProvisioner` (`Identity`, `LatestKnownVersion`, `CurrentVersion()`, `ApplyTo(int)`, `Verify()`, `DropAll()`, `Backup(string)`, `Delete()`); `SchemaDifference`; `SchemaVerifyResult`; `SchemaDriftException`; `SchemaTooNewException`; `IMoneyStoreTestControl`; `TableRef`; `ClearOptions`; `ClearResult`; `RowSnapshot`; `IRowScope`; `StoreTestControlException`.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Architecture/TestKitContractsTests.cs`:
```csharp
using System;
using System.Linq;
using System.Reflection;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0. MyMoney.TestKit.Contracts exists to be ABSENT from the production dependency
    /// graph: production code cannot name IMoneyStoreTestControl, so it cannot implement or call
    /// it, and a copy-pasted implementation would not compile - spec sections 1 and 2.5 layer 4.
    /// The sweep over every production assembly lands in Task 22; this pins what is true the
    /// moment the assembly exists.
    /// </summary>
    [TestFixture]
    public class TestKitContractsTests
    {
        [Test]
        public void TestKitContracts_ReferencesOnlyMyMoneyBusiness()
        {
            var projectReferences = typeof(IMoneyStoreTestControl).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("MyMoney", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.That(projectReferences, Is.EqualTo(new[] { "MyMoney.Business" }));
        }

        [Test]
        public void MyMoneyBusiness_DoesNotReferenceTestKitContracts()
        {
            Assert.That(
                typeof(IMoneyStore).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.Contains("TestKit"),
                "MyMoney.Business referenced the test-tier contracts. The whole point of that "
                + "assembly is that production cannot name the type - spec section 2.5 layer 4.");
        }

        [Test]
        public void TableRef_CannotBeConstructedForAnUnmappedRootType()
        {
            Assert.Throws<ArgumentException>(() => TableRef.Of<Payee>());
        }

        [Test]
        public void TableRef_OfAccount_EqualsTheNamedOne()
        {
            Assert.That(TableRef.Of<Account>(), Is.EqualTo(TableRef.Accounts));
            Assert.That(TableRef.Accounts.Name, Is.EqualTo("Accounts"));
        }

        [Test]
        public void ClearOptions_DefaultResetsIdentityAndVerifiesForeignKeys()
        {
            // A record STRUCT's default value would silently give false here, on the exact option
            // spec 2.6.4 calls "the cross-engine trap". ClearOptions is a class for that reason.
            Assert.That(ClearOptions.Default.ResetIdentity, Is.True);
            Assert.That(ClearOptions.Default.VerifyForeignKeysAfter, Is.True);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
Expected: FAIL to compile — `CS0246: The type or namespace name 'MyMoney' could not be found` (on `using MyMoney.TestKit.Contracts;`).

- [ ] **Step 3: Write `IMoneyStoreProvisioner`**

Create `Source/WPF/MyMoney.Business/Store/IMoneyStoreProvisioner.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace Walkabout.Data
{
    /// <summary>One difference between an expected and an actual schema object.</summary>
    public sealed record SchemaDifference(string ObjectKind, string ObjectName, string Expected, string Actual);

    public sealed record SchemaVerifyResult(int Version, IReadOnlyList<SchemaDifference> Differences)
    {
        public bool IsClean => this.Differences.Count == 0;
    }

    /// <summary>
    /// Thrown when an already-recorded step's checksum no longer matches the step SQL in this
    /// build - i.e. someone edited an applied step. Spec section 1.5, S-3 rule 1: during
    /// nuke-and-pave that rule is cheap to follow AND cheap to break, so the check is on from day
    /// one rather than turned on later against a corpus nobody was disciplined about.
    /// </summary>
    public class SchemaDriftException : Exception
    {
        public SchemaDriftException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown at open when the database is at a HIGHER schema version than this binary knows.
    /// ApplyTo handles "the app is newer than the database"; the reverse needs a refusal, not a
    /// best-effort open - spec section 1.8's second bullet.
    /// </summary>
    public class SchemaTooNewException : Exception
    {
        public SchemaTooNewException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Create the file, run the schema, verify, back up, delete. ONE mechanism, several entry
    /// points: creating a database is ApplyTo(N) from version 0, nuke-and-pave is DropAll() then
    /// ApplyTo(N), and the eventual production upgrade is ApplyTo(N) from version M. They differ
    /// only in what CurrentVersion() returns when they start, which is what makes the upgrade
    /// path run on every single fresh creation. Spec section 1.5, S-1.
    /// </summary>
    public interface IMoneyStoreProvisioner : IDisposable
    {
        StoreIdentity Identity { get; }

        /// <summary>The highest step number this binary carries.</summary>
        int LatestKnownVersion { get; }

        /// <summary>Highest fully-applied step; 0 on an empty database.</summary>
        int CurrentVersion();

        /// <summary>Apply every unapplied step from CurrentVersion()+1 up to targetVersion.</summary>
        void ApplyTo(int targetVersion);

        /// <summary>Introspected actual schema vs. expected for the current version.</summary>
        SchemaVerifyResult Verify();

        /// <summary>
        /// Drop every object this schema family owns. DESTRUCTIVE. Lives on the SHIPPED
        /// provisioner rather than only in the never-shipped test tier, because "the assembly
        /// isn't shipped" protects end users but does not protect a developer running a test
        /// suite against the wrong registry entry (spec section 1.8). Task 21 puts
        /// TestDatabaseGuard.Require in front of it.
        /// </summary>
        void DropAll();

        /// <summary>
        /// Transactionally-consistent copy, overwriting destinationPath if it exists. Present
        /// from the first slice on spec section 1.8's third compatibility property: a backup
        /// routine that only appears when production appears is one that has never been restored
        /// from.
        /// </summary>
        void Backup(string destinationPath);

        /// <summary>Remove the database entirely. DESTRUCTIVE; guarded in Task 21.</summary>
        void Delete();
    }
}
```

- [ ] **Step 4: Create the `MyMoney.TestKit.Contracts` project**

Create `Source/WPF/MyMoney.TestKit.Contracts/MyMoney.TestKit.Contracts.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>MyMoney.TestKit.Contracts</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <!-- NEVER SHIPPED. Deliberately NOT marked IsProductionAssembly (Directory.Build.targets,
         Task 22). Spec sections 1 (Candidate A) and 2.5 layer 4. -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
  </ItemGroup>
</Project>
```

Register and reference:
```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.TestKit.Contracts/MyMoney.TestKit.Contracts.csproj
dotnet add Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj reference Source/WPF/MyMoney.TestKit.Contracts/MyMoney.TestKit.Contracts.csproj
```

- [ ] **Step 5: Write `TableRef`**

Create `Source/WPF/MyMoney.TestKit.Contracts/TableRef.cs`:
```csharp
using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// A table identifier that cannot be mistyped, cannot be a view, and cannot be a table that
    /// does not exist - spec section 2.6.6. Instances come from Of&lt;TRoot&gt;() or the named
    /// statics; there is no public constructor.
    ///
    /// A sealed class rather than the spec's readonly record struct: a record struct always has a
    /// public parameterless constructor that cannot be removed, so default(TableRef) would be a
    /// TableRef with a null name - precisely the hole "no public constructor" was buying.
    ///
    /// Note the one-character distance from IMoneyStore.DeleteRoot: this type's DeleteRow is raw,
    /// unchecked, below-the-version-check row removal. They live in different assemblies behind
    /// different interfaces on purpose - spec section 2.6.3.
    /// </summary>
    public sealed class TableRef : IEquatable<TableRef>
    {
        private TableRef(string name)
        {
            this.Name = name;
        }

        public string Name { get; }

        public static TableRef Accounts { get; } = new TableRef("Accounts");
        public static TableRef Categories { get; } = new TableRef("Categories");
        public static TableRef OnlineAccounts { get; } = new TableRef("OnlineAccounts");

        /// <summary>
        /// Every TableRef this build exposes. Task 19's anti-drift contract test asserts this is
        /// exactly the set of data tables the introspected schema contains at the current version,
        /// so adding a table without a TableRef - or leaving one behind after dropping a table -
        /// goes red.
        /// </summary>
        public static IReadOnlyList<TableRef> All { get; } = new[] { Accounts, Categories, OnlineAccounts };

        private static readonly Dictionary<Type, TableRef> ByRootType = new Dictionary<Type, TableRef>
        {
            { typeof(Account), Accounts },
            { typeof(Category), Categories },
            { typeof(OnlineAccount), OnlineAccounts },
        };

        public static TableRef Of<TRoot>() where TRoot : IAggregateRoot
        {
            if (ByRootType.TryGetValue(typeof(TRoot), out TableRef table))
            {
                return table;
            }

            throw new ArgumentException(
                $"No table is mapped for root type {typeof(TRoot).Name}. Add it to TableRef when "
                + "the slice creating its table lands - the anti-drift contract test will already "
                + "be red if the table exists and the TableRef does not.");
        }

        public bool Equals(TableRef other) =>
            other != null && string.Equals(this.Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object obj) => this.Equals(obj as TableRef);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Name);

        public override string ToString() => this.Name;

        public static bool operator ==(TableRef left, TableRef right) => Equals(left, right);

        public static bool operator !=(TableRef left, TableRef right) => !Equals(left, right);
    }
}
```

- [ ] **Step 6: Write `ClearOptions`, `RowSnapshot`, `StoreTestControlException`**

Create `Source/WPF/MyMoney.TestKit.Contracts/ClearOptions.cs`:
```csharp
using System.Collections.Generic;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// A sealed record CLASS, not the spec's record struct: default(ClearOptions) on a record
    /// struct ignores parameter defaults and would silently give ResetIdentity == false, the exact
    /// opposite of the intended default on the exact option spec 2.6.4 calls the cross-engine trap.
    /// </summary>
    public sealed record ClearOptions
    {
        /// <summary>
        /// After DELETE FROM, the next Id a fresh insert receives is NOT the same on the two
        /// engines - SQLite's INTEGER PRIMARY KEY rewinds to 1 by itself unless AUTOINCREMENT
        /// persisted a high-water mark in sqlite_sequence; SQL Server's IDENTITY never rewinds
        /// without DBCC CHECKIDENT. A test asserting Id == 1 after a clear would pass on one
        /// engine and fail on the other for reasons having nothing to do with what it tests.
        /// Spec section 2.6.4.
        /// </summary>
        public bool ResetIdentity { get; init; } = true;

        /// <summary>PRAGMA foreign_key_check after the clear.</summary>
        public bool VerifyForeignKeysAfter { get; init; } = true;

        public static ClearOptions Default { get; } = new ClearOptions();
    }

    public sealed record ClearResult(IReadOnlyDictionary<TableRef, long> RowsDeleted);
}
```

Create `Source/WPF/MyMoney.TestKit.Contracts/RowSnapshot.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// Every column of one row, verbatim - including Id and Version. RestoreRow re-inserts these
    /// exactly; it is not "insert a new row with the same values". A restored row that came back
    /// with a fresh Id or a bumped version would break every reference to it and make the
    /// version-conflict tests unreproducible. Spec section 2.6.5 point 2.
    /// </summary>
    public sealed record RowSnapshot(TableRef Table, long Id, IReadOnlyDictionary<string, object> Values);

    /// <summary>
    /// The scoped form, which is the shape tests should actually use. Dispose restores inside its
    /// own transaction and THROWS if the restore fails rather than swallowing - a silently-failed
    /// restore leaks state into every subsequent test in the fixture, the exact failure mode
    /// AppCrashGuard was written to stop tolerating elsewhere in this project. Spec 2.6.5 point 3.
    ///
    /// Singular RowSnapshot rather than the spec's RowSetSnapshot because only RemoveRow
    /// (singular) is built in Plan A - see the plan's D-4 deferral note.
    /// </summary>
    public interface IRowScope : IDisposable
    {
        RowSnapshot Removed { get; }
    }
}
```

Create `Source/WPF/MyMoney.TestKit.Contracts/StoreTestControlException.cs`:
```csharp
using System;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// Wraps an engine error raised by a test-control operation - e.g. DeleteRow failing and
    /// rolling back because another table's foreign key references the row. DeleteRow does not
    /// cascade, deliberately: if it did, RestoreRow would be a lie, restoring the one row it
    /// captured while the rows the cascade took are gone for good. Spec section 2.6.5 point 1.
    /// </summary>
    public class StoreTestControlException : Exception
    {
        public StoreTestControlException(string message) : base(message)
        {
        }

        public StoreTestControlException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
```

- [ ] **Step 7: Write `IMoneyStoreTestControl`**

Create `Source/WPF/MyMoney.TestKit.Contracts/IMoneyStoreTestControl.cs`:
```csharp
using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit.Contracts
{
    /// <summary>
    /// Put the store into a known state, BELOW the object graph and BELOW the version check.
    /// These exist so a test can reach a state the domain rules would not permit, or would only
    /// permit through a sequence of calls that is itself under test. Using the production API to
    /// build the fixture for a test of the production API is how a test ends up asserting that a
    /// bug is consistent with itself. Spec section 2.6.3.
    ///
    /// THREE LADDERS, and only the data one decomposes (spec section 2.6.1):
    ///   schema - ResetSchema(N): destroys and rebuilds every object
    ///   data   - ClearAllData -> ClearTables -> ClearTable: rows only; every schema object,
    ///            including __SchemaHistory, survives
    ///   row    - CaptureRow / DeleteRow / RestoreRow / RemoveRow
    ///
    /// IDEMPOTENCY (spec section 2.6.8): ResetSchema, ClearAllData, ClearTables, ClearTable and
    /// DeleteRow are safe to call any number of times. RestoreRow deliberately is NOT - it is the
    /// single-shot inverse half of a capture-then-delete pair, and a second call is a primary-key
    /// violation by design.
    ///
    /// EVERY operation here is invisible to any live object graph or cached store state. After any
    /// of them, the caller re-reads.
    ///
    /// Seed is deliberately ABSENT: seeding writes THROUGH IMoneyStore, so a seeded fixture has
    /// exercised the real write path, the real version assignment and the real FK enforcement.
    /// Spec section 2.6.2.
    /// </summary>
    public interface IMoneyStoreTestControl : IDisposable
    {
        // ---- Schema ladder ----

        /// <summary>DropAll() then ApplyTo(targetVersion) - nuke and pave through section 1.5's machinery.</summary>
        void ResetSchema(int targetVersion);

        int CurrentSchemaVersion { get; }

        // ---- Data ladder ----

        ClearResult ClearAllData(ClearOptions options = null);

        ClearResult ClearTables(IReadOnlyCollection<TableRef> tables, ClearOptions options = null);

        ClearResult ClearTable(TableRef table, ClearOptions options = null);

        ClearResult ClearTable<TRoot>(ClearOptions options = null) where TRoot : IAggregateRoot;

        // ---- Row ladder ----

        /// <summary>Throws StoreTestControlException if the row is absent.</summary>
        RowSnapshot CaptureRow(TableRef table, long id);

        /// <summary>Null if the row is absent.</summary>
        RowSnapshot TryCaptureRow(TableRef table, long id);

        /// <summary>
        /// No version check, no cascade. Succeeds as a no-op when id is already absent, so a reset
        /// call site can call it unconditionally and stay idempotent - spec section 2.6.8.
        /// </summary>
        void DeleteRow(TableRef table, long id);

        /// <summary>Re-inserts verbatim: same Id, same Version, same every column.</summary>
        void RestoreRow(RowSnapshot row);

        /// <summary>Capture + delete; Dispose restores byte for byte and throws on failure.</summary>
        IRowScope RemoveRow(TableRef table, long id);

        // ---- Introspection ----

        /// <summary>Data tables only - never views, and never __SchemaHistory.</summary>
        IReadOnlyList<TableRef> Tables { get; }

        long RowCount(TableRef table);
    }
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
Expected: PASS — 8 tests (3 from Task 2, 5 new).

- [ ] **Step 9: Commit**
```bash
git add Source/WPF/MyMoney.Business/Store/IMoneyStoreProvisioner.cs Source/WPF/MyMoney.TestKit.Contracts Source/WPF/MyMoney.Tests.Architecture Source/WPF/MyMoney.sln
git commit -m "$(cat <<'EOF'
feat(data): IMoneyStoreProvisioner port and the never-shipped TestKit.Contracts

Slice 1, task 3. Completes the tier split as TYPES before any engine implements
it. IMoneyStoreTestControl lives in MyMoney.TestKit.Contracts, which no production
assembly references, so production code cannot name the type it would need in
order to implement or call it.

TableRef is a sealed class and ClearOptions a record class rather than the spec's
record structs - a record struct's default value would give a null table name and
ResetIdentity == false respectively, defeating both properties.

Spec: sections 1, 1.5, 1.8, 2.5, 2.6.1-2.6.8.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `MoneyScale` — the `decimal` ↔ `INTEGER` ten-thousandths codec

**Files:**
- Create: `Source/WPF/MyMoney.Business/Store/MoneyScale.cs`
- Test: `Source/WPF/MyMoney.Tests.Business/Store/MoneyScaleTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static class MoneyScale` with `const int Scale = 4`, `static long ToStorage(decimal value)`, `static decimal FromStorage(long stored)`.

**Why this exists.** `STRICT` tables allow only `INT`/`INTEGER`/`REAL`/`TEXT`/`BLOB`/`ANY`, so the `money` column type today's reflection-to-DDL emits is illegal. `REAL` loses exactness on money. `TEXT` is exact but not SQL-summable, which kills §3.3 stage 1 ("filtering and subtotaling happen in SQL"). `INTEGER` ten-thousandths is exact, SQL-summable, and is *literally* SQL Server's `money` representation — an `int64` of ten-thousandths whose maximum, 922,337,203,685,477.5807, is `Int64.MaxValue / 10000`. Adopting it on SQLite therefore *converges* the engines rather than diverging them (§6.8 rule 1).

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Business/Store/MoneyScaleTests.cs`:
```csharp
using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Business.Store
{
    [TestFixture]
    public class MoneyScaleTests
    {
        [TestCase("0", 0L)]
        [TestCase("1", 10000L)]
        [TestCase("-1", -10000L)]
        [TestCase("0.0001", 1L)]
        [TestCase("-0.0001", -1L)]
        [TestCase("1234.5678", 12345678L)]
        [TestCase("922337203685477.5807", 9223372036854775807L)]
        [TestCase("-922337203685477.5808", -9223372036854775808L)]
        public void ToStorage_MatchesSqlServerMoneysOwnRepresentation(string value, long expected)
        {
            Assert.That(MoneyScale.ToStorage(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)),
                Is.EqualTo(expected));
        }

        [TestCase("0")]
        [TestCase("1234.5678")]
        [TestCase("-0.0001")]
        [TestCase("922337203685477.5807")]
        public void RoundTrip_IsLossless(string value)
        {
            decimal d = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

            Assert.That(MoneyScale.FromStorage(MoneyScale.ToStorage(d)), Is.EqualTo(d));
        }

        [Test]
        public void ToStorage_RefusesMoreThanFourDecimalPlaces_RatherThanSilentlyRounding()
        {
            // Silent rounding is how a cent goes missing and nobody can say where. A money value
            // finer than SQL Server's money can express is a caller bug, not a storage detail.
            Assert.Throws<ArgumentOutOfRangeException>(() => MoneyScale.ToStorage(0.00005m));
        }

        [Test]
        public void ToStorage_RefusesAValueOutsideTheRepresentableRange()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MoneyScale.ToStorage(1000000000000000m));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj --filter "FullyQualifiedName~MoneyScaleTests"`
Expected: FAIL to compile — `CS0103: The name 'MoneyScale' does not exist in the current context`.

- [ ] **Step 3: Write `MoneyScale`**

Create `Source/WPF/MyMoney.Business/Store/MoneyScale.cs`:
```csharp
using System;

namespace Walkabout.Data
{
    /// <summary>
    /// Money is stored as an INTEGER count of ten-thousandths on every engine.
    ///
    /// STRICT tables (spec section 1.7.1, adopted) allow only INT/INTEGER/REAL/TEXT/BLOB/ANY, so
    /// the "money" column type today's reflection-to-DDL emits is illegal. REAL loses exactness.
    /// TEXT is exact but not SQL-summable, which would kill section 3.3 stage 1's whole point -
    /// filtering and subtotaling happening in SQL rather than in memory.
    ///
    /// INTEGER ten-thousandths is exact, summable, and is LITERALLY SQL Server's money
    /// representation: money is an int64 of ten-thousandths whose maximum,
    /// 922,337,203,685,477.5807, is Int64.MaxValue / 10000. So adopting this on SQLite converges
    /// the two engines rather than diverging them - spec section 6.8 rule 1.
    /// </summary>
    public static class MoneyScale
    {
        public const int Scale = 4;

        private const decimal Factor = 10000m;
        private const decimal MaxValue = 922337203685477.5807m;
        private const decimal MinValue = -922337203685477.5808m;

        public static long ToStorage(decimal value)
        {
            if (value < MinValue || value > MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Money values must be between {MinValue} and {MaxValue} - the range SQL Server's "
                    + "money type can hold, which this storage encoding matches exactly.");
            }

            decimal scaled = value * Factor;
            if (decimal.Truncate(scaled) != scaled)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Money values carry at most {Scale} decimal places. Silent rounding here is how "
                    + "a fraction of a cent goes missing with nobody able to say where.");
            }

            return (long)scaled;
        }

        public static decimal FromStorage(long stored) => stored / Factor;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj --filter "FullyQualifiedName~MoneyScaleTests"`
Expected: PASS — 14 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Business/Store/MoneyScale.cs Source/WPF/MyMoney.Tests.Business/Store/MoneyScaleTests.cs
git commit -m "$(cat <<'EOF'
feat(data): MoneyScale - money stored as INTEGER ten-thousandths on every engine

Slice 2, task 4. STRICT tables forbid the "money" column type today's
reflection-to-DDL emits; REAL loses exactness and TEXT is not SQL-summable,
which would defeat SQL-side subtotaling. INTEGER ten-thousandths is exact,
summable, and is literally SQL Server's own money representation, so adopting
it on SQLite converges the engines rather than diverging them.

Refuses out-of-range values and values finer than four decimal places rather
than silently rounding.

Spec: sections 1.7.1, 3.3, 6.8 rule 1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: `MyMoney.Data.Sqlite.Provisioning`, the connection factory, and `MyMoney.Tests.Data`

**Files:**
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/MyMoney.Data.Sqlite.Provisioning.csproj`
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteConnectionFactory.cs`
- Create: `Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteConnectionFactoryTests.cs`

**Interfaces:**
- Consumes: `System.Data.SQLite`.
- Produces: `static class SqliteConnectionFactory` (namespace `Walkabout.Data.Sqlite.Provisioning`) with `const string InMemoryDataSource = ":memory:"`, `static bool IsInMemory(string dataSource)`, `static SQLiteConnection Open(string dataSource)`.

**The gotcha this task exists to pin.** `PRAGMA journal_mode=WAL` is **not applicable to an in-memory database** — it reports `memory`. T-1 (§2.4) makes `:memory:` the default business-test store, so a factory that treats a non-`wal` result as failure surfaces as a confusing first-run error. §2.4 names this explicitly; this task makes it a test.

- [ ] **Step 1: Create both projects**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/MyMoney.Data.Sqlite.Provisioning.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>Walkabout.Data.Sqlite.Provisioning</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <!-- SHIPPED. Creating a file is not a privileged act in any meaningful sense, and per-test
         provisioning speed is load-bearing, so SQLite provisioning stays in-process - spec
         section 1's Recommendation. -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />
    <PackageReference Include="System.Data.SQLite" Version="2.0.4" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Schema\*.sql" />
  </ItemGroup>
</Project>
```

Create `Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Walkabout.Tests.Data</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="NUnit" Version="4.6.1" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
    <PackageReference Include="NUnit.Analyzers" Version="4.14.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
    <ProjectReference Include="..\MyMoney.Data.Sqlite.Provisioning\MyMoney.Data.Sqlite.Provisioning.csproj" />
  </ItemGroup>
</Project>
```

Create the (initially empty) resource folder so the `EmbeddedResource` glob resolves:
```bash
mkdir -p Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Data.Sqlite.Provisioning/MyMoney.Data.Sqlite.Provisioning.csproj
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj
```

- [ ] **Step 2: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteConnectionFactoryTests.cs`:
```csharp
using System;
using System.Data.SQLite;
using System.IO;
using NUnit.Framework;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteConnectionFactoryTests
    {
        private static string Scalar(SQLiteConnection c, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, c))
            {
                return Convert.ToString(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void Open_OnAFile_AppliesWalForeignKeysAndBusyTimeout()
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".mmdb");
            try
            {
                using (SQLiteConnection c = SqliteConnectionFactory.Open(path))
                {
                    Assert.That(Scalar(c, "PRAGMA journal_mode;"), Is.EqualTo("wal").IgnoreCase);
                    Assert.That(Scalar(c, "PRAGMA foreign_keys;"), Is.EqualTo("1"));
                    Assert.That(Scalar(c, "PRAGMA busy_timeout;"), Is.EqualTo("5000"));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Open_OnAnInMemoryDatabase_SucceedsAndDoesNotDemandWal()
        {
            // PRAGMA journal_mode=WAL is not applicable to an in-memory database - it reports
            // "memory". T-1 makes :memory: the default business-test store, so a factory that
            // treated a non-wal result as failure would surface as a confusing first-run error.
            // Spec section 2.4, "Known gotcha, from reading Connect()".
            using (SQLiteConnection c = SqliteConnectionFactory.Open(SqliteConnectionFactory.InMemoryDataSource))
            {
                Assert.That(Scalar(c, "PRAGMA journal_mode;"), Is.EqualTo("memory").IgnoreCase);
                Assert.That(Scalar(c, "PRAGMA foreign_keys;"), Is.EqualTo("1"));
            }
        }

        [Test]
        public void TheBundledEngineSupportsEverythingTheDesignAdopts()
        {
            // RETURNING (3.35), json_each (3.38), STRICT (3.37), PRAGMA table_list (3.37),
            // VACUUM INTO (3.27). Spec section 0.1 verified 3.53.4; this keeps that true.
            using (SQLiteConnection c = SqliteConnectionFactory.Open(SqliteConnectionFactory.InMemoryDataSource))
            {
                var version = new Version(Scalar(c, "SELECT sqlite_version();"));

                Assert.That(version, Is.GreaterThanOrEqualTo(new Version(3, 38, 0)));
            }
        }

        [Test]
        public void IsInMemory_RecognisesTheInMemoryDataSource()
        {
            Assert.That(SqliteConnectionFactory.IsInMemory(":memory:"), Is.True);
            Assert.That(SqliteConnectionFactory.IsInMemory(@"C:\books.mmdb"), Is.False);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: FAIL to compile — `CS0234: The type or namespace name 'Sqlite' does not exist in the namespace 'Walkabout.Data'`.

- [ ] **Step 4: Write `SqliteConnectionFactory`**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteConnectionFactory.cs`:
```csharp
using System;
using System.Data.SQLite;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// Open a connection and apply the pragma sequence.
    ///
    /// DELIBERATELY DUPLICATED in MyMoney.Data.Sqlite (copy B). Spec section 1's Recommendation
    /// is explicit: do NOT create a shared MyMoney.Data.Sqlite.Common assembly and do NOT use
    /// InternalsVisibleTo; accept the ~40 lines of overlap between store and provisioner instead.
    /// Task 22 adds a Tier-0 test that the store assembly does not reference this one, so the
    /// duplication is enforced rather than merely intended. If this file ever grows past
    /// connection construction and open/close, that is the signal the tier boundary was drawn in
    /// the wrong place - move the boundary, do not punch through it (spec section 6.2).
    /// </summary>
    public static class SqliteConnectionFactory
    {
        public const string InMemoryDataSource = ":memory:";

        public static bool IsInMemory(string dataSource) =>
            string.Equals(dataSource, InMemoryDataSource, StringComparison.OrdinalIgnoreCase);

        public static SQLiteConnection Open(string dataSource)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                throw new ArgumentException("A SQLite data source is required.", nameof(dataSource));
            }

            var builder = new SQLiteConnectionStringBuilder { DataSource = dataSource };
            var connection = new SQLiteConnection(builder.ConnectionString);
            connection.Open();

            Execute(connection, "PRAGMA foreign_keys = ON;");

            // busy_timeout: a second writer retries for this long instead of failing immediately
            // with SQLITE_BUSY. 5000ms carried forward from SqliteDatabase.Connect().
            Execute(connection, "PRAGMA busy_timeout = 5000;");

            if (!IsInMemory(dataSource))
            {
                // WAL: readers never block writers, writers never block readers - the concurrency
                // model section 1.6a's human-plus-agent goal needs. NOT applicable to :memory:,
                // which reports "memory"; asking for it there is not an error, but demanding the
                // answer be "wal" would be. Spec section 2.4's known gotcha.
                Execute(connection, "PRAGMA journal_mode = WAL;");
            }

            return connection;
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 4 tests.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.Provisioning Source/WPF/MyMoney.Tests.Data Source/WPF/MyMoney.sln
git commit -m "$(cat <<'EOF'
feat(data): MyMoney.Data.Sqlite.Provisioning scaffolding and the connection factory

Slice 2, task 5. Pins the pragma sequence (foreign_keys, busy_timeout, and WAL
only where WAL applies) and the in-memory gotcha the spec flagged: journal_mode
is "memory" on a :memory: database, and T-1 makes that the default
business-test store, so demanding "wal" would fail every business test with a
confusing message.

SqliteConnectionFactory is deliberately duplicated into MyMoney.Data.Sqlite in
task 11 rather than shared - the spec rules out both a Common assembly and
InternalsVisibleTo, and task 22 enforces the non-reference.

Spec: sections 1 (Recommendation), 1.6a, 2.4, 6.2.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: the versioned step executor — `__SchemaHistory`, `CurrentVersion`, `ApplyTo`

**Files:**
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SchemaStep.cs`
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SchemaStepCatalog.cs`
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs`
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0001_create_online_accounts.sql`
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0002_create_categories.sql`
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0003_create_accounts.sql`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaExecutorTests.cs`

**Interfaces:**
- Consumes: `SqliteConnectionFactory` (Task 5), `IMoneyStoreProvisioner` / `StoreIdentity` / `SchemaVerifyResult` (Tasks 1, 3).
- Produces:
  - `sealed record SchemaStep(int Version, string Name, string Sql, string ChecksumHash)`
  - `static class SchemaStepCatalog` with `static IReadOnlyList<SchemaStep> All { get; }` and `static int LatestVersion { get; }`
  - `sealed class SqliteMoneyStoreProvisioner : IMoneyStoreProvisioner` with `static SqliteMoneyStoreProvisioner Open(SqliteProvisioningOptions options)`
  - `sealed record SqliteProvisioningOptions(string DisplayName, string DataSource, bool IsTestDatabase)`

**Design notes that the code below depends on:**

- **The ledger is a table, not `PRAGMA user_version`** (§1.5 S-2). `user_version` is one 32-bit integer; it cannot say *which* steps produced a version, when, by whom, or whether the step-7 SQL in this build is the step-7 SQL that ran.
- **`__SchemaHistory` is step 0**, created by the executor before the ledger exists to record it — the one genuine bootstrap exception, and the same exception on both engines.
- **One transaction per step, with the ledger row written inside it** (§1.5 S-3 rule 3). A failure at step 9 of 12 must leave the database at version 8, not at "version 0 but actually partly at 9." This is why `ApplyTo` loops rather than wrapping the whole range.
- **Checksums normalise line endings to `\n` before hashing.** `.sql` files are checked out with platform line endings; a CRLF/LF difference between two machines must not read as an edited step. This is the same class of trap CLAUDE.md records for `core.autocrlf` and `.gitattributes`.
- **Steps are applied with `BEGIN IMMEDIATE`** (`BeginTransaction(deferredLock: false)`), so the write lock is taken up front rather than upgraded mid-transaction.

- [ ] **Step 1: Write the three schema steps**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0001_create_online_accounts.sql`:
```sql
-- Step 1. FK target for Accounts.OnlineAccount.
-- Deliberately minimal: this table's full column set arrives as its own numbered step in the
-- slice that brings the OnlineAccount root. A table created by step N and extended by step M is
-- exactly what the step mechanism is for - spec section 1.5, S-3 rule 4.
CREATE TABLE IF NOT EXISTS OnlineAccounts (
    Id      INTEGER NOT NULL PRIMARY KEY,
    Name    TEXT    NOT NULL,
    Version INTEGER NOT NULL DEFAULT 1
) STRICT;
```

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0002_create_categories.sql`:
```sql
-- Step 2. FK target for Accounts.CategoryIdForPrincipal / CategoryIdForInterest.
-- Minimal for the same reason as step 1.
CREATE TABLE IF NOT EXISTS Categories (
    Id      INTEGER NOT NULL PRIMARY KEY,
    Name    TEXT    NOT NULL,
    Version INTEGER NOT NULL DEFAULT 1
) STRICT;
```

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0003_create_accounts.sql`:
```sql
-- Step 3. The Plan A vertical's one fully-populated table.
--
-- Id is INTEGER (64-bit) because IAggregateRoot.Id is long - do NOT let a fresh schema narrow it
-- to INT because that is what the old tables said (spec section 5).
--
-- OpeningBalance is INTEGER ten-thousandths, which is exactly SQL Server's money representation
-- (see MoneyScale). LastSync/LastBalance are TEXT 'yyyy-MM-dd HH:mm:ss.fff' (lexicographically
-- sortable). SyncGuid is TEXT "D".
--
-- OnlineAccount / CategoryIdForPrincipal / CategoryIdForInterest exist as real, nullable foreign
-- keys even though Plan A can never set them non-null: no slice in 1-7 produces an OnlineAccount
-- or Category root, so the null round trip is lossless HERE. The slice that brings those roots
-- must revisit AccountRowCodec at the same time.
CREATE TABLE IF NOT EXISTS Accounts (
    Id                     INTEGER NOT NULL PRIMARY KEY,
    AccountId              TEXT,
    OfxAccountId           TEXT,
    Name                   TEXT    NOT NULL,
    Type                   INTEGER NOT NULL,
    Description            TEXT,
    OnlineAccount          INTEGER REFERENCES OnlineAccounts (Id),
    OpeningBalance         INTEGER NOT NULL,
    LastSync               TEXT,
    LastBalance            TEXT,
    SyncGuid               TEXT,
    Flags                  INTEGER,
    Currency               TEXT,
    WebSite                TEXT,
    ReconcileWarning       INTEGER,
    CategoryIdForPrincipal INTEGER REFERENCES Categories (Id),
    CategoryIdForInterest  INTEGER REFERENCES Categories (Id),
    Version                INTEGER NOT NULL DEFAULT 1
) STRICT;
```

- [ ] **Step 2: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaExecutorTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SchemaExecutorTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory()
        {
            return SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "test", SqliteConnectionFactory.InMemoryDataSource, true));
        }

        [Test]
        public void Catalog_IsContiguouslyNumberedFromOne()
        {
            var versions = SchemaStepCatalog.All.Select(s => s.Version).ToList();

            Assert.That(versions, Is.EqualTo(Enumerable.Range(1, versions.Count).ToList()));
            Assert.That(SchemaStepCatalog.LatestVersion, Is.EqualTo(versions.Last()));
        }

        [Test]
        public void Catalog_ChecksumsAreLineEndingIndependent()
        {
            // A CRLF/LF difference between two checkouts must not read as an edited step.
            SchemaStep step = SchemaStepCatalog.All[0];

            Assert.That(step.ChecksumHash, Is.EqualTo(SchemaStep.ComputeChecksum(step.Sql.Replace("\n", "\r\n"))));
        }

        [Test]
        public void CurrentVersion_OnAnEmptyDatabase_IsZero()
        {
            using (var p = OpenInMemory())
            {
                Assert.That(p.CurrentVersion(), Is.EqualTo(0));
            }
        }

        [Test]
        public void ApplyTo_FromZero_AppliesEveryStepAndRecordsEachInTheLedger()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(p.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
                Assert.That(p.AppliedSteps().Select(s => s.Version),
                    Is.EqualTo(SchemaStepCatalog.All.Select(s => s.Version)));
                Assert.That(p.AppliedSteps().All(s => !string.IsNullOrEmpty(s.AppliedBy)), Is.True);
            }
        }

        [Test]
        public void ApplyTo_IsIdempotent_ASecondCallAppliesNothing()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(p.AppliedSteps(), Has.Count.EqualTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void ApplyTo_StopsAtTheRequestedVersion()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(2);

                Assert.That(p.CurrentVersion(), Is.EqualTo(2));
                Assert.That(TableExists(p, "OnlineAccounts"), Is.True);
                Assert.That(TableExists(p, "Accounts"), Is.False);
            }
        }

        [Test]
        public void ApplyTo_CreatesAccountsAsAStrictTableWithA64BitId()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                using (var cmd = new SQLiteCommand(
                    "SELECT strict FROM pragma_table_list WHERE name = 'Accounts';", p.Connection))
                {
                    Assert.That(Convert.ToInt32(cmd.ExecuteScalar()), Is.EqualTo(1));
                }

                using (var cmd = new SQLiteCommand(
                    "SELECT type FROM pragma_table_info('Accounts') WHERE name = 'Id';", p.Connection))
                {
                    Assert.That(Convert.ToString(cmd.ExecuteScalar()), Is.EqualTo("INTEGER"));
                }
            }
        }

        [Test]
        public void ApplyTo_ToALowerVersionThanCurrent_IsANoOpNotADowngrade()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.ApplyTo(1);

                Assert.That(p.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void ApplyTo_AboveTheLatestKnownVersion_Throws()
        {
            using (var p = OpenInMemory())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => p.ApplyTo(SchemaStepCatalog.LatestVersion + 1));
            }
        }

        private static bool TableExists(SqliteMoneyStoreProvisioner p, string name)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @n;", p.Connection))
            {
                cmd.Parameters.AddWithValue("@n", name);
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SchemaExecutorTests"`
Expected: FAIL to compile — `CS0103: The name 'SchemaStepCatalog' does not exist in the current context`.

- [ ] **Step 4: Write `SchemaStep` and `SchemaStepCatalog`**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SchemaStep.cs`:
```csharp
using System;
using System.Security.Cryptography;
using System.Text;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// One numbered, immutable unit of DDL. A mistake in step 7 is fixed by step 8, never by
    /// editing step 7 - spec section 1.5, S-3 rule 1. ChecksumHash is what makes that rule
    /// OBSERVABLE during a phase where breaking it is otherwise free.
    /// </summary>
    public sealed record SchemaStep(int Version, string Name, string Sql, string ChecksumHash)
    {
        /// <summary>
        /// SHA-256 over the step SQL with line endings normalised to \n.
        ///
        /// The normalisation is load-bearing, not tidiness: .sql files are checked out with
        /// platform line endings, so without it the same step would hash differently on two
        /// machines and every database paved by the other one would look like it had run an
        /// edited step. Same class of trap CLAUDE.md records for core.autocrlf + .gitattributes.
        /// </summary>
        public static string ComputeChecksum(string sql)
        {
            string normalised = sql.Replace("\r\n", "\n").Replace("\r", "\n");
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
            return Convert.ToHexString(hash);
        }
    }
}
```

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SchemaStepCatalog.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// The ordered step list, loaded once from embedded resources named
    /// Walkabout.Data.Sqlite.Provisioning.Schema.NNNN_name.sql.
    ///
    /// This is the SQLite analogue of SQL Server's deployed Schema/*.sql procs (spec section 1.5,
    /// S-5 step 2): the DDL lives in a versioned, auditable artifact rather than in string-building
    /// C#. What is NOT identical across engines is where the executor's control flow physically
    /// lives, and no amount of design makes that identical, because SQLite has no server.
    /// </summary>
    public static class SchemaStepCatalog
    {
        private const string ResourcePrefix = "Walkabout.Data.Sqlite.Provisioning.Schema.";

        public static IReadOnlyList<SchemaStep> All { get; } = Load();

        public static int LatestVersion { get; } = All.Count == 0 ? 0 : All[All.Count - 1].Version;

        private static IReadOnlyList<SchemaStep> Load()
        {
            Assembly assembly = typeof(SchemaStepCatalog).Assembly;
            var steps = new List<SchemaStep>();

            foreach (string name in assembly.GetManifestResourceNames()
                         .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                                     && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = name.Substring(ResourcePrefix.Length);
                int underscore = fileName.IndexOf('_');
                if (underscore <= 0
                    || !int.TryParse(fileName.Substring(0, underscore), NumberStyles.None,
                                     CultureInfo.InvariantCulture, out int version))
                {
                    throw new InvalidOperationException(
                        $"Schema resource '{fileName}' is not named NNNN_description.sql.");
                }

                string sql;
                using (Stream stream = assembly.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                {
                    sql = reader.ReadToEnd();
                }

                steps.Add(new SchemaStep(
                    version,
                    Path.GetFileNameWithoutExtension(fileName),
                    sql,
                    SchemaStep.ComputeChecksum(sql)));
            }

            steps.Sort((a, b) => a.Version.CompareTo(b.Version));

            // A gap or a duplicate means two branches both claimed the same step number, which is
            // the merge accident this numbering exists to make loud rather than silent.
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Version != i + 1)
                {
                    throw new InvalidOperationException(
                        $"Schema steps must be contiguous from 1. Expected step {i + 1} but found "
                        + $"{steps[i].Version} ('{steps[i].Name}').");
                }
            }

            return steps;
        }
    }
}
```

- [ ] **Step 5: Write `SqliteMoneyStoreProvisioner` (ledger + `CurrentVersion` + `ApplyTo`)**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace Walkabout.Data.Sqlite.Provisioning
{
    public sealed record SqliteProvisioningOptions(string DisplayName, string DataSource, bool IsTestDatabase);

    /// <summary>One recorded row of __SchemaHistory.</summary>
    public sealed record AppliedStep(int Version, string StepName, string ChecksumHash, string AppliedUtc, string AppliedBy);

    /// <summary>
    /// The SQLite half of spec section 1.5's one mechanism. Creating a database is ApplyTo(N) from
    /// version 0; nuke-and-pave is DropAll() then ApplyTo(N) and is then LITERALLY
    /// indistinguishable from creating a new database; the eventual production upgrade is
    /// ApplyTo(N) from version M. The upgrade path is therefore exercised on every single fresh
    /// creation, which is the whole trick.
    /// </summary>
    public sealed class SqliteMoneyStoreProvisioner : IMoneyStoreProvisioner
    {
        private readonly SqliteProvisioningOptions options;
        private readonly SQLiteConnection connection;

        private SqliteMoneyStoreProvisioner(SqliteProvisioningOptions options, SQLiteConnection connection)
        {
            this.options = options;
            this.connection = connection;
        }

        public static SqliteMoneyStoreProvisioner Open(SqliteProvisioningOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new SqliteMoneyStoreProvisioner(options, SqliteConnectionFactory.Open(options.DataSource));
        }

        /// <summary>
        /// Exposed for the provisioning tier's own tests and for the test tier, which runs its
        /// ladders over the same open handle. Not on IMoneyStoreProvisioner: the port must not
        /// hand a raw connection to the business layer.
        /// </summary>
        public SQLiteConnection Connection => this.connection;

        public string DataSource => this.options.DataSource;

        public int LatestKnownVersion => SchemaStepCatalog.LatestVersion;

        public StoreIdentity Identity => new StoreIdentity(
            this.options.DisplayName,
            DbFlavor.Sqlite,
            this.options.IsTestDatabase,
            this.CurrentVersion());

        public int CurrentVersion()
        {
            if (!this.HistoryTableExists())
            {
                return 0;
            }

            using (var cmd = new SQLiteCommand(
                "SELECT COALESCE(MAX(Version), 0) FROM __SchemaHistory;", this.connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        public IReadOnlyList<AppliedStep> AppliedSteps()
        {
            var applied = new List<AppliedStep>();
            if (!this.HistoryTableExists())
            {
                return applied;
            }

            using (var cmd = new SQLiteCommand(
                "SELECT Version, StepName, ChecksumHash, AppliedUtc, AppliedBy "
                + "FROM __SchemaHistory ORDER BY Version;", this.connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    applied.Add(new AppliedStep(
                        reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetString(4)));
                }
            }

            return applied;
        }

        public void ApplyTo(int targetVersion)
        {
            if (targetVersion < 0 || targetVersion > SchemaStepCatalog.LatestVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetVersion),
                    targetVersion,
                    $"This build carries schema steps 1..{SchemaStepCatalog.LatestVersion}.");
            }

            this.EnsureHistoryTable();
            this.VerifyNoDriftInAppliedSteps();

            int current = this.CurrentVersion();

            foreach (SchemaStep step in SchemaStepCatalog.All)
            {
                if (step.Version <= current || step.Version > targetVersion)
                {
                    continue;
                }

                // One transaction PER STEP, with the ledger row written inside it. A failure at
                // step 9 of 12 must leave the database at version 8, not at "version 0 but
                // actually partly at 9" - spec section 1.5, S-3 rule 3. BEGIN IMMEDIATE takes the
                // write lock up front rather than upgrading mid-transaction.
                using (SQLiteTransaction tx = this.connection.BeginTransaction(deferredLock: false))
                {
                    try
                    {
                        using (var cmd = new SQLiteCommand(step.Sql, this.connection, tx))
                        {
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SQLiteCommand(
                            "INSERT INTO __SchemaHistory (Version, StepName, ChecksumHash, AppliedUtc, AppliedBy) "
                            + "VALUES (@v, @n, @c, @u, @b);", this.connection, tx))
                        {
                            cmd.Parameters.AddWithValue("@v", step.Version);
                            cmd.Parameters.AddWithValue("@n", step.Name);
                            cmd.Parameters.AddWithValue("@c", step.ChecksumHash);
                            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
                            cmd.Parameters.AddWithValue("@b", Environment.UserName ?? "unknown");
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                    catch (Exception original)
                    {
                        try
                        {
                            tx.Rollback();
                        }
                        catch (Exception rollbackFailure)
                        {
                            throw new InvalidOperationException(
                                $"Rollback failed after schema step {step.Version} ('{step.Name}') failed: "
                                + original.Message,
                                rollbackFailure);
                        }

                        throw new InvalidOperationException(
                            $"Schema step {step.Version} ('{step.Name}') failed; the database is still at "
                            + $"version {step.Version - 1}.",
                            original);
                    }
                }
            }
        }

        // Verify, DropAll, Backup and Delete arrive in Tasks 8, 18, 9 and 9.
        public SchemaVerifyResult Verify() => throw new NotImplementedException("Task 8.");

        public void DropAll() => throw new NotImplementedException("Task 18.");

        public void Backup(string destinationPath) => throw new NotImplementedException("Task 9.");

        public void Delete() => throw new NotImplementedException("Task 9.");

        public void Dispose() => this.connection?.Dispose();

        private bool HistoryTableExists()
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__SchemaHistory';",
                this.connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            }
        }

        /// <summary>
        /// __SchemaHistory is step 0: the executor creates it before the ledger exists to record
        /// it. That is the one genuine bootstrap exception, and it is the same exception on both
        /// engines - spec section 1.5, S-2.
        /// </summary>
        private void EnsureHistoryTable()
        {
            using (var cmd = new SQLiteCommand(
                "CREATE TABLE IF NOT EXISTS __SchemaHistory ("
                + "Version INTEGER NOT NULL PRIMARY KEY,"
                + "StepName TEXT NOT NULL,"
                + "ChecksumHash TEXT NOT NULL,"
                + "AppliedUtc TEXT NOT NULL,"
                + "AppliedBy TEXT NOT NULL) STRICT;",
                this.connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void VerifyNoDriftInAppliedSteps()
        {
            // Body arrives in Task 7.
        }
    }
}
```

> Leaving four members throwing `NotImplementedException` **inside this task only** is deliberate and bounded: `Verify` is filled by Task 8, `Backup` and `Delete` by Task 9, and `DropAll` by Task 18 (it needs `SqliteTableOrder`, which the test tier introduces). Each stub names its task. Nothing outside the provisioning assembly can reach them until then, because no store exists yet.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SchemaExecutorTests"`
Expected: PASS — 9 tests.

- [ ] **Step 7: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.Provisioning Source/WPF/MyMoney.Tests.Data
git commit -m "$(cat <<'EOF'
feat(data): versioned schema executor with the __SchemaHistory ledger

Slice 2, task 6. One mechanism, several entry points: creating a database is
ApplyTo(N) from version 0, so the never-yet-needed upgrade path runs on every
fresh creation. One transaction per step with the ledger row written inside it,
so a failure at step 9 of 12 leaves the database at version 8.

The ledger is a table, not PRAGMA user_version, because a single 32-bit integer
cannot say which steps produced a version, when, by whom, or whether the step-7
SQL in this build is the step-7 SQL that ran. Checksums normalise line endings
before hashing so a CRLF/LF difference between checkouts is not read as an
edited step.

Accounts is STRICT with a 64-bit Id and money as INTEGER ten-thousandths.

Spec: sections 1.5 (S-1 through S-4), 1.7.1, 5.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: the checksum drift refusal (S-3 rule 1, on from day one)

**Files:**
- Modify: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs` (`VerifyNoDriftInAppliedSteps`)
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaDriftTests.cs`

**Interfaces:**
- Consumes: `SchemaDriftException` (Task 3), `AppliedSteps()` (Task 6).
- Produces: `ApplyTo` throws `SchemaDriftException` when a recorded step's checksum no longer matches this build's.

**Why now and not later (§6.7).** Under nuke-and-pave, S-3 rule 1 ("a step is never edited once applied") protects nothing you can see, so it is the first rule to erode — *"change the table definition, re-pave, move on."* The checksum check is the only thing that makes the rule observable during a phase where breaking it is free. Turning it on later means turning it on against a corpus nobody was disciplined about.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaDriftTests.cs`:
```csharp
using System.Data.SQLite;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SchemaDriftTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory() =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "test", SqliteConnectionFactory.InMemoryDataSource, true));

        [Test]
        public void ApplyTo_RefusesWhenAnAlreadyAppliedStepsChecksumNoLongerMatches()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                // Simulate "someone edited step 2 and re-paved somewhere else": the ledger in this
                // database records a checksum this build's step 2 no longer produces.
                using (var cmd = new SQLiteCommand(
                    "UPDATE __SchemaHistory SET ChecksumHash = 'TAMPERED' WHERE Version = 2;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                var ex = Assert.Throws<SchemaDriftException>(() => p.ApplyTo(SchemaStepCatalog.LatestVersion));

                Assert.That(ex.Message, Does.Contain("2"));
                Assert.That(ex.Message, Does.Contain("fixed by a new step"));
            }
        }

        [Test]
        public void ApplyTo_RefusesWhenTheDatabaseRecordsAStepThisBuildDoesNotCarry()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                using (var cmd = new SQLiteCommand(
                    "INSERT INTO __SchemaHistory VALUES (@v, 'from_the_future', 'X', '2026-01-01 00:00:00.000', 'someone');",
                    p.Connection))
                {
                    cmd.Parameters.AddWithValue("@v", SchemaStepCatalog.LatestVersion + 1);
                    cmd.ExecuteNonQuery();
                }

                Assert.Throws<SchemaDriftException>(() => p.ApplyTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void ApplyTo_OnAnUntamperedDatabase_DoesNotThrow()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.DoesNotThrow(() => p.ApplyTo(SchemaStepCatalog.LatestVersion));
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SchemaDriftTests"`
Expected: FAIL — `Expected: <Walkabout.Data.SchemaDriftException> But was: no exception thrown`.

- [ ] **Step 3: Implement `VerifyNoDriftInAppliedSteps`**

Replace the stub body in `SqliteMoneyStoreProvisioner.cs`:
```csharp
        /// <summary>
        /// Spec section 1.5, S-3 rule 1: a step is never edited once it has been applied
        /// ANYWHERE. This is the detection that makes that rule observable. It is on from the
        /// first commit rather than "when it matters", because under nuke-and-pave (section 6.7)
        /// breaking the rule is free and invisible, so it is the first rule to erode - and
        /// turning the check on later means turning it on against a corpus of steps nobody was
        /// disciplined about.
        /// </summary>
        private void VerifyNoDriftInAppliedSteps()
        {
            var known = new Dictionary<int, SchemaStep>();
            foreach (SchemaStep step in SchemaStepCatalog.All)
            {
                known[step.Version] = step;
            }

            foreach (AppliedStep applied in this.AppliedSteps())
            {
                if (!known.TryGetValue(applied.Version, out SchemaStep step))
                {
                    throw new SchemaDriftException(
                        $"'{this.options.DisplayName}' records schema step {applied.Version} "
                        + $"('{applied.StepName}'), which this build does not carry. This database was "
                        + $"paved by a newer build; this one knows steps 1..{SchemaStepCatalog.LatestVersion}.");
                }

                if (!string.Equals(applied.ChecksumHash, step.ChecksumHash, StringComparison.Ordinal))
                {
                    throw new SchemaDriftException(
                        $"Schema step {applied.Version} ('{step.Name}') has been edited since it was "
                        + $"applied to '{this.options.DisplayName}' on {applied.AppliedUtc} by "
                        + $"{applied.AppliedBy}. An applied step is immutable - a mistake in it is "
                        + "fixed by a new step, never by editing it, or every database that already "
                        + "ran it silently disagrees with every database that has not.");
                }
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 16 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaDriftTests.cs
git commit -m "$(cat <<'EOF'
feat(data): refuse to apply steps when an already-applied step has been edited

Slice 2, task 7. The checksum check is on from the first commit rather than
"when it matters": under nuke-and-pave, "never edit an applied step" protects
nothing visible, so it is the first rule to erode, and this detection is the
only thing that makes it observable while breaking it is free.

Also refuses a database that records a step this build does not carry.

Spec: sections 1.5 S-3 rule 1, 1.8 property 1, 6.7.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: `SchemaSnapshot` introspection and `Verify()`

**Files:**
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SchemaSnapshot.cs`
- Modify: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs` (`Verify`)
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaVerifyTests.cs`

**Interfaces:**
- Consumes: `SchemaVerifyResult`, `SchemaDifference` (Task 3); `SchemaStepCatalog`, `SqliteProvisioningOptions` (Task 6).
- Produces: `sealed class SchemaSnapshot` with `static SchemaSnapshot Capture(SQLiteConnection c)`, `IReadOnlyList<string> Facts { get; }`, `static IReadOnlyList<SchemaDifference> Diff(SchemaSnapshot expected, SchemaSnapshot actual)`.

**The design decision this task makes.** `Verify()`'s *expected* object set is **not** a hand-maintained list. It is produced by paving a scratch `:memory:` database to the target's current version with the very same executor, then introspecting that. Three things follow: it is hand-list-free by construction (issue #34's lesson, §2.6.4); it is the same comparison slice 2b's equality test performs, so both share one code path; and a `:memory:` pave is sub-millisecond-to-low-millisecond (§2.4), so a `Verify()` call is cheap enough to be an assertion rather than a ritual.

The canonical form uses `PRAGMA table_list` (reports `strict`), `table_info`, `index_list`/`index_info`, `foreign_key_list`, and `sqlite_master` for views and triggers — the structured introspection §1.7.1 adopts, replacing `TableExists`'s `sqlite_master.sql` text-scraping.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaVerifyTests.cs`:
```csharp
using System.Data.SQLite;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SchemaVerifyTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory() =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "test", SqliteConnectionFactory.InMemoryDataSource, true));

        [Test]
        public void Verify_OnAFreshlyPavedDatabase_ReportsNoDrift()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                SchemaVerifyResult result = p.Verify();

                Assert.That(result.Version, Is.EqualTo(SchemaStepCatalog.LatestVersion));
                Assert.That(result.IsClean, Is.True,
                    string.Join("; ", result.Differences.Select(d => $"{d.ObjectKind} {d.ObjectName}: expected '{d.Expected}' actual '{d.Actual}'")));
            }
        }

        [Test]
        public void Verify_NoticesAMissingIndex()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                using (var cmd = new SQLiteCommand("DROP INDEX IF EXISTS IX_Accounts_Name;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                // Before Task 10 adds IX_Accounts_Name this drop is a no-op and Verify stays
                // clean; after Task 10 it goes red. Either way the assertion below is the point:
                // Verify's expected set includes indexes, which is what issue #34 needed.
                Assert.That(SchemaSnapshot.Capture(p.Connection).Facts.Any(f => f.StartsWith("index:")),
                    Is.True, "Verify must introspect indexes, not just tables.");
            }
        }

        [Test]
        public void Verify_NoticesAnAddedColumn()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                using (var cmd = new SQLiteCommand("ALTER TABLE Accounts ADD COLUMN Rogue TEXT;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                SchemaVerifyResult result = p.Verify();

                Assert.That(result.IsClean, Is.False);
                Assert.That(result.Differences.Any(d => d.ObjectName.Contains("Rogue")), Is.True);
            }
        }

        [Test]
        public void Verify_NoticesADroppedTable()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                using (var cmd = new SQLiteCommand("DROP TABLE Accounts;", p.Connection))
                {
                    cmd.ExecuteNonQuery();
                }

                Assert.That(p.Verify().IsClean, Is.False);
            }
        }

        [Test]
        public void Snapshot_RecordsThatAccountsIsStrict()
        {
            using (var p = OpenInMemory())
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(SchemaSnapshot.Capture(p.Connection).Facts,
                    Has.Some.EqualTo("table:Accounts|strict=1"));
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SchemaVerifyTests"`
Expected: FAIL to compile — `CS0103: The name 'SchemaSnapshot' does not exist in the current context`.

- [ ] **Step 3: Write `SchemaSnapshot`**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SchemaSnapshot.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using Walkabout.Data;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// The whole schema reduced to an order-independent set of canonical one-line facts, captured
    /// by structured introspection (PRAGMA table_list/table_info/index_list/index_info/
    /// foreign_key_list plus sqlite_master for views and triggers) rather than by scraping
    /// sqlite_master.sql text the way TableExists does today - spec section 1.7.1.
    ///
    /// Views and triggers are included deliberately: a paved database missing a view must report
    /// drift, or the first symptom is an IMoneyQuery call failing at runtime (spec section 2.7.1).
    /// </summary>
    public sealed class SchemaSnapshot
    {
        private SchemaSnapshot(IReadOnlyList<string> facts)
        {
            this.Facts = facts;
        }

        public IReadOnlyList<string> Facts { get; }

        public static SchemaSnapshot Capture(SQLiteConnection connection)
        {
            var facts = new List<string>();

            foreach ((string table, int strict) in Tables(connection))
            {
                facts.Add($"table:{table}|strict={strict}");

                foreach (string column in Rows(connection,
                    "SELECT name || '|' || type || '|notnull=' || \"notnull\" || '|pk=' || pk || "
                    + "'|default=' || COALESCE(dflt_value, '<none>') FROM pragma_table_info(@t) ORDER BY name;",
                    table))
                {
                    facts.Add($"column:{table}.{column}");
                }

                foreach (string index in Rows(connection,
                    "SELECT il.name || '|unique=' || il.\"unique\" || '|origin=' || il.origin || '|cols=' || "
                    + "(SELECT group_concat(ii.name, ',') FROM pragma_index_info(il.name) ii) "
                    + "FROM pragma_index_list(@t) il ORDER BY il.name;",
                    table))
                {
                    facts.Add($"index:{table}.{index}");
                }

                foreach (string fk in Rows(connection,
                    "SELECT \"from\" || '->' || \"table\" || '.' || COALESCE(\"to\", 'rowid') "
                    + "FROM pragma_foreign_key_list(@t) ORDER BY \"from\";",
                    table))
                {
                    facts.Add($"fk:{table}.{fk}");
                }
            }

            foreach (string obj in Rows(connection,
                "SELECT type || ':' || name || '|' || COALESCE(sql, '') FROM sqlite_master "
                + "WHERE type IN ('view', 'trigger') ORDER BY type, name;", null))
            {
                facts.Add(Normalise(obj));
            }

            facts.Sort(StringComparer.Ordinal);
            return new SchemaSnapshot(facts);
        }

        public static IReadOnlyList<SchemaDifference> Diff(SchemaSnapshot expected, SchemaSnapshot actual)
        {
            var differences = new List<SchemaDifference>();

            foreach (string missing in expected.Facts.Except(actual.Facts, StringComparer.Ordinal))
            {
                differences.Add(new SchemaDifference(Kind(missing), Subject(missing), missing, "<absent>"));
            }

            foreach (string extra in actual.Facts.Except(expected.Facts, StringComparer.Ordinal))
            {
                differences.Add(new SchemaDifference(Kind(extra), Subject(extra), "<absent>", extra));
            }

            return differences;
        }

        private static IEnumerable<(string Name, int Strict)> Tables(SQLiteConnection connection)
        {
            var result = new List<(string, int)>();
            using (var cmd = new SQLiteCommand(
                "SELECT name, strict FROM pragma_table_list WHERE type = 'table' "
                + "AND name NOT LIKE 'sqlite_%' ORDER BY name;", connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    result.Add((reader.GetString(0), Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture)));
                }
            }

            return result;
        }

        private static IEnumerable<string> Rows(SQLiteConnection connection, string sql, string table)
        {
            var result = new List<string>();
            using (var cmd = new SQLiteCommand(sql, connection))
            {
                if (table != null)
                {
                    cmd.Parameters.AddWithValue("@t", table);
                }

                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            result.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Collapse whitespace so a reformatted CREATE VIEW is not reported as drift.</summary>
        private static string Normalise(string text) =>
            string.Join(" ", text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        private static string Kind(string fact) => fact.Substring(0, fact.IndexOf(':'));

        private static string Subject(string fact)
        {
            string rest = fact.Substring(fact.IndexOf(':') + 1);
            int bar = rest.IndexOf('|');
            return bar < 0 ? rest : rest.Substring(0, bar);
        }
    }
}
```

- [ ] **Step 4: Implement `Verify()`**

Replace the `Verify` stub in `SqliteMoneyStoreProvisioner.cs`:
```csharp
        /// <summary>
        /// The expected object set is NOT a hand-maintained list. It is produced by paving a
        /// scratch :memory: database to this database's current version with the very same
        /// executor, then introspecting that. Hand-list-free by construction (issue #34's lesson,
        /// spec section 2.6.4), identical to the comparison slice 2b's equality test performs, and
        /// cheap enough to be an assertion rather than a ritual - a :memory: pave is DDL against
        /// an empty page cache with no fsync and no journal (spec section 2.4).
        /// </summary>
        public SchemaVerifyResult Verify()
        {
            int version = this.CurrentVersion();
            SchemaSnapshot actual = SchemaSnapshot.Capture(this.connection);
            SchemaSnapshot expected = CaptureExpected(version);

            return new SchemaVerifyResult(version, SchemaSnapshot.Diff(expected, actual));
        }

        /// <summary>Pave a throwaway in-memory database to <paramref name="version"/> and introspect it.</summary>
        public static SchemaSnapshot CaptureExpected(int version)
        {
            using (var scratch = Open(new SqliteProvisioningOptions(
                "<expected>", SqliteConnectionFactory.InMemoryDataSource, true)))
            {
                scratch.ApplyTo(version);
                return SchemaSnapshot.Capture(scratch.Connection);
            }
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 21 tests.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.Provisioning Source/WPF/MyMoney.Tests.Data
git commit -m "$(cat <<'EOF'
feat(data): Schema_Verify - structured introspection and a drift report

Slice 2, task 8. The expected object set is derived by paving a scratch
:memory: database to the same version with the same executor and introspecting
it, so there is no hand-maintained list to forget a table in - issue #34's
lesson applied where it would otherwise recur. Tables, columns, indexes,
foreign keys, views and triggers are all compared, and STRICT is part of the
captured fact so the adoption is verified rather than assumed.

Spec: sections 1.5 S-4, 1.7.1, 2.6.4, 2.7.1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: `Backup` (`VACUUM INTO`) and `Delete`

**Files:**
- Modify: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/ProvisionerBackupTests.cs`

**Interfaces:**
- Consumes: `IMoneyStoreProvisioner.Backup/Delete` (Task 3).
- Produces: working `Backup(string destinationPath)` and `Delete()`.

**Why this is in slice 2 rather than unscheduled.** §1.8's third compatibility property is explicit: *"`Backup` must be in the provisioner contract and contract-tested from the start… A backup routine that only appears when production appears is a backup routine that has never been restored from."* §1.7.1's `VACUUM INTO` removes the cost argument — it is transactionally consistent and, unlike close-and-`File.Copy`, does not require closing a live WAL database.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/ProvisionerBackupTests.cs`:
```csharp
using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class ProvisionerBackupTests
    {
        private string dir;

        [SetUp]
        public void SetUp()
        {
            this.dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.dir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(this.dir, true);
            }
            catch (IOException)
            {
            }
        }

        private SqliteMoneyStoreProvisioner OpenFile(string name) =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                name, Path.Combine(this.dir, name + ".mmdb"), true));

        [Test]
        public void Backup_ProducesAFileThatOpensAtTheSameSchemaVersion()
        {
            string backupPath = Path.Combine(this.dir, "backup.mmdb");

            using (var p = OpenFile("books"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.Backup(backupPath);
            }

            Assert.That(File.Exists(backupPath), Is.True);

            using (var restored = SqliteMoneyStoreProvisioner.Open(
                new SqliteProvisioningOptions("backup", backupPath, true)))
            {
                Assert.That(restored.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
                Assert.That(restored.Verify().IsClean, Is.True);
            }
        }

        [Test]
        public void Backup_OverwritesAnExistingDestination()
        {
            // "IMoneyStoreProvisioner.Backup must be able to overwrite" - spec section 3.2.
            // VACUUM INTO refuses an existing file, so the implementation has to clear it first.
            string backupPath = Path.Combine(this.dir, "backup.mmdb");
            File.WriteAllText(backupPath, "stale");

            using (var p = OpenFile("books"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.DoesNotThrow(() => p.Backup(backupPath));
            }

            using (var restored = SqliteMoneyStoreProvisioner.Open(
                new SqliteProvisioningOptions("backup", backupPath, true)))
            {
                Assert.That(restored.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion));
            }
        }

        [Test]
        public void Backup_OfAnInMemoryDatabase_Works()
        {
            // The T-1 fixture backs a prepared :memory: template up to clone it per test
            // (spec section 2.4), so this path is not hypothetical.
            string backupPath = Path.Combine(this.dir, "from-memory.mmdb");

            using (var p = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "mem", SqliteConnectionFactory.InMemoryDataSource, true)))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                p.Backup(backupPath);
            }

            Assert.That(File.Exists(backupPath), Is.True);
        }

        [Test]
        public void Delete_RemovesTheFile()
        {
            string path;
            using (var p = OpenFile("doomed"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                path = p.DataSource;
                p.Delete();
            }

            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void Backup_ToAnEmptyPath_Throws()
        {
            using (var p = OpenFile("books"))
            {
                Assert.Throws<ArgumentException>(() => p.Backup("  "));
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~ProvisionerBackupTests"`
Expected: FAIL — `System.NotImplementedException : Task 9.`

- [ ] **Step 3: Implement `Backup` and `Delete`**

Replace both stubs in `SqliteMoneyStoreProvisioner.cs`:
```csharp
        /// <summary>
        /// VACUUM INTO rather than close-and-File.Copy: it is transactionally consistent and does
        /// not require closing a live WAL database - spec sections 1.7.1 and 3.2. The filename is
        /// a bound parameter, not concatenated (R-CRUD-1).
        ///
        /// VACUUM INTO refuses an existing destination, so an overwrite is explicit. Spec section
        /// 3.2 requires Backup to be able to overwrite.
        ///
        /// Task 21 puts TestDatabaseGuard in front of the destructive operations on this class;
        /// Backup is not one of them - it only ever writes a NEW file.
        /// </summary>
        public void Backup(string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("A backup destination path is required.", nameof(destinationPath));
            }

            if (System.IO.File.Exists(destinationPath))
            {
                System.IO.File.Delete(destinationPath);
            }

            using (var cmd = new SQLiteCommand("VACUUM INTO @path;", this.connection))
            {
                cmd.Parameters.AddWithValue("@path", destinationPath);
                cmd.ExecuteNonQuery();
            }
        }

        public void Delete()
        {
            string dataSource = this.options.DataSource;
            this.connection.Close();

            if (!SqliteConnectionFactory.IsInMemory(dataSource) && System.IO.File.Exists(dataSource))
            {
                System.IO.File.Delete(dataSource);
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 26 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs Source/WPF/MyMoney.Tests.Data/Sqlite/ProvisionerBackupTests.cs
git commit -m "$(cat <<'EOF'
feat(data): provisioner Backup via VACUUM INTO, and Delete

Slice 2, task 9. Backup is in the contract and contract-tested from the start
because a backup routine that only appears when production appears is one that
has never been restored from - the spec's third compatibility property.
VACUUM INTO is transactionally consistent and needs no close, unlike the
close-and-File.Copy the design originally named, and the destination is a bound
parameter. Overwrite is supported explicitly because VACUUM INTO refuses an
existing file.

Spec: sections 1.7.1, 1.8 property 3, 3.2.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 10 (slice 2b): the fresh-vs-upgraded schema-equality test — issue #34 as a red test

**Files:**
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0004_add_accounts_name_index.sql`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaEqualityTests.cs`

**Interfaces:**
- Consumes: `SchemaStepCatalog`, `SchemaSnapshot`, `SqliteMoneyStoreProvisioner` (Tasks 6, 8).
- Produces: no new types. Step 4 is deliberately **an index added to a table created by an earlier step** — the exact shape of [issue #34](https://github.com/markabrandjord/MyMoney.Net/issues/34).

**What this proves.** Today's `CreateOrUpdateTable` only runs `GetCreateIndexScripts` inside the *"table doesn't exist yet"* branch, so an index added to an already-existing table is silently never created. The new mechanism removes the hole structurally because the branch no longer exists — a fresh database at version 0 applies steps 1..4 and gets the index because step 4 ran; an existing database at version 3 applies step 4 and gets the index because step 4 ran. **Same step, same executor, same reason, no branch that can skip one case and not the other.** This test is what keeps that true, and per §1.5 S-1 it is also what keeps the never-yet-needed upgrade path honest during a phase in which nobody upgrades anything.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaEqualityTests.cs`:
```csharp
using System.Linq;
using NUnit.Framework;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    /// <summary>
    /// Slice 2b. The test that would actually have caught issue #34, generalised from indexes to
    /// every schema object. It runs on every build from here on - spec section 1.5, S-4 point 2.
    /// </summary>
    [TestFixture]
    public class SchemaEqualityTests
    {
        private static SqliteMoneyStoreProvisioner OpenInMemory(string name) =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                name, SqliteConnectionFactory.InMemoryDataSource, true));

        [Test]
        public void ADatabaseBuiltFreshAtN_IsIdenticalToOneBuiltAtNMinusOneThenUpgraded()
        {
            int n = SchemaStepCatalog.LatestVersion;

            using (var fresh = OpenInMemory("fresh"))
            using (var upgraded = OpenInMemory("upgraded"))
            {
                fresh.ApplyTo(n);

                upgraded.ApplyTo(n - 1);
                upgraded.ApplyTo(n);

                SchemaSnapshot a = SchemaSnapshot.Capture(fresh.Connection);
                SchemaSnapshot b = SchemaSnapshot.Capture(upgraded.Connection);

                var differences = SchemaSnapshot.Diff(a, b);

                Assert.That(differences, Is.Empty,
                    "Fresh-at-N and upgraded-to-N disagree: "
                    + string.Join("; ", differences.Select(d => $"{d.ObjectKind} {d.ObjectName} expected '{d.Expected}' actual '{d.Actual}'")));
            }
        }

        [Test]
        public void EveryIntermediateVersionUpgradesToTheSameSchemaAsAFreshBuild()
        {
            // Not just N-1: a step that is only correct when applied straight after its immediate
            // predecessor is a bug the single-hop version would miss.
            int n = SchemaStepCatalog.LatestVersion;

            for (int start = 0; start < n; start++)
            {
                using (var fresh = OpenInMemory("fresh"))
                using (var upgraded = OpenInMemory("upgraded"))
                {
                    fresh.ApplyTo(n);
                    upgraded.ApplyTo(start);
                    upgraded.ApplyTo(n);

                    Assert.That(
                        SchemaSnapshot.Diff(
                            SchemaSnapshot.Capture(fresh.Connection),
                            SchemaSnapshot.Capture(upgraded.Connection)),
                        Is.Empty,
                        $"Upgrading from version {start} to {n} did not produce a fresh-at-{n} schema.");
                }
            }
        }

        [Test]
        public void TheCatalogContainsAnIndexAddedToAnAlreadyCreatedTable()
        {
            // The issue #34 shape, present on purpose. If a future refactor removes it, the two
            // tests above become much weaker without anything going red - so this pins it.
            using (var p = OpenInMemory("shape"))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.That(SchemaSnapshot.Capture(p.Connection).Facts,
                    Has.Some.StartsWith("index:Accounts.IX_Accounts_Name|"));
            }

            SchemaStep indexStep = SchemaStepCatalog.All.Single(s => s.Name.Contains("accounts_name_index"));
            SchemaStep tableStep = SchemaStepCatalog.All.Single(s => s.Name.Contains("create_accounts"));

            Assert.That(indexStep.Version, Is.GreaterThan(tableStep.Version),
                "The index must be added by a LATER step than the one creating its table - that is "
                + "the case issue #34's CreateOrUpdateTable silently skipped.");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SchemaEqualityTests"`
Expected: FAIL — `TheCatalogContainsAnIndexAddedToAnAlreadyCreatedTable` fails with `System.InvalidOperationException : Sequence contains no matching element` (no such step yet).

- [ ] **Step 3: Add step 4**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0004_add_accounts_name_index.sql`:
```sql
-- Step 4. An index added to a table created by an EARLIER step - the exact shape of issue #34,
-- where CreateOrUpdateTable only emitted index DDL inside its "table doesn't exist yet" branch
-- and therefore silently never created this. Under the step mechanism there is no such branch:
-- a fresh database at version 0 applies steps 1..4 and gets this index because step 4 ran; a
-- database at version 3 applies step 4 and gets this index because step 4 ran. Same step, same
-- executor, same reason. Spec section 1.5, S-4.
CREATE INDEX IF NOT EXISTS IX_Accounts_Name ON Accounts (Name);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 29 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.Provisioning/Schema/0004_add_accounts_name_index.sql Source/WPF/MyMoney.Tests.Data/Sqlite/SchemaEqualityTests.cs
git commit -m "$(cat <<'EOF'
test(data): slice 2b - fresh-vs-upgraded schema equality, issue #34 as a red test

Build a database fresh at N; build another at N-1 (and at every intermediate
version) then upgrade it to N; assert the introspected schemas are identical.
Step 4 adds an index to a table created by step 3, which is precisely the case
CreateOrUpdateTable silently skipped in issue #34 - and a third test pins that
shape so a future refactor cannot weaken the first two without going red.

From here the never-yet-needed upgrade path runs on every build, which the spec
calls the single most important compatibility property in the design.

Spec: sections 1.5 S-1 and S-4, 1.8 property 2.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 11: `MyMoney.Data.Sqlite` — the store handle, `StoreIdentity`, and the open-time version check

**Files:**
- Create: `Source/WPF/MyMoney.Data.Sqlite/MyMoney.Data.Sqlite.csproj`
- Create: `Source/WPF/MyMoney.Data.Sqlite/SqliteConnectionFactory.cs` (copy B — see Task 5)
- Create: `Source/WPF/MyMoney.Data.Sqlite/SqliteStoreOptions.cs`
- Create: `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs`
- Modify: `Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteMoneyStoreOpenTests.cs`

**Interfaces:**
- Consumes: `MoneyStoreBase`, `StoreIdentity`, `SchemaTooNewException` (Tasks 1, 3).
- Produces:
  - `sealed record SqliteStoreOptions(string DisplayName, string DataSource, bool IsTestDatabase)`
  - `sealed class SqliteMoneyStore : MoneyStoreBase` with `static SqliteMoneyStore Open(SqliteStoreOptions options)`, `SQLiteConnection Connection { get; }`, `const int MaxKnownSchemaVersion`

**The open-time check (§1.8's second bullet).** `ApplyTo` handles *"the app is newer than the database."* The reverse — an older binary opening a database at a **higher** version than it knows — needs a refusal, not a best-effort open, and it is cheap now and awkward once databases are out in the world. The spec recommends adding it in slice 3; this is that.

**Why `MaxKnownSchemaVersion` is a `const` in the store and not read from `SchemaStepCatalog`.** The store assembly does not reference the provisioning assembly (§1's Recommendation; Task 22 enforces it). The constant is checked against the catalog by a contract test in Task 17, so the two cannot silently disagree.

- [ ] **Step 1: Create the project and wire the test reference**

Create `Source/WPF/MyMoney.Data.Sqlite/MyMoney.Data.Sqlite.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>Walkabout.Data.Sqlite</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <!--
      SHIPPED, and deliberately does NOT reference MyMoney.Data.Sqlite.Provisioning: the spec's
      section 1 Recommendation rules out both a shared Common assembly and InternalsVisibleTo,
      and accepts ~40 lines of connection-handling duplication instead. Task 22 enforces the
      non-reference. This assembly must contain no DROP TABLE, no VACUUM INTO over an existing
      file, and no DELETE FROM without a WHERE Id= - spec section 1, "How the SQLite facade gets
      its teeth".
    -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />
    <PackageReference Include="System.Data.SQLite" Version="2.0.4" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Data.Sqlite/MyMoney.Data.Sqlite.csproj
dotnet add Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj reference Source/WPF/MyMoney.Data.Sqlite/MyMoney.Data.Sqlite.csproj
```

- [ ] **Step 2: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteMoneyStoreOpenTests.cs`:
```csharp
using System;
using System.Data.SQLite;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteMoneyStoreOpenTests
    {
        [Test]
        public void Open_ReportsAnIdentityCarryingTheFlagEngineAndSchemaVersion()
        {
            using (var p = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "My Books", SqliteConnectionFactory.InMemoryDataSource, true)))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                // Same in-memory database means the same connection, so the store is opened over
                // the provisioner's handle in this test. Task 16's TestKit fixture makes that
                // arrangement first class.
                using (var store = SqliteMoneyStore.OpenOver(p.Connection,
                    new SqliteStoreOptions("My Books", SqliteConnectionFactory.InMemoryDataSource, true)))
                {
                    Assert.That(store.Identity.DisplayName, Is.EqualTo("My Books"));
                    Assert.That(store.Identity.Engine, Is.EqualTo(DbFlavor.Sqlite));
                    Assert.That(store.Identity.IsTestDatabase, Is.True);
                    Assert.That(store.Identity.SchemaVersion, Is.EqualTo(SchemaStepCatalog.LatestVersion));
                }
            }
        }

        [Test]
        public void Open_RefusesADatabaseNewerThanThisBinaryKnows()
        {
            using (var p = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "future", SqliteConnectionFactory.InMemoryDataSource, true)))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                using (var cmd = new SQLiteCommand(
                    "INSERT INTO __SchemaHistory VALUES (@v, 'future', 'X', '2027-01-01 00:00:00.000', 'them');",
                    p.Connection))
                {
                    cmd.Parameters.AddWithValue("@v", SqliteMoneyStore.MaxKnownSchemaVersion + 1);
                    cmd.ExecuteNonQuery();
                }

                var ex = Assert.Throws<SchemaTooNewException>(() => SqliteMoneyStore.OpenOver(
                    p.Connection,
                    new SqliteStoreOptions("future", SqliteConnectionFactory.InMemoryDataSource, true)));

                // Plain language, not a type name or a raw engine string - spec section 7's UI rule.
                Assert.That(ex.Message, Does.Contain("newer version"));
                Assert.That(ex.Message, Does.Contain("future"));
            }
        }

        [Test]
        public void Open_RefusesAnUnprovisionedDatabase()
        {
            using (SQLiteConnection bare = SqliteConnectionFactory.Open(SqliteConnectionFactory.InMemoryDataSource))
            {
                Assert.Throws<SchemaTooNewException>(() => SqliteMoneyStore.OpenOver(
                    bare, new SqliteStoreOptions("empty", SqliteConnectionFactory.InMemoryDataSource, true)));
            }
        }

        [Test]
        public void WriteRoots_OnARootTypeThisSliceDoesNotHandle_ThrowsNotSupported()
        {
            using (var p = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "books", SqliteConnectionFactory.InMemoryDataSource, true)))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);
                using (var store = SqliteMoneyStore.OpenOver(p.Connection,
                    new SqliteStoreOptions("books", SqliteConnectionFactory.InMemoryDataSource, true)))
                {
                    var money = new MyMoney();
                    var payee = new Payee(money.Payees) { Id = 1, Name = "Landlord" };

                    var ex = Assert.Throws<NotSupportedException>(() => store.SaveRoot(payee));

                    Assert.That(ex.Message, Does.Contain("Payee"));
                }
            }
        }
    }
}
```

> **On the `Open_RefusesAnUnprovisionedDatabase` case:** a database with no `__SchemaHistory` reports version 0, which is *below* `MaxKnownSchemaVersion`, so a naive check would let it through and every later statement would fail with "no such table: Accounts". The store therefore refuses version 0 as well, with the same exception type and a different message. That is why this test asserts `SchemaTooNewException` for an empty database — the type name covers "this store cannot work against this schema version", both directions.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteMoneyStoreOpenTests"`
Expected: FAIL to compile — `CS0103: The name 'SqliteMoneyStore' does not exist in the current context`.

- [ ] **Step 4: Write copy B of the connection factory and `SqliteStoreOptions`**

Create `Source/WPF/MyMoney.Data.Sqlite/SqliteConnectionFactory.cs` — **byte-identical to Task 5's file except for the namespace line**, which becomes:
```csharp
namespace Walkabout.Data.Sqlite
```
and the class doc-comment's first paragraph, which becomes:
```csharp
    /// <summary>
    /// Open a connection and apply the pragma sequence.
    ///
    /// DELIBERATELY DUPLICATED from MyMoney.Data.Sqlite.Provisioning (copy A). Spec section 1's
    /// Recommendation rules out both a shared Common assembly and InternalsVisibleTo and accepts
    /// this ~40-line overlap instead; Task 22 enforces that this assembly does not reference the
    /// provisioning one. If this file ever grows past connection construction and open/close,
    /// that is the signal the tier boundary was drawn in the wrong place.
    /// </summary>
```

Create `Source/WPF/MyMoney.Data.Sqlite/SqliteStoreOptions.cs`:
```csharp
namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// What an open store handle needs to know about the database it is talking to. IsTestDatabase
    /// comes from DatabaseEntry.TestDatabase, a permanent per-database attribute fixed at creation
    /// (spec section 4 item 8), and is a bool defaulting to false so every way it can break
    /// resolves to "the guarded capability refuses" - spec section 1.9.2.
    /// </summary>
    public sealed record SqliteStoreOptions(string DisplayName, string DataSource, bool IsTestDatabase);
}
```

- [ ] **Step 5: Write `SqliteMoneyStore`**

Create `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// Everyday reads and writes over one SQLite file (or one :memory: database). No DDL, no
    /// destructive bulk operations: this assembly contains no DROP TABLE, no VACUUM INTO over an
    /// existing file, and no DELETE FROM without a WHERE Id=.
    ///
    /// The honest limit, stated where the code is rather than only in the design document (spec
    /// section 6.1): this arrangement does NOT prevent a buggy, compromised or deliberately
    /// reflective production process from destroying the file. What it gives is that the
    /// destructive code is not in the shipped assembly set, that production code cannot name the
    /// type it would need, and that a build and a test fail if either becomes untrue.
    /// </summary>
    public sealed class SqliteMoneyStore : MoneyStoreBase
    {
        /// <summary>
        /// The highest schema version this store's SQL was written against. Kept as a constant
        /// rather than read from SchemaStepCatalog because this assembly deliberately does not
        /// reference the provisioning one; Task 17's contract suite asserts the two agree, so
        /// they cannot silently drift.
        /// </summary>
        public const int MaxKnownSchemaVersion = 4;

        private readonly SqliteStoreOptions options;
        private readonly SQLiteConnection connection;
        private readonly bool ownsConnection;
        private readonly int schemaVersion;

        private SqliteMoneyStore(SqliteStoreOptions options, SQLiteConnection connection, bool ownsConnection, int schemaVersion)
        {
            this.options = options;
            this.connection = connection;
            this.ownsConnection = ownsConnection;
            this.schemaVersion = schemaVersion;
        }

        /// <summary>Open a store over its own connection to <c>options.DataSource</c>.</summary>
        public static SqliteMoneyStore Open(SqliteStoreOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            SQLiteConnection connection = SqliteConnectionFactory.Open(options.DataSource);
            try
            {
                return new SqliteMoneyStore(options, connection, true, CheckSchemaVersion(connection, options));
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Open a store over an EXISTING connection, which the caller keeps ownership of. An
        /// in-memory database lives exactly as long as its connection, so the T-1 fixture (spec
        /// section 2.4) needs the store, the query and the test control to share one - and
        /// SqliteDatabase already holds one long-lived cached connection, so this is the lifetime
        /// model the codebase already has.
        /// </summary>
        public static SqliteMoneyStore OpenOver(SQLiteConnection connection, SqliteStoreOptions options)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new SqliteMoneyStore(options, connection, false, CheckSchemaVersion(connection, options));
        }

        public SQLiteConnection Connection => this.connection;

        public override StoreIdentity Identity => new StoreIdentity(
            this.options.DisplayName, DbFlavor.Sqlite, this.options.IsTestDatabase, this.schemaVersion);

        public override IReadOnlyList<Account> LoadAccounts() => throw new NotImplementedException("Task 15.");

        protected override void WriteRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            foreach (IAggregateRoot root in roots)
            {
                if (!(root is Account))
                {
                    // Not NotImplementedException-as-a-capability-signal (spec section 3.2): this
                    // is a deliberate, tested statement that Plan A's vertical stops at Account.
                    // The slice that brings this root type extends WriteRoots and deletes this.
                    throw new NotSupportedException(
                        $"This build's SQLite store writes Account roots only; it was handed a "
                        + $"{root.GetType().Name}. Support for that root type arrives with the slice "
                        + "that creates its table.");
                }
            }

            throw new NotImplementedException("Task 12.");
        }

        public override void Dispose()
        {
            if (this.ownsConnection)
            {
                this.connection?.Dispose();
            }
        }

        /// <summary>
        /// Spec section 1.8's second bullet: ApplyTo handles "the app is newer than the database";
        /// the reverse needs a refusal, not a best-effort open. Version 0 (no ledger at all) is
        /// refused by the same check and the same exception type, because a store that opened
        /// cleanly against an unprovisioned database would fail on its first statement with
        /// "no such table: Accounts" instead of saying what is actually wrong.
        /// </summary>
        private static int CheckSchemaVersion(SQLiteConnection connection, SqliteStoreOptions options)
        {
            int version = ReadSchemaVersion(connection);

            if (version == 0)
            {
                throw new SchemaTooNewException(
                    $"'{options.DisplayName}' has no MyMoney schema in it yet. Create or upgrade it "
                    + "before opening it for reading and writing.");
            }

            if (version > MaxKnownSchemaVersion)
            {
                throw new SchemaTooNewException(
                    $"'{options.DisplayName}' was created or upgraded by a newer version of MyMoney "
                    + $"(schema version {version}); this one understands up to version "
                    + $"{MaxKnownSchemaVersion}. Update MyMoney to open it.");
            }

            return version;
        }

        private static int ReadSchemaVersion(SQLiteConnection connection)
        {
            using (var exists = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__SchemaHistory';",
                connection))
            {
                if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                {
                    return 0;
                }
            }

            using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(Version), 0) FROM __SchemaHistory;", connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteMoneyStoreOpenTests"`
Expected: PASS — 4 tests.

- [ ] **Step 7: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite Source/WPF/MyMoney.Tests.Data Source/WPF/MyMoney.sln
git commit -m "$(cat <<'EOF'
feat(data): MyMoney.Data.Sqlite store handle with an open-time schema check

Slice 3, task 11. The store refuses a database at a higher schema version than
this binary knows (and an unprovisioned one), in plain language rather than by
failing later with "no such table" - the spec's compatibility recommendation
for this slice.

The store assembly deliberately does not reference the provisioning assembly;
the connection factory is duplicated instead, per the spec's recommendation
against a shared Common assembly and against InternalsVisibleTo. Task 22
enforces the non-reference; task 17 asserts MaxKnownSchemaVersion agrees with
the step catalog.

Spec: sections 1 (Recommendation), 1.8, 2.4, 6.1, 6.2.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 12: `WriteRoots` — the `json_each` set-based insert with `RETURNING`

**Files:**
- Create: `Source/WPF/MyMoney.Data.Sqlite/AccountRowCodec.cs`
- Modify: `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteInsertTests.cs`

**Interfaces:**
- Consumes: `MoneyScale` (Task 4), `Account`, `ConcurrencyConflictException`.
- Produces: `static class AccountRowCodec` with `const string Columns`, `static string ToJsonArray(IReadOnlyList<Account> accounts)`, `static Account Read(SQLiteDataReader reader, Accounts container)`, `static void AddUpdateParameters(SQLiteCommand cmd, Account a)`; a working insert branch in `WriteRoots`.

**The three decisions in this task:**

1. **The insert group is one set-based statement** — `INSERT INTO Accounts (…) SELECT json_extract(value, '$.…'), … FROM json_each(@Rows) RETURNING Id, Version`. This is §1.7.1's adopted `json_each` mechanism: the genuine TVP equivalent, one parameterized statement, set-based, engine-side, one transaction (R-CRUD-3). The whole batch travels as **one bound parameter**, so R-CRUD-1 holds — the JSON is a *value*, not concatenated SQL. *(Whether it beats a prepared-statement C# loop at realistic batch sizes is slice 10's benchmark, which is Plan B. This task builds the mechanism; it does not claim the measurement.)*
2. **`UPDATE` and `DELETE` stay per-root** (Tasks 13 and 14), and that is not an inconsistency. A version-checked write must attribute a conflict to a *specific* root, and a set-based version-checked statement can only report an aggregate row count. Set-based where it is correct, per-row where the conflict contract needs it.
3. **A primary-key collision on insert becomes `ConcurrencyConflictException`.** Detected by a pre-check inside the same `BEGIN IMMEDIATE` transaction (`SELECT a.Id, a.Version FROM Accounts a JOIN json_each(@Ids) j ON a.Id = j.value`) rather than by sniffing an engine error code — deterministic, parameterized, and it tells us *which* root and at *what* stored version. This is a deliberate, narrow extension of R-CRUD-4 from "version-checked UPDATE/DELETE" to "someone else wrote this row since you looked," so §1.6a's business-layer retry loop covers the insert race too. **Plan B's SQL Server side must match it** (§6.8 rule 1).

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteInsertTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteWriteInsertTests
    {
        private SqliteMoneyStoreProvisioner provisioner;
        private SqliteMoneyStore store;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);
            this.store = SqliteMoneyStore.OpenOver(this.provisioner.Connection,
                new SqliteStoreOptions("books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown()
        {
            this.store?.Dispose();
            this.provisioner?.Dispose();
        }

        private Account NewAccount(int id, string name) =>
            new Account(this.container)
            {
                Id = id,
                Name = name,
                Type = AccountType.Checking,
                Currency = "USD",
                OpeningBalance = 1234.5678m,
                Description = "opened by a test",
            };

        private long RowCount()
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Accounts;", this.provisioner.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void SaveRoot_OnANewAccount_InsertsItAndAssignsRowVersionOne()
        {
            Account a = this.NewAccount(1, "Checking");

            this.store.SaveRoot(a);

            Assert.That(this.RowCount(), Is.EqualTo(1));
            Assert.That(a.RowVersion, Is.EqualTo(1));
            Assert.That(a.IsInserted, Is.False, "OnUpdated() should have cleared the inserted flag after commit.");
        }

        [Test]
        public void SaveRoot_StoresMoneyAsIntegerTenThousandths()
        {
            this.store.SaveRoot(this.NewAccount(1, "Checking"));

            using (var cmd = new SQLiteCommand(
                "SELECT typeof(OpeningBalance), OpeningBalance FROM Accounts WHERE Id = 1;",
                this.provisioner.Connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                Assert.That(reader.Read(), Is.True);
                Assert.That(reader.GetString(0), Is.EqualTo("integer"));
                Assert.That(reader.GetInt64(1), Is.EqualTo(12345678L));
            }
        }

        [Test]
        public void SaveRoots_InsertsAWholeBatchInOneTransactionAndVersionsThemAll()
        {
            var roots = new List<IAggregateRoot>
            {
                this.NewAccount(1, "Checking"),
                this.NewAccount(2, "Savings"),
                this.NewAccount(3, "Brokerage"),
            };

            this.store.SaveRoots(roots);

            Assert.That(this.RowCount(), Is.EqualTo(3));
            foreach (Account a in roots)
            {
                Assert.That(a.RowVersion, Is.EqualTo(1), $"{a.Name} did not get its version back.");
            }
        }

        [Test]
        public void SaveRoot_OnAnIdThatAlreadyExists_ThrowsConcurrencyConflictAndWritesNothing()
        {
            this.store.SaveRoot(this.NewAccount(1, "Checking"));

            var colliding = this.NewAccount(1, "Someone Else's Checking");

            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.store.SaveRoot(colliding));

            Assert.That(ex.StoredRowVersion, Is.EqualTo(1));
            Assert.That(this.RowCount(), Is.EqualTo(1));
            using (var cmd = new SQLiteCommand("SELECT Name FROM Accounts WHERE Id = 1;", this.provisioner.Connection))
            {
                Assert.That(Convert.ToString(cmd.ExecuteScalar()), Is.EqualTo("Checking"));
            }
        }

        [Test]
        public void SaveRoots_WhenOneRootCollides_RollsBackTheWholeBatch()
        {
            // R-CRUD-2: atomic at the call boundary, including in-memory side effects. The first
            // root's statement "succeeded" before the second one collided, so without the
            // postCommitActions deferral its RowVersion would claim a commit the rollback undid.
            this.store.SaveRoot(this.NewAccount(2, "Existing"));

            Account first = this.NewAccount(1, "Checking");
            Account colliding = this.NewAccount(2, "Collides");

            Assert.Throws<ConcurrencyConflictException>(
                () => this.store.SaveRoots(new List<IAggregateRoot> { first, colliding }));

            Assert.That(this.RowCount(), Is.EqualTo(1));
            Assert.That(first.RowVersion, Is.EqualTo(0), "A rolled-back insert must not leave a RowVersion behind.");
            Assert.That(first.IsInserted, Is.True, "A rolled-back insert must leave the root still dirty.");
        }

        [Test]
        public void SaveRoot_OnARootWithNoIdAssigned_ThrowsArgumentException()
        {
            Account a = new Account(this.container) { Name = "No Id", Type = AccountType.Cash };

            // Account's own default is id == -1. Id allocation stays a caller concern - with no
            // ambient graph, IMoneyQuery.NextAccountId is where a caller gets one (task 15).
            Assert.Throws<ArgumentException>(() => this.store.SaveRoot(a));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteWriteInsertTests"`
Expected: FAIL — `System.NotImplementedException : Task 12.`

- [ ] **Step 3: Write `AccountRowCodec`**

Create `Source/WPF/MyMoney.Data.Sqlite/AccountRowCodec.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Data.SqlTypes;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// Account &lt;-&gt; storage, in ONE place. Every encoding decision the schema depends on lives
    /// here: money as INTEGER ten-thousandths (MoneyScale), DateTime as TEXT
    /// 'yyyy-MM-dd HH:mm:ss.fff', Guid as TEXT "D".
    ///
    /// OnlineAccount / CategoryIdForPrincipal / CategoryIdForInterest are written from the
    /// Account's object references, which no Plan A code path can make non-null because no slice
    /// in 1-7 produces an OnlineAccount or Category root. The null round trip is therefore
    /// LOSSLESS HERE and only here: the slice that brings those roots must revisit this file at
    /// the same time, or a save will null out a real foreign key.
    /// </summary>
    public static class AccountRowCodec
    {
        public const string Columns =
            "Id,AccountId,OfxAccountId,Name,Type,Description,OnlineAccount,OpeningBalance,LastSync," +
            "LastBalance,SyncGuid,Flags,Currency,WebSite,ReconcileWarning,CategoryIdForPrincipal," +
            "CategoryIdForInterest";

        private static readonly string[] ColumnNames = Columns.Split(',');

        private const string DateFormat = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>The SELECT list for the json_each insert: one json_extract per column.</summary>
        public static string JsonExtractList()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ColumnNames.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append("json_extract(value, '$.").Append(ColumnNames[i]).Append("')");
            }

            return sb.ToString();
        }

        public static string ToJsonArray(IReadOnlyList<Account> accounts)
        {
            var rows = new List<Dictionary<string, object>>(accounts.Count);
            foreach (Account a in accounts)
            {
                rows.Add(ToDictionary(a));
            }

            return JsonSerializer.Serialize(rows);
        }

        public static string IdsJsonArray(IReadOnlyList<Account> accounts)
        {
            var ids = new List<long>(accounts.Count);
            foreach (Account a in accounts)
            {
                ids.Add(a.Id);
            }

            return JsonSerializer.Serialize(ids);
        }

        public static Dictionary<string, object> ToDictionary(Account a) => new Dictionary<string, object>
        {
            { "Id", a.Id },
            { "AccountId", a.AccountId },
            { "OfxAccountId", a.OfxAccountId },
            { "Name", a.Name },
            { "Type", (int)a.Type },
            { "Description", a.Description },
            { "OnlineAccount", a.OnlineAccount?.Id },
            { "OpeningBalance", MoneyScale.ToStorage(a.OpeningBalance) },
            { "LastSync", ToText(a.LastSync) },
            { "LastBalance", ToText(a.LastBalance) },
            { "SyncGuid", a.SyncGuid.IsNull ? null : a.SyncGuid.Value.ToString("D") },
            { "Flags", (int)a.Flags },
            { "Currency", a.Currency },
            { "WebSite", a.WebSite },
            { "ReconcileWarning", a.ReconcileWarning },
            { "CategoryIdForPrincipal", a.CategoryForPrincipal?.Id },
            { "CategoryIdForInterest", a.CategoryForInterest?.Id },
        };

        /// <summary>Binds every column as @Name, for the per-root UPDATE path (Task 13).</summary>
        public static void AddParameters(SQLiteCommand cmd, Account a)
        {
            foreach (KeyValuePair<string, object> pair in ToDictionary(a))
            {
                cmd.Parameters.AddWithValue("@" + pair.Key, pair.Value ?? DBNull.Value);
            }
        }

        /// <summary>Materialises one row as an Account parented to <paramref name="container"/>.</summary>
        public static Account Read(SQLiteDataReader reader, Accounts container)
        {
            var a = new Account(container)
            {
                // Account.Id is int while the column is INTEGER(64) because IAggregateRoot.Id is
                // long; checked() makes a value that truly does not fit loud instead of silent.
                Id = checked((int)reader.GetInt64(reader.GetOrdinal("Id"))),
                AccountId = GetString(reader, "AccountId"),
                OfxAccountId = GetString(reader, "OfxAccountId"),
                Name = GetString(reader, "Name"),
                Type = (AccountType)reader.GetInt64(reader.GetOrdinal("Type")),
                Description = GetString(reader, "Description"),
                OpeningBalance = MoneyScale.FromStorage(reader.GetInt64(reader.GetOrdinal("OpeningBalance"))),
                Currency = GetString(reader, "Currency"),
                WebSite = GetString(reader, "WebSite"),
            };

            DateTime? lastSync = FromText(GetString(reader, "LastSync"));
            if (lastSync.HasValue)
            {
                a.LastSync = lastSync.Value;
            }

            DateTime? lastBalance = FromText(GetString(reader, "LastBalance"));
            if (lastBalance.HasValue)
            {
                a.LastBalance = lastBalance.Value;
            }

            string syncGuid = GetString(reader, "SyncGuid");
            if (!string.IsNullOrEmpty(syncGuid))
            {
                a.SyncGuid = new SqlGuid(Guid.Parse(syncGuid));
            }

            int flagsOrdinal = reader.GetOrdinal("Flags");
            if (!reader.IsDBNull(flagsOrdinal))
            {
                a.Flags = (AccountFlags)reader.GetInt64(flagsOrdinal);
            }

            int warnOrdinal = reader.GetOrdinal("ReconcileWarning");
            if (!reader.IsDBNull(warnOrdinal))
            {
                a.ReconcileWarning = checked((int)reader.GetInt64(warnOrdinal));
            }

            a.RowVersion = reader.GetInt64(reader.GetOrdinal("Version"));

            // A freshly constructed PersistentObject starts at ChangeType.Inserted, and every
            // setter above fired OnChanged. OnUpdated() puts a loaded root at None, which is what
            // makes the NEXT SaveRoot on it an UPDATE rather than an INSERT.
            a.OnUpdated();
            return a;
        }

        private static string GetString(SQLiteDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static string ToText(DateTime value) =>
            value == DateTime.MinValue ? null : value.ToString(DateFormat, CultureInfo.InvariantCulture);

        private static DateTime? FromText(string value) =>
            string.IsNullOrEmpty(value)
                ? (DateTime?)null
                : DateTime.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 4: Implement `WriteRoots`'s transaction shell and insert branch**

Replace `WriteRoots` in `SqliteMoneyStore.cs` (and add the helpers below it):
```csharp
        protected override void WriteRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            var accounts = new List<Account>(roots.Count);
            foreach (IAggregateRoot root in roots)
            {
                if (root is Account account)
                {
                    accounts.Add(account);
                }
                else
                {
                    throw new NotSupportedException(
                        $"This build's SQLite store writes Account roots only; it was handed a "
                        + $"{root.GetType().Name}. Support for that root type arrives with the slice "
                        + "that creates its table.");
                }
            }

            // BEGIN IMMEDIATE: take the write lock up front rather than upgrading mid-transaction,
            // so the collision pre-check below cannot be invalidated by a concurrent writer.
            using (SQLiteTransaction tx = this.connection.BeginTransaction(deferredLock: false))
            {
                // EVERY in-memory side effect is deferred until after Commit() returns. If a later
                // root in the batch conflicts, the rollback undoes every write this transaction
                // made - including ones whose own statement already succeeded. Applying RowVersion
                // or OnUpdated() per root instead would leave an earlier root claiming a commit
                // the rollback undid, which is a silent-corruption bug. Carried forward verbatim
                // from SqliteDatabase.SaveBatch - R-CRUD-2.
                var postCommitActions = new List<Action>();
                try
                {
                    var inserted = new List<Account>();
                    var changed = new List<Account>();
                    var deleted = new List<Account>();

                    foreach (Account a in accounts)
                    {
                        if (a.IsInserted)
                        {
                            inserted.Add(a);
                        }
                        else if (a.IsDeleted)
                        {
                            deleted.Add(a);
                        }
                        else if (a.IsChanged)
                        {
                            changed.Add(a);
                        }

                        // A root at None produces no statement for its own row. Account has no
                        // owned children, so for this slice that is the whole story - spec 1.6c's
                        // deliberate silent no-op path.
                    }

                    this.InsertAccounts(inserted, tx, postCommitActions);
                    this.UpdateAccounts(changed, tx, postCommitActions);
                    this.DeleteAccounts(deleted, tx, postCommitActions);

                    tx.Commit();
                }
                catch (Exception original)
                {
                    // A failing Rollback must never silently replace the original exception (the
                    // caller needs to see a ConcurrencyConflictException), and must never leave
                    // the caller thinking the rollback happened when it did not.
                    try
                    {
                        tx.Rollback();
                    }
                    catch (Exception rollbackFailure)
                    {
                        throw new InvalidOperationException(
                            "Rollback failed after: " + original.Message, rollbackFailure);
                    }

                    throw;
                }

                foreach (Action action in postCommitActions)
                {
                    action();
                }
            }
        }

        /// <summary>
        /// One set-based statement for the whole insert group: spec section 1.7.1's adopted
        /// json_each mechanism, the genuine table-valued-parameter equivalent on SQLite. The batch
        /// travels as ONE bound parameter, so R-CRUD-1 holds - the JSON is a value, not
        /// concatenated SQL. RETURNING reads the authoritative post-write version from the engine
        /// rather than inferring it in C#.
        /// </summary>
        private void InsertAccounts(IReadOnlyList<Account> inserted, SQLiteTransaction tx, List<Action> postCommitActions)
        {
            if (inserted.Count == 0)
            {
                return;
            }

            foreach (Account a in inserted)
            {
                if (a.Id < 0)
                {
                    throw new ArgumentException(
                        $"Account '{a.Name}' reached the store with no Id assigned. Id allocation is a "
                        + "caller concern - use IMoneyQuery.NextAccountId().");
                }
            }

            this.RejectExistingIds(inserted, tx);

            var versions = new Dictionary<long, long>();
            using (var cmd = new SQLiteCommand(
                $"INSERT INTO Accounts ({AccountRowCodec.Columns}) "
                + $"SELECT {AccountRowCodec.JsonExtractList()} FROM json_each(@Rows) "
                + "RETURNING Id, Version;",
                this.connection,
                tx))
            {
                cmd.Parameters.AddWithValue("@Rows", AccountRowCodec.ToJsonArray(inserted));
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        versions[reader.GetInt64(0)] = reader.GetInt64(1);
                    }
                }
            }

            foreach (Account a in inserted)
            {
                long version = versions[a.Id];
                postCommitActions.Add(() =>
                {
                    a.RowVersion = version;
                    a.OnUpdated();
                });
            }
        }

        /// <summary>
        /// A primary-key collision is "someone else wrote this row since you looked", so it is
        /// reported as ConcurrencyConflictException and section 1.6a's business-layer retry loop
        /// covers the insert race as well as the update race. Detected by a pre-check inside the
        /// same BEGIN IMMEDIATE transaction rather than by sniffing an engine error code:
        /// deterministic, parameterized, and it can say WHICH root and at what stored version.
        /// </summary>
        private void RejectExistingIds(IReadOnlyList<Account> inserted, SQLiteTransaction tx)
        {
            using (var cmd = new SQLiteCommand(
                "SELECT a.Id, a.Version FROM Accounts a JOIN json_each(@Ids) j ON a.Id = j.value LIMIT 1;",
                this.connection,
                tx))
            {
                cmd.Parameters.AddWithValue("@Ids", AccountRowCodec.IdsJsonArray(inserted));
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        long existingId = reader.GetInt64(0);
                        long storedVersion = reader.GetInt64(1);
                        foreach (Account a in inserted)
                        {
                            if (a.Id == existingId)
                            {
                                throw new ConcurrencyConflictException(a, storedVersion, a.RowVersion);
                            }
                        }
                    }
                }
            }
        }

        private void UpdateAccounts(IReadOnlyList<Account> changed, SQLiteTransaction tx, List<Action> postCommitActions)
        {
            if (changed.Count > 0)
            {
                throw new NotImplementedException("Task 13.");
            }
        }

        private void DeleteAccounts(IReadOnlyList<Account> deleted, SQLiteTransaction tx, List<Action> postCommitActions)
        {
            if (deleted.Count > 0)
            {
                throw new NotImplementedException("Task 14.");
            }
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteWriteInsertTests"`
Expected: PASS — 6 tests.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteInsertTests.cs
git commit -m "$(cat <<'EOF'
feat(data): set-based json_each insert with RETURNING, and R-CRUD-2 deferral

Slice 3, task 12. The insert group is one parameterized, set-based statement -
the spec's adopted json_each mechanism, SQLite's genuine TVP equivalent - and
RETURNING reads the authoritative post-write version from the engine instead of
computing callerRowVersion + 1 in C#. UPDATE and DELETE stay per-root because a
version-checked write has to attribute a conflict to a specific root, which an
aggregate row count cannot do.

A primary-key collision on insert is reported as ConcurrencyConflictException,
detected by a pre-check inside the same BEGIN IMMEDIATE transaction, so the
business-layer retry loop covers the insert race too. This is a deliberate,
narrow extension of R-CRUD-4 that Plan B's SQL Server side must match.

postCommitActions is carried forward verbatim: nothing touches RowVersion or
calls OnUpdated() until Commit() has returned.

Spec: sections 1.6c, 1.7.1, R-CRUD-1 through R-CRUD-4, 6.8 rule 1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 13: `WriteRoots` — the version-checked `UPDATE` with `RETURNING Version`

**Files:**
- Modify: `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs` (`UpdateAccounts`)
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteUpdateTests.cs`

**Interfaces:**
- Consumes: `AccountRowCodec.AddParameters` (Task 12), `ConcurrencyConflictException`.
- Produces: a working `UpdateAccounts`; `private long ReadStoredVersion(long id, SQLiteTransaction tx)` (returns `0` when the row is absent).

**Why `RETURNING Version` and not `rowsAffected`.** §1.7.1 adopted `RETURNING` precisely so the post-write version is *read from the engine* rather than inferred as `callerRowVersion + 1` in C#. `ExecuteScalar` returning `null` is the zero-rows signal, so one statement gives both the conflict detection and the authoritative new version. Note what is **not** being changed: the version check itself stays `WHERE Id=@Id AND Version=@ExpectedVersion` in SQL. §1.7.3 is explicit that moving it into a trigger would make the two engines' conflict *signal* genuinely different and is the one place the panel pushes back on P-SQLITE.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteUpdateTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteWriteUpdateTests
    {
        private SqliteMoneyStoreProvisioner provisioner;
        private SqliteMoneyStore store;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);
            this.store = SqliteMoneyStore.OpenOver(this.provisioner.Connection,
                new SqliteStoreOptions("books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown()
        {
            this.store?.Dispose();
            this.provisioner?.Dispose();
        }

        private Account Saved(int id, string name)
        {
            var a = new Account(this.container)
            {
                Id = id, Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 10m
            };
            this.store.SaveRoot(a);
            return a;
        }

        private string NameInDatabase(int id)
        {
            using (var cmd = new SQLiteCommand("SELECT Name FROM Accounts WHERE Id = @id;", this.provisioner.Connection))
            {
                cmd.Parameters.AddWithValue("@id", id);
                return Convert.ToString(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void SaveRoot_OnAChangedAccount_UpdatesTheRowAndBumpsTheVersion()
        {
            Account a = this.Saved(1, "Checking");

            a.Name = "Current Account";
            this.store.SaveRoot(a);

            Assert.That(this.NameInDatabase(1), Is.EqualTo("Current Account"));
            Assert.That(a.RowVersion, Is.EqualTo(2));
            Assert.That(a.IsChanged, Is.False);
        }

        [Test]
        public void SaveRoot_OnAStaleRoot_ThrowsConcurrencyConflictCarryingTheStoredVersion()
        {
            Account a = this.Saved(1, "Checking");
            Account other = this.Saved(2, "Savings");

            // Someone else wrote row 1 since 'a' was loaded.
            using (var cmd = new SQLiteCommand(
                "UPDATE Accounts SET Name = 'Theirs', Version = Version + 1 WHERE Id = 1;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.Name = "Mine";

            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.store.SaveRoot(a));

            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(ex.CallerRowVersion, Is.EqualTo(1));
            Assert.That(this.NameInDatabase(1), Is.EqualTo("Theirs"));
            Assert.That(other.RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void AfterAConflict_TheSameCallIsStillAdmissible()
        {
            // Spec section 1.6a consequence 2: the postCommitActions deferral means a failed write
            // leaves the root's change state untouched, which is what makes the business-layer
            // retry loop legal at all. Under a unified SaveRoot this was true but invisible; with
            // a state precondition on the method it becomes load-bearing.
            Account a = this.Saved(1, "Checking");

            using (var cmd = new SQLiteCommand("UPDATE Accounts SET Version = Version + 1 WHERE Id = 1;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.Name = "Mine";
            Assert.Throws<ConcurrencyConflictException>(() => this.store.SaveRoot(a));

            Assert.That(a.IsChanged, Is.True);
            Assert.That(a.RowVersion, Is.EqualTo(1), "A failed write must not advance RowVersion.");

            // Re-query, reapply, retry - what AddAccountService will do in Task 24.
            a.RowVersion = 2;
            Assert.DoesNotThrow(() => this.store.SaveRoot(a));
            Assert.That(this.NameInDatabase(1), Is.EqualTo("Mine"));
        }

        [Test]
        public void SaveRoot_OnACleanAccount_IsASilentNoOp()
        {
            // Spec section 1.6c: SaveRoot deliberately admits None and no-ops. Account has no
            // owned children, so for this slice nothing at all is written.
            Account a = this.Saved(1, "Checking");
            long before = a.RowVersion;

            Assert.DoesNotThrow(() => this.store.SaveRoot(a));

            Assert.That(a.RowVersion, Is.EqualTo(before));
            Assert.That(this.NameInDatabase(1), Is.EqualTo("Checking"));
        }

        [Test]
        public void SaveRoots_WithOneStaleRootAmongMany_RollsBackEveryWrite()
        {
            Account a = this.Saved(1, "Checking");
            Account b = this.Saved(2, "Savings");

            using (var cmd = new SQLiteCommand("UPDATE Accounts SET Version = Version + 1 WHERE Id = 2;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.Name = "A2";
            b.Name = "B2";

            Assert.Throws<ConcurrencyConflictException>(
                () => this.store.SaveRoots(new List<IAggregateRoot> { a, b }));

            Assert.That(this.NameInDatabase(1), Is.EqualTo("Checking"),
                "The first root's successful statement must have been rolled back.");
            Assert.That(a.RowVersion, Is.EqualTo(1),
                "The first root must not carry a RowVersion claiming a commit the rollback undid.");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteWriteUpdateTests"`
Expected: FAIL — `System.NotImplementedException : Task 13.`

- [ ] **Step 3: Implement `UpdateAccounts`**

Replace the `UpdateAccounts` stub in `SqliteMoneyStore.cs`:
```csharp
        /// <summary>
        /// Per-root, version-checked, with RETURNING Version so the new version is READ from the
        /// engine rather than inferred as callerRowVersion + 1 in C# (spec section 1.7.1's
        /// RETURNING adoption). ExecuteScalar returning null is the zero-rows signal, so one
        /// statement gives both the conflict detection and the authoritative version.
        ///
        /// The version check itself stays in SQL, as a WHERE clause. Spec section 1.7.3 is
        /// explicit that moving it into a BEFORE UPDATE trigger would make the two engines'
        /// conflict SIGNAL genuinely different - an aborted statement with a message string on
        /// SQLite versus a rowcount plus a follow-up SELECT on SQL Server - which is more
        /// divergence, not less, on the most contract-tested behaviour in the data layer.
        /// </summary>
        private void UpdateAccounts(IReadOnlyList<Account> changed, SQLiteTransaction tx, List<Action> postCommitActions)
        {
            foreach (Account a in changed)
            {
                long callerRowVersion = a.RowVersion;
                object newVersion;

                using (var cmd = new SQLiteCommand(
                    "UPDATE Accounts SET AccountId=@AccountId, OfxAccountId=@OfxAccountId, Name=@Name, "
                    + "Type=@Type, Description=@Description, OnlineAccount=@OnlineAccount, "
                    + "OpeningBalance=@OpeningBalance, LastSync=@LastSync, LastBalance=@LastBalance, "
                    + "SyncGuid=@SyncGuid, Flags=@Flags, Currency=@Currency, WebSite=@WebSite, "
                    + "ReconcileWarning=@ReconcileWarning, CategoryIdForPrincipal=@CategoryIdForPrincipal, "
                    + "CategoryIdForInterest=@CategoryIdForInterest, Version=Version+1 "
                    + "WHERE Id=@Id AND Version=@ExpectedVersion "
                    + "RETURNING Version;",
                    this.connection,
                    tx))
                {
                    AccountRowCodec.AddParameters(cmd, a);
                    cmd.Parameters.AddWithValue("@ExpectedVersion", callerRowVersion);
                    newVersion = cmd.ExecuteScalar();
                }

                if (newVersion == null || newVersion == DBNull.Value)
                {
                    throw new ConcurrencyConflictException(a, this.ReadStoredVersion(a.Id, tx), callerRowVersion);
                }

                long version = Convert.ToInt64(newVersion, CultureInfo.InvariantCulture);
                postCommitActions.Add(() =>
                {
                    a.RowVersion = version;
                    a.OnUpdated();
                });
            }
        }

        /// <summary>The store's actual current version for a row; 0 when the row is not there.</summary>
        private long ReadStoredVersion(long id, SQLiteTransaction tx)
        {
            using (var cmd = new SQLiteCommand("SELECT Version FROM Accounts WHERE Id=@Id;", this.connection, tx))
            {
                cmd.Parameters.AddWithValue("@Id", id);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteWriteUpdateTests"`
Expected: PASS — 5 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteUpdateTests.cs
git commit -m "$(cat <<'EOF'
feat(data): version-checked UPDATE with RETURNING Version

Slice 3, task 13. One statement gives both the conflict detection (ExecuteScalar
returning null is the zero-rows signal) and the authoritative new version, read
from the engine rather than inferred as callerRowVersion + 1 in C#. The version
check itself stays a WHERE clause: moving it into a trigger would make the two
engines' conflict signal genuinely different, which is more divergence, not
less, on the most contract-tested behaviour in the data layer.

Also pins the property that makes a business-layer retry loop legal: after a
conflict the root's change state is untouched, so the same call is still
admissible.

Spec: sections 1.6a, 1.6c, 1.7.1, 1.7.3, R-CRUD-2, R-CRUD-4.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 14: `WriteRoots` — `DELETE`, and pinning `DeleteRoot`-on-a-never-persisted-root

**Files:**
- Modify: `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs` (`DeleteAccounts`)
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteDeleteTests.cs`

**Interfaces:**
- Consumes: `ReadStoredVersion` (Task 13).
- Produces: a working `DeleteAccounts` implementing decision **D-2**.

**The semantics this task decides** (the spec routed this here deliberately — §1.6c's honest-costs list, §4.1's "a behaviour to pin, not a trade-off to choose"):

| Situation | Behaviour | Why |
|---|---|---|
| `DeleteRoot` on a root with `RowVersion == 0` | **silent no-op**, no SQL at all | `RowVersion == 0` means never written: a loaded root always has `Version >= 1` (`DEFAULT 1`) and a constructed one has the `long` default. `OnUpdated()` never clears `Deleted`, and a root created-then-deleted before any save is also `Deleted` — so without this rule the common accident raises a `ConcurrencyConflictException` that misdiagnoses "this row was never there." This is the panel's own recommended answer. |
| `DeleteRoot` twice on a persisted root | **`ConcurrencyConflictException`** | The first delete succeeded, so the second call is either a caller bug or a real race, and both are exactly what the conflict exception plus §1.6a's retry loop exist for. Deliberately not softened. |

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteDeleteTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteWriteDeleteTests
    {
        private SqliteMoneyStoreProvisioner provisioner;
        private SqliteMoneyStore store;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);
            this.store = SqliteMoneyStore.OpenOver(this.provisioner.Connection,
                new SqliteStoreOptions("books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown()
        {
            this.store?.Dispose();
            this.provisioner?.Dispose();
        }

        private Account New(int id, string name) => new Account(this.container)
        {
            Id = id, Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 0m
        };

        private long RowCount()
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Accounts;", this.provisioner.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void DeleteRoot_OnAPersistedAccount_RemovesTheRow()
        {
            Account a = this.New(1, "Checking");
            this.store.SaveRoot(a);

            a.OnDelete();
            this.store.DeleteRoot(a);

            Assert.That(this.RowCount(), Is.EqualTo(0));
        }

        [Test]
        public void DeleteRoot_OnAStaleRoot_ThrowsConcurrencyConflict()
        {
            Account a = this.New(1, "Checking");
            this.store.SaveRoot(a);

            using (var cmd = new SQLiteCommand("UPDATE Accounts SET Version = Version + 1 WHERE Id = 1;",
                this.provisioner.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            a.OnDelete();

            var ex = Assert.Throws<ConcurrencyConflictException>(() => this.store.DeleteRoot(a));

            Assert.That(ex.StoredRowVersion, Is.EqualTo(2));
            Assert.That(this.RowCount(), Is.EqualTo(1));
        }

        [Test]
        public void DeleteRoot_OnARootThatWasNeverPersisted_IsASilentNoOp()
        {
            // Decision D-2. OnUpdated() never clears Deleted, and a root created then deleted
            // before any save is also Deleted - so without this rule the COMMON accident raises a
            // ConcurrencyConflictException that misdiagnoses "this row was never there".
            Account a = this.New(99, "Never Saved");
            a.OnDelete();

            Assert.DoesNotThrow(() => this.store.DeleteRoot(a));
            Assert.That(a.RowVersion, Is.EqualTo(0));
            Assert.That(this.RowCount(), Is.EqualTo(0));
        }

        [Test]
        public void DeleteRoot_Twice_OnAPersistedRoot_ThrowsOnTheSecondCall()
        {
            // Decision D-2, the other half, pinned in the opposite direction: the first delete
            // succeeded, so a second call is a caller bug or a real race - both of which are what
            // the conflict exception and the retry loop exist for. Deliberately NOT a no-op.
            Account a = this.New(1, "Checking");
            this.store.SaveRoot(a);
            a.OnDelete();
            this.store.DeleteRoot(a);

            Assert.Throws<ConcurrencyConflictException>(() => this.store.DeleteRoot(a));
        }

        [Test]
        public void SaveRoots_CanCommitAChangedSurvivorAndADeletedVictimTogether()
        {
            // The merge shape: SaveRoots is mixed-state by definition and asserts nothing.
            Account survivor = this.New(1, "Checking");
            Account victim = this.New(2, "Duplicate");
            this.store.SaveRoots(new List<IAggregateRoot> { survivor, victim });

            survivor.Name = "Checking (merged)";
            victim.OnDelete();
            this.store.SaveRoots(new List<IAggregateRoot> { survivor, victim });

            Assert.That(this.RowCount(), Is.EqualTo(1));
            Assert.That(survivor.RowVersion, Is.EqualTo(2));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteWriteDeleteTests"`
Expected: FAIL — `System.NotImplementedException : Task 14.`

- [ ] **Step 3: Implement `DeleteAccounts`**

Replace the `DeleteAccounts` stub in `SqliteMoneyStore.cs`:
```csharp
        /// <summary>
        /// Per-root and version-checked, for the same conflict-attribution reason as UpdateAccounts.
        ///
        /// The never-persisted rule is the plan's decision D-2, which the design deliberately
        /// routed to this slice to pin (spec sections 1.6c honest costs and 4.1): RowVersion == 0
        /// means the root has never been written - a loaded root always has Version >= 1 from the
        /// schema's DEFAULT 1, and a constructed one has the long default. Because OnUpdated()
        /// never clears Deleted, a root created and then deleted before any save is also Deleted,
        /// so without this rule the common accident would raise a ConcurrencyConflictException
        /// that misdiagnoses "this row was never there".
        ///
        /// Deleting the SAME persisted root twice deliberately still conflicts: the first delete
        /// succeeded, so the second call is a caller bug or a real race, which is precisely what
        /// the conflict exception and section 1.6a's retry loop are for.
        ///
        /// No Parent.RemoveChild here. That call existed to keep the ambient MyMoney graph in
        /// sync, and revision 7 removed whole-graph Load(), so there is no long-lived container
        /// for the store to maintain.
        /// </summary>
        private void DeleteAccounts(IReadOnlyList<Account> deleted, SQLiteTransaction tx, List<Action> postCommitActions)
        {
            foreach (Account a in deleted)
            {
                if (a.RowVersion == 0)
                {
                    postCommitActions.Add(() => a.OnUpdated());
                    continue;
                }

                long callerRowVersion = a.RowVersion;
                object removedId;

                using (var cmd = new SQLiteCommand(
                    "DELETE FROM Accounts WHERE Id=@Id AND Version=@ExpectedVersion RETURNING Id;",
                    this.connection,
                    tx))
                {
                    cmd.Parameters.AddWithValue("@Id", a.Id);
                    cmd.Parameters.AddWithValue("@ExpectedVersion", callerRowVersion);
                    removedId = cmd.ExecuteScalar();
                }

                if (removedId == null || removedId == DBNull.Value)
                {
                    throw new ConcurrencyConflictException(a, this.ReadStoredVersion(a.Id, tx), callerRowVersion);
                }

                postCommitActions.Add(() => a.OnUpdated());
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 45 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteWriteDeleteTests.cs
git commit -m "$(cat <<'EOF'
feat(data): version-checked DELETE, and pin DeleteRoot's never-persisted semantics

Slice 3, task 14. Implements the semantics call the design explicitly routed to
this slice: DeleteRoot on a root with RowVersion == 0 is a silent no-op, because
a root created and then deleted before any save is still marked Deleted
(OnUpdated never clears it) and a version-checked DELETE would otherwise
misdiagnose it as a concurrency conflict. Deleting the same PERSISTED root twice
deliberately still conflicts - that is a caller bug or a real race, which is
what the conflict exception and the retry loop are for. Both directions are
pinned by tests.

No Parent.RemoveChild in the post-commit action: that kept the ambient MyMoney
graph in sync, and whole-graph Load() does not survive.

Spec: sections 1.6c (honest costs), 4.1, R-CRUD-4. Plan decision D-2.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 15: `LoadAccounts` (roots) and `SqliteMoneyQuery` (projections) — both ports' reads, one slice

**Files:**
- Modify: `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyStore.cs` (`LoadAccounts`)
- Create: `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyQuery.cs`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteReadTests.cs`

**Interfaces:**
- Consumes: `AccountRowCodec.Read` (Task 12), `IMoneyQuery`, `AccountRow`, `AccountQuery` (Task 2).
- Produces:
  - `SqliteMoneyStore.LoadAccounts()` → `IReadOnlyList<Account>`
  - `sealed class SqliteMoneyQuery : IMoneyQuery` with `static SqliteMoneyQuery OpenOver(SQLiteConnection connection)`

**This is decision D-1 landing in code.** Both ports carry reads and the split is by *what comes back*: `LoadAccounts` returns roots carrying `RowVersion` (the only legal input to a later version-checked write); `ListAccounts` returns `AccountRow` projections that carry no version and cannot be passed to any write method. Implementing `IMoneyQuery` **here**, in the same slice as the store's writes, is what closes §5's flagged gap — and Task 17's contract suite covers it from the moment a contract suite exists.

**The non-obvious detail in `LoadAccounts`:** a freshly constructed `PersistentObject` starts at `ChangeType.Inserted`, and every property setter fires `OnChanged`. A loaded root that stayed at `Inserted` would make its next `SaveRoot` an `INSERT` against an existing row — i.e. a `ConcurrencyConflictException` on an ordinary edit. `AccountRowCodec.Read` therefore ends with `OnUpdated()`, and the test below pins it.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteReadTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class SqliteReadTests
    {
        private SqliteMoneyStoreProvisioner provisioner;
        private SqliteMoneyStore store;
        private SqliteMoneyQuery query;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);
            this.store = SqliteMoneyStore.OpenOver(this.provisioner.Connection,
                new SqliteStoreOptions("books", SqliteConnectionFactory.InMemoryDataSource, true));
            this.query = SqliteMoneyQuery.OpenOver(this.provisioner.Connection);
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown()
        {
            this.store?.Dispose();
            this.provisioner?.Dispose();
        }

        [Test]
        public void LoadAccounts_RoundTripsEveryPersistedColumn()
        {
            var original = new Account(this.container)
            {
                Id = 7,
                AccountId = "12345",
                OfxAccountId = "OFX-1",
                Name = "Checking",
                Type = AccountType.Brokerage,
                Description = "main account",
                OpeningBalance = -98765.4321m,
                LastSync = new DateTime(2026, 2, 3, 4, 5, 6, 789),
                LastBalance = new DateTime(2026, 2, 4, 0, 0, 0, 0),
                SyncGuid = new SqlGuid(Guid.Parse("11112222-3333-4444-5555-666677778888")),
                Flags = AccountFlags.Budgeted | AccountFlags.TaxDeferred,
                Currency = "EUR",
                WebSite = "https://example.invalid",
                ReconcileWarning = 3,
            };
            this.store.SaveRoot(original);

            Account loaded = this.store.LoadAccounts().Single();

            Assert.That(loaded.Id, Is.EqualTo(7));
            Assert.That(loaded.AccountId, Is.EqualTo("12345"));
            Assert.That(loaded.OfxAccountId, Is.EqualTo("OFX-1"));
            Assert.That(loaded.Name, Is.EqualTo("Checking"));
            Assert.That(loaded.Type, Is.EqualTo(AccountType.Brokerage));
            Assert.That(loaded.Description, Is.EqualTo("main account"));
            Assert.That(loaded.OpeningBalance, Is.EqualTo(-98765.4321m));
            Assert.That(loaded.LastSync, Is.EqualTo(new DateTime(2026, 2, 3, 4, 5, 6, 789)));
            Assert.That(loaded.LastBalance, Is.EqualTo(new DateTime(2026, 2, 4, 0, 0, 0, 0)));
            Assert.That(loaded.SyncGuid.Value, Is.EqualTo(Guid.Parse("11112222-3333-4444-5555-666677778888")));
            Assert.That(loaded.Flags, Is.EqualTo(AccountFlags.Budgeted | AccountFlags.TaxDeferred));
            Assert.That(loaded.Currency, Is.EqualTo("EUR"));
            Assert.That(loaded.WebSite, Is.EqualTo("https://example.invalid"));
            Assert.That(loaded.ReconcileWarning, Is.EqualTo(3));
        }

        [Test]
        public void LoadAccounts_ReturnsRootsAtChangeStateNone_CarryingTheAuthoritativeVersion()
        {
            // A freshly constructed PersistentObject starts at ChangeType.Inserted and every
            // setter fires OnChanged. A loaded root left at Inserted would make its next SaveRoot
            // an INSERT against an existing row - a conflict on an ordinary edit.
            var a = new Account(this.container) { Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m };
            this.store.SaveRoot(a);

            Account loaded = this.store.LoadAccounts().Single();

            Assert.That(loaded.IsInserted, Is.False);
            Assert.That(loaded.IsChanged, Is.False);
            Assert.That(loaded.IsDeleted, Is.False);
            Assert.That(loaded.RowVersion, Is.EqualTo(1));
            Assert.That(loaded.Parent, Is.Not.Null, "A loaded root must be parented - see CLAUDE.md's new Transaction() gotcha.");
        }

        [Test]
        public void ARootLoadedThenEditedThenSaved_Updates()
        {
            var a = new Account(this.container) { Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m };
            this.store.SaveRoot(a);

            Account loaded = this.store.LoadAccounts().Single();
            loaded.Name = "Renamed";
            this.store.SaveRoot(loaded);

            Assert.That(this.store.LoadAccounts().Single().Name, Is.EqualTo("Renamed"));
            Assert.That(this.store.LoadAccounts().Single().RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void ListAccounts_ReturnsProjectionsForEveryAccount()
        {
            this.Save(1, "Checking", AccountFlags.None);
            this.Save(2, "Old Savings", AccountFlags.Closed);

            IReadOnlyList<AccountRow> rows = this.query.ListAccounts(AccountQuery.All);

            Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Checking", "Old Savings" }));
            Assert.That(rows[1].IsClosed, Is.True);
            Assert.That(rows[0].OpeningBalance, Is.EqualTo(25.5m));
        }

        [Test]
        public void ListAccounts_CanExcludeClosedAccounts()
        {
            this.Save(1, "Checking", AccountFlags.None);
            this.Save(2, "Old Savings", AccountFlags.Closed);

            IReadOnlyList<AccountRow> rows = this.query.ListAccounts(new AccountQuery(Array.Empty<long>(), false));

            Assert.That(rows.Select(r => r.Name), Is.EqualTo(new[] { "Checking" }));
        }

        [Test]
        public void ListAccounts_CanFilterByIds()
        {
            this.Save(1, "Checking", AccountFlags.None);
            this.Save(2, "Savings", AccountFlags.None);
            this.Save(3, "Brokerage", AccountFlags.None);

            IReadOnlyList<AccountRow> rows = this.query.ListAccounts(new AccountQuery(new long[] { 3, 1 }, true));

            Assert.That(rows.Select(r => r.Id), Is.EqualTo(new long[] { 1, 3 }));
        }

        [Test]
        public void NextAccountId_IsOneOnAnEmptyDatabaseAndMaxPlusOneAfterwards()
        {
            Assert.That(this.query.NextAccountId(), Is.EqualTo(1));

            this.Save(1, "Checking", AccountFlags.None);
            this.Save(9, "Savings", AccountFlags.None);

            Assert.That(this.query.NextAccountId(), Is.EqualTo(10));
        }

        private void Save(int id, string name, AccountFlags flags)
        {
            this.store.SaveRoot(new Account(this.container)
            {
                Id = id, Name = name, Type = AccountType.Checking, Currency = "USD",
                OpeningBalance = 25.5m, Flags = flags
            });
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteReadTests"`
Expected: FAIL to compile — `CS0103: The name 'SqliteMoneyQuery' does not exist in the current context`.

- [ ] **Step 3: Implement `LoadAccounts`**

Replace the `LoadAccounts` stub in `SqliteMoneyStore.cs`:
```csharp
        /// <summary>
        /// Returns AGGREGATE ROOTS - objects carrying RowVersion, which is what makes a later
        /// version-checked write possible. This is the write-preparation read spec section 2.7.2
        /// point 3 describes: "to write, you re-read the root through IMoneyStore, which hands you
        /// the authoritative version." Reads that do not intend to write go through IMoneyQuery
        /// and come back as projections instead.
        ///
        /// Each call builds its own Accounts container. With whole-graph Load() gone there is no
        /// long-lived container to maintain; the container exists so every returned Account has a
        /// non-null Parent, which CLAUDE.md records as a real source of NullReferenceException in
        /// business-layer code that walks it.
        /// </summary>
        public override IReadOnlyList<Account> LoadAccounts()
        {
            var container = new Accounts((PersistentObject)null);
            var accounts = new List<Account>();

            using (var cmd = new SQLiteCommand(
                $"SELECT {AccountRowCodec.Columns},Version FROM Accounts ORDER BY Id;", this.connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    accounts.Add(AccountRowCodec.Read(reader, container));
                }
            }

            return accounts;
        }
```

- [ ] **Step 4: Write `SqliteMoneyQuery`**

Create `Source/WPF/MyMoney.Data.Sqlite/SqliteMoneyQuery.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Text.Json;

namespace Walkabout.Data.Sqlite
{
    /// <summary>
    /// The SQLite implementation of the application's read path. Filtering happens in SQL, not in
    /// memory - spec section 3.3 stage 1, replacing today's "load everything then filter in C#".
    ///
    /// Everything it returns is a projection: no RowVersion, not an IAggregateRoot, so it cannot
    /// be handed to any write method. The business layer never names a view or a table here - it
    /// issues a typed AccountQuery and this class decides how to answer it (spec section 2.7.2
    /// point 4). When view-backed shapes arrive they land HERE, behind this same signature.
    ///
    /// Filters are bound parameters throughout, including the id list, which travels as one JSON
    /// parameter through json_each rather than a concatenated IN list - R-CRUD-1.
    /// </summary>
    public sealed class SqliteMoneyQuery : IMoneyQuery
    {
        private const int ClosedFlag = (int)AccountFlags.Closed;

        private readonly SQLiteConnection connection;

        private SqliteMoneyQuery(SQLiteConnection connection)
        {
            this.connection = connection;
        }

        public static SqliteMoneyQuery OpenOver(SQLiteConnection connection) =>
            new SqliteMoneyQuery(connection ?? throw new ArgumentNullException(nameof(connection)));

        public IReadOnlyList<AccountRow> ListAccounts(AccountQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            string sql =
                "SELECT Id, Name, Type, Currency, OpeningBalance, COALESCE(Flags, 0) AS Flags FROM Accounts "
                + "WHERE (@AllIds = 1 OR Id IN (SELECT value FROM json_each(@Ids))) "
                + "AND (@IncludeClosed = 1 OR (COALESCE(Flags, 0) & @ClosedFlag) = 0) "
                + "ORDER BY Id;";

            var rows = new List<AccountRow>();
            using (var cmd = new SQLiteCommand(sql, this.connection))
            {
                cmd.Parameters.AddWithValue("@AllIds", query.AccountIds.Count == 0 ? 1 : 0);
                cmd.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(query.AccountIds));
                cmd.Parameters.AddWithValue("@IncludeClosed", query.IncludeClosed ? 1 : 0);
                cmd.Parameters.AddWithValue("@ClosedFlag", ClosedFlag);

                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        long flags = reader.GetInt64(5);
                        rows.Add(new AccountRow(
                            reader.GetInt64(0),
                            reader.IsDBNull(1) ? null : reader.GetString(1),
                            (AccountType)reader.GetInt64(2),
                            reader.IsDBNull(3) ? null : reader.GetString(3),
                            MoneyScale.FromStorage(reader.GetInt64(4)),
                            (flags & ClosedFlag) != 0));
                    }
                }
            }

            return rows;
        }

        public long NextAccountId()
        {
            using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(Id), 0) + 1 FROM Accounts;", this.connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 52 tests.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteReadTests.cs
git commit -m "$(cat <<'EOF'
feat(data): LoadAccounts (roots) and SqliteMoneyQuery (projections), one slice

Slice 3, task 15. Closes the scheduling gap the spec's own self-review flagged:
IMoneyQuery is implemented on SQLite in the SAME slice as the store's writes,
not deferred. Both ports carry reads and the split is by what comes back -
LoadAccounts returns roots carrying RowVersion (the only legal input to a later
version-checked write), ListAccounts returns projections that carry no version
and cannot be passed to any write method.

Filtering happens in SQL, including the id list, which travels as one JSON
parameter through json_each rather than a concatenated IN list.

A loaded root is put back to change state None, because a freshly constructed
PersistentObject starts at Inserted and would otherwise make its next SaveRoot
an INSERT against an existing row.

Spec: sections 2.7.2, 3.3 stage 1, 6.4. Plan decision D-1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 16: `MyMoney.TestKit` — the in-memory fixture, `RecordingStore`, `FaultInjectingStore`

**Files:**
- Create: `Source/WPF/MyMoney.TestKit/MyMoney.TestKit.csproj`
- Create: `Source/WPF/MyMoney.TestKit/InMemorySqliteStore.cs`
- Create: `Source/WPF/MyMoney.TestKit/RecordingStore.cs`
- Create: `Source/WPF/MyMoney.TestKit/FaultInjectingStore.cs`
- Modify: `Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj`, `Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
- Test: `Source/WPF/MyMoney.Tests.Business/TestKit/StoreDecoratorTests.cs`

**Interfaces:**
- Consumes: `IMoneyStore`, `IMoneyQuery`, `SqliteMoneyStore`, `SqliteMoneyQuery`, `SqliteMoneyStoreProvisioner`.
- Produces:
  - `sealed class InMemorySqliteStore : IDisposable` with `IMoneyStore Store`, `IMoneyQuery Query`, `SqliteMoneyStoreProvisioner Provisioner`, `SQLiteConnection Connection`, `static InMemorySqliteStore Create(bool isTestDatabase = true)`
  - `sealed class RecordingStore : IMoneyStore` with `IReadOnlyList<string> Calls`, `int WriteCount`
  - `sealed class FaultInjectingStore : IMoneyStore` with `void ThrowConflictFor(long rootId)`, `void ThrowOnCall(int callNumber, Exception exception)`, `void Clear()`

**T-1, resolved and implemented (§2.4).** The default business-test double is a **decorated real in-memory SQLite store**; `MockStore` is *not* built. The reasons, restated because they are load-bearing: P-SQLITE says behaviour belongs in the engine, and a hand-written mock is by construction a C# re-implementation of engine behaviour; a mock would need a LINQ-over-memory implementation of every `IMoneyQuery` shape, and a subtly-wrong subtotal in it would make every business-layer report test assert against a fiction that looks green; and the speed objection is weak here because `SqliteDatabase` already holds one long-lived cached connection, which is exactly the lifetime model `:memory:` requires. *(Measuring that is slice 9 — Plan B. This task builds the fixture; it does not claim the measurement.)*

**Why the schema is applied with the production executor.** §2.4 is explicit: use *the same* `MyMoney.Data.Sqlite.Provisioning` executor production uses, never a test-only shortcut. If that turns out too slow per test, the fix is `Backup`/`VACUUM INTO` cloning of a prepared template (Task 9 already built it), **not** a parallel test-only schema path, which would reintroduce drift by the back door.

- [ ] **Step 1: Create the project and wire references**

Create `Source/WPF/MyMoney.TestKit/MyMoney.TestKit.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>MyMoney.TestKit</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <!--
      THE "test business layer" LIBRARY. Library ONLY: no Microsoft.NET.Test.Sdk, no NUnit
      adapter, no [TestFixture] - spec section 2.2, fixing the current MyMoney.TestSupport shape
      where anything wanting the test doubles drags the test runner with it.
      NEVER SHIPPED; deliberately not marked IsProductionAssembly.
    -->
  </PropertyGroup>
  <ItemGroup>
    <!-- NUnit (the framework, not the runner) so StoreContractTests can carry [Test] methods
         that derived fixtures in the test projects inherit - spec section 2.2's "shared
         store-contract base classes". -->
    <PackageReference Include="NUnit" Version="4.6.1" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />
    <PackageReference Include="System.Data.SQLite" Version="2.0.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
    <ProjectReference Include="..\MyMoney.TestKit.Contracts\MyMoney.TestKit.Contracts.csproj" />
    <ProjectReference Include="..\MyMoney.Data.Sqlite\MyMoney.Data.Sqlite.csproj" />
    <ProjectReference Include="..\MyMoney.Data.Sqlite.Provisioning\MyMoney.Data.Sqlite.Provisioning.csproj" />
  </ItemGroup>
</Project>
```

```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.TestKit/MyMoney.TestKit.csproj
dotnet add Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj reference Source/WPF/MyMoney.TestKit/MyMoney.TestKit.csproj
dotnet add Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj reference Source/WPF/MyMoney.TestKit/MyMoney.TestKit.csproj
```

- [ ] **Step 2: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Business/TestKit/StoreDecoratorTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Business.TestKit
{
    [TestFixture]
    public class StoreDecoratorTests
    {
        private InMemorySqliteStore fixture;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.fixture = InMemorySqliteStore.Create();
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private Account New(int id, string name) => new Account(this.container)
        {
            Id = id, Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 0m
        };

        [Test]
        public void InMemorySqliteStore_IsProvisionedAndUsable()
        {
            this.fixture.Store.SaveRoot(this.New(1, "Checking"));

            Assert.That(this.fixture.Store.LoadAccounts().Single().Name, Is.EqualTo("Checking"));
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(1));
            Assert.That(this.fixture.Store.Identity.SchemaVersion, Is.GreaterThan(0));
        }

        [Test]
        public void InMemorySqliteStore_InstancesAreIsolatedFromEachOther()
        {
            this.fixture.Store.SaveRoot(this.New(1, "Checking"));

            using (InMemorySqliteStore other = InMemorySqliteStore.Create())
            {
                Assert.That(other.Store.LoadAccounts(), Is.Empty);
            }
        }

        [Test]
        public void RecordingStore_RecordsWhatWasAskedOfTheDataLayer()
        {
            var recording = new RecordingStore(this.fixture.Store);

            recording.SaveRoot(this.New(1, "Checking"));
            Account deleted = this.New(2, "Doomed");
            recording.SaveRoot(deleted);
            deleted.OnDelete();
            recording.DeleteRoot(deleted);

            Assert.That(recording.Calls, Is.EqualTo(new[]
            {
                "SaveRoot(Account#1)", "SaveRoot(Account#2)", "DeleteRoot(Account#2)"
            }));
            Assert.That(recording.WriteCount, Is.EqualTo(3));
        }

        [Test]
        public void RecordingStore_OnARefusedCall_RecordsNoWrite()
        {
            // The assertion spec section 1.9.7 says is the one that actually matters: proving a
            // guard ran BEFORE any write, not merely that it threw.
            var recording = new RecordingStore(this.fixture.Store);
            Account a = this.New(1, "Checking");
            a.OnDelete();

            Assert.Throws<ArgumentException>(() => recording.SaveRoot(a));

            Assert.That(recording.WriteCount, Is.EqualTo(0));
        }

        [Test]
        public void FaultInjectingStore_ThrowsAConflictForANamedRootOnDemand()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);

            Account a = this.New(1, "Checking");

            Assert.Throws<ConcurrencyConflictException>(() => faulty.SaveRoot(a));
            Assert.That(this.fixture.Store.LoadAccounts(), Is.Empty);
        }

        [Test]
        public void FaultInjectingStore_ConflictsOnceThenLetsTheRetrySucceed()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);

            Account a = this.New(1, "Checking");
            Assert.Throws<ConcurrencyConflictException>(() => faulty.SaveRoot(a));

            Assert.DoesNotThrow(() => faulty.SaveRoot(a));
            Assert.That(this.fixture.Store.LoadAccounts(), Has.Count.EqualTo(1));
        }

        [Test]
        public void FaultInjectingStore_CanThrowAnArbitraryExceptionOnTheNthCall()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(2, new InvalidOperationException("boom"));

            faulty.SaveRoot(this.New(1, "One"));

            Assert.Throws<InvalidOperationException>(() => faulty.SaveRoot(this.New(2, "Two")));
        }

        [Test]
        public void Decorators_ForwardIdentityWithoutAlteringIt()
        {
            var recording = new RecordingStore(this.fixture.Store);
            var faulty = new FaultInjectingStore(recording);

            Assert.That(faulty.Identity, Is.EqualTo(this.fixture.Store.Identity));
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj --filter "FullyQualifiedName~StoreDecoratorTests"`
Expected: FAIL to compile — `CS0246: The type or namespace name 'InMemorySqliteStore' could not be found`.

- [ ] **Step 4: Write `InMemorySqliteStore`**

Create `Source/WPF/MyMoney.TestKit/InMemorySqliteStore.cs`:
```csharp
using System;
using System.Data.SQLite;
using System.Threading;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace MyMoney.TestKit
{
    /// <summary>
    /// T-1, implemented: the default business-test store is a REAL in-memory SQLite database, not
    /// a hand-written mock (spec section 2.4). One :memory: store per test, over one connection,
    /// disposed at teardown - no file, no cleanup, no cross-test leakage.
    ///
    /// An in-memory database lives exactly as long as its connection, so the store, the query and
    /// (from Task 18) the test control all share ONE connection. That is the lifetime model
    /// SqliteDatabase's long-lived cached connection already has, which is why this needs no new
    /// lifetime design.
    ///
    /// The schema is applied with the SAME executor production uses. If per-test provisioning ever
    /// proves too slow, the fix is Backup/VACUUM INTO cloning of a prepared template (already
    /// built, Task 9) - NOT a parallel test-only schema path, which would reintroduce drift by the
    /// back door.
    /// </summary>
    public sealed class InMemorySqliteStore : IDisposable
    {
        private static int counter;

        private readonly SqliteMoneyStore store;

        private InMemorySqliteStore(string displayName, SqliteMoneyStoreProvisioner provisioner, SqliteMoneyStore store, SqliteMoneyQuery query)
        {
            this.DisplayName = displayName;
            this.Provisioner = provisioner;
            this.store = store;
            this.Query = query;
        }

        public static InMemorySqliteStore Create(bool isTestDatabase = true)
        {
            string displayName = "in-memory-" + Interlocked.Increment(ref counter).ToString();

            var provisioner = SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                displayName, SqliteConnectionFactory.InMemoryDataSource, isTestDatabase));
            try
            {
                provisioner.ApplyTo(SchemaStepCatalog.LatestVersion);

                SqliteMoneyStore store = SqliteMoneyStore.OpenOver(
                    provisioner.Connection,
                    new SqliteStoreOptions(displayName, SqliteConnectionFactory.InMemoryDataSource, isTestDatabase));

                return new InMemorySqliteStore(
                    displayName, provisioner, store, SqliteMoneyQuery.OpenOver(provisioner.Connection));
            }
            catch
            {
                provisioner.Dispose();
                throw;
            }
        }

        public string DisplayName { get; }

        public SqliteMoneyStoreProvisioner Provisioner { get; }

        public SQLiteConnection Connection => this.Provisioner.Connection;

        public IMoneyStore Store => this.store;

        public IMoneyQuery Query { get; }

        public void Dispose()
        {
            this.store.Dispose();
            this.Provisioner.Dispose();
        }
    }
}
```

- [ ] **Step 5: Write `RecordingStore` and `FaultInjectingStore`**

Create `Source/WPF/MyMoney.TestKit/RecordingStore.cs`:
```csharp
using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit
{
    /// <summary>
    /// Records every operation so a business test can assert WHAT WAS ASKED of the data layer -
    /// explicitly shape (b) of the business-layer testing responsibility, which is currently done
    /// by inference. Spec section 2.4.
    ///
    /// It implements IMoneyStore DIRECTLY and forwards each of the four public methods, so the
    /// precondition fires in the inner store and the recording happens at the PUBLIC level, where
    /// "SaveRoot(Account#3)" is a more useful record than "WriteRoots([...])". MoneyStoreBase's
    /// protected WriteRoots being unreachable from a decorator is the correct shape, not a
    /// constraint to work around - spec section 1.6c, Developer note 3.
    /// </summary>
    public sealed class RecordingStore : IMoneyStore
    {
        private readonly IMoneyStore inner;
        private readonly List<string> calls = new List<string>();

        public RecordingStore(IMoneyStore inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IReadOnlyList<string> Calls => this.calls;

        /// <summary>How many calls actually reached the inner store (a refused call is not one).</summary>
        public int WriteCount { get; private set; }

        public StoreIdentity Identity => this.inner.Identity;

        public void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.inner.SaveRoot(root);
            this.Record("SaveRoot", root);
        }

        public void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.inner.DeleteRoot(root);
            this.Record("DeleteRoot", root);
        }

        public void SaveRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            this.inner.SaveRoots(roots);
            var names = new List<string>(roots.Count);
            foreach (IAggregateRoot root in roots)
            {
                names.Add(Describe(root));
            }

            this.calls.Add($"SaveRoots([{string.Join(", ", names)}])");
            this.WriteCount++;
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            this.inner.SaveTransfer(from, to);
            this.calls.Add($"SaveTransfer({Describe(from)}, {Describe(to)})");
            this.WriteCount++;
        }

        public IReadOnlyList<Account> LoadAccounts()
        {
            this.calls.Add("LoadAccounts()");
            return this.inner.LoadAccounts();
        }

        public void Dispose() => this.inner.Dispose();

        private void Record(string method, IAggregateRoot root)
        {
            this.calls.Add($"{method}({Describe(root)})");
            this.WriteCount++;
        }

        private static string Describe(IAggregateRoot root) => $"{root.GetType().Name}#{root.Id}";
    }
}
```

Create `Source/WPF/MyMoney.TestKit/FaultInjectingStore.cs`:
```csharp
using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace MyMoney.TestKit
{
    /// <summary>
    /// The one, first-class, documented way a business-layer test deliberately provokes a
    /// ConcurrencyConflictException and asserts on the AppServices retry loop's behaviour - spec
    /// sections 1.6a and 2.4. It supersedes the old ad hoc pattern (save a root, then mutate its
    /// RowVersion to a stale value before saving again), which was a real mechanism but an
    /// incidental side effect of test setup rather than a named capability.
    ///
    /// Revision 6 gave it one more job (spec section 1.6c, Test Engineer note 3): proving the
    /// retry loop lets an ArgumentException from SaveRoot/DeleteRoot's preconditions STRAIGHT
    /// THROUGH rather than treating it as a conflict and retrying a call that can only fail
    /// identically forever. That is what Task 24 uses it for.
    ///
    /// A conflict registered with ThrowConflictFor fires ONCE and then clears, so a test can
    /// assert that the retry succeeds.
    /// </summary>
    public sealed class FaultInjectingStore : IMoneyStore
    {
        private readonly IMoneyStore inner;
        private readonly HashSet<long> conflictingRootIds = new HashSet<long>();
        private readonly Dictionary<int, Exception> scheduled = new Dictionary<int, Exception>();
        private int callNumber;

        public FaultInjectingStore(IMoneyStore inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public StoreIdentity Identity => this.inner.Identity;

        /// <summary>The next write touching this root id conflicts; the one after it does not.</summary>
        public void ThrowConflictFor(long rootId) => this.conflictingRootIds.Add(rootId);

        /// <summary>Throw <paramref name="exception"/> on the Nth write call (1-based).</summary>
        public void ThrowOnCall(int number, Exception exception) => this.scheduled[number] = exception;

        public void Clear()
        {
            this.conflictingRootIds.Clear();
            this.scheduled.Clear();
            this.callNumber = 0;
        }

        public void SaveRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.Intercept(new IAggregateRoot[] { root });
            this.inner.SaveRoot(root);
        }

        public void DeleteRoot<TRoot>(TRoot root) where TRoot : PersistentObject, IAggregateRoot
        {
            this.Intercept(new IAggregateRoot[] { root });
            this.inner.DeleteRoot(root);
        }

        public void SaveRoots(IReadOnlyList<IAggregateRoot> roots)
        {
            this.Intercept(roots);
            this.inner.SaveRoots(roots);
        }

        public void SaveTransfer(Transaction from, Transaction to)
        {
            this.Intercept(new IAggregateRoot[] { from, to });
            this.inner.SaveTransfer(from, to);
        }

        public IReadOnlyList<Account> LoadAccounts() => this.inner.LoadAccounts();

        public void Dispose() => this.inner.Dispose();

        private void Intercept(IReadOnlyList<IAggregateRoot> roots)
        {
            this.callNumber++;

            if (this.scheduled.TryGetValue(this.callNumber, out Exception scheduledException))
            {
                this.scheduled.Remove(this.callNumber);
                throw scheduledException;
            }

            foreach (IAggregateRoot root in roots)
            {
                if (this.conflictingRootIds.Remove(root.Id))
                {
                    throw new ConcurrencyConflictException(
                        (PersistentObject)root,
                        ((root as PersistentObject)?.RowVersion ?? 0) + 1,
                        (root as PersistentObject)?.RowVersion ?? 0);
                }
            }
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj`
Expected: PASS — 8 new tests plus the 23 from Tasks 1 and 4.

- [ ] **Step 7: Commit**
```bash
git add Source/WPF/MyMoney.TestKit Source/WPF/MyMoney.Tests.Business Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj Source/WPF/MyMoney.sln
git commit -m "$(cat <<'EOF'
feat(test): MyMoney.TestKit - in-memory SQLite fixture and the two store decorators

Slice 4, task 16. T-1 implemented: the default business-test store is a real
in-memory SQLite database, not a hand-written MockStore, so a mock and an engine
cannot disagree and no second LINQ-over-memory implementation of IMoneyQuery can
silently drift from the SQL one. The schema is applied with the same executor
production uses.

MyMoney.TestKit is a library only - no test runner, no [TestFixture] - fixing
the current MyMoney.TestSupport shape where anything wanting the test doubles
drags the runner with it.

FaultInjectingStore is the named, first-class way to provoke a concurrency
conflict on demand, which is what the spec asked the test business layer for.

Spec: sections 2.2, 2.4 (T-1), 1.6a, 1.6c, 6.5.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 17: `StoreContractTests` — one suite covering `IMoneyStore` **and** `IMoneyQuery`

**Files:**
- Create: `Source/WPF/MyMoney.TestKit/StoreContractTests.cs`
- Create: `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteStoreContractTests.cs`
- Test: the above (the base class carries the `[Test]` methods; the derived fixture runs them)

**Interfaces:**
- Consumes: `IMoneyStore`, `IMoneyQuery`, `IMoneyStoreProvisioner`, `InMemorySqliteStore`.
- Produces: `public abstract class StoreContractTests` with `protected abstract IMoneyStore Store { get; }`, `protected abstract IMoneyQuery Query { get; }`, `protected abstract IMoneyStoreProvisioner Provisioner { get; }`, `protected abstract int ExpectedStoreMaxSchemaVersion { get; }`, `protected Accounts Container { get; }`.

**Why this exists and why it covers both ports.** §6.4: *"the contract suite must grow to cover `IMoneyQuery` in the same slice the port is introduced — not later,"* because a subtly-wrong subtotal makes every business-layer report test assert against a fiction that looks green. With no mock, the "two implementations" are the two *engines*, and this suite is what proves they are indistinguishable through the ports. **Plan B's slice 8 adds exactly one file** — a SQL Server fixture deriving from this same base — and if the two engines disagree, this is where it shows.

Per §1.6c's Test Engineer note, the six precondition cases run **per engine** even though the check lives once in `MoneyStoreBase`: a precondition is part of the port's observable behaviour, and the suite's job is to prove the engines are indistinguishable through the port.

- [ ] **Step 1: Write the contract base class**

Create `Source/WPF/MyMoney.TestKit/StoreContractTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace MyMoney.TestKit
{
    /// <summary>
    /// The shared, engine-agnostic contract suite. One derived fixture per engine; if the two
    /// disagree, this is where it shows. Spec sections 2.3 tier 2 and 6.4.
    ///
    /// It covers IMoneyQuery as well as IMoneyStore, from its first commit, because a query surface
    /// the suite does not cover is a surface every business-layer test asserts against on trust.
    /// </summary>
    public abstract class StoreContractTests
    {
        protected abstract IMoneyStore Store { get; }

        protected abstract IMoneyQuery Query { get; }

        protected abstract IMoneyStoreProvisioner Provisioner { get; }

        /// <summary>
        /// The store assembly's MaxKnownSchemaVersion. Kept in the store rather than read from the
        /// provisioner's catalog because the store assembly deliberately does not reference the
        /// provisioning one; this assertion is what stops the two silently drifting.
        /// </summary>
        protected abstract int ExpectedStoreMaxSchemaVersion { get; }

        protected Accounts Container { get; } = new Accounts((PersistentObject)null);

        protected Account NewAccount(string name) => new Account(this.Container)
        {
            Id = checked((int)this.Query.NextAccountId()),
            Name = name,
            Type = AccountType.Checking,
            Currency = "USD",
            OpeningBalance = 0m,
        };

        // ---- schema / identity ----

        [Test]
        public void TheStoreAndTheProvisionerAgreeOnTheSchemaVersion()
        {
            Assert.That(this.Store.Identity.SchemaVersion, Is.EqualTo(this.Provisioner.CurrentVersion()));
            Assert.That(this.Provisioner.LatestKnownVersion, Is.EqualTo(this.ExpectedStoreMaxSchemaVersion),
                "The store's MaxKnownSchemaVersion has drifted from the provisioner's step catalog. "
                + "They are separate constants because the two assemblies deliberately do not "
                + "reference each other; this assertion is what keeps them honest.");
        }

        [Test]
        public void AFreshlyProvisionedSchemaVerifiesClean()
        {
            Assert.That(this.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void TheSchemaOwnsNoInsteadOfTrigger()
        {
            // Spec section 2.7.3: INSTEAD OF triggers are demoted to "do not adopt now", kept as a
            // deferred single-candidate spike with a standing "never above IMoneyStore" constraint.
            // If the spike is ever taken, THIS is the test someone has to deliberately amend, which
            // is exactly the conversation that should happen at that moment. Correct under either
            // answer to the views question the spec flagged back to the owner.
            Assert.That(this.InsteadOfTriggerCount(), Is.EqualTo(0));
        }

        protected abstract int InsteadOfTriggerCount();

        // ---- write surface preconditions, per engine (spec 1.6c, Test Engineer note 2) ----

        [Test]
        public void SaveRoot_OnADeletedRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);
            long countBefore = this.Query.ListAccounts(AccountQuery.All).Count;

            a.Name = "Renamed";
            a.OnDelete();

            Assert.Throws<ArgumentException>(() => this.Store.SaveRoot(a));
            Assert.That(this.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(countBefore));
            Assert.That(this.Store.LoadAccounts().Single(x => x.Id == a.Id).Name, Is.EqualTo("Checking"));
        }

        [Test]
        public void DeleteRoot_OnAnInsertedRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Never Saved");

            Assert.Throws<ArgumentException>(() => this.Store.DeleteRoot(a));
            Assert.That(this.Query.ListAccounts(AccountQuery.All), Is.Empty);
        }

        [Test]
        public void DeleteRoot_OnAChangedRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);
            a.Name = "Renamed";

            Assert.Throws<ArgumentException>(() => this.Store.DeleteRoot(a));
            Assert.That(this.Store.LoadAccounts().Single().Name, Is.EqualTo("Checking"));
        }

        [Test]
        public void DeleteRoot_OnACleanRoot_ThrowsArgumentExceptionAndWritesNothing()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);

            Assert.Throws<ArgumentException>(() => this.Store.DeleteRoot(a));
            Assert.That(this.Store.LoadAccounts(), Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveRoot_OnACleanRoot_Succeeds()
        {
            // The case a tighter precondition would have broken. Account has no owned children, so
            // here it is a clean no-op; the slice that brings Transaction extends this to "a root
            // at None with a dirty owned child SUCCEEDS and writes the child" - spec 1.6c.
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);

            Assert.DoesNotThrow(() => this.Store.SaveRoot(a));
        }

        [Test]
        public void AfterAConflict_TheSameCallIsStillAdmissible()
        {
            // What makes the business-layer retry loop legal at all - spec 1.6a consequence 2.
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);

            long goodVersion = a.RowVersion;
            a.Name = "Renamed";
            a.RowVersion = goodVersion + 99;

            Assert.Throws<ConcurrencyConflictException>(() => this.Store.SaveRoot(a));
            Assert.That(a.IsChanged, Is.True);

            a.RowVersion = goodVersion;
            Assert.DoesNotThrow(() => this.Store.SaveRoot(a));
        }

        // ---- round trip ----

        [Test]
        public void ARootSurvivesASaveLoadEditSaveCycle()
        {
            Account a = this.NewAccount("Checking");
            a.OpeningBalance = 1234.5678m;
            this.Store.SaveRoot(a);

            Account loaded = this.Store.LoadAccounts().Single();
            Assert.That(loaded.OpeningBalance, Is.EqualTo(1234.5678m));
            Assert.That(loaded.RowVersion, Is.EqualTo(1));

            loaded.OpeningBalance = -0.0001m;
            this.Store.SaveRoot(loaded);

            Assert.That(this.Store.LoadAccounts().Single().OpeningBalance, Is.EqualTo(-0.0001m));
            Assert.That(this.Store.LoadAccounts().Single().RowVersion, Is.EqualTo(2));
        }

        [Test]
        public void DeleteRoot_OnANeverPersistedRoot_IsASilentNoOp()
        {
            // The plan's decision D-2, pinned as a per-engine contract so Plan B's SQL Server
            // implementation has to make the same call.
            Account a = this.NewAccount("Never Saved");
            a.OnDelete();

            Assert.DoesNotThrow(() => this.Store.DeleteRoot(a));
            Assert.That(this.Query.ListAccounts(AccountQuery.All), Is.Empty);
        }

        [Test]
        public void DeleteRoot_TwiceOnAPersistedRoot_ConflictsOnTheSecondCall()
        {
            Account a = this.NewAccount("Checking");
            this.Store.SaveRoot(a);
            a.OnDelete();
            this.Store.DeleteRoot(a);

            Assert.Throws<ConcurrencyConflictException>(() => this.Store.DeleteRoot(a));
        }

        [Test]
        public void SaveRoot_OnAnIdThatAlreadyExists_ReportsAConcurrencyConflict()
        {
            // The deliberate extension of R-CRUD-4 to the insert race (Task 12). Pinned per engine
            // so Plan B must match it - spec section 6.8 rule 1.
            Account first = this.NewAccount("Checking");
            this.Store.SaveRoot(first);

            Account colliding = new Account(this.Container)
            {
                Id = first.Id, Name = "Collides", Type = AccountType.Cash, OpeningBalance = 0m
            };

            Assert.Throws<ConcurrencyConflictException>(() => this.Store.SaveRoot(colliding));
        }

        // ---- IMoneyQuery ----

        [Test]
        public void ListAccounts_ReturnsProjectionsThatCannotCarryAVersionBack()
        {
            Account a = this.NewAccount("Checking");
            a.OpeningBalance = 42.25m;
            this.Store.SaveRoot(a);

            AccountRow row = this.Query.ListAccounts(AccountQuery.All).Single();

            Assert.That(row.Id, Is.EqualTo(a.Id));
            Assert.That(row.Name, Is.EqualTo("Checking"));
            Assert.That(row.OpeningBalance, Is.EqualTo(42.25m));
            Assert.That(row.IsClosed, Is.False);
            Assert.That(row, Is.Not.InstanceOf<IAggregateRoot>());
        }

        [Test]
        public void ListAccounts_HonoursTheClosedFilter()
        {
            Account open = this.NewAccount("Open");
            this.Store.SaveRoot(open);
            Account closed = this.NewAccount("Closed");
            closed.Flags = AccountFlags.Closed;
            this.Store.SaveRoot(closed);

            Assert.That(this.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(2));
            Assert.That(
                this.Query.ListAccounts(new AccountQuery(Array.Empty<long>(), false)).Select(r => r.Name),
                Is.EqualTo(new[] { "Open" }));
        }

        [Test]
        public void ListAccounts_HonoursTheIdFilter()
        {
            Account one = this.NewAccount("One");
            this.Store.SaveRoot(one);
            Account two = this.NewAccount("Two");
            this.Store.SaveRoot(two);

            Assert.That(
                this.Query.ListAccounts(new AccountQuery(new[] { (long)two.Id }, true)).Select(r => r.Name),
                Is.EqualTo(new[] { "Two" }));
        }

        [Test]
        public void NextAccountId_AdvancesAsAccountsAreAdded()
        {
            long first = this.Query.NextAccountId();
            this.Store.SaveRoot(this.NewAccount("Checking"));

            Assert.That(this.Query.NextAccountId(), Is.GreaterThan(first));
        }
    }
}
```

- [ ] **Step 2: Write the SQLite fixture**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/SqliteStoreContractTests.cs`:
```csharp
using System;
using System.Data.SQLite;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;

namespace Walkabout.Tests.Data.Sqlite
{
    /// <summary>
    /// The SQLite arm of the shared contract suite. Plan B's slice 8 adds exactly one more file
    /// like this one for SQL Server.
    /// </summary>
    [TestFixture]
    public class SqliteStoreContractTests : StoreContractTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        protected override IMoneyStore Store => this.fixture.Store;

        protected override IMoneyQuery Query => this.fixture.Query;

        protected override IMoneyStoreProvisioner Provisioner => this.fixture.Provisioner;

        protected override int ExpectedStoreMaxSchemaVersion => SqliteMoneyStore.MaxKnownSchemaVersion;

        protected override int InsteadOfTriggerCount()
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' "
                + "AND UPPER(sql) LIKE '%INSTEAD OF%';",
                this.fixture.Connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }
    }
}
```

- [ ] **Step 3: Run the suite to verify it passes**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~SqliteStoreContractTests"`
Expected: PASS — 17 tests. If `TheStoreAndTheProvisionerAgreeOnTheSchemaVersion` fails, `SqliteMoneyStore.MaxKnownSchemaVersion` needs raising to match the step catalog; that is the drift this assertion exists to catch.

- [ ] **Step 4: Remove the now-duplicated per-task tests**

Delete the cases that the contract suite now owns, so each behaviour is asserted once (DRY):
- from `SqliteWriteDeleteTests.cs`: `DeleteRoot_OnARootThatWasNeverPersisted_IsASilentNoOp`, `DeleteRoot_Twice_OnAPersistedRoot_ThrowsOnTheSecondCall`
- from `SqliteWriteInsertTests.cs`: `SaveRoot_OnAnIdThatAlreadyExists_ThrowsConcurrencyConflictAndWritesNothing`
- from `SqliteWriteUpdateTests.cs`: `AfterAConflict_TheSameCallIsStillAdmissible`

Keep everything else: those cases assert SQLite-specific mechanics (the `json_each` statement, `typeof(OpeningBalance)`, batch rollback) that are not port-level behaviour and do not belong in an engine-agnostic suite.

- [ ] **Step 5: Run the whole data tier to verify it passes**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 65 tests.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.TestKit/StoreContractTests.cs Source/WPF/MyMoney.Tests.Data/Sqlite
git commit -m "$(cat <<'EOF'
test(data): shared StoreContractTests covering IMoneyStore and IMoneyQuery

Slice 4, task 17. One engine-agnostic suite, one derived fixture per engine;
Plan B's slice 8 adds exactly one more file. It covers IMoneyQuery from its
first commit, because a query surface the suite does not cover is one every
business-layer test asserts against on trust - and with no mock, the "two
implementations" that could diverge are the two engines.

Carries the six write-surface precondition cases per engine even though the
check lives once in MoneyStoreBase: a precondition is part of the port's
observable behaviour. Also pins DeleteRoot's never-persisted semantics and the
insert-collision conflict per engine, so Plan B must match both, and asserts
the store's MaxKnownSchemaVersion agrees with the provisioner's step catalog.

Spec: sections 2.3, 2.7.3, 6.4, 6.8 rule 1, 1.6a, 1.6c.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 18: the test tier's schema ladder — `SqliteTableOrder`, `DropAll`, `ResetSchema`

**Files:**
- Create: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteTableOrder.cs`
- Modify: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs` (`DropAll`)
- Create: `Source/WPF/MyMoney.Data.Sqlite.TestTier/MyMoney.Data.Sqlite.TestTier.csproj`
- Create: `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteMoneyStoreTestControl.cs`
- Create: `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteTestControlFactory.cs`
- Modify: `Source/WPF/MyMoney.TestKit/MyMoney.TestKit.csproj`, `InMemorySqliteStore.cs` (expose `TestControl`)
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlSchemaLadderTests.cs`

**Interfaces:**
- Consumes: `IMoneyStoreTestControl` (Task 3), `SqliteMoneyStoreProvisioner` (Tasks 6–9).
- Produces:
  - `static class SqliteTableOrder` with `static IReadOnlyList<string> ForDelete(SQLiteConnection c)` (reverse-FK-topological, `__SchemaHistory` excluded), `static IReadOnlyList<string> DataTables(SQLiteConnection c)`
  - `sealed class SqliteMoneyStoreTestControl : IMoneyStoreTestControl` (schema ladder in this task; data and row ladders in Tasks 19 and 20)
  - `static class SqliteTestControlFactory` with `static IMoneyStoreTestControl Acquire(SqliteMoneyStoreProvisioner provisioner)`
  - `InMemorySqliteStore.TestControl`

**Two placement points, both derived from the spec rather than chosen:**

1. **`DropAll` lives on the shipped provisioner, not in the test tier.** §1.9.3's call-site table puts it there, and §1.8 gives the reason: *"the assembly isn't shipped" protects end users but does not protect a developer running a test suite against the wrong registry entry.* The test tier's `ResetSchema` calls it. Task 21 puts the guard in front of it.
2. **Drop order is derived, never hand-written.** §2.6.4 is emphatic: a hand-maintained table list has issue #34's exact failure shape — someone adds a table in step 23, forgets the list, and every test from then on runs against a table that is never emptied, silently. `SqliteTableOrder` introspects `PRAGMA foreign_key_list` and topologically sorts, so a new table is handled the moment its step exists.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlSchemaLadderTests.cs`:
```csharp
using System;
using System.Data.SQLite;
using System.Linq;
using MyMoney.TestKit;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class TestControlSchemaLadderTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private long ObjectCount()
        {
            using (var cmd = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%';", this.fixture.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        [Test]
        public void DropAll_RemovesEverySchemaObjectIncludingTheLedger()
        {
            Assert.That(this.ObjectCount(), Is.GreaterThan(0));

            this.fixture.Provisioner.DropAll();

            Assert.That(this.ObjectCount(), Is.EqualTo(0));
            Assert.That(this.fixture.Provisioner.CurrentVersion(), Is.EqualTo(0));
        }

        [Test]
        public void DropAll_IsIdempotent()
        {
            this.fixture.Provisioner.DropAll();

            Assert.DoesNotThrow(() => this.fixture.Provisioner.DropAll());
        }

        [Test]
        public void ResetSchema_IsDropAllThenApplyTo_AndLeavesAVerifiedCleanSchema()
        {
            var a = new Account(new Accounts((PersistentObject)null))
            {
                Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m
            };
            this.fixture.Store.SaveRoot(a);

            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);

            Assert.That(this.fixture.TestControl.CurrentSchemaVersion, Is.EqualTo(SchemaStepCatalog.LatestVersion));
            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All), Is.Empty);
        }

        [Test]
        public void ResetSchema_IsIdempotent()
        {
            // Spec section 2.6.8: idempotency is what makes "nuke and pave is over" true after
            // EVERY call to a reset routine, not just the first.
            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);
            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);

            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void ResetSchema_RecoversFromADriftedSchema()
        {
            // The normal reason to reset during nuke-and-pave. ClearAllData could not do this,
            // which is why the two ladders are separate - spec section 2.6.1.
            using (var cmd = new SQLiteCommand("ALTER TABLE Accounts ADD COLUMN Rogue TEXT;", this.fixture.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.False);

            this.fixture.TestControl.ResetSchema(SchemaStepCatalog.LatestVersion);

            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void TableOrder_PutsAReferencingTableBeforeTheTableItReferences()
        {
            var order = SqliteTableOrder.ForDelete(this.fixture.Connection).ToList();

            Assert.That(order, Does.Not.Contain("__SchemaHistory"));
            Assert.That(order.IndexOf("Accounts"), Is.LessThan(order.IndexOf("OnlineAccounts")));
            Assert.That(order.IndexOf("Accounts"), Is.LessThan(order.IndexOf("Categories")));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~TestControlSchemaLadderTests"`
Expected: FAIL to compile — `CS0103: The name 'SqliteTableOrder' does not exist in the current context`.

- [ ] **Step 3: Write `SqliteTableOrder`**

Create `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteTableOrder.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Walkabout.Data.Sqlite.Provisioning
{
    /// <summary>
    /// The FK-safe order for emptying or dropping tables, DERIVED BY INTROSPECTION and never
    /// hand-written.
    ///
    /// This is the single most important implementation constraint in spec section 2.6.4, and the
    /// reason is issue #34's lesson applied where it would otherwise recur verbatim: a
    /// hand-maintained list has exactly #34's failure shape - someone adds a table in step 23,
    /// forgets the list, and every test from then on runs against a table that is never emptied,
    /// silently, with the symptom appearing somewhere else entirely. Deriving the list means a new
    /// table is handled the moment its step exists, by the same route that a new index is created
    /// the moment its step exists.
    /// </summary>
    public static class SqliteTableOrder
    {
        public const string HistoryTable = "__SchemaHistory";

        /// <summary>Data tables: every table except SQLite internals and the schema ledger.</summary>
        public static IReadOnlyList<string> DataTables(SQLiteConnection connection)
        {
            var tables = new List<string>();
            using (var cmd = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' "
                + "AND name <> @history ORDER BY name;", connection))
            {
                cmd.Parameters.AddWithValue("@history", HistoryTable);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }
            }

            return tables;
        }

        /// <summary>
        /// Reverse topological order over PRAGMA foreign_key_list: a table that REFERENCES another
        /// comes first, so its rows are gone before the referenced table is touched. A cycle (a
        /// self-referencing table, or a genuine loop) cannot be ordered, and the caller's
        /// PRAGMA defer_foreign_keys = ON inside the transaction is the safety net - the same
        /// pragma SqliteDatabase.SaveBatch already uses.
        /// </summary>
        public static IReadOnlyList<string> ForDelete(SQLiteConnection connection)
        {
            IReadOnlyList<string> tables = DataTables(connection);
            var dependsOn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string table in tables)
            {
                dependsOn[table] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (string table in tables)
            {
                using (var cmd = new SQLiteCommand("SELECT \"table\" FROM pragma_foreign_key_list(@t);", connection))
                {
                    cmd.Parameters.AddWithValue("@t", table);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string referenced = reader.GetString(0);
                            if (dependsOn.ContainsKey(referenced)
                                && !string.Equals(referenced, table, StringComparison.OrdinalIgnoreCase))
                            {
                                dependsOn[table].Add(referenced);
                            }
                        }
                    }
                }
            }

            // Depth-first: emit a table only after everything that references it, i.e. visit
            // referencing tables first. Equivalent to "children before parents".
            var ordered = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(string table)
            {
                if (!visited.Add(table))
                {
                    return;
                }

                foreach (string other in tables)
                {
                    if (dependsOn[other].Contains(table))
                    {
                        Visit(other);
                    }
                }

                if (!ordered.Contains(table, StringComparer.OrdinalIgnoreCase))
                {
                    ordered.Add(table);
                }
            }

            foreach (string table in tables)
            {
                Visit(table);
            }

            return ordered;
        }
    }
}
```

- [ ] **Step 4: Implement `DropAll`**

Replace the `DropAll` stub in `SqliteMoneyStoreProvisioner.cs`:
```csharp
        /// <summary>
        /// Drops every object this schema family owns, INCLUDING __SchemaHistory, so
        /// CurrentVersion() returns 0 afterwards and the next ApplyTo(N) is literally
        /// indistinguishable from creating a new database - spec section 1.5, S-1.
        ///
        /// Views before tables (required on SQL Server, done here for parity of the step list -
        /// spec section 2.7.1), then triggers, then tables in reverse-FK order. Idempotent: every
        /// statement is IF EXISTS and the object list is introspected each time. Task 21 puts
        /// TestDatabaseGuard.Require at the top of this method.
        /// </summary>
        public void DropAll()
        {
            using (SQLiteTransaction tx = this.connection.BeginTransaction(deferredLock: false))
            {
                try
                {
                    using (var cmd = new SQLiteCommand("PRAGMA defer_foreign_keys = ON;", this.connection, tx))
                    {
                        cmd.ExecuteNonQuery();
                    }

                    foreach (string view in ObjectNames(this.connection, tx, "view"))
                    {
                        Execute(this.connection, tx, $"DROP VIEW IF EXISTS \"{Quote(view)}\";");
                    }

                    foreach (string trigger in ObjectNames(this.connection, tx, "trigger"))
                    {
                        Execute(this.connection, tx, $"DROP TRIGGER IF EXISTS \"{Quote(trigger)}\";");
                    }

                    foreach (string table in SqliteTableOrder.ForDelete(this.connection))
                    {
                        Execute(this.connection, tx, $"DROP TABLE IF EXISTS \"{Quote(table)}\";");
                    }

                    Execute(this.connection, tx, $"DROP TABLE IF EXISTS \"{SqliteTableOrder.HistoryTable}\";");

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private static IReadOnlyList<string> ObjectNames(SQLiteConnection connection, SQLiteTransaction tx, string type)
        {
            var names = new List<string>();
            using (var cmd = new SQLiteCommand(
                "SELECT name FROM sqlite_master WHERE type = @type AND name NOT LIKE 'sqlite_%';",
                connection, tx))
            {
                cmd.Parameters.AddWithValue("@type", type);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        names.Add(reader.GetString(0));
                    }
                }
            }

            return names;
        }

        private static void Execute(SQLiteConnection connection, SQLiteTransaction tx, string sql)
        {
            using (var cmd = new SQLiteCommand(sql, connection, tx))
            {
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// An identifier cannot be parameterized in any SQL dialect, so interpolation is
        /// unavoidable here. The names come from sqlite_master introspection, never from a caller,
        /// and a name containing a double quote is rejected rather than escaped - global
        /// constraint R-CRUD-1's identifier rule.
        /// </summary>
        private static string Quote(string identifier)
        {
            if (identifier.IndexOf('"') >= 0)
            {
                throw new InvalidOperationException($"Refusing to build SQL for the identifier {identifier}.");
            }

            return identifier;
        }
```

- [ ] **Step 5: Create the test tier and its schema ladder**

Create `Source/WPF/MyMoney.Data.Sqlite.TestTier/MyMoney.Data.Sqlite.TestTier.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>Walkabout.Data.Sqlite.TestTier</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <!-- NEVER SHIPPED. This is the SQLite facade's teeth: the code that can empty the database
         does not exist in any assembly a release build contains - spec section 1. Deliberately
         not marked IsProductionAssembly. -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
    <ProjectReference Include="..\MyMoney.TestKit.Contracts\MyMoney.TestKit.Contracts.csproj" />
    <ProjectReference Include="..\MyMoney.Data.Sqlite.Provisioning\MyMoney.Data.Sqlite.Provisioning.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />
    <PackageReference Include="System.Data.SQLite" Version="2.0.4" />
  </ItemGroup>
</Project>
```

Create `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteMoneyStoreTestControl.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using MyMoney.TestKit.Contracts;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Data.Sqlite.TestTier
{
    /// <summary>
    /// The SQLite facade, for real. Every operation here runs BELOW the object graph and BELOW the
    /// version check, so a test can put the store into a state the domain rules would not permit.
    /// Spec section 2.6.3.
    ///
    /// Obtain it only through SqliteTestControlFactory.Acquire - that is the one guarded
    /// acquisition point (spec section 2.6.4: the check goes where the instance is handed out, not
    /// in twenty methods where the twenty-first is the one that forgets).
    ///
    /// Data ladder: Task 19. Row ladder: Task 20.
    /// </summary>
    public sealed class SqliteMoneyStoreTestControl : IMoneyStoreTestControl
    {
        private readonly SqliteMoneyStoreProvisioner provisioner;

        internal SqliteMoneyStoreTestControl(SqliteMoneyStoreProvisioner provisioner)
        {
            this.provisioner = provisioner ?? throw new ArgumentNullException(nameof(provisioner));
        }

        private SQLiteConnection Connection => this.provisioner.Connection;

        public int CurrentSchemaVersion => this.provisioner.CurrentVersion();

        public void ResetSchema(int targetVersion)
        {
            // Unconditionally drop then apply. Neither half is conditioned on "was this already
            // reset", which is what makes it idempotent by construction - spec section 2.6.8.
            this.provisioner.DropAll();
            this.provisioner.ApplyTo(targetVersion);
        }

        public ClearResult ClearAllData(ClearOptions options = null) => throw new NotImplementedException("Task 19.");

        public ClearResult ClearTables(IReadOnlyCollection<TableRef> tables, ClearOptions options = null) =>
            throw new NotImplementedException("Task 19.");

        public ClearResult ClearTable(TableRef table, ClearOptions options = null) =>
            throw new NotImplementedException("Task 19.");

        public ClearResult ClearTable<TRoot>(ClearOptions options = null) where TRoot : IAggregateRoot =>
            this.ClearTable(TableRef.Of<TRoot>(), options);

        public RowSnapshot CaptureRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public RowSnapshot TryCaptureRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public void DeleteRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public void RestoreRow(RowSnapshot row) => throw new NotImplementedException("Task 20.");

        public IRowScope RemoveRow(TableRef table, long id) => throw new NotImplementedException("Task 20.");

        public IReadOnlyList<TableRef> Tables => throw new NotImplementedException("Task 19.");

        public long RowCount(TableRef table) => throw new NotImplementedException("Task 19.");

        public void Dispose()
        {
            // The connection belongs to the provisioner, which belongs to the fixture.
        }
    }
}
```

Create `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteTestControlFactory.cs`:
```csharp
using System;
using MyMoney.TestKit.Contracts;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Data.Sqlite.TestTier
{
    /// <summary>
    /// The ONE place an IMoneyStoreTestControl is obtained, which is where the TestDatabase guard
    /// goes (Task 21).
    ///
    /// Spec section 2.6.4's reasoning, which is worth having at the call site: ResetSchema reads as
    /// dangerous and gets respect; ClearTable(TableRef.Payees) reads as housekeeping. So the check
    /// belongs at acquisition - the factory refuses to hand back an instance at all - rather than
    /// repeated in twenty methods where the twenty-first will be forgotten.
    /// </summary>
    public static class SqliteTestControlFactory
    {
        public static IMoneyStoreTestControl Acquire(SqliteMoneyStoreProvisioner provisioner)
        {
            if (provisioner == null)
            {
                throw new ArgumentNullException(nameof(provisioner));
            }

            // Task 21 inserts TestDatabaseGuard.Require(provisioner.Identity, "Test control") here.
            return new SqliteMoneyStoreTestControl(provisioner);
        }
    }
}
```

- [ ] **Step 6: Expose `TestControl` from the fixture**

```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Data.Sqlite.TestTier/MyMoney.Data.Sqlite.TestTier.csproj
dotnet add Source/WPF/MyMoney.TestKit/MyMoney.TestKit.csproj reference Source/WPF/MyMoney.Data.Sqlite.TestTier/MyMoney.Data.Sqlite.TestTier.csproj
```

In `InMemorySqliteStore.cs`, add the property and set it in `Create`:
```csharp
        /// <summary>
        /// The fixture's test control, over the SAME connection - an in-memory database lives
        /// exactly as long as its connection. Obtained through the guarded factory, so the fixture
        /// exercises the same acquisition path a test author would.
        /// </summary>
        public IMoneyStoreTestControl TestControl { get; private set; }
```
with, inside `Create` just before the `return`:
```csharp
                var fixture = new InMemorySqliteStore(
                    displayName, provisioner, store, SqliteMoneyQuery.OpenOver(provisioner.Connection));
                fixture.TestControl = SqliteTestControlFactory.Acquire(provisioner);
                return fixture;
```
(add `using MyMoney.TestKit.Contracts;` and `using Walkabout.Data.Sqlite.TestTier;`).

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~TestControlSchemaLadderTests"`
Expected: PASS — 6 tests.

- [ ] **Step 8: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.Provisioning Source/WPF/MyMoney.Data.Sqlite.TestTier Source/WPF/MyMoney.TestKit Source/WPF/MyMoney.Tests.Data Source/WPF/MyMoney.sln
git commit -m "$(cat <<'EOF'
feat(test): SQLite test tier - the schema ladder, DropAll, and derived table order

Slice 5, task 18. ResetSchema is unconditionally DropAll then ApplyTo, which is
what makes it idempotent by construction - and idempotency is what makes "nuke
and pave is over" true after every call, not just the first.

DropAll lives on the SHIPPED provisioner rather than in the never-shipped test
tier, because "the assembly isn't shipped" protects end users but not a
developer running a test suite against the wrong registry entry. Task 21 guards
it.

The drop and clear order is derived by introspection over PRAGMA
foreign_key_list and never hand-written: a hand-maintained table list has issue
#34's exact failure shape.

Spec: sections 1.5 S-1, 1.8, 2.6.1, 2.6.4, 2.6.8, 2.7.1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 19: the data ladder — `ClearAllData` / `ClearTables` / `ClearTable`, `Tables`, `RowCount`

**Files:**
- Modify: `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteMoneyStoreTestControl.cs`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlDataLadderTests.cs`

**Interfaces:**
- Consumes: `SqliteTableOrder` (Task 18), `ClearOptions`, `ClearResult`, `TableRef`.
- Produces: working `ClearAllData`, `ClearTables`, `ClearTable`, `Tables`, `RowCount`.

**The cross-engine trap this task pins (§2.6.4).** After `DELETE FROM Accounts`, the next `Id` a fresh insert receives is **not** the same on the two engines. SQLite's `INTEGER PRIMARY KEY` is the rowid and allocates `max(rowid)+1`, so an emptied table starts again at 1 by itself — *unless* the table is `AUTOINCREMENT`, in which case the high-water mark persists in `sqlite_sequence`. SQL Server's `IDENTITY` never rewinds without `DBCC CHECKIDENT`. A test asserting `Id == 1` after a clear would pass on one engine and fail on the other for reasons having nothing to do with what it tests. The contract assertion is: **after a clear with `ResetIdentity: true`, the next inserted row gets the same Id it would in a freshly paved database.**

**`TRUNCATE` is deliberately not used** — it is refused on any table an FK references, so it cannot be applied uniformly, and parity beats a constant factor here. `NOCHECK CONSTRAINT`'s SQLite analogue is likewise avoided beyond the in-transaction `defer_foreign_keys` safety net: a clear that disables constraint checking can leave a state the schema forbids.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlDataLadderTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using MyMoney.TestKit;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class TestControlDataLadderTests
    {
        private InMemorySqliteStore fixture;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.fixture = InMemorySqliteStore.Create();
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private IMoneyStoreTestControl Control => this.fixture.TestControl;

        private Account Save(string name)
        {
            var a = new Account(this.container)
            {
                Id = checked((int)this.fixture.Query.NextAccountId()),
                Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 0m
            };
            this.fixture.Store.SaveRoot(a);
            return a;
        }

        private void InsertOnlineAccount(long id)
        {
            using (var cmd = new SQLiteCommand(
                "INSERT INTO OnlineAccounts (Id, Name) VALUES (@id, 'bank');", this.fixture.Connection))
            {
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        [Test]
        public void Tables_AreTheDataTablesAndNeverTheLedger()
        {
            var names = this.Control.Tables.Select(t => t.Name).OrderBy(n => n).ToList();

            Assert.That(names, Is.EqualTo(new[] { "Accounts", "Categories", "OnlineAccounts" }));
        }

        [Test]
        public void TableRefsExposedByTheTestKit_AreExactlyTheIntrospectedDataTables()
        {
            // Spec section 2.6.6's anti-drift guard. Add a table without a TableRef, or leave a
            // TableRef behind after dropping a table, and this goes red.
            Assert.That(
                this.Control.Tables.Select(t => t.Name).OrderBy(n => n),
                Is.EqualTo(TableRef.All.Select(t => t.Name).OrderBy(n => n)));
        }

        [Test]
        public void ClearTable_EmptiesOneTableAndReportsTheCount()
        {
            this.Save("Checking");
            this.Save("Savings");
            this.InsertOnlineAccount(1);

            ClearResult result = this.Control.ClearTable(TableRef.Accounts);

            Assert.That(result.RowsDeleted[TableRef.Accounts], Is.EqualTo(2));
            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
            Assert.That(this.Control.RowCount(TableRef.OnlineAccounts), Is.EqualTo(1));
        }

        [Test]
        public void ClearTableOfTRoot_IsTheSameAsClearTableOfItsTableRef()
        {
            this.Save("Checking");

            this.Control.ClearTable<Account>();

            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
        }

        [Test]
        public void ClearAllData_EmptiesEveryDataTableButLeavesEverySchemaObject()
        {
            this.InsertOnlineAccount(1);
            this.Save("Checking");
            int versionBefore = this.Control.CurrentSchemaVersion;

            ClearResult result = this.Control.ClearAllData();

            Assert.That(result.RowsDeleted.Values.Sum(), Is.EqualTo(2));
            Assert.That(this.Control.Tables.All(t => this.Control.RowCount(t) == 0), Is.True);
            Assert.That(this.Control.CurrentSchemaVersion, Is.EqualTo(versionBefore),
                "ClearAllData must leave __SchemaHistory alone - spec 2.6.1's two ladders.");
            Assert.That(this.fixture.Provisioner.Verify().IsClean, Is.True);
        }

        [Test]
        public void ClearAllData_RespectsForeignKeyOrderWithFkEnforcementOn()
        {
            this.InsertOnlineAccount(1);
            using (var cmd = new SQLiteCommand(
                "UPDATE Accounts SET OnlineAccount = 1 WHERE Id = @id;", this.fixture.Connection))
            {
                Account a = this.Save("Checking");
                cmd.Parameters.AddWithValue("@id", a.Id);
                cmd.ExecuteNonQuery();
            }

            Assert.DoesNotThrow(() => this.Control.ClearAllData());
        }

        [Test]
        public void ClearAllData_IsIdempotent()
        {
            this.Save("Checking");
            this.Control.ClearAllData();

            ClearResult second = this.Control.ClearAllData();

            Assert.That(second.RowsDeleted.Values.Sum(), Is.EqualTo(0));
        }

        [Test]
        public void AfterAClearWithResetIdentity_TheNextRowGetsTheSameIdAsInAFreshlyPavedDatabase()
        {
            // Spec section 2.6.4's cross-engine trap, pinned. This is the only thing that keeps
            // the ResetIdentity option honest, and Plan B's SQL Server arm must satisfy it too.
            this.Save("Checking");
            this.Save("Savings");

            this.Control.ClearTable(TableRef.Accounts);

            Assert.That(this.fixture.Query.NextAccountId(), Is.EqualTo(1));

            using (InMemorySqliteStore pristine = InMemorySqliteStore.Create())
            {
                Assert.That(this.fixture.Query.NextAccountId(), Is.EqualTo(pristine.Query.NextAccountId()));
            }
        }

        [Test]
        public void ClearTables_ClearsOnlyTheNamedSubset()
        {
            this.InsertOnlineAccount(1);
            this.Save("Checking");

            this.Control.ClearTables(new List<TableRef> { TableRef.Accounts });

            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
            Assert.That(this.Control.RowCount(TableRef.OnlineAccounts), Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~TestControlDataLadderTests"`
Expected: FAIL — `System.NotImplementedException : Task 19.`

- [ ] **Step 3: Implement the data ladder**

Replace the five stubs in `SqliteMoneyStoreTestControl.cs`:
```csharp
        public IReadOnlyList<TableRef> Tables
        {
            get
            {
                var refs = new List<TableRef>();
                foreach (string name in SqliteTableOrder.DataTables(this.Connection))
                {
                    foreach (TableRef known in TableRef.All)
                    {
                        if (string.Equals(known.Name, name, StringComparison.Ordinal))
                        {
                            refs.Add(known);
                        }
                    }
                }

                return refs;
            }
        }

        public long RowCount(TableRef table)
        {
            string name = this.Validate(table);
            using (var cmd = new SQLiteCommand($"SELECT COUNT(*) FROM \"{name}\";", this.Connection))
            {
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        public ClearResult ClearTable(TableRef table, ClearOptions options = null) =>
            this.ClearTables(new[] { table }, options);

        public ClearResult ClearAllData(ClearOptions options = null) =>
            this.ClearTables(this.Tables, options);

        /// <summary>
        /// DELETE FROM per table, in derived FK order, inside ONE transaction. Not TRUNCATE: it is
        /// refused on any table an FK references, so it cannot be applied uniformly, and parity
        /// beats a constant factor here (spec section 2.6.4). No constraint-disabling beyond the
        /// in-transaction defer_foreign_keys safety net - a clear that disables constraint
        /// checking can leave a state the schema forbids.
        ///
        /// Idempotent: a DELETE with no WHERE against an already-empty table deletes zero rows and
        /// succeeds; nothing conditions success on the table being non-empty first (spec 2.6.8).
        /// </summary>
        public ClearResult ClearTables(IReadOnlyCollection<TableRef> tables, ClearOptions options = null)
        {
            options ??= ClearOptions.Default;

            var requested = new HashSet<string>(StringComparer.Ordinal);
            foreach (TableRef table in tables)
            {
                requested.Add(this.Validate(table));
            }

            var deleted = new Dictionary<TableRef, long>();

            using (SQLiteTransaction tx = this.Connection.BeginTransaction(deferredLock: false))
            {
                try
                {
                    using (var pragma = new SQLiteCommand("PRAGMA defer_foreign_keys = ON;", this.Connection, tx))
                    {
                        pragma.ExecuteNonQuery();
                    }

                    foreach (string name in SqliteTableOrder.ForDelete(this.Connection))
                    {
                        if (!requested.Contains(name))
                        {
                            continue;
                        }

                        long rows;
                        using (var cmd = new SQLiteCommand($"DELETE FROM \"{name}\";", this.Connection, tx))
                        {
                            rows = cmd.ExecuteNonQuery();
                        }

                        if (options.ResetIdentity)
                        {
                            this.ResetIdentity(name, tx);
                        }

                        foreach (TableRef known in TableRef.All)
                        {
                            if (string.Equals(known.Name, name, StringComparison.Ordinal))
                            {
                                deleted[known] = rows;
                            }
                        }
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    throw new StoreTestControlException("Clearing tables failed and was rolled back.", ex);
                }
            }

            if (options.VerifyForeignKeysAfter)
            {
                this.VerifyForeignKeys();
            }

            return new ClearResult(deleted);
        }

        /// <summary>
        /// SQLite's INTEGER PRIMARY KEY is the rowid and allocates max(rowid)+1, so an emptied
        /// table starts again at 1 by itself - UNLESS the table is AUTOINCREMENT, in which case
        /// the high-water mark persists in sqlite_sequence and must be deleted explicitly. SQL
        /// Server's IDENTITY never rewinds without DBCC CHECKIDENT, which is why this option
        /// exists at all. Spec section 2.6.4.
        ///
        /// Reseeding a counter that is already at its reset value is itself a no-op, so this does
        /// not break ClearTables' idempotency.
        /// </summary>
        private void ResetIdentity(string table, SQLiteTransaction tx)
        {
            using (var exists = new SQLiteCommand(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'sqlite_sequence';",
                this.Connection, tx))
            {
                if (Convert.ToInt64(exists.ExecuteScalar()) == 0)
                {
                    return;
                }
            }

            using (var cmd = new SQLiteCommand("DELETE FROM sqlite_sequence WHERE name = @t;", this.Connection, tx))
            {
                cmd.Parameters.AddWithValue("@t", table);
                cmd.ExecuteNonQuery();
            }
        }

        private void VerifyForeignKeys()
        {
            using (var cmd = new SQLiteCommand("PRAGMA foreign_key_check;", this.Connection))
            using (SQLiteDataReader reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    throw new StoreTestControlException(
                        $"foreign_key_check reported a violation in table '{reader.GetValue(0)}' after the clear.");
                }
            }
        }

        /// <summary>
        /// An identifier cannot be parameterized, so it is validated against the LIVE introspected
        /// table set before interpolation and rejected if it contains a double quote - the
        /// identifier rule under R-CRUD-1. A TableRef cannot be mistyped in the first place, so
        /// this catches the case where the schema no longer has the table.
        /// </summary>
        private string Validate(TableRef table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (table.Name.IndexOf('"') >= 0)
            {
                throw new StoreTestControlException($"Refusing to build SQL for the identifier {table.Name}.");
            }

            foreach (string name in SqliteTableOrder.DataTables(this.Connection))
            {
                if (string.Equals(name, table.Name, StringComparison.Ordinal))
                {
                    return name;
                }
            }

            throw new StoreTestControlException(
                $"'{table.Name}' is not a data table in this database at schema version "
                + $"{this.CurrentSchemaVersion}.");
        }
```

Add `using MyMoney.TestKit.Contracts;`, `using System.Linq;` as needed.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~TestControlDataLadderTests"`
Expected: PASS — 9 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.TestTier Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlDataLadderTests.cs
git commit -m "$(cat <<'EOF'
feat(test): the test tier's data ladder, with the id-reseed parity assertion

Slice 5, task 19. ClearAllData decomposes into ClearTables decomposes into
ClearTable - one executor with a narrowing target - and every one of them leaves
every schema object, including __SchemaHistory, untouched. The table set and
delete order are introspected, never hand-written.

Pins the cross-engine trap: after a clear with ResetIdentity, the next inserted
row gets the same Id it would in a freshly paved database. A test asserting
Id == 1 after a clear would otherwise pass on one engine and fail on the other
for reasons having nothing to do with what it tests.

Also adds the TableRef anti-drift assertion: the refs the TestKit exposes are
exactly the introspected data tables.

Spec: sections 2.6.1, 2.6.2, 2.6.4, 2.6.6, 2.6.8.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 20: the row ladder — `CaptureRow`, `DeleteRow`, `RestoreRow`, and the `RemoveRow` scope

**Files:**
- Modify: `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteMoneyStoreTestControl.cs`
- Create: `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteRowScope.cs`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlRowLadderTests.cs`

**Interfaces:**
- Consumes: `RowSnapshot`, `IRowScope`, `StoreTestControlException`.
- Produces: working `CaptureRow`, `TryCaptureRow`, `DeleteRow`, `RestoreRow`, `RemoveRow`; `sealed class SqliteRowScope : IRowScope`.

**The three decisions §2.6.5 makes, all of which the tests below pin:**

1. **`DeleteRow` does not cascade.** If another table's FK references the row, the delete fails and rolls back as a `StoreTestControlException`. Cascading would make `RestoreRow` a *lie*: it restores the one row it captured, while the rows the cascade took are gone for good, leaving a state that is neither before nor after. **Restore must be exactly the inverse of remove, or it is not a restore.**
2. **`RestoreRow` re-inserts verbatim** — same `Id`, same `Version`, same every column. Not "insert a new row with the same values": a restored row with a fresh Id or a bumped version would break every reference to it and make the version-conflict tests unreproducible.
3. **`Dispose` restores inside its own transaction and throws on failure** rather than swallowing. A silently-failed restore leaks state into every subsequent test in the fixture — the exact failure mode `AppCrashGuard` was written to stop tolerating elsewhere in this project (CLAUDE.md records why that check is deliberately not wrapped in a try/catch).

Plus §2.6.8's one explicit tightening: **`DeleteRow(table, id)` succeeds as a no-op when `id` is already absent**, so a reset call site can call it unconditionally and stay idempotent. `RestoreRow` is deliberately *not* idempotent — a second call is a primary-key violation by design, because it is the single-shot inverse half of a capture-then-delete pair.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlRowLadderTests.cs`:
```csharp
using System;
using System.Data.SQLite;
using System.Linq;
using MyMoney.TestKit;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class TestControlRowLadderTests
    {
        private InMemorySqliteStore fixture;
        private Accounts container;

        [SetUp]
        public void SetUp()
        {
            this.fixture = InMemorySqliteStore.Create();
            this.container = new Accounts((PersistentObject)null);
        }

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private IMoneyStoreTestControl Control => this.fixture.TestControl;

        private Account Save(string name)
        {
            var a = new Account(this.container)
            {
                Id = checked((int)this.fixture.Query.NextAccountId()),
                Name = name, Type = AccountType.Checking, Currency = "USD", OpeningBalance = 12.34m
            };
            this.fixture.Store.SaveRoot(a);
            return a;
        }

        [Test]
        public void CaptureRow_ReturnsEveryColumnIncludingIdAndVersion()
        {
            Account a = this.Save("Checking");

            RowSnapshot snapshot = this.Control.CaptureRow(TableRef.Accounts, a.Id);

            Assert.That(snapshot.Id, Is.EqualTo(a.Id));
            Assert.That(snapshot.Values["Name"], Is.EqualTo("Checking"));
            Assert.That(Convert.ToInt64(snapshot.Values["Version"]), Is.EqualTo(1));
            Assert.That(snapshot.Values.ContainsKey("OpeningBalance"), Is.True);
        }

        [Test]
        public void CaptureRow_OnAnAbsentRow_Throws_AndTryCaptureRow_ReturnsNull()
        {
            Assert.Throws<StoreTestControlException>(() => this.Control.CaptureRow(TableRef.Accounts, 404));
            Assert.That(this.Control.TryCaptureRow(TableRef.Accounts, 404), Is.Null);
        }

        [Test]
        public void DeleteRow_RemovesTheRowWithNoVersionCheck()
        {
            Account a = this.Save("Checking");

            // No version is supplied and none is consulted - the fixture does not have to know
            // what version a row is at in order to remove it. Spec section 2.6.3.
            this.Control.DeleteRow(TableRef.Accounts, a.Id);

            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(0));
        }

        [Test]
        public void DeleteRow_OnAnAbsentRow_IsASilentNoOp()
        {
            // Spec section 2.6.8's one explicit tightening: a reset call site can call DeleteRow
            // unconditionally, without first checking existence, and stay idempotent.
            Assert.DoesNotThrow(() => this.Control.DeleteRow(TableRef.Accounts, 404));
            Assert.DoesNotThrow(() => this.Control.DeleteRow(TableRef.Accounts, 404));
        }

        [Test]
        public void DeleteRow_DoesNotCascade_AndRollsBackOnAForeignKeyViolation()
        {
            using (var cmd = new SQLiteCommand(
                "INSERT INTO OnlineAccounts (Id, Name) VALUES (1, 'bank');", this.fixture.Connection))
            {
                cmd.ExecuteNonQuery();
            }

            Account a = this.Save("Checking");
            using (var cmd = new SQLiteCommand(
                "UPDATE Accounts SET OnlineAccount = 1 WHERE Id = @id;", this.fixture.Connection))
            {
                cmd.Parameters.AddWithValue("@id", a.Id);
                cmd.ExecuteNonQuery();
            }

            Assert.Throws<StoreTestControlException>(() => this.Control.DeleteRow(TableRef.OnlineAccounts, 1));
            Assert.That(this.Control.RowCount(TableRef.OnlineAccounts), Is.EqualTo(1));
            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(1));
        }

        [Test]
        public void RestoreRow_ReInsertsVerbatimIncludingIdAndVersion()
        {
            Account a = this.Save("Checking");
            a.Name = "Renamed";
            this.fixture.Store.SaveRoot(a);          // version is now 2
            RowSnapshot snapshot = this.Control.CaptureRow(TableRef.Accounts, a.Id);
            this.Control.DeleteRow(TableRef.Accounts, a.Id);

            this.Control.RestoreRow(snapshot);

            Account restored = this.fixture.Store.LoadAccounts().Single();
            Assert.That(restored.Id, Is.EqualTo(a.Id));
            Assert.That(restored.Name, Is.EqualTo("Renamed"));
            Assert.That(restored.RowVersion, Is.EqualTo(2),
                "A restored row that came back with a bumped version would make the "
                + "version-conflict tests unreproducible - spec 2.6.5 point 2.");
        }

        [Test]
        public void RestoreRow_Twice_Throws_BecauseItIsSingleShotByDesign()
        {
            Account a = this.Save("Checking");
            RowSnapshot snapshot = this.Control.CaptureRow(TableRef.Accounts, a.Id);
            this.Control.DeleteRow(TableRef.Accounts, a.Id);
            this.Control.RestoreRow(snapshot);

            Assert.Throws<StoreTestControlException>(() => this.Control.RestoreRow(snapshot));
        }

        [Test]
        public void RemoveRow_HidesTheRowForTheScopeAndPutsItBackByteForByte()
        {
            Account a = this.Save("Checking");
            this.Save("Savings");

            using (IRowScope scope = this.Control.RemoveRow(TableRef.Accounts, a.Id))
            {
                Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All).Select(r => r.Name),
                    Is.EqualTo(new[] { "Savings" }));
                Assert.That(scope.Removed.Values["Name"], Is.EqualTo("Checking"));
            }

            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All).Select(r => r.Name),
                Is.EqualTo(new[] { "Checking", "Savings" }));
            Assert.That(this.fixture.Store.LoadAccounts().Single(x => x.Id == a.Id).RowVersion, Is.EqualTo(1));
        }

        [Test]
        public void RemoveRow_DisposedTwice_RestoresOnlyOnce()
        {
            Account a = this.Save("Checking");
            IRowScope scope = this.Control.RemoveRow(TableRef.Accounts, a.Id);
            scope.Dispose();

            Assert.DoesNotThrow(() => scope.Dispose());
            Assert.That(this.Control.RowCount(TableRef.Accounts), Is.EqualTo(1));
        }

        [Test]
        public void RemoveRow_WhenTheRestoreCannotSucceed_Throws_RatherThanSwallowing()
        {
            // A silently-failed restore leaks state into every subsequent test in the fixture -
            // the exact failure mode AppCrashGuard was written to stop tolerating elsewhere in
            // this project. Spec section 2.6.5 point 3.
            Account a = this.Save("Checking");
            IRowScope scope = this.Control.RemoveRow(TableRef.Accounts, a.Id);

            // Someone re-created the row while it was "removed", so restoring it collides.
            this.fixture.Store.SaveRoot(new Account(this.container)
            {
                Id = a.Id, Name = "Impostor", Type = AccountType.Cash, OpeningBalance = 0m
            });

            Assert.Throws<StoreTestControlException>(() => scope.Dispose());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~TestControlRowLadderTests"`
Expected: FAIL — `System.NotImplementedException : Task 20.`

- [ ] **Step 3: Write `SqliteRowScope`**

Create `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteRowScope.cs`:
```csharp
using MyMoney.TestKit.Contracts;

namespace Walkabout.Data.Sqlite.TestTier
{
    /// <summary>
    /// Capture + delete on construction; restore on Dispose, inside its own transaction, THROWING
    /// if the restore fails rather than swallowing - spec section 2.6.5 point 3.
    /// </summary>
    internal sealed class SqliteRowScope : IRowScope
    {
        private readonly IMoneyStoreTestControl control;
        private bool restored;

        internal SqliteRowScope(IMoneyStoreTestControl control, RowSnapshot removed)
        {
            this.control = control;
            this.Removed = removed;
        }

        public RowSnapshot Removed { get; }

        public void Dispose()
        {
            if (this.restored)
            {
                return;
            }

            this.restored = true;
            this.control.RestoreRow(this.Removed);
        }
    }
}
```

- [ ] **Step 4: Implement the row ladder**

Replace the five stubs in `SqliteMoneyStoreTestControl.cs`:
```csharp
        public RowSnapshot CaptureRow(TableRef table, long id)
        {
            RowSnapshot snapshot = this.TryCaptureRow(table, id);
            if (snapshot == null)
            {
                throw new StoreTestControlException($"{table.Name} has no row with Id {id}.");
            }

            return snapshot;
        }

        public RowSnapshot TryCaptureRow(TableRef table, long id)
        {
            string name = this.Validate(table);

            // Column list built from PRAGMA introspection rather than SELECT *, so the snapshot's
            // key order and content are explicit and a schema change is visible here.
            IReadOnlyList<string> columns = this.ColumnsOf(name);
            var values = new Dictionary<string, object>(StringComparer.Ordinal);

            string columnList = string.Join(", ", columns.Select(c => $"\"{c}\""));
            using (var cmd = new SQLiteCommand(
                $"SELECT {columnList} FROM \"{name}\" WHERE Id = @id;", this.Connection))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    for (int i = 0; i < columns.Count; i++)
                    {
                        values[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                }
            }

            return new RowSnapshot(table, id, values);
        }

        /// <summary>
        /// No version check, no cascade. If another table's FK references the row, the delete
        /// fails and rolls back - cascading would make RestoreRow a lie, restoring the one row it
        /// captured while the rows the cascade took are gone for good. Restore must be exactly the
        /// inverse of remove, or it is not a restore. Spec section 2.6.5 point 1.
        ///
        /// Succeeds as a no-op when the row is already absent (spec section 2.6.8).
        /// </summary>
        public void DeleteRow(TableRef table, long id)
        {
            string name = this.Validate(table);

            using (SQLiteTransaction tx = this.Connection.BeginTransaction(deferredLock: false))
            {
                try
                {
                    using (var cmd = new SQLiteCommand($"DELETE FROM \"{name}\" WHERE Id = @id;", this.Connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    throw new StoreTestControlException(
                        $"Deleting {name} row {id} failed and was rolled back. DeleteRow does not "
                        + "cascade: use CaptureRow/DeleteRow on the dependents first if that is "
                        + "genuinely what the test wants.",
                        ex);
                }
            }
        }

        /// <summary>
        /// Re-inserts VERBATIM: same Id, same Version, same every column. Not idempotent, by
        /// design - a second call is a primary-key violation, because this is the single-shot
        /// inverse half of a capture-then-delete pair. Spec sections 2.6.5 point 2 and 2.6.8.
        /// </summary>
        public void RestoreRow(RowSnapshot row)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            string name = this.Validate(row.Table);
            var columns = row.Values.Keys.ToList();
            string columnList = string.Join(", ", columns.Select(c => $"\"{c}\""));
            string parameterList = string.Join(", ", columns.Select((c, i) => "@p" + i));

            using (SQLiteTransaction tx = this.Connection.BeginTransaction(deferredLock: false))
            {
                try
                {
                    using (var cmd = new SQLiteCommand(
                        $"INSERT INTO \"{name}\" ({columnList}) VALUES ({parameterList});", this.Connection, tx))
                    {
                        for (int i = 0; i < columns.Count; i++)
                        {
                            cmd.Parameters.AddWithValue("@p" + i, row.Values[columns[i]] ?? DBNull.Value);
                        }

                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.Rollback();
                    throw new StoreTestControlException(
                        $"Restoring {name} row {row.Id} failed. A silently-failed restore would leak "
                        + "state into every subsequent test in this fixture, so this throws.",
                        ex);
                }
            }
        }

        public IRowScope RemoveRow(TableRef table, long id)
        {
            RowSnapshot snapshot = this.CaptureRow(table, id);
            this.DeleteRow(table, id);
            return new SqliteRowScope(this, snapshot);
        }

        private IReadOnlyList<string> ColumnsOf(string table)
        {
            var columns = new List<string>();
            using (var cmd = new SQLiteCommand("SELECT name FROM pragma_table_info(@t) ORDER BY cid;", this.Connection))
            {
                cmd.Parameters.AddWithValue("@t", table);
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(0));
                    }
                }
            }

            return columns;
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj`
Expected: PASS — 90 tests.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Data.Sqlite.TestTier Source/WPF/MyMoney.Tests.Data/Sqlite/TestControlRowLadderTests.cs
git commit -m "$(cat <<'EOF'
feat(test): the test tier's row ladder and the using-scoped RemoveRow

Slice 5, task 20. DeleteRow does not cascade - cascading would make RestoreRow a
lie, restoring the one row it captured while the rows the cascade took are gone
for good. RestoreRow re-inserts verbatim, same Id and same Version, so a
restored row does not break references or dodge the version-conflict tests it
exists to support. Dispose restores in its own transaction and throws on
failure rather than swallowing, because a silently-failed restore leaks state
into every subsequent test in the fixture.

DeleteRow succeeds as a no-op on an absent row, so a reset call site can call it
unconditionally and stay idempotent; RestoreRow is deliberately single-shot.

Spec: sections 2.6.2, 2.6.3, 2.6.5, 2.6.8.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 21 (slice 5b): the shared `TestDatabaseGuard`

**Files:**
- Create: `Source/WPF/MyMoney.Business/Store/TestDatabaseGuard.cs`
- Modify: `Source/WPF/MyMoney.Data.Sqlite.Provisioning/SqliteMoneyStoreProvisioner.cs` (`DropAll`, `Delete`)
- Modify: `Source/WPF/MyMoney.Data.Sqlite.TestTier/SqliteTestControlFactory.cs`
- Modify: `Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
- Test: `Source/WPF/MyMoney.Tests.Data/Sqlite/TestDatabaseGuardTests.cs`
- Test: `Source/WPF/MyMoney.Tests.Architecture/TestDatabaseFlagTests.cs`

**Interfaces:**
- Consumes: `StoreIdentity` (Task 1).
- Produces: `static class TestDatabaseGuard` with `static void Require(StoreIdentity identity, string capability)`; `sealed class TestDatabaseRequiredException : Exception` with `string DatabaseDisplayName`, `string Capability`.

**Scope, in the spec's own corrected words.** The guard covers **"operations that must only ever touch a test database"**, not merely *destructive* ones — §1.8's bullet as widened by revision 8, because fabricating data indistinguishable from real data is the second kind. Plan A wires the two call sites it has: the provisioner's destructive operations, and the point an `IMoneyStoreTestControl` is **obtained**. The third call site, `SampleDataService.Populate`, is unscheduled work that reuses **this same static method and this same exception type** — that reuse is the whole point, which is why Task 21 also adds the IL scan that keeps a second, hand-rolled check from appearing.

**The guard goes at acquisition, once (§2.6.4), not in every method.** `ResetSchema` reads as dangerous and gets respect; `ClearTable(TableRef.Payees)` reads as housekeeping — and the twenty-first method is the one that forgets.

**What this cannot claim (§6.9, stated so nobody overclaims later).** It is a runtime check in a shipped assembly, the weakest of the three protections in this design. It stops the *feature*, not the *data*: a caller with the database open can still write ordinary rows through `SaveRoots`. What it can claim is that it is always compiled in, that it is one code path rather than two that drift, that it runs before anything is written, and that every failure mode of its input resolves to "refuse."

- [ ] **Step 1: Write the failing tests**

Create `Source/WPF/MyMoney.Tests.Data/Sqlite/TestDatabaseGuardTests.cs`:
```csharp
using System;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite.Provisioning;
using Walkabout.Data.Sqlite.TestTier;

namespace Walkabout.Tests.Data.Sqlite
{
    [TestFixture]
    public class TestDatabaseGuardTests
    {
        private static SqliteMoneyStoreProvisioner Open(bool isTestDatabase) =>
            SqliteMoneyStoreProvisioner.Open(new SqliteProvisioningOptions(
                "My Real Money", SqliteConnectionFactory.InMemoryDataSource, isTestDatabase));

        [Test]
        public void DropAll_OnANonTestDatabase_Refuses()
        {
            using (var p = Open(false))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                var ex = Assert.Throws<TestDatabaseRequiredException>(() => p.DropAll());

                Assert.That(ex.DatabaseDisplayName, Is.EqualTo("My Real Money"));
                Assert.That(ex.Capability, Is.Not.Empty);
                Assert.That(p.CurrentVersion(), Is.EqualTo(SchemaStepCatalog.LatestVersion),
                    "A refusal must leave the schema untouched.");
            }
        }

        [Test]
        public void Delete_OnANonTestDatabase_Refuses()
        {
            using (var p = Open(false))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.Throws<TestDatabaseRequiredException>(() => p.Delete());
            }
        }

        [Test]
        public void AcquiringTestControl_OnANonTestDatabase_Refuses()
        {
            // Spec 2.6.4: the check is at the point the instance is HANDED OUT, once, not repeated
            // in twenty methods where the twenty-first is the one that forgets.
            using (var p = Open(false))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.Throws<TestDatabaseRequiredException>(() => SqliteTestControlFactory.Acquire(p));
            }
        }

        [Test]
        public void OnATestDatabase_EverythingIsAllowed()
        {
            using (var p = Open(true))
            {
                p.ApplyTo(SchemaStepCatalog.LatestVersion);

                Assert.DoesNotThrow(() => SqliteTestControlFactory.Acquire(p));
                Assert.DoesNotThrow(() => p.DropAll());
            }
        }

        [Test]
        public void TheFlagRoundTripsOntoStoreIdentity()
        {
            using (var p = Open(false))
            {
                Assert.That(p.Identity.IsTestDatabase, Is.False);
            }

            using (var p = Open(true))
            {
                Assert.That(p.Identity.IsTestDatabase, Is.True);
            }
        }

        [Test]
        public void TheRefusalMessageIsPlainLanguage()
        {
            // Spec section 7's UI rule: failures reach the user as plain language through the
            // callback port, never as a type name or a raw engine string.
            var identity = new StoreIdentity("My Real Money", DbFlavor.Sqlite, false, 4);

            var ex = Assert.Throws<TestDatabaseRequiredException>(
                () => TestDatabaseGuard.Require(identity, "Add sample data"));

            Assert.That(ex.Message, Does.Contain("My Real Money"));
            Assert.That(ex.Message, Does.Contain("Add sample data"));
            Assert.That(ex.Message, Does.Contain("test database"));
        }
    }
}
```

Create `Source/WPF/MyMoney.Tests.Architecture/TestDatabaseFlagTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0, and heavier than the GetReferencedAssemblies checks beside it - an IL scan, called
    /// out as such rather than glossed (spec section 1.9.7). It is worth the weight because the
    /// failure it prevents is the single way section 1.9's "it's the same mechanism as 1.8's, not
    /// a parallel one" claim stops being true: a second, hand-rolled if (entry.TestDatabase) check
    /// appearing somewhere and drifting from the shared one.
    /// </summary>
    [TestFixture]
    public class TestDatabaseFlagTests
    {
        /// <summary>
        /// Types allowed to read StoreIdentity.IsTestDatabase in a production assembly. The UI's
        /// CanExecute handler joins this list when the sample-data feature is scheduled; it is a
        /// read with no write behind it (greying a command), deliberate and not a loophole.
        /// </summary>
        private static readonly string[] AllowedReaders = { "TestDatabaseGuard" };

        private static readonly string[] ProductionAssemblies =
        {
            "MyMoney.Business", "MyMoney.Data.Sqlite", "MyMoney.Data.Sqlite.Provisioning"
        };

        [Test]
        public void TestDatabaseFlag_IsReadOnlyByTheSharedGuard()
        {
            foreach (string assemblyName in ProductionAssemblies)
            {
                string path = AssemblyPath(assemblyName);
                foreach (string caller in CallersOf(path, "get_IsTestDatabase"))
                {
                    Assert.That(
                        AllowedReaders.Any(a => caller.Contains(a, StringComparison.Ordinal)),
                        Is.True,
                        $"{assemblyName}'s {caller} reads StoreIdentity.IsTestDatabase directly. Call "
                        + "TestDatabaseGuard.Require instead - a second, hand-rolled check is exactly "
                        + "what makes 'it's the same mechanism' stop being true.");
                }
            }
        }

        private static string AssemblyPath(string name) =>
            System.IO.Path.Combine(TestContext.CurrentContext.TestDirectory, name + ".dll");

        /// <summary>
        /// Names of methods whose IL contains a call to <paramref name="calleeName"/>. Uses
        /// System.Reflection.Metadata so no assembly has to be loaded for execution.
        /// </summary>
        private static IEnumerable<string> CallersOf(string assemblyPath, string calleeName)
        {
            using var stream = System.IO.File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            MetadataReader md = pe.GetMetadataReader();

            foreach (MethodDefinitionHandle handle in md.MethodDefinitions)
            {
                MethodDefinition method = md.GetMethodDefinition(handle);
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }

                byte[] il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
                if (il == null)
                {
                    continue;
                }

                for (int i = 0; i + 4 < il.Length; i++)
                {
                    // 0x28 call, 0x6F callvirt - followed by a 4-byte metadata token.
                    if (il[i] != 0x28 && il[i] != 0x6F)
                    {
                        continue;
                    }

                    int token = BitConverter.ToInt32(il, i + 1);
                    string callee = ResolveMemberName(md, token);
                    if (callee != null && callee.EndsWith(calleeName, StringComparison.Ordinal))
                    {
                        string declaring = md.GetString(md.GetTypeDefinition(method.GetDeclaringType()).Name);
                        yield return declaring + "." + md.GetString(method.Name);
                        break;
                    }
                }
            }
        }

        private static string ResolveMemberName(MetadataReader md, int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MemberReference => md.GetString(md.GetMemberReference((MemberReferenceHandle)handle).Name),
                HandleKind.MethodDefinition => md.GetString(md.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
                _ => null
            };
        }
    }
}
```

Wire the references the architecture project needs:
```bash
dotnet add Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj reference Source/WPF/MyMoney.Data.Sqlite/MyMoney.Data.Sqlite.csproj
dotnet add Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj reference Source/WPF/MyMoney.Data.Sqlite.Provisioning/MyMoney.Data.Sqlite.Provisioning.csproj
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj --filter "FullyQualifiedName~TestDatabaseGuardTests"`
Expected: FAIL to compile — `CS0103: The name 'TestDatabaseGuard' does not exist in the current context`.

- [ ] **Step 3: Write `TestDatabaseGuard`**

Create `Source/WPF/MyMoney.Business/Store/TestDatabaseGuard.cs`:
```csharp
using System;

namespace Walkabout.Data
{
    /// <summary>
    /// ONE guard for every capability that must only ever touch a test database.
    ///
    /// Scope, in the spec's corrected words (section 1.8 as widened by revision 8): this is not
    /// only about DESTRUCTIVE operations. There are two kinds - operations that destroy data, and
    /// operations that fabricate data indistinguishable from real data. Sample-data generation is
    /// squarely the second and is not destructive at all, so a reader who stops at the word
    /// "destructive" would wrongly conclude it is out of scope.
    ///
    /// ALWAYS COMPILED IN, in every configuration, and never in a *.TestKit* assembly: "the
    /// assembly isn't shipped" protects end users but does not protect a developer running a test
    /// suite against the wrong registry entry, and the sample-data feature is deliberately
    /// reachable by a customer in a release build.
    ///
    /// What this cannot claim (spec section 6.9, stated so nobody overclaims later): it is a
    /// runtime check in a shipped assembly, the weakest of this design's three protections. It
    /// stops the FEATURE, not the DATA - a caller with the database open can still write ordinary
    /// rows through SaveRoots, and a caller who assembles a sample set by hand has simply left the
    /// feature. What it can claim: always compiled in, one code path rather than two that drift,
    /// runs before anything is written so a refusal leaves nothing partial, and every failure mode
    /// of its input resolves to "refuse".
    /// </summary>
    public static class TestDatabaseGuard
    {
        public static void Require(StoreIdentity identity, string capability)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (!identity.IsTestDatabase)
            {
                throw new TestDatabaseRequiredException(identity.DisplayName, capability);
            }
        }
    }

    /// <summary>
    /// Surfaced through IBusinessLayerUiCallback as plain language, never as a type name or a raw
    /// engine string - spec section 7's UI rule.
    /// </summary>
    public sealed class TestDatabaseRequiredException : Exception
    {
        public TestDatabaseRequiredException(string databaseDisplayName, string capability)
            : base($"'{databaseDisplayName}' is not marked as a test database, so '{capability}' is "
                   + "not allowed on it. Create a new database with 'Test database' checked if you "
                   + "want to try this out.")
        {
            this.DatabaseDisplayName = databaseDisplayName;
            this.Capability = capability;
        }

        public string DatabaseDisplayName { get; }

        public string Capability { get; }
    }
}
```

- [ ] **Step 4: Wire the two call sites**

In `SqliteMoneyStoreProvisioner.cs`, make these the **first statements** of `DropAll` and `Delete` — first, so a refusal leaves nothing partially done:
```csharp
            TestDatabaseGuard.Require(this.Identity, "Drop the whole schema");
```
```csharp
            TestDatabaseGuard.Require(this.Identity, "Delete the database");
```

In `SqliteTestControlFactory.Acquire`, replace the comment with the real call:
```csharp
            TestDatabaseGuard.Require(provisioner.Identity, "Test control (schema, data and row reset)");
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Data/MyMoney.Tests.Data.csproj` then `dotnet test Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
Expected: PASS — 96 and 9 tests respectively.

> If `DropAll_OnANonTestDatabase_Refuses` passes but `OnATestDatabase_EverythingIsAllowed` now fails elsewhere, check `InMemorySqliteStore.Create`'s default: it passes `isTestDatabase: true`, which is what every existing fixture relies on.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Business/Store/TestDatabaseGuard.cs Source/WPF/MyMoney.Data.Sqlite.Provisioning Source/WPF/MyMoney.Data.Sqlite.TestTier Source/WPF/MyMoney.Tests.Data Source/WPF/MyMoney.Tests.Architecture
git commit -m "$(cat <<'EOF'
feat(data): the shared TestDatabaseGuard, built once as one mechanism

Slice 5b, task 21. Wired into the provisioner's destructive operations (as their
FIRST statement, so a refusal leaves nothing partial) and into the point an
IMoneyStoreTestControl is obtained - once, at acquisition, not repeated in
twenty methods where the twenty-first is the one that forgets.

The guard covers "operations that must only ever touch a test database", not
merely destructive ones: fabricating data indistinguishable from real data is
the second kind, which is why the future sample-data feature reuses this exact
static method and exception type rather than a parallel check. A Tier-0 IL scan
asserts the guard is the only production reader of the flag, which is the one
thing that keeps "it's the same mechanism" true a year from now.

The honest limit is stated in the type's own doc comment: this is a runtime
check in a shipped assembly, it stops the feature and not the data, and it is
the weakest of the design's three protections.

Spec: sections 1.8, 1.9.2, 1.9.3, 1.9.7, 2.6.4, 6.9, 7.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 22 (slice 6): the rest of the Tier-0 band, and the build-time enforcement

**Files:**
- Create: `Source/WPF/Directory.Build.targets`
- Modify: `Source/WPF/MyMoney/MyMoney.csproj`, `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`, `Source/WPF/MyMoney.Data/MyMoney.Data.csproj` (one property line each)
- Modify: `Source/WPF/MyMoney.Data.Sqlite/MyMoney.Data.Sqlite.csproj`, `Source/WPF/MyMoney.Data.Sqlite.Provisioning/MyMoney.Data.Sqlite.Provisioning.csproj` (one property line each)
- Modify: `Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj`
- Test: `Source/WPF/MyMoney.Tests.Architecture/AssemblyBoundaryTests.cs`

**Interfaces:**
- Consumes: every assembly built so far.
- Produces: no new runtime types. Produces the MSBuild property `IsProductionAssembly` and the target `FailOnTestTierReference`.

**The Tier-0 roster after this task** — §2.5's eleven, all accounted for:

| Test | Lands in |
|---|---|
| `ProjectionTypes_DoNotImplementIAggregateRoot` | Task 2 |
| `StoreWriteMethods_AcceptOnlyAggregateRoots` | Task 2 |
| `StoreWriteSurface_IsExactlyTheFourNamedMethods` | Task 2 |
| `TestDatabaseFlag_IsReadOnlyByTheSharedGuard` | Task 21 |
| `ProductionAssemblies_DoNotReferenceTestKit` | **this task** |
| `ProductionAssemblies_DeclareNoInternalsVisibleTo` | **this task** |
| `SqliteStoreAssembly_ContainsNoTestControlImplementation` | **this task** |
| `ProductionReferenceClosure_ContainsNoSqlServerEngineAssembly` | **this task** (see D-4 for the deviation) |
| `MyMoneyBusiness_HasNoWpfAssemblyReference` | **this task** (carried forward) |
| `MyMoneyData_HasNoWpfAssemblyReference` | **this task** (per-engine now) |
| `TestsBusiness_DoesNotReferenceTheWpfUiProject` | **this task** |

Plus two more this plan adds: `SqliteStoreAssembly_DoesNotReferenceTheProvisioningAssembly` (enforcing §1's accepted duplication) and `SqliteStoreAssembly_ContainsNoDestructiveSql`.

The Tier-2 `Schema_OwnsNoInsteadOfTrigger` check §5's slice 6 also names is **already delivered**, in Task 17's contract suite — it is per engine, so it belongs there, exactly as §5 says.

**Why the build-time layer matters more than the test layer (§6.3).** *"I just need `BasicsFixtureBuilder` from TestKit in this one production class"* is a real, recurring shortcut. A test-run-time failure is easy to ignore mid-task; a `dotnet build` failure is not. **Both**, not either.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Architecture/AssemblyBoundaryTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using MyMoney.TestKit.Contracts;
using NUnit.Framework;
using Walkabout.Data;
using Walkabout.Data.Sqlite;
using Walkabout.Data.Sqlite.Provisioning;

namespace Walkabout.Tests.Architecture
{
    /// <summary>
    /// Tier 0, layer 3 of the four-layer enforcement in spec section 2.5. Layer 1 is "no
    /// ProjectReference" (necessary, insufficient, one merge away from untrue), layer 2 is the
    /// Directory.Build.targets failure this task also adds, layer 4 is "production cannot name the
    /// type". These run in under a second and on every build.
    /// </summary>
    [TestFixture]
    public class AssemblyBoundaryTests
    {
        private static readonly string[] WpfAssemblyNames =
        {
            "PresentationFramework", "PresentationCore", "WindowsBase"
        };

        private static readonly Assembly[] ProductionAssemblies =
        {
            typeof(IMoneyStore).Assembly,                    // MyMoney.Business
            typeof(SqliteMoneyStore).Assembly,               // MyMoney.Data.Sqlite
            typeof(SqliteMoneyStoreProvisioner).Assembly,    // MyMoney.Data.Sqlite.Provisioning
        };

        [Test]
        public void ProductionAssemblies_DoNotReferenceTestKit()
        {
            foreach (Assembly assembly in ProductionAssemblies)
            {
                var offending = assembly.GetReferencedAssemblies()
                    .Select(a => a.Name)
                    .Where(n => n.Contains("TestKit", StringComparison.Ordinal)
                                || n.Contains("TestTier", StringComparison.Ordinal))
                    .ToList();

                Assert.That(offending, Is.Empty,
                    $"{assembly.GetName().Name} references {string.Join(", ", offending)}.");
            }
        }

        [Test]
        public void ProductionAssemblies_DeclareNoInternalsVisibleTo()
        {
            // Spec section 6.2 calls this the most likely crack: it is the standard .NET answer to
            // "the test needs to reach this", and it dissolves the whole tier split in one line.
            foreach (Assembly assembly in ProductionAssemblies)
            {
                Assert.That(
                    assembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName),
                    Is.Empty,
                    $"{assembly.GetName().Name} declares InternalsVisibleTo. If a test genuinely needs "
                    + "to reach something, the tier boundary was drawn in the wrong place - move the "
                    + "boundary, do not punch through it.");
            }
        }

        [Test]
        public void SqliteStoreAssembly_ContainsNoTestControlImplementation()
        {
            Assembly store = typeof(SqliteMoneyStore).Assembly;

            Assert.That(
                store.GetTypes().Where(t => typeof(IMoneyStoreTestControl).IsAssignableFrom(t)),
                Is.Empty);
        }

        [Test]
        public void SqliteStoreAssembly_DoesNotReferenceTheProvisioningAssembly()
        {
            // The spec's section 1 Recommendation rules out both a shared Common assembly and
            // InternalsVisibleTo and accepts ~40 lines of duplication instead. This is what makes
            // that an enforced decision rather than an intention.
            Assert.That(
                typeof(SqliteMoneyStore).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.Contains("Provisioning"));
        }

        [Test]
        public void SqliteStoreAssembly_ContainsNoDestructiveSql()
        {
            // Spec section 1, "How the SQLite facade gets its teeth": the shipped store assembly
            // contains no DROP TABLE, no VACUUM INTO, and no DELETE FROM without a WHERE Id=.
            // A crude but honest string scan over the assembly's UTF-8 bytes; the SQL in this
            // assembly is all literal, so a literal scan is the right shape.
            string path = typeof(SqliteMoneyStore).Assembly.Location;
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));

            foreach (string forbidden in new[] { "DROP TABLE", "DROP VIEW", "VACUUM", "TRUNCATE" })
            {
                Assert.That(text, Does.Not.Contain(forbidden),
                    $"MyMoney.Data.Sqlite.dll contains the string '{forbidden}'. The destructive code "
                    + "must not be in a release build's bits at all - that is the whole guarantee.");
            }

            foreach (string deleteStatement in Occurrences(text, "DELETE FROM"))
            {
                Assert.That(deleteStatement, Does.Contain("WHERE Id="),
                    "A DELETE without a WHERE Id= clause in the store assembly: " + deleteStatement);
            }
        }

        [Test]
        public void ProductionReferenceClosure_ContainsNoSqlServerEngineAssembly()
        {
            // See the plan's D-4 for why this is a reference-closure walk rather than an assertion
            // over a dotnet publish directory. In Plan A no MyMoney.Data.SqlServer* assembly
            // exists, so this cannot fail today - it is built now as the tripwire Plan B's slice 8
            // has to satisfy, and it says so rather than implying it proves something.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Assembly>(ProductionAssemblies);

            while (queue.Count > 0)
            {
                Assembly current = queue.Dequeue();
                if (!seen.Add(current.GetName().Name))
                {
                    continue;
                }

                foreach (AssemblyName reference in current.GetReferencedAssemblies())
                {
                    Assert.That(
                        reference.Name.StartsWith("MyMoney.Data.SqlServer", StringComparison.Ordinal),
                        Is.False,
                        $"{current.GetName().Name} pulls in {reference.Name}. The released assembly "
                        + "set must not contain the SQL Server engine at all.");

                    if (reference.Name.StartsWith("MyMoney", StringComparison.Ordinal)
                        && !seen.Contains(reference.Name))
                    {
                        queue.Enqueue(Assembly.Load(reference));
                    }
                }
            }
        }

        [Test]
        public void MyMoneyBusiness_HasNoWpfAssemblyReference()
        {
            AssertNoWpf(typeof(IMoneyStore).Assembly);
        }

        [TestCase("MyMoney.Data.Sqlite")]
        [TestCase("MyMoney.Data.Sqlite.Provisioning")]
        public void MyMoneyData_HasNoWpfAssemblyReference(string assemblyName)
        {
            AssertNoWpf(ProductionAssemblies.Single(a => a.GetName().Name == assemblyName));
        }

        [Test]
        public void TestsBusiness_DoesNotReferenceTheWpfUiProject()
        {
            // Fixes spec section 2.1 item 2: UnitTests.csproj references MyMoney.csproj today, so
            // "business test" is currently an honor system. The new business tier is not.
            Assembly tests = Assembly.Load("MyMoney.Tests.Business");

            Assert.That(
                tests.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.EqualTo("MyMoney"),
                "MyMoney.Tests.Business referenced the WPF UI project.");
        }

        private static void AssertNoWpf(Assembly assembly)
        {
            Assert.That(
                assembly.GetReferencedAssemblies().Select(a => a.Name).Where(n => WpfAssemblyNames.Contains(n)),
                Is.Empty,
                $"{assembly.GetName().Name} referenced a WPF assembly.");
        }

        private static IEnumerable<string> Occurrences(string text, string needle)
        {
            int index = text.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                yield return text.Substring(index, Math.Min(160, text.Length - index));
                index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
            }
        }
    }
}
```

Wire the one reference it still needs:
```bash
dotnet add Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj reference Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj --filter "FullyQualifiedName~AssemblyBoundaryTests"`
Expected: FAIL to compile — `CS0246: The type or namespace name 'MyMoney.Tests.Business' could not be resolved` until the reference is added; then, once it compiles, all nine should already **pass**, because the boundaries were designed in from Task 1 rather than retrofitted. **That is the expected outcome and it is not a broken TDD cycle** — these are *regression* tests for properties the earlier tasks established, and the way to see them fail is Step 3's deliberate violation.

- [ ] **Step 3: Prove the tests can fail**

Temporarily add to `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`:
```xml
    <ProjectReference Include="..\MyMoney.TestKit.Contracts\MyMoney.TestKit.Contracts.csproj" />
```
Run: `dotnet test Source/WPF/MyMoney.Tests.Architecture/MyMoney.Tests.Architecture.csproj --filter "Name=ProductionAssemblies_DoNotReferenceTestKit"`
Expected: FAIL with `MyMoney.Business references MyMoney.TestKit.Contracts.`
**Then revert the edit** and re-run to confirm it passes again.

- [ ] **Step 4: Add the build-time enforcement**

Create `Source/WPF/Directory.Build.targets`:
```xml
<Project>

  <!--
    Layer 2 of the four-layer one-way-dependency enforcement (spec section 2.5).

    This is the layer that beats deadline pressure, because it fails at `dotnet build`, not at a
    test run someone might skip. Spec section 6.3: "I just need BasicsFixtureBuilder from TestKit
    in this one production class" is a real, recurring shortcut, and a test-run-time failure is
    easy to ignore mid-task while a build failure is not. BOTH layers, not either.

    Opt-IN via <IsProductionAssembly>true</IsProductionAssembly>, so this file is inert for every
    existing project in the tree until that property is added.
  -->
  <Target Name="FailOnTestTierReference"
          AfterTargets="ResolveAssemblyReferences"
          Condition="'$(IsProductionAssembly)' == 'true'">

    <ItemGroup>
      <TestTierReference Include="@(ReferencePath)"
                         Condition="$([System.String]::Copy('%(FileName)').Contains('TestKit')) Or
                                    $([System.String]::Copy('%(FileName)').Contains('TestTier'))" />
    </ItemGroup>

    <Error Condition="'@(TestTierReference)' != ''"
           Code="MYMONEY0001"
           Text="$(MSBuildProjectName) is a production assembly and must not reference the test tier, but resolved: @(TestTierReference->'%(FileName)', ', '). If a test genuinely needs to reach something here, the tier boundary was drawn in the wrong place - move the boundary, do not punch through it (design spec section 6.2)." />
  </Target>

</Project>
```

Add this single line to the first `<PropertyGroup>` of `MyMoney.csproj`, `MyMoney.Business.csproj`, `MyMoney.Data.csproj`, `MyMoney.Data.Sqlite.csproj` and `MyMoney.Data.Sqlite.Provisioning.csproj`:
```xml
    <IsProductionAssembly>true</IsProductionAssembly>
```

- [ ] **Step 5: Prove the build-time layer fires**

Temporarily add the same `ProjectReference` to `MyMoney.Business.csproj` as in Step 3 and run:
`dotnet build Source/WPF/MyMoney.Business/MyMoney.Business.csproj`
Expected: FAIL with `error MYMONEY0001: MyMoney.Business is a production assembly and must not reference the test tier…`
**Then revert the edit.**

- [ ] **Step 6: Run the whole solution to verify it passes**

Run: `dotnet build Source/WPF/MyMoney.sln` then `dotnet test Source/WPF/MyMoney.sln`
Expected: build succeeds; all suites pass, including the pre-existing `UnitTests` and `MyMoney.TestSupport` projects, which this plan has not touched.

- [ ] **Step 7: Commit**
```bash
git add Source/WPF/Directory.Build.targets Source/WPF/MyMoney/MyMoney.csproj Source/WPF/MyMoney.Business/MyMoney.Business.csproj Source/WPF/MyMoney.Data/MyMoney.Data.csproj Source/WPF/MyMoney.Data.Sqlite/MyMoney.Data.Sqlite.csproj Source/WPF/MyMoney.Data.Sqlite.Provisioning/MyMoney.Data.Sqlite.Provisioning.csproj Source/WPF/MyMoney.Tests.Architecture
git commit -m "$(cat <<'EOF'
test(arch): the rest of the Tier-0 band, plus build-time tier enforcement

Slice 6, task 22. Completes the eleven Tier-0 tests: no production assembly
references the test tier or declares InternalsVisibleTo, the SQLite store
assembly implements no test control and contains no DROP/VACUUM/TRUNCATE and no
unqualified DELETE, the WPF-free boundaries hold per engine, and the new
business test tier does not reference the WPF UI project.

Adds the layer that actually beats deadline pressure: a Directory.Build.targets
target that fails `dotnet build` - not a test run someone might skip - when a
project marked IsProductionAssembly resolves a TestKit or TestTier reference.
It is opt-in, so it is inert for every untouched project in the tree.

The SQL-Server-absence check is a reference-closure walk rather than an
assertion over a publish directory (plan decision D-4), and in Plan A it cannot
fail - it is the tripwire Plan B's slice 8 has to satisfy.

Spec: sections 1, 2.1, 2.5, 6.1, 6.2, 6.3.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 23 (slice 7): `AddAccountService` — the business layer, callable with no UI present

**Files:**
- Create: `Source/WPF/MyMoney.Business/AppServices/AddAccountService.cs`
- Test: `Source/WPF/MyMoney.Tests.Business/AppServices/AddAccountServiceTests.cs`

**Interfaces:**
- Consumes: `IMoneyStore`, `IMoneyQuery`, `AccountQuery` (Tasks 1, 2); `InMemorySqliteStore`, `RecordingStore` (Task 16).
- Produces:
  - `sealed class AddAccountService` with `AddAccountService(IMoneyStore store, IMoneyQuery query)` and `Account AddAccount(string name, AccountType type, string currency)`
  - `sealed class DuplicateAccountNameException : Exception` with `string AccountName`

**Where this lives and why.** The `AppServices` band in `MyMoney.Business`, alongside the existing `DatabaseLifecycle`, which is already exactly this shape and is the working precedent (§3.1). It talks to the UI only through the callback ports — and in Plan A it does not talk to the UI at all, which is the point: **slice 7's success criterion is that the business layer is callable with no UI present.**

**Why both ports.** It reads through `IMoneyQuery` (the duplicate-name check and Id allocation — projections, no write intended) and writes through `IMoneyStore`. That is decision D-1 in use rather than in theory, and it is what makes `IMoneyQuery` a real Plan-A consumer rather than a type waiting for reports.

The retry loop is Task 24; this task is the happy path and the input validation.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Business/AppServices/AddAccountServiceTests.cs`:
```csharp
using System;
using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace Walkabout.Tests.Business.AppServices
{
    [TestFixture]
    public class AddAccountServiceTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        private AddAccountService NewService(IMoneyStore store = null) =>
            new AddAccountService(store ?? this.fixture.Store, this.fixture.Query);

        [Test]
        public void AddAccount_PersistsTheAccountAndReturnsItWithAnIdAndAVersion()
        {
            Account created = this.NewService().AddAccount("Checking", AccountType.Checking, "USD");

            Assert.That(created.Id, Is.GreaterThan(0));
            Assert.That(created.RowVersion, Is.EqualTo(1));

            Account persisted = this.fixture.Store.LoadAccounts().Single();
            Assert.That(persisted.Name, Is.EqualTo("Checking"));
            Assert.That(persisted.Type, Is.EqualTo(AccountType.Checking));
            Assert.That(persisted.Currency, Is.EqualTo("USD"));
        }

        [Test]
        public void AddAccount_RunsWithNoUiPresent()
        {
            // Slice 7's success criterion, asserted rather than assumed: nothing in this call
            // path touches a dispatcher, a dialog or a callback port.
            AddAccountService service = this.NewService();

            Assert.DoesNotThrow(() => service.AddAccount("Headless", AccountType.Cash, "USD"));

            Assert.That(
                typeof(AddAccountService).Assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.None.EqualTo("PresentationFramework"));
        }

        [Test]
        public void AddAccount_AllocatesSuccessiveIds()
        {
            AddAccountService service = this.NewService();

            Account first = service.AddAccount("Checking", AccountType.Checking, "USD");
            Account second = service.AddAccount("Savings", AccountType.Savings, "USD");

            Assert.That(second.Id, Is.EqualTo(first.Id + 1));
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All), Has.Count.EqualTo(2));
        }

        [Test]
        public void AddAccount_RefusesADuplicateNameCaseInsensitively_AndWritesNothing()
        {
            var recording = new RecordingStore(this.fixture.Store);
            AddAccountService service = this.NewService(recording);
            service.AddAccount("Checking", AccountType.Checking, "USD");
            int writesAfterFirst = recording.WriteCount;

            var ex = Assert.Throws<DuplicateAccountNameException>(
                () => service.AddAccount("CHECKING", AccountType.Savings, "USD"));

            Assert.That(ex.AccountName, Is.EqualTo("CHECKING"));
            Assert.That(recording.WriteCount, Is.EqualTo(writesAfterFirst),
                "The refusal must happen before any write, not after a partial one.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AddAccount_RefusesAnEmptyName(string name)
        {
            Assert.Throws<ArgumentException>(() => this.NewService().AddAccount(name, AccountType.Cash, "USD"));
        }

        [Test]
        public void AddAccount_RefusesANameContainingACharacterTheDomainForbids()
        {
            // Accounts.InvalidNameChars is the domain's own list; the service consults it rather
            // than inventing a second rule.
            Assert.Throws<ArgumentException>(() => this.NewService().AddAccount("Bad:Name", AccountType.Cash, "USD"));
        }

        [Test]
        public void AddAccount_UsesTheOrdinaryWriteSurfaceAndNothingPrivileged()
        {
            var recording = new RecordingStore(this.fixture.Store);

            this.NewService(recording).AddAccount("Checking", AccountType.Checking, "USD");

            Assert.That(recording.Calls, Has.Some.StartsWith("SaveRoot(Account#"));
            Assert.That(recording.Calls, Has.None.Contains("SaveRoots"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj --filter "FullyQualifiedName~AddAccountServiceTests"`
Expected: FAIL to compile — `CS0246: The type or namespace name 'AddAccountService' could not be found`.

- [ ] **Step 3: Write `AddAccountService`**

Create `Source/WPF/MyMoney.Business/AppServices/AddAccountService.cs`:
```csharp
using System;
using System.Collections.Generic;
using Walkabout.Data;

namespace Walkabout.Business.AppServices
{
    /// <summary>Raised when an account with this name already exists.</summary>
    public sealed class DuplicateAccountNameException : Exception
    {
        public DuplicateAccountNameException(string accountName)
            : base($"There is already an account called '{accountName}'.")
        {
            this.AccountName = accountName;
        }

        public string AccountName { get; }
    }

    /// <summary>
    /// The AppServices band in MyMoney.Business, alongside the existing DatabaseLifecycle, which
    /// is already exactly this shape and is the working precedent - spec section 3.1.
    ///
    /// It uses BOTH ports, which is the split working as designed: IMoneyQuery for the reads it
    /// does not intend to write from (the duplicate-name check and the id allocation, which come
    /// back as projections), IMoneyStore for the write. Spec section 2.7.2 and the plan's D-1.
    ///
    /// It talks to no UI at all - no dispatcher, no dialog, no callback port - which is slice 7's
    /// success criterion rather than an incidental property.
    ///
    /// The conflict-retry loop is Task 24.
    /// </summary>
    public sealed class AddAccountService
    {
        private readonly IMoneyStore store;
        private readonly IMoneyQuery query;

        public AddAccountService(IMoneyStore store, IMoneyQuery query)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public Account AddAccount(string name, AccountType type, string currency)
        {
            ValidateName(name);

            foreach (AccountRow row in this.query.ListAccounts(AccountQuery.All))
            {
                if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DuplicateAccountNameException(name);
                }
            }

            Account account = this.BuildAccount(name, type, currency);
            this.store.SaveRoot(account);
            return account;
        }

        /// <summary>
        /// A brand-new, Inserted root with a caller-allocated id. Id allocation stays a caller
        /// concern, exactly as it already is in Accounts.AddAccount's NextAccount counter; with no
        /// ambient graph, IMoneyQuery.NextAccountId is where that counter now lives. A lost race
        /// is handled rather than ignored - the store reports a primary-key collision as
        /// ConcurrencyConflictException, which Task 24's retry loop catches.
        /// </summary>
        private Account BuildAccount(string name, AccountType type, string currency)
        {
            // A detached container so every root has a non-null Parent - CLAUDE.md records that a
            // parentless PersistentObject is a real source of NullReferenceException in
            // business-layer code that walks it.
            var container = new Accounts((PersistentObject)null);

            return new Account(container)
            {
                Id = checked((int)this.query.NextAccountId()),
                Name = name,
                Type = type,
                Currency = currency,
                OpeningBalance = 0m,
            };
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("An account needs a name.", nameof(name));
            }

            // The domain's own list, consulted rather than duplicated.
            if (name.IndexOfAny(Accounts.InvalidNameChars) >= 0)
            {
                throw new ArgumentException(
                    $"An account name cannot contain any of: {new string(Accounts.InvalidNameChars)}",
                    nameof(name));
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj --filter "FullyQualifiedName~AddAccountServiceTests"`
Expected: PASS — 9 tests.

- [ ] **Step 5: Commit**
```bash
git add Source/WPF/MyMoney.Business/AppServices Source/WPF/MyMoney.Tests.Business/AppServices
git commit -m "$(cat <<'EOF'
feat(business): AddAccountService - provision-then-add, with no UI present

Slice 7, task 23. The first use case in the AppServices band, alongside the
existing DatabaseLifecycle. It reads through IMoneyQuery (duplicate-name check,
id allocation - projections it does not intend to write from) and writes through
IMoneyStore, which is the read/write port split working as designed rather than
in theory.

Talks to no UI at all: no dispatcher, no dialog, no callback port. That is the
slice's success criterion and it is asserted, not assumed.

Spec: sections 3.1, 2.7.2, 5. Plan decision D-1.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 24 (slice 7): the conflict-retry loop — the last piece of the vertical

**Files:**
- Modify: `Source/WPF/MyMoney.Business/AppServices/AddAccountService.cs`
- Test: `Source/WPF/MyMoney.Tests.Business/AppServices/AddAccountRetryTests.cs`

**Interfaces:**
- Consumes: `ConcurrencyConflictException`, `FaultInjectingStore` (Task 16).
- Produces: `AddAccountService.MaxAttempts` (const `int`, value `3`); `sealed class ConcurrencyRetryExhaustedException : Exception` with `int Attempts`.

**This is new logic, not a relocation.** §1.6a is explicit: *"no retry loop exists anywhere in the codebase today; today's behavior is 'the conflict propagates to the UI as a message box.'"* The shipped concurrency model is version-checked optimistic concurrency, permanently — there is no lease, because the product's actual goal is **a human and an AI agent working the same books at the same time**, which a lease defeats by making them take turns.

**The two properties that must hold, and why each is load-bearing:**

1. **The loop catches `ConcurrencyConflictException` and nothing wider.** §1.6a consequence 1: `SaveRoot`/`DeleteRoot`'s precondition failures are `ArgumentException` — caller bugs, unconditionally reproducible. A loop that caught them would re-query, reapply and retry a call that can only ever fail the same way, turning an immediate, obvious crash into a spin. `FaultInjectingStore` proves the `ArgumentException` goes straight through.
2. **Re-query, reapply, retry — not "retry the same object."** The retry must go back to the data layer for current state, because the whole reason the first attempt failed is that its state was stale. Here that means a fresh `NextAccountId()` and a fresh duplicate-name check, both through `IMoneyQuery`.

§1.6a consequence 2 is what makes the retry legal at all, and it is already pinned by the contract suite (Task 17's `AfterAConflict_TheSameCallIsStillAdmissible`): a failed write leaves the root's change state untouched, because `postCommitActions` never runs unless the commit succeeded.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/MyMoney.Tests.Business/AppServices/AddAccountRetryTests.cs`:
```csharp
using System;
using System.Linq;
using MyMoney.TestKit;
using NUnit.Framework;
using Walkabout.Business.AppServices;
using Walkabout.Data;

namespace Walkabout.Tests.Business.AppServices
{
    /// <summary>
    /// The concurrency path, tested from the very first slice rather than deferred - spec section
    /// 5, slice 7. FaultInjectingStore is the named, first-class way to provoke a conflict on
    /// demand, which is what the owner asked the test business layer for (spec section 2.4).
    /// </summary>
    [TestFixture]
    public class AddAccountRetryTests
    {
        private InMemorySqliteStore fixture;

        [SetUp]
        public void SetUp() => this.fixture = InMemorySqliteStore.Create();

        [TearDown]
        public void TearDown() => this.fixture?.Dispose();

        [Test]
        public void AddAccount_RecoversFromASingleConflictByRequeryingAndRetrying()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);      // the id the first attempt will allocate
            var service = new AddAccountService(faulty, this.fixture.Query);

            Account created = service.AddAccount("Checking", AccountType.Checking, "USD");

            Assert.That(created.Id, Is.EqualTo(1));
            Assert.That(this.fixture.Store.LoadAccounts(), Has.Count.EqualTo(1));
        }

        [Test]
        public void AddAccount_AfterTooManyConflicts_GivesUpWithAClearException()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            for (int i = 0; i < AddAccountService.MaxAttempts; i++)
            {
                faulty.ThrowConflictFor(1);
            }

            var service = new AddAccountService(faulty, this.fixture.Query);

            var ex = Assert.Throws<ConcurrencyRetryExhaustedException>(
                () => service.AddAccount("Checking", AccountType.Checking, "USD"));

            Assert.That(ex.Attempts, Is.EqualTo(AddAccountService.MaxAttempts));
            Assert.That(ex.InnerException, Is.InstanceOf<ConcurrencyConflictException>());
            Assert.That(this.fixture.Store.LoadAccounts(), Is.Empty);
        }

        [Test]
        public void TheRetryLoop_DoesNotRetryAnArgumentException()
        {
            // Spec sections 1.6a consequence 1 and 1.6c: a precondition failure is a CALLER BUG,
            // unconditionally reproducible. A loop that caught it would retry a call that can only
            // ever fail the same way, turning an immediate, obvious crash into a spin.
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(1, new ArgumentException("root is in the wrong state"));
            var service = new AddAccountService(faulty, this.fixture.Query);

            Assert.Throws<ArgumentException>(() => service.AddAccount("Checking", AccountType.Checking, "USD"));
        }

        [Test]
        public void TheRetryLoop_DoesNotSwallowAnUnrelatedFailure()
        {
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowOnCall(1, new InvalidOperationException("the disk caught fire"));
            var service = new AddAccountService(faulty, this.fixture.Query);

            Assert.Throws<InvalidOperationException>(() => service.AddAccount("Checking", AccountType.Checking, "USD"));
        }

        [Test]
        public void TheRetry_ReQueriesRatherThanReusingTheStaleState()
        {
            // The whole reason the first attempt failed is that its state was stale, so a retry
            // that did not go back to the data layer would fail identically. Here: someone else
            // took id 1 during the conflict, and the retry must pick up id 2.
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);
            var service = new AddAccountService(faulty, this.fixture.Query);

            // The "someone else" - written through the inner store, so the decorator does not see it.
            this.fixture.Store.SaveRoot(new Account(new Accounts((PersistentObject)null))
            {
                Id = 1, Name = "Theirs", Type = AccountType.Cash, OpeningBalance = 0m
            });

            Account created = service.AddAccount("Mine", AccountType.Checking, "USD");

            Assert.That(created.Id, Is.EqualTo(2));
            Assert.That(this.fixture.Query.ListAccounts(AccountQuery.All).Select(r => r.Name),
                Is.EqualTo(new[] { "Theirs", "Mine" }));
        }

        [Test]
        public void TheRetry_StillRefusesADuplicateNameThatAppearedDuringTheConflict()
        {
            // Re-query means re-validate: a name that became taken between the two attempts must
            // be refused, not written. This is why the retry re-runs the whole use case body
            // rather than just re-issuing the failed SaveRoot.
            var faulty = new FaultInjectingStore(this.fixture.Store);
            faulty.ThrowConflictFor(1);
            var service = new AddAccountService(faulty, this.fixture.Query);

            this.fixture.Store.SaveRoot(new Account(new Accounts((PersistentObject)null))
            {
                Id = 1, Name = "Checking", Type = AccountType.Cash, OpeningBalance = 0m
            });

            Assert.Throws<DuplicateAccountNameException>(
                () => service.AddAccount("Checking", AccountType.Checking, "USD"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj --filter "FullyQualifiedName~AddAccountRetryTests"`
Expected: FAIL — `Expected: <Walkabout.Data.ConcurrencyConflictException> ... But was: no exception` on the first test's `AddAccount` call, which currently lets the conflict escape.

- [ ] **Step 3: Add the retry loop**

In `AddAccountService.cs`, add the exception type and replace `AddAccount`'s body:
```csharp
    /// <summary>
    /// Raised when a use case gave up after MaxAttempts version conflicts. Distinct from
    /// ConcurrencyConflictException so a caller can tell "this one write lost a race" from "we
    /// kept losing"; the last conflict is carried as InnerException.
    /// </summary>
    public sealed class ConcurrencyRetryExhaustedException : Exception
    {
        public ConcurrencyRetryExhaustedException(int attempts, Exception inner)
            : base($"Gave up after {attempts} attempts - another writer kept changing the same data.",
                   inner)
        {
            this.Attempts = attempts;
        }

        public int Attempts { get; }
    }
```
```csharp
        /// <summary>
        /// How many times a use case reapplies its intent before giving up. Three is a starting
        /// default: the product's stated concurrency goal is ONE human and possibly ONE agent
        /// (spec section 1.6a), so a third consecutive loss means something other than ordinary
        /// contention is happening and spinning longer would hide it.
        /// </summary>
        public const int MaxAttempts = 3;

        /// <summary>
        /// The business-layer retry loop spec section 1.6a puts here rather than in the data
        /// layer: the store cannot retry, because reapplying a change requires knowing what the
        /// caller's INTENT was, and intent is a business concept the store must not know about.
        ///
        /// This is NEW logic, not a relocation - no retry loop exists anywhere in the codebase
        /// today; today's behaviour is that the conflict propagates to the UI as a message box.
        ///
        /// It catches ConcurrencyConflictException AND NOTHING WIDER. SaveRoot/DeleteRoot's
        /// precondition failures are ArgumentException - caller bugs, unconditionally
        /// reproducible - and a loop that caught them would retry a call that can only ever fail
        /// the same way, turning an immediate crash into a spin (spec section 1.6a consequence 1).
        ///
        /// It re-runs the WHOLE use case body, not just the failed write, because the reason the
        /// first attempt failed is that its state was stale: the id and the duplicate-name check
        /// both have to be re-derived from current data.
        ///
        /// Retrying is legal at all only because a failed write leaves the root's change state
        /// untouched - R-CRUD-2's postCommitActions deferral, pinned per engine by the contract
        /// suite's AfterAConflict_TheSameCallIsStillAdmissible (spec section 1.6a consequence 2).
        /// </summary>
        public Account AddAccount(string name, AccountType type, string currency)
        {
            ValidateName(name);

            ConcurrencyConflictException lastConflict = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                foreach (AccountRow row in this.query.ListAccounts(AccountQuery.All))
                {
                    if (string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DuplicateAccountNameException(name);
                    }
                }

                Account account = this.BuildAccount(name, type, currency);

                try
                {
                    this.store.SaveRoot(account);
                    return account;
                }
                catch (ConcurrencyConflictException conflict)
                {
                    lastConflict = conflict;
                }
            }

            throw new ConcurrencyRetryExhaustedException(MaxAttempts, lastConflict);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/MyMoney.Tests.Business/MyMoney.Tests.Business.csproj`
Expected: PASS — 6 new tests plus everything from Tasks 1, 4, 16 and 23.

- [ ] **Step 5: Run the whole solution — the slice's end state**

Run: `dotnet build Source/WPF/MyMoney.sln` then `dotnet test Source/WPF/MyMoney.sln`
Expected: build succeeds; every suite passes, old and new.

- [ ] **Step 6: Commit**
```bash
git add Source/WPF/MyMoney.Business/AppServices/AddAccountService.cs Source/WPF/MyMoney.Tests.Business/AppServices/AddAccountRetryTests.cs
git commit -m "$(cat <<'EOF'
feat(business): the conflict-retry loop - Plan A's vertical is complete

Slice 7, task 24. Version-checked optimistic concurrency with the retry in the
business layer, which is the shipped model permanently: the product's goal is a
human and an AI agent working the same books at once, and a lease would defeat
that by making them take turns.

This is new logic, not a relocation - no retry loop exists in the codebase
today. It catches ConcurrencyConflictException and nothing wider, because a
precondition failure is an ArgumentException and a loop that caught it would
spin on a call that can only fail identically. It re-runs the whole use case
body rather than re-issuing the failed write, because the id and the
duplicate-name check both have to be re-derived from current data.

End state: provision a SQLite database from a versioned ledger and add an
account, headlessly, with the conflict path tested.

Spec: sections 1.6a, 1.6c, 2.4, 5 slice 7.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Definition of done for Plan A

After Task 24, all of the following are true and checkable:

- [ ] `dotnet build Source/WPF/MyMoney.sln` succeeds, including the untouched `MyMoney`, `MyMoney.Business`, `MyMoney.Data`, `UnitTests`, `MyMoney.TestSupport`, `UITests` and `ScenarioTest` projects.
- [ ] `dotnet test Source/WPF/MyMoney.sln` passes every suite.
- [ ] A SQLite database can be provisioned from version 0 to the latest step, verified clean, backed up and restored, and reset — all through `IMoneyStoreProvisioner` and `IMoneyStoreTestControl`.
- [ ] `AddAccountService.AddAccount(...)` works with no UI present, survives a provoked concurrency conflict, and refuses a caller bug without retrying it.
- [ ] `MyMoney.Data.Sqlite.dll` contains no `DROP TABLE`, no `VACUUM`, no `TRUNCATE`, and no `DELETE FROM` without a `WHERE Id=`.
- [ ] No production assembly references `*.TestKit*` or `*.TestTier*`, at build time **and** at test time.
- [ ] The fresh-vs-upgraded schema-equality test runs on every build, from every intermediate version.

**What Plan A deliberately does not do**, restated so a reviewer does not read an omission as a gap: no SQL Server anything (Plan B slice 8), no T-1 wall-time measurement (slice 9), no `json_each` benchmark (slice 10 — the *mechanism* is built, the *measurement* is not), no `SampleDataFactory`/`SampleDataService`, no `IDataFormat`/XML, no `MainWindow` decomposition, no `ReportModel` or renderers, and no change to the existing shipped app beyond five one-line MSBuild properties and the solution file.

---

## Self-review

Performed against the finished plan, by the author, before commit. Three passes, as the skill requires.

### 1. Spec coverage — every section of the design, and where it lands

| Spec section | Where implemented | Notes |
|---|---|---|
| §0, §0.1 (verified facts) | Grounding for Tasks 5, 11, 12 | The bundled engine version, the long-lived-connection lifetime model, `defer_foreign_keys`, the `postCommitActions` deferral and `DatabaseEntry.TestDatabase` are all used as stated; Task 5 re-asserts the engine version so §0.1's verification stays true. |
| §1 Candidate A (assembly structure) | Tasks 3, 5, 11, 16, 18 | `MyMoney.Domain` and `MyMoney.Data.Contracts` are **not** created, per §1's own Recommendation ("don't create the assembly yet"); the ports stay in `MyMoney.Business` exactly where `IDatabase` is. |
| §1 `IMoneyQuery` introduction | Tasks 2, 15, 17 | Decision D-1. |
| §1.5 S-0…S-5 (schema executor, ledger) | Tasks 6, 7, 8, 10, 18 | |
| §1.6 R-CRUD-1…5 | Tasks 12, 13, 14 (1–4); Task 6 (5 — steps carry all DDL, and the `Access/*` proc half is SQL-Server-only so has no Plan-A analogue) | |
| §1.6a (concurrency, retry placement) | Tasks 13, 17, 24 | |
| §1.6b / §1.6c (write-surface naming and shape) | Tasks 1, 2, 12–14, 17 | |
| §1.7.1 adoptions | `RETURNING` (12, 13, 14), `json_each` (12, 15), `STRICT` (6, 8), `CHECK` (**not adopted** — no Plan-A invariant is genuinely schema-level yet; stated, not skipped), generated columns / `UPSERT` (noted, not adopted, per the spec), `PRAGMA` introspection (8, 18, 19), `foreign_key_check` (19), `VACUUM INTO` (9) | Views: none created in Plan A (nothing to project yet); the rules are recorded in Global Constraints and enforced by `SchemaSnapshot` including views in its expected set. |
| §1.7.2 / §1.7.3 / §1.7.4 (limits, pushbacks) | Task 13's doc comment (conflict check stays SQL, not a trigger); Task 17 (`Schema_OwnsNoInsteadOfTrigger`) | |
| §1.8 (deferred upgrade, three properties) | Ledger from step 1 (6) + checksums on day one (7); upgrade path exercised every build (10); backup contract-tested from the start (9); open-time version check (11) | The workflow itself is correctly **not** built. |
| §1.9 (sample data) | `StoreIdentity` (1), `TestDatabaseGuard` + IL scan (21) | The `SampleDataService` rewrite is out of scope, exactly as §1.9.9 says. |
| §2.1 / §2.2 / §2.3 (test subsystem shape) | Tasks 1, 2, 5, 16, 22 | Tier 3 (FlaUI) unchanged and untouched, per §5's "one honest deviation". |
| §2.4 (T-1, `RecordingStore`, `FaultInjectingStore`) | Task 16 | Includes the in-memory WAL gotcha (Task 5). |
| §2.5 (four enforcement layers, eleven Tier-0 tests) | Tasks 2, 21, 22 | Roster table in Task 22 accounts for all eleven. |
| §2.6.1–2.6.8 (three ladders, idempotency) | Tasks 18, 19, 20 | `Snapshot`/`Restore` and the plural row forms deferred with a stated reason (D-4). |
| §2.7.1 / §2.7.2 / §2.7.3 (views) | Task 8 (`Schema_DropAll` drops views first, `Verify` includes them), Task 2 (`IProjection` type-system properties), Task 17 (`INSTEAD OF` check) | D-3: nothing depends on the owner's pending answer. |
| §3.1 (AppServices band) | Tasks 23, 24 | |
| §3.2 (backup in the provisioner) | Task 9 | Import/export and `IDataFormat` are deferred by §4 item 5. |
| §3.3 (reporting) | Out of scope — decided but unscheduled, per §5's scope call | |
| §4 items 1–8 | All eight honoured: in place (all tasks), no lease (24), SQL Server parity (17's shared base is what slice 8 derives from), no `Load()` (15, 23), no XML, no credential work, sample-data guard (21), permanent `TestDatabase` flag (11, 21) | |
| §5 slices 1–7 | Tasks 1–3, 4–9, 10, 11–15, 16–17, 18–20, 21, 22, 23–24 | Plus the three deliberate additions/deviations in D-4. |
| §6.1–6.9 (adversarial limits) | Honest-claim rule in Global Constraints; the limits restated at the code in Tasks 11 (§6.1) and 21 (§6.9); §6.2 enforced in 22; §6.3's build-time layer in 22; §6.4 in 17; §6.7's checksum-from-day-one in 7; §6.8 rule 1 in 12 and 19 | No task claims equivalence with SQL Server role separation. |
| §7 (UI plain-language rule) | Tasks 11 and 21 assert plain-language messages | |

**Gaps found: none that are in Plan A's scope.** Two spec items are named in §5's slice 6 and turn out to live elsewhere, which the plan states rather than silently dropping: the `Schema_OwnsNoInsteadOfTrigger` check and §1.6c's six precondition cases are Tier-2 and per engine, so they land in Task 17's contract suite — which is exactly what §5's own slice-6 row says ("in `MyMoney.Tests.Data`… since these are Tier-2 and per engine, not architecture tests").

### 2. Placeholder scan

Searched the finished plan for `TBD`, `TODO`, `implement later`, `add appropriate error handling`, `add validation`, `write tests for the above`, and `similar to Task N`. **Zero occurrences.** Every step carries the actual file content.

Three things that *look* like placeholders and are not, each with its resolution named in the same line of code: the four `NotImplementedException("Task N.")` stubs introduced in Task 6 (`Verify` → 8, `Backup`/`Delete` → 9, `DropAll` → 18), the `WriteRoots` stub in Task 11 (→ 12), and the `UpdateAccounts`/`DeleteAccounts`/test-control stubs (→ 13, 14, 19, 20). Each names the exact task that removes it, and none is reachable from outside its own assembly before then.

**A fourth thing this pass caught and fixed:** Task 6's four stubs and its prose both said `DropAll` arrives in "Task 15" — a stale number from an earlier task ordering. `DropAll` lands in **Task 18**, where `SqliteTableOrder` (the introspected drop order it needs) is introduced. Corrected in the code block, the prose and the note beneath it. This is exactly the class of drift the type-consistency pass below is for, arriving in a task number rather than a type name.

**One thing the first draft got wrong and this review fixed:** Task 22's Step 2 originally read "Expected: FAIL", which is false — the boundary tests pass on first run because the boundaries were designed in from Task 1 rather than retrofitted. Pretending otherwise would have had an implementer hunting a failure that cannot happen. It now says so plainly and adds **Step 3**, which deliberately breaks the boundary, watches the test go red, and reverts — a real red/green cycle for a regression test.

### 3. Type consistency

Checked every type, member and signature named in more than one task.

| Name | Declared | Used | Consistent? |
|---|---|---|---|
| `StoreIdentity(string, DbFlavor, bool, int)` | Task 1 | 11, 16, 21 | ✅ `DbFlavor`, not `DataEngineType` (D-4), everywhere. |
| `IMoneyStore` — the four writes + `Identity` + `LoadAccounts` | Task 1 | 2, 11, 15, 16, 17, 23, 24 | ✅ `RecordingStore` and `FaultInjectingStore` implement all six members. |
| `MoneyStoreBase.WriteRoots(IReadOnlyList<IAggregateRoot>)` | Task 1 | 11, 12 | ✅ `protected abstract`, same signature. |
| `IMoneyQuery.ListAccounts` / `NextAccountId` | Task 2 | 15, 17, 19, 20, 23, 24 | ✅ |
| `AccountQuery(IReadOnlyList<long>, bool)` + `.All` | Task 2 | 15, 17, 19, 23, 24 | ✅ |
| `IMoneyStoreProvisioner` — 8 members | Task 3 | 6, 8, 9, 17, 18, 21 | ✅ `SqliteMoneyStoreProvisioner` implements all eight; `AppliedSteps()` and `Connection` are extras on the class, deliberately not on the port. |
| `SchemaVerifyResult` / `SchemaDifference` | Task 3 | 8, 17 | ✅ |
| `SchemaDriftException` / `SchemaTooNewException` | Task 3 | 7, 11 | ✅ |
| `IMoneyStoreTestControl` — 13 members | Task 3 | 18, 19, 20 | ✅ Every member implemented by Task 20's end; none left throwing. |
| `TableRef` (`.Name`, `.All`, `.Of<T>()`, three statics) | Task 3 | 19, 20 | ✅ |
| `ClearOptions.Default` / `ClearResult(IReadOnlyDictionary<TableRef, long>)` | Task 3 | 19 | ✅ |
| `RowSnapshot(TableRef, long, IReadOnlyDictionary<string, object>)` / `IRowScope.Removed` | Task 3 | 20 | ✅ Singular `RowSnapshot`, matching D-4's deferral of the plural forms. |
| `MoneyScale.ToStorage` / `FromStorage` | Task 4 | 12, 15 | ✅ |
| `SqliteConnectionFactory.Open` / `IsInMemory` / `InMemoryDataSource` | Task 5 (copy A), Task 11 (copy B) | 6, 9, 16 | ✅ Identical surface in both namespaces; the non-reference is enforced in 22. |
| `SqliteProvisioningOptions` vs. `SqliteStoreOptions` | Tasks 6, 11 | 16 | ✅ Two records with the same shape in two assemblies — deliberate, since the assemblies do not reference each other. |
| `SchemaStepCatalog.All` / `.LatestVersion`; `SchemaStep.ComputeChecksum` | Task 6 | 7, 8, 10, 16, 17, 18, 21 | ✅ |
| `SqliteMoneyStore.MaxKnownSchemaVersion` | Task 11 | 17 | ✅ Task 17 asserts it equals `SchemaStepCatalog.LatestVersion`, so the two constants cannot drift. **Task 10 adds step 4, so this constant is `4`** — checked against Task 11's declaration, which says `4`. |
| `AccountRowCodec.Columns` / `JsonExtractList` / `ToJsonArray` / `IdsJsonArray` / `AddParameters` / `Read` | Task 12 | 13, 15 | ✅ `Read` takes `(SQLiteDataReader, Accounts)` in both the declaration and Task 15's call. |
| `SqliteMoneyStore.ReadStoredVersion(long, SQLiteTransaction)` | Task 13 | 14 | ✅ |
| `InMemorySqliteStore` — `Store`, `Query`, `Provisioner`, `Connection`, `TestControl`, `Create` | Task 16, extended in Task 18 | 17, 19, 20, 23, 24 | ✅ |
| `RecordingStore.Calls` / `.WriteCount` | Task 16 | 23 | ✅ Task 23 asserts on `Calls` entries of the form `SaveRoot(Account#1)`, which is exactly the format Task 16 produces. |
| `FaultInjectingStore.ThrowConflictFor` / `.ThrowOnCall` / `.Clear` | Task 16 | 24 | ✅ Task 24 relies on a registered conflict firing **once** then clearing, which Task 16's `conflictingRootIds.Remove(...)` provides and Task 16's own test pins. |
| `StoreContractTests` — four `protected abstract` members + `InsteadOfTriggerCount` | Task 17 | 17 | ✅ The SQLite fixture overrides all five. |
| `SqliteTableOrder.ForDelete` / `.DataTables` / `.HistoryTable` | Task 18 | 19 | ✅ |
| `TestDatabaseGuard.Require(StoreIdentity, string)` / `TestDatabaseRequiredException` | Task 21 | 18's factory, 9's `Delete`, 6's `DropAll` | ✅ |
| `AddAccountService` ctor, `AddAccount`, `MaxAttempts`; `DuplicateAccountNameException`; `ConcurrencyRetryExhaustedException` | Tasks 23, 24 | 23, 24 | ✅ |

**Three inconsistencies found and fixed inline during this pass:**

1. **`IRowScope.Removed` was `RowSetSnapshot` in Task 3 and `RowSnapshot` in Task 20.** `RowSetSnapshot` is never declared anywhere in Plan A, because the plural row forms are deferred — so Task 3 would not have compiled. Task 3's declaration is now `RowSnapshot`, with the deferral noted in the doc comment and in D-4.
2. **`ClearOptions` was a `readonly record struct` in the spec and therefore in the first draft of Task 3, while Task 19's `options ??= ClearOptions.Default` requires a reference type** — and, worse, `default(ClearOptions)` on a record struct silently gives `ResetIdentity == false`, inverting the one option §2.6.4 calls the cross-engine trap. Changed to a `sealed record` class in Task 3 and recorded in D-4, with an assertion in Task 3's own test.
3. **`StoreIdentity.Engine` was `DataEngineType` in the spec**, which lives in `MyMoney.Data` under the same namespace as `MyMoney.Business`'s types — a genuine duplicate-type ambiguity for anything referencing both. Changed to `DbFlavor` (already in `MyMoney.Business/IDatabase.cs`) in Task 1, propagated to Tasks 11, 16 and 21, and recorded in D-4.

One further consistency risk was checked and is **not** a defect: `SqliteProvisioningOptions` and `SqliteStoreOptions` are two records of identical shape in two assemblies. That is the direct consequence of §1's Recommendation against a shared `Common` assembly, it is ~4 lines of duplication rather than the ~40 §1 budgeted for, and Task 22 enforces the non-reference that makes it necessary.
