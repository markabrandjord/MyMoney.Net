# Layer Extraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `MyMoney.csproj` into `MyMoney.Business` (domain model, WPF-free) and `MyMoney.Data` (storage implementations, WPF-free), leaving `MyMoney.csproj` as a thin WPF consumer of both.

**Architecture:** `MyMoney.Business` has no project references and contains `Money.cs` plus the smaller domain-adjacent types. `MyMoney.Data` references `MyMoney.Business` and contains the six `IDatabase` implementations plus item #2's SQL Server engine-selection classes, with a new `IDataLayerUiCallback` interface replacing the two direct UI calls found inside it. `MyMoney.csproj` references both, keeps everything else (Views, Reports, Importers, StockQuote fetchers, `DataEngineStartup`, `MoneyDataObject`), and supplies the real WPF-backed implementations of the small interfaces the lower layers now declare instead of calling WPF directly.

**Tech Stack:** .NET 10 (`net10.0-windows7.0`), C#, MSTest/NUnit (existing `UnitTests.csproj` conventions), FlaUI (existing `UITests.csproj`, reused for regression verification only).

**Spec:** `docs/superpowers/specs/2026-09-15-layer-extraction-design.md`

## Global Constraints

- Both new projects target `net10.0-windows7.0`, matching `MyMoney.csproj` (spec: TargetFramework).
- `MyMoney.Business.csproj` has zero project references and forbids `PresentationFramework`/`PresentationCore` (true UI-rendering assemblies). It MAY reference `WindowsBase` — Task 1 discovered the domain model's change-notification backbone (`EventHandlerCollection<T>`/`UiDispatcher`) has a load-bearing dependency on `System.Windows.Threading.Dispatcher`/`DependencyObject` for cross-thread event marshaling, used throughout `PersistentObject`/`PersistentContainer` including background-thread paths. Rewriting that plumbing to drop the dependency entirely was rejected as a real behavioral risk to production financial-data threading code given the current testing environment can't safely validate it, not a mechanical move (Ruling, 2026-09-15 — see ledger). True cross-platform portability (dropping `WindowsBase` too) stays deferred to item #5, consistent with the spec's existing `SqlCeDatabase` precedent. Tracked for later revisit once testing improves: [GitHub issue #7](https://github.com/markabrandjord/MyMoney.Net/issues/7).
- `MyMoney.Data.csproj` references only `MyMoney.Business.csproj`, plus its own package references; zero direct WPF assembly references of its own (including `WindowsBase` — nothing in `MyMoney.Data`'s own file list needs it; it only reaches `MyMoney.Business`'s `WindowsBase` dependency transitively through the `ProjectReference`).
- Namespaces are preserved exactly as they exist today (`Walkabout.Data`, `Walkabout.Utilities`, `Walkabout.StockQuotes`) — this is a project-boundary move, not a rename.
- File moves use `git mv` so history is preserved.
- The full existing test suite (currently 29 passed, 1 skipped, 0 failed) must keep passing, unchanged, after every task that touches build output.
- No new inline/mock business-logic unit tests beyond the portability-boundary tests in Task 6 — deeper test coverage is item #3's scope, not this plan's (spec: Non-Goals).
- `Importers/`, `Ofx/`, `Reports/`, `Charts/`, `Taxes/`, `ScenarioTest.csproj`'s own test logic, and the StockQuote fetch/throttle machinery (`StockQuoteManager`, `Yahoo`/`Polygon`/`TwelveData`/`MarketStack`, throttling) are never modified by this plan (spec: Non-Goals).

---

### Task 1: Create MyMoney.Business and move the domain-model file set

**Files:**
- Create: `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`
- Move (via `git mv`) from `Source/WPF/MyMoney/Database/` to `Source/WPF/MyMoney.Business/`:
  `Money.cs`, `IDatabase.cs`, `Mapping.cs`, `Query.cs`, `AutoCategorization.cs`,
  `CostBasis.cs`, `Encryption.cs`, `AsyncSqlQuery.cs`, `Payments.cs`,
  `Money_Loans.cs`, `DatabaseSettings.cs`
- Move (via `git mv`) from `Source/WPF/MyMoney/StockQuotes/` to `Source/WPF/MyMoney.Business/StockQuotes/`:
  `StockQuoteCache.cs`, `IStockQuoteService.cs`
- Move (via `git mv`) `Source/WPF/MyMoney/Utilities/UsHolidays.cs` to `Source/WPF/MyMoney.Business/UsHolidays.cs`
- Modify: `Source/WPF/MyMoney.sln` (add the new project)

**Interfaces:**
- Produces: every public type in `Walkabout.Data` currently defined in the moved files (`MyMoney`, `Accounts`, `Account`, `Transactions`, `Transaction`, `Categories`, `Category`, `Securities`, `Security`, `Payees`, `Payee`, `Splits`, `RentBuildings`, `PersistentObject`, `PersistentContainer`, `IDatabase`, `CostBasisCalculator`, etc.) plus `Walkabout.StockQuotes.StockQuoteCache`/`IStockQuoteService`/`StockQuoteHistory`/`StockQuote`/`DateRange`/`DownloadCompleteEventArgs` and `Walkabout.Utilities.UsHolidays` — these are what Task 2, Task 3, and Task 4 build against.

> **Amendment (2026-09-15, mid-execution):** Steps 1-5 below are already committed (`887605c`). The implementer's standalone build (Step 6) then failed on 9 errors from 3 transitive-dependency gaps the design's per-file grep missed. The implementer independently found and fixed several clean transitive dependencies (moved `Hashset.cs`, `KNearestNeighbor.cs`, `IStatusService.cs`, `QuickFilterParser.cs`, `FilteredObservableCollection.cs`, `SimpleGraph.cs` into a new `MyMoney.Business/Utilities/` subfolder; extracted the `DbFlavor` enum out of `SqlDatabase.cs` into `IDatabase.cs`; added a `Microsoft.Data.SqlClient` `PackageReference` for `AsyncSqlQuery.cs`) — all already committed in `887605c` too, and all correct, no further action needed on those. Two real blockers remained and are resolved by Ruling (ledger, 2026-09-15): **Steps 2a and 2b below are the remaining work for this task.**

- [ ] **Step 2a: Move the event-marshaling backbone, allow WindowsBase (Ruling A)**

