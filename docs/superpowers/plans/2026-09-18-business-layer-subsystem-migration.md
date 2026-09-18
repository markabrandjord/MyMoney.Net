# Business-Layer Subsystem Migration (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Taxes, StockQuotes, Importers, Ofx-parsing, and MainWindow's file/database-lifecycle + import-dispatch logic from `Source/WPF/MyMoney` (the WPF app project) into `Source/WPF/MyMoney.Business`, each validated by real business-layer tests, so this logic is testable without the full WPF app or an interactive desktop.

**Architecture:** One subsystem per task, in dependency-of-difficulty order (Taxes → StockQuotes → Importers+MainWindow-import-dispatch → MainWindow-file-lifecycle → Ofx-parsing). Each task moves files via `git mv` (both projects share `RootNamespace=Walkabout` and rely on SDK-style implicit compile-item globbing, so a move is a pure filesystem operation — no namespace or `.csproj` `<Compile>` changes needed for `.cs` files). Where a file has genuine WPF coupling (`MessageBoxEx.Show`, a live `DownloadControl` reference), the coupling is cut via a small callback interface living in `MyMoney.Business`, with a thin WPF-backed adapter left in `MyMoney.csproj` — the same shape as the existing `IDataLayerUiCallback`/`WpfDataLayerUiCallback` pair, just for `MyMoney.Business` (which cannot reference `MyMoney.Data`, where `IDataLayerUiCallback` lives, without creating a circular project reference).

**Tech Stack:** C# / .NET 10.0-windows7.0, NUnit (UnitTests), `MyMoney.TestSupport`'s `MockDatabase`.

**Spec:** `docs/superpowers/specs/2026-09-18-business-layer-subsystem-migration-design.md`

## Global Constraints

- `MyMoney.Business` must end this plan with zero `PresentationFramework`/`PresentationCore`/`WindowsBase` assembly references, verified by the existing `LayerBoundaryTests.MyMoneyBusiness_HasNoWpfAssemblyReference` test (no changes needed to that test — it already covers the whole assembly, including newly added files).
- No new test framework/harness: reuse `MyMoney.TestSupport`'s `MockDatabase` and the existing `DatabaseContractTests*` pattern.
- Every subsystem migration ends with `dotnet build Source/WPF/MyMoney.sln` and `dotnet test Source/WPF/UnitTests/UnitTests.csproj` + `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj` green before moving to the next task.
- Reports, the rest of MainWindow's orchestration, dual-engine parity, and FlaUI-level testing are explicitly out of scope — do not touch `Source/WPF/MyMoney/Reports/`.

---

## Task 1: Seed `SampleDataGenerator` and add a headless embedded-sample-data loader

**Files:**
- Modify: `Source/WPF/MyMoney.Business/SampleDataGenerator.cs`
- Create: `Source/WPF/MyMoney.Business/SampleDataLoader.cs`
- Modify: `Source/WPF/MyMoney/Database/SampleDatabase.cs:38-49` (dedupe against the new loader)
- Test: `Source/WPF/UnitTests/SampleDataLoaderTests.cs`

**Interfaces:**
- Produces: `SampleDataGenerator(MyMoney money, Dictionary<string, StockQuoteHistory> quotes, int? randomSeed = null)` (new optional 3rd parameter; existing 2-arg call sites unaffected).
- Produces: `SampleDataLoader.LoadEmbeddedSampleData(Assembly resourceAssembly, string tempDir) -> (SampleData Data, Dictionary<string, StockQuoteHistory> Quotes)`.

- [ ] **Step 1: Add a seeded-Random overload to `SampleDataGenerator`**

Current state (verified in `Source/WPF/MyMoney.Business/SampleDataGenerator.cs:17-24`):
```csharp
public class SampleDataGenerator
{
    private readonly MyMoney money;
    private Account checking;
    private readonly Random rand = new Random();
    private readonly Dictionary<string, StockQuoteHistory> quotes;

    public SampleDataGenerator(MyMoney money, Dictionary<string, StockQuoteHistory> quotes)
```

Change the `rand` field to a plain `readonly Random` (no inline initializer) and set it in the constructor:

```csharp
public class SampleDataGenerator
{
    private readonly MyMoney money;
    private Account checking;
    private readonly Random rand;
    private readonly Dictionary<string, StockQuoteHistory> quotes;

    public SampleDataGenerator(MyMoney money, Dictionary<string, StockQuoteHistory> quotes, int? randomSeed = null)
    {
        this.money = money;
        this.quotes = quotes;
        this.rand = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
    }
```

(Confirm the existing constructor body only assigned `this.money`/`this.quotes` before this change — if it did anything else, preserve those lines.)

- [ ] **Step 2: Build to confirm no regressions from the field change**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors (the two existing 2-arg call sites — `SampleDatabase.cs` — still bind fine since `randomSeed` defaults to `null`).

- [ ] **Step 3: Add the headless embedded-sample-data loader**

Create `Source/WPF/MyMoney.Business/SampleDataLoader.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Walkabout.StockQuotes;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    /// <summary>
    /// Extracts and deserializes the embedded sample-data resources
    /// (SampleData.xml, SampleStockQuotes.zip) without any WPF dependency.
    /// The resources are embedded in whichever assembly the caller passes
    /// in (MyMoney.csproj today) -- this class doesn't assume which
    /// assembly that is, so it works the same way from a headless test as
    /// from the WPF-hosted SampleDatabase wizard.
    /// </summary>
    public static class SampleDataLoader
    {
        public static (SampleData Data, Dictionary<string, StockQuoteHistory> Quotes) LoadEmbeddedSampleData(
            Assembly resourceAssembly, string tempDir)
        {
            Directory.CreateDirectory(tempDir);

            string xmlPath = Path.Combine(tempDir, "SampleData.xml");
            ProcessHelper.ExtractEmbeddedResourceAsFile(resourceAssembly, "Walkabout.Database.SampleData.xml", xmlPath);

            string zipPath = Path.Combine(tempDir, "SampleStockQuotes.zip");
            ProcessHelper.ExtractEmbeddedResourceAsFile(resourceAssembly, "Walkabout.Database.SampleStockQuotes.zip", zipPath);

            string quoteFolder = Path.Combine(tempDir, "StockQuotes");
            if (Directory.Exists(quoteFolder))
            {
                Directory.Delete(quoteFolder, true);
            }
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            SampleData data;
            XmlSerializer serializer = new XmlSerializer(typeof(SampleData));
            using (XmlReader reader = XmlReader.Create(xmlPath))
            {
                data = (SampleData)serializer.Deserialize(reader);
            }

            Dictionary<string, StockQuoteHistory> quotes = new Dictionary<string, StockQuoteHistory>();
            foreach (SampleSecurity ss in data.Securities)
            {
                StockQuoteHistory history = StockQuoteHistory.Load(quoteFolder, ss.Symbol);
                if (history != null)
                {
                    quotes[ss.Symbol] = history;
                }
            }

            return (data, quotes);
        }
    }
}
```

