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

- **Merging long-lived branches can hit false "whole-file" conflicts from
  `core.autocrlf=true` + `.gitattributes`' `*.cs text eol=crlf` fighting each
  other.** Symptom: `git merge` reports a conflict where the *entire* file is
  one `<<<<<<<`/`=======`/`>>>>>>>` block, even though the real edits on each
  side are small. Cause: some files' blobs are stored with literal CRLF
  (never renormalized — the repo uses an *incremental* normalization
  strategy per issue #10, so old/untouched files can still be CRLF-native in
  the object store) while others are properly LF-native (git's normal form,
  smudged to CRLF on checkout). When one side of a merge is LF-native and the
  other is CRLF-native, git's diff3 can't align a single line and gives up on
  the whole file. `git merge --abort` can also fail here ("Entry not
  uptodate") because checkout itself re-triggers a clean/smudge mismatch on
  the affected files. Fix: verify with `git cat-file -p <blob> | file -`
  (raw stored bytes, bypassing smudge) which side is inconsistent; extract
  base/ours/theirs via `git cat-file -p <blob-sha>` (get shas from
  `git ls-files -u`), normalize all three to LF, run `git merge-file` on the
  normalized copies to get the real 3-way merge, then copy that result back
  into the working file (git's `eol=crlf` will smudge it back to CRLF on
  checkout as usual). Confirmed via `dotnet build`/`dotnet test` giving
  identical results before and after — the fix only touched line endings and
  genuinely merged content, not behavior.
