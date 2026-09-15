# FlaUI Test Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a small, independent FlaUI-based UI test project that can reliably launch `MyMoney.exe`, drive it through opening a database file via the native File→Open dialog, select a payee, and confirm the app survives — the exact scenario raw UI Automation failed to automate during item #2's live verification.

**Architecture:** A new `net10.0-windows7.0` NUnit test project (`Source/WPF/UITests`) that drives the built `MyMoney.exe` as an external process via `FlaUI.Core`/`FlaUI.UIA3`, independent of `ScenarioTest`. Three tasks: a minimal launch/close smoke test to prove FlaUI can see the app at all, a checked-in SQLite fixture file, and the full payee-selection scenario test.

**Tech Stack:** C# / .NET 10.0, `FlaUI.Core` 5.0.0, `FlaUI.UIA3` 5.0.0, NUnit 4.6.1 (matching `UnitTests.csproj`'s existing versions).

**Spec:** `docs/superpowers/specs/2026-09-14-flaui-test-harness-design.md`

## Global Constraints

- Independent of `ScenarioTest` — no files under `Source/WPF/ScenarioTest` are touched by this plan.
- No custom test-framework/page-object abstraction layer — direct, straightforward FlaUI calls in the test files themselves.
- The SQL Server engine path is out of scope — every test in this plan uses the SQLite fixture only.
- `UITests.csproj` is not referenced by `MyMoney.sln`'s Release configuration build (DEBUG-only dev tooling, consistent with the rest of this initiative).
- FlaUI's exact API surface (method/property names) should be verified against the actually-installed 5.0.0 package if any code below doesn't compile — treat the code in this plan as a strong, well-researched starting point, not a guarantee; note and explain any deviation in the task's report, the same way earlier plans in this initiative handled MSBuild syntax that needed adjusting once tested for real.

---

## Task 1: Scaffold `UITests` project with a launch/close smoke test

**Files:**
- Create: `Source/WPF/UITests/UITests.csproj`
- Create: `Source/WPF/UITests/LaunchSmokeTests.cs`
- Modify: `Source/WPF/MyMoney.sln` (add the new project)

**Interfaces:**
- Produces: a working, FlaUI-referencing NUnit test project that can launch and close `MyMoney.exe`. Relied on by Tasks 2 and 3.

This is the foundational checkpoint: prove FlaUI can launch the app and see its main window before attempting anything harder. If this doesn't work reliably, nothing else in this plan will either, and that needs to be known now.

- [ ] **Step 1: Create the project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows7.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="NUnit" Version="4.6.1" />
    <PackageReference Include="NUnit3TestAdapter" Version="6.2.0" />
    <PackageReference Include="NUnit.Analyzers" Version="4.14.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="10.0.1">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FlaUI.Core" Version="5.0.0" />
    <PackageReference Include="FlaUI.UIA3" Version="5.0.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add to the solution**

```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/UITests/UITests.csproj
```

- [ ] **Step 3: Write the launch/close smoke test**

```csharp
using System;
using System.IO;
using FlaUI.Core;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests
{
    public class LaunchSmokeTests
    {
        internal static string MyMoneyExePath =>
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "MyMoney", "bin", "Debug", "net10.0-windows7.0", "win-x64", "MyMoney.exe");

        [Test]
        public void LaunchAndClose_MainWindowAppears()
        {
            Assert.That(File.Exists(MyMoneyExePath), Is.True,
                $"MyMoney.exe not found at {MyMoneyExePath}. Build Source/WPF/MyMoney.csproj first.");

            using var app = Application.Launch(MyMoneyExePath);
            using var automation = new UIA3Automation();

            var mainWindow = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

            Assert.That(mainWindow, Is.Not.Null);
            Assert.That(mainWindow.Title, Does.Contain("MyMoney"));

            app.Close();
        }
    }
}
```

- [ ] **Step 4: Build the main app first (the smoke test needs it to exist)**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors, `MyMoney.exe` present at the path referenced above.