Verify `ProcessHelper.ExtractEmbeddedResourceAsFile`'s exact signature in
`Source/WPF/MyMoney.Business/Utilities/ProcessHelper.cs` before compiling — adjust the call
if the parameter order/names differ from `(Assembly, string resourceName, string targetPath)`.

- [ ] **Step 4: Write a test proving the loader works against the real embedded resources**

Create `Source/WPF/UnitTests/SampleDataLoaderTests.cs`:

```csharp
using System.IO;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class SampleDataLoaderTests
    {
        [Test]
        public void LoadEmbeddedSampleData_ReturnsNonEmptyDataAndQuotes()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "MyMoneyTests", "SampleDataLoaderTests");
            var (data, quotes) = SampleDataLoader.LoadEmbeddedSampleData(typeof(Walkabout.MainWindow).Assembly, tempDir);

            Assert.That(data, Is.Not.Null);
            Assert.That(data.Accounts, Is.Not.Empty);
            Assert.That(data.Payees, Is.Not.Empty);
            Assert.That(data.Securities, Is.Not.Empty);
            Assert.That(quotes, Is.Not.Empty, "expected at least one security's quote history to load");
        }
    }
}
```

- [ ] **Step 5: Run the test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SampleDataLoaderTests"`
Expected: PASS. If it fails on the embedded resource name, open `Source/WPF/MyMoney/MyMoney.csproj` and confirm the exact resource name Visual Studio's naming convention produced (verified during research as `Walkabout.Database.SampleData.xml`/`Walkabout.Database.SampleStockQuotes.zip` — `RootNamespace.FolderPath.FileName`).

- [ ] **Step 6: Dedupe `SampleDatabase.Create()` against the new loader**

In `Source/WPF/MyMoney/Database/SampleDatabase.cs`, replace the manual extraction (lines ~38-49, the `ProcessHelper.ExtractEmbeddedResourceAsFile` + zip-extraction + XML-deserialize block) with a call to `SampleDataLoader.LoadEmbeddedSampleData`, keeping the `SampleDatabaseOptions` dialog step (WPF-only) and the `this.manager.DownloadLog.AddHistory(history)` loop (still WPF-project-only, since it needs the `StockQuoteManager` instance):

```csharp
public void Create()
{
    string temp = Path.Combine(Path.GetTempPath(), "MyMoney");
    var (data, quotes) = SampleDataLoader.LoadEmbeddedSampleData(System.Reflection.Assembly.GetExecutingAssembly(), temp);

    SampleDatabaseOptions options = new SampleDatabaseOptions();
    options.Owner = Application.Current.MainWindow;
    options.SampleData = Path.Combine(temp, "SampleData.xml");
    if (options.ShowDialog() == false)
    {
        return;
    }

    foreach (var kvp in quotes)
    {
        this.manager.DownloadLog.AddHistory(kvp.Value);
    }

    new SampleDataGenerator(this.money, quotes).Create(data, options.Inflation, options.Years, options.Employer, options.PayCheck);
}
```

Note: this reorders the dialog to appear *after* extraction instead of before (the original showed the dialog, then extracted quotes only after confirmation) — check with a quick manual run whether that's an acceptable behavior change, since extraction is fast and the dialog's `Cancel` path previously skipped the zip/quote work entirely. If preserving the original ordering matters, keep the XML-only extraction before the dialog and call the quotes-loading half of `LoadEmbeddedSampleData` only after `ShowDialog()` returns true (split the method into two, e.g. `LoadSampleDataXml` and `LoadQuoteHistory`, if so).

- [ ] **Step 7: Build and run full existing suite**

Run: `dotnet build Source/WPF/MyMoney.sln` then `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: 0 errors, all tests green (including the pre-existing sample-data-driven ones, e.g. issue #51's `SampleDataGenerator.Symbol` fix regression test if one exists).

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney.Business/SampleDataGenerator.cs Source/WPF/MyMoney.Business/SampleDataLoader.cs Source/WPF/MyMoney/Database/SampleDatabase.cs Source/WPF/UnitTests/SampleDataLoaderTests.cs
git commit -m "feat: add seeded SampleDataGenerator and headless embedded-sample-data loader"
```

---

## Task 2: Canary + property-based invariant tests

**Files:**
- Create: `Source/WPF/UnitTests/SampleDataRegressionTests.cs`

**Interfaces:**
- Consumes: `SampleDataLoader.LoadEmbeddedSampleData` (Task 1), `SampleDataGenerator(MyMoney, Dictionary<string,StockQuoteHistory>, int?)` (Task 1), `MockDatabase` (existing, `Walkabout.Data` namespace).

- [ ] **Step 1: Write the property-based invariant tests (no golden value needed)**

Create `Source/WPF/UnitTests/SampleDataRegressionTests.cs`:

```csharp
using System.IO;
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    [TestFixture]
    public class SampleDataRegressionTests
    {
        private MyMoney GenerateSample(int seed)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "MyMoneyTests", "SampleDataRegressionTests", seed.ToString());
            var (data, quotes) = SampleDataLoader.LoadEmbeddedSampleData(typeof(Walkabout.MainWindow).Assembly, tempDir);

            var money = new MyMoney();
            new SampleDataGenerator(money, quotes, randomSeed: seed).Create(data, inflation: 0.02, years: 10, employer: "ACME inc", paycheck: 2000m);
            return money;
        }

        [Test]
        public void Invariant_SplitsSumToTransactionAmount()
        {
            var money = this.GenerateSample(seed: 42);
            foreach (var t in money.Transactions)
            {
                if (t.IsSplit)
                {
                    decimal splitTotal = t.Splits.Sum(s => s.Amount);
                    Assert.That(splitTotal, Is.EqualTo(t.Amount).Within(0.01m),
                        $"Transaction {t.Id} splits sum to {splitTotal} but transaction amount is {t.Amount}");
                }
            }
        }

        [Test]
        public void Invariant_TransfersBalance()
        {
            var money = this.GenerateSample(seed: 42);
            foreach (var t in money.Transactions)
            {
                if (t.Transfer != null && !t.IsDeleted)
                {
                    var other = t.Transfer.Transaction;
                    Assert.That(other, Is.Not.Null, $"Transaction {t.Id} has a Transfer with no linked Transaction");
                    Assert.That(other.Amount, Is.EqualTo(-t.Amount).Within(0.01m),
                        $"Transfer pair {t.Id}/{other.Id} does not balance: {t.Amount} vs {other.Amount}");
                }
            }
        }

        [Test]
        public void Invariant_CategoryTotalsReconcileWithTransactions()
        {
            var money = this.GenerateSample(seed: 42);
            var byCategory = money.Transactions
                .Where(t => !t.IsDeleted && !t.IsSplit && t.Category != null)
                .GroupBy(t => t.Category)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

            foreach (var category in money.Categories)
            {
                if (byCategory.TryGetValue(category, out decimal expectedTotal))
                {
                    decimal actualTotal = money.Transactions
                        .Where(t => !t.IsDeleted && !t.IsSplit && t.Category == category)
                        .Sum(t => t.Amount);
                    Assert.That(actualTotal, Is.EqualTo(expectedTotal).Within(0.01m));
                }
            }
        }
    }
}
```

