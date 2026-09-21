# UI Modernization: WPF-UI Migration (Phase 0 + Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Validate `lepoco/wpfui` against this app's highest-risk UI pattern (a `DataGrid` cell's
edit-mode template swap), then swap the dead `ModernWpfUI` dependency for `WPF-UI` and migrate
`MainWindow`'s chrome (menu, toolbar, toolbox accordion, status bar, window style) to it.

**Architecture:** Phase 0 is an isolated, reversible spike — a throwaway WPF window that merges
WPF-UI's theme resources into its own scope (not `Application.Resources`), so it can be built,
tested, and deleted without touching the real app at all. Phase 1 is the real migration: remove
`ModernWpfUI`, add `WPF-UI` at the `Application` level, and convert every `ModernWpfUI`-specific
type `MainWindow.xaml` currently uses to its verified `WPF-UI` equivalent.

**Tech Stack:** C#/.NET 10, WPF, `WPF-UI` (NuGet package `WPF-UI`, version `4.3.0`, namespace
`http://schemas.lepo.co/wpfui/2022/xaml`), NUnit (`[Apartment(ApartmentState.STA)]` for
in-process WPF window tests), FlaUI (for the existing Basics regression suite).

**Spec:** `docs/superpowers/specs/2026-09-19-ui-modernization-wpfui-migration-design.md`

## Global Constraints

- Target framework stays `net10.0-windows7.0` (already WPF-UI 4.3.0's supported TFM — no
  downgrade/upgrade needed).
