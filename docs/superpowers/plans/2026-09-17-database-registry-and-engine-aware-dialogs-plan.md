# Database Registry & Engine-Aware Dialogs (Issue #32) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `DataEngineConfig`/`DataEngineCredentialStore`/`MyMoneyAdmin.exe` with one JSON `DatabaseRegistry`, engine-aware `File | New` dialogs (SQL Server DEBUG-only, SQLite always), a registry-backed `File | Open` and `RecentFilesMenu`, and an in-process (no separate `.exe`) SQL Server bootstrap driven by two new permanent stored procedures.

**Architecture:** A new `DatabaseRegistry` class (`MyMoney.Data`) owns the single `dataengine.config.json` file (servers' credentials + named database entries). A new `SqlServerBootstrapper` (`MyMoney.Data`) replaces `MyMoneyAdmin.exe`'s `BootstrapRunner`/`SaBootstrapConnection`, calling two new permanent `master`-scoped stored procs instead of the old create-drop temp-proc script. New WPF dialogs (`NewSqlServerDatabaseDialog`, `NewSqliteDatabaseDialog`, `OpenDatabaseDialog`, `SaCredentialDialog`) replace `CreateDatabaseDialog`'s File|New/Open usage. `RecentFilesMenu` becomes a pull-based view over the registry instead of its own path list. The SQL Server test harness reads the registry instead of three env vars.

**Tech Stack:** C# / .NET 10.0-windows7.0, WPF, Newtonsoft.Json 13.0.4, Microsoft.Data.SqlClient 7.0.2, NUnit (existing `Source/WPF/UnitTests` + `Source/WPF/MyMoney.TestSupport` projects).

**Spec:** `docs/superpowers/specs/2026-09-17-database-registry-and-engine-aware-dialogs-design.md` (as amended by the two follow-up conversation turns: Release `File | New` has no submenu and goes straight to the SQLite dialog with no "Test database" checkbox; the checkbox reads "Test database:" + checkbox; the `TestDatabase` field is PascalCase against an otherwise camelCase schema; entries carry an optional free-text `comment`/`Comment` field). The spec is authoritative for scope/behavior; this plan is authoritative for exact files, signatures, and code.

## Global Constraints

- SQL Server stays a **DEBUG-only** engine this WI (`#if DEBUG`, compiled out — not merely hidden — in Release). Lifting this is explicitly Future Work, not this WI.
- **No environment variables anywhere in the startup/connection path** once this plan lands — `MYMONEY_TEST_SQLSERVER_USER_CONNECTION`, `MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`, `MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE` are all removed, not just deprecated.
- **`sa`'s password is never persisted anywhere** (a deliberate break from today's `SaBootstrapConnection`, which caches it in the credential store) — it is prompted for every time a server needs bootstrapping.
- **No `RoleGuardedSqliteDatabase` enforcement, no remaining 5 `_Test_Reset` procs, no test-tier business API, no config migration tooling, no secrets-merge mechanism, no upgrade-rehearsal pipeline** — all explicitly out of scope (spec Non-Goals); do not build them.
- `TestDatabase` is `false` by construction on every database Release ever creates (checkbox compiled out) and Release's `File | Open` / `RecentFilesMenu` always filter out `TestDatabase: true` entries regardless.
- `Database/SqlScripts/` in the spec text actually means **`Source/WPF/MyMoney.Data/SqlScripts/{Bootstrap,Access,Test,Migrations}/`** — there is no `Schema/` folder; schema comes from `SqlServerStoredProcDatabase.LazyCreateTables()` (reflection over `[TableMapping]`), not scripts.
- Namespaces: registry/bootstrap/connection-factory classes go in `Walkabout.Data` (project `MyMoney.Data`); new WPF dialogs go in `Walkabout.Dialogs` (project `MyMoney`); `RecentFilesMenu` stays in `Walkabout.Utilities`.

---

## File Structure

**New files:**
- `Source/WPF/MyMoney.Data/DatabaseRegistry.cs` — `DatabaseRole` enum, `DatabaseCredential`, `DatabaseServerEntry`, `DatabaseEntry`, `DatabaseRegistry` (load/save/connection-string building).
- `Source/WPF/MyMoney.Data/SqlServerConnectionFactory.cs` — one static helper turning a registry + display name into a ready `SqlServerStoredProcDatabase`.
- `Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_BootstrapServer.sql` — new permanent proc (replaces the login-creation half of `CreateDatabaseAndLogins.sql`).
- `Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_CreateCatalog.sql` — new permanent proc (replaces the catalog-creation half).
- `Source/WPF/MyMoney.Data/ISaCredentialPrompt.cs` — WPF-agnostic abstraction for prompting for `sa`'s password.
- `Source/WPF/MyMoney.Data/SqlServerBootstrapper.cs` — in-process replacement for `MyMoneyAdmin`'s `BootstrapRunner`.
- `Source/WPF/MyMoney/Dialogs/SaCredentialDialog.xaml` / `.xaml.cs` — implements `ISaCredentialPrompt`.
- `Source/WPF/MyMoney/Dialogs/NewSqlServerDatabaseDialog.xaml` / `.xaml.cs` — DEBUG-only.
- `Source/WPF/MyMoney/Dialogs/NewSqliteDatabaseDialog.xaml` / `.xaml.cs`.
- `Source/WPF/MyMoney/Dialogs/OpenDatabaseDialog.xaml` / `.xaml.cs`.
- `Source/WPF/UnitTests/DatabaseRegistryTests.cs`.
- `Source/WPF/UnitTests/SqlServerBootstrapperTests.cs` (the testable, non-DB parts — batch-splitting/USE-skipping).

**Modified files:**
- `Source/WPF/MyMoney/Database/DataEngineStartup.cs` — rewritten to use `DatabaseRegistry`/`SqlServerConnectionFactory`, drop the exe shell-out and `RequiredDatabaseName`.
- `Source/WPF/MyMoney/Utilities/RecentFilesMenu.cs` — rewritten to be a pull-based registry view.
- `Source/WPF/MyMoney/Utilities/Settings.cs` — drop the `RecentFiles` property.
- `Source/WPF/MyMoney/MyMoney.csproj` — gains its own DEBUG-only `SqlScripts` content-copy item (see Task 10 Step 5) now that `MyMoneyAdmin.csproj` — today's only source of that copy — is deleted.
- `Source/WPF/MyMoney/MainWindow.xaml` — `File | New` becomes a DEBUG-only submenu host; `CommandBinding Command="New"` removed.
- `Source/WPF/MyMoney/MainWindow.xaml.cs` — new menu wiring, `OpenRegisteredDatabase`, `RegisterRecentDatabase`; dead `CreateDatabaseDialog`-based `NewDatabase()`/`OpenDatabase()`/`InitializeCreateDatabaseDialog()` deleted.
- `Source/WPF/MyMoney.TestSupport/SqlServerTestDatabase.cs`, `SqlServerDatabaseContractTests.cs`, and `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs` — read the registry instead of env vars (`SqlServerDatabaseContractTests` keeps its documented fail-loud-not-skip behavior; only `SqlServerStoredProcDatabaseTests` skips).
- `Source/WPF/MyMoney/dataengine.config.template.json` — updated to the new schema.

**Deleted files:**
- `Source/WPF/MyMoneyAdmin/` (entire project: `Program.cs`, `BootstrapRunner.cs`, `SaBootstrapConnection.cs`, `RetryLoop.cs`, `MyMoneyAdmin.csproj`), removed from `Source/WPF/MyMoney.sln`.
- `Source/WPF/MyMoney.Data/DataEngineConfig.cs`, `Source/WPF/MyMoney.Data/DataEngineCredentialStore.cs`.
- `Source/WPF/MyMoney.TestSupport/MyMoneyTestConnectionResolver.cs` — its two-config-sources-cross-validation purpose is moot with a single registry source; superseded by `DatabaseRegistry.BuildConnectionString`.
- `Source/WPF/UnitTests/DataEngineConfigTests.cs`, `Source/WPF/UnitTests/DataEngineCredentialStoreTests.cs`, `Source/WPF/UnitTests/MyMoneyTestConnectionResolverTests.cs`.
- `Source/WPF/MyMoney/Dialogs/CreateDatabaseDialog.xaml` / `.xaml.cs` (only after Task 10 confirms no remaining callers).
- `Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/CreateDatabaseAndLogins.sql`.

---

## Task 1: `DatabaseRegistry` model, JSON schema, round-trip tests

**Files:**
- Create: `Source/WPF/MyMoney.Data/DatabaseRegistry.cs`
- Test: `Source/WPF/UnitTests/DatabaseRegistryTests.cs`

**Interfaces:**
- Produces: `enum DatabaseRole { Admin, User, Test }`; `class DatabaseCredential { string UserId; string Password; }`; `class DatabaseServerEntry { string Comment; DatabaseCredential MyMoneyAdmin/MyMoneyUser/MyMoneyTest; DatabaseCredential GetCredential(DatabaseRole) }`; `class DatabaseEntry { DataEngineType Engine; string Server; string Catalog; string Path; bool TestDatabase; DateTime? LastUsedUtc; string Comment; }`; `class DatabaseRegistry { Dictionary<string,DatabaseServerEntry> Servers; Dictionary<string,DatabaseEntry> Databases; static string GetDefaultPath(); static DatabaseRegistry Load(string path); void Save(); string BuildConnectionString(DatabaseEntry entry, DatabaseRole role); }`. Reuses existing `Walkabout.Data.DataEngineType` enum (`Sqlite`/`SqlServer`, already defined in `DataEngineConfig.cs` — do **not** redefine it; `DataEngineConfig.cs` itself is deleted in Task 12, by which point `DatabaseRegistry.cs` is its only home, so move the enum here as part of *this* task, not Task 12, to avoid a two-task window with the enum in two places).
- Consumes: `Walkabout.Data.SqlServerDatabase.GetConnectionString(string server, string database, string userid, string password)` (existing static method, `SqlDatabase.cs:222`) and `Walkabout.Utilities.ProcessHelper.AppDataPath` (existing static property).

- [ ] **Step 1: Write the failing round-trip test**