Before finalizing, check `Transaction`'s real property names (`IsSplit`, `Splits`, `Transfer`, `Category`, `Amount`) against `Source/WPF/MyMoney.Business/*.cs` (the domain model) — adjust names if they differ from what's assumed here.

- [ ] **Step 2: Run the invariant tests**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SampleDataRegressionTests"`
Expected: all 3 PASS against the current, unmigrated codebase (this proves the invariants hold *before* any subsystem migration, establishing the baseline every later task must keep green).

- [ ] **Step 3: Add the seed-derived canary**

This step requires running code once to discover the real value, then pinning it — that's the standard way to write a canary test, not a placeholder. Add a temporary diagnostic test:

```csharp
        [Test]
        public void Diagnostic_PrintCanaryTotal()
        {
            var money = this.GenerateSample(seed: 42);
            decimal netWorth = money.Accounts.Where(a => !a.IsClosed).Sum(a => a.Balance);
            TestContext.WriteLine($"CANARY_NET_WORTH={netWorth}");
        }
```

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~Diagnostic_PrintCanaryTotal" -v n`
Read the printed `CANARY_NET_WORTH=` value from the test output.

- [ ] **Step 4: Replace the diagnostic with the real pinned canary test**

Delete `Diagnostic_PrintCanaryTotal` and add, using the value observed in Step 3 (replace `<OBSERVED_VALUE>` with it — do not leave this unresolved):

```csharp
        [Test]
        public void Canary_Seed42NetWorth_IsStable()
        {
            var money = this.GenerateSample(seed: 42);
            decimal netWorth = money.Accounts.Where(a => !a.IsClosed).Sum(a => a.Balance);
            Assert.That(netWorth, Is.EqualTo(<OBSERVED_VALUE>m).Within(0.01m),
                "Net worth for seed 42 changed - a subsystem migration may have altered behavior, not just location");
        }
```

- [ ] **Step 5: Run the full regression file**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SampleDataRegressionTests"`
Expected: all 4 tests PASS (3 invariants + 1 canary).

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/UnitTests/SampleDataRegressionTests.cs
git commit -m "test: add seed-derived canary and property-based invariants for phase-1 migration safety net"
```

---

## Task 3: Migrate Taxes

**Files:**
- Move: `Source/WPF/MyMoney/Taxes/CapitalGains.cs` → `Source/WPF/MyMoney.Business/Taxes/CapitalGains.cs`
- Move: `Source/WPF/MyMoney/Taxes/FederalTaxes.cs` → `Source/WPF/MyMoney.Business/Taxes/FederalTaxes.cs`
- Move: `Source/WPF/MyMoney/Taxes/FederalTaxes.json` → `Source/WPF/MyMoney.Business/Taxes/FederalTaxes.json`
- Move: `Source/WPF/MyMoney/Taxes/StateTaxes.cs` → `Source/WPF/MyMoney.Business/Taxes/StateTaxes.cs`
- Move: `Source/WPF/MyMoney/Taxes/StateTaxes.json` → `Source/WPF/MyMoney.Business/Taxes/StateTaxes.json`
- Move: `Source/WPF/MyMoney/Taxes/TaxCategory.cs` → `Source/WPF/MyMoney.Business/Taxes/TaxCategory.cs`
- Move: `Source/WPF/MyMoney/Taxes/TxfExporter.cs` → `Source/WPF/MyMoney.Business/Taxes/TxfExporter.cs`
- Move: `Source/WPF/MyMoney/Taxes/TxfSpec.txt` → `Source/WPF/MyMoney.Business/Taxes/TxfSpec.txt`
- Modify: `Source/WPF/MyMoney/MyMoney.csproj:606-616` (remove the 3 explicit item entries for the moved data files)
- Modify: `Source/WPF/MyMoney.Business/MyMoney.Business.csproj` (add matching entries)
- Test: `Source/WPF/UnitTests/CostBasisTests.cs`, `Source/WPF/UnitTests/TaxTests.cs` (already exist — no new tests needed, this task's job is to keep them green through the move)

**Interfaces:** None — Taxes has zero WPF coupling (verified: 0/5 files reference `System.Windows`), this is a pure relocation.

- [ ] **Step 1: Move the 5 `.cs` files and 3 data files**

```bash
git mv Source/WPF/MyMoney/Taxes/CapitalGains.cs Source/WPF/MyMoney.Business/Taxes/CapitalGains.cs
git mv Source/WPF/MyMoney/Taxes/FederalTaxes.cs Source/WPF/MyMoney.Business/Taxes/FederalTaxes.cs
git mv Source/WPF/MyMoney/Taxes/FederalTaxes.json Source/WPF/MyMoney.Business/Taxes/FederalTaxes.json
git mv Source/WPF/MyMoney/Taxes/StateTaxes.cs Source/WPF/MyMoney.Business/Taxes/StateTaxes.cs
git mv Source/WPF/MyMoney/Taxes/StateTaxes.json Source/WPF/MyMoney.Business/Taxes/StateTaxes.json
git mv Source/WPF/MyMoney/Taxes/TaxCategory.cs Source/WPF/MyMoney.Business/Taxes/TaxCategory.cs
git mv Source/WPF/MyMoney/Taxes/TxfExporter.cs Source/WPF/MyMoney.Business/Taxes/TxfExporter.cs
git mv Source/WPF/MyMoney/Taxes/TxfSpec.txt Source/WPF/MyMoney.Business/Taxes/TxfSpec.txt
```

`.cs` files need no namespace change: both projects share `<RootNamespace>Walkabout</RootNamespace>`, and every file under `Taxes/` already declares `namespace Walkabout.Taxes` explicitly (verified in `CapitalGains.cs:6`) — that declaration is independent of which project compiles it.

- [ ] **Step 2: Move the explicit csproj item declarations for the data files**

In `Source/WPF/MyMoney/MyMoney.csproj`, remove these 4 lines (verified at lines 314, 606-608, 613-615, 616):

```xml
    <None Remove="Taxes\StateTaxes.json" />
```
```xml
    <Content Include="Taxes\FederalTaxes.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
```
```xml
    <Content Include="Taxes\StateTaxes.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
```
```xml
    <EmbeddedResource Include="Taxes\TxfSpec.txt" />
```

In `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`, add a new `<ItemGroup>` (before the closing `</Project>`):

```xml
  <ItemGroup>
    <None Remove="Taxes\StateTaxes.json" />
    <Content Include="Taxes\FederalTaxes.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
    <Content Include="Taxes\StateTaxes.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
    <EmbeddedResource Include="Taxes\TxfSpec.txt" />
  </ItemGroup>
```

- [ ] **Step 3: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors. If `MyMoney.csproj` call sites into `Walkabout.Taxes` types fail to resolve, confirm `MyMoney.csproj` still has its existing `ProjectReference` to `MyMoney.Business.csproj` (it does — verified at `MyMoney.csproj:649`).

- [ ] **Step 4: Run the existing Taxes tests plus the phase-1 safety net**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~CostBasisTests|FullyQualifiedName~TaxTests|FullyQualifiedName~SampleDataRegressionTests"`
Expected: all PASS, unchanged from before the move.