- [ ] **Step 5: Run the smoke test**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~LaunchAndClose_MainWindowAppears"`
Expected: PASS. If `MyMoney.exe`'s path resolution in `MyMoneyExePath` doesn't match your actual build output layout (RID-specific folders can vary), adjust the path construction and note why in your report — this is exactly the kind of environment-specific detail worth getting right by testing, not guessing.

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/UITests/UITests.csproj Source/WPF/UITests/LaunchSmokeTests.cs Source/WPF/MyMoney.sln
git commit -m "Scaffold UITests project with a FlaUI launch/close smoke test"
```

---

## Task 2: Generate and commit the SQLite fixture file

**Files:**
- Create: `Source/WPF/UITests/FixtureGenerator.cs`
- Create: `Source/WPF/UITests/Fixtures/PayeeSmokeTest.mmdb` (binary, generated by Step 1-2 below, then committed)
- Modify: `Source/WPF/UITests/UITests.csproj` (reference `MyMoney.csproj` for `Walkabout.Data` types, and copy the fixture to output)

**Interfaces:**
- Produces: `Fixtures/PayeeSmokeTest.mmdb`, a SQLite database with one account, one payee ("Payee Smoke Test"), one transaction. Relied on by Task 3.

The generator is kept in the repo (as an `[Explicit]` NUnit test — excluded from normal test runs, but re-runnable on demand) rather than a throwaway script, so regenerating the fixture later is documented and reproducible.

- [ ] **Step 1: Add the `MyMoney.csproj` reference and fixture copy-to-output**

Add to `Source/WPF/UITests/UITests.csproj`, inside the existing `<Project>` element:

```xml
  <ItemGroup>
    <ProjectReference Include="..\MyMoney\MyMoney.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Fixtures\PayeeSmokeTest.mmdb" Condition="Exists('Fixtures\PayeeSmokeTest.mmdb')">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 2: Write the fixture generator**

```csharp
using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UITests
{
    /// <summary>
    /// Regenerates Fixtures/PayeeSmokeTest.mmdb. Marked [Explicit] so it
    /// never runs as part of a normal test pass -- the fixture is a
    /// checked-in binary, not something rebuilt on every run. Re-run this
    /// deliberately (e.g. `dotnet test --filter FixtureGenerator`) only
    /// when the fixture's shape needs to change.
    /// </summary>
    [Explicit("Regenerates the checked-in fixture file; not part of normal test runs.")]
    public class FixtureGenerator
    {
        [Test]
        public void RegeneratePayeeSmokeTestFixture()
        {
            string outputPath = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Fixtures", "PayeeSmokeTest.mmdb");
            outputPath = Path.GetFullPath(outputPath);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var money = new MyMoney();

            var payee = money.Payees.AddPayee(1);
            payee.Name = "Payee Smoke Test";

            var account = new Account();
            account.Name = "Smoke Test Checking";
            money.Accounts.AddAccount(account);

            var transaction = new Transaction();
            transaction.Account = account;
            transaction.Date = new DateTime(2026, 1, 1);
            transaction.Amount = -10.00M;
            transaction.Payee = payee;
            money.Transactions.AddTransaction(transaction);

            var db = new SqliteDatabase();
            db.DatabasePath = outputPath;
            db.Create();
            db.Save(money);

            Assert.That(File.Exists(outputPath), Is.True);
            TestContext.WriteLine($"Wrote fixture to {outputPath}");
        }
    }
}
```

- [ ] **Step 3: Run the generator once to produce the fixture**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~RegeneratePayeeSmokeTestFixture"`

NUnit skips `[Explicit]` tests by default under a name-only filter match on the containing class/method unless explicitly selected — if this filter doesn't pick it up, use `dotnet test Source/WPF/UITests/UITests.csproj --filter "TestCategory=Explicit"` or pass `--filter` matching the fully-qualified test name exactly; confirm whichever form actually runs it by checking for the `Wrote fixture to ...` output line, not just a "0 tests run, considered passing" result.

Expected: `Source/WPF/UITests/Fixtures/PayeeSmokeTest.mmdb` now exists.