```csharp
// Source/WPF/UnitTests/DatabaseRegistryTests.cs
using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UnitTests
{
    public class DatabaseRegistryTests
    {
        private string tempFile;

        [SetUp]
        public void Setup()
        {
            this.tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(this.tempFile)) { File.Delete(this.tempFile); }
        }

        [Test]
        public void MissingFile_ReturnsEmptyRegistry()
        {
            var registry = DatabaseRegistry.Load(this.tempFile);
            Assert.That(registry.Servers, Is.Empty);
            Assert.That(registry.Databases, Is.Empty);
        }

        [Test]
        public void SaveThenLoad_RoundTripsServersAndDatabases()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Servers["Redmond"] = new DatabaseServerEntry
            {
                Comment = "Home NAS",
                MyMoneyAdmin = new DatabaseCredential { UserId = "MyMoneyAdmin", Password = "adminpw" },
                MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = "userpw" },
                MyMoneyTest = new DatabaseCredential { UserId = "MyMoneyTest", Password = "testpw" }
            };
            registry.Databases["My Real Money"] = new DatabaseEntry
            {
                Engine = DataEngineType.SqlServer, Server = "Redmond", Catalog = "MyMoney",
                TestDatabase = false, LastUsedUtc = new DateTime(2026, 9, 17, 20, 0, 0, DateTimeKind.Utc),
                Comment = "The real one"
            };
            registry.Databases["Personal.mmdb"] = new DatabaseEntry
            {
                Engine = DataEngineType.Sqlite, Path = @"C:\Users\x\MyMoney\personal.mmdb",
                TestDatabase = false, LastUsedUtc = null, Comment = null
            };
            registry.Save();

            var reloaded = DatabaseRegistry.Load(this.tempFile);

            Assert.That(reloaded.Servers["Redmond"].Comment, Is.EqualTo("Home NAS"));
            Assert.That(reloaded.Servers["Redmond"].MyMoneyAdmin.Password, Is.EqualTo("adminpw"));
            Assert.That(reloaded.Databases["My Real Money"].Server, Is.EqualTo("Redmond"));
            Assert.That(reloaded.Databases["My Real Money"].Catalog, Is.EqualTo("MyMoney"));
            Assert.That(reloaded.Databases["My Real Money"].TestDatabase, Is.False);
            Assert.That(reloaded.Databases["Personal.mmdb"].Path, Is.EqualTo(@"C:\Users\x\MyMoney\personal.mmdb"));
        }

        [Test]
        public void Save_WritesTestDatabaseFieldWithExactCasing()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Databases["x"] = new DatabaseEntry { Engine = DataEngineType.Sqlite, Path = "x.mmdb", TestDatabase = true };
            registry.Save();

            string json = File.ReadAllText(this.tempFile);
            Assert.That(json, Does.Contain("\"TestDatabase\": true"));
            Assert.That(json, Does.Not.Contain("\"testDatabase\""));
        }

        [Test]
        public void GetDefaultPath_EndsWithDataEngineConfigJson()
        {
            Assert.That(DatabaseRegistry.GetDefaultPath(), Does.EndWith(Path.Combine("MyMoney", "dataengine.config.json")));
        }

        [Test]
        public void BuildConnectionString_ForSqlServerEntry_UsesRequestedRoleCredential()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            registry.Servers["Redmond"] = new DatabaseServerEntry
            {
                MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = "secret" }
            };
            var entry = new DatabaseEntry { Engine = DataEngineType.SqlServer, Server = "Redmond", Catalog = "MyMoney" };

            string connectionString = registry.BuildConnectionString(entry, DatabaseRole.User);

            Assert.That(connectionString, Does.Contain("User ID=MyMoneyUser").IgnoreCase);
            Assert.That(connectionString, Does.Contain("Initial Catalog=MyMoney").IgnoreCase);
        }

        [Test]
        public void BuildConnectionString_UnknownServer_ThrowsWithClearMessage()
        {
            var registry = new DatabaseRegistry(this.tempFile);
            var entry = new DatabaseEntry { Engine = DataEngineType.SqlServer, Server = "Nowhere", Catalog = "MyMoney" };

            var ex = Assert.Throws<InvalidOperationException>(() => registry.BuildConnectionString(entry, DatabaseRole.User));
            Assert.That(ex.Message, Does.Contain("Nowhere"));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DatabaseRegistryTests"`
Expected: FAIL — `DatabaseRegistry`/`DatabaseServerEntry`/`DatabaseCredential`/`DatabaseEntry` don't exist yet.

- [ ] **Step 3: Implement `DatabaseRegistry`**

```csharp
// Source/WPF/MyMoney.Data/DatabaseRegistry.cs
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    // Moved here from the now-deleted DataEngineConfig.cs -- this is its
    // only home from this task onward.
    public enum DataEngineType
    {
        Sqlite,
        SqlServer
    }

    public enum DatabaseRole
    {
        Admin,
        User,
        Test
    }

    public class DatabaseCredential
    {
        public string UserId { get; set; }
        public string Password { get; set; }
    }

    public class DatabaseServerEntry
    {
        public string Comment { get; set; }
        public DatabaseCredential MyMoneyAdmin { get; set; }
        public DatabaseCredential MyMoneyUser { get; set; }
        public DatabaseCredential MyMoneyTest { get; set; }

        public DatabaseCredential GetCredential(DatabaseRole role)
        {
            switch (role)
            {
                case DatabaseRole.Admin: return this.MyMoneyAdmin;
                case DatabaseRole.User: return this.MyMoneyUser;
                case DatabaseRole.Test: return this.MyMoneyTest;
                default: throw new ArgumentOutOfRangeException(nameof(role));
            }
        }
    }

    public class DatabaseEntry
    {
        public DataEngineType Engine { get; set; }
        public string Server { get; set; }
        public string Catalog { get; set; }
        public string Path { get; set; }

        // Explicit override: everything else in this schema serializes
        // camelCase (see DatabaseRegistry.JsonSettings below), but the user
        // specifically asked for this one field to read "TestDatabase" in
        // the hand-edited config file, not "testDatabase".
        [JsonProperty("TestDatabase")]
        public bool TestDatabase { get; set; }

        public DateTime? LastUsedUtc { get; set; }
        public string Comment { get; set; }
    }

    public class DatabaseRegistry
    {
        private readonly string path;

        public Dictionary<string, DatabaseServerEntry> Servers { get; private set; } = new Dictionary<string, DatabaseServerEntry>();
        public Dictionary<string, DatabaseEntry> Databases { get; private set; } = new Dictionary<string, DatabaseEntry>();

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include
        };

        public DatabaseRegistry(string path)
        {
            this.path = path;
        }

        public static string GetDefaultPath()
        {
            return Path.Combine(ProcessHelper.AppDataPath, "dataengine.config.json");
        }

        public static DatabaseRegistry Load(string path)
        {
            var registry = new DatabaseRegistry(path);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return registry;
            }

            string json = File.ReadAllText(path);
            RawRegistry raw = JsonConvert.DeserializeObject<RawRegistry>(json, JsonSettings);
            if (raw == null)
            {
                return registry;
            }

            registry.Servers = raw.Servers ?? new Dictionary<string, DatabaseServerEntry>();
            registry.Databases = raw.Databases ?? new Dictionary<string, DatabaseEntry>();
            return registry;
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(this.path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var raw = new RawRegistry { Servers = this.Servers, Databases = this.Databases };
            string json = JsonConvert.SerializeObject(raw, JsonSettings);
            File.WriteAllText(this.path, json);
        }

        public string BuildConnectionString(DatabaseEntry entry, DatabaseRole role)
        {
            if (entry.Engine != DataEngineType.SqlServer)
            {
                throw new InvalidOperationException("BuildConnectionString only applies to SqlServer entries.");
            }

            if (!this.Servers.TryGetValue(entry.Server, out DatabaseServerEntry server))
            {
                throw new InvalidOperationException($"No servers[\"{entry.Server}\"] entry in the registry at {this.path}.");
            }

            DatabaseCredential credential = server.GetCredential(role);
            if (credential == null)
            {
                throw new InvalidOperationException($"servers[\"{entry.Server}\"] has no {role} credential in the registry at {this.path}.");
            }

            return SqlServerDatabase.GetConnectionString(entry.Server, entry.Catalog, credential.UserId, credential.Password);
        }

        private class RawRegistry
        {
            public Dictionary<string, DatabaseServerEntry> Servers { get; set; }
            public Dictionary<string, DatabaseEntry> Databases { get; set; }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DatabaseRegistryTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney.Data/DatabaseRegistry.cs Source/WPF/UnitTests/DatabaseRegistryTests.cs
git commit -m "feat: add DatabaseRegistry model with JSON round-trip (#32)"
```

---

## Task 2: `SqlServerConnectionFactory`

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlServerConnectionFactory.cs`
- Test: `Source/WPF/UnitTests/DatabaseRegistryTests.cs` (append)

**Interfaces:**
- Consumes: `DatabaseRegistry`, `DatabaseEntry`, `DatabaseRole` (Task 1); `Walkabout.Data.SqlServerStoredProcDatabase` (existing, `ConnectionStringOverride`/`DatabasePath`/`UiCallback` settable properties); `Walkabout.Data.IDataLayerUiCallback` (existing interface, implemented by `WpfDataLayerUiCallback`, already used identically in `DataEngineStartup.cs:119`).
- Produces: `static class SqlServerConnectionFactory { static SqlServerStoredProcDatabase Connect(DatabaseRegistry registry, string displayName, IDataLayerUiCallback uiCallback); }`. Used by Task 10 (MainWindow New/Open handlers) and Task 12 (`DataEngineStartup` rewrite) — both need to turn "a display name the user picked" into a ready-to-`Load()` database object without duplicating the `ConnectionStringOverride`/`DatabasePath` construction.

- [ ] **Step 1: Write the failing test**

```csharp
// append to DatabaseRegistryTests.cs
[Test]
public void SqlServerConnectionFactory_Connect_BuildsDatabaseWithOverrideAndSyntheticPath()
{
    var registry = new DatabaseRegistry(this.tempFile);
    registry.Servers["Redmond"] = new DatabaseServerEntry
    {
        MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = "secret" }
    };
    registry.Databases["My Real Money"] = new DatabaseEntry
    {
        Engine = DataEngineType.SqlServer, Server = "Redmond", Catalog = "MyMoney"
    };

    var database = SqlServerConnectionFactory.Connect(registry, "My Real Money", uiCallback: null);

    Assert.That(database.ConnectionStringOverride, Does.Contain("Initial Catalog=MyMoney").IgnoreCase);
    Assert.That(database.DatabasePath, Does.EndWith(Path.Combine("SqlServer", "MyMoney.sqlserver")));
}