`Money.cs`/`AsyncSqlQuery.cs` need `Walkabout.Utilities.EventHandlerCollection<T>` (`Utilities/EventHandlerCollection.cs`), which has a load-bearing dependency on `System.Windows.Threading.Dispatcher`/`DependencyObject` via `Utilities/Dispatcher.cs`'s `UiDispatcher`. `StockQuoteCache.cs` needs `DownloadLog` (stays in the WPF-heavy `StockQuoteManager.cs`, out of scope per Non-Goals) only for its constructor parameter — change `StockQuoteCache`'s constructor to accept `DownloadLog log` by keeping the parameter typed as-is is NOT possible without moving `DownloadLog`; instead, verify what `StockQuoteCache` actually does with `log` (store as a field, or call methods on it) — if it only stores and forwards it opaquely without calling `DownloadLog`-specific members outside what's already portable, this may resolve itself once `EventHandlerCollection`/`UiDispatcher` move (since `DownloadLog`'s own blocker was the same `UiDispatcher` dependency via `DelayedAction.cs`, not something in `StockQuoteCache` itself) — re-check the actual error after Step 2a's moves; if `DownloadLog` itself is still needed by `StockQuoteCache`'s own code (not just as an opaque field type), that's a separate, narrower problem than Blocker A and needs its own resolution (do not move `StockQuoteManager.cs` — out of scope; consider whether `StockQuoteCache` can take a narrower callback/interface instead of the concrete `DownloadLog`, following this same task's Step 2b pattern).

```bash
mkdir -p Source/WPF/MyMoney.Business/Utilities
git mv Source/WPF/MyMoney/Utilities/EventHandlerCollection.cs Source/WPF/MyMoney.Business/Utilities/EventHandlerCollection.cs
git mv Source/WPF/MyMoney/Utilities/Dispatcher.cs Source/WPF/MyMoney.Business/Utilities/Dispatcher.cs
```

