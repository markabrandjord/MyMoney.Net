# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope of this file

Build/test/contribution steps already live in `docs/dev/index.md` — don't
duplicate them here, link to them. This file covers things that aren't
obvious from the docs or from reading the code: the fork workflow specific to
this checkout, and a running log of non-obvious gotchas as they're discovered.

## What this is

This is `markabrandjord`'s fork of [MoneyTools/MyMoney.Net](https://github.com/MoneyTools/MyMoney.Net),
a WPF personal-finance app (C#, .NET 10.0). Being evaluated as a Quicken
Classic replacement by importing Quicken data into it; if it doesn't work out
this checkout gets deleted, otherwise the migration becomes permanent.

## Git remotes and workflow

- `origin` → `markabrandjord/MyMoney.Net` (your fork) — fetch & push
- `upstream` → `MoneyTools/MyMoney.Net` (original project) — fetch only, push is disabled
- `master` tracks `origin/master`

Sync fork with upstream:
```
git checkout master
git fetch upstream
git merge upstream/master
git push origin master
```

New work branches off `master`. To contribute back, push the branch to
`origin` and open a PR against `MoneyTools/MyMoney.Net:master` (`gh pr create`).

## Build / test / run

See `docs/dev/index.md` for the full walkthrough (Visual Studio, DGML
scenario tests, project dependency diagram). Quick reference:

- Solution: `Source/WPF/MyMoney.sln` (main app + tests); `Source/WPF/MyMoneyPackage.sln` is the ClickOnce packaging project.
- Build: `dotnet build Source/WPF/MyMoney.sln`
- Unit tests (NUnit, in `Source/WPF/UnitTests`): `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
- Run every test project in the solution (e.g. also picks up `Source/WPF/MyMoney.TestSupport`): `dotnet test Source/WPF/MyMoney.sln`
- Single test: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "Name=TestMethodName"`
- The `ScenarioTest` project is a separate model-based integration test driven by `TestModel.dgml`, not run via `dotnet test` — see the docs for the DGML Test Monitor tooling it needs.
- Windows-only (WPF + WinForms); build/run requires Windows.

## Architecture

- Root namespace `Walkabout`. Main app project: `Source/WPF/MyMoney`.
- `Database/Money.cs` is the core domain model — a single ~15k-line file
  defining the object graph (`MyMoney`, `Accounts`/`Account`, `Transactions`/`Transaction`,
  `Categories`/`Category`, `Securities`, `Payees`, `Splits`, `RentBuildings`, etc.).
  Every persistent type derives from `PersistentObject`, and collections derive
  from `PersistentContainer`, which is what drives change tracking
  (`ChangeType`: Inserted/Changed/Deleted/...) and dirty-state/save logic.
- Storage is pluggable behind `IDatabase` (`Database/IDatabase.cs`): implementations
  include `SqliteDatabase`, `SqlDatabase`, `SqlCeDatabase`, `XmlStore`, `CsvStore`.
  SQLite is the default local file format.
- `Importers/` and `Ofx/` handle bringing in external data (OFX/QFX, CSV, QIF-style
  imports) — relevant to the Quicken-conversion evaluation this fork exists for.
- `Reports/`, `Charts/`, `Taxes/` are feature areas built on top of the core model.
- Other source trees are separate/experimental UI ports, not part of the main
  app: `Source/Xamarin` (legacy mobile) and `Source/Uno` (in-progress Uno
  Platform port). Don't assume changes to `Source/WPF/MyMoney` need mirroring there.

## Things that have been gotten wrong before

(Empty so far — this fork was just created. Add entries here as real mistakes
happen, with enough concrete detail — file, symptom, actual cause — to avoid
repeating them. Don't pre-fill with speculative gotchas.)
