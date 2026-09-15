# Layer Extraction — Design Spec

## Status

Design phase. This is item #1 of the five-item 3-tier architecture
initiative (see the tracking work item, GitHub issue #1, for the full
list). It is the foundation item #3 (business-layer test-support
library) and item #5 (Uno/Xamarin thin clients) both depend on.

## Context

Today, `MyMoney.csproj` is a monolith: the domain model
(`Database/Money.cs`, ~15,000 lines defining `MyMoney`,
`Accounts`/`Account`, `Transactions`/`Transaction`, `Categories`,
`Securities`, `Payees`, `Splits`, `RentBuildings`, and the
`PersistentObject`/`PersistentContainer` change-tracking base classes)
and the storage layer (`IDatabase` and its implementations —
`SqliteDatabase`, `SqlDatabase`, `SqlCeDatabase`, `XmlStore`,
`CsvStore`, and item #2's new `SqlServerStoredProcDatabase`) are
compiled directly into the WPF application project, alongside Views,
Reports, Charts, Taxes, Importers, and everything else. There is no
enforced boundary — nothing stops a UI concern from creeping into the
domain model or the storage layer, and nothing lets another client
(a test-support library, an Uno app, the legacy Xamarin app) consume
the domain model or storage layer without dragging in WPF.

Investigating the current code for this spec surfaced concrete
evidence of that boundary already having eroded:

- `MoneyDataObject.cs`, which lives in `Database/`, implements
  `System.Windows.IDataObject` — WPF clipboard/drag-drop interop, not
  domain data.
- `SqlDatabase.cs` calls `MessageBoxEx.Show(...)` directly (a
  mixed-mode SQL login warning).
- `SampleDatabase.cs` reaches into `Application.Current.MainWindow` to
  set a dialog owner.
- `Money.cs` depends on `StockQuoteCache` (from the separate
  `StockQuotes/` folder, not a separate project), which in turn pulls
  in a stray `using System.Windows.Forms.Design.Behavior;` that
  nothing in the file actually uses.
- `CostBasis.cs` (in `Database/`) has an unused
  `using System.Windows.Navigation;`.

`Money.cs` itself, notably, has **zero** WPF dependencies — the
domain model is already clean; the leaks are all in adjacent files
that happen to share the `Database/` folder.

## Goals

- Pull the domain model and the storage layer out of `MyMoney.csproj`
  into two new class libraries — `MyMoney.Business` and
  `MyMoney.Data` — each independently buildable, each with a real
  public contract, with `MyMoney.csproj` reduced to a thin consumer of
  both.
- Do this as a full extraction in one item: all of `Money.cs` and all
  six `IDatabase` implementations move, not a partial vertical slice.
  A half-migrated state (some storage engines extracted, some not)
  would be a worse resting point than either "all in the monolith" or
  "all extracted."
- Fix the WPF leaks found above as part of the move — not a
  drive-by refactor, but a direct consequence of the boundary this
  item exists to create. Per the brainstorming skill's own guidance:
  existing problems that affect the work get fixed as part of the
  work, not ignored and not expanded into unrelated cleanup.
- Preserve behavior exactly. This is a refactor: the full existing
  test suite must keep passing, unchanged, throughout.
- Prove the resulting layers are genuinely portable: no true
  UI-rendering assembly (`PresentationFramework`/`PresentationCore`)
  in either new DLL. `MyMoney.Data` additionally has zero WPF-family
  references at all; `MyMoney.Business` may reference `WindowsBase`
  for its event-marshaling backbone (discovered during implementation
  to be load-bearing — see the plan's Global Constraints and ledger).
  Full portability, including dropping `WindowsBase`, stays deferred
  to item #5, the entire reason item #5 depends on this item existing.

## Non-Goals

- Building item #3 (the business-layer test-support library) or any
  new inline/mock unit tests beyond proving the layer boundary is
  real. Item #3 explicitly depends on this item and is scoped to do
  that deeper testing work — building it here would be doing item
  #3's job inside item #1.
- Wiring up Uno or Xamarin (item #5). No design work has started
  there and it stays TBD until this item lands.
- Moving `Importers/`, `Ofx/`, `Reports/`, `Charts/`, `Taxes/`, or the
  StockQuotes fetch/throttle machinery (`StockQuoteManager`,
  `Yahoo`/`Polygon`/`TwelveData`/`MarketStack`, throttling). These are
  business-logic-adjacent but not part of the tracking issue's stated
  scope ("the domain model and storage layer"), and none of them are
  a dependency of `Money.cs` itself (see StockQuotes handling below).
  They stay in `MyMoney.csproj` for now.
- Renaming the `Walkabout.Data` namespace, or any other namespace.
  The project/assembly names change; the C# namespaces inside them do
  not. Bundling a namespace rename with a project-structure move would
  compound two risky changes into one unreviewable diff for no forcing
  reason.
- True cross-platform portability of `SqlCeDatabase` (legacy SQL
  Server Compact support). It uses `Microsoft.Win32` registry lookups
  and dynamically loads a Windows-only assembly by reflection — not
  portable regardless of TargetFramework choice. Left as-is; if item
  #5 needs to exclude it later, that's item #5's problem to solve.

## Architecture

```
MyMoney.Business.csproj  (net10.0-windows7.0, WindowsBase allowed, no
                          PresentationFramework/PresentationCore)
  Database/Money.cs           -- MyMoney, Accounts, Transactions, Categories,
                                  Securities, Payees, Splits, RentBuildings,
                                  PersistentObject/PersistentContainer
  Database/IDatabase.cs       -- the storage contract; lives here because
                                  IDatabase.Load/Save reference MyMoney, a
                                  business-layer type (dependency inversion:
                                  the business layer declares the contract,
                                  the data layer implements it)
  StockQuotes/StockQuoteCache.cs  -- moved from MyMoney.csproj's StockQuotes/
                                      folder; the one class Money.cs actually
                                      depends on (stray unused WinForms-design
                                      using dropped in the move)
  (other Money.cs-adjacent Database/ files with no WPF dependency, e.g.
   Mapping.cs, Query.cs, AutoCategorization.cs, CostBasis.cs (dead
   System.Windows.Navigation using dropped), Encryption.cs, SecurityService.cs,
   AsyncSqlQuery.cs, Payments.cs, Money_Loans.cs, DatabaseSettings.cs,
   IDirectorySecurity.cs — final per-file placement confirmed during
   implementation by checking each for WPF references, same method used
   for this spec)

MyMoney.Data.csproj  (net10.0-windows7.0)
  references MyMoney.Business
  Database/SqliteDatabase.cs
  Database/SqlDatabase.cs           -- MessageBoxEx.Show call replaced with
                                        the new UI-prompt callback (below)
  Database/SqlCeDatabase.cs
  Database/XmlStore.cs
  Database/CsvStore.cs
  Database/SqlServerStoredProcDatabase.cs   (item #2)
  Database/DataEngineConfig.cs              (item #2, no WPF dependency)
  Database/DataEngineCredentialStore.cs     (item #2, no WPF dependency)
  Database/DataEnginePasswordGenerator.cs   (item #2, no WPF dependency)
  Database/IDirectorySecurity.cs    -- pre-existing interface
                                        (SqlServerDatabase already has a
                                        settable IDirectorySecurity
                                        SecurityService property; only its
                                        one concrete implementation,
                                        SecurityService, is WPF-app-bound
                                        via Setup/DirectorySetup, so it
                                        stays in MyMoney.csproj, same
                                        injection pattern as
                                        IDataLayerUiCallback below)
  Database/IDataLayerUiCallback.cs  -- new: the UI-decoupling interface
                                        (see below)

MyMoney.csproj  (WPF app, thinned)
  references MyMoney.Business, MyMoney.Data
  Interop/MoneyDataObject.cs    -- relocated here from Database/ (it's WPF
                                    clipboard interop, IDataObject, not
                                    domain data)
  Database/DataEngineStartup.cs -- stays here; it wires into
                                    MainWindow.xaml.cs's constructor and is
                                    inherently WPF-startup-sequence code
  Database/SampleDatabase.cs    -- stays here in full, unmodified in kind:
                                    it's not a storage engine (no IDatabase
                                    implementation, lives in namespace
                                    Walkabout.Assistance not Walkabout.Data)
                                    but a WPF wizard whose Create() method
                                    directly drives a real dialog
                                    (SampleDatabaseOptions.ShowDialog()) as
                                    its actual control flow -- discovered
                                    during implementation planning to be far
                                    more WPF-coupled than a single
                                    Application.Current.MainWindow lookup.
                                    Its portable data-construction logic
                                    (everything after the dialog returns)
                                    is split out into a new
                                    SampleDataGenerator class in
                                    MyMoney.Business instead -- not routed
                                    through IDataLayerUiCallback, since the
                                    dialog-driving code that stays here
                                    never crosses the assembly boundary at
                                    all.
  StockQuotes/ (minus StockQuoteCache.cs, IStockQuoteService.cs) --
                                    StockQuoteManager and the fetch
                                    providers stay here, now consuming
                                    StockQuoteCache/StockQuoteHistory from
                                    MyMoney.Business instead of sibling
                                    files
  a new class implementing IDataLayerUiCallback with the real
    MessageBoxEx call, registered wherever SqlServerDatabase is
    constructed (IDataLayerUiCallback ended up serving only the
    SqlDatabase.cs mixed-mode-login warning -- see above for why
    SampleDatabase doesn't use it)
  everything else unchanged: Views/, Reports/, Charts/, Taxes/,
    Importers/, Ofx/, MainWindow.xaml.cs, etc.
```

## UI-Decoupling Mechanism

Investigation during implementation planning found `SampleDatabase.cs`
is not actually a data-layer class at all (see Architecture, above) —
it stays in `MyMoney.csproj` entirely, so its
`Application.Current.MainWindow` dialog-owner lookup never needs to
cross the assembly boundary. That leaves exactly one real UI call
inside the code being extracted: `SqlServerDatabase.AddLogin`'s
mixed-mode-login warning (`SqlDatabase.cs`) — the original authors had
already left a `// TODO - we need to remove any UI from the DataBase
model lower layer` comment at that exact line. It's replaced by a
single small interface in `MyMoney.Data`:

```csharp
namespace Walkabout.Data
{
    public interface IDataLayerUiCallback
    {
        void ShowWarning(string message, string title);
    }
}
```

`SqlServerDatabase` takes an `IDataLayerUiCallback` via a settable
property (matching the existing `SecurityService` property's pattern
on the same class) and calls it instead of touching
`MessageBoxEx`/`MessageBoxButton`/`MessageBoxImage` directly.
`MyMoney.csproj` provides the real implementation (backed by
`MessageBoxEx.Show`) and sets it wherever `SqlServerDatabase` is
constructed. If no callback is supplied, the call site degrades
gracefully (a no-op via `?.`) rather than throwing.

## Migration Sequencing

Order is dictated by the dependency graph: `MyMoney.Data` depends on
`MyMoney.Business` (confirmed concretely — `SqlDatabase.cs` alone
references domain types like `Payee`/`Account`/`Transaction`/`MyMoney`
171 times), so extraction has to happen in that order.

1. **`MyMoney.Business` first.** Move `Money.cs`, `IDatabase.cs`,
   `StockQuoteCache.cs`, and the other WPF-free `Database/`-adjacent
   files. Verify it builds standalone before touching anything else.
2. **`MyMoney.Data` second.** Move the six `IDatabase` implementations
   and item #2's config/credential/password classes; add
   `IDataLayerUiCallback` and wire `SqlDatabase`/`SampleDatabase` to
   use it. Verify it builds standalone, referencing only
   `MyMoney.Business`.
3. **Thin `MyMoney.csproj` last.** Remove the now-moved files, add
   `ProjectReference`s to both new projects, relocate
   `MoneyDataObject.cs`, implement `IDataLayerUiCallback` for real,
   leave `DataEngineStartup.cs` and the StockQuotes fetch machinery in
   place.
4. **Update downstream consumers.** `UnitTests.csproj`,
   `MyMoneyAdmin.csproj`, `UITests.csproj`, `ScenarioTest.csproj` get
   `ProjectReference`s to whichever of the new projects they actually
   need, added mechanically.

Namespaces are preserved throughout (`Walkabout.Data` stays
`Walkabout.Data`) — this is a project-boundary move, not a rename, so
existing `using` statements in untouched files keep compiling
unchanged.

## Testing / Verification

- Full solution build stays green at every stage above — no
  intermediate state ships a broken build.
- The full existing test suite (currently 29 passed, 1 skipped, 0
  failed) must keep passing, unchanged, throughout. This is a
  refactor: any existing test that needs to change to keep passing is
  a signal something leaked, not an expected cost of the work.
- A new, automated portability check (added to `UnitTests.csproj` or a
  small dedicated test) asserts `MyMoney.Business.dll` and
  `MyMoney.Data.dll` reference no WPF assembly
  (`PresentationFramework`/`PresentationCore`/`WindowsBase`) — turns
  the "is this actually portable" question into something `dotnet
  test` answers on every run, not a one-time manual inspection.
- `UnitTests.csproj` gains direct `ProjectReference`s to
  `MyMoney.Business` and `MyMoney.Data` (not just transitively via
  `MyMoney.csproj`) — proves the two DLLs are independently
  consumable, which is the actual point of this item.
- The existing FlaUI scenario test from item #4
  (`PayeeSelectionTests.SelectingAPayee_AfterOpeningFixtureFile_DoesNotCrash`)
  is re-run against the thinned app as an end-to-end regression smoke
  check — not new UI test coverage (out of scope, see Non-Goals), just
  reuse of what item #4 already built to confirm the real running app
  still works after the boundary move.
- No new inline/mock unit tests beyond the two boundary-proof checks
  above. Deeper business-layer test coverage is item #3's explicit
  scope, not this item's.

## Open Questions / Future Work

- Final per-file placement of the smaller `Database/`-adjacent files
  (`Mapping.cs`, `Query.cs`, `AutoCategorization.cs`, `Encryption.cs`,
  `SecurityService.cs`, `AsyncSqlQuery.cs`, `Payments.cs`,
  `Money_Loans.cs`, `DatabaseSettings.cs`, `IDirectorySecurity.cs`) is
  confirmed file-by-file during implementation using the same
  WPF-reference check used for this spec, not decided in advance here
  — none showed WPF dependencies in the investigation for this spec,
  so the working assumption is all of them join `MyMoney.Business`,
  but this gets verified, not assumed, per file.
- True cross-platform portability (dropping the `-windows7.0` TFM
  suffix, dealing with `SqlCeDatabase`) is explicitly deferred to
  item #5, which is unspecced and TBD.
- Whether `IDataLayerUiCallback` should grow additional methods is
  deferred until a third real UI-in-data-layer call site is found —
  YAGNI; two call sites don't justify speculative extensibility.