- [ ] **Step 4: Verify the fixture's shape**

Run a quick manual check (e.g. reuse the `SqlMappingTests.cs` pattern of opening it with `SqliteDatabase` and asserting `money.Payees.Count == 1` and the payee's name) — or simply confirm via the file's existence and non-trivial size (a few KB, not 0 bytes) that `Create()`/`Save()` actually wrote real content.

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/UITests/UITests.csproj Source/WPF/UITests/FixtureGenerator.cs Source/WPF/UITests/Fixtures/PayeeSmokeTest.mmdb
git commit -m "Add checked-in SQLite fixture and its regeneration test"
```

---

## Task 3: The payee-selection scenario test

**Files:**
- Create: `Source/WPF/UITests/PayeeSelectionTests.cs`

**Interfaces:**
- Consumes: `LaunchSmokeTests.MyMoneyExePath` (Task 1) — reuse the same path-resolution logic rather than duplicating it (extract to a small shared internal helper class if that reads more cleanly, e.g. `TestPaths.MyMoneyExePath`).
- Produces: the spec's acceptance-criterion test.

This is the actual deliverable this whole plan exists for. Every step of the "First Test" section in the spec, as one continuous scenario.

- [ ] **Step 1: Write the scenario test**

```csharp
using System;
using System.IO;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Walkabout.UITests
{
    public class PayeeSelectionTests
    {
        private Application app;
        private UIA3Automation automation;

        [TearDown]
        public void TearDown()
        {
            try
            {
                app?.Close();
            }
            catch
            {
                // best-effort cleanup; a test failure shouldn't be masked by a teardown exception
            }
            app?.Dispose();
            automation?.Dispose();
        }

        [Test]
        public void SelectingAPayee_AfterOpeningFixtureFile_DoesNotCrash()
        {
            string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "PayeeSmokeTest.mmdb");
            Assert.That(File.Exists(fixturePath), Is.True,
                $"Fixture not found at {fixturePath}. Run Task 2's FixtureGenerator first.");

            app = Application.Launch(LaunchSmokeTests.MyMoneyExePath);
            automation = new UIA3Automation();
            Window mainWindow = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

            OpenFileViaFileMenu(mainWindow, fixturePath);

            // The window title reflects the opened file once loading completes.
            Retry.WhileFalse(
                () => mainWindow.Title.IndexOf("PayeeSmokeTest", StringComparison.OrdinalIgnoreCase) >= 0,
                TimeSpan.FromSeconds(10));
            Assert.That(mainWindow.Title, Does.Contain("PayeeSmokeTest").IgnoreCase);

            AutomationElement payeesHeader = mainWindow.FindFirstDescendant(cf => cf.ByName("PAYEES"));
            Assert.That(payeesHeader, Is.Not.Null, "PAYEES section header not found.");
            AutomationElement expander = payeesHeader.Parent;
            var togglePattern = expander.Patterns.Toggle.Pattern;
            if (togglePattern.ToggleState.Value == ToggleState.Off)
            {
                togglePattern.Toggle();
            }

            AutomationElement payeeRow = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByName("Payee Smoke Test")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(payeeRow, Is.Not.Null, "Fixture payee row not found after expanding PAYEES.");

            payeeRow.Click();
            Wait.UntilInputIsProcessed();

            Assert.That(app.HasExited, Is.False, "App exited unexpectedly after selecting the payee.");

            AutomationElement[] topLevelWindows = automation.GetDesktop().FindAllChildren(
                cf => cf.ByProcessId(app.ProcessId));
            bool errorDialogPresent = topLevelWindows.Any(w =>
                w.Name.IndexOf("Unhandled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                w.Name.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.That(errorDialogPresent, Is.False, "An unhandled-exception dialog appeared after selecting the payee.");
        }

        private static void OpenFileViaFileMenu(Window mainWindow, string filePath)
        {
            AutomationElement fileMenu = mainWindow.FindFirstDescendant(cf => cf.ByName("File"));
            fileMenu.Patterns.ExpandCollapse.Pattern.Expand();
            Wait.UntilInputIsProcessed();

            AutomationElement openItem = mainWindow.FindFirstDescendant(cf => cf.ByName("Open..."));
            openItem.Patterns.Invoke.Pattern.Invoke();

            // The native common file dialog is a separate top-level window
            // belonging to the same process, not a descendant of mainWindow.
            Window openDialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.FirstOrDefault() ??
                mainWindow.Automation.GetDesktop()
                    .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                    .Select(w => w.AsWindow())
                    .FirstOrDefault(w => w.Title.IndexOf("Open", StringComparison.OrdinalIgnoreCase) >= 0),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(openDialog, Is.Not.Null, "Open file dialog did not appear.");

            // Standard Windows common-dialog automation IDs for the filename
            // edit box (1148) and Open button (1).
            AutomationElement fileNameBox = openDialog.FindFirstDescendant(cf => cf.ByAutomationId("1148"));
            Assert.That(fileNameBox, Is.Not.Null, "File name box not found in Open dialog.");
            fileNameBox.Patterns.Value.Pattern.SetValue(filePath);

            AutomationElement openButton = openDialog.FindFirstDescendant(cf => cf.ByAutomationId("1"));
            Assert.That(openButton, Is.Not.Null, "Open button not found in Open dialog.");
            openButton.Patterns.Invoke.Pattern.Invoke();
        }
    }
}
```

Also add the shared path helper reference from Task 1 if you split it out — otherwise this file already reuses `LaunchSmokeTests.MyMoneyExePath` directly, which is fine for two files in the same project.

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/UITests/UITests.csproj`
Expected: 0 errors. FlaUI 5.0.0's exact API (`Patterns.Toggle.Pattern` vs `TogglePattern` cast, `Retry.WhileNull`/`WhileFalse` signatures, `AsWindow()`/`ModalWindows` availability) should be checked against IntelliSense/the installed package here — fix any naming mismatches against the real API, and note what changed and why in your report, the same way earlier plans in this initiative handled unverified library specifics.

- [ ] **Step 3: Run the test**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~SelectingAPayee_AfterOpeningFixtureFile_DoesNotCrash"`
Expected: PASS. This is the acceptance criterion for the whole plan — if it's flaky (passes sometimes, fails others) rather than reliably passing, that's a real finding to report, not something to paper over by re-running until green. Run it at least 3 times in a row and report the actual pass rate, not just one result.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/UITests/PayeeSelectionTests.cs
git commit -m "Add the FlaUI payee-selection scenario test"
```

---

## Plan Self-Review

**Spec coverage:**
- New, independent project, not touching `ScenarioTest` → Task 1 (Files list confirms no `ScenarioTest` paths anywhere in this plan).
- FlaUI.Core + FlaUI.UIA3, NUnit runner → Task 1.
- First test's full six-step scenario (launch, File→Open dialog, load confirmation, expand Payees, select row, assert survival + no error dialog) → Task 3, one continuous test matching the spec's acceptance criterion exactly.
- Checked-in fixture, not regenerated per run → Task 2, with the generator kept in-repo as `[Explicit]` rather than a throwaway script.
- SQL Server path explicitly out of scope → no task references `SqlServerStoredProcDatabase`, `dataengine.config.json`, or any SQL Server type; the fixture is plain `SqliteDatabase`.

**Placeholder scan:** No TBD/TODO. The FlaUI API surface carries a documented, explicit uncertainty (Global Constraints + Task 3 Step 2) rather than false confidence — that's a named risk with instructions for how to handle it, not an unfilled placeholder.

**Type consistency:** `LaunchSmokeTests.MyMoneyExePath` (Task 1) is consumed by name in Task 3 exactly as defined (`internal static string`, same class/namespace `Walkabout.UITests`). `FixtureGenerator`'s output path (Task 2) and `PayeeSelectionTests`'s fixture path (Task 3, `AppContext.BaseDirectory` + `Fixtures/PayeeSmokeTest.mmdb`, matching the `CopyToOutputDirectory` item added in Task 2 Step 1) resolve to the same relative layout.