- [ ] **Step 5: Confirm the layer boundary still holds**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~LayerBoundaryTests"`
Expected: PASS (Taxes had zero WPF references, so this should be a no-op confirmation).

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney/Taxes Source/WPF/MyMoney.Business/Taxes Source/WPF/MyMoney/MyMoney.csproj Source/WPF/MyMoney.Business/MyMoney.Business.csproj
git commit -m "refactor: move Taxes into MyMoney.Business"
```

---

## Task 4: Add the `MyMoney.Business` UI-callback interfaces (shared infrastructure for Tasks 5-7)

**Files:**
- Create: `Source/WPF/MyMoney.Business/IBusinessLayerUiCallback.cs`
- Create: `Source/WPF/MyMoney/WpfBusinessLayerUiCallback.cs`
- Create: `Source/WPF/MyMoney.Business/Importers/IImportProgressReporter.cs`
- Create: `Source/WPF/MyMoney/Controls/DownloadControlProgressReporter.cs`
- Modify: `Source/WPF/MyMoney.Business/Properties/AssemblyInfo.cs` (add `InternalsVisibleTo("MyMoney")`)

**Interfaces:**
- Produces: `IBusinessLayerUiCallback` with `void ShowError(string message, string title)`, `bool Confirm(string message, string title)`, `bool ConfirmOkCancel(string message, string title)`.
- Produces: `IImportProgressReporter` with `void SetEntries(ThreadSafeObservableCollection<DownloadData> entries)`, `void SelectEntry(DownloadData entry)`.

This task exists because `StockQuoteManager` (Task 5), `QifImporter`/`CsvImportController`/`Exporters` (Task 6), and `Ofx.cs` (Task 8) each call `MessageBoxEx.Show` directly, and `QifImporter`/`CsvImportController` also hold a direct reference to the WPF `DownloadControl` `UserControl`. `MyMoney.Data` already solves the message-box problem for itself via `IDataLayerUiCallback` (`Source/WPF/MyMoney.Data/IDataLayerUiCallback.cs`), but `MyMoney.Business` cannot reference `MyMoney.Data` — `MyMoney.Data.csproj` already references `MyMoney.Business.csproj` (verified), so the reverse would be circular. Hence a separate, `Business`-side interface, same shape.

- [ ] **Step 1: Add `InternalsVisibleTo("MyMoney")` to `MyMoney.Business`**

Every subsystem being moved (StockQuotes, Importers, Ofx) has `internal` types/members that WPF-project-only files call across the assembly boundary once moved (verified: `Dialogs/ChangePasswordDialog.cs` calls `Ofx.cs`'s `internal async Task ChangePassword(...)`; several `StockQuoteManager` members are `internal`). Rather than widen dozens of individual members to `public`, follow the existing precedent in `Source/WPF/MyMoney.Business/Properties/AssemblyInfo.cs`, which already grants this to `MyMoney.Data` and `UnitTests`:

```csharp
[assembly: InternalsVisibleTo("MyMoney")]
```

Add this line alongside the existing `InternalsVisibleTo("MyMoney.Data")`/`InternalsVisibleTo("UnitTests")` lines.

- [ ] **Step 2: Create the `IBusinessLayerUiCallback` interface**

Create `Source/WPF/MyMoney.Business/IBusinessLayerUiCallback.cs`:

```csharp
namespace Walkabout.Data
{
    /// <summary>
    /// MyMoney.Business has no WPF dependency, so code that needs to show
    /// an error or ask the user a yes/no question (StockQuoteManager,
    /// the Importers, Ofx.cs's protocol-edge-case prompts) goes through
    /// this instead of calling MessageBoxEx directly. MyMoney.csproj
    /// supplies the real, WPF-backed implementation. Mirrors
    /// MyMoney.Data's IDataLayerUiCallback, which MyMoney.Business cannot
    /// reference directly (MyMoney.Data already references
    /// MyMoney.Business; the reverse would be circular).
    /// </summary>
    public interface IBusinessLayerUiCallback
    {
        void ShowError(string message, string title);
        bool Confirm(string message, string title);
        bool ConfirmOkCancel(string message, string title);
    }
}
```

- [ ] **Step 3: Create the WPF-backed implementation**

Create `Source/WPF/MyMoney/WpfBusinessLayerUiCallback.cs`:

```csharp
using System.Windows;
using Walkabout.Data;

namespace Walkabout
{
    public class WpfBusinessLayerUiCallback : IBusinessLayerUiCallback
    {
        public void ShowError(string message, string title)
        {
            MessageBoxEx.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Confirm(string message, string title)
        {
            return MessageBoxEx.Show(message, title, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        }

        public bool ConfirmOkCancel(string message, string title)
        {
            return MessageBoxEx.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK;
        }
    }
}
```

Verify `MessageBoxEx.Show`'s exact overloads in this codebase before compiling (used elsewhere in `MainWindow.xaml.cs`) — adjust parameter order/names if they differ from what's assumed here.

- [ ] **Step 4: Create the `IImportProgressReporter` interface**

Create `Source/WPF/MyMoney.Business/Importers/IImportProgressReporter.cs`:

```csharp
using Walkabout.Utilities;

namespace Walkabout.Importers
{
    /// <summary>
    /// QifImporter and CsvImportController used to hold a direct reference
    /// to the WPF DownloadControl (a UserControl) and manipulate its
    /// DownloadEventTree.ItemsSource/SelectEntry directly. This interface
    /// replaces that direct reference so both importers can live in
    /// MyMoney.Business. MyMoney.csproj supplies a thin adapter forwarding
    /// to the real DownloadControl.
    /// </summary>
    public interface IImportProgressReporter
    {
        void SetEntries(ThreadSafeObservableCollection<DownloadData> entries);
        void SelectEntry(DownloadData entry);
    }
}
```

Confirm `ThreadSafeObservableCollection<T>` and `DownloadData`'s actual namespaces before compiling — `DownloadData` currently lives in `Source/WPF/MyMoney/Importers/DownloadData.cs` under `namespace Walkabout.Importers` (moves in Task 6, so this reference will resolve once both are in `MyMoney.Business`); `ThreadSafeObservableCollection` needs locating (search `Source/WPF/MyMoney.Business/Utilities/` and `Source/WPF/MyMoney/Utilities/` — it's referenced from `QifImporter.cs` today, so it already compiles from wherever it currently lives).

- [ ] **Step 5: Create the WPF-backed `DownloadControl` adapter**

Create `Source/WPF/MyMoney/Controls/DownloadControlProgressReporter.cs`:

```csharp
using Walkabout.Importers;
using Walkabout.Utilities;

namespace Walkabout.Controls
{
    public class DownloadControlProgressReporter : IImportProgressReporter
    {
        private readonly DownloadControl control;

        public DownloadControlProgressReporter(DownloadControl control)
        {
            this.control = control;
        }

        public void SetEntries(ThreadSafeObservableCollection<DownloadData> entries)
        {
            this.control.DownloadEventTree.ItemsSource = entries;
        }