(Re-verify both files' actual current content and dependencies before moving — the report's investigation is a strong signal, not a substitute for your own check, per this task's standing re-verification instruction.)

Update `Source/WPF/MyMoney.Business/MyMoney.Business.csproj` to allow `WindowsBase` without pulling in `PresentationCore`/`PresentationFramework`. Determine the minimal correct MSBuild mechanism by iteration (there is more than one plausible approach — e.g. `<ImportWindowsDesktopTargets>true</ImportWindowsDesktopTargets>` paired with a plain `<Reference Include="WindowsBase" />` and neither `<UseWPF>` nor `<UseWindowsForms>` set to `true`; or another mechanism entirely) — build, read the actual error/success, adjust. Confirm the result with:
```bash
dotnet build Source/WPF/MyMoney.Business/MyMoney.Business.csproj -v:d 2>&1 | grep -i "PresentationFramework\|PresentationCore"
```
Expected: no output (these two must never appear). A `WindowsBase` reference appearing is expected and fine.

- [ ] **Step 2b: Decouple DatabaseSettings.MigrateSettings from the concrete WPF Settings class (Ruling C)**

`DatabaseSettings.cs`'s `internal bool MigrateSettings(Settings settings)` calls `settings.MigrateFiscalYearStart()` and `settings.MigrateRentalManagement()` on `Walkabout.Configuration.Settings` (`Utilities/Settings.cs`, ~1200 lines, genuinely WPF-coupled, correctly not on any move list). Read `Utilities/Settings.cs` to find `MigrateFiscalYearStart`'s and `MigrateRentalManagement`'s exact return types, then create `Source/WPF/MyMoney.Business/ISettingsMigrationSource.cs`:

```csharp
namespace Walkabout.Data
{
    /// <summary>
    /// DatabaseSettings.MigrateSettings needs two values from the
    /// application-wide (WPF) Settings object for a one-time migration.
    /// This interface lets it depend on the shape it needs instead of the
    /// concrete Walkabout.Configuration.Settings type, which stays in
    /// MyMoney.csproj. The real Settings class implements this in
    /// MyMoney.csproj (Task 4).
    /// </summary>
    public interface ISettingsMigrationSource
    {
        // exact method signatures: match MigrateFiscalYearStart()'s and
        // MigrateRentalManagement()'s actual return types from
        // Utilities/Settings.cs verbatim -- do not guess them.
    }
}
```

Change `DatabaseSettings.cs`'s method signature from `MigrateSettings(Settings settings)` to `MigrateSettings(ISettingsMigrationSource settings)` — the body's two call sites (`settings.MigrateFiscalYearStart()`, `settings.MigrateRentalManagement()`) are unchanged, since the interface exposes the same members. Note for Task 4 (do not do this now — `MyMoney.csproj` can't reference `Walkabout.Data.ISettingsMigrationSource` until its `ProjectReference` to `MyMoney.Business` exists): `Utilities/Settings.cs`'s `Settings` class will need `: Walkabout.Data.ISettingsMigrationSource` added to its class declaration, and every caller of `MigrateSettings(...)` will keep compiling unchanged once that's in place (implicit reference conversion, no call-site changes needed) — added as an explicit new step in Task 4 below.

- [ ] **Step 2c: Re-verify the standalone build and the relaxed WPF check**

Run: `dotnet build Source/WPF/MyMoney.Business/MyMoney.Business.csproj`

Expected: 0 errors. If new unresolved-type errors surface, apply the same standing instruction as the original Step 6/brief: investigate, and either move a genuinely WPF-free dependency or escalate a genuine new blocker rather than guessing.

Then run the original Step 7 check exactly as the brief specifies, and commit (original Step 9), amending the commit message to describe both this task's original file-move work and this amendment's `EventHandlerCollection`/`Dispatcher`/`ISettingsMigrationSource` changes.

- [ ] **Step 1: Create the project file**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>Walkabout</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
</Project>
```

Save as `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`. No `UseWPF`, no `UseWindowsForms`, no `ImportWindowsDesktopTargets` — this is the enforcement mechanism for "no WPF in this project" (a WPF type wouldn't even resolve without those SDK flags, so a stray reference fails to compile immediately rather than silently succeeding).

- [ ] **Step 2: Move the domain-model files**

```bash
mkdir -p Source/WPF/MyMoney.Business/StockQuotes
git mv Source/WPF/MyMoney/Database/Money.cs Source/WPF/MyMoney.Business/Money.cs
git mv Source/WPF/MyMoney/Database/IDatabase.cs Source/WPF/MyMoney.Business/IDatabase.cs
git mv Source/WPF/MyMoney/Database/Mapping.cs Source/WPF/MyMoney.Business/Mapping.cs
git mv Source/WPF/MyMoney/Database/Query.cs Source/WPF/MyMoney.Business/Query.cs
git mv Source/WPF/MyMoney/Database/AutoCategorization.cs Source/WPF/MyMoney.Business/AutoCategorization.cs
git mv Source/WPF/MyMoney/Database/CostBasis.cs Source/WPF/MyMoney.Business/CostBasis.cs
git mv Source/WPF/MyMoney/Database/Encryption.cs Source/WPF/MyMoney.Business/Encryption.cs
git mv Source/WPF/MyMoney/Database/AsyncSqlQuery.cs Source/WPF/MyMoney.Business/AsyncSqlQuery.cs
git mv Source/WPF/MyMoney/Database/Payments.cs Source/WPF/MyMoney.Business/Payments.cs
git mv Source/WPF/MyMoney/Database/Money_Loans.cs Source/WPF/MyMoney.Business/Money_Loans.cs
git mv Source/WPF/MyMoney/Database/DatabaseSettings.cs Source/WPF/MyMoney.Business/DatabaseSettings.cs
git mv Source/WPF/MyMoney/StockQuotes/StockQuoteCache.cs Source/WPF/MyMoney.Business/StockQuotes/StockQuoteCache.cs
git mv Source/WPF/MyMoney/StockQuotes/IStockQuoteService.cs Source/WPF/MyMoney.Business/StockQuotes/IStockQuoteService.cs
git mv Source/WPF/MyMoney/Utilities/UsHolidays.cs Source/WPF/MyMoney.Business/UsHolidays.cs
```

Before moving on: for each file just listed, grep it for `System.Windows` one more time (`grep -n "System.Windows" <file>`) as the final per-file confirmation the spec calls for. Every one was checked clean during design, but re-verify now against the actual file content, not the earlier investigation notes.

- [ ] **Step 3: Drop the three confirmed-dead WPF/WinForms usings uncovered during design**

In `Source/WPF/MyMoney.Business/CostBasis.cs`, remove the line `using System.Windows.Navigation;` (confirmed unused — no `Navigation`-namespace symbol appears anywhere else in the file).

In `Source/WPF/MyMoney.Business/StockQuotes/StockQuoteCache.cs`, remove the line `using System.Windows.Forms.Design.Behavior;` (confirmed unused).

In `Source/WPF/MyMoney.Business/StockQuotes/IStockQuoteService.cs`, remove these two lines (both confirmed unused — no `Forms`- or `Navigation`-namespace symbol appears anywhere else in the file):
```
using System.Windows.Forms;
using System.Windows.Navigation;
```
(Leave the `using static System.Runtime.InteropServices.JavaScript.JSType;` line — it's odd but not a WPF/WinForms reference and out of scope for this task to clean up.)

- [ ] **Step 4: Make UsHolidays public**

In `Source/WPF/MyMoney.Business/UsHolidays.cs`, change:
```csharp
internal class UsHolidays
```
to:
```csharp
public class UsHolidays
```
(Required because it's now referenced from `StockQuoteHistory`, which will live in `MyMoney.Data`/`MyMoney.csproj` consumers across an assembly boundary — `internal` types aren't visible outside their own assembly.)

- [ ] **Step 5: Add the new project to the solution**

```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Business/MyMoney.Business.csproj
```

- [ ] **Step 6: Build MyMoney.Business standalone**

Run: `dotnet build Source/WPF/MyMoney.Business/MyMoney.Business.csproj`

Expected: 0 errors. If there are unresolved-type errors, it means a dependency was missed in the file list above — check the error for the missing type's source file and add it to this task (don't work around it by adding a project reference to `MyMoney.csproj`; every dependency of `Money.cs` and its adjacent files belongs in this same project by design).

- [ ] **Step 7: Confirm no WPF assembly reference**

Run: `dotnet build Source/WPF/MyMoney.Business/MyMoney.Business.csproj -v:d 2>&1 | grep -i "PresentationFramework\|PresentationCore\|WindowsBase"`

Expected: no output.

- [ ] **Step 8: Full-solution build will now fail — that's expected at this stage**

Run: `dotnet build Source/WPF/MyMoney.sln`

Expected: FAIL, with errors in `Source/WPF/MyMoney/Database/Money.cs` and friends reported as no-longer-found (they were `git mv`'d out) plus errors in whatever files in `MyMoney.csproj` reference `Walkabout.Data`/`Walkabout.StockQuotes` types that no longer resolve. This is expected and will be fixed by Task 4 adding the `ProjectReference`. Do not attempt to fix `MyMoney.csproj` in this task.

- [ ] **Step 9: Commit**

```bash
git add Source/WPF/MyMoney.Business Source/WPF/MyMoney.sln
git add -u Source/WPF/MyMoney/Database Source/WPF/MyMoney/StockQuotes Source/WPF/MyMoney/Utilities
git commit -m "Create MyMoney.Business and move the domain-model file set"
```

---

### Task 2: Create MyMoney.Data and move the storage implementations

**Files:**
- Create: `Source/WPF/MyMoney.Data/MyMoney.Data.csproj`
- Create: `Source/WPF/MyMoney.Data/IDataLayerUiCallback.cs`
- Move (via `git mv`) from `Source/WPF/MyMoney/Database/` to `Source/WPF/MyMoney.Data/`:
  `SqliteDatabase.cs`, `SqlDatabase.cs`, `SqlCeDatabase.cs`, `XmlStore.cs`,
  `CsvStore.cs`, `SqlServerStoredProcDatabase.cs`, `IDirectorySecurity.cs`,
  `DataEngineConfig.cs`, `DataEngineCredentialStore.cs`,
  `DataEnginePasswordGenerator.cs`
- Move (via `git mv`) `Source/WPF/MyMoney/Database/SqlScripts/` to `Source/WPF/MyMoney.Data/SqlScripts/` (item #2's `.sql` files — `BootstrapRunner` reads these by relative path at runtime, and `BootstrapRunner` lives in `MyMoneyAdmin.csproj`, which after Task 5 references `MyMoney.Data.csproj`; keeping the scripts co-located with the code that ships them avoids a separate copy-item)
- Modify: `Source/WPF/MyMoney.Data/SqlDatabase.cs` (replace the `MessageBoxEx.Show` call; remove the `SecurityService = new SecurityService()` default in the `Restore` factory)

**Interfaces:**
- Consumes: everything from Task 1 (`Walkabout.Data.*`, `Walkabout.Utilities.UsHolidays`)
- Produces: `IDataLayerUiCallback` (below), plus every `IDatabase` implementation and the three `DataEngine*` classes — these are what Task 4 (MyMoney.csproj) and Task 5 (downstream consumers) build against.

- [ ] **Step 1: Create the project file**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <RootNamespace>Walkabout</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.2" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.5" />
    <PackageReference Include="System.Data.SQLite" Version="2.0.4" />
    <PackageReference Include="System.IO.FileSystem.AccessControl" Version="5.0.0" />
  </ItemGroup>
</Project>
```

Save as `Source/WPF/MyMoney.Data/MyMoney.Data.csproj`. Package versions copied verbatim from `Source/WPF/MyMoney/MyMoney.csproj`'s current `PackageReference` entries — keep them in sync if that file's versions have moved since this plan was written (check `git log` on `MyMoney.csproj` for `PackageReference` changes since this plan's commit if unsure).

- [ ] **Step 2: Create the UI-decoupling interface**

```csharp
namespace Walkabout.Data
{
    /// <summary>
    /// The data layer has no WPF dependency, so it cannot show a message
    /// box or own a dialog directly. The two call sites that used to do
    /// this (SqlServerDatabase.AddLogin's mixed-mode warning, and the
    /// sample-database dialog owner lookup that lives with SampleDatabase
    /// in MyMoney.csproj) go through this instead. MyMoney.csproj supplies
    /// the real, WPF-backed implementation.
    /// </summary>
    public interface IDataLayerUiCallback
    {
        void ShowWarning(string message, string title);
    }
}
```

Save as `Source/WPF/MyMoney.Data/IDataLayerUiCallback.cs`.

- [ ] **Step 3: Move the storage implementation files**

```bash
git mv Source/WPF/MyMoney/Database/SqliteDatabase.cs Source/WPF/MyMoney.Data/SqliteDatabase.cs
git mv Source/WPF/MyMoney/Database/SqlDatabase.cs Source/WPF/MyMoney.Data/SqlDatabase.cs
git mv Source/WPF/MyMoney/Database/SqlCeDatabase.cs Source/WPF/MyMoney.Data/SqlCeDatabase.cs
git mv Source/WPF/MyMoney/Database/XmlStore.cs Source/WPF/MyMoney.Data/XmlStore.cs
git mv Source/WPF/MyMoney/Database/CsvStore.cs Source/WPF/MyMoney.Data/CsvStore.cs
git mv Source/WPF/MyMoney/Database/SqlServerStoredProcDatabase.cs Source/WPF/MyMoney.Data/SqlServerStoredProcDatabase.cs
git mv Source/WPF/MyMoney/Database/IDirectorySecurity.cs Source/WPF/MyMoney.Data/IDirectorySecurity.cs
git mv Source/WPF/MyMoney/Database/DataEngineConfig.cs Source/WPF/MyMoney.Data/DataEngineConfig.cs
git mv Source/WPF/MyMoney/Database/DataEngineCredentialStore.cs Source/WPF/MyMoney.Data/DataEngineCredentialStore.cs
git mv Source/WPF/MyMoney/Database/DataEnginePasswordGenerator.cs Source/WPF/MyMoney.Data/DataEnginePasswordGenerator.cs
git mv Source/WPF/MyMoney/Database/SqlScripts Source/WPF/MyMoney.Data/SqlScripts
```

Note: `Source/WPF/MyMoney/Database/DataEngineStartup.cs` and `Source/WPF/MyMoney/Database/SampleDatabase.cs` are NOT in this list — `DataEngineStartup.cs` stays in `MyMoney.csproj` (spec decision), and `SampleDatabase.cs` is handled by Task 3.

- [ ] **Step 4: Replace the direct MessageBox call in SqlDatabase.cs**

In `Source/WPF/MyMoney.Data/SqlDatabase.cs`, `SqlServerDatabase.AddLogin` currently ends with:
```csharp
using (RegistryKey reg = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL.1\MSSQLServer", true))
{
    if (reg != null)
    {
        object value = reg.GetValue("LoginMode");
        if ((int)value != 2)
        {
            // Must be set to 2 for mixed mode to work.
            reg.SetValue("LoginMode", 2);

            // TODO - we need to remove any UI from the DataBase model lower layer
            MessageBoxEx.Show("Please restart your SQL Service in order for mixed mode logins to work", "Mixed Mode Enabled", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
```

Add a settable property near the top of the `SqlServerDatabase` class (alongside the existing `SecurityService` property):
```csharp
public IDataLayerUiCallback UiCallback { get; set; }
```

Replace the `MessageBoxEx.Show(...)` line (and delete the `// TODO` comment above it — this task is that TODO being done) with:
```csharp
this.UiCallback?.ShowWarning("Please restart your SQL Service in order for mixed mode logins to work", "Mixed Mode Enabled");
```

Remove the `using System.Windows;` line from the top of the file if nothing else in the file uses a `System.Windows.*` type (re-check with `grep -n "System\.Windows\." Source/WPF/MyMoney.Data/SqlDatabase.cs` after this edit — expect no matches).

- [ ] **Step 5: Remove the default SecurityService self-wiring**

In the same file, `SqlServerDatabase.Restore(...)`'s object initializer currently includes `SecurityService = new SecurityService()`. Remove that line — `SecurityService` (the concrete class) stays in `MyMoney.csproj` and can no longer be referenced from this project. The property (`public IDirectorySecurity SecurityService { get; set; }`) stays; it's just no longer defaulted here. Task 4 finds and fixes the one caller of this factory that relied on the default.

- [ ] **Step 6: Build MyMoney.Data standalone**

Run: `dotnet build Source/WPF/MyMoney.Data/MyMoney.Data.csproj`

Expected: 0 errors.

- [ ] **Step 7: Confirm no WPF assembly reference**

**Correction (found during Task 2, 2026-09-15):** the literal `-v:d | grep` command below produces non-empty output in practice, but it's a false positive, not a real leak — `MyMoney.Business`'s `FrameworkReference` (added by Task 1's Ruling A) flows transitively through the `ProjectReference` and shows up in `MyMoney.Data`'s verbose RAR log as an available-but-unused compiler input, even though `MyMoney.Data.dll` itself never references it. The actual gate is what the assembly's own metadata says it references, not what MSBuild's resolver had available — use the reflection-based check instead (the same mechanism Task 6's `LayerBoundaryTests` uses), which is the check that actually matters:

```bash
dotnet build Source/WPF/MyMoney.Data/MyMoney.Data.csproj
dotnet run --project Source/WPF/MyMoney.Data -- 2>/dev/null || true
```
(If there's no convenient way to run a one-off reflection check from the command line at this point in the plan — `MyMoney.Data.csproj` is a library, not an executable — skip straight to Task 6, where `LayerBoundaryTests.MyMoneyData_HasNoWpfAssemblyReference` performs this exact check for real, in a real test run, and treat that as this step's actual verification. Don't rely on the `-v:d | grep` command below as a pass/fail gate; it's noisy in this specific transitively-referenced-project shape.)

Run for informational purposes only, not as a gate (expect it to show `WindowsBase`/possibly others among many unrelated lines — that's the known noise, not a failure):
```bash
dotnet build Source/WPF/MyMoney.Data/MyMoney.Data.csproj -v:d 2>&1 | grep -i "PresentationFramework\|PresentationCore\|WindowsBase"
```

- [ ] **Step 8: Add to solution**

```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoney.Data/MyMoney.Data.csproj
```

- [ ] **Step 9: Commit**

```bash
git add Source/WPF/MyMoney.Data Source/WPF/MyMoney.sln
git add -u Source/WPF/MyMoney/Database
git commit -m "Create MyMoney.Data and move the storage implementations"
```

---

### Task 3: Split SampleDatabase into a WPF wrapper and a portable generator

**Files:**
- Modify: `Source/WPF/MyMoney/Database/SampleDatabase.cs` (stays in `MyMoney.csproj`, shrinks to the dialog-driving wrapper)
- Create: `Source/WPF/MyMoney.Business/SampleDataGenerator.cs` (the portable data-construction core)

**Interfaces:**
- Consumes: `MyMoney`, `Account`, `AccountType`, `Security`, `StockSplit`, `Investment`, `InvestmentType`, `Transaction`, `Payee`, `Category`, `CategoryType` (Task 1), `StockQuoteHistory`, `StockQuote` (Task 1)
- Produces: `SampleDataGenerator.Create(MyMoney money, SampleData data, Dictionary<string, StockQuoteHistory> quotes, double inflation, int years, string employer, decimal paycheck)` — the method Task 3's own `SampleDatabase.Create()` wrapper calls; `SampleDataGenerator.Export(MyMoney money, string path)` for the existing `Export` method (also fully portable — no dialog involvement, moves in full).

The split point in the original `SampleDatabase.Create()` (see spec's investigation) is exactly after the dialog interaction and the stock-quote-zip extraction: everything from `foreach (SampleSecurity ss in data.Securities)` (loading `StockQuoteHistory` per symbol and registering it with `manager.DownloadLog`) onward is the portable core, EXCEPT the `this.manager.DownloadLog.AddHistory(history)` call itself, which is an app-integration side effect (registering sample history with the live running app's stock-quote cache) that stays on the WPF side.

- [ ] **Step 1: Create the portable generator**

Create `Source/WPF/MyMoney.Business/SampleDataGenerator.cs` with this content — it is `SampleDatabase.cs`'s current body from `Create()`'s `path = options.SampleData;` line onward (lines 71-203 of the original file), plus every one of its private helper methods (`GetStockMoney`, `GetClosingPrice`, the nested `Ownership` class, `CreateInvestmentSamples`, `RoundCents`, `GetRandomDaysInTheYearForTransactions`, `AddPaychecks`, `CreateRandomTransactions`, `Inflate`, `Export`), restructured as:

```csharp
using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using Walkabout.StockQuotes;

namespace Walkabout.Data
{
    /// <summary>
    /// The portable half of sample-database generation: takes already-resolved
    /// options (no dialog involved) and builds fictitious accounts and
    /// transactions into an existing MyMoney object. The WPF-facing half that
    /// shows the options dialog and loads stock-quote history from disk is
    /// Walkabout.Assistance.SampleDatabase, in MyMoney.csproj.
    /// </summary>
    public class SampleDataGenerator
    {
        private readonly MyMoney money;
        private Account checking;
        private readonly Random rand = new Random();
        private readonly Dictionary<string, StockQuoteHistory> quotes;

        public SampleDataGenerator(MyMoney money, Dictionary<string, StockQuoteHistory> quotes)
        {
            this.money = money;
            this.quotes = quotes;
        }

        public void Create(SampleData data, double inflation, int years, string employer, decimal paycheck)
        {
            // body: everything from the original Create()'s
            // "int totalFrequency = data.GetTotalFrequency();" line through
            // "this.money.OnLoaded();" -- unchanged logic, just relocated.
            // Includes the account-creation loop, the BeginUpdate/EndUpdate
            // securities-and-splits block, and the three calls:
            //   this.CreateRandomTransactions(list, inflation, years);
            //   this.AddPaychecks(employer, paycheck, inflation, years);
            //   this.CreateInvestmentSamples(data, brokerageAccounts, years);
            // plus the trailing Payees/Categories BeginUpdate/EndUpdate pairs.
        }

        // ... GetStockMoney, GetClosingPrice (using this.quotes), Ownership,
        // CreateInvestmentSamples, RoundCents, GetRandomDaysInTheYearForTransactions,
        // AddPaychecks, CreateRandomTransactions, Inflate: copied verbatim from
        // the original SampleDatabase.cs, unchanged -- none of them reference
        // WPF or the StockQuoteManager/dialog machinery.

        public void Export(string path)
        {
            // copied verbatim from the original SampleDatabase.Export -- no
            // WPF dependency, no reason for it to stay in MyMoney.csproj.
        }
    }
}
```

This step is a relocation of working code, not a rewrite — copy each method body verbatim from the current `Source/WPF/MyMoney/Database/SampleDatabase.cs`, adjusting only: constructor signature (drops `StockQuoteManager manager, string stockQuotePath`, takes `quotes` directly instead), and `Create`'s signature (drops the dialog/zip-extraction prefix, takes the already-resolved `SampleData data, double inflation, int years, string employer, decimal paycheck` instead of reading them off `options`).

The `SampleData`, `SampleAccount`, `SamplePayee`, `SampleSecurity`, `SampleSplit`, `SampleCategory`, `PaymentType`, and `SampleTransaction` types currently defined at the bottom of `SampleDatabase.cs` move to this new file too (they're the data-transfer types `SampleDataGenerator.Create` and `Export` operate on, no WPF dependency, no reason to stay behind).

- [ ] **Step 2: Shrink SampleDatabase.cs to the WPF wrapper**

Replace `Source/WPF/MyMoney/Database/SampleDatabase.cs`'s `Create()` method (and delete every helper method now duplicated in `SampleDataGenerator`) with:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using Walkabout.Data;
using Walkabout.Dialogs;
using Walkabout.StockQuotes;
using Walkabout.Utilities;

namespace Walkabout.Assistance
{
    /// <summary>
    /// WPF-facing half of sample-database generation: shows the options
    /// dialog, extracts the bundled sample-data and stock-quote-history
    /// resources, and hands the resolved inputs to
    /// Walkabout.Data.SampleDataGenerator (MyMoney.Business) for the actual
    /// account/transaction construction.
    /// </summary>
    public class SampleDatabase
    {
        private readonly MyMoney money;
        private readonly string stockQuotePath;
        private readonly StockQuoteManager manager;

        public SampleDatabase(MyMoney money, StockQuoteManager manager, string stockQuotePath)
        {
            this.manager = manager;
            this.money = money;
            this.stockQuotePath = stockQuotePath;
            if (string.IsNullOrEmpty(stockQuotePath))
            {
                throw new Exception("StockQuotePath cannot be empty. Have you created a database yet?");
            }
        }

        public void Create()
        {
            string temp = Path.Combine(Path.GetTempPath(), "MyMoney");
            Directory.CreateDirectory(temp);

            string path = Path.Combine(temp, "SampleData.xml");
            ProcessHelper.ExtractEmbeddedResourceAsFile("Walkabout.Database.SampleData.xml", path);

            SampleDatabaseOptions options = new SampleDatabaseOptions();
            options.Owner = Application.Current.MainWindow;
            options.SampleData = path;
            if (options.ShowDialog() == false)
            {
                return;
            }

            string zipPath = Path.Combine(temp, "SampleStockQuotes.zip");
            ProcessHelper.ExtractEmbeddedResourceAsFile("Walkabout.Database.SampleStockQuotes.zip", zipPath);

            string quoteFolder = Path.Combine(temp, "StockQuotes");
            if (Directory.Exists(quoteFolder))
            {
                Directory.Delete(quoteFolder, true);
            }
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, temp);
            foreach (var file in Directory.GetFiles(quoteFolder))
            {
                var target = Path.Combine(this.stockQuotePath, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target, true);
                }
            }

            path = options.SampleData;
            SampleData data;
            XmlSerializer s = new XmlSerializer(typeof(SampleData));
            using (XmlReader reader = XmlReader.Create(path))
            {
                data = (SampleData)s.Deserialize(reader);
            }

            var quotes = new Dictionary<string, StockQuoteHistory>();
            foreach (SampleSecurity ss in data.Securities)
            {
                var history = StockQuoteHistory.Load(quoteFolder, ss.Symbol);
                if (history != null)
                {
                    quotes[ss.Symbol] = history;
                    this.manager.DownloadLog.AddHistory(history);
                }
            }

            new SampleDataGenerator(this.money, quotes).Create(data, options.Inflation, options.Years, options.Employer, options.PayCheck);
        }

        public void Export(string path)
        {
            new SampleDataGenerator(this.money, new Dictionary<string, StockQuoteHistory>()).Export(path);
        }
    }
}
```

Note `Export` no longer has any real logic of its own here — it just forwards to the portable generator (an empty `quotes` dictionary is fine; `Export` never reads it, it only reads `this.money`'s existing transactions).

- [ ] **Step 3: Build and verify**

Run: `dotnet build Source/WPF/MyMoney.sln`

Expected: still fails at this stage (Task 4 hasn't wired `MyMoney.csproj`'s `ProjectReference`s yet) — but the failures should now be limited to unresolved `Walkabout.Data`/`Walkabout.StockQuotes` types across the whole project, not a `SampleDatabase`-specific error. If `SampleDatabase.cs` itself has a syntax or type-mismatch error distinct from "type not found" (e.g. a typo in the forwarding call), fix it now — that class of error will not go away once Task 4 adds the project references.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney.Business/SampleDataGenerator.cs Source/WPF/MyMoney/Database/SampleDatabase.cs
git commit -m "Split SampleDatabase into a portable generator and a WPF dialog wrapper"
```

---

### Task 4: Thin MyMoney.csproj and wire the new interfaces

**Files:**
- Move (via `git mv`) `Source/WPF/MyMoney/Database/MoneyDataObject.cs` to `Source/WPF/MyMoney/Interop/MoneyDataObject.cs`
- Modify: `Source/WPF/MyMoney/MyMoney.csproj` (add `ProjectReference`s; remove now-relocated `PackageReference`s that only the moved code needed, keeping any still used directly by `MyMoney.csproj`)
- Modify: `Source/WPF/MyMoney/Database/DataEngineStartup.cs` (no logic change — confirm it still compiles once `IDatabase`/`SqlServerStoredProcDatabase`/`DataEngineConfig`/`DataEngineCredentialStore` resolve via the new project references instead of being local files)
- Modify: `Source/WPF/MyMoney/Dialogs/AddLoginDialog.xaml.cs` and the two `new SqlServerDatabase()` sites in `Source/WPF/MyMoney/MainWindow.xaml.cs` (~line 1992 and ~line 2204) — add `UiCallback` wiring
- Create: `Source/WPF/MyMoney/Database/WpfDataLayerUiCallback.cs`

**Interfaces:**
- Consumes: everything produced by Tasks 1-3.

- [ ] **Step 1: Relocate MoneyDataObject.cs**

```bash
mkdir -p Source/WPF/MyMoney/Interop
git mv Source/WPF/MyMoney/Database/MoneyDataObject.cs Source/WPF/MyMoney/Interop/MoneyDataObject.cs
```

No code changes needed inside the file — its namespace (check the file's current `namespace` line and leave it as-is; if it's `Walkabout.Data`, leave it, since this is a pure directory move, not a namespace rename).

- [ ] **Step 2: Add project references**

In `Source/WPF/MyMoney/MyMoney.csproj`, add to the existing `ProjectReference` `ItemGroup` (the one that currently has `PerformanceProvider.csproj`):
```xml
<ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
<ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
```

- [ ] **Step 3: Review remaining PackageReferences**

`Microsoft.Data.SqlClient` must stay in `MyMoney.csproj`'s `PackageReference`s — `DataEngineStartup.cs` (staying here) directly uses `SqlConnectionStringBuilder`. `ModernWpfUI`, `System.ServiceModel.*`, `System.IO.FileSystem.AccessControl` (used by `SecurityService.cs`/`Setup/DirectorySetup.cs`, both staying here) also stay. `SQLitePCLRaw.bundle_e_sqlite3` and `System.Data.SQLite` were only used by the now-moved `SqliteDatabase.cs`/`SqlCeDatabase.cs` — grep to confirm nothing else in `MyMoney.csproj` uses them (`grep -rl "System.Data.SQLite\|SQLitePCLRaw" Source/WPF/MyMoney --include=*.cs`); if truly unused now, remove those two `PackageReference` entries. `Newtonsoft.Json` — grep the same way; if something in `MyMoney.csproj` besides the moved `DataEngine*` classes still uses it, keep it, otherwise remove it.

- [ ] **Step 4: Add the WPF-backed callback implementation**

Create `Source/WPF/MyMoney/Database/WpfDataLayerUiCallback.cs`:
```csharp
using System.Windows;

namespace Walkabout.Data
{
    public class WpfDataLayerUiCallback : IDataLayerUiCallback
    {
        public void ShowWarning(string message, string title)
        {
            MessageBoxEx.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
```

- [ ] **Step 5: Wire the callback where AddLogin is reachable**

`AddLogin` is called from `Source/WPF/MyMoney/Dialogs/AddLoginDialog.xaml.cs:81` (`this.database.AddLogin(user, pswd)`). Find where that dialog's `this.database` field is assigned (its constructor or an initializer) and confirm it is (or cast it to) a `SqlServerDatabase`. Set `.UiCallback = new WpfDataLayerUiCallback()` on it at that assignment point, the same way `.SecurityService = new SecurityService()` is already set at the two `new SqlServerDatabase()` sites in `MainWindow.xaml.cs`. If `AddLoginDialog` receives an already-constructed `SqlServerDatabase` from `MainWindow.xaml.cs` instead of constructing its own, add `UiCallback = new WpfDataLayerUiCallback()` to the object initializers at `MainWindow.xaml.cs`'s two `new SqlServerDatabase()` sites (~line 1992 and ~line 2204) instead, alongside the existing `SecurityService = new SecurityService()` line — trace the actual data flow from `AddLoginDialog`'s `database` field backward to find the real construction site before deciding which.

**Also wire it for `CsvStore` (found during Task 2, not in the original brief):** Task 2's per-file grep for the brief's move list missed a second `MessageBoxEx.Show` call — `CsvStore.Backup` ("XML Backup is not implemented"). Task 2 already applied the same `IDataLayerUiCallback` pattern to it (`CsvStore` now has a `UiCallback` property, same shape as `SqlServerDatabase`'s). Find every `new CsvStore(...)` construction site in `MyMoney.csproj` (`grep -rn "new CsvStore(" Source/WPF/MyMoney --include=*.cs`) and set `.UiCallback = new WpfDataLayerUiCallback()` on each, same as the `SqlServerDatabase` sites above.

- [ ] **Step 6: Fix the Restore() factory's removed default**

Find every caller of `SqlServerDatabase.Restore(...)` (`grep -rn "SqlServerDatabase.Restore(" Source/WPF/MyMoney --include=*.cs`). For each one, add `.SecurityService = new SecurityService()` on the returned object if that call site's flow needs directory-permission writes (matches the pattern already used at the two `new SqlServerDatabase()` sites) — check what the caller does with the restored database immediately afterward to judge whether this matters for that specific path, and set it if there's any doubt, since a null `SecurityService` only matters if `AddWritePermission` is actually invoked down that path.

- [ ] **Step 6a: Make Settings implement ISettingsMigrationSource (Ruling C, from Task 1's amendment)**

Task 1 changed `DatabaseSettings.MigrateSettings` (now in `MyMoney.Business`) to take `Walkabout.Data.ISettingsMigrationSource` instead of the concrete `Walkabout.Configuration.Settings` type, and created that interface in `Source/WPF/MyMoney.Business/ISettingsMigrationSource.cs`. Now that this task's Step 2 has added the `MyMoney.Business` project reference, edit `Source/WPF/MyMoney/Utilities/Settings.cs`'s class declaration to add `: Walkabout.Data.ISettingsMigrationSource` (add a `using Walkabout.Data;` if not already present).

**Correction (found by Task 1's review, 2026-09-15 — the original text below this line understated the change needed):** `Utilities/Settings.cs:175,181` currently declares `internal int MigrateFiscalYearStart()` and `internal bool MigrateRentalManagement()`. C# requires a member implicitly implementing a `public` interface member to itself be `public` — `internal` won't satisfy `ISettingsMigrationSource` and this step will fail to compile as originally written. Change both methods from `internal` to `public` as part of this step (or use explicit interface implementation if there's a reason the direct `internal` call sites need to keep working unqualified — check for any such call sites first; the known caller, `MainWindow.xaml.cs:2080`, calls through `DatabaseSettings.MigrateSettings`, not `Settings`'s methods directly, so a plain `internal`→`public` change is expected to be suffient). ~~No other change should be needed~~ — this accessibility change IS the other change needed. Build after this change and confirm no new errors at any `MigrateSettings(...)` call site (implicit reference conversion should make them compile unchanged once accessibility is fixed).

- [ ] **Step 6b: Make DownloadLog implement IStockDownloadLog (same pattern, from Task 1's amendment)**

While resolving Task 1's Blocker B, the implementer found `StockQuoteCache.cs` (moved to `MyMoney.Business`) needs `DownloadLog.Folder`/`DownloadLog.GetHistory(symbol)` — `DownloadLog` itself stays in `StockQuotes/StockQuoteManager.cs` (genuinely WPF-heavy, out of scope). Resolved with the same interface pattern as Step 6a: `Source/WPF/MyMoney.Business/StockQuotes/IStockDownloadLog.cs` (declaring `Folder`/`GetHistory`), and `StockQuoteCache`'s field/constructor parameter now typed as `IStockDownloadLog` instead of the concrete `DownloadLog`. Edit `StockQuotes/StockQuoteManager.cs`'s `DownloadLog` class declaration to add `: Walkabout.StockQuotes.IStockDownloadLog` (check the exact namespace the interface landed in against the actual file — it may be `Walkabout.Data` or `Walkabout.StockQuotes`, follow what's there). Verify every existing construction of `StockQuoteCache(...)` still compiles (implicit reference conversion, same as Step 6a).

- [ ] **Step 7: Full solution build**

Run: `dotnet build Source/WPF/MyMoney.sln`

Expected: 0 errors. Fix any remaining unresolved-reference or type-mismatch error before moving on — this is the first point where the whole solution is expected to build clean again since Task 1 started.

- [ ] **Step 8: Run the full existing test suite**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`

Expected: 29 passed, 1 skipped, 0 failed — identical to the pre-extraction baseline. Any change in this count is a real regression from this task's changes, not an artifact of the move.

- [ ] **Step 9: Commit**

```bash
git add Source/WPF/MyMoney
git commit -m "Thin MyMoney.csproj to a consumer of MyMoney.Business and MyMoney.Data"
```

---

### Task 5: Update downstream consumer projects

**Files:**
- Modify: `Source/WPF/UnitTests/UnitTests.csproj`
- Modify: `Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj`
- Modify: `Source/WPF/UITests/UITests.csproj`
- Modify: `Source/WPF/ScenarioTest/ScenarioTest.csproj`

**Interfaces:**
- Consumes: `MyMoney.Business.csproj`, `MyMoney.Data.csproj` (Tasks 1-2)

- [ ] **Step 1: UnitTests.csproj**

Add direct references to both new projects, alongside its existing `MyMoney.csproj`/`MyMoneyAdmin.csproj` references (this is the "proves the DLLs are independently consumable" requirement from the spec's Testing section — Task 6 adds the test that exercises it):
```xml
<ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
<ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
```

- [ ] **Step 2: MyMoneyAdmin.csproj**

`MyMoneyAdmin`'s own source (`BootstrapRunner.cs`, `Program.cs`, `RetryLoop.cs`, `SaBootstrapConnection.cs`) only ever uses `Walkabout.Data.DataEngineCredentialStore`/`DataEngineCredential`/`DataEnginePasswordGenerator` (all now in `MyMoney.Data`) — confirmed during design, it has no dependency on anything in `MyMoney.csproj` itself. Replace:
```xml
<ProjectReference Include="..\MyMoney\MyMoney.csproj" />
```
with:
```xml
<ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
```
(`MyMoney.Data` transitively brings in `MyMoney.Business`.) This also resolves the circular-reference constraint noted in this file's existing comment about `MyMoney.csproj` — re-read that comment block once this change is in and confirm it's now stale/no-longer-applicable; if the comment is now inaccurate, delete it (its content described a problem that no longer exists after this change, not a decision this task needs to re-litigate).

- [ ] **Step 3: UITests.csproj**

`FixtureGenerator.cs` uses `Walkabout.Data` types (`SqliteDatabase`, `MyMoney`, `Account`, `Payee`, `Transaction`) directly. Add both new references while keeping the existing `MyMoney.csproj` reference (needed for build-ordering — `UITests` drives the built `MyMoney.exe` as an external process and depends on it being freshly built, per item #4's own design):
```xml
<ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
<ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
```

- [ ] **Step 4: ScenarioTest.csproj**

`ScenarioTest.cs` uses `Walkabout.Data` types directly (inspecting the live app's domain state during automation). Add both new references, keeping the existing `MyMoney.csproj` reference:
```xml
<ProjectReference Include="..\MyMoney.Business\MyMoney.Business.csproj" />
<ProjectReference Include="..\MyMoney.Data\MyMoney.Data.csproj" />
```

- [ ] **Step 5: Build everything**

Run: `dotnet build Source/WPF/MyMoney.sln`

Expected: 0 errors, all projects including `UnitTests`, `MyMoneyAdmin`, `UITests`, `ScenarioTest`.

- [ ] **Step 6: Run the full test suite again**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`

Expected: 29 passed, 1 skipped, 0 failed.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/UnitTests/UnitTests.csproj Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj Source/WPF/UITests/UITests.csproj Source/WPF/ScenarioTest/ScenarioTest.csproj
git commit -m "Update downstream consumer projects to reference MyMoney.Business and MyMoney.Data"
```

---

### Task 6: Add the portability-boundary tests

**Files:**
- Create: `Source/WPF/UnitTests/LayerBoundaryTests.cs`

**Interfaces:**
- Consumes: `MyMoney.Business.dll`/`MyMoney.Data.dll` build output (via reflection); `Walkabout.Data.MyMoney`, `Walkabout.Data.SqliteDatabase` (Tasks 1-2), to prove direct, non-transitive consumability.

- [ ] **Step 1: Write the assembly-reference test**

```csharp
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Walkabout.UnitTests
{
    [TestFixture]
    public class LayerBoundaryTests
    {
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
        private static readonly string[] UiRenderingAssemblyNames =
        {
            "PresentationFramework", "PresentationCore"
        };

        private static readonly string[] AllWpfAssemblyNames =
        {
            "PresentationFramework", "PresentationCore", "WindowsBase"
        };

        [Test]
        public void MyMoneyBusiness_HasNoUiRenderingAssemblyReference()
        {
            var assembly = typeof(Walkabout.Data.MyMoney).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            CollectionAssert.IsEmpty(
                referenced.Where(n => UiRenderingAssemblyNames.Contains(n)).ToList(),
                $"MyMoney.Business referenced a UI-rendering assembly: {string.Join(", ", referenced)}");
        }

        [Test]
        public void MyMoneyData_HasNoWpfAssemblyReference()
        {
            var assembly = typeof(Walkabout.Data.SqliteDatabase).Assembly;
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            CollectionAssert.IsEmpty(
                referenced.Where(n => AllWpfAssemblyNames.Contains(n)).ToList(),
                $"MyMoney.Data referenced a WPF assembly: {string.Join(", ", referenced)}");
        }

        [Test]
        public void MyMoneyBusiness_IsADistinctAssemblyFromMyMoneyData()
        {
            var businessAssembly = typeof(Walkabout.Data.MyMoney).Assembly;
            var dataAssembly = typeof(Walkabout.Data.SqliteDatabase).Assembly;
            Assert.AreNotEqual(businessAssembly.GetName().Name, dataAssembly.GetName().Name);
            Assert.AreEqual("MyMoney.Business", businessAssembly.GetName().Name);
            Assert.AreEqual("MyMoney.Data", dataAssembly.GetName().Name);
        }
    }
}
```

Save as `Source/WPF/UnitTests/LayerBoundaryTests.cs`. The third test is the concrete proof that `UnitTests.csproj`'s Task 5 references reach two genuinely separate DLLs, not one merged assembly under two names.

- [ ] **Step 2: Run the new tests**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~LayerBoundaryTests"`

Expected: 3 passed, 0 failed.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`

Expected: 32 passed (29 + 3 new), 1 skipped, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/UnitTests/LayerBoundaryTests.cs
git commit -m "Add automated portability-boundary tests for MyMoney.Business and MyMoney.Data"
```

---

### Task 7: End-to-end regression verification

**Files:** none (verification-only task, no code changes expected)

**Interfaces:** none produced; this task consumes the fully-extracted solution from Tasks 1-6.

- [ ] **Step 1: Full solution build**

Run: `dotnet build Source/WPF/MyMoney.sln`

Expected: 0 errors.

- [ ] **Step 2: Full existing test suite**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`

Expected: 32 passed, 1 skipped, 0 failed (29 original + 3 from Task 6).

- [ ] **Step 3: Re-run the existing FlaUI scenario as a regression smoke check**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~PayeeSelectionTests"`

Expected: 1 passed, 0 failed — this is `PayeeSelectionTests.SelectingAPayee_AfterOpeningFixtureFile_DoesNotCrash` from item #4, unmodified, run here purely as proof that the real running app (launch, File→Open, expand Payees, select a row) still works end-to-end after the boundary move. If it fails, do not modify the test to make it pass — the test is verified-correct from item #4's own final review; a failure here means Tasks 1-6 broke something real.

- [ ] **Step 4: Verify the portability check would actually catch a violation**

This is a one-time sanity check on the Task 6 tests themselves, not a permanent step: temporarily add `<UseWPF>true</UseWPF>` and a trivial `using System.Windows;` reference to a scratch file in `MyMoney.Data.csproj`, confirm `MyMoneyData_HasNoWpfAssemblyReference` actually fails, then revert the temporary change completely (`git checkout -- Source/WPF/MyMoney.Data`) before continuing. This confirms the new test is a real tripwire, not a no-op that would pass regardless of what's in the project.

- [ ] **Step 5: Report**

No commit for this task (nothing changes). Summarize: final build status, final test counts, FlaUI regression result, and confirmation that the portability check is a real tripwire (Step 4). This is the evidence the plan's Goals section promised — a working solution with the layers genuinely separated, not just projects that happen to compile today.