- Don't touch `Source/WPF/MyMoney/Themes/generic.xaml`'s `TextBoxHelper.IsEnabled` setter or any
  file using `ui:AppBarButton`/`ui:CommandBar`/`ui:DropDownButton`/`ui:TextBoxHelper` in this
  plan — `WPF-UI` has no verified equivalent for `AppBarButton`/`CommandBar` (confirmed via
  research, not found in its public API), and those types don't appear in `MainWindow.xaml` at
  all. They're isolated to `AttachmentDialog.xaml`, `CategoryDialog.xaml`, `QuickFilterControl.
  xaml`, `AliasesView.xaml`, `CurrenciesView.xaml`, `SecuritiesView.xaml`, `TransactionsView.xaml`,
  and `generic.xaml` — all Phase 2/3 territory per the spec, out of scope here, and needing their
  own design pass (not a guess) before they're touched.
- Every phase ends with the existing Basics FlaUI suite (`Source/WPF/UITests/Basics/*`, currently
  14 tests) still green — run it as part of each task's own deliverable, not deferred to the end.
- This plan covers Phase 0 and Phase 1 only, per the spec's own phasing rationale (Phase 0 exists
  specifically to inform what's realistic for Phase 2 onward). Phase 2-4 get their own follow-up
  plan once Phase 0/1 findings are in hand.

---

### Task 1: Add the WPF-UI package reference (non-breaking)

**Files:**
- Modify: `Source/WPF/MyMoney/MyMoney.csproj`

**Interfaces:**
- Produces: the `WPF-UI` NuGet package (namespace `Wpf.Ui`, XAML namespace
  `http://schemas.lepo.co/wpfui/2022/xaml`) available to the `MyMoney` project. Nothing
  references it yet, so this step is risk-free — it cannot conflict with the still-installed
  `ModernWpfUI` 0.9.7-preview.2 package until XAML actually imports its namespace.

- [ ] **Step 1: Add the package reference**

In `Source/WPF/MyMoney/MyMoney.csproj`, in the `<ItemGroup>` containing the existing
`<PackageReference Include="ModernWpfUI" Version="0.9.7-preview.2" />` (around line 608), add:

```xml
<PackageReference Include="WPF-UI" Version="4.3.0" />
```

- [ ] **Step 2: Restore and build**

Run: `dotnet restore Source/WPF/MyMoney/MyMoney.csproj`
Run: `dotnet build Source/WPF/MyMoney/MyMoney.csproj`
Expected: Build succeeds, 0 errors. (Both packages are now referenced but only `ModernWpfUI`'s
types are actually used anywhere yet, so there's nothing for them to conflict over.)

- [ ] **Step 3: Commit**

```bash
git add Source/WPF/MyMoney/MyMoney.csproj
git commit -m "build: add WPF-UI 4.3.0 package reference alongside ModernWpfUI"
```

---

### Task 2: Build the isolated edit-mode spike window

**Files:**
- Create: `Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml`
- Create: `Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml.cs`

**Interfaces:**
- Produces: `Walkabout.Dialogs.WpfUiGridEditSpike`, a public `Window` subclass with a public
  parameterless constructor, containing a `DataGrid` named `SpikeGrid` with one
  `DataGridTemplateColumn` (`CellTemplate` = read-only `TextBlock`, `CellEditingTemplate` =
  editable `ComboBox`) bound to a small in-memory list — this directly mirrors the real
  edit-mode-template-swap pattern already used by `CurrenciesView.xaml`'s Symbol column
  (`myTemplateSymbol`/`myTemplateSymbolEdit`, `Source/WPF/MyMoney/Views/CurrenciesView.xaml`
  lines 90-106), which is the highest-risk pattern identified in the design spec.

This window merges `WPF-UI`'s theme resources into its **own** `Window.Resources`, not
`Application.Resources` — this keeps the spike fully isolated from the real app (which still has
`ModernWpfUI` merged at the `Application` level) and fully reversible; deleting these two files
in Task 4 leaves zero trace.

- [ ] **Step 1: Write the spike window's XAML**

```xml
<Window x:Class="Walkabout.Dialogs.WpfUiGridEditSpike"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="WPF-UI Grid Edit Spike" Width="400" Height="300">
    <Window.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ui:ThemesDictionary Theme="Light" />
                <ui:ControlsDictionary />
            </ResourceDictionary.MergedDictionaries>

            <DataTemplate x:Key="SpikeCellDisplay">
                <TextBlock Text="{Binding Code}" Padding="4" />
            </DataTemplate>

            <DataTemplate x:Key="SpikeCellEdit">
                <ComboBox x:Name="SpikeEditCombo"
                          ItemsSource="{Binding RelativeSource={RelativeSource AncestorType=Window}, Path=DataContext.Choices}"
                          SelectedItem="{Binding Code, Mode=TwoWay}" />
            </DataTemplate>
        </ResourceDictionary>
    </Window.Resources>

    <Grid Margin="10">
        <DataGrid x:Name="SpikeGrid" AutoGenerateColumns="False" CanUserAddRows="False">
            <DataGrid.Columns>
                <DataGridTemplateColumn Header="Code"
                                        CellTemplate="{StaticResource SpikeCellDisplay}"
                                        CellEditingTemplate="{StaticResource SpikeCellEdit}" />
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</Window>
```

- [ ] **Step 2: Write the spike window's code-behind**

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace Walkabout.Dialogs
{
    public class SpikeRow : INotifyPropertyChanged
    {
        private string code;
        public string Code
        {
            get => this.code;
            set { this.code = value; this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class WpfUiGridEditSpike : Window
    {
        public ObservableCollection<SpikeRow> Rows { get; } = new ObservableCollection<SpikeRow>
        {
            new SpikeRow { Code = "USD" },
            new SpikeRow { Code = "EUR" },
        };

        public ObservableCollection<string> Choices { get; } = new ObservableCollection<string> { "USD", "EUR", "GBP" };

        public WpfUiGridEditSpike()
        {
            this.InitializeComponent();
            this.DataContext = this;
            this.SpikeGrid.ItemsSource = this.Rows;
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Source/WPF/MyMoney/MyMoney.csproj`
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml.cs
git commit -m "spike: add isolated WPF-UI edit-mode DataGrid spike window (throwaway, see Task 4)"
```

---

### Task 3: Write an in-process test that exercises the spike's edit-mode swap

**Files:**
- Create: `Source/WPF/UnitTests/WpfUiGridEditSpikeTests.cs`

**Interfaces:**
- Consumes: `Walkabout.Dialogs.WpfUiGridEditSpike` (Task 2).
- Produces: nothing further consumes this — it's the spike's own verification, deleted in Task 4.

This runs **in-process** (not via FlaUI/`Application.Launch`) because the question — "does a
`DataGridCell`'s `CellTemplate`→`CellEditingTemplate` swap render and function correctly when
`WPF-UI`'s theme resources are in scope" — is answerable directly by constructing the real
`Window`, forcing layout, and walking the visual tree; it doesn't need a second OS process or UI
Automation. `UnitTests.csproj` already references `MyMoney.csproj` directly (see `CLAUDE.md`'s
note on `TransactionCollection` for precedent), so it can construct WPF types.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using Walkabout.Dialogs;

namespace Walkabout.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class WpfUiGridEditSpikeTests
    {
        [Test]
        public void EnteringEditModeOnTemplateColumn_SwapsInThemedComboBox_AndItRendersWithNonZeroSize()
        {
            var window = new WpfUiGridEditSpike();
            window.Show();
            window.UpdateLayout();

            DataGrid grid = window.SpikeGrid;
            grid.UpdateLayout();

            // Select and begin editing the first row's single template column.
            grid.SelectedIndex = 0;
            grid.CurrentCell = new DataGridCellInfo(grid.Items[0], grid.Columns[0]);
            bool enteredEditMode = grid.BeginEdit();
            grid.UpdateLayout();

            Assert.That(enteredEditMode, Is.True, "DataGrid.BeginEdit() should succeed on the template column.");

            DataGridCell cell = FindCell(grid, 0, 0);
            Assert.That(cell, Is.Not.Null, "Expected to find the DataGridCell for row 0, column 0.");

            ComboBox editCombo = FindVisualChild<ComboBox>(cell);
            Assert.That(editCombo, Is.Not.Null,
                "Expected the CellEditingTemplate's ComboBox to be present in the visual tree after BeginEdit().");
            Assert.That(editCombo.ActualWidth, Is.GreaterThan(0),
                "The themed ComboBox should have a real rendered width, not collapse to zero (the 'big white box'/broken-template failure mode).");
            Assert.That(editCombo.ActualHeight, Is.GreaterThan(0),
                "The themed ComboBox should have a real rendered height.");

            window.Close();
        }

        private static DataGridCell FindCell(DataGrid grid, int row, int column)
        {
            DataGridRow dataGridRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(row);
            if (dataGridRow == null)
            {
                return null;
            }
            DataGridCellsPresenter presenter = FindVisualChild<DataGridCellsPresenter>(dataGridRow);
            return (DataGridCell)presenter?.ItemContainerGenerator.ContainerFromIndex(column);
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                {
                    return typed;
                }
                T found = FindVisualChild<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
```

- [ ] **Step 2: Run it and record the result**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~WpfUiGridEditSpikeTests"`

Expected: one of two outcomes, both are valid findings to record in Task 4 — this is a spike, not
a pass/fail gate on its own:
- **PASS** — the themed `ComboBox` rendered with a real size inside the swapped-in edit template.
  This is the "WPF-UI is compatible with the edit-mode-template-swap pattern" finding.
- **FAIL** on the `ActualWidth`/`ActualHeight` assertions specifically (not a crash) — this is the
  "big white box"-class failure predicted by the design spec's research. Also a valid, useful
  finding: it means Phase 1+ needs either a style override for `DataGridCell`/`ComboBox` or a
  fallback plan, decided in Task 4, not guessed here.

Do not "fix" the spike code to force a pass — the whole point is an honest answer.

---

### Task 4: Record the spike finding and clean up

**Files:**
- Modify: `docs/superpowers/specs/2026-09-19-ui-modernization-wpfui-migration-design.md`
- Delete: `Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml`
- Delete: `Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml.cs`
- Delete: `Source/WPF/UnitTests/WpfUiGridEditSpikeTests.cs`

- [ ] **Step 1: Add a "Phase 0 finding" section to the design spec**

Append a new section to `docs/superpowers/specs/2026-09-19-ui-modernization-wpfui-migration-design.md`,
directly below "## Phased plan", recording:
- The exact test result from Task 3 (pass or fail, with the actual `ActualWidth`/`ActualHeight`
  values observed if it failed).
- If it passed: confirmation that Phase 1 can proceed as planned.
- If it failed: what specifically broke (e.g., the `ComboBox` never received a `Style` at all, or
  received one but with a zero-size template part), and whether a targeted style override is a
  reasonable Phase 1 follow-up task or whether this should escalate back to a design discussion
  (per the original design spec's own "if this spike reveals a dead end... stop and reconsider").

- [ ] **Step 2: Delete the throwaway spike files**

```bash
git rm Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml Source/WPF/MyMoney/Dialogs/WpfUiGridEditSpike.xaml.cs Source/WPF/UnitTests/WpfUiGridEditSpikeTests.cs
```

- [ ] **Step 3: Build and run the full existing suites to confirm the deletion is clean**

Run: `dotnet build Source/WPF/MyMoney.sln --no-incremental -t:Rebuild`
Expected: 0 errors.

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: same pass count as before Task 2 (188, per the last confirmed baseline) — the spike
test is gone, nothing else changed.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-09-19-ui-modernization-wpfui-migration-design.md
git commit -m "docs: record Phase 0 spike finding, remove throwaway WPF-UI edit-mode spike"
```

**STOP HERE if Task 3's finding was a failure that isn't a reasonable style-override fix.** Do
not proceed to Task 5 — return to the design/brainstorming step instead, per the spec's own
"if this spike reveals a dead end... stop and reconsider before Phase 1" instruction.

---

### Task 5: Remove ModernWpfUI, add WPF-UI at the application level

**Files:**
- Modify: `Source/WPF/MyMoney/MyMoney.csproj`
- Modify: `Source/WPF/MyMoney/App.xaml`

**Interfaces:**
- Produces: `WPF-UI`'s `ThemesDictionary`/`ControlsDictionary` merged into
  `Application.Resources`, and `App.xaml`'s own `xmlns:ui=` repointed to `WPF-UI`'s schema
  (`http://schemas.lepo.co/wpfui/2022/xaml`). Each of the other 19 `ui:`-using files declares its
  own independent `xmlns:ui=` and is unaffected by `App.xaml`'s change (XAML namespace prefixes
  are per-file, not inherited). What *does* affect all 19 of them is Step 1's removal of the
  `ModernWpfUI` package reference — its assembly becomes unavailable, so every file still
  declaring `xmlns:ui="http://schemas.modernwpf.com/2019"` (which, until Task 6 repoints
  `MainWindow.xaml` specifically, is all 19 remaining files) fails to compile, temporarily. This
  is expected and resolved within this same task (Step 4) by restoring the package reference and
  re-merging `ModernWpfUI`'s resources under a second, distinct prefix — this task does not
  attempt a `WPF-UI` replacement for `AppBarButton`/`CommandBar`/`TextBoxHelper`/`DropDownButton`
  (see Global Constraints); those 8 files keep working exactly as before, unmigrated.

- [ ] **Step 1: Remove the ModernWpfUI package reference**

In `Source/WPF/MyMoney/MyMoney.csproj`, delete the line:

```xml
<PackageReference Include="ModernWpfUI" Version="0.9.7-preview.2" />
```

- [ ] **Step 2: Update App.xaml's resource merge**

`Source/WPF/MyMoney/App.xaml`'s root `<Application>` element declares
`xmlns:ui="http://schemas.modernwpf.com/2019"` (confirmed by reading the file - this is
`ModernWpfUI`'s real schema URI, not a guess). Since every other file in the app declares its own
independent `xmlns:ui=` (XAML namespace prefixes are scoped per-file, not inherited from
`App.xaml`), changing this one declaration only affects type/resource resolution *within
`App.xaml` itself* - it does not affect the 8 out-of-scope files' own `ui:` usage, which keeps
resolving against `ModernWpfUI` exactly as before, unaffected by this step.

Change line 6 from:

```xml
xmlns:ui="http://schemas.modernwpf.com/2019">
```

to:

```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
```

Then replace the `<ResourceDictionary.MergedDictionaries>` block's first three entries:

```xml
<ui:ThemeResources />
<ui:XamlControlsResources />
<ui:IntellisenseResources/>
```

with:

```xml
<ui:ThemesDictionary Theme="Light" />
<ui:ControlsDictionary />
```

Leave the fourth entry, `<ResourceDictionary Source="Themes/generic.xaml"/>`, untouched -
`generic.xaml` is one of the 8 out-of-scope files (it has its own `TextBoxHelper.IsEnabled`
usage) and stays merged as-is.

- [ ] **Step 3: Attempt a build and record every resulting error**

Run: `dotnet build Source/WPF/MyMoney/MyMoney.csproj`

Expected: build errors in **every** file that uses a `ui:` type — that's all 20 files from the
design spec's inventory, not just the 8 listed in "Global Constraints". Removing the
`ModernWpfUI` package reference in Step 1 makes its assembly unavailable to every file still
declaring `xmlns:ui="http://schemas.modernwpf.com/2019"` (which, at this point, is all 20 files —
`MainWindow.xaml`'s own repoint doesn't happen until Task 6). This is expected and temporary,
resolved by Step 4 below.

If the error list includes any file **not** in the design spec's 20-file inventory, stop and
investigate before continuing — that means the inventory itself is incomplete.

- [ ] **Step 4: Temporarily re-add ModernWpfUI so the solution builds while Task 6 is in progress**

Removing `ModernWpfUI`'s `PackageReference` in Step 1 makes its assembly unavailable entirely, so
the 8 out-of-scope files (whose own `xmlns:ui=` still points at `http://schemas.modernwpf.com/2019`,
untouched) will fail to compile - not because of anything in `App.xaml`, but because the type
`AppBarButton`/`CommandBar`/etc. no longer resolves to any assembly at all. Fix this by restoring
the package reference and re-merging `ModernWpfUI`'s resources under a **second, distinct xmlns
prefix** in `App.xaml` - `ui:` in this file now means `WPF-UI` (Step 2), so `ModernWpfUI`'s
resources need their own prefix here to be referenced by name in this file's own markup:

In `Source/WPF/MyMoney/App.xaml`, add a second namespace declaration to the root `<Application>`
element:

```xml
xmlns:mwpf="http://schemas.modernwpf.com/2019"
```

Then add `ModernWpfUI`'s three resource entries back into `<ResourceDictionary.MergedDictionaries>`,
under the new `mwpf:` prefix, placed **before** the `WPF-UI` entries (last-merged wins for a
given implicit style key at the `Application` scope, and `WPF-UI`'s styles should win for any
overlapping base-control style since that's the direction this migration is heading):

```xml
<mwpf:ThemeResources />
<mwpf:XamlControlsResources />
<mwpf:IntellisenseResources/>
<ui:ThemesDictionary Theme="Light" />
<ui:ControlsDictionary />
<ResourceDictionary Source="Themes/generic.xaml"/>
```

(This only changes what `App.xaml` itself calls the prefix - `generic.xaml` and the 8 other
out-of-scope files keep their own independent `xmlns:ui="http://schemas.modernwpf.com/2019"`
declarations and are completely unaffected by what `App.xaml` names its prefix.)

In `Source/WPF/MyMoney/MyMoney.csproj`, add back:

```xml
<PackageReference Include="ModernWpfUI" Version="0.9.7-preview.2" />
```

Run: `dotnet build Source/WPF/MyMoney/MyMoney.csproj`
Expected: build succeeds now. All 20 `ui:`-using files resolve again, since the `ModernWpfUI`
assembly is back and every file except `App.xaml` still declares its own unchanged
`xmlns:ui="http://schemas.modernwpf.com/2019"`. At this point `MainWindow.xaml` is still using
its *old* `ModernWpfUI` window-style/theme/icon markup, unmigrated — Task 6 is what actually
repoints `MainWindow.xaml`'s own `xmlns:ui=` to `WPF-UI` and converts its icons. This is a
deliberate, temporary, two-library-coexistence state at the `App.xaml`/package level, not yet
reflected in any individual screen's visuals — expected to look visually inconsistent between
migrated and unmigrated screens once Task 6 lands, until the deferred follow-up plan finishes
Phase 2/3. Record this explicitly as a known, intentional transitional state, not a bug.

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney/MyMoney.csproj Source/WPF/MyMoney/App.xaml
git commit -m "build: add WPF-UI application-level resources; keep ModernWpfUI temporarily for the 8 files not yet migrated"
```

---

### Task 6: Migrate MainWindow's chrome to WPF-UI

**Files:**
- Modify: `Source/WPF/MyMoney/MainWindow.xaml`

**Interfaces:**
- Consumes: `WPF-UI`'s `SymbolIcon` (`Wpf.Ui.Controls.SymbolIcon`, `Symbol` property of type
  `Wpf.Ui.Controls.SymbolRegular`), `SplitButton` (`Wpf.Ui.Controls.SplitButton`, has a `Flyout`
  property), `Flyout` (`Wpf.Ui.Controls.Flyout`) — all confirmed present in `WPF-UI` 4.3.0's
  public API (Task 5 landed the package that provides these).

`MainWindow.xaml`'s current `ModernWpfUI` usage (confirmed via `grep`, nothing else) is:

```
ui:WindowHelper.UseModernWindowStyle="True"    (line 13)
ui:ThemeManager.IsThemeAware="True"            (line 14)
ui:SymbolIcon Symbol="Download"                (line 249)
ui:SplitButton ... Click="PendingChangeClicked" (line 256)
ui:SymbolIcon Symbol="Save"                    (line 259)
ui:SplitButton.Flyout / ui:Flyout ...           (lines 263-276)
ui:SymbolIcon Symbol="AllApps"                 (line 283)
```

- [ ] **Step 1: Repoint MainWindow.xaml's own `xmlns:ui=` declaration**

`MainWindow.xaml` declares its own `xmlns:ui="http://schemas.modernwpf.com/2019"` at line 12,
independent of `App.xaml`'s declaration (confirmed by reading the file - each XAML file's
namespace prefixes are its own, not inherited from `App.xaml`). Change line 12 to:

```xml
xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
```

`WPF-UI` doesn't have a direct `WindowHelper.UseModernWindowStyle` equivalent for a plain
`Window` — its modern-window experience is `Wpf.Ui.Controls.FluentWindow`, a different base
class. Converting `MainWindow`'s base class is a larger change than this task's scope (it's a
4800+ line code-behind file; changing its base type has ripple effects worth its own task). For
this task, remove the two `ModernWpfUI`-specific attributes without replacing them yet - they'd
no longer resolve to anything after the namespace repoint above anyway:

In `Source/WPF/MyMoney/MainWindow.xaml`, delete lines 13-14:

```xml
ui:WindowHelper.UseModernWindowStyle="True"
ui:ThemeManager.IsThemeAware="True"
```

Record in the design spec (Task 8-equivalent finding in the follow-up plan) that adopting
`FluentWindow`/`ApplicationThemeManager` for `MainWindow` is a follow-up task, not done here -
this task only needs the file to compile and render correctly with `WPF-UI`'s control-level
theming, not its window-chrome theming.

- [ ] **Step 2: Replace the three SymbolIcon usages**

`WPF-UI`'s `SymbolIcon.Symbol` takes a `Wpf.Ui.Controls.SymbolRegular` enum value (Fluent System
Icons naming, e.g. `ArrowDownload24`), not `ModernWpfUI`'s `Symbol` enum (`Download`, `Save`,
`AllApps`) — the two enums are unrelated types with different member names. Look up the correct
`SymbolRegular` member for each icon using WPF-UI's icon reference
(https://wpfui.lepo.co/documentation/icons.html, or `Wpf.Ui.Controls.SymbolRegular`'s XML doc
comments via IntelliSense) before writing each line — do not guess a member name.

Replace each of the 3 `<ui:SymbolIcon Symbol="...">` elements at lines 249, 259, and 283 with the
looked-up `SymbolRegular` equivalent, keeping the surrounding `Margin` attribute unchanged, e.g.:

```xml
<ui:SymbolIcon Symbol="{x:Static ui:SymbolRegular.ArrowDownload24}" Margin="0,0,4,0"/>
```

(Confirm the exact `SymbolRegular` member names for the "Download", "Save", and "AllApps"
concepts before committing this step — this plan intentionally does not hardcode unverified enum
member names.)

- [ ] **Step 3: Verify SplitButton and Flyout compile as-is**

`Wpf.Ui.Controls.SplitButton` and `Wpf.Ui.Controls.Flyout` are confirmed to exist with the same
element names and a compatible `SplitButton.Flyout` property structure as `ModernWpfUI`'s. Since
Step 1 already repointed this file's own `xmlns:ui=` declaration to `WPF-UI`'s schema, the
existing `<ui:SplitButton>` and `<ui:Flyout>` markup at lines 256-277 should resolve against
`WPF-UI`'s types and compile unchanged - no further edits needed to this block. Verify via the
build in Step 4.

- [ ] **Step 4: Build**

Run: `dotnet build Source/WPF/MyMoney/MyMoney.csproj`
Expected: 0 errors. If `SplitButton`/`Flyout` markup from Step 3 doesn't compile as-is, that's a
real finding — record the exact compiler error in the design spec rather than guessing a fix.

- [ ] **Step 5: Visual and functional check**

Run the app (`dotnet run --project Source/WPF/MyMoney/MyMoney.csproj`), open the main window, and
confirm:
- The toolbar's download/save/pending-changes area renders with visible icons (not blank/missing
  glyphs — a missing `SymbolRegular` mapping renders as empty space, not a build error).
- Clicking the pending-changes split button's dropdown arrow opens the flyout correctly.

- [ ] **Step 6: Run the full Basics FlaUI suite**

Run: `dotnet test Source/WPF/UITests/UITests.csproj --filter "FullyQualifiedName~Walkabout.UITests.Basics"`
Expected: 14/14 passing (same baseline as before this plan started) - `MainWindow`'s
`AutomationId`s weren't touched, only visual styling, so existing `AutomationId`/`ControlType`
based tests should be unaffected. If any test fails, investigate whether `WPF-UI`'s `SplitButton`
exposes a different `ControlType`/pattern support than `ModernWpfUI`'s did (per the tester
review's specific warning about this risk) before assuming it's unrelated flakiness.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney/MainWindow.xaml
git commit -m "feat: migrate MainWindow chrome (toolbar icons, split button, flyout) to WPF-UI"
```

---

## Self-Review Notes

- **Spec coverage**: This plan implements Phase 0 (Tasks 1-4) and the `MainWindow`-chrome portion
  of Phase 1 (Tasks 5-6) from the design spec in full. It explicitly does not cover: the rest of
  Phase 1 (toolbox accordion, status bar — neither uses any `ModernWpfUI`-specific type per the
  `MainWindow.xaml` grep, so they may already be unaffected, but confirm this explicitly as a
  Task 8-equivalent finding in the follow-up plan), Phase 2 (simple dialogs), Phase 3 (grid
  views + `TransactionConnectorAdorner`), and Phase 4 (duplicate-transaction redesign) - these
  need their own plan once this one's findings (especially Task 4's spike result and the 8
  out-of-scope files' migration approach) are in hand.
- **Placeholder scan**: no TBD/TODO/"handle appropriately" language. The one deliberately
  open item (exact `SymbolRegular` enum member names in Task 6 Step 2) is flagged explicitly as
  "look this up, don't guess" rather than left as an unmarked gap, consistent with this plan's
  own evidence-gathering standard.
- **Type consistency**: `WpfUiGridEditSpike`/`SpikeRow`/`Rows`/`Choices`/`SpikeGrid` names are
  used consistently between Task 2 (definition) and Task 3 (consumption).