        public void SelectEntry(DownloadData entry)
        {
            this.control.SelectEntry(entry);
        }
    }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors. `DownloadData`/`ThreadSafeObservableCollection` resolution may fail until Task 6 physically moves `DownloadData.cs` — if so, it's fine to leave `IImportProgressReporter.cs` uncompilable-in-isolation and complete this build check as part of Task 6 instead; note that dependency explicitly if it comes up.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney.Business/IBusinessLayerUiCallback.cs Source/WPF/MyMoney/WpfBusinessLayerUiCallback.cs Source/WPF/MyMoney.Business/Importers/IImportProgressReporter.cs Source/WPF/MyMoney/Controls/DownloadControlProgressReporter.cs Source/WPF/MyMoney.Business/Properties/AssemblyInfo.cs
git commit -m "feat: add MyMoney.Business UI-callback interfaces for the subsystem migration"
```

---

## Task 5: Migrate StockQuotes

**Files:**
- Move: all 9 files in `Source/WPF/MyMoney/StockQuotes/` → `Source/WPF/MyMoney.Business/StockQuotes/`
- Modify: `Source/WPF/MyMoney.Business/StockQuotes/ExchangeRateService.cs` (remove unused `System.Windows.Documents`/`System.Windows.Forms` usings)
- Modify: `Source/WPF/MyMoney.Business/StockQuotes/TwelveData.cs` (remove unused `System.Windows.*` usings)
- Modify: `Source/WPF/MyMoney.Business/StockQuotes/StockQuoteManager.cs` (add `IBusinessLayerUiCallback` property, replace the one `MessageBoxEx.Show` call)
- Modify: `Source/WPF/MyMoney/MainWindow.xaml.cs` (wire `WpfBusinessLayerUiCallback` into `StockQuoteManager` construction)
- Modify: `Source/WPF/MyMoney/MyMoney.csproj:327` (move the `StockQuotes\Design.dgml` `<None Include>` entry)
- Test: reuse `SampleDataRegressionTests` (Task 2) as the safety net; no new unit tests required beyond confirming existing coverage still passes

**Interfaces:**
- Consumes: `IBusinessLayerUiCallback` (Task 4).
- Produces: `StockQuoteManager.UiCallback` settable property (`IBusinessLayerUiCallback`, defaults `null`).

- [ ] **Step 1: Move the 9 files**

```bash
git mv Source/WPF/MyMoney/StockQuotes/*.cs Source/WPF/MyMoney.Business/StockQuotes/
```

(Run `ls Source/WPF/MyMoney/StockQuotes/` first to confirm the exact 9 filenames match what's expected: `ExchangeRateService.cs`, `IOnlineService.cs`, `MarketStack.cs`, `Polygon.cs`, `StockQuoteManager.cs`, `StockQuoteThrottle.cs`, `ThrottledStockQuoteService.cs`, `TwelveData.cs`, `Yahoo.cs`.)

- [ ] **Step 2: Move the `Design.dgml` csproj entry**

In `Source/WPF/MyMoney/MyMoney.csproj`, remove:
```xml
    <None Include="StockQuotes\Design.dgml" />
```
In `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`, add it to the `<ItemGroup>` created in Task 3 (or a new one):
```xml
    <None Include="StockQuotes\Design.dgml" />
```
Also `git mv Source/WPF/MyMoney/StockQuotes/Design.dgml Source/WPF/MyMoney.Business/StockQuotes/Design.dgml` if it wasn't already swept up by the wildcard move in Step 1 (it's a `.dgml`, not `.cs`, so it wasn't).

- [ ] **Step 3: Remove the unused WPF usings from `ExchangeRateService.cs` and `TwelveData.cs`**

In `ExchangeRateService.cs`, delete:
```csharp
using System.Windows.Documents;
using System.Windows.Forms;
```
In `TwelveData.cs`, delete:
```csharp
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Interop;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
```
(Verified during research: neither file has any real symbol usage from these namespaces — only `UiDispatcher.Invoke`/`BeginInvoke` calls remain in `ExchangeRateService.cs`, and `UiDispatcher` is already a portable `SynchronizationContext`-based utility from the #7 rewrite, not a WPF type.)

- [ ] **Step 4: Wire `StockQuoteManager`'s `MessageBoxEx.Show` call through the new interface**

In `StockQuoteManager.cs`, remove `using System.Windows;` (and any other now-unused WPF usings from that file) and add a settable callback property near the top of the class:

```csharp
public IBusinessLayerUiCallback UiCallback { get; set; }
```

Replace (originally at line 639, verified):
```csharp
MessageBoxEx.Show(e.ToString(), Walkabout.Properties.Resources.StockQuotesException, MessageBoxButton.OK, MessageBoxImage.Error);
```
with:
```csharp
this.UiCallback?.ShowError(e.ToString(), Walkabout.Properties.Resources.StockQuotesException);
```

(`Walkabout.Properties.Resources` is generated from `MyMoney.Business/Properties/Resources.resx`, already `public` per the existing `PublicResXFileCodeGenerator` setting in `MyMoney.Business.csproj` — confirmed usable from this file already.)

- [ ] **Step 5: Wire the real implementation from `MainWindow.xaml.cs`**

Find `StockQuoteManager`'s construction site(s) in `MainWindow.xaml.cs` (search for `new StockQuoteManager(`) and set the callback immediately after construction:

```csharp
this.stockQuotes = new StockQuoteManager(provider, serviceSettings, logPath)
{
    UiCallback = new WpfBusinessLayerUiCallback()
};
```

(Adjust variable names to match whatever the actual local/field assignment looks like at that call site — do not guess the surrounding code without opening the file at the real line.)

- [ ] **Step 6: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors. If `internal` members of moved `StockQuotes` types fail to resolve from WPF-project-only call sites (e.g. `ExchangeRateService.IsMySettings`, called from `MainWindow.xaml.cs:836`), confirm Task 4's `InternalsVisibleTo("MyMoney")` was added correctly.

- [ ] **Step 7: Run the safety net**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SampleDataRegressionTests|FullyQualifiedName~LayerBoundaryTests"`
Expected: all PASS.

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney/StockQuotes Source/WPF/MyMoney.Business/StockQuotes Source/WPF/MyMoney/MainWindow.xaml.cs Source/WPF/MyMoney/MyMoney.csproj Source/WPF/MyMoney.Business/MyMoney.Business.csproj
git commit -m "refactor: move StockQuotes into MyMoney.Business"
```

---

## Task 6: Migrate Importers + MainWindow's import dispatch

**Files:**
- Move: all 7 files in `Source/WPF/MyMoney/Importers/` → `Source/WPF/MyMoney.Business/Importers/`
- Modify: `Source/WPF/MyMoney.Business/Importers/CsvImportController.cs` (wire `IBusinessLayerUiCallback` + `IImportProgressReporter`, remove `System.Windows`/`System.Windows.Input` usings)
- Modify: `Source/WPF/MyMoney.Business/Importers/CsvImporter.cs` (remove the `Application.Current.MainWindow` owner assignment — see Step 3)
- Modify: `Source/WPF/MyMoney.Business/Importers/QifImporter.cs` (wire `IBusinessLayerUiCallback` + `IImportProgressReporter`, remove `using System.Windows;`)
- Modify: `Source/WPF/MyMoney.Business/Importers/Exporters.cs` (wire `IBusinessLayerUiCallback`, remove `using System.Windows;`)
- Modify: `Source/WPF/MyMoney.Business/Importers/DownloadData.cs` (remove `using System.Windows;` if unused — verify first, `UiDispatcher` call was the only real hit found)
- Modify: `Source/WPF/MyMoney/MainWindow.xaml.cs:1828,2510-2546,4169-4293` (`ImportQif`/`ImportOfx`/`ImportXml`/`ImportMoneyFile`/`ImportCsv`/`OnCommandFileImport`: wire the new callbacks when constructing importers)
- Test: reuse `SampleDataRegressionTests`

**Interfaces:**
- Consumes: `IBusinessLayerUiCallback`, `IImportProgressReporter` (Task 4).

- [ ] **Step 1: Move the 7 files**

```bash
git mv Source/WPF/MyMoney/Importers/*.cs Source/WPF/MyMoney.Business/Importers/
```

(`CsvImportController.cs`, `CsvImporter.cs`, `DownloadData.cs`, `Exporters.cs`, `Importer.cs`, `QifImporter.cs`, `XmlImporter.cs`.)

- [ ] **Step 2: Check `DownloadData.cs`'s `System.Windows` using is actually needed**

Open `DownloadData.cs`; the only WPF-flagged line found during research was `using System.Windows;` at the top. If no `System.Windows`-namespace symbol is used in the file body (check for `Dispatcher`/`UiDispatcher` usage — if it's `UiDispatcher`, that's already portable per the Task 5 precedent), delete the using. If genuine WPF usage exists that wasn't caught, treat it the same way as the other files (route through `IBusinessLayerUiCallback` or leave the file's WPF-touching method as a thin partial left in `MyMoney.csproj` — do not silently skip building this file).

- [ ] **Step 3: Fix `CsvImporter.cs`'s dialog-owner assignment**

At the original line 311 (verified): `cd.Owner = System.Windows.Application.Current.MainWindow;` — this sets a `System.Windows.Forms.ColorDialog`'s (or similar) owner window. Since `MyMoney.Business` can't reference `System.Windows.Application`, this single line needs to move to the WPF-project-only caller. Find where this method (containing `cd`) is invoked from `MyMoney.csproj`, and either:
  - pass the owner `Window` in as a parameter from the WPF-project call site, or
  - if `cd` (the dialog) is itself only ever shown from a WPF-project call site already, move just that dialog-showing sub-method (not the whole file) to stay in `MyMoney.csproj` as a small partial helper.

Open the actual method containing line 311 before deciding — this plan can't specify the exact split without seeing the surrounding method body, which wasn't part of the research pass. Flag this as the one sub-step in this task needing an in-context read before editing.

- [ ] **Step 4: Wire `CsvImportController`'s `MessageBoxEx.Show` and `DownloadControl` coupling**

Add to `CsvImportController.cs` (constructor currently `public CsvImportController(DownloadControl control, MyMoney money, string databaseDir, StockQuoteCache cache)`):

Change the constructor signature to take the two new interfaces instead of the concrete `DownloadControl`:
```csharp
public CsvImportController(IImportProgressReporter progressReporter, IBusinessLayerUiCallback uiCallback, MyMoney money, string databaseDir, StockQuoteCache cache)
{
    this.progressReporter = progressReporter;
    this.uiCallback = uiCallback;
    // ... existing assignments for money, databaseDir, cache
}
```

Replace (originally at line 39, verified): `this.control.DownloadEventTree.ItemsSource = entries;` with `this.progressReporter.SetEntries(entries);`

Replace (originally at line 86, verified): `this.control.SelectEntry(last);` with `this.progressReporter.SelectEntry(last);`

Replace (originally at line 96, verified): `MessageBoxEx.Show(ex.Message, "Import Error", MessageBoxButton.OK, MessageBoxImage.Exclamation);` with `this.uiCallback?.ShowError(ex.Message, "Import Error");`

- [ ] **Step 5: Wire `QifImporter`'s coupling the same way**

Change the constructor (currently `public QifImporter(DownloadControl control, MyMoney myMoney)`) to:
```csharp
public QifImporter(IImportProgressReporter progressReporter, IBusinessLayerUiCallback uiCallback, MyMoney myMoney)
    : base(myMoney)
{
    this.progressReporter = progressReporter;
    this.uiCallback = uiCallback;
}
```

Replace (line 31, verified): `this.control.DownloadEventTree.ItemsSource = entries;` with `this.progressReporter.SetEntries(entries);`

Replace the 3 `MessageBoxEx.Show` calls (lines 45, 56, 66, verified) with `this.uiCallback?.Confirm(...)` (for the two Yes/No prompts) and `this.uiCallback?.ShowError(...)` (for the "You must first select an account" acknowledgment-only one) — open the file to match each call's exact message string when substituting, since this plan's research captured the calls' presence and button type but not their full message text.

- [ ] **Step 6: Wire `Exporters`'s single `MessageBoxEx.Show` call**

Add an `IBusinessLayerUiCallback` dependency to `Exporters` (constructor injection, matching the pattern above) and replace (line 69, verified):
```csharp
MessageBoxEx.Show("Error exporting rows\n" + e.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
```
with:
```csharp
this.uiCallback?.ShowError("Error exporting rows\n" + e.Message, "Export Error");
```

- [ ] **Step 7: Update `MainWindow.xaml.cs`'s construction sites**

`ImportQif` (line 1828), `ImportCsv` (called from `OnCommandFileImport`, line 4169 area) construct `QifImporter`/`CsvImportController` today with a `DownloadControl` argument. Update each construction site to pass `new DownloadControlProgressReporter(<the control>)` and `new WpfBusinessLayerUiCallback()` instead:

```csharp
var importer = new QifImporter(new DownloadControlProgressReporter(this.downloadControl), new WpfBusinessLayerUiCallback(), this.myMoney);
```

(Substitute the real local/field name for the `DownloadControl` instance at each call site — open `MainWindow.xaml.cs` at the noted line ranges to confirm it before editing.)

- [ ] **Step 8: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors.

- [ ] **Step 9: Run the safety net**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SampleDataRegressionTests|FullyQualifiedName~LayerBoundaryTests"`
Expected: all PASS.

- [ ] **Step 10: Commit**

```bash
git add Source/WPF/MyMoney/Importers Source/WPF/MyMoney.Business/Importers Source/WPF/MyMoney/MainWindow.xaml.cs
git commit -m "refactor: move Importers into MyMoney.Business, cut DownloadControl/MessageBox coupling"
```

---

## Task 7: Migrate MainWindow's file/database lifecycle

**Files:**
- Modify: `Source/WPF/MyMoney/MainWindow.xaml.cs` (extract `LoadDatabase`, `BeginLoadDatabase`, `CreateNewDatabase`, `Save`/`SaveIfDirty`, `SaveAsSqlCe`/`SaveAsSqlite`/`SaveAsXml`/`SaveAsBinaryXml`, `ExportCsv` — lines 1917-2547 area, verified)
- Create: `Source/WPF/MyMoney.Business/DatabaseLifecycle.cs`

**Interfaces:**
- Produces: whatever public surface `DatabaseLifecycle` ends up exposing — determined during this task, not pre-specified here.

This task needs an in-context read of `MainWindow.xaml.cs:1913-2547` (the "Data" region) before writing concrete steps: the spec's survey identified these methods as decision logic, but the exact split between "genuinely portable" and "needs a WPF callback" (dialogs for `Save As` file pickers, password prompts via `PromptForPassword` at line 2348) requires seeing the full method bodies, which this plan's research pass didn't capture line-by-line (only signatures and line numbers, gathered for scoping, not extraction).

- [ ] **Step 1: Read the full "Data" region**

Read `Source/WPF/MyMoney/MainWindow.xaml.cs:1913-2547` in full.

- [ ] **Step 2: Classify each method**

For each of `BeginLoadDatabase`, `LoadDatabase` (both overloads), `CreateNewDatabase`, `SaveIfDirty` (both overloads), `Save`, `PromptForPassword`, `SaveAsSqlCe`, `SaveNewDatabase`, `SaveAsSqlite`, `SaveAsXml`, `SaveAsBinaryXml`, `ExportCsv`: determine whether it (a) only orchestrates `IDatabase`/`MyMoney.Business`/`MyMoney.Data` calls (portable, move to `DatabaseLifecycle.cs`), or (b) genuinely shows a WPF dialog/file picker (stays in `MainWindow.xaml.cs`, calls into the portable piece).

- [ ] **Step 3: Extract the portable methods to `MyMoney.Business/DatabaseLifecycle.cs`, following Task 4/5/6's `IBusinessLayerUiCallback` pattern for any error/confirm dialogs found**

- [ ] **Step 4: Update `MainWindow.xaml.cs` call sites to delegate to the new class**

- [ ] **Step 5: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors.

- [ ] **Step 6: Run the safety net plus a manual smoke check**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SampleDataRegressionTests|FullyQualifiedName~LayerBoundaryTests"`
Then manually run the app (`dotnet run --project Source/WPF/MyMoney/MyMoney.csproj`) and confirm File > New, File > Open, File > Save, File > Save As, and File > Export all still work — this task touches the most operationally sensitive code in the app (data loss risk if `Save` breaks silently), so don't skip the manual pass even though the automated suite is green.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney/MainWindow.xaml.cs Source/WPF/MyMoney.Business/DatabaseLifecycle.cs
git commit -m "refactor: move MainWindow's file/database lifecycle into MyMoney.Business"
```

---

## Task 8: Migrate Ofx-parsing

**Files:**
- Move: `Source/WPF/MyMoney/Ofx/OfxObjectModel.cs`, `SgmlParser.cs`, `SgmlReader.cs`, `OfxInstitutionInfo.cs`, `HtmlResponseException.cs`, `OfxStrings.Designer.cs`, `OfxStrings.resx` → `Source/WPF/MyMoney.Business/Ofx/`
- Modify: `Source/WPF/MyMoney.Business/Ofx/Ofx.cs` (wire `IBusinessLayerUiCallback` for its 3 `MessageBoxEx.Show` calls, then move it too)
- Keep in place: `Source/WPF/MyMoney/Ofx/OfxDownloadController.cs` (genuine UI controller — see rationale below)
- Modify: `Source/WPF/MyMoney/MyMoney.csproj` (move the 5 embedded-resource entries: `ofx201.dtd`, `MfaPhrases.xml`, `ofx160.dtd`, `OfxProviderList.xml`, `OfxErrorTemplate.htm`; and 2 `None` entries: `OFX 2.1.1.pdf`, `ofx16.pdf`)
- Modify: `Source/WPF/MyMoney.Business/MyMoney.Business.csproj` (add the matching entries)

**Interfaces:**
- Consumes: `IBusinessLayerUiCallback` (Task 4).

**Rationale for keeping `OfxDownloadController.cs` in the WPF project:** verified during research that its `System.Windows` usage isn't a stray dialog call like the others — it's `Application.Current.MainWindow` used 4 times as a dialog `.Owner` for what are, per its name, genuine UI flows (login/MFA prompts during a download). This is real UI orchestration, not decision logic wearing a UI costume, so it stays — consistent with the spec's own framing of what counts as "genuinely UI."

- [ ] **Step 1: Move the 5 parsing-related `.cs`/resource files**

```bash
git mv Source/WPF/MyMoney/Ofx/OfxObjectModel.cs Source/WPF/MyMoney.Business/Ofx/OfxObjectModel.cs
git mv Source/WPF/MyMoney/Ofx/SgmlParser.cs Source/WPF/MyMoney.Business/Ofx/SgmlParser.cs
git mv Source/WPF/MyMoney/Ofx/SgmlReader.cs Source/WPF/MyMoney.Business/Ofx/SgmlReader.cs
git mv Source/WPF/MyMoney/Ofx/OfxInstitutionInfo.cs Source/WPF/MyMoney.Business/Ofx/OfxInstitutionInfo.cs
git mv Source/WPF/MyMoney/Ofx/HtmlResponseException.cs Source/WPF/MyMoney.Business/Ofx/HtmlResponseException.cs
git mv Source/WPF/MyMoney/Ofx/OfxStrings.Designer.cs Source/WPF/MyMoney.Business/Ofx/OfxStrings.Designer.cs
git mv Source/WPF/MyMoney/Ofx/OfxStrings.resx Source/WPF/MyMoney.Business/Ofx/OfxStrings.resx
```

- [ ] **Step 2: Move the associated data files and their csproj entries**

```bash
git mv "Source/WPF/MyMoney/Ofx/ofx201.dtd" "Source/WPF/MyMoney.Business/Ofx/ofx201.dtd"
git mv "Source/WPF/MyMoney/Ofx/MfaPhrases.xml" "Source/WPF/MyMoney.Business/Ofx/MfaPhrases.xml"
git mv "Source/WPF/MyMoney/Ofx/ofx160.dtd" "Source/WPF/MyMoney.Business/Ofx/ofx160.dtd"
git mv "Source/WPF/MyMoney/Ofx/OfxProviderList.xml" "Source/WPF/MyMoney.Business/Ofx/OfxProviderList.xml"
git mv "Source/WPF/MyMoney/Ofx/OfxErrorTemplate.htm" "Source/WPF/MyMoney.Business/Ofx/OfxErrorTemplate.htm"
git mv "Source/WPF/MyMoney/Ofx/OFX 2.1.1.pdf" "Source/WPF/MyMoney.Business/Ofx/OFX 2.1.1.pdf"
git mv "Source/WPF/MyMoney/Ofx/ofx16.pdf" "Source/WPF/MyMoney.Business/Ofx/ofx16.pdf"
```

In `Source/WPF/MyMoney/MyMoney.csproj`, remove these 7 lines (verified at lines 325, 329, 617, 622, 623, 628, 629):
```xml
    <EmbeddedResource Include="Ofx\ofx201.dtd" />
    <EmbeddedResource Include="Ofx\MfaPhrases.xml" />
    <EmbeddedResource Include="Ofx\ofx160.dtd" />
    <EmbeddedResource Include="Ofx\OfxProviderList.xml" />
    <EmbeddedResource Include="Ofx\OfxErrorTemplate.htm" />
    <None Include="Ofx\OFX 2.1.1.pdf" />
    <None Include="Ofx\ofx16.pdf" />
```

In `Source/WPF/MyMoney.Business/MyMoney.Business.csproj`, add the same 7 lines to an `<ItemGroup>`.

- [ ] **Step 3: Wire `Ofx.cs`'s `MessageBoxEx.Show` calls through `IBusinessLayerUiCallback`**

`Ofx.cs`'s `MessageBoxEx.Show` calls are on `OfxRequest` (constructed from 10+ call sites across `Dialogs/` and `Ofx/OfxDownloadController.cs`, verified). Rather than change `OfxRequest`'s constructor (which would touch all 10+ callers), add a settable property matching the `StockQuoteManager` pattern from Task 5:

```csharp
public IBusinessLayerUiCallback UiCallback { get; set; }
```

Replace the 3 calls (originally lines 169, 1715-1716, 1725-1726, verified):
```csharp
MessageBoxEx.Show("Error opening import file", null, ex.Message, MessageBoxButton.OK, MessageBoxImage.Error);
```
→
```csharp
this.UiCallback?.ShowError($"Error opening import file\n{ex.Message}", "Error");
```

```csharp
if (MessageBoxEx.Show(string.Format(@"Unexpected CHALLENGERQ or CHALLENGERS element, not implemented.
Please save the log file '{0}' so we can implement this", GetLogFileLocation(doc)), "CHALLENGERQ", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.Cancel)
```
→
```csharp
if (!(this.UiCallback?.ConfirmOkCancel(string.Format(@"Unexpected CHALLENGERQ or CHALLENGERS element, not implemented.
Please save the log file '{0}' so we can implement this", GetLogFileLocation(doc)), "CHALLENGERQ") ?? true))
```

(Same substitution shape for the PINCHRQ/PINCHRS case at the other line.) Note the `?? true` default: if no callback is wired, preserve the original "OK" (continue) path rather than silently cancelling — pick whichever default is actually safer once you see how the caller handles the `if` branch's consequences; don't assume `true` is correct without checking what happens on each path.

Remove `using System.Windows;` and `using Dispatcher = System.Windows.Threading.Dispatcher;` from `Ofx.cs` once the `MessageBoxEx` calls are gone — confirm the `Dispatcher` alias isn't used anywhere else in the file first (the `UiDispatcher.Invoke`/`BeginInvoke` calls found during research use the already-portable `UiDispatcher`, not a raw `Dispatcher`, so this alias is likely unused, but verify before deleting).

- [ ] **Step 4: Move `Ofx.cs` itself**

```bash
git mv Source/WPF/MyMoney/Ofx/Ofx.cs Source/WPF/MyMoney.Business/Ofx/Ofx.cs
```

- [ ] **Step 5: Wire the real implementation from callers**

`OfxRequest`'s 10+ construction sites (in `Dialogs/*.cs` and `Ofx/OfxDownloadController.cs`, all staying in `MyMoney.csproj`) need `req.UiCallback = new WpfBusinessLayerUiCallback();` added after each `new OfxRequest(...)` call. Search for `new OfxRequest(` across `Source/WPF/MyMoney/Dialogs/*.cs` and `Source/WPF/MyMoney/Ofx/OfxDownloadController.cs` and add the assignment at each site.

- [ ] **Step 6: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors. `OfxDownloadController.cs` staying in `MyMoney.csproj` still needs a reference to the moved `Ofx.cs`/`OfxObjectModel.cs` types — confirm `MyMoney.csproj`'s existing `ProjectReference` to `MyMoney.Business.csproj` covers this (it does; no new reference needed).

- [ ] **Step 7: Run the full test suite (final check for phase 1)**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj` and `dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj`
Expected: all green.

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney/Ofx Source/WPF/MyMoney.Business/Ofx Source/WPF/MyMoney/Dialogs Source/WPF/MyMoney/MyMoney.csproj Source/WPF/MyMoney.Business/MyMoney.Business.csproj
git commit -m "refactor: move Ofx parsing into MyMoney.Business, keep OfxDownloadController's UI orchestration in place"
```

---

## Task 9: Final validation and formal closure of #1

**Files:** None modified — verification only.

- [ ] **Step 1: Run the complete safety net**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SampleDataRegressionTests"`
Expected: all 4 tests (3 invariants + canary) still PASS after all 4 subsystem migrations — this is the cross-subsystem check the per-task runs couldn't catch individually.

- [ ] **Step 2: Full build and test suite**

Run: `dotnet build Source/WPF/MyMoney.sln` (Debug and `-c Release`), then `dotnet test Source/WPF/MyMoney.sln`
Expected: 0 errors both configs; tests green (excluding the pre-existing, documented `ScenarioTest` gap that needs the DGML Test Monitor tooling).

- [ ] **Step 3: Re-verify the layer boundary**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~LayerBoundaryTests"`
Expected: PASS — `MyMoney.Business` still has zero WPF-family assembly references despite absorbing 4 subsystems' worth of code.

- [ ] **Step 4: Interactive FlaUI smoke check**

Run `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~PayeeSelectionTests"` from an interactive desktop session (not a background/headless job — see `docs/dev/index.md` and this repo's FlaUI skill notes on why).
Expected: PASS, confirming the moved import/lifecycle code didn't regress real end-to-end behavior.

- [ ] **Step 5: Close issue #1**

```bash
gh issue close 1 --repo markabrandjord/MyMoney.Net --reason completed --comment "Item #3's remaining slices (SQL Server contract-test flavor, UiDispatcher portability) closed via #24/#13. This migration (see docs/superpowers/specs/2026-09-18-business-layer-subsystem-migration-design.md) completed the layer-extraction goal by moving Taxes/StockQuotes/Importers/Ofx-parsing/MainWindow-lifecycle into MyMoney.Business. Closing as complete."
```

- [ ] **Step 6: Update #5 with phase-1 completion status**

```bash
gh issue comment 5 --repo markabrandjord/MyMoney.Net --body "Phase 1 (Taxes, StockQuotes, Importers, Ofx-parsing, MainWindow file/database lifecycle) is complete — see docs/superpowers/specs/2026-09-18-business-layer-subsystem-migration-design.md and docs/superpowers/plans/2026-09-18-business-layer-subsystem-migration.md. Remaining scope per that spec: Reports (needs a new business-layer query/aggregation API), MainWindow's remaining UI-only orchestration (not committed to), dual-engine parity and FlaUI-level testing (deferred from #4). Next up, if picked up: Reports redesign as its own spec."
```

- [ ] **Step 7: Final commit (if any doc updates were made)**

```bash
git add -A
git commit -m "docs: mark phase 1 of business-layer subsystem migration complete"
```