[Test]
public void SqlServerConnectionFactory_Connect_UnknownDisplayName_Throws()
{
    var registry = new DatabaseRegistry(this.tempFile);
    Assert.Throws<InvalidOperationException>(() => SqlServerConnectionFactory.Connect(registry, "nope", null));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DatabaseRegistryTests"`
Expected: FAIL — `SqlServerConnectionFactory` doesn't exist.

- [ ] **Step 3: Implement**

```csharp
// Source/WPF/MyMoney.Data/SqlServerConnectionFactory.cs
using System;
using System.IO;
using Walkabout.Utilities;

namespace Walkabout.Data
{
    public static class SqlServerConnectionFactory
    {
        public static SqlServerStoredProcDatabase Connect(DatabaseRegistry registry, string displayName, IDataLayerUiCallback uiCallback)
        {
            if (!registry.Databases.TryGetValue(displayName, out DatabaseEntry entry) || entry.Engine != DataEngineType.SqlServer)
            {
                throw new InvalidOperationException($"No registered SQL Server database named '{displayName}'.");
            }

            string connectionString = registry.BuildConnectionString(entry, DatabaseRole.User);

            return new SqlServerStoredProcDatabase
            {
                ConnectionStringOverride = connectionString,
                // SqlServerStoredProcDatabase has no on-disk file, but
                // DatabasePath is read throughout MainWindow (OFX log path,
                // caption text, attachment/statement/report directories) --
                // see the identical comment in DataEngineStartup.cs this
                // replaces.
                DatabasePath = Path.Combine(ProcessHelper.AppDataPath, "SqlServer", entry.Catalog + ".sqlserver"),
                UiCallback = uiCallback
            };
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DatabaseRegistryTests"`
Expected: PASS (8 tests total).

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlServerConnectionFactory.cs Source/WPF/UnitTests/DatabaseRegistryTests.cs
git commit -m "feat: add SqlServerConnectionFactory (#32)"
```

---

## Task 3: Permanent stored procs `MyMoney_BootstrapServer` / `MyMoney_CreateCatalog`

**Files:**
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_BootstrapServer.sql`
- Create: `Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_CreateCatalog.sql`
- Delete: `Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/CreateDatabaseAndLogins.sql`

**Interfaces:**
- Produces: two permanent, idempotent `master`-scoped procs, called directly via ADO.NET `SqlCommand` from Task 4's `SqlServerBootstrapper` — no more `{{AdminPassword}}`-style text substitution, no create-then-drop temp proc.
- Design note carried over from the real `CreateDatabaseAndLogins.sql` (read in full during research): that single script today conflates three things — (a) server-level `CREATE LOGIN`/`ALTER LOGIN` for all three logins (genuinely once-per-server), (b) `CREATE DATABASE`, and (c) per-catalog `CREATE USER`×3 + `db_owner` grant for `MyMoneyAdmin`. (a) maps to `MyMoney_BootstrapServer`; **(b) and (c) are catalog-scoped and both belong in `MyMoney_CreateCatalog`**, not just the `CREATE DATABASE` half. Today's script also never grants `dbcreator` to `MyMoneyAdmin` — this WI adds that grant so `MyMoneyAdmin` can run `MyMoney_CreateCatalog` itself.

- [ ] **Step 1: Write `MyMoney_BootstrapServer.sql`**

```sql
-- Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_BootstrapServer.sql
--
-- Permanent, idempotent, callable as 'sa'. Creates/updates the three
-- server-level logins and grants MyMoneyAdmin the dbcreator server role so
-- it can run MyMoney_CreateCatalog itself afterward. Called once per
-- server, ever, by SqlServerBootstrapper.BootstrapServerIfNeeded -- never
-- dropped after use, unlike the temp-proc pattern it replaces.
USE master;
GO

CREATE OR ALTER PROCEDURE dbo.MyMoney_BootstrapServer
    @AdminPassword NVARCHAR(128),
    @UserPassword  NVARCHAR(128),
    @TestPassword  NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyAdmin')
    BEGIN
        SET @sql = N'CREATE LOGIN [MyMoneyAdmin] WITH PASSWORD = ' + QUOTENAME(@AdminPassword, N'''') + N', CHECK_POLICY = OFF;';
        EXEC (@sql);
    END
    ELSE
    BEGIN
        SET @sql = N'ALTER LOGIN [MyMoneyAdmin] WITH PASSWORD = ' + QUOTENAME(@AdminPassword, N'''') + N';';
        EXEC (@sql);
    END
    IF NOT EXISTS (SELECT 1 FROM sys.server_role_members rm
                   JOIN sys.server_principals r ON rm.role_principal_id = r.principal_id
                   JOIN sys.server_principals m ON rm.member_principal_id = m.principal_id
                   WHERE r.name = N'dbcreator' AND m.name = N'MyMoneyAdmin')
    BEGIN
        ALTER SERVER ROLE dbcreator ADD MEMBER [MyMoneyAdmin];
    END

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyUser')
    BEGIN
        SET @sql = N'CREATE LOGIN [MyMoneyUser] WITH PASSWORD = ' + QUOTENAME(@UserPassword, N'''') + N', CHECK_POLICY = OFF;';
        EXEC (@sql);
    END
    ELSE
    BEGIN
        SET @sql = N'ALTER LOGIN [MyMoneyUser] WITH PASSWORD = ' + QUOTENAME(@UserPassword, N'''') + N';';
        EXEC (@sql);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyTest')
    BEGIN
        SET @sql = N'CREATE LOGIN [MyMoneyTest] WITH PASSWORD = ' + QUOTENAME(@TestPassword, N'''') + N', CHECK_POLICY = OFF;';
        EXEC (@sql);
    END
    ELSE
    BEGIN
        SET @sql = N'ALTER LOGIN [MyMoneyTest] WITH PASSWORD = ' + QUOTENAME(@TestPassword, N'''') + N';';
        EXEC (@sql);
    END
END
GO
```

- [ ] **Step 2: Write `MyMoney_CreateCatalog.sql`**

```sql
-- Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_CreateCatalog.sql
--
-- Permanent, idempotent, callable as 'MyMoneyAdmin' (granted dbcreator by
-- MyMoney_BootstrapServer). Creates one catalog by name -- production, a
-- test catalog, or any future catalog, no per-catalog script variant
-- needed -- and creates all three logins as database users inside it with
-- MyMoneyAdmin as db_owner. QUOTENAME on @CatalogName specifically to
-- avoid SQL injection through the parameter, per issue #32's design spec.
USE master;
GO

CREATE OR ALTER PROCEDURE dbo.MyMoney_CreateCatalog
    @CatalogName NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @CatalogName)
    BEGIN
        SET @sql = N'CREATE DATABASE ' + QUOTENAME(@CatalogName) + N';';
        EXEC (@sql);
    END

    SET @sql = N'
        USE ' + QUOTENAME(@CatalogName) + N';
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyAdmin'')
            CREATE USER [MyMoneyAdmin] FOR LOGIN [MyMoneyAdmin];
        IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
                        JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
                        JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                        WHERE r.name = N''db_owner'' AND m.name = N''MyMoneyAdmin'')
            ALTER ROLE db_owner ADD MEMBER [MyMoneyAdmin];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyUser'')
            CREATE USER [MyMoneyUser] FOR LOGIN [MyMoneyUser];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyTest'')
            CREATE USER [MyMoneyTest] FOR LOGIN [MyMoneyTest];
    ';
    EXEC (@sql);
END
GO
```

- [ ] **Step 3: Delete the replaced script**

```bash
git rm Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/CreateDatabaseAndLogins.sql
```

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_BootstrapServer.sql Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_CreateCatalog.sql
git commit -m "feat: replace temp-proc SQL Server bootstrap script with two permanent procs (#32)"
```

(No automated test here — these are plain `.sql` files with no build-time verification; Task 13's live Redmond verification is what actually exercises them. Task 4's unit tests cover the C# orchestration around them.)

---

## Task 4: `SqlServerBootstrapper` (in-process, replaces `MyMoneyAdmin`'s `BootstrapRunner`)

**Files:**
- Create: `Source/WPF/MyMoney.Data/ISaCredentialPrompt.cs`
- Create: `Source/WPF/MyMoney.Data/SqlServerBootstrapper.cs`
- Test: `Source/WPF/UnitTests/SqlServerBootstrapperTests.cs`

**Interfaces:**
- Consumes: `DatabaseRegistry`/`DatabaseServerEntry`/`DatabaseCredential` (Task 1); `Walkabout.Data.DataEnginePasswordGenerator.Generate(int length = 24)` (existing, `DataEnginePasswordGenerator.cs`); `Walkabout.Data.SqlServerStoredProcDatabase.LazyCreateTables()`/`.Disconnect()` (existing, used identically by today's `BootstrapRunner.cs:99-104`).
- Produces: `interface ISaCredentialPrompt { bool TryGetSaPassword(string server, out string password); }` (implemented by Task 5's `SaCredentialDialog`, kept WPF-agnostic so this class has no WPF dependency); `class SqlServerBootstrapper { SqlServerBootstrapper(string sqlScriptsRoot); bool BootstrapServerIfNeeded(DatabaseRegistry registry, string server, ISaCredentialPrompt saPrompt, out string error); bool CreateCatalog(DatabaseRegistry registry, string server, string catalogName, bool testDatabase, out string error); }`. Task 6/10 call both methods; Task 12 (`DataEngineStartup`) does **not** call this class at all (auto-load never bootstraps — see Task 12).
- **Safety decision (not resolved by the spec text, made explicit here):** `CreateCatalog` deploys all 16 `Access/*.sql` files to every catalog it creates (test or production), but deploys `Test/*.sql` files **only when `testDatabase == true`**. Deploying `_Test_Reset` procs into a production catalog would hand the `MyMoneyTest` login the ability to destructively wipe real data — the three logins get a `CREATE USER` in every catalog either way (needed for basic connectivity, ported unchanged from today's script), but the destructive procs themselves stay confined to catalogs the caller explicitly flagged as test databases.

- [ ] **Step 1: Write the failing tests for the testable (non-DB) parts**

`ExecuteBatchScript`'s GO-splitting and `^USE\b`-skipping logic is pure string processing — factor it into an internal static method so it's unit-testable without a live SQL Server, matching the spec's explicit claim that the `USE` line becomes dead weight to skip.

```csharp
// Source/WPF/UnitTests/SqlServerBootstrapperTests.cs
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.UnitTests
{
    public class SqlServerBootstrapperTests
    {
        [Test]
        public void SplitBatches_SplitsOnGoAndTrims()
        {
            string script = "SELECT 1;\r\nGO\r\n  SELECT 2;  \r\nGO\r\n";
            var batches = SqlServerBootstrapper.SplitBatches(script);
            Assert.That(batches, Is.EqualTo(new[] { "SELECT 1;", "SELECT 2;" }));
        }

        [Test]
        public void SplitBatches_SkipsUseStatements()
        {
            string script = "USE MyMoney;\r\nGO\r\nSELECT 1;\r\nGO\r\n";
            var batches = SqlServerBootstrapper.SplitBatches(script);
            Assert.That(batches, Is.EqualTo(new[] { "SELECT 1;" }));
        }

        [Test]
        public void SplitBatches_IgnoresEmptyBatches()
        {
            string script = "GO\r\nSELECT 1;\r\nGO\r\n\r\nGO\r\n";
            var batches = SqlServerBootstrapper.SplitBatches(script);
            Assert.That(batches, Is.EqualTo(new[] { "SELECT 1;" }));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerBootstrapperTests"`
Expected: FAIL — `SqlServerBootstrapper` doesn't exist.

- [ ] **Step 3: Implement `ISaCredentialPrompt` and `SqlServerBootstrapper`**

```csharp
// Source/WPF/MyMoney.Data/ISaCredentialPrompt.cs
namespace Walkabout.Data
{
    /// <summary>
    /// Kept WPF-agnostic so SqlServerBootstrapper (MyMoney.Data, no WPF
    /// reference) can prompt for 'sa' without depending on the UI layer.
    /// Implemented by MyMoney's SaCredentialDialog.
    /// </summary>
    public interface ISaCredentialPrompt
    {
        /// <returns>false if the user cancelled.</returns>
        bool TryGetSaPassword(string server, out string password);
    }
}
```

```csharp
// Source/WPF/MyMoney.Data/SqlServerBootstrapper.cs
using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    public class SqlServerBootstrapper
    {
        private readonly string sqlScriptsRoot;

        private static readonly string[] AccessProcFiles =
        {
            "Payees_AccessProcs.sql", "Accounts_AccessProcs.sql", "Categories_AccessProcs.sql",
            "Currencies_AccessProcs.sql", "Securities_AccessProcs.sql", "StockSplits_AccessProcs.sql",
            "Aliases_AccessProcs.sql", "Transactions_AccessProcs.sql", "Splits_AccessProcs.sql",
            "Investments_AccessProcs.sql", "OnlineAccounts_AccessProcs.sql", "AccountAliases_AccessProcs.sql",
            "TransactionExtras_AccessProcs.sql", "LoanPayments_AccessProcs.sql", "RentUnits_AccessProcs.sql",
            "RentBuildings_AccessProcs.sql"
        };

        public SqlServerBootstrapper(string sqlScriptsRoot)
        {
            this.sqlScriptsRoot = sqlScriptsRoot;
        }

        public bool BootstrapServerIfNeeded(DatabaseRegistry registry, string server, ISaCredentialPrompt saPrompt, out string error)
        {
            error = null;
            if (registry.Servers.ContainsKey(server))
            {
                return true;
            }

            if (!saPrompt.TryGetSaPassword(server, out string saPassword))
            {
                error = "Bootstrap cancelled.";
                return false;
            }

            string adminPassword = DataEnginePasswordGenerator.Generate();
            string userPassword = DataEnginePasswordGenerator.Generate();
            string testPassword = DataEnginePasswordGenerator.Generate();

            var saBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = "sa",
                Password = saPassword,
                InitialCatalog = "master",
                TrustServerCertificate = true,
                ConnectTimeout = 10
            };

            try
            {
                DeployScript(saBuilder.ConnectionString, Path.Combine(this.sqlScriptsRoot, "Bootstrap", "MyMoney_BootstrapServer.sql"));
                using (var connection = new SqlConnection(saBuilder.ConnectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand("master.dbo.MyMoney_BootstrapServer", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                    {
                        command.Parameters.AddWithValue("@AdminPassword", adminPassword);
                        command.Parameters.AddWithValue("@UserPassword", userPassword);
                        command.Parameters.AddWithValue("@TestPassword", testPassword);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                error = $"Could not bootstrap server '{server}' as 'sa': {ex.Message}";
                return false;
            }

            // sa's password is never persisted (Global Constraints) -- only
            // the three generated role passwords go into the registry.
            registry.Servers[server] = new DatabaseServerEntry
            {
                MyMoneyAdmin = new DatabaseCredential { UserId = "MyMoneyAdmin", Password = adminPassword },
                MyMoneyUser = new DatabaseCredential { UserId = "MyMoneyUser", Password = userPassword },
                MyMoneyTest = new DatabaseCredential { UserId = "MyMoneyTest", Password = testPassword }
            };
            registry.Save();
            return true;
        }

        public bool CreateCatalog(DatabaseRegistry registry, string server, string catalogName, bool testDatabase, out string error)
        {
            error = null;
            if (!registry.Servers.TryGetValue(server, out DatabaseServerEntry serverEntry))
            {
                error = $"Server '{server}' has not been bootstrapped yet.";
                return false;
            }

            var adminBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = serverEntry.MyMoneyAdmin.UserId,
                Password = serverEntry.MyMoneyAdmin.Password,
                InitialCatalog = "master",
                TrustServerCertificate = true,
                ConnectTimeout = 10
            };

            try
            {
                DeployScript(adminBuilder.ConnectionString, Path.Combine(this.sqlScriptsRoot, "Bootstrap", "MyMoney_CreateCatalog.sql"));
                using (var connection = new SqlConnection(adminBuilder.ConnectionString))
                {
                    connection.Open();
                    using (var command = new SqlCommand("master.dbo.MyMoney_CreateCatalog", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                    {
                        command.Parameters.AddWithValue("@CatalogName", catalogName);
                        command.ExecuteNonQuery();
                    }
                }

                var catalogBuilder = new SqlConnectionStringBuilder(adminBuilder.ConnectionString) { InitialCatalog = catalogName };

                var adminDatabase = new SqlServerStoredProcDatabase { ConnectionStringOverride = catalogBuilder.ConnectionString };
                adminDatabase.LazyCreateTables();
                adminDatabase.Disconnect();

                foreach (string procFile in AccessProcFiles)
                {
                    string script = File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", procFile));
                    ExecuteBatchScript(catalogBuilder.ConnectionString, script);
                }

                if (testDatabase)
                {
                    // Test/_Test_Reset procs are destructive-wipe capable --
                    // only ever deployed into a catalog the caller flagged
                    // as a test database (see this task's Safety decision).
                    foreach (string testFile in Directory.GetFiles(Path.Combine(this.sqlScriptsRoot, "Test"), "*.sql"))
                    {
                        ExecuteBatchScript(catalogBuilder.ConnectionString, File.ReadAllText(testFile));
                    }
                }
            }
            catch (Exception ex)
            {
                error = $"Could not create catalog '{catalogName}' on '{server}': {ex.Message}";
                return false;
            }

            return true;
        }

        private static void DeployScript(string connectionString, string scriptPath)
        {
            ExecuteBatchScript(connectionString, File.ReadAllText(scriptPath));
        }

        /// <summary>
        /// Ported from MyMoneyAdmin's BootstrapRunner.ExecuteBatchScript,
        /// plus skipping any batch matching ^USE\b -- the connection is
        /// already scoped to the right catalog via InitialCatalog, so the
        /// checked-in scripts' leading "USE &lt;db&gt;;" line (kept for a human
        /// running them by hand in SSMS) is dead weight for us.
        /// </summary>
        private static void ExecuteBatchScript(string connectionString, string script)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (string batch in SplitBatches(script))
                {
                    using (var command = new SqlCommand(batch, connection) { CommandTimeout = 60 })
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        internal static string[] SplitBatches(string script)
        {
            string[] rawBatches = script.Split(new[] { "\r\nGO", "\nGO" }, StringSplitOptions.RemoveEmptyEntries);
            var batches = new System.Collections.Generic.List<string>();
            foreach (string raw in rawBatches)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0 || Regex.IsMatch(trimmed, @"^USE\b", RegexOptions.IgnoreCase))
                {
                    continue;
                }
                batches.Add(trimmed);
            }
            return batches.ToArray();
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerBootstrapperTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney.Data/ISaCredentialPrompt.cs Source/WPF/MyMoney.Data/SqlServerBootstrapper.cs Source/WPF/UnitTests/SqlServerBootstrapperTests.cs
git commit -m "feat: add in-process SqlServerBootstrapper, replacing MyMoneyAdmin's BootstrapRunner (#32)"
```

(Live-server behavior — actually calling `MyMoney_BootstrapServer`/`MyMoney_CreateCatalog` against Redmond, idempotency on a second run, a genuinely new second catalog — is verified in Task 13, not here; this task's tests cover only the DB-free string logic.)

---

## Task 5: `SaCredentialDialog` (implements `ISaCredentialPrompt`)

**Files:**
- Create: `Source/WPF/MyMoney/Dialogs/SaCredentialDialog.xaml`
- Create: `Source/WPF/MyMoney/Dialogs/SaCredentialDialog.xaml.cs`

**Interfaces:**
- Consumes: `Walkabout.Data.ISaCredentialPrompt` (Task 4).
- Produces: `class SaCredentialDialog : Window, ISaCredentialPrompt`. Used by Task 6/10's `OnNewSqlServerDatabase` handler as the `saPrompt` argument to `SqlServerBootstrapper.BootstrapServerIfNeeded`.

- [ ] **Step 1: Write the dialog XAML**

```xml
<!-- Source/WPF/MyMoney/Dialogs/SaCredentialDialog.xaml -->
<Window x:Class="Walkabout.Dialogs.SaCredentialDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Connect as 'sa'" Height="160" Width="360"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False">
    <StackPanel Margin="12">
        <TextBlock x:Name="TextBlockPrompt" TextWrapping="Wrap" Margin="0,0,0,8"/>
        <PasswordBox x:Name="TextBoxPassword" Margin="0,0,0,12"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="ButtonOk" Content="_OK" IsDefault="True" Width="75" Click="OnOk"/>
            <Button x:Name="ButtonCancel" Content="_Cancel" IsCancel="True" Width="75" Margin="8,0,0,0"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Write the code-behind**

```csharp
// Source/WPF/MyMoney/Dialogs/SaCredentialDialog.xaml.cs
using System.Windows;
using Walkabout.Data;

namespace Walkabout.Dialogs
{
    public partial class SaCredentialDialog : Window, ISaCredentialPrompt
    {
        public SaCredentialDialog()
        {
            this.InitializeComponent();
        }

        public bool TryGetSaPassword(string server, out string password)
        {
            this.TextBlockPrompt.Text = $"Enter the 'sa' password for server '{server}' to bootstrap it for MyMoney.";
            bool? result = this.ShowDialog();
            password = result == true ? this.TextBoxPassword.Password : null;
            return result == true;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: Build succeeds (this dialog has no automated test — it's a thin, directly-observable UI shell around an interface already covered by `SqlServerBootstrapperTests`' caller contract; Task 13 exercises it manually against live Redmond).

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/SaCredentialDialog.xaml Source/WPF/MyMoney/Dialogs/SaCredentialDialog.xaml.cs
git commit -m "feat: add SaCredentialDialog implementing ISaCredentialPrompt (#32)"
```

---

## Task 6: `NewSqlServerDatabaseDialog` (DEBUG-only)

**Files:**
- Create: `Source/WPF/MyMoney/Dialogs/NewSqlServerDatabaseDialog.xaml`
- Create: `Source/WPF/MyMoney/Dialogs/NewSqlServerDatabaseDialog.xaml.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks directly (a pure data-collection dialog); Task 10 reads its results.
- Produces: `class NewSqlServerDatabaseDialog : Window { string DisplayName; string ServerName; string CatalogName; bool TestDatabase; }`, all populated only after `ShowDialog() == true`.

- [ ] **Step 1: Write the dialog XAML**

```xml
<!-- Source/WPF/MyMoney/Dialogs/NewSqlServerDatabaseDialog.xaml -->
<Window x:Class="Walkabout.Dialogs.NewSqlServerDatabaseDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="New SQL Server Database" Height="260" Width="400"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False">
    <StackPanel Margin="12">
        <TextBlock Text="Display name:"/>
        <TextBox x:Name="TextBoxDisplayName" Margin="0,0,0,8"/>
        <TextBlock Text="Server:"/>
        <ComboBox x:Name="ComboBoxServer" IsEditable="True" Margin="0,0,0,8"/>
        <TextBlock Text="Catalog name:"/>
        <TextBox x:Name="TextBoxCatalog" Margin="0,0,0,8"/>
        <CheckBox x:Name="CheckBoxTestDatabase" Content="Test database:" Margin="0,0,0,12"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="ButtonOk" Content="_Create" IsDefault="True" Width="75" Click="OnCreate"/>
            <Button x:Name="ButtonCancel" Content="_Cancel" IsCancel="True" Width="75" Margin="8,0,0,0"/>
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Write the code-behind**

```csharp
// Source/WPF/MyMoney/Dialogs/NewSqlServerDatabaseDialog.xaml.cs
using System.Linq;
using System.Windows;
using Walkabout.Data;

namespace Walkabout.Dialogs
{
    public partial class NewSqlServerDatabaseDialog : Window
    {
        public string DisplayName { get; private set; }
        public string ServerName { get; private set; }
        public string CatalogName { get; private set; }
        public bool TestDatabase { get; private set; }

        public NewSqlServerDatabaseDialog(DatabaseRegistry registry)
        {
            this.InitializeComponent();
            foreach (string server in registry.Servers.Keys)
            {
                this.ComboBoxServer.Items.Add(server);
            }
        }

        private void OnCreate(object sender, RoutedEventArgs e)
        {
            this.CatalogName = this.TextBoxCatalog.Text.Trim();
            this.ServerName = (this.ComboBoxServer.Text ?? string.Empty).Trim();
            this.DisplayName = string.IsNullOrWhiteSpace(this.TextBoxDisplayName.Text)
                ? this.CatalogName
                : this.TextBoxDisplayName.Text.Trim();
            this.TestDatabase = this.CheckBoxTestDatabase.IsChecked == true;

            if (string.IsNullOrEmpty(this.ServerName) || string.IsNullOrEmpty(this.CatalogName) || string.IsNullOrEmpty(this.DisplayName))
            {
                MessageBoxEx.Show("Server, catalog name, and display name are all required.", "New SQL Server Database", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
        }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/NewSqlServerDatabaseDialog.xaml Source/WPF/MyMoney/Dialogs/NewSqlServerDatabaseDialog.xaml.cs
git commit -m "feat: add NewSqlServerDatabaseDialog (#32)"
```

---

## Task 7: `NewSqliteDatabaseDialog`

**Files:**
- Create: `Source/WPF/MyMoney/Dialogs/NewSqliteDatabaseDialog.xaml`
- Create: `Source/WPF/MyMoney/Dialogs/NewSqliteDatabaseDialog.xaml.cs`

**Interfaces:**
- Produces: `class NewSqliteDatabaseDialog : Window { string DisplayName; string FilePath; bool TestDatabase; }`. This is what Release's `File | New` opens directly, and what DEBUG's `File | New | SQLite...` opens — **same dialog, same class**, no engine-specific variant needed. The "Test database:" checkbox is compiled out in Release (`#if DEBUG` around the `CheckBox` element and its backing field), per the spec amendment.

- [ ] **Step 1: Write the dialog XAML**

```xml
<!-- Source/WPF/MyMoney/Dialogs/NewSqliteDatabaseDialog.xaml -->
<Window x:Class="Walkabout.Dialogs.NewSqliteDatabaseDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="New Database" Height="220" Width="420"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False">
    <StackPanel Margin="12">
        <TextBlock Text="Display name:"/>
        <TextBox x:Name="TextBoxDisplayName" Margin="0,0,0,8"/>
        <TextBlock Text="File location:"/>
        <DockPanel Margin="0,0,0,8">
            <Button x:Name="ButtonBrowse" Content="_Browse..." DockPanel.Dock="Right" Width="75" Click="OnBrowse"/>
            <TextBox x:Name="TextBoxFile" Margin="0,0,8,0"/>
        </DockPanel>
#if DEBUG
        <CheckBox x:Name="CheckBoxTestDatabase" Content="Test database:" Margin="0,0,0,12"/>
#endif
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="ButtonOk" Content="_Create" IsDefault="True" Width="75" Click="OnCreate"/>
            <Button x:Name="ButtonCancel" Content="_Cancel" IsCancel="True" Width="75" Margin="8,0,0,0"/>
        </StackPanel>
    </StackPanel>
</Window>
```

Note: WPF XAML does not actually support `#if` preprocessor directives (confirmed during research — the codebase's only `#if DEBUG` usage is in `.xaml.cs`, never `.xaml`). Replace the block above with an **always-present** `CheckBox x:Name="CheckBoxTestDatabase"` in the XAML, and instead **collapse its visibility from code-behind** under `#if DEBUG` in Step 2 below — this achieves the same "Release users never see or can check it" outcome; strict compiled-out-not-hidden only matters for the SQL Server dialog/menu item itself (Task 10), where an entire feature — not just one checkbox on an always-shipped dialog — must not exist in the Release binary.

```xml
        <CheckBox x:Name="CheckBoxTestDatabase" Content="Test database:" Margin="0,0,0,12"/>
```

- [ ] **Step 2: Write the code-behind**

```csharp
// Source/WPF/MyMoney/Dialogs/NewSqliteDatabaseDialog.xaml.cs
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Walkabout.Dialogs
{
    public partial class NewSqliteDatabaseDialog : Window
    {
        public string DisplayName { get; private set; }
        public string FilePath { get; private set; }
        public bool TestDatabase { get; private set; }

        public NewSqliteDatabaseDialog()
        {
            this.InitializeComponent();
#if !DEBUG
            this.CheckBoxTestDatabase.Visibility = Visibility.Collapsed;
#endif
        }

        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "MyMoney Database (*.mmdb)|*.mmdb", DefaultExt = ".mmdb" };
            if (dlg.ShowDialog(this) == true)
            {
                this.TextBoxFile.Text = dlg.FileName;
            }
        }

        private void OnCreate(object sender, RoutedEventArgs e)
        {
            this.FilePath = this.TextBoxFile.Text.Trim();
            this.DisplayName = string.IsNullOrWhiteSpace(this.TextBoxDisplayName.Text)
                ? Path.GetFileName(this.FilePath)
                : this.TextBoxDisplayName.Text.Trim();
#if DEBUG
            this.TestDatabase = this.CheckBoxTestDatabase.IsChecked == true;
#endif

            if (string.IsNullOrEmpty(this.FilePath) || string.IsNullOrEmpty(this.DisplayName))
            {
                MessageBoxEx.Show("File location and display name are both required.", "New Database", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
        }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: Build succeeds in both `dotnet build -c Debug` and `dotnet build -c Release` (the `#if DEBUG` around `this.TestDatabase = ...` must not leave `TestDatabase`'s auto-property unset in a way that breaks Release — it defaults to `false`, which is correct: Release can never produce `TestDatabase: true`).

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/NewSqliteDatabaseDialog.xaml Source/WPF/MyMoney/Dialogs/NewSqliteDatabaseDialog.xaml.cs
git commit -m "feat: add NewSqliteDatabaseDialog with DEBUG-only test-database checkbox (#32)"
```

---

## Task 8: `OpenDatabaseDialog`

**Files:**
- Create: `Source/WPF/MyMoney/Dialogs/OpenDatabaseDialog.xaml`
- Create: `Source/WPF/MyMoney/Dialogs/OpenDatabaseDialog.xaml.cs`

**Interfaces:**
- Consumes: `DatabaseRegistry`/`DatabaseEntry` (Task 1).
- Produces: `class OpenDatabaseDialog : Window { string SelectedDisplayName; }`, populated only after `ShowDialog() == true`.

- [ ] **Step 1: Write the dialog XAML**

```xml
<!-- Source/WPF/MyMoney/Dialogs/OpenDatabaseDialog.xaml -->
<Window x:Class="Walkabout.Dialogs.OpenDatabaseDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Open Database" Height="320" Width="420"
        WindowStartupLocation="CenterOwner">
    <DockPanel Margin="12">
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" DockPanel.Dock="Bottom" Margin="0,12,0,0">
            <Button x:Name="ButtonOk" Content="_Open" IsDefault="True" Width="75" Click="OnOpen"/>
            <Button x:Name="ButtonCancel" Content="_Cancel" IsCancel="True" Width="75" Margin="8,0,0,0"/>
        </StackPanel>
        <ListBox x:Name="ListBoxDatabases" DisplayMemberPath="Key" MouseDoubleClick="OnOpen"/>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Write the code-behind**

```csharp
// Source/WPF/MyMoney/Dialogs/OpenDatabaseDialog.xaml.cs
using System;
using System.Linq;
using System.Windows;
using Walkabout.Data;

namespace Walkabout.Dialogs
{
    public partial class OpenDatabaseDialog : Window
    {
        public string SelectedDisplayName { get; private set; }

        public OpenDatabaseDialog(DatabaseRegistry registry)
        {
            this.InitializeComponent();

            bool includeTest =
#if DEBUG
                true;
#else
                false;
#endif
            var entries = registry.Databases
                .Where(kv => includeTest || !kv.Value.TestDatabase)
                .OrderByDescending(kv => kv.Value.LastUsedUtc ?? DateTime.MinValue)
                .ToList();

            foreach (var kv in entries)
            {
                this.ListBoxDatabases.Items.Add(kv);
            }
        }

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            if (this.ListBoxDatabases.SelectedItem is System.Collections.Generic.KeyValuePair<string, DatabaseEntry> kv)
            {
                this.SelectedDisplayName = kv.Key;
                this.DialogResult = true;
            }
        }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoney/Dialogs/OpenDatabaseDialog.xaml Source/WPF/MyMoney/Dialogs/OpenDatabaseDialog.xaml.cs
git commit -m "feat: add OpenDatabaseDialog listing registered databases (#32)"
```

---

## Task 9: `RecentFilesMenu` rework (registry-backed)

**Files:**
- Modify: `Source/WPF/MyMoney/Utilities/RecentFilesMenu.cs`
- Modify: `Source/WPF/MyMoney/Utilities/Settings.cs:117-127` (remove `RecentFiles` property)

**Interfaces:**
- Consumes: `DatabaseRegistry`/`DatabaseEntry` (Task 1).
- Produces: `class RecentDatabaseEventArgs : EventArgs { string DisplayName; }`; `internal class RecentFilesMenu { RecentFilesMenu(MenuItem parent); void Refresh(DatabaseRegistry registry); event EventHandler<RecentDatabaseEventArgs> RecentDatabaseSelected; }` — **replaces** `SetFiles`/`AddRecentFile`/`ToArray`/`RecentFileSelected` entirely, since the registry (not a separately-persisted path list) is now the only source of truth. Task 10 is the sole consumer of the new API.

- [ ] **Step 1: Rewrite `RecentFilesMenu.cs`**

```csharp
// Source/WPF/MyMoney/Utilities/RecentFilesMenu.cs
using System;
using System.Linq;
using System.Windows.Controls;
using Walkabout.Data;

namespace Walkabout.Utilities
{
    public class RecentDatabaseEventArgs : EventArgs
    {
        public string DisplayName { get; set; }
    }

    /// <summary>
    /// A thin, pull-based view over DatabaseRegistry.Databases -- no
    /// separate storage of its own (replaces the old path-list/File.Exists
    /// pruning approach). Call Refresh(registry) after any registry
    /// mutation (a new database created, a database opened and its
    /// LastUsedUtc bumped) to keep the menu in sync.
    /// </summary>
    internal class RecentFilesMenu
    {
        private const int MaxRecentFiles = 10;
        private readonly MenuItem parent;

        public event EventHandler<RecentDatabaseEventArgs> RecentDatabaseSelected;

        public RecentFilesMenu(MenuItem parent)
        {
            this.parent = parent;
        }

        public void Refresh(DatabaseRegistry registry)
        {
            this.parent.Items.Clear();

            bool includeTest =
#if DEBUG
                true;
#else
                false;
#endif
            var entries = registry.Databases
                .Where(kv => includeTest || !kv.Value.TestDatabase)
                .OrderByDescending(kv => kv.Value.LastUsedUtc ?? DateTime.MinValue)
                .Take(MaxRecentFiles);

            foreach (var kv in entries)
            {
                string displayName = kv.Key;
                var item = new MenuItem { Header = displayName };
                item.Click += (s, e) => this.RecentDatabaseSelected?.Invoke(this, new RecentDatabaseEventArgs { DisplayName = displayName });
                this.parent.Items.Add(item);
            }
        }
    }
}
```

- [ ] **Step 2: Remove `Settings.RecentFiles`**

Delete this exact block from `Source/WPF/MyMoney/Utilities/Settings.cs:117-129` (confirmed by reading the file directly — sits between the `StatementsDirectory` and `ToolBoxWidth` properties):

```csharp
public string[] RecentFiles
{
    get
    {
        object value = this.map["RecentFiles"];
        return value is string[]? (string[])value : null;
    }
    set
    {
        this.map["RecentFiles"] = value;
        this.OnPropertyChanged("RecentFiles");
    }
}
```

Grep afterward to confirm nothing else references `settings.RecentFiles`/`Settings.RecentFiles` (Task 10 removes the two remaining call sites in `MainWindow.xaml.cs` as part of its own rewiring, so this should already be zero by the time Task 10 finishes — but Task 9 can land first since C# doesn't error on an unused-but-still-modified-elsewhere property mid-refactor; just don't delete `Settings.RecentFiles` until Task 10's `MainWindow.xaml.cs` edits are also in the same commit or a following one that leaves the build green).

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: Build fails at this point (expected — `MainWindow.xaml.cs` still calls the old `RecentFilesMenu` API and `settings.RecentFiles`); this is resolved in Task 10. **Do not commit Task 9 alone if it leaves the build red** — fold the `Settings.cs` edit into Task 10's commit instead, and land only `RecentFilesMenu.cs` itself here if it compiles standalone (it does — it has no callers yet to break).

- [ ] **Step 3: Commit `RecentFilesMenu.cs` alone (compiles standalone; `Settings.cs` edit moves to Task 10)**

```bash
git add Source/WPF/MyMoney/Utilities/RecentFilesMenu.cs
git commit -m "feat: rework RecentFilesMenu into a registry-backed view (#32)"
```

---

## Task 10: Wire up `MainWindow` — `File | New`, `File | Open`, Recent Files

**Files:**
- Modify: `Source/WPF/MyMoney/MainWindow.xaml:37-38,143-161`
- Modify: `Source/WPF/MyMoney/MainWindow.xaml.cs:265-267,1599,2061-2094,2140-2260+,3993-4000`
- Modify: `Source/WPF/MyMoney/Utilities/Settings.cs` (finish the `RecentFiles` removal started in Task 9)
- Delete: `Source/WPF/MyMoney/Dialogs/CreateDatabaseDialog.xaml`, `Source/WPF/MyMoney/Dialogs/CreateDatabaseDialog.xaml.cs` (only after Step 1 confirms no other callers)

**Interfaces:**
- Consumes: everything from Tasks 1–9 (`DatabaseRegistry`, `SqlServerConnectionFactory`, `SqlServerBootstrapper`, `SaCredentialDialog`, `NewSqlServerDatabaseDialog`, `NewSqliteDatabaseDialog`, `OpenDatabaseDialog`, `RecentFilesMenu.Refresh`/`RecentDatabaseSelected`).
- Produces: nothing further downstream — this is the integration point.

- [ ] **Step 1: Confirm `CreateDatabaseDialog` has no other callers before touching it**

Run: `git grep -n "CreateDatabaseDialog\|InitializeCreateDatabaseDialog\|ConnectMode" -- Source/WPF/MyMoney`
Expected: only `NewDatabase()`, `OpenDatabase()`, `InitializeCreateDatabaseDialog()` in `MainWindow.xaml.cs`, and the dialog's own two files. If anything else turns up, stop and re-scope this step before deleting.

- [ ] **Step 2: Rewrite the `File | New` XAML to a plain click target**

In `Source/WPF/MyMoney/MainWindow.xaml`, change:

```xml
<MenuItem x:Name="MenuFileNew" Header="_New" Command="New"/>
```

to:

```xml
<MenuItem x:Name="MenuFileNew" Header="_New" Click="OnMenuFileNewClick"/>
```

and remove the now-unused command binding at line 37:

```xml
<CommandBinding Command="New" Executed="OnCommandFileNew"/>
```

(Leave `Command="Open"`/`OnCommandFileOpen`'s `CommandBinding` alone — `File | Open` keeps using `ApplicationCommands.Open`, only its handler body changes in Step 5.)

- [ ] **Step 3: Add the DEBUG-only SQL Server submenu item in the constructor**

In `MainWindow.xaml.cs`, immediately after the existing `this.recentFilesMenu.RecentFileSelected += this.OnRecentFileSelected;` line (265-267), replace those three lines with:

```csharp
this.recentFilesMenu = new RecentFilesMenu(this.MenuRecentFiles);
this.recentFilesMenu.RecentDatabaseSelected += this.OnRecentDatabaseSelected;
this.recentFilesMenu.Refresh(DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath()));

#if DEBUG
            var newSqlServerItem = new MenuItem { Header = "_SQL Server..." };
            newSqlServerItem.Click += this.OnNewSqlServerDatabase;
            var newSqliteItem = new MenuItem { Header = "S_QLite..." };
            newSqliteItem.Click += this.OnNewSqliteDatabase;
            this.MenuFileNew.Items.Add(newSqlServerItem);
            this.MenuFileNew.Items.Add(newSqliteItem);
#endif
```

This is the mechanism that satisfies "compiled out entirely in Release, not merely hidden": in DEBUG, `MenuFileNew.Items` has two children, so WPF treats it as a submenu container and its own `Click` event (wired next step) never fires for the parent header itself — only the two children's handlers fire. In Release, `MenuFileNew.Items` stays empty, so WPF treats it as an ordinary leaf command item and the parent's own `Click` fires directly.

- [ ] **Step 4: Add the click handlers**

Replace `OnCommandFileNew` (line 3993) with:

```csharp
private void OnMenuFileNewClick(object sender, RoutedEventArgs e)
{
    // DEBUG builds never reach here for a click on MenuFileNew itself --
    // see the constructor comment above; it has children there, so WPF
    // opens the submenu instead of raising Click. Release builds have no
    // children, so this fires directly and goes straight to SQLite.
    this.OnNewSqliteDatabase(sender, e);
}

#if DEBUG
private void OnNewSqlServerDatabase(object sender, RoutedEventArgs e)
{
    if (!this.SaveIfDirty()) { return; }

    var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
    var dlg = new NewSqlServerDatabaseDialog(registry) { Owner = this };
    if (dlg.ShowDialog() != true) { return; }

    var bootstrapper = new SqlServerBootstrapper(Path.Combine(AppContext.BaseDirectory, "SqlScripts"));

    if (!registry.Servers.ContainsKey(dlg.ServerName))
    {
        var saPrompt = new SaCredentialDialog { Owner = this };
        if (!bootstrapper.BootstrapServerIfNeeded(registry, dlg.ServerName, saPrompt, out string bootstrapError))
        {
            MessageBoxEx.Show(bootstrapError, "Bootstrap Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
    }

    if (!bootstrapper.CreateCatalog(registry, dlg.ServerName, dlg.CatalogName, dlg.TestDatabase, out string catalogError))
    {
        MessageBoxEx.Show(catalogError, "Create Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
    }

    registry.Databases[dlg.DisplayName] = new DatabaseEntry
    {
        Engine = DataEngineType.SqlServer,
        Server = dlg.ServerName,
        Catalog = dlg.CatalogName,
        TestDatabase = dlg.TestDatabase,
        LastUsedUtc = DateTime.UtcNow
    };
    registry.Save();

    this.OpenRegisteredDatabase(dlg.DisplayName, registry);
}
#endif

private void OnNewSqliteDatabase(object sender, RoutedEventArgs e)
{
    if (!this.SaveIfDirty()) { return; }

    var dlg = new NewSqliteDatabaseDialog { Owner = this };
    if (dlg.ShowDialog() != true) { return; }

    this.canSave = false;
    try
    {
        this.LoadDatabase(null, dlg.FilePath, null, null, null);
    }
    catch (Exception ex)
    {
        this.log.Error("Error creating new database", ex);
        MessageBoxEx.Show(ex.Message, "Create Error", MessageBoxButton.OKCancel, MessageBoxImage.Error);
        return;
    }

    var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
    registry.Databases[dlg.DisplayName] = new DatabaseEntry
    {
        Engine = DataEngineType.Sqlite,
        Path = dlg.FilePath,
        TestDatabase = dlg.TestDatabase,
        LastUsedUtc = DateTime.UtcNow
    };
    registry.Save();
    this.recentFilesMenu.Refresh(registry);
}
```

Replace `OnCommandFileOpen` (line 3998) with:

```csharp
private void OnCommandFileOpen(object sender, ExecutedRoutedEventArgs e)
{
    if (!this.SaveIfDirty()) { return; }

    var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
    var dlg = new OpenDatabaseDialog(registry) { Owner = this };
    if (dlg.ShowDialog() == true)
    {
        this.OpenRegisteredDatabase(dlg.SelectedDisplayName, registry);
    }
}

private void OnRecentDatabaseSelected(object sender, RecentDatabaseEventArgs e)
{
    if (!this.SaveIfDirty()) { return; }
    this.OpenRegisteredDatabase(e.DisplayName, DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath()));
}

private void OpenRegisteredDatabase(string displayName, DatabaseRegistry registry)
{
    if (!registry.Databases.TryGetValue(displayName, out DatabaseEntry entry))
    {
        MessageBoxEx.Show($"'{displayName}' is no longer registered.", "Open Database", MessageBoxButton.OK, MessageBoxImage.Error);
        return;
    }

    if (entry.Engine == DataEngineType.Sqlite)
    {
        this.canSave = false;
        this.LoadDatabase(null, entry.Path, null, null, null);
    }
    else
    {
        var sqlServerDatabase = SqlServerConnectionFactory.Connect(registry, displayName, new WpfDataLayerUiCallback());
        MyMoney newMoney = sqlServerDatabase.Load(null);
        this.database = sqlServerDatabase;
        this.MenuFileAddUser.Visibility = sqlServerDatabase.SupportsUserLogin ? Visibility.Visible : Visibility.Collapsed;
        this.DataContext = newMoney;
        this.canSave = true;
    }

    entry.LastUsedUtc = DateTime.UtcNow;
    registry.Save();
    this.recentFilesMenu.Refresh(registry);
}

private void RegisterRecentDatabase(IDatabase database)
{
    // Only file-based engines land here (via the shared LoadDatabase(string,...)
    // continuation below); SQL Server loads go through OpenRegisteredDatabase/
    // OnNewSqlServerDatabase instead, which manage the registry entry themselves.
    string ext = Path.GetExtension(database.DatabasePath)?.ToLowerInvariant();
    if (ext != ".db" && ext != ".mmdb")
    {
        // Legacy XML/BinaryXML/SqlCe formats aren't modeled in DatabaseRegistry
        // (DataEngineType only has Sqlite/SqlServer) -- out of #32's scope.
        return;
    }

    var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
    string displayName = registry.Databases
        .Where(kv => kv.Value.Engine == DataEngineType.Sqlite
            && string.Equals(kv.Value.Path, database.DatabasePath, StringComparison.OrdinalIgnoreCase))
        .Select(kv => kv.Key)
        .FirstOrDefault() ?? Path.GetFileName(database.DatabasePath);

    bool existingTestFlag = registry.Databases.TryGetValue(displayName, out DatabaseEntry existing) && existing.TestDatabase;

    registry.Databases[displayName] = new DatabaseEntry
    {
        Engine = DataEngineType.Sqlite,
        Path = database.DatabasePath,
        TestDatabase = existingTestFlag,
        LastUsedUtc = DateTime.UtcNow
    };
    registry.Save();
    this.recentFilesMenu.Refresh(registry);
}
```

Replace the old `this.recentFilesMenu.AddRecentFile(database.DatabasePath);` (line 2088, inside the generic `LoadDatabase(string,...)` success continuation) with:

```csharp
this.RegisterRecentDatabase(database);
```

Delete `s.RecentFiles = this.recentFilesMenu.ToArray();` (line 1599) entirely — there is no longer a separate persisted list to save.

Now finish Task 9's `Settings.cs` edit: delete the `RecentFiles` property (`Settings.cs:117-127`) and its `OnPropertyChanged("RecentFiles")` call.

- [ ] **Step 5: Delete the dead `CreateDatabaseDialog`-based code and the dialog itself**

Delete `NewDatabase()` (2164-2197), `OpenDatabase()` (2248+, through its closing brace), and `InitializeCreateDatabaseDialog()` (2140-2144) from `MainWindow.xaml.cs` — all three are now unreferenced (confirmed by Step 1's grep once these edits land).

```bash
git rm Source/WPF/MyMoney/Dialogs/CreateDatabaseDialog.xaml Source/WPF/MyMoney/Dialogs/CreateDatabaseDialog.xaml.cs
```

- [ ] **Step 5b: Give `MyMoney.csproj` its own `SqlScripts` content-copy item**

`SqlServerBootstrapper` (Task 4) and this task's `OnNewSqlServerDatabase` read scripts from `Path.Combine(AppContext.BaseDirectory, "SqlScripts")` — i.e. next to `MyMoney.exe` itself. Today, nothing copies them there directly: `MyMoneyAdmin.csproj` copies `..\MyMoney.Data\SqlScripts\**\*.sql` into **its own** output directory (`None Include ... CopyToOutputDirectory=PreserveNewest`), and a separate Debug-only MSBuild target (`CopyMyMoneyAdminOutputToMyMoney`) then copies MyMoneyAdmin's *entire* build output — including that `SqlScripts` folder — into `MyMoney`'s bin folder. Task 12 deletes `MyMoneyAdmin.csproj` entirely, taking both mechanisms with it. `MyMoney.csproj` needs its own equivalent, gated to Debug only (SQL Server stays DEBUG-only per Global Constraints — Release must not ship these scripts at all). Add, in `Source/WPF/MyMoney/MyMoney.csproj`, right after the existing `ProjectReference` `ItemGroup` (after line 651):

```xml
  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <None Include="..\MyMoney.Data\SqlScripts\**\*.sql">
      <Link>SqlScripts\%(RecursiveDir)%(Filename)%(Extension)</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 6: Build and run the existing UnitTests suite**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: Build succeeds, both Debug and Release configurations (`dotnet build Source/WPF/MyMoney.sln -c Release` too — this is the step that actually proves the SQL Server submenu item is compiled out, not just untested).

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: All pre-existing tests still pass (no regressions from the `RecentFilesMenu`/`Settings` API changes — nothing else in `UnitTests` references either).

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney/MainWindow.xaml Source/WPF/MyMoney/MainWindow.xaml.cs Source/WPF/MyMoney/Utilities/Settings.cs Source/WPF/MyMoney/MyMoney.csproj
git commit -m "feat: wire File|New/File|Open/Recent Files to the database registry (#32)"
```

---

## Task 11: Migrate the SQL Server test harness off environment variables

**Files:**
- Modify: `Source/WPF/MyMoney.TestSupport/SqlServerTestDatabase.cs`
- Modify: `Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs`
- Modify: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs`
- Delete: `Source/WPF/MyMoney.TestSupport/MyMoneyTestConnectionResolver.cs`, `Source/WPF/UnitTests/MyMoneyTestConnectionResolverTests.cs`

**Interfaces:**
- Consumes: `DatabaseRegistry`/`DatabaseRole` (Task 1) in place of `DataEngineConfig`/`DataEngineCredentialStore`/env vars.
- Produces: `SqlServerTestDatabase` gains two new public members — `static string GetSkipReasonIfNotConfigured()` (returns `null` if a `TestDatabase: true` SQL Server entry is registered, otherwise an explanatory message for `Assert.Ignore`) and `static string GetUserConnectionString()` (throws `InvalidOperationException` if unconfigured — same fail-loud contract `WipeAllTables()` already has). `WipeAllTables()` keeps its existing signature and behavior, only its configuration *source* changes.

**⚠️ Important, discovered by reading the actual current files in full (not just the excerpts captured during initial research):** `SqlServerDatabaseContractTests`'s class doc comment is explicit — *"Deliberately does NOT skip/Ignore when SQL Server is unreachable... Do not add Assert.Ignore/skip logic to this fixture."* This is a real, documented prior design decision (a broken connection there is meant to be a loud regression signal, not a quiet skip), not an artifact of the env-var mechanism. **Do not unify it with `SqlServerStoredProcDatabaseTests`'s skip behavior** — keep it throwing, just change what it reads. Only `SqlServerStoredProcDatabaseTests` uses `Assert.Ignore`, exactly as today.

`MyMoneyTestConnectionResolver`'s entire purpose — reconciling two independent config sources (`dataengine.config.json` + an admin-connection env var) that were "supposed to agree but aren't guaranteed to" — is moot once there is only one source (the registry). Its `ValidateMatchesAdminConnection` has nothing left to validate against; its `BuildConnectionString(DataEngineConfig, DataEngineCredential)` is superseded by `DatabaseRegistry.BuildConnectionString(entry, role)`. Delete the class and its test file rather than rewriting them.

- [ ] **Step 1: Rewrite `SqlServerTestDatabase.cs`**

Keep `TablesToWipe` and `TestResetProcs` exactly as they are (unrelated to this change). Replace `AdminConnectionEnvVar`/`DestructiveWipeAckEnvVar` and the env-var-reading body of `WipeAllTables()` with:

```csharp
// Source/WPF/MyMoney.TestSupport/SqlServerTestDatabase.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Walkabout.Data;

namespace Walkabout.TestSupport
{
    public static class SqlServerTestDatabase
    {
        private static readonly string[] TablesToWipe =
        {
            "Splits", "Investments", "Transactions", "TransactionExtras", "StockSplits", "Aliases",
            "AccountAliases", "LoanPayments", "RentUnits", "RentBuildings",
            "Accounts", "OnlineAccounts", "Securities", "Currencies", "Categories", "Payees"
        };

        private static readonly Dictionary<string, string> TestResetProcs = new Dictionary<string, string>
        {
            ["Categories"] = "dbo.Categories_Test_Reset",
            ["Currencies"] = "dbo.Currencies_Test_Reset",
            ["OnlineAccounts"] = "dbo.OnlineAccounts_Test_Reset",
            ["Accounts"] = "dbo.Accounts_Test_Reset",
            ["Payees"] = "dbo.Payees_Test_Reset",
            ["Aliases"] = "dbo.Aliases_Test_Reset",
            ["Securities"] = "dbo.Securities_Test_Reset",
            ["StockSplits"] = "dbo.StockSplits_Test_Reset",
            ["LoanPayments"] = "dbo.LoanPayments_Test_Reset",
            ["RentBuildings"] = "dbo.RentBuildings_Test_Reset",
            ["Transactions"] = "dbo.Transactions_Test_Reset",
        };

        /// <summary>
        /// Null if a TestDatabase: true SQL Server entry is registered;
        /// otherwise an explanatory message for a caller's Assert.Ignore.
        /// </summary>
        public static string GetSkipReasonIfNotConfigured()
        {
            var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
            bool hasTestEntry = registry.Databases.Values.Any(e => e.Engine == DataEngineType.SqlServer && e.TestDatabase);
            return hasTestEntry
                ? null
                : "No SQL Server database in the registry is flagged TestDatabase: true -- register one via File | New | SQL Server... with \"Test database:\" checked.";
        }

        /// <summary>
        /// MyMoneyUser-tier connection string for the registered test
        /// database. Throws (does not return null) if none is configured --
        /// callers that want to skip instead must check
        /// GetSkipReasonIfNotConfigured() first, matching
        /// SqlServerStoredProcDatabaseTests' existing pattern.
        /// </summary>
        public static string GetUserConnectionString()
        {
            var (registry, entry) = GetTestEntryOrThrow();
            return registry.BuildConnectionString(entry, DatabaseRole.User);
        }

        /// <summary>
        /// Deletes every row from every table in the registered test
        /// database. Fails fast (does not skip) if no TestDatabase: true
        /// SQL Server entry is registered -- same explicit, unskippable
        /// safety posture the old env-var guard had, just sourced from the
        /// registry's TestDatabase flag instead of a separate ack variable.
        /// </summary>
        public static void WipeAllTables()
        {
            var (registry, entry) = GetTestEntryOrThrow();
            string adminConnectionString = registry.BuildConnectionString(entry, DatabaseRole.Admin);
            string testConnectionString = registry.BuildConnectionString(entry, DatabaseRole.Test);

            using (var adminConnection = new SqlConnection(adminConnectionString))
            using (var testConnection = new SqlConnection(testConnectionString))
            {
                adminConnection.Open();
                testConnection.Open();

                foreach (string table in TablesToWipe)
                {
                    if (TestResetProcs.TryGetValue(table, out string procName))
                    {
                        using (var command = new SqlCommand(procName, testConnection) { CommandType = CommandType.StoredProcedure })
                        {
                            command.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var command = new SqlCommand($"DELETE FROM dbo.[{table}];", adminConnection))
                        {
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        private static (DatabaseRegistry registry, DatabaseEntry entry) GetTestEntryOrThrow()
        {
            var registry = DatabaseRegistry.Load(DatabaseRegistry.GetDefaultPath());
            var entry = registry.Databases.Values.FirstOrDefault(e => e.Engine == DataEngineType.SqlServer && e.TestDatabase);
            if (entry == null)
            {
                throw new InvalidOperationException(
                    "No SQL Server database in the registry is flagged TestDatabase: true. Register one via File | New | SQL Server... with \"Test database:\" checked before running SQL-Server-backed tests.");
            }
            return (registry, entry);
        }
    }
}
```

- [ ] **Step 2: Delete `MyMoneyTestConnectionResolver` and its test**

```bash
git rm Source/WPF/MyMoney.TestSupport/MyMoneyTestConnectionResolver.cs Source/WPF/UnitTests/MyMoneyTestConnectionResolverTests.cs
```

- [ ] **Step 3: Update `SqlServerStoredProcDatabaseTests.cs` — keep its skip behavior, swap the source**

Replace the `EnvVarName` constant and every use of `Environment.GetEnvironmentVariable(EnvVarName)` (both in `[SetUp]` and the private `GetConnectionStringOrSkip()` helper every `[Test]` method calls) with:

```csharp
[SetUp]
public void SetUp()
{
    string skipReason = SqlServerTestDatabase.GetSkipReasonIfNotConfigured();
    if (skipReason != null)
    {
        Assert.Ignore(skipReason);
    }
    SqlServerTestDatabase.WipeAllTables();
}

private string GetConnectionStringOrSkip()
{
    string skipReason = SqlServerTestDatabase.GetSkipReasonIfNotConfigured();
    if (skipReason != null)
    {
        Assert.Ignore(skipReason);
    }
    return SqlServerTestDatabase.GetUserConnectionString();
}
```

Every individual `[Test]` method's own call to `GetConnectionStringOrSkip()` is unchanged — only its implementation moved.

- [ ] **Step 4: Update `SqlServerDatabaseContractTests.cs` — keep it fail-loud, swap the source**

```csharp
public override void SetUp()
{
    SqlServerTestDatabase.WipeAllTables(); // still throws if unconfigured -- unchanged fail-loud contract, see this task's note above
    base.SetUp();
}

protected override IDatabase CreateDatabase()
{
    string connectionString = SqlServerTestDatabase.GetUserConnectionString(); // throws if unconfigured, matching this fixture's documented "fails rather than skips" intent
    string catalogName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    return new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString, DatabasePath = catalogName };
}
```

Delete the now-unused `UserConnectionEnvVar` constant and the `using System;` import if nothing else in the file needs it. Update the class doc comment's "Requires three environment variables..." paragraph to instead say it requires a `TestDatabase: true` SQL Server entry in the registry — the *content* of the "do not add skip logic" rationale stays accurate and must stay in the comment; only the configuration-source description changes.

- [ ] **Step 5: Build and run without a live SQL Server available**

Run: `dotnet test Source/WPF/MyMoney.sln`
Expected: `SqlServerStoredProcDatabaseTests` reports `Ignored` with the new message (no `TestDatabase: true` entry exists yet in this dev environment). `SqlServerDatabaseContractTests` **fails** (not ignored) with `InvalidOperationException`'s message surfaced — this is the correct, unchanged fail-loud behavior for that fixture, not a regression. Full live-server re-verification (where both fixtures actually run and pass) is Task 13.

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoney.TestSupport/SqlServerTestDatabase.cs Source/WPF/MyMoney.TestSupport/SqlServerDatabaseContractTests.cs Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "refactor: migrate SQL Server test harness from env vars to DatabaseRegistry (#32)"
```

---

## Task 12: Retire `MyMoneyAdmin`, old config classes, and rewrite `DataEngineStartup`

**Files:**
- Delete: `Source/WPF/MyMoneyAdmin/` (entire directory and its `.csproj`)
- Modify: `Source/WPF/MyMoney.sln` (remove the `MyMoneyAdmin` project entry and its build-configuration lines)
- Delete: `Source/WPF/MyMoney.Data/DataEngineConfig.cs`, `Source/WPF/MyMoney.Data/DataEngineCredentialStore.cs`
- Delete: `Source/WPF/UnitTests/DataEngineConfigTests.cs`, `Source/WPF/UnitTests/DataEngineCredentialStoreTests.cs`
- Modify: `Source/WPF/MyMoney/Database/DataEngineStartup.cs`
- Modify: `Source/WPF/MyMoney/dataengine.config.template.json`

**Interfaces:**
- Consumes: `DatabaseRegistry`/`SqlServerConnectionFactory` (Tasks 1-2).
- Produces: nothing further downstream — this is the cleanup task.

- [ ] **Step 1: Confirm nothing outside the files already touched references the classes being deleted**

Run: `git grep -n "DataEngineConfig\|DataEngineCredentialStore\|MyMoneyAdmin" -- Source/WPF`
Expected: only `DataEngineStartup.cs` (rewritten in this task), the `MyMoneyAdmin` project itself (deleted in this task), the two `UnitTests` files (deleted in this task), and `MyMoney.sln`'s project-reference lines (edited in this task). If `DatabaseRegistry.cs` or anything from Tasks 1-11 shows up, something upstream wasn't fully migrated — stop and fix it there first, not here.

- [ ] **Step 2: Delete `MyMoneyAdmin` and remove it from the solution**

```bash
git rm -r Source/WPF/MyMoneyAdmin
```

Open `Source/WPF/MyMoney.sln` and remove the `Project("...") = "MyMoneyAdmin", "MyMoneyAdmin\MyMoneyAdmin.csproj", "{...}"` block plus every line referencing that project's GUID under `GlobalSection(ProjectConfigurationPlatforms)`. (Research confirmed no other project has a `ProjectReference` to `MyMoneyAdmin.csproj` and its own `CopyMyMoneyAdminOutputToMyMoney` MSBuild target dies with the file, so this is a clean removal with no dangling references elsewhere.)

- [ ] **Step 3: Delete the old config classes and their tests**

```bash
git rm Source/WPF/MyMoney.Data/DataEngineConfig.cs Source/WPF/MyMoney.Data/DataEngineCredentialStore.cs Source/WPF/UnitTests/DataEngineConfigTests.cs Source/WPF/UnitTests/DataEngineCredentialStoreTests.cs
```

(`DataEngineType` was already moved into `DatabaseRegistry.cs` in Task 1, so this delete does not remove that enum — only the two classes and their now-superseded tests.)

- [ ] **Step 4: Rewrite `DataEngineStartup.TryAutoLoad`**

Replace the whole `#if DEBUG` class body with a registry-driven version. Auto-load now means "reconnect to whichever SQL Server database was used most recently," not "bootstrap a new one" — bootstrapping is explicitly a `File | New` action (Task 10), never something that happens silently at app startup:

```csharp
// Source/WPF/MyMoney/Database/DataEngineStartup.cs
using System;
using System.Diagnostics;
using System.Linq;
using Walkabout.Utilities;

namespace Walkabout.Data
{
#if DEBUG
    public static class DataEngineStartup
    {
        /// <summary>
        /// Reconnects to whichever registered SQL Server database was used
        /// most recently, if any. Never bootstraps -- if no SQL Server
        /// entry exists yet in the registry, returns false and the caller
        /// falls through to its normal SQLite/File-menu-driven behavior.
        /// Bootstrapping only ever happens via File | New | SQL Server...
        /// (see MainWindow.OnNewSqlServerDatabase).
        /// </summary>
        public static bool TryAutoLoad(string registryPath, out IDatabase database, out MyMoney money, Action<string> log = null)
        {
            database = null;
            money = null;

            DatabaseRegistry registry;
            try
            {
                registry = DatabaseRegistry.Load(registryPath);
            }
            catch (Exception ex)
            {
                LogFailure(log, $"DataEngineStartup: could not read '{registryPath}', falling back to SQLite. {ex.Message}");
                return false;
            }

            var mostRecent = registry.Databases
                .Where(kv => kv.Value.Engine == DataEngineType.SqlServer)
                .OrderByDescending(kv => kv.Value.LastUsedUtc ?? DateTime.MinValue)
                .FirstOrDefault();

            if (mostRecent.Value == null)
            {
                return false;
            }

            try
            {
                var sqlServerDatabase = SqlServerConnectionFactory.Connect(registry, mostRecent.Key, new WpfDataLayerUiCallback());
                money = sqlServerDatabase.Load(null);
                database = sqlServerDatabase;
                return true;
            }
            catch (Exception ex)
            {
                // The server may be unreachable, MyMoneyUser's login may
                // have been revoked, or the database may have been dropped
                // since the registry was written -- fall through to
                // MainWindow's normal SQLite/File-menu-driven behavior
                // instead of crashing app startup.
                LogFailure(log, $"DataEngineStartup: {ex.GetType().FullName}: {ex.Message}");
                database = null;
                money = null;
                return false;
            }
        }

        private static void LogFailure(Action<string> log, string message)
        {
            Debug.WriteLine(message);
            log?.Invoke(message);
        }
    }
#endif
}
```

This drops `RequiredDatabaseName`, `DatabaseExists()`, and `RunMyMoneyAdmin()` entirely (no more hardcoded catalog-name validation, no more credential-file-key-existence check standing in for "is this bootstrapped," no more `Process.Start`).

- [ ] **Step 5: Update the call site in `MainWindow.xaml.cs`**

The constructor's `#if DEBUG` block (around line 297) currently builds `dataEngineConfigPath` from `"dataengine.config.json"` and calls `DataEngineStartup.TryAutoLoad(dataEngineConfigPath, ...)`. Change the variable name and source to match the registry (same physical file, same path — only the type reading it changed across Tasks 1-12):

```csharp
string registryPath = DatabaseRegistry.GetDefaultPath();
if (Walkabout.Data.DataEngineStartup.TryAutoLoad(registryPath, out Walkabout.Data.IDatabase autoDatabase, out MyMoney autoMoney, msg => this.log.Warning(msg)))
```

- [ ] **Step 6: Update `dataengine.config.template.json`**

```json
{
  "servers": {
    "ExampleServer": {
      "comment": "Delete this template block and replace with your own -- populated automatically the first time you use File | New | SQL Server...",
      "myMoneyAdmin": { "userId": "MyMoneyAdmin", "password": "" },
      "myMoneyUser": { "userId": "MyMoneyUser", "password": "" },
      "myMoneyTest": { "userId": "MyMoneyTest", "password": "" }
    }
  },
  "databases": {
    "Example Database": {
      "engine": "SqlServer",
      "server": "ExampleServer",
      "catalog": "MyMoney",
      "TestDatabase": false,
      "lastUsedUtc": null,
      "comment": null
    }
  }
}
```

- [ ] **Step 7: Full solution build and test, both configurations**

Run: `dotnet build Source/WPF/MyMoney.sln` and `dotnet build Source/WPF/MyMoney.sln -c Release`
Expected: both succeed with `MyMoneyAdmin` gone from the solution.

Run: `dotnet test Source/WPF/MyMoney.sln`
Expected: all non-`RequiresSqlServer` tests pass; SQL Server-dependent ones skip gracefully (no live server configured yet in this dev environment — expected until Task 13).

- [ ] **Step 8: Commit**

```bash
git add -A Source/WPF/MyMoneyAdmin Source/WPF/MyMoney.sln Source/WPF/MyMoney.Data/DataEngineConfig.cs Source/WPF/MyMoney.Data/DataEngineCredentialStore.cs Source/WPF/UnitTests/DataEngineConfigTests.cs Source/WPF/UnitTests/DataEngineCredentialStoreTests.cs Source/WPF/MyMoney/Database/DataEngineStartup.cs Source/WPF/MyMoney/MainWindow.xaml.cs Source/WPF/MyMoney/dataengine.config.template.json
git commit -m "refactor: retire MyMoneyAdmin.exe and old DataEngineConfig/CredentialStore; DataEngineStartup now registry-driven (#32)"
```

---

## Task 13: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Full local build and test suite**

Run: `dotnet build Source/WPF/MyMoney.sln` and `dotnet build Source/WPF/MyMoney.sln -c Release`
Expected: both succeed.

Run: `dotnet test Source/WPF/MyMoney.sln`
Expected: every non-`RequiresSqlServer` test passes; `RequiresSqlServer`-tagged tests skip with the new "no `TestDatabase: true` entry registered" message (no live server configured on this machine's registry file yet).

- [ ] **Step 2: Manually register a live Redmond test database and re-run the SQL Server suite**

Using a DEBUG build, `File | New | SQL Server...` against the real Redmond server: server name `Redmond`, catalog `MyMoneyTest`, display name `SQL Server Test`, **"Test database:" checked**. This exercises `MyMoney_BootstrapServer` (first-time, prompts for `sa`) and `MyMoney_CreateCatalog` end-to-end for the first time against a live server.

Run: `dotnet test Source/WPF/MyMoney.sln`
Expected: the previously-skipped SQL Server tests now run and pass — this is the direct re-verification of the 16 + 191 + 79 tests noted in the #27 handoff, now sourced from the registry instead of the three retired env vars.

- [ ] **Step 3: Idempotency and second-catalog verification**

Re-run `File | New | SQL Server...` a second time with the **same** server (`Redmond`) but a **different**, genuinely new catalog name (e.g. `MyMoneyTest2`), test database checked. Expected: no `sa` prompt this time (server already in registry), `MyMoney_BootstrapServer` and `MyMoney_CreateCatalog` both no-op/succeed idempotently, the new catalog is created and independently wipeable without touching `MyMoneyTest`.

- [ ] **Step 4: Release-build UI verification**

Build and run `Source/WPF/MyMoney` in Release configuration. Confirm: `File | New` has no submenu and opens `NewSqliteDatabaseDialog` directly with no "Test database:" checkbox visible; `File | Open` and the Recent Files menu never show the `TestDatabase: true` entries created in Steps 2-3.

- [ ] **Step 5: DEBUG-build UI verification**

Build and run in DEBUG configuration. Confirm: `File | New` shows the **SQL Server...** / **SQLite...** submenu; `File | Open` and Recent Files show every registered entry including the test ones; opening a database updates its position in Recent Files (most-recently-used ordering).

No commit for this task — it's verification of Tasks 1-12's already-committed work. If any step surfaces a bug, fix it as a small follow-up commit against the specific task's files, re-run the relevant step, and continue.
