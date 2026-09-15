# Business-Layer Test-Support Library: Shared `IDatabase` Contract Suite (Design Spec)

## Status

Design phase. This is slice 1 of epic #1's item #3 (three-slice initiative — see
GitHub issue #20 for the full breakdown and links to slice 2 (#19, dual-engine
sample-data parity) and slice 3 (#7, threading/concurrency test support, which
this slice's mock is expected to serve as the fast deterministic backdrop for).

## Goal

Today, exactly one test (`Source/WPF/UnitTests/SqlMappingTests.cs`) exercises a
real save/load round-trip, and it does so against a real on-disk SQLite file.
Every other business-logic test (`CostBasisTests.cs`, `TaxTests.cs`, etc.)
constructs a `MyMoney` object directly and never touches `IDatabase` at all —
which is fine for pure business-rule logic, but means nothing currently tests
the `IDatabase` *contract* itself (save, reload, and the `PersistentObject`
dirty-tracking lifecycle — `ChangeType` Inserted/Changed/Deleted transitions,
`OnUpdated()`, `RemoveDeleted()`) in an engine-independent way, and nothing
gives fast, zero-I/O coverage of that contract for tight dev-loop iteration.

Build one shared test suite, expressed purely in terms of `IDatabase`, that
runs against multiple backing-store implementations:

- **This slice**: an in-memory `MockDatabase` (fast, zero I/O) and
  `SqliteDatabase` (already exists) as the two flavors.
- **Slice 2 (#19)**: adds `SqlServerStoredProcDatabase` as a third flavor, plus
  a headless `SampleDataGenerator` entry point and a FlaUI end-to-end scenario
  — no new *contract* test logic, just more flavors and a richer generated
  dataset.

## Non-Goals

- Slice 2 (#19) and slice 3 (#7) themselves — tracked separately, not built here.
- Any change to the `IDatabase` interface's shape.
- Any change to `XmlStore`'s file-coupled implementation (it stays as-is; this
  spec reuses one existing internal helper from it — see Design — but does not
  refactor it to be stream-agnostic, which would be its own separate,
  higher-risk piece of work).
- Migrating `SqlMappingTests.cs`'s SQLite *schema-migration* coverage
  (`CreateOrUpdateTable`, `LoadTableMetadata` — adding/resizing/removing a
  column at runtime). That's SQLite-specific behavior with no equivalent
  contract on other engines, not part of the generic `IDatabase` suite. It
  stays in `SqlMappingTests.cs` unchanged.
- SQL Server as a flavor in this slice (that's #19's job, once the headless
  `SampleDataGenerator` entry point and bootstrap wiring it needs exist).

## Design

### 1. Shared contract test suite: abstract base class + one concrete subclass per engine

```csharp
public abstract class DatabaseContractTests
{
    protected IDatabase Database { get; private set; }

    [SetUp]
    public virtual void SetUp() => this.Database = this.CreateDatabase();

    [TearDown]
    public virtual void TearDown() => this.Database?.Disconnect();

    protected abstract IDatabase CreateDatabase();

    [Test]
    public void SaveAndReload_PreservesAccountAndTransactionData() { /* ... */ }

    [Test]
    public void Insert_ThenReload_ItemAppearsWithCleanState() { /* ... */ }

    [Test]
    public void Update_ThenReload_ChangePersisted() { /* ... */ }

    [Test]
    public void Delete_ThenReload_ItemIsGone() { /* ... */ }
}

[TestFixture]
public class MockDatabaseContractTests : DatabaseContractTests
{
    protected override IDatabase CreateDatabase() => new MockDatabase();
}

[TestFixture]
public class SqliteDatabaseContractTests : DatabaseContractTests
{
    private string path;

    protected override IDatabase CreateDatabase()
    {
        this.path = Path.Combine(Path.GetTempPath(), $"ContractTest_{Guid.NewGuid():N}.mmdb");
        var db = new SqliteDatabase { DatabasePath = this.path };
        db.Create();
        return db;
    }

    public override void TearDown()
    {
        base.TearDown();
        if (File.Exists(this.path)) File.Delete(this.path);
    }
}
```

Rejected alternatives (from brainstorming): NUnit generic `[TestFixture(typeof(...))]`
(less idiomatic given engines need meaningfully different setup — SQL Server's
future flavor needs a live-connection skip-guard, SQLite needs a temp file, the
mock needs neither) and a single class with `[TestCaseSource]` factory
parameters (messier per-engine setup/teardown, worse test-explorer output).
The abstract-base approach also matches an existing precedent in this
codebase: `SqlServerStoredProcDatabaseTests.cs` already self-skips when no SQL
Server connection string is available via environment variable — each
concrete subclass here can carry that same kind of per-engine skip-guard
independently (relevant once slice 2 adds the SQL Server flavor), without
polluting the shared test bodies.

Assertions are scoped to **post-reload state**, not "did the original
in-memory object's flags mutate in place after `Save()`." That in-place
behavior is engine-specific, not part of the `IDatabase` contract:
`SqlServerDatabase`'s SQL-based `UpdateXxx` methods call `.OnUpdated()` per
item during `Save()`, clearing dirty flags on the original objects, but
`XmlStore.Save()` does not (confirmed by reading its source — it calls
`PrepareSave()` for `RemoveDeleted()` bookkeeping only, never `OnUpdated()`).
Testing "save, then reload, then verify the *reloaded* graph has clean state
and correct data" is true and comparable across every engine; testing
"the original object's flags changed after Save() without a reload" is not,
and the suite must not assert it.

#### Ruling: `XmlStore.Save()` is not being changed to call `OnUpdated()`

Raised and investigated during design review: could this asymmetry just be
fixed at the source, so every `IDatabase` implementation left the graph in
the same "clean" state after a successful save, removing the need for the
post-reload-only carve-out above?

**Root cause of the asymmetry.** `SqlServerDatabase`'s `UpdateXxx` methods are
*incremental* — each row's SQL statement is decided by inspecting that item's
`IsChanged`/`IsInserted`/`IsDeleted` flags, so clearing them via `OnUpdated()`
afterward is load-bearing: without it, an already-saved, unchanged row would
look "changed" forever and get re-sent on every subsequent save. `XmlStore.Save()`
is not incremental — it serializes the entire graph unconditionally every
time and never consults those flags for its own logic (only `RemoveDeleted()`,
which is `IsDeleted`-specific). `OnUpdated()` is necessary for one family and
genuinely inert for the other; that's why only one calls it today.

**What was checked before deciding.**
- No override of `PersistentObject.OnUpdated()` exists anywhere in the domain
  model, and the base implementation only flips the `change` enum — it never
  calls `FireChangeEvent(...)` the way `OnInserted()`/`OnDelete()` do. So
  adding the call would not have triggered any change-notification side
  effects.
- The app's actual "unsaved changes" indicator (title bar, exit-save-prompt
  via `MainWindow.SaveIfDirty()`) is driven by `MainWindow`'s own independent
  `dirty` bool, explicitly cleared by `SetDirty(false)` right after `Save()`
  succeeds — it does not read individual `PersistentObject.IsChanged` flags
  at all. The asymmetry has no effect on that behavior either way.
- No test or UI code was found asserting `IsChanged`/`IsInserted` state
  specifically after an `XmlStore.Save()` call.

**Decision: leave `XmlStore` unchanged.** Even though the change looked
low-risk by the checks above, three things argue against making it as part of
this work:
1. **Real, uncompensated perf cost.** Walking every container (Accounts,
   Categories, Payees, Transactions + nested Splits, Securities, StockSplits,
   LoanPayments, OnlineAccounts, Aliases, AccountAliases, TransactionExtras,
   ...) to call `OnUpdated()` is an O(n) pass over the whole graph that
   `XmlStore.Save()` does not currently pay, on a path that already tracks and
   logs its own elapsed time (`Debug.WriteLine("Saved XML store in ...")`),
   suggesting save latency here is an existing, live concern, not a
   hypothetical one.
2. **A second list to keep in sync.** That container walk would duplicate,
   in a second file, the same enumeration `SqlServerDatabase`'s save logic
   already walks in `SqlDatabase.cs` — two independent lists that can
   silently drift as new container types are added, the same class of risk
   issue #11's self-testing MSBuild guard was written to catch elsewhere.
3. **Scope discipline.** `XmlStore` is a real, shipping production
   `IDatabase` implementation (the legacy `.xml` money-file format), not
   test-only code. Changing its behavior — even a change that checks out as
   safe today — is a different risk class than anything else in this spec,
   and doesn't belong bundled into a test-support-library change.

The inconsistency is real but currently inert (nothing depends on it). Fixing
it buys conceptual tidiness, not correctness. If ever wanted, it's a small,
well-bounded, separately-tracked follow-up — not part of this work.

### 2. `MockDatabase`: real serialization round-trip against memory, not object-identity passthrough

If `MockDatabase.Save()` just remembered the `MyMoney` reference it was given
and `Load()` handed the same reference back, tests could pass on object-identity
coincidence in ways that would fail against every real engine (which always
constructs a fresh object graph from rows on `Load()`). To avoid that, `MockDatabase`
performs a genuine serialize/deserialize round-trip in memory:

```csharp
public class MockDatabase : IDatabase
{
    private byte[] snapshot;

    public void Create() { this.snapshot = null; }

    public bool Exists => this.snapshot != null;

    public void Save(MyMoney money)
    {
        XmlStore.PrepareSave(money); // reuse: RemoveDeleted() across every container
        var serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream))
        {
            serializer.WriteObject(writer, money);
        }
        this.snapshot = stream.ToArray();
    }

    public MyMoney Load(IStatusService status)
    {
        if (this.snapshot == null) return new MyMoney();
        var serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
        using var stream = new MemoryStream(this.snapshot);
        using var reader = XmlReader.Create(stream);
        return (MyMoney)serializer.ReadObject(reader);
    }

    public DbFlavor DbFlavor => DbFlavor.Mock; // new enum value, see below

    // Server/DatabasePath/UserId/Password/BackupPath/SupportsUserLogin/
    // UpgradeRequired/Upgrade()/Delete()/GetLog()/QueryDataSet()/Disconnect():
    // trivial in-memory fields or no-ops, matching how SqliteDatabase treats
    // the members it doesn't meaningfully use.
}
```

This reuses `XmlStore.PrepareSave(MyMoney)` — an existing, already-correct,
already-tested helper (it's what `XmlStore.Save()` itself calls) — rather than
hand-rolling a second copy of the dirty-tracking cleanup sequence that could
drift out of sync with the real one. `PrepareSave` is currently `internal` to
`MyMoney.Data`; this spec promotes it to `public` (pure visibility change, no
behavior change) so `MockDatabase` — living in a separate assembly, see
Placement below — can call it. `DataContractSerializer` and `MyMoney.GetKnownTypes()`
are already used by `XmlStore` for exactly this purpose, so the domain model's
serializability is proven, not a new assumption.

New `DbFlavor.Mock` enum value (in `MyMoney.Business/IDatabase.cs`) rather than
reusing `DbFlavor.None` — `DbFlavor` is documented as "identifies which storage
engine an `IDatabase` implementation talks to," and the mock is a genuine
distinct engine for this purpose, not the absence of one.

### 3. Placement: new `MyMoney.TestSupport` project

A new class library, `Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj`,
holding `MockDatabase`, `DatabaseContractTests` (the abstract base), and the
`MockDatabaseContractTests`/`SqliteDatabaseContractTests` concrete fixtures.
References `MyMoney.Business` and `MyMoney.Data` (for `XmlStore.PrepareSave`
and `SqliteDatabase`), plus NUnit (for the `[TestFixture]`/`[Test]` attributes
on the shared classes themselves — the abstract base and its concrete
subclasses are real, runnable test fixtures, not merely helpers referenced
*by* test projects).

`Source/WPF/UnitTests/UnitTests.csproj` gets a `ProjectReference` to this new
project so `dotnet test UnitTests.csproj` picks up and runs
`MockDatabaseContractTests`/`SqliteDatabaseContractTests` alongside its
existing tests. This also makes `MockDatabase` available to `UITests.csproj`
or any future test project without duplicating it, matching item #3's framing
as "a library," not just more files inside `UnitTests`.

## Data Flow

```
DatabaseContractTests.SaveAndReload_PreservesAccountAndTransactionData()
  -> this.CreateDatabase()                (subclass-specific: MockDatabase, or
                                            SqliteDatabase pointed at a temp file)
  -> build a small MyMoney graph in memory (account, category, payee, transaction)
  -> this.Database.Save(money)
  -> reloaded = this.Database.Load(null)
  -> assert reloaded's data matches what was saved, via independent lookups
     (FindAccount, GetTransactionsFrom, etc.) - never via reference equality
     to the original `money` object
```

## Error Handling

- `MockDatabase.Load()` on a `Create()`-only, never-`Save()`d instance returns
  a fresh empty `MyMoney` (matching every real engine's "new database" behavior).
- `SqliteDatabaseContractTests.TearDown()` deletes its temp file even if a test
  fails (`[TearDown]` always runs); the temp file uses a `Guid`-based name so
  parallel/repeated runs never collide.
- No engine-unavailability skip-guard needed in this slice — both flavors
  (mock, SQLite-via-temp-file) are always available in any environment. That
  concern only arrives with slice 2's SQL Server flavor.

## Testing (verifying this spec's own work)

- The 4 shared contract tests must pass identically against both flavors.
- A one-time sanity check (mirroring the layer-extraction plan's own "prove
  the tripwire is real" discipline): temporarily make `MockDatabase.Load()`
  return the same object reference `Save()` was given (the naive, rejected
  design), confirm at least one contract test now passes for the *wrong*
  reason or a subsequent independent-lookup assertion would fail to catch a
  bug it should — then revert. This proves the suite is actually exercising a
  real round-trip, not coincidentally passing either way.
- Full existing suite (`dotnet test UnitTests.csproj`) must keep passing
  unchanged alongside the new fixtures.
- `SqlMappingTests.cs` is left untouched (its schema-migration-specific
  coverage is out of scope, per Non-Goals) but should still pass.
EOF
