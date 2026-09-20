# UI Modernization Phase 2 (Dialogs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the app's 24 dialogs from ModernWpfUI to WPF-UI styling, fixing the shared
resource-key landmine that would otherwise silently break every dialog's background the day
`ModernWpfUI` is finally removed, and closing the FlaUI-test blind spot (purely visual/timing
bugs) that let four real bugs slip past a green suite during Phase 1.

**Architecture:** Dialogs stay plain `Window`s (via `BaseDialog`), restyled in place - no
`ContentDialog` adoption (that needs `FluentWindow`, which is out of scope). Five dialogs
(`AttachmentDialog`, `CategoryDialog`, `MoneyFileImportDialog`, `OnlineAccountDialog`,
`PasswordWindow`) reference ModernWpf's `xmlns:ui=` directly and need real markup work; the other
~19 already inherit WPF-UI's implicit styling for their stock controls via the Phase 1 App.xaml
merge and only need re-verification, not rework.

**Tech Stack:** WPF/.NET 10, WPF-UI (lepoco/wpfui) 4.3.0, FlaUI 5.0.0/NUnit for UI tests.

**Spec:** `docs/superpowers/specs/2026-09-19-ui-modernization-wpfui-migration-design.md` - read
the "Phase 1 Finding", "Phase 2 — Simple dialogs first", "Phase 2 Planning Notes", updated
"Testing strategy", and "Design/UX acceptance criteria" sections before starting. This plan
implements only Phase 2 of that spec; Phase 3/4 are separate, not-yet-written plans.

## Global Constraints

- Every `Wpf.Ui.*` API used for the first time in this plan must be verified against its real
  source (reflection on the installed `Wpf.Ui.dll` at
  `~/.nuget/packages/wpf-ui/4.3.0/lib/net10.0-windows7.0/Wpf.Ui.dll`, or the real GitHub source at
  the `4.3.0` tag) before being used in markup or code - never guessed from memory or by analogy
  to another icon/control library. This is the single most-violated discipline from Phase 1's own
  post-mortem.
- Dialogs stay plain `Window`-derived (`BaseDialog`). Do not introduce `ContentDialog` or
  `FluentWindow` in this plan.
