# Business-Layer Test-Support Library (Item #3 Slice 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a shared `IDatabase` contract test suite (save/reload data integrity + `PersistentObject` dirty-tracking lifecycle) that runs identically against an in-memory `MockDatabase` (fast, zero I/O) and the real `SqliteDatabase` (temp-file-backed), proving one set of test bodies is genuinely engine-agnostic.

**Architecture:** New `MyMoney.TestSupport` class library holds `MockDatabase` (a real `DataContractSerializer`/`MemoryStream` round-trip, not an object-identity passthrough) plus an abstract `DatabaseContractTests` NUnit fixture base class with the shared test bodies. Two concrete subclasses (`MockDatabaseContractTests`, `SqliteDatabaseContractTests`) each supply their own `IDatabase` instance via `CreateDatabase()`. `UnitTests.csproj` references the new project so `dotnet test` picks up both fixtures.

**Tech Stack:** .NET 10 (`net10.0-windows7.0`), NUnit 4.6.1, `System.Runtime.Serialization.DataContractSerializer` (already used by the existing `XmlStore` engine for the same purpose).

**Spec:** `docs/superpowers/specs/2026-09-15-test-support-library-design.md`

## Global Constraints

- Namespaces follow the existing convention: each project's `RootNamespace` is `Walkabout`; individual files declare their own deeper namespace explicitly (e.g. `namespace Walkabout.Data`, `namespace Walkabout.TestSupport`) — matching `MyMoney.Business.csproj`/`MyMoney.Data.csproj`.
- Contract-suite assertions are scoped to **post-reload state only** (verified via independent lookups like `FindAccount(...)`, never reference equality to the object passed into `Save()`). Do not assert on the original in-memory object's `IsChanged`/`IsInserted` flags after `Save()` — that behavior is engine-specific (see spec's "Ruling" section) and out of scope.
- `XmlStore.cs` is not modified except promoting one existing method's visibility (`PrepareSave`: `internal` → `public`). No behavior change to `XmlStore` itself.
- SQL Server is not a flavor in this plan (that's slice 2, tracked as GitHub issue #19). Only `MockDatabase` and `SqliteDatabase`.
- The existing full test suite (32 passed, 1 skipped, 0 failed as of this plan's baseline) must keep passing unchanged after every task.
- `SqlMappingTests.cs` is not modified (its SQLite schema-migration coverage is out of scope per the spec's Non-Goals).

---

### Task 1: `MyMoney.TestSupport` project + `MockDatabase`

**Files:**
- Modify: `Source/WPF/MyMoney.Data/XmlStore.cs` (line ~147: `internal static void PrepareSave` → `public static void PrepareSave`)
- Modify: `Source/WPF/MyMoney.Business/IDatabase.cs` (add `Mock` to the `DbFlavor` enum)
- Create: `Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj`
- Create: `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`
- Create: `Source/WPF/MyMoney.TestSupport/MockDatabaseSmokeTests.cs`
- Modify: `Source/WPF/MyMoney.sln` (add new project)
- Modify: `Source/WPF/UnitTests/UnitTests.csproj` (add `ProjectReference`)

**Interfaces:**
- Produces: `Walkabout.Data.MockDatabase : IDatabase` (public, parameterless constructor) — Task 2 and Task 3 construct it directly.
- Produces: `Walkabout.Data.DbFlavor.Mock` (new enum value).
- Produces: `Walkabout.Data.XmlStore.PrepareSave(MyMoney money)` now public — Task 1's own `MockDatabase.Save()` calls it; no other task needs to call it directly.

- [ ] **Step 1: Promote `XmlStore.PrepareSave` to public**

In `Source/WPF/MyMoney.Data/XmlStore.cs`, find:

```csharp
        internal static void PrepareSave(MyMoney money)
```

Change to:

```csharp
        public static void PrepareSave(MyMoney money)
```

No other change to the method body.

- [ ] **Step 2: Add `DbFlavor.Mock`**

In `Source/WPF/MyMoney.Business/IDatabase.cs`, find:

```csharp
    public enum DbFlavor
    {
        None,
        SqlServer,
        SqlCE,
        Sqlite,
        Xml,
        BinaryXml
    }
```

Change to:

```csharp
    public enum DbFlavor
    {
        None,
        SqlServer,
        SqlCE,
        Sqlite,
        Xml,
        BinaryXml,
        Mock
    }
```

- [ ] **Step 3: Build to confirm the two changes above compile with no other effect**

Run: `dotnet build Source/WPF/MyMoney.sln -v:q`
Expected: `Build succeeded.`, `0 Error(s)`, same warning count as before this task (these are pure additive/visibility changes — no new warnings, no new errors).

- [ ] **Step 4: Create the `MyMoney.TestSupport` project file**

Create `Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>Walkabout</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
    <ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="NUnit" Version="4.6.1" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
  </ItemGroup>
</Project>
```

`Microsoft.NET.Test.Sdk` and `NUnit3TestAdapter` (not just the bare `NUnit`
assertion library) are required for `dotnet test` to discover and run tests
in this project directly, matching `UnitTests.csproj`'s own package set -
every later step in this plan runs `dotnet test` against
`MyMoney.TestSupport.csproj` directly, so this project must be independently
test-runnable, not just referenced by another test project.

- [ ] **Step 5: Implement `MockDatabase`**

Create `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`:

```csharp
using System.Data;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// A fast, in-memory IDatabase implementation for tests. Performs a real
    /// DataContractSerializer round-trip against a MemoryStream on Save/Load
    /// (reusing XmlStore.PrepareSave for dirty-tracking bookkeeping) rather
    /// than passing the same MyMoney reference through, so tests exercise a
    /// genuine round-trip instead of passing on object-identity coincidence.
    /// See docs/superpowers/specs/2026-09-15-test-support-library-design.md.
    /// </summary>
    public class MockDatabase : IDatabase
    {
        private byte[] snapshot;

        public string Server => "mock";
        public string DatabasePath => "mock://in-memory";
        public string UserId => null;
        public string Password { get; set; }
        public string BackupPath => null;
        public bool SupportsUserLogin => false;
        public DbFlavor DbFlavor => DbFlavor.Mock;
        public bool Exists => this.snapshot != null;
        public bool UpgradeRequired => false;

        public void Create()
        {
            this.snapshot = null;
        }

        public void Save(MyMoney money)
        {
            XmlStore.PrepareSave(money);
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
            using (MemoryStream stream = new MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream))
                {
                    serializer.WriteObject(writer, money);
                }
                this.snapshot = stream.ToArray();
            }
        }

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
                return (MyMoney)serializer.ReadObject(reader);
            }
        }

        public void Backup(string path)
        {
            if (this.snapshot != null)
            {
                File.WriteAllBytes(path, this.snapshot);
            }
        }

        public void Upgrade()
        {
        }

        public void Delete()
        {
            this.snapshot = null;
        }

        public string GetLog()
        {
            return string.Empty;
        }

        public DataSet QueryDataSet(string cmd)
        {
            return null;
        }

        public void Disconnect()
        {
        }
    }
}
```

- [ ] **Step 6: Write a smoke test proving the round-trip mechanics work**

Create `Source/WPF/MyMoney.TestSupport/MockDatabaseSmokeTests.cs`:

```csharp
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    [TestFixture]
    public class MockDatabaseSmokeTests
    {
        [Test]
        public void Load_BeforeAnySave_ReturnsEmptyMyMoney()
        {
            MockDatabase db = new MockDatabase();
            db.Create();

            MyMoney money = db.Load(null);

            Assert.That(money, Is.Not.Null);
            Assert.That(money.Accounts.Count, Is.EqualTo(0));
        }

        [Test]
        public void SaveThenLoad_ReturnsADifferentObjectInstance()
        {
            MockDatabase db = new MockDatabase();
            db.Create();
            MyMoney original = new MyMoney();
            original.Accounts.AddAccount("Checking");

            db.Save(original);
            MyMoney reloaded = db.Load(null);

            // Proves this is a real round-trip, not an object-identity
            // passthrough - a passthrough would return the exact same
            // reference, which would defeat the point of the contract suite.
            Assert.That(reloaded, Is.Not.SameAs(original));
            Assert.That(reloaded.Accounts.FindAccount("Checking"), Is.Not.Null);
        }
    }
}
```

- [ ] **Step 7: Add the new project to the solution**

Run: `dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj`

- [ ] **Step 8: Reference the new project from `UnitTests.csproj`**

This makes `MockDatabase` available for any test written directly in
`UnitTests.csproj` in the future. It does **not** make `dotnet test
UnitTests.csproj` also run `MyMoney.TestSupport`'s own `[TestFixture]`
classes - VSTest/NUnit test discovery is scoped to the specific entry
assembly a `dotnet test` invocation targets, not every assembly referenced
by it, even though the referenced assembly's DLL is copied into the same
output folder. `MyMoney.TestSupport`'s own tests are run by pointing
`dotnet test` at `MyMoney.TestSupport.csproj` directly, as every step in this
plan does - see Task 4, Step 5 for this distinction spelled out again at the
point it matters for the final verification pass.

In `Source/WPF/UnitTests/UnitTests.csproj`, find:

```xml
  <ItemGroup>
    <ProjectReference Include="..\MyMoney\MyMoney.csproj" />
    <ProjectReference Include="..\MyMoneyAdmin\MyMoneyAdmin.csproj" />
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
    <ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
  </ItemGroup>
```

Change to:

```xml
  <ItemGroup>
    <ProjectReference Include="..\MyMoney\MyMoney.csproj" />
    <ProjectReference Include="..\MyMoneyAdmin\MyMoneyAdmin.csproj" />
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
    <ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
    <ProjectReference Include="..\MyMoney.TestSupport\MyMoney.TestSupport.csproj" />
  </ItemGroup>
```

- [ ] **Step 9: Build and run the new smoke tests**

Run: `dotnet build Source/WPF/MyMoney.sln -v:q`
Expected: `Build succeeded.`, `0 Error(s)`.

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj -v:q`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 10: Run the full existing suite to confirm no regressions**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj -v:q`
Expected: `Passed!` with the same 32 passed / 1 skipped / 0 failed baseline (the new project isn't referenced by any existing test, so nothing should change here yet).

- [ ] **Step 11: Commit**

```bash
git add Source/WPF/MyMoney.Data/XmlStore.cs Source/WPF/MyMoney.Business/IDatabase.cs Source/WPF/MyMoney.TestSupport Source/WPF/MyMoney.sln Source/WPF/UnitTests/UnitTests.csproj
git commit -m "Add MyMoney.TestSupport project with MockDatabase (item #3 slice 1, Task 1)"
```

---

### Task 2: Shared `DatabaseContractTests` base class + `MockDatabaseContractTests`

**Files:**
- Create: `Source/WPF/MyMoney.TestSupport/DatabaseContractTests.cs`
- Create: `Source/WPF/MyMoney.TestSupport/MockDatabaseContractTests.cs`

**Interfaces:**
- Consumes: `Walkabout.Data.MockDatabase` (Task 1, parameterless constructor), `IDatabase.Create()/Save()/Load()/Disconnect()` (existing interface).
- Produces: `Walkabout.TestSupport.DatabaseContractTests` (abstract, `protected abstract IDatabase CreateDatabase()`) — Task 3's `SqliteDatabaseContractTests` subclasses this.

- [ ] **Step 1: Write the abstract base class with all four shared tests**

Create `Source/WPF/MyMoney.TestSupport/DatabaseContractTests.cs`:

```csharp
using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    /// <summary>
    /// Shared IDatabase contract tests: save/reload data integrity and the
    /// PersistentObject dirty-tracking lifecycle (Insert/Update/Delete),
    /// expressed purely in terms of IDatabase so the same test bodies run
    /// against every backing-store flavor. Assertions are scoped to
    /// post-reload state only - see this plan's Global Constraints and the
    /// design spec's "Ruling" section for why.
    /// </summary>
    public abstract class DatabaseContractTests
    {
        protected IDatabase Database { get; private set; }

        [SetUp]
        public virtual void SetUp()
        {
            this.Database = this.CreateDatabase();
            this.Database.Create();
        }

        [TearDown]
        public virtual void TearDown()
        {
            this.Database?.Disconnect();
        }

        protected abstract IDatabase CreateDatabase();

        private static MyMoney BuildSampleMoney()
        {
            MyMoney money = new MyMoney();
            Category category = money.Categories.GetOrCreateCategory("Auto:Gas", CategoryType.Expense);
            Payee payee = money.Payees.FindPayee("Shell", true);
            Account account = money.Accounts.AddAccount("Checking");
            Transaction t = money.Transactions.NewTransaction(account);
            t.Date = new DateTime(2026, 1, 15);
            t.Amount = -42.50m;
            t.Payee = payee;
            t.Category = category;
            t.Memo = "Fill-up";
            money.Transactions.AddTransaction(t);
            return money;
        }

        [Test]
        public void SaveAndReload_PreservesAccountAndTransactionData()
        {
            MyMoney money = BuildSampleMoney();

            this.Database.Save(money);
            MyMoney reloaded = this.Database.Load(null);

            Account account = reloaded.Accounts.FindAccount("Checking");
            Assert.That(account, Is.Not.Null);

            var transactions = reloaded.Transactions.GetTransactionsFrom(account);
            Assert.That(transactions.Count, Is.EqualTo(1));
            Transaction t = transactions[0];
            Assert.That(t.Amount, Is.EqualTo(-42.50m));
            Assert.That(t.Memo, Is.EqualTo("Fill-up"));
            Assert.That(t.Payee, Is.Not.Null);
            Assert.That(t.Payee.Name, Is.EqualTo("Shell"));
            Assert.That(t.Category, Is.Not.Null);
            Assert.That(t.Category.Name, Is.EqualTo("Auto:Gas"));
        }

        [Test]
        public void Insert_ThenReload_ItemAppearsWithCleanState()
        {
            MyMoney money = BuildSampleMoney();
            this.Database.Save(money);

            MyMoney reloaded = this.Database.Load(null);
            reloaded.Accounts.AddAccount("Savings");
            this.Database.Save(reloaded);

            MyMoney reloadedAgain = this.Database.Load(null);
            Account found = reloadedAgain.Accounts.FindAccount("Savings");
            Assert.That(found, Is.Not.Null);
            Assert.That(found.IsInserted, Is.False);
            Assert.That(found.IsChanged, Is.False);
        }

        [Test]
        public void Update_ThenReload_ChangePersisted()
        {
            MyMoney money = BuildSampleMoney();
            this.Database.Save(money);

            MyMoney reloaded = this.Database.Load(null);
            Account account = reloaded.Accounts.FindAccount("Checking");
            account.Description = "Updated description";
            this.Database.Save(reloaded);

            MyMoney reloadedAgain = this.Database.Load(null);
            Account found = reloadedAgain.Accounts.FindAccount("Checking");
            Assert.That(found.Description, Is.EqualTo("Updated description"));
        }

        [Test]
        public void Delete_ThenReload_ItemIsGone()
        {
            MyMoney money = BuildSampleMoney();
            money.Categories.GetOrCreateCategory("ToDelete", CategoryType.Expense);
            this.Database.Save(money);

            MyMoney reloaded = this.Database.Load(null);
            Category toDelete = reloaded.Categories.FindCategory("ToDelete");
            Assert.That(toDelete, Is.Not.Null);
            reloaded.Categories.RemoveCategory(toDelete);
            this.Database.Save(reloaded);

            MyMoney reloadedAgain = this.Database.Load(null);
            Category shouldBeGone = reloadedAgain.Categories.FindCategory("ToDelete");
            Assert.That(shouldBeGone, Is.Null);
        }
    }
}
```

- [ ] **Step 2: Write the concrete mock fixture**

Create `Source/WPF/MyMoney.TestSupport/MockDatabaseContractTests.cs`:

```csharp
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
    }
}
```

- [ ] **Step 3: Run the new fixture**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj -v:q`
Expected: `Passed!  - Failed: 0, Passed: 6, Skipped: 0, Total: 6` (2 smoke tests from Task 1 + 4 new contract tests).

If any test fails, read the failure message before changing anything - the most likely causes are: (a) a factory method name mismatch (double check `GetOrCreateCategory`, `FindPayee`, `AddAccount`, `NewTransaction`, `AddTransaction`, `RemoveCategory`, `FindAccount`, `FindCategory`, `GetTransactionsFrom` against `Source/WPF/MyMoney.Business/Money.cs`), or (b) `CategoryType`/`Account`/`Category`/`Payee`/`Transaction` needing an explicit `using Walkabout.Data;` if IntelliSense/the compiler reports them unresolved (all of Money.cs's domain types live in that namespace).

- [ ] **Step 4: Run the full existing suite to confirm no regressions**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj -v:q`
Expected: unchanged baseline, 32 passed / 1 skipped / 0 failed (still nothing in `UnitTests.csproj` itself references the new fixtures yet - that's fine, this task's own project runs them directly).

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney.TestSupport/DatabaseContractTests.cs Source/WPF/MyMoney.TestSupport/MockDatabaseContractTests.cs
git commit -m "Add shared DatabaseContractTests suite, run against MockDatabase (item #3 slice 1, Task 2)"
```

---

### Task 3: `SqliteDatabaseContractTests` (second engine flavor)

**Files:**
- Create: `Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs`

**Interfaces:**
- Consumes: `Walkabout.TestSupport.DatabaseContractTests` (Task 2, abstract base — this task's entire job is subclassing it), `Walkabout.Data.SqliteDatabase` (existing, in `MyMoney.Data`, `DatabasePath` settable property + `Create()`).

- [ ] **Step 1: Write the concrete SQLite fixture**

Create `Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs`:

```csharp
using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    [TestFixture]
    public class SqliteDatabaseContractTests : DatabaseContractTests
    {
        private string path;

        protected override IDatabase CreateDatabase()
        {
            this.path = Path.Combine(Path.GetTempPath(), $"ContractTest_{Guid.NewGuid():N}.mmdb");
            return new SqliteDatabase { DatabasePath = this.path };
        }

        public override void TearDown()
        {
            base.TearDown();
            if (this.path != null && File.Exists(this.path))
            {
                File.Delete(this.path);
            }
        }
    }
}
```

Note `SetUp()` (inherited, unchanged from the base class) already calls
`this.Database.Create()` after `CreateDatabase()` returns - this is what
actually creates the `.mmdb` file and its schema on disk, matching how
`SqlMappingTests.cs` uses `SqliteDatabase` today.

- [ ] **Step 2: Run the new fixture**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj -v:q`
Expected: `Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10` (6 from Tasks 1-2 + 4 new contract tests, now proven against a second engine).

- [ ] **Step 3: Confirm the temp files are actually being cleaned up**

Run (before the test run, note the count; after, confirm it's unchanged):
`ls $env:TEMP\ContractTest_*.mmdb 2>$null | Measure-Object | Select-Object -ExpandProperty Count` (PowerShell) or
`ls /tmp/ContractTest_*.mmdb 2>/dev/null | wc -l` if running under the Bash tool's environment.
Expected: `0` after the test run completes (each fixture instance's own file is deleted in `TearDown`).

- [ ] **Step 4: Run the full existing suite to confirm no regressions**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj -v:q`
Expected: unchanged baseline, 32 passed / 1 skipped / 0 failed.

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney.TestSupport/SqliteDatabaseContractTests.cs
git commit -m "Add SqliteDatabaseContractTests, prove the shared suite is engine-agnostic (item #3 slice 1, Task 3)"
```

---

### Task 4: Prove the suite is a real tripwire, wire it into `UnitTests`, final regression pass

**Files:**
- Modify (temporarily, then revert): `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`
- No permanent file changes in this task beyond verification - `UnitTests.csproj` already references `MyMoney.TestSupport` (Task 1, Step 8), so no additional wiring is needed here; this task is entirely about proving correctness and running the full regression pass.

**Interfaces:**
- Consumes: everything from Tasks 1-3. Produces nothing new - this is the plan's closing verification task.

- [ ] **Step 1: Temporarily misconfigure the serializer to prove the suite depends on real serialization correctness**

In `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`, find both occurrences of:

```csharp
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
```

Temporarily change **both** to (omitting known-types registration - a realistic
misconfiguration bug a naive implementation might make, since `Account`,
`Category`, `Payee`, `Transaction`, etc. are polymorphic members reachable
from `MyMoney` and require this registration to serialize/deserialize
correctly):

```csharp
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney));
```

- [ ] **Step 2: Run the contract suite and confirm it fails**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj -v:q`
Expected: at least one of the 4 shared `DatabaseContractTests` fails for
`MockDatabaseContractTests` specifically (the `SqliteDatabaseContractTests`
instances are unaffected, since `SqliteDatabase` doesn't use this serializer
at all - only the mock's tests should regress). This proves the suite
actually exercises meaningful serialization correctness rather than passing
regardless of how `MockDatabase` is implemented.

If nothing fails: STOP. Do not proceed or revert yet - this means the
contract suite isn't actually testing what it's supposed to, which is a real
problem with Task 2's test bodies, not with this step. Re-examine which
assertions in `DatabaseContractTests.cs` should have caught a serialization
failure and why they didn't.

- [ ] **Step 3: Revert the temporary misconfiguration**

In `Source/WPF/MyMoney.TestSupport/MockDatabase.cs`, change both occurrences
back to:

```csharp
            DataContractSerializer serializer = new DataContractSerializer(typeof(MyMoney), MyMoney.GetKnownTypes());
```

Run: `git diff Source/WPF/MyMoney.TestSupport/MockDatabase.cs`
Expected: no output (file identical to the last commit - confirms the revert is complete and clean, nothing accidentally left half-changed).

- [ ] **Step 4: Run the contract suite again to confirm it's back to green**

Run: `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj -v:q`
Expected: `Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10`

- [ ] **Step 5: Full solution rebuild and full existing test suite**

Run: `dotnet build Source/WPF/MyMoney.sln -v:q -t:Rebuild`
Expected: `Build succeeded.`, `0 Error(s)`, no new warnings versus this plan's baseline.

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj -v:q`
Expected: 32 passed / 1 skipped / 0 failed, unchanged from baseline (the new
project's tests are picked up by `MyMoney.TestSupport.csproj`'s own test run,
not duplicated into `UnitTests.csproj`'s count, since `UnitTests.csproj`'s
`ProjectReference` to `MyMoney.TestSupport.csproj` makes `MockDatabase`
available for future use there without NUnit re-discovering
`MyMoney.TestSupport`'s own `[TestFixture]` classes a second time under a
different test host).

- [ ] **Step 6: Report final state**

No commit needed if Step 3's `git diff` was clean (nothing changed since
Task 3's commit). Confirm and state plainly: full rebuild 0 errors, all three
test surfaces green (`MyMoney.TestSupport.csproj` 10/10, `UnitTests.csproj`
32/1/0 unchanged), the tripwire in Steps 1-2 proved the suite depends on
real, correctly-configured serialization rather than passing coincidentally.

- [ ] **Step 7: Update GitHub issue #20 and close it**

```bash
gh issue comment 20 --repo markabrandjord/MyMoney.Net --body "Implemented (Tasks 1-4, commits <list them>). MyMoney.TestSupport project holds MockDatabase (real DataContractSerializer/MemoryStream round-trip) and the shared DatabaseContractTests suite, run against both MockDatabase and SqliteDatabase. Verified the suite is a real tripwire (temporarily broke serialization, confirmed a test failed, reverted). Full rebuild 0 errors, MyMoney.TestSupport 10/10, UnitTests.csproj unchanged at 32/1/0."
gh issue close 20 --repo markabrandjord/MyMoney.Net
```
EOF