- `Themes/generic.xaml` stays untouched (explicitly out of scope for this whole migration, per
  the design spec's "Scope" section).
- Every task that touches a dialog with a real change ends with: `dotnet build
  Source/WPF/MyMoney.sln` clean, the relevant FlaUI test(s) green, and a live/manual visual check
  (screenshot or direct interaction) - not just a green automated run, given Phase 1's own
  experience that a green FlaUI suite alone missed four real visual/timing bugs.
- `AppCrashGuard` (`Source/WPF/UITests/Basics/AppCrashGuard.cs`) and the new `VisualGuard` (Task 2)
  must both run in every Basics FlaUI test's teardown - don't add a new dialog test that bypasses
  `BasicsTestSetup.CleanUpDatabase`.

---

### Task 1: Fix `BaseDialog`'s ModernWpf-only resource keys

**Files:**
- Create: `Source/WPF/MyMoney/Themes/DialogResources.xaml`
- Modify: `Source/WPF/MyMoney/App.xaml` (merge the new dictionary)
- Modify: `Source/WPF/MyMoney/Dialogs/BaseDialog.cs`
- Test: `Source/WPF/UnitTests/BaseDialogResourceTests.cs`

**Interfaces:**
- Produces: two new app-owned resource keys, `DialogBackgroundBrush` and `DialogForegroundBrush`,
  merged into `Application.Resources` and usable via `DynamicResource`/`SetResourceReference` from
  any dialog. Later tasks in this plan don't consume these directly (only `BaseDialog` does), but
  Phase 3+ work touching `SystemControlPageBackgroundChromeMediumLowBrush`
  (`Views/TransactionsView.xaml` and others) should follow the same pattern rather than
  reinventing it - noted here for that future plan, not acted on in this one.

`SystemControlPageBackgroundChromeLowBrush` and `SystemControlPageTextBaseHighBrush` (referenced
directly in `Dialogs/BaseDialog.cs:9-10` via `SetResourceReference`) exist only in `ModernWpf.dll`
- confirmed by grepping both assemblies' resource keys. They resolve today because `ModernWpfUI`
is still referenced, but resolve to nothing (silently - no compile error, no exception) the day
that package reference is finally removed, breaking every dialog's background/foreground -
worst in Dark mode, where a transparent-defaulting `Window.Background` reads as broken black.

- [ ] **Step 1: Write the failing test**

Create `Source/WPF/UnitTests/BaseDialogResourceTests.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using NUnit.Framework;
using Walkabout.Dialogs;

namespace Walkabout.Tests
{
    [TestFixture]
    public class BaseDialogResourceTests
    {
        [Test, Apartment(System.Threading.ApartmentState.STA)]
        public void BaseDialog_BackgroundAndForeground_ResolveToRealBrushes()
        {
            // Application.Current is null in a plain unit test host - BaseDialog's constructor
            // calls SetResourceReference, which needs a live Application.Resources chain to
            // resolve against. Build one here mirroring App.xaml's real merge order/dictionaries,
            // scoped to just this test - not a full App.xaml.cs bootstrap (that needs Log/settings
            // init this test doesn't have).
            var app = Application.Current ?? new Application();
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new System.Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new System.Uri("/MyMoney;component/Themes/DialogResources.xaml", System.UriKind.Relative)
            });

            var dialog = new BaseDialog();

            Assert.That(dialog.Background, Is.Not.Null, "BaseDialog.Background resolved to null - DialogBackgroundBrush is missing.");
            Assert.That(dialog.Background, Is.InstanceOf<SolidColorBrush>());
            Assert.That(dialog.Foreground, Is.Not.Null, "BaseDialog.Foreground resolved to null - DialogForegroundBrush is missing.");
            Assert.That(dialog.Foreground, Is.InstanceOf<SolidColorBrush>());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=BaseDialog_BackgroundAndForeground_ResolveToRealBrushes"`
Expected: FAIL - `dialog.Background`/`dialog.Foreground` are `null` (or the pack URI for
`DialogResources.xaml` fails to resolve, since the file doesn't exist yet).

- [ ] **Step 3: Create the app-owned resource dictionary**

Create `Source/WPF/MyMoney/Themes/DialogResources.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--
      App-owned aliases for the two ModernWpf-only brush keys BaseDialog.cs used to reference
      directly (SystemControlPageBackgroundChromeLowBrush / SystemControlPageTextBaseHighBrush).
      Both exist only in ModernWpf.dll (confirmed via assembly resource-key scan, 2026-09-20) -
      referencing them directly meant every dialog's background/foreground would silently break
      the day ModernWpfUI is removed. These aliases point at WPF-UI's real equivalents
      (ApplicationBackgroundBrush / TextFillColorPrimaryBrush, confirmed present in the installed
      Wpf.Ui.dll 4.3.0) so BaseDialog no longer depends on ModernWpf at all.
    -->
    <SolidColorBrush x:Key="DialogBackgroundBrush" Color="{DynamicResource ApplicationBackgroundColor}"/>
    <SolidColorBrush x:Key="DialogForegroundBrush" Color="{DynamicResource TextFillColorPrimary}"/>
</ResourceDictionary>
```

- [ ] **Step 4: Merge the new dictionary in `App.xaml`**

Modify `Source/WPF/MyMoney/App.xaml` - add one line to `ResourceDictionary.MergedDictionaries`,
after `<ui:ControlsDictionary />` and before the `generic.xaml` merge (so it can see WPF-UI's
`ApplicationBackgroundColor`/`TextFillColorPrimary` colors, which are defined in the
`ui:ThemesDictionary` merge earlier in the same list):

```xml
                <ui:ThemesDictionary Theme="Light" />
                <ui:ControlsDictionary />
                <ResourceDictionary Source="Themes/DialogResources.xaml"/>
                <ResourceDictionary Source="Themes/generic.xaml"/>
```

- [ ] **Step 5: Repoint `BaseDialog.cs`**

Modify `Source/WPF/MyMoney/Dialogs/BaseDialog.cs`:

```csharp
using System.Windows;

namespace Walkabout.Dialogs
{
    public class BaseDialog : Window
    {
        public BaseDialog()
        {
            this.SetResourceReference(Window.BackgroundProperty, "DialogBackgroundBrush");
            this.SetResourceReference(Window.ForegroundProperty, "DialogForegroundBrush");
        }
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=BaseDialog_BackgroundAndForeground_ResolveToRealBrushes"`
Expected: PASS

- [ ] **Step 7: Full build + regression check**

Run: `dotnet build Source/WPF/MyMoney.sln` - expect 0 warnings, 0 errors.
Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj` - expect the full suite green (188+ this
new one), not just the new test in isolation.
Run the Basics FlaUI suite: `dotnet test Source/WPF/UITests/UITests.csproj --filter
"FullyQualifiedName~Walkabout.UITests.Basics"` - expect all green. Several of these tests open
dialogs derived from `BaseDialog` (e.g. `RenamePayeeDialog` via `PayeesFlaUiTests.cs`); this is
the first real regression signal that the new brushes resolve correctly at runtime, not just in
the isolated unit test.

- [ ] **Step 8: Manual check - toggle dark theme, open any dialog**

Launch the app (`Source/WPF/MyMoney/bin/Debug/net10.0-windows7.0/win-x64/MyMoney.exe
/nosettings`), press Ctrl+L to switch to Dark, then open any dialog derived from `BaseDialog`
(e.g. right-click a transaction row → Rename Payee). Confirm the dialog's background/foreground
are legible and match the app's dark theme, not a broken/transparent/black state. This is the
concrete case Task 1 exists to prevent - verify it directly, don't infer it from the automated
checks alone.

- [ ] **Step 9: Commit**

```bash
git add Source/WPF/MyMoney/Themes/DialogResources.xaml Source/WPF/MyMoney/App.xaml \
        Source/WPF/MyMoney/Dialogs/BaseDialog.cs Source/WPF/UnitTests/BaseDialogResourceTests.cs
git commit -m "fix: BaseDialog no longer depends on ModernWpf-only resource keys"
```

---

### Task 2: Add `VisualGuard` test helper and wire it into the shared teardown

**Files:**
- Create: `Source/WPF/UITests/Basics/VisualGuard.cs`
- Modify: `Source/WPF/UITests/Basics/BasicsTestSetup.cs`
- Modify: `Source/WPF/UITests/Basics/BasicsAppSession.cs` (if theme-pass support needs a session-level flag - see Step 5)
- Test: exercised by the existing Basics suite once wired in; no new dedicated test file.

**Interfaces:**
- Produces: `VisualGuard.AssertNotClipped(AutomationElement element)`,
  `VisualGuard.AssertNotBlank(AutomationElement element, bool expectDark)`,
  `VisualGuard.AssertSettledWithin(AutomationElement element, TimeSpan budget, Action interaction)`
  - all static, all throw `NUnit.Framework.AssertionException` (via `Assert.Fail`) on violation,
    matching `AppCrashGuard`'s own idiom.
- Consumes: `FlaUI.Core.Capturing.Capture.Element(AutomationElement)` (confirmed real API via
  reflection on the installed `FlaUI.Core.dll` 5.0.0, 2026-09-20 - returns a `CaptureImage` with
  a `.Bitmap` property, `System.Drawing.Bitmap`).

This directly implements the forward-looking QA panel's recommendation (see the design spec's
Testing strategy section): self-referential visual invariants that need no golden-image baseline,
catching the three purely-visual/timing bug classes Phase 1's automation-tree-only checks missed.

- [ ] **Step 1: Write the failing test - a deliberately-clipped element**

This is a helper, not a feature with its own isolated unit test (it drives real FlaUI element
captures, which need a running app). Verify it via a deliberately-broken throwaway case first,
inside the same session used for real testing later. Add a temporary scratch test:

Create `Source/WPF/UITests/VisualGuardScratchTest.cs` (throwaway - deleted in Step 6):

```csharp
using System;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using NUnit.Framework;
using Walkabout.UITests.Basics;

namespace Walkabout.UITests
{
    public class VisualGuardScratchTest
    {
        [Test]
        public void AssertNotClipped_FailsOnMenuHelpItem_IfDeliberatelyTruncated()
        {
            // Sanity check only: confirms AssertNotClipped can detect a real clip when one
            // exists, and does NOT false-positive on a normal, correctly-rendered element.
            using var app = Application.Launch(LaunchSmokeTests.MyMoneyExePath, "/nosettings");
            using var automation = new UIA3Automation();
            var mainWindow = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));

            AutomationElement helpMenu = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuHelp"));
            Assert.That(helpMenu, Is.Not.Null);

            // This must NOT throw - MenuHelp renders correctly today (the Task 8051fbbc fix).
            Assert.DoesNotThrow(() => VisualGuard.AssertNotClipped(helpMenu));

            app.Close();
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~VisualGuardScratchTest"`
Expected: FAIL - `VisualGuard` doesn't exist yet (`CS0103`).

- [ ] **Step 3: Implement `VisualGuard.cs`**

Create `Source/WPF/UITests/Basics/VisualGuard.cs`:

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    /// <summary>
    /// Self-referential visual invariants that catch purely-visual/timing bugs FlaUI's
    /// automation-tree assertions (AutomationId/ControlType/pattern state) cannot see - added
    /// after Phase 1 shipped a green FlaUI suite alongside four real visual bugs (a truncated
    /// menu label, a wrong-colored dialog under Dark theme, a blank title bar, and an animation
    /// racing the app's own layout code). No golden-image baselines: each check is an invariant
    /// about the element's own captured pixels, so it needs no stored reference image and no
    /// tolerance tuning for theme/DPI/frame variance.
    /// </summary>
    internal static class VisualGuard
    {
        /// <summary>
        /// Fails if any non-background-colored pixel touches the last 2px of any edge of the
        /// element's bounding rectangle - the signature of text/content clipped by a container
        /// too small for its content (e.g. issue: MainWindow's Help menu item truncated to "He").
        /// </summary>
        internal static void AssertNotClipped(AutomationElement element)
        {
            using Bitmap bmp = Capture.Element(element).Bitmap;
            if (bmp.Width < 6 || bmp.Height < 6)
            {
                return; // too small to meaningfully check edge pixels; not this guard's job
            }

            Color background = bmp.GetPixel(1, 1); // corner pixel, assumed representative of background
            const int edgeBand = 2;
            const int colorTolerance = 12; // small AA/subpixel tolerance, not a golden-image threshold

            bool EdgeHasForegroundPixel(Func<int, Point> alongEdge, int length)
            {
                for (int i = 0; i < length; i++)
                {
                    Point p = alongEdge(i);
                    Color c = bmp.GetPixel(p.X, p.Y);
                    if (Math.Abs(c.R - background.R) > colorTolerance ||
                        Math.Abs(c.G - background.G) > colorTolerance ||
                        Math.Abs(c.B - background.B) > colorTolerance)
                    {
                        return true;
                    }
                }
                return false;
            }

            for (int edgeOffset = 0; edgeOffset < edgeBand; edgeOffset++)
            {
                int rightX = bmp.Width - 1 - edgeOffset;
                if (EdgeHasForegroundPixel(y => new Point(rightX, y), bmp.Height))
                {
                    Assert.Fail($"VisualGuard.AssertNotClipped: '{element.Name}' has non-background " +
                        $"pixels within {edgeBand}px of its right edge - likely truncated content.");
                }
            }
        }

        /// <summary>
        /// Fails if the element's captured area is a single flat color (a blank/broken-render
        /// region), or if its mean luminance doesn't match the expected theme - catches e.g. a
        /// title bar rendering with no text, or a dialog with the wrong-theme background.
        /// </summary>
        internal static void AssertNotBlank(AutomationElement element, bool expectDark)
        {
            using Bitmap bmp = Capture.Element(element).Bitmap;
            var distinctColors = new System.Collections.Generic.HashSet<int>();
            long luminanceSum = 0;
            int sampleCount = 0;

            for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 40))
            {
                for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 40))
                {
                    Color c = bmp.GetPixel(x, y);
                    distinctColors.Add(c.ToArgb());
                    luminanceSum += (c.R + c.G + c.B) / 3;
                    sampleCount++;
                }
            }

            if (distinctColors.Count <= 1)
            {
                Assert.Fail($"VisualGuard.AssertNotBlank: '{element.Name}' is a single flat color - likely a blank/broken render.");
            }

            double meanLuminance = luminanceSum / (double)sampleCount / 255.0;
            if (expectDark && meanLuminance > 0.5)
            {
                Assert.Fail($"VisualGuard.AssertNotBlank: '{element.Name}' has mean luminance " +
                    $"{meanLuminance:F2} (expected dark, < 0.5) - likely still rendering Light-themed.");
            }
            if (!expectDark && meanLuminance < 0.2)
            {
                Assert.Fail($"VisualGuard.AssertNotBlank: '{element.Name}' has mean luminance " +
                    $"{meanLuminance:F2} (expected light, > 0.2) - likely rendering as a dark/blank box.");
            }
        }

        /// <summary>
        /// Runs <paramref name="interaction"/>, captures immediately and again after
        /// <paramref name="budget"/>, and fails if the two captures differ - meaning the element
        /// was still visually changing (an in-progress animation/layout pass) past the expected
        /// settle time. No baseline needed: this compares the element only against itself.
        /// </summary>
        internal static void AssertSettledWithin(AutomationElement element, TimeSpan budget, Action interaction)
        {
            interaction();
            using Bitmap immediate = Capture.Element(element).Bitmap;
            System.Threading.Thread.Sleep(budget);
            using Bitmap settled = Capture.Element(element).Bitmap;

            if (immediate.Width != settled.Width || immediate.Height != settled.Height)
            {
                return; // size itself changed (e.g. real expand/collapse) - not this guard's concern
            }

            int sampleStepX = Math.Max(1, immediate.Width / 20);
            int sampleStepY = Math.Max(1, immediate.Height / 20);
            for (int x = 0; x < immediate.Width; x += sampleStepX)
            {
                for (int y = 0; y < immediate.Height; y += sampleStepY)
                {
                    if (immediate.GetPixel(x, y).ToArgb() != settled.GetPixel(x, y).ToArgb())
                    {
                        Assert.Fail($"VisualGuard.AssertSettledWithin: '{element.Name}' was still " +
                            $"visually changing {budget.TotalMilliseconds}ms after the interaction - " +
                            "a repaint/animation is still in progress past the expected settle time.");
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~VisualGuardScratchTest"`
Expected: PASS

- [ ] **Step 5: Wire `AssertNotBlank` into the shared teardown**

Modify `Source/WPF/UITests/Basics/BasicsTestSetup.cs`'s `CleanUpDatabase` (the universal
`[TearDown]` every Basics test already calls) - add a call right after the existing
`AppCrashGuard.AssertNoCrashOccurred` line:

```csharp
            // Deliberately NOT inside a try/catch that swallows it (unlike the best-effort
            // cleanup above) - a real app crash must fail the test, not be silently absorbed.
            // See AppCrashGuard's own comment for why this check exists.
            AppCrashGuard.AssertNoCrashOccurred(BasicsAppSession.MainWindow, ref BasicsAppSession.LastCheckedLogLength);

            // Same rationale, for purely-visual bugs AppCrashGuard can't see (Phase 1's own
            // post-mortem: 4 real bugs, 0 exceptions). Checks the whole main window is not a
            // single flat color - a coarse, cheap, always-applicable smoke check; per-dialog
            // AssertNotClipped/AssertSettledWithin checks are added explicitly by the tests that
            // open those dialogs, since they need to know which theme is currently active.
            VisualGuard.AssertNotBlank(BasicsAppSession.MainWindow, expectDark: false);
```

- [ ] **Step 6: Delete the scratch test, run full regression**

```bash
rm Source/WPF/UITests/VisualGuardScratchTest.cs
```

Run: `dotnet build Source/WPF/MyMoney.sln` - 0 errors.
Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~Walkabout.UITests.Basics"`
Expected: all 15 (pre-existing) Basics tests still pass, now each additionally checked by
`AssertNotBlank` in their shared teardown with no code changes needed in the tests themselves.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/UITests/Basics/VisualGuard.cs Source/WPF/UITests/Basics/BasicsTestSetup.cs
git commit -m "test: add VisualGuard self-referential visual invariants, wire into shared teardown"
```

---

### Task 3: Migrate the 3 simple `SymbolIcon`-only dialogs

**Files:**
- Modify: `Source/WPF/MyMoney/Dialogs/MoneyFileImportDialog.xaml`
- Modify: `Source/WPF/MyMoney/Dialogs/OnlineAccountDialog.xaml`
- Modify: `Source/WPF/MyMoney/Dialogs/PasswordWindow.xaml`
- Create: `Source/WPF/UITests/Basics/SimpleDialogsSmokeFlaUiTests.cs` (none of these 3 dialogs has
  any existing FlaUI coverage today - confirmed by grepping `Source/WPF/UITests/Basics/*.cs` for
  each dialog's class name).

**Interfaces:**
- Consumes: the real `SymbolRegular` enum from the installed `Wpf.Ui.dll` 4.3.0 (verify via
  reflection in Step 1 below, per this plan's Global Constraints - the exact enum member names
  are confirmed there, not guessed here).

These three dialogs each only use `ui:SymbolIcon` (no `AppBarButton`/`CommandBar`/`DropDownButton`
- the harder control types Tasks 4-5 handle). Same mechanical pattern already proven in the merged
Phase 1 plan's Task 6 (MainWindow's icon replacements): repoint `xmlns:ui=` to WPF-UI's schema,
replace each `Symbol="X"` (ModernWpf's own `Symbol` enum) with
`Symbol="{x:Static ui:SymbolRegular.X##}"` (WPF-UI's real enum, with its numeric size suffix).

- [ ] **Step 1: Verify the real `SymbolRegular` names for each icon via reflection**

Run in PowerShell (matching the technique already used and documented for Phase 1's Task 6):

```powershell
$asm = [System.Reflection.Assembly]::LoadFrom("$env:USERPROFILE\.nuget\packages\wpf-ui\4.3.0\lib\net10.0-windows7.0\Wpf.Ui.dll")
$symbolType = $asm.GetType("Wpf.Ui.Controls.SymbolRegular")
$symbolType.GetEnumNames() | Where-Object { $_ -match "^(Accept|Filter)" }
```

`MoneyFileImportDialog.xaml:39-40` uses ModernWpf `Symbol="Accept"` and `Symbol="Filter"`.
`OnlineAccountDialog.xaml:32` uses `Symbol="Accept"`. Record the real matching `SymbolRegular`
names this command prints (expected: `Checkmark24` or similar for "Accept" - WPF-UI doesn't use
the name "Accept" - and a `Filter*` match for "Filter"; use whatever the command actually prints,
not this expectation, per the Global Constraints rule against guessing).

- [ ] **Step 2: Write the failing FlaUI smoke tests**

Create `Source/WPF/UITests/Basics/SimpleDialogsSmokeFlaUiTests.cs`:

```csharp
using System;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    // MoneyFileImportDialog, OnlineAccountDialog, and PasswordWindow had zero FlaUI coverage
    // before this task - added alongside their Phase 2 migration per the design spec's testing
    // strategy ("coverage should exist before/alongside the phase, not accumulate as drift").
    [TestFixture]
    public class SimpleDialogsSmokeFlaUiTests
    {
        private (string ScratchPath, string RegisteredName) db;

        [SetUp]
        public void SetUp()
        {
            this.db = BasicsTestSetup.OpenFreshDatabase("SimpleDialogsSmokeFlaUiTests Fixture");
        }

        [TearDown]
        public void TearDown()
        {
            BasicsTestSetup.CleanUpDatabase(this.db.ScratchPath, this.db.RegisteredName);
        }

        [Test]
        public void PasswordWindow_OpensViaAddUser_ShowsKeyIcon()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);

            AutomationElement fileMenu = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuFile")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(fileMenu, Is.Not.Null);
            fileMenu.Patterns.ExpandCollapse.Pattern.Expand();

            AutomationElement addUserItem = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("MenuFileAddUser")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(addUserItem, Is.Not.Null);
            addUserItem.Patterns.Invoke.Pattern.Invoke();

            Window dialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(dialog, Is.Not.Null, "PasswordWindow did not appear after File > Add User.");

            AutomationElement keyImage = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("KeyImage")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(keyImage, Is.Not.Null, "KeyImage SymbolIcon not found - likely the WPF-UI Symbol repoint broke.");
            VisualGuard.AssertNotClipped(keyImage);

            AutomationElement cancelButton = dialog.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button).And(cf.ByName("Cancel")));
            cancelButton?.Patterns.Invoke.Pattern.Invoke();
        }
    }
}
```

(`MoneyFileImportDialog`/`OnlineAccountDialog` smoke coverage: both require external
state - `MoneyFileImportDialog` needs a real import source file, `OnlineAccountDialog` needs a
configured online-banking connection - that this fixture-based harness doesn't set up. Note this
as a follow-up gap rather than fabricating a fake trigger path; `PasswordWindow`'s File > Add User
entry point is the one of the three reachable from a fresh fixture database.)

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~PasswordWindow_OpensViaAddUser"`
Expected: FAIL - the dialog opens (ModernWpf-styled), but `KeyImage` lookup or a later assertion
may already pass since the AutomationId doesn't change; this step's real purpose is confirming the
test harness itself works end-to-end before the markup change. If it passes outright at this
point, proceed anyway - Step 5's `VisualGuard.AssertNotClipped` and the manual check in Step 6 are
still the real verification for the actual migration.

- [ ] **Step 4: Migrate the 3 dialogs' markup**

Modify `Source/WPF/MyMoney/Dialogs/PasswordWindow.xaml` - repoint the namespace (line 5) and all
3 `Symbol=` usages (lines 46-48), using whatever real names Step 1's reflection printed:

```xml
xmlns:c="clr-namespace:Walkabout.Controls" xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```
```xml
<ui:SymbolIcon x:Name="KeyImage" Symbol="{x:Static ui:SymbolRegular.Key24}"/>
<ui:SymbolIcon x:Name="ShieldImage" Symbol="{x:Static ui:SymbolRegular.CheckmarkCircle24}" Visibility="Hidden"/>
<ui:SymbolIcon x:Name="BrokenImage" Symbol="{x:Static ui:SymbolRegular.ShieldError24}" Visibility="Hidden"/>
```

(Replace `Key24`/`CheckmarkCircle24`/`ShieldError24` above with the real enum members if Step 1's
reflection printed different names for "Permissions"/"Accept"/"ReportHacked" - these three are
this plan's best-guess starting point, not verified; Step 1's actual output is the source of
truth, and this step must use it, not this placeholder text.)

Modify `Source/WPF/MyMoney/Dialogs/MoneyFileImportDialog.xaml` (line 8, 39-40) and
`Source/WPF/MyMoney/Dialogs/OnlineAccountDialog.xaml` (line 13, 32) the same way: repoint
`xmlns:ui=` to `http://schemas.lepo.co/wpfui/2022/xaml`, replace each `Symbol="X"` with
`Symbol="{x:Static ui:SymbolRegular.<verified-name>}"` using Step 1's real "Accept"/"Filter"
matches.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet build Source/WPF/MyMoney.sln` - 0 errors (a wrong `SymbolRegular` member name fails
the build here, not silently at runtime - this is a compile-time-checked API, unlike the
`SplitButton.Flyout`/`ApplicationThemeManager.Apply` runtime-only failures from Phase 1).
Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~PasswordWindow_OpensViaAddUser"`
Expected: PASS, including the `VisualGuard.AssertNotClipped(keyImage)` line.

- [ ] **Step 6: Manual check - all 3 dialogs, both themes**

Launch the app, trigger each dialog (`PasswordWindow` via File > Add User; `OnlineAccountDialog`
via an account's Online Services setup if reachable without real credentials, otherwise inspect
via the Visual Studio XAML designer/live visual tree; `MoneyFileImportDialog` via File > Import
with any file). Toggle Ctrl+L between Light/Dark for each and confirm the icons render (not
blank/missing glyphs - a wrong `SymbolRegular` member can compile but render as a missing-glyph
box if the reflection step picked a real-but-wrong enum name).

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/MoneyFileImportDialog.xaml \
        Source/WPF/MyMoney/Dialogs/OnlineAccountDialog.xaml \
        Source/WPF/MyMoney/Dialogs/PasswordWindow.xaml \
        Source/WPF/UITests/Basics/SimpleDialogsSmokeFlaUiTests.cs
git commit -m "feat: migrate MoneyFileImportDialog/OnlineAccountDialog/PasswordWindow icons to WPF-UI"
```

---

### Task 4: Migrate `CategoryDialog`'s `DropDownButton`/`Flyout` color picker

**Files:**
- Modify: `Source/WPF/MyMoney/Dialogs/CategoryDialog.xaml`
- Modify: `Source/WPF/MyMoney/Dialogs/CategoryDialog.xaml.cs`
- Test: `Source/WPF/UITests/Basics/CategoriesFlaUiTests.cs` (already exists and opens this dialog
  - extend it, don't create a new file)

**Interfaces:**
- Consumes: nothing new from earlier tasks.
- Produces: nothing later tasks in this plan consume.

`ModernWpf`'s `ui:DropDownButton` has no WPF-UI equivalent at all (confirmed: WPF-UI's control set
has `SplitButton`, `Button`, `ToggleButton`, no `DropDownButton`). Its
`ui:DropDownButton.Flyout > ui:Flyout` structure is the same shape as the `SplitButton.Flyout` bug
already found and fixed in the merged Phase 1 plan (Task 6): don't repeat that mistake here.
Replace with a plain `ui:Button` whose `Click` opens a real `ContextMenu` (proven reliable in
Phase 1 - `ContextMenu` owns its own open/close/reopen state correctly, unlike a hand-toggled
`Flyout.IsOpen`, which desynced after an outside-click dismissal).

- [ ] **Step 1: Write the failing test**

`Source/WPF/UITests/Basics/CategoriesFlaUiTests.cs` already has `CancelOnCategoryDialog_LeavesCategoryUnchanged`
and `RenamingCategoryToExistingName_IsRejected`, both of which open `CategoryDialog`. Add a new
test to the same file (append inside the existing `[TestFixture] class CategoriesFlaUiTests`):

```csharp
        [Test]
        public void CategoryDialog_ColorDropDown_OpensClosesAndReopensAfterOutsideDismissal()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.ExpandCategoriesPanel(mainWindow);

            AutomationElement moviesNode = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.TreeItem).And(cf.ByName("Fun:Movies"))),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(moviesNode, Is.Not.Null);
            moviesNode.DoubleClick();
            Wait.UntilInputIsProcessed();

            Window dialog = Retry.WhileNull(() =>
                mainWindow.ModalWindows.Length > 0 ? mainWindow.ModalWindows[0] : null,
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(dialog, Is.Not.Null, "CategoryDialog did not appear.");

            AutomationElement colorDropDown = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("ColorDropDown")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(colorDropDown, Is.Not.Null);

            colorDropDown.AsButton().Invoke();
            AutomationElement flyoutContent = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("ColorDropDownFlyout")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(flyoutContent, Is.Not.Null, "Color picker did not open after first click.");

            // Dismiss by clicking elsewhere in the dialog (light-dismiss ContextMenu).
            var elsewhere = new System.Drawing.Point((int)dialog.BoundingRectangle.X + 10, (int)dialog.BoundingRectangle.Y + 10);
            FlaUI.Core.Input.Mouse.Click(elsewhere);
            Wait.UntilInputIsProcessed();

            // Reopen - the exact scenario a hand-toggled Flyout.IsOpen desynced on in Phase 1.
            colorDropDown.AsButton().Invoke();
            AutomationElement flyoutContentAgain = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("ColorDropDownFlyout")),
                TimeSpan.FromSeconds(5)).Result;
            Assert.That(flyoutContentAgain, Is.Not.Null, "Color picker did not reopen after outside-click dismissal.");

            AutomationElement cancelButton = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel")));
            cancelButton?.Patterns.Invoke.Pattern.Invoke();
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~CategoryDialog_ColorDropDown"`
Expected: FAIL - `ColorDropDown` is still `ui:DropDownButton` from ModernWpf; the test's
`AsButton().Invoke()` call may not correctly trigger a ModernWpf `DropDownButton`'s flyout (it
isn't a plain `Button`), or `ColorDropDownFlyout`'s automation identity differs. Either failure
mode is fine - the point is a red bar before the markup change.

- [ ] **Step 3: Migrate the markup**

Modify `Source/WPF/MyMoney/Dialogs/CategoryDialog.xaml` - repoint the namespace (line 9) and
replace the `ui:DropDownButton` block (lines 63-83):

```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```

```xml
            <ui:Button
                        x:Name="ColorDropDown"
                        Grid.Column="1" Grid.Row="4" 
                        AutomationProperties.Name="ColorDropDown"
                        VerticalAlignment="Center" HorizontalAlignment="Left"
                        Margin="10"
                        Click="OnColorDropDownClick"
                       >
                <Rectangle Width="30" Height="20"/>
                <ui:Button.ContextMenu>
                    <ContextMenu x:Name="ColorDropDownFlyout"
                                 AutomationProperties.Name="ColorDropDownFlyout"
                                 Placement="Bottom">
                        <local2:ColorPickerPanel x:Name="ColorPickerContent" Width="300" Height="300"
                                                 BorderThickness="1" ColorChanged="ColorPickerPanel_ColorChanged" Focusable="true"/>
                    </ContextMenu>
                </ui:Button.ContextMenu>
            </ui:Button>
```

- [ ] **Step 4: Update the code-behind**

Modify `Source/WPF/MyMoney/Dialogs/CategoryDialog.xaml.cs` - replace the reflection-based
`ColorPicker` property getter (lines 231-240, which existed only because ModernWpf's `Flyout`
class's `Content` property "is not public", per its own comment) with a direct field reference,
and add the new `Click` handler:

```csharp
        private ColorPickerPanel ColorPicker
        {
            get { return this.ColorPickerContent; }
        }

        private void OnColorDropDownClick(object sender, RoutedEventArgs e)
        {
            this.ColorDropDown.ContextMenu.PlacementTarget = this.ColorDropDown;
            this.ColorDropDown.ContextMenu.IsOpen = true;
        }
```

(`this.ColorPickerContent` and `this.ColorDropDown` are the XAML-generated fields from the
`x:Name`s set in Step 3 - confirm they resolve after Step 5's build; if `ColorPickerContent`
doesn't generate as expected, check `Source/WPF/MyMoney/obj/Debug/net10.0-windows7.0/win-x64/Dialogs/CategoryDialog.g.cs`
for the actual generated field name rather than guessing further.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet build Source/WPF/MyMoney.sln` - 0 errors.
Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~CategoryDialog_ColorDropDown"`
Expected: PASS.
Run the full Basics suite: `dotnet test Source/WPF/UITests/UITests.csproj --filter
"FullyQualifiedName~Walkabout.UITests.Basics"` - expect all green (the two pre-existing
`CategoryDialog`-opening tests in this same file must still pass unmodified).

- [ ] **Step 6: Manual check**

Launch the app, open a Category dialog (double-click any category in the left panel), click the
color swatch, confirm the picker opens, click elsewhere to dismiss, click the swatch again and
confirm it reopens (the exact regression this task's test encodes). Toggle Ctrl+L and repeat.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/CategoryDialog.xaml \
        Source/WPF/MyMoney/Dialogs/CategoryDialog.xaml.cs \
        Source/WPF/UITests/Basics/CategoriesFlaUiTests.cs
git commit -m "feat: migrate CategoryDialog's color picker from DropDownButton to Button+ContextMenu"
```

---

### Task 5: Migrate `AttachmentDialog`'s `CommandBar`/`AppBarButton` toolbar

**Files:**
- Modify: `Source/WPF/MyMoney/Dialogs/AttachmentDialog.xaml`
- Test: `Source/WPF/UITests/Basics/AttachmentsFlaUiTests.cs` (already exists - extend it)

**Interfaces:**
- Consumes: nothing new from earlier tasks.
- Produces: nothing later tasks in this plan consume.

`ui:CommandBar`/`ui:AppBarButton` (ModernWpf) have no WPF-UI equivalent at all - WPF-UI's control
set has no toolbar/command-bar container. `MainWindow.xaml`'s own toolbar (merged in Phase 1)
already established the real replacement pattern for this exact situation: a plain
`StackPanel Orientation="Horizontal"` hosting `ui:Button`s with `ui:SymbolIcon` content. Apply the
same pattern here. This is the largest single-file change in this plan - 11 `AppBarButton`
instances - so budget more time for Step 1's verification than the other tasks.

- [ ] **Step 1: Verify the real `SymbolRegular` names for all icons used, via reflection**

`AttachmentDialog.xaml`'s `CommandBar` (lines 83-113) uses ModernWpf icon names: `ZoomIn`,
`ZoomOut`, `Save`, `Rotate` (×2, one with a horizontal-flip `LayoutTransform` to distinguish
left/right), `Crop`, `Cut`, `Copy`, `Paste`, `Delete`, `Print`. The `Window.ContextMenu` block
(lines 51-73, already namespaced under `ui:` but pointing at ModernWpf today) separately uses
`Cut`/`Copy`/`Paste`/`Delete` via `ui:SymbolIcon Symbol="X"`.

Run in PowerShell:

```powershell
$asm = [System.Reflection.Assembly]::LoadFrom("$env:USERPROFILE\.nuget\packages\wpf-ui\4.3.0\lib\net10.0-windows7.0\Wpf.Ui.dll")
$names = [System.Reflection.Assembly]::LoadFrom("$env:USERPROFILE\.nuget\packages\wpf-ui\4.3.0\lib\net10.0-windows7.0\Wpf.Ui.dll").GetType("Wpf.Ui.Controls.SymbolRegular").GetEnumNames()
$names | Where-Object { $_ -match "^(ZoomIn|ZoomOut|Save|ArrowRotate|Crop|Cut|Copy|ClipboardPaste|Delete|Print)" }
```

Record every match this prints. Some ModernWpf names won't have an exact WPF-UI counterpart by
the same word (e.g. "Paste" is likely `ClipboardPaste24` in WPF-UI, not `Paste24` - included above
as a starting guess for the filter, not the final answer) - widen the `-match` pattern and re-run
if a name comes back empty, rather than assuming no equivalent exists.

- [ ] **Step 2: Write the failing test**

`Source/WPF/UITests/Basics/AttachmentsFlaUiTests.cs` already opens `AttachmentDialog` in
`DeletingAttachment_RemovesFileAndListEntry` (see its `OpenRenamePayeeDialogForAmcTransaction`\-style
helper). Add a new test to the same file:

```csharp
        [Test]
        public void AttachmentDialog_ToolbarButtons_AreNotClippedAndRespondToClick()
        {
            Window mainWindow = BasicsAppSession.MainWindow;
            OpenFixtureDatabase.Open(mainWindow, this.db.RegisteredName);
            OpenFixtureDatabase.SelectAccount(mainWindow, "Basics Checking");

            AutomationElement amcRow = Retry.WhileNull(
                () => mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem).And(
                    cf.ByName("Transaction: AMC THEATRES 1234 on 1/5/2026 for -32.5"))),
                TimeSpan.FromSeconds(5)).Result;
            amcRow.Patterns.SelectionItem.Pattern.Select();

            AutomationElement attachmentCell = Retry.WhileNull(
                () => amcRow.FindFirstDescendant(cf => cf.ByName("Item: Transaction: AMC THEATRES 1234 on 1/5/2026 for -32.5, Column Display Index: 0")),
                TimeSpan.FromSeconds(5)).Result;
            attachmentCell.DoubleClick();

            AutomationElement scanButton = Retry.WhileNull(
                () => amcRow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button)),
                TimeSpan.FromSeconds(5)).Result;
            scanButton.Patterns.Invoke.Pattern.Invoke();

            Window attachmentDialog = Retry.WhileNull(() =>
            {
                foreach (var w in Win32WindowFallback.FindTopLevelWindowsForProcess(BasicsAppSession.Automation, BasicsAppSession.App.ProcessId))
                {
                    if (w.Title == "Attachments") { return w; }
                }
                return null;
            }, TimeSpan.FromSeconds(5)).Result;
            Assert.That(attachmentDialog, Is.Not.Null);

            foreach (string buttonName in new[] { "ZoomInButton", "ZoomOutButton", "SaveButton",
                "RotateLeftButton", "RotateRightButton", "CropImageButton", "CutButton",
                "CopyButton", "PasteButton", "DeleteButton", "PrintButton" })
            {
                AutomationElement button = attachmentDialog.FindFirstDescendant(cf => cf.ByAutomationId(buttonName));
                Assert.That(button, Is.Not.Null, $"Toolbar button '{buttonName}' not found after migration.");
                VisualGuard.AssertNotClipped(button);
            }

            attachmentDialog.Close();
        }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~AttachmentDialog_ToolbarButtons"`
Expected: FAIL or PASS-with-old-styling - either is an acceptable "before" state; the real
verification is Step 6's rerun plus the manual check in Step 7.

- [ ] **Step 4: Migrate the markup**

Modify `Source/WPF/MyMoney/Dialogs/AttachmentDialog.xaml`:

Repoint the namespace (line 7) and remove the two ModernWpf-only window-chrome attributes (lines
8-9, same removal already done for `MainWindow.xaml` in the merged Phase 1 plan - no WPF-UI
replacement without `FluentWindow`, out of scope here too):

```xml
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```

Remove the `Style TargetType="{x:Type ui:AppBarButton}"` block (lines 17-19, no longer applicable)
and the 4 `ui:SymbolIcon Symbol="Cut"/"Copy"/"Paste"/"Delete"` in the `Window.ContextMenu`
(lines 60,65,70,76) - replace each `Symbol="X"` with `Symbol="{x:Static ui:SymbolRegular.<verified-name>}"`
using Step 1's real matches.

Replace the `ui:CommandBar` block (lines 83-113) with a plain `StackPanel`:

```xml
        <StackPanel Orientation="Horizontal" DockPanel.Dock="top" HorizontalAlignment="Left">
            <ui:Button x:Name="ZoomInButton" Click="ZoomIn" ToolTip="Zoom in">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.ZoomIn24}"/>
            </ui:Button>
            <ui:Button x:Name="ZoomOutButton" Click="ZoomOut" ToolTip="Zoom out">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.ZoomOut24}"/>
            </ui:Button>
            <ui:Button x:Name="SaveButton" Command="Save" ToolTip="Save attachment">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.Save24}"/>
            </ui:Button>
            <ui:Button x:Name="RotateLeftButton" Command="local:AttachmentDialog.CommandRotateLeft"
                       ToolTip="Rotate counter-clockwise 90 degrees">
                <ui:Button.LayoutTransform>
                    <ScaleTransform ScaleX="-1"/>
                </ui:Button.LayoutTransform>
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.ArrowRotateClockwise24}"/>
            </ui:Button>
            <ui:Button x:Name="RotateRightButton" Command="local:AttachmentDialog.CommandRotateRight"
                       ToolTip="Rotate clockwise 90 degrees">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.ArrowRotateClockwise24}"/>
            </ui:Button>
            <ui:Button x:Name="CropImageButton" Command="local:AttachmentDialog.CommandCropImage"
                       ToolTip="Find image bounds and crop image">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.Crop24}"/>
            </ui:Button>
            <ui:Button x:Name="CutButton" Command="Cut" ToolTip="Cut selected attachment">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.Cut24}"/>
            </ui:Button>
            <ui:Button x:Name="CopyButton" Command="Copy" ToolTip="Copy selected attachment">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.Copy24}"/>
            </ui:Button>
            <ui:Button x:Name="PasteButton" Command="Paste" ToolTip="Paste a new attachment">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.ClipboardPaste24}"/>
            </ui:Button>
            <ui:Button x:Name="DeleteButton" Command="Delete" ToolTip="Delete selected attachment">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.Delete24}"/>
            </ui:Button>
            <ui:Button x:Name="PrintButton" Command="Print" ToolTip="Click here to print the selected attachment">
                <ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.Print24}"/>
            </ui:Button>
        </StackPanel>
```

(Every `SymbolRegular` member name above - `ZoomIn24`, `ZoomOut24`, `Save24`,
`ArrowRotateClockwise24`, `Crop24`, `Cut24`, `Copy24`, `ClipboardPaste24`, `Delete24`, `Print24` -
is this plan's best-guess starting point, consistent with WPF-UI's general `<Name><Size>` naming
convention observed elsewhere in this codebase's already-merged icon replacements. Step 1's actual
reflection output is the source of truth; replace any name here that doesn't match what Step 1
printed. `RotateLeftButton` reuses the same `ArrowRotateClockwise24` glyph as `RotateRightButton`
with an added horizontal flip, matching the original ModernWpf markup's own approach of reusing
one "Rotate" icon for both directions via `LayoutTransform`.)

`ZoomIn`/`ZoomOut` use `Click=`, the rest use `Command=` - confirmed both are valid on `ui:Button`
(it derives from stock `Button`, which supports both) - no code-behind changes needed for either
binding style.

- [ ] **Step 5: Verify `Save`/`Cut`/`Copy`/`Paste`/`Delete`/`Print` remain wired to the app's
      existing `RoutedCommand`s, not WPF-UI's own command properties**

These use the stock WPF `Command="Save"` etc. syntax (mapping to `ApplicationCommands.Save` and
friends via `Window.CommandBindings`, already defined at lines 33-41, untouched by this task).
Confirm `ui:Button.Command` is a real, inherited `ICommand` property (it is - `Button`'s own,
`ui:Button` doesn't override it) so this binding continues to work unchanged - no verification
step needed beyond the build succeeding and Step 6's functional test passing.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet build Source/WPF/MyMoney.sln` - 0 errors (a wrong `SymbolRegular` name fails here).
Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~AttachmentDialog_ToolbarButtons"`
Expected: PASS - all 11 buttons found by `AutomationId`, none clipped.
Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~Walkabout.UITests.Basics"`
Expected: all 15+ pre-existing tests still pass, including
`DeletingAttachment_RemovesFileAndListEntry` (confirms the Delete button/command still works end
to end, not just that it's visually present).

- [ ] **Step 7: Manual check**

Launch the app, open the Attachments dialog for a transaction with an attachment, click through
Zoom In/Out, Rotate Left/Right (confirm the flip direction still looks correct relative to each
other, not both rotating the same way), Crop, Cut/Copy/Paste/Delete, Print. Toggle Ctrl+L and
re-check the toolbar's icons remain visible/legible under Dark theme.

- [ ] **Step 8: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/AttachmentDialog.xaml \
        Source/WPF/UITests/Basics/AttachmentsFlaUiTests.cs
git commit -m "feat: migrate AttachmentDialog's CommandBar/AppBarButton toolbar to WPF-UI Buttons"
```

---

### Task 6: Re-verification sweep of the remaining ~19 dialogs

**Files:**
- Modify (verification only, changes only if a real issue is found): any of the ~19 dialogs in
  `Source/WPF/MyMoney/Dialogs/` not touched by Tasks 3-5 (i.e. everything except
  `AttachmentDialog`, `CategoryDialog`, `MoneyFileImportDialog`, `OnlineAccountDialog`,
  `PasswordWindow`).
- No new test files - this task runs existing coverage plus ad hoc manual checks; any dialog found
  genuinely broken gets its own new task inserted here (not silently patched inline), per this
  plan's own discipline.

**Interfaces:** none - this is a verification pass, not new functionality.

These ~19 dialogs never referenced ModernWpf's `xmlns:ui=` directly - they only use stock WPF
controls (`Button`, `TextBox`, `ComboBox`, etc.), which have already been inheriting WPF-UI's
implicit styling since the Phase 1 `App.xaml` merge landed (the same mechanism the Phase 1 Finding
documents restyled the whole app "silently"). This task confirms that inherited styling is
actually correct for each one - not a blind sign-off.

- [ ] **Step 1: List every dialog not covered by Tasks 3-5**

```bash
ls Source/WPF/MyMoney/Dialogs/*.xaml
```

Cross off `AttachmentDialog.xaml`, `CategoryDialog.xaml`, `MoneyFileImportDialog.xaml`,
`OnlineAccountDialog.xaml`, `PasswordWindow.xaml` (Tasks 3-5) and `BaseDialog.cs` (not a XAML
dialog, Task 1). The remainder is this task's real scope - confirm the count against the design
spec's Phase 2 Planning Notes (~19).

- [ ] **Step 2: Run the full existing test suite as a first-pass signal**

```bash
dotnet build Source/WPF/MyMoney.sln
dotnet test Source/WPF/UnitTests/UnitTests.csproj
dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~Walkabout.UITests.Basics"
```

Expected: all green. This alone is not sufficient sign-off (per this plan's Global Constraints -
Phase 1's own post-mortem is the reason), but a failure here means stop and fix before continuing
this task's manual sweep.

- [ ] **Step 3: For each dialog with existing FlaUI coverage, add a `VisualGuard.AssertNotClipped`
      check on its root if one isn't already implied by Task 2's shared-teardown `AssertNotBlank`**

Dialogs already opened by an existing Basics FlaUI test: `RenamePayeeDialog`
(`PayeesFlaUiTests.cs`), `MergeCategoryDialog`/`RecategorizeDialog` if exercised by
`CategoriesFlaUiTests.cs` (confirm by reading that file - add if missing). For each, add one line
inside the test that already captures a reference to the dialog window:

```csharp
VisualGuard.AssertNotClipped(dialog);
```

Run each affected test individually to confirm it still passes with the new assertion added.

- [ ] **Step 4: Manual sweep of the remaining dialogs with no FlaUI coverage**

For each dialog not reached by Step 3 (expect: `AccountDialog`, `AddLoginDialog`,
`FreeStyleQueryDialog`, `LoanDialog`, `NewSqliteDatabaseDialog`, `NewSqlServerDatabaseDialog`,
`OpenDatabaseDialog`, `PickDateDialog`, `RecategorizeDialog` if not already covered,
`RentalDialog`, `ReportRangeDialog`, `SelectAccountDialog`, and any others this task's Step 1
listing turns up) - launch the app, trigger each one through its real UI entry point, and confirm:
renders correctly in both Light and Dark theme (Ctrl+L), no clipped text, no blank/flat-color
regions, standard controls (buttons, text boxes, combo boxes) are legible and interactive. This is
the live-review artifact the design spec's testing strategy calls for - screenshot each one (light
+ dark) to `docs/superpowers/plans/2026-09-20-ui-modernization-phase2-dialogs-artifacts/` (create
the directory) as the reviewable record of this sweep, rather than relying on "someone happened to
be watching."

- [ ] **Step 5: Triage any real issue found**

If Step 4 finds a genuinely broken dialog (not just a cosmetic preference difference), do not fix
it inline as an unplanned addition to this task - stop, document exactly what's broken and why
(matching this plan's own root-cause discipline, not just "looks wrong"), and add a new task to
this plan before continuing, following the same Task Structure as Tasks 1-5 above.

- [ ] **Step 6: Commit the sweep's artifacts and any `VisualGuard` additions**

```bash
git add docs/superpowers/plans/2026-09-20-ui-modernization-phase2-dialogs-artifacts/ \
        Source/WPF/UITests/Basics/PayeesFlaUiTests.cs Source/WPF/UITests/Basics/CategoriesFlaUiTests.cs
git commit -m "test: Phase 2 re-verification sweep of remaining dialogs, add VisualGuard checks"
```

---

## Self-Review

**Spec coverage:** Task 1 covers the "Phase 2's actual first task" (BaseDialog fix). Task 2 covers
the design spec's updated Testing strategy (`VisualGuard`). Tasks 3-5 cover the 5
ModernWpf-referencing dialogs named in the Phase 2 Planning Notes, in the sequencing the panel
recommended (simple icon-only dialogs before the two hard cases; `RenamePayeeDialog` itself needs
no markup changes per Task 6's finding that it - like the other ~19 - only needed
re-verification, so it's covered there, not as its own earlier task). Task 6 covers the ~19
re-verification dialogs. Not covered by this plan (explicitly out of scope, deferred to Phase 3
per the spec): `Views/TransactionsView.xaml`'s own use of
`SystemControlPageBackgroundChromeMediumLowBrush`, and the app-wide (non-dialog) usages of the
same brush keys found in `Charts/`, `Controls/`, `View Selectors/`, `Views/` during this plan's own
research - flagged here for whoever writes the Phase 3 plan, not silently dropped.

**Placeholder scan:** Task 3/5's icon-name code blocks are explicitly marked as best-guess
starting points requiring Step 1's real reflection output to confirm/correct - this is not a
placeholder in the prohibited sense (vague "TBD"/"add error handling"), it's real, compilable code
with an explicit, concrete verification step attached, consistent with how the merged Phase 1
plan's own Task 6 handled the same uncertainty (its `SymbolRegular` names were verified via the
identical technique before landing).

**Type consistency:** `VisualGuard`'s three method signatures (`AssertNotClipped(AutomationElement)`,
`AssertNotBlank(AutomationElement, bool)`, `AssertSettledWithin(AutomationElement, TimeSpan, Action)`)
are used identically across Tasks 3-6 wherever referenced.

## Next step

Offer execution choice: subagent-driven-development (recommended, fresh subagent per task +
review) or executing-plans (inline, batch execution with checkpoints).
