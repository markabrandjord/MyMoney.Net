# SQL Server Dual-Engine Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a DEBUG build of the WPF app run against SQL Server (in addition to today's default SQLite), via a revived `MyMoneyAdmin` tool that bootstraps a tiered-trust SQL Server database (three logins, stored-procedure-only access) and a new JSON config file that selects the engine.

**Architecture:** A new console project (`MyMoneyAdmin`) performs one-time `sa`-driven bootstrap (create DB + three logins via a self-dropping `master`-db procedure, then deploy hand-authored schema/access/test stored procedures as the new `MyMoneyAdmin` login). The WPF app reads a new `dataengine.config.json` at startup (DEBUG builds only); when configured for SQL Server it auto-loads (invoking `MyMoneyAdmin` first if the database doesn't exist yet) using a new `SqlServerStoredProcDatabase` class that calls stored procedures instead of raw SQL. This plan implements the full mechanism end-to-end for one table (`Payees`) as a proving vertical slice — extending coverage to the rest of `Money.cs`'s tables is explicitly out of scope here (see Known Limitation below).
**Post-implementation correction (whole-branch review, see `final-review-fix-report.md`):** as originally implemented, `SqlServerStoredProcDatabase` only overrode `ReadPayees`/`UpdatePayees` and inherited the base class's `Load()` unchanged, which tried to DDL/read every table via raw SQL that `MyMoneyUser` has no grants on — so the app's actual startup auto-load path could never succeed; only the Payees-only integration test (which calls `ReadPayees`/`UpdatePayees` directly, bypassing `Load()`) exercised the working path. `SqlServerStoredProcDatabase` now also overrides `Load()` to read only `Payees`, so the auto-load path this paragraph describes is now actually reachable through the app's normal startup — confirmed by build/unit-test verification, but still not confirmed against a real SQL Server (see the fix report's "needs live-server confirmation" list).

**Tech Stack:** C# / .NET 10.0, `Microsoft.Data.SqlClient` 7.0.2, `Newtonsoft.Json` 13.0.4, NUnit 4.6.1 (existing project dependencies — no new packages required).

**Spec:** `docs/superpowers/specs/2026-09-14-sql-server-dual-engine-design.md`

## Global Constraints

- SQL Server support (config parsing of `"SqlServer"`, the `MyMoneyAdmin`-invocation path, `SqlServerStoredProcDatabase`) is reachable only in DEBUG builds. A Release build that encounters `"engine": "SqlServer"` in config silently falls back to SQLite and logs it.
- `sa` credentials **(revised 2026-09-14, mid-implementation, at the developer's explicit request — see spec's "`sa` Handling" section)**: stored in the same credentials file as the three app accounts, under the key `"sa"`. `MyMoneyAdmin` checks for a stored `"sa"` entry first and uses it without prompting; if absent, it prompts interactively and saves the result. This widens the credentials file's blast radius (it can now grant full SQL Server control, not just the three scoped accounts) — an accepted tradeoff for this dev-network prototype.
- The three account passwords (`MyMoneyAdmin`, `MyMoneyUser`, `MyMoneyTest`) are auto-generated, never typed by the developer, and stored only in `%USERPROFILE%\.secrets\MyMoney\dataengine.credentials.json` — outside the repo, gitignore is not relied upon.
- `SqliteDatabase` and all its current behavior are untouched by this plan. No file under `Database/SqliteDatabase.cs` is modified.
- All schema/access/test SQL is hand-authored `.sql` content checked into the repo — never generated at runtime from `Mapping.cs` reflection.
- `MyMoneyUser` gets `EXECUTE`-only grants on access procedures; it must never receive direct table grants.

## Known Limitation (by design)

This plan wires the mechanism through completely for the `Payees` table only. `SqlServerStoredProcDatabase` overrides `ReadPayees`/`UpdatePayees`; every other table (`Accounts`, `Transactions`, `Categories`, etc.) still falls back to the inherited `SqlServerDatabase` methods, which build raw SQL against tables `MyMoneyUser` has no grants on — those calls will fail with a permissions error if exercised. Extending coverage to the rest of `Money.cs` is explicit follow-on work, not part of this plan. The goal here is a fully working, provably correct bootstrap-through-CRUD path for one table, not a fully functional SQL-Server-backed app.

---

## Task 1: Data-engine JSON config model

**Files:**
- Create: `Source/WPF/MyMoney/Database/DataEngineConfig.cs`
- Test: `Source/WPF/UnitTests/DataEngineConfigTests.cs`

**Interfaces:**
- Produces: `enum DataEngineType { Sqlite, SqlServer }`; `class DataEngineConfig { DataEngineType Engine; string Server; string Database; static DataEngineConfig Load(string path); }`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class DataEngineConfigTests
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
            if (File.Exists(this.tempFile))
            {
                File.Delete(this.tempFile);
            }
        }

        [Test]
        public void MissingFile_DefaultsToSqlite()
        {
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.Sqlite));
        }

        [Test]
        public void SqlServerEngine_ParsesAllFields()
        {
            File.WriteAllText(this.tempFile, @"{ ""engine"": ""SqlServer"", ""server"": ""DEVSQL01"", ""database"": ""MyMoney"" }");
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.SqlServer));
            Assert.That(config.Server, Is.EqualTo("DEVSQL01"));
            Assert.That(config.Database, Is.EqualTo("MyMoney"));
        }

        [Test]
        public void UnrecognizedEngineValue_DefaultsToSqlite()
        {
            File.WriteAllText(this.tempFile, @"{ ""engine"": ""Postgres"" }");
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.Sqlite));
        }

        [Test]
        public void EngineKeyMissing_DefaultsToSqlite()
        {
            File.WriteAllText(this.tempFile, @"{ ""server"": ""DEVSQL01"" }");
            var config = DataEngineConfig.Load(this.tempFile);
            Assert.That(config.Engine, Is.EqualTo(DataEngineType.Sqlite));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DataEngineConfigTests"`
Expected: FAIL (compile error — `DataEngineConfig` doesn't exist yet)

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.IO;
using Newtonsoft.Json;

namespace Walkabout.Data
{
    public enum DataEngineType
    {
        Sqlite,
        SqlServer
    }

    public class DataEngineConfig
    {
        public DataEngineType Engine { get; set; } = DataEngineType.Sqlite;
        public string Server { get; set; }
        public string Database { get; set; }

        public static DataEngineConfig Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new DataEngineConfig();
            }

            string json = File.ReadAllText(path);
            RawDataEngineConfig raw = JsonConvert.DeserializeObject<RawDataEngineConfig>(json);
            if (raw == null)
            {
                return new DataEngineConfig();
            }

            var config = new DataEngineConfig
            {
                Server = raw.Server,
                Database = raw.Database,
                Engine = string.Equals(raw.Engine, "SqlServer", StringComparison.OrdinalIgnoreCase)
                    ? DataEngineType.SqlServer
                    : DataEngineType.Sqlite
            };

            return config;
        }

        private class RawDataEngineConfig
        {
            public string Engine { get; set; }
            public string Server { get; set; }
            public string Database { get; set; }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DataEngineConfigTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney/Database/DataEngineConfig.cs Source/WPF/UnitTests/DataEngineConfigTests.cs
git commit -m "Add DataEngineConfig for SQLite/SQL Server engine selection"
```

---

## Task 2: Data-engine credential store

**Files:**
- Create: `Source/WPF/MyMoney/Database/DataEngineCredentialStore.cs`
- Test: `Source/WPF/UnitTests/DataEngineCredentialStoreTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `class DataEngineCredential { string UserId; string Password; }`; `class DataEngineCredentialStore { DataEngineCredentialStore(string path); static string GetDefaultPath(); Dictionary<string, DataEngineCredential> Load(); void Save(Dictionary<string, DataEngineCredential> credentials); DataEngineCredential GetCredential(string accountName); }`. `GetDefaultPath()` and `GetCredential` are relied on by Task 9 (MyMoneyAdmin) and Task 10 (WPF integration).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class DataEngineCredentialStoreTests
    {
        private string tempPath;

        [SetUp]
        public void Setup()
        {
            this.tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "dataengine.credentials.json");
        }

        [TearDown]
        public void TearDown()
        {
            string dir = Path.GetDirectoryName(this.tempPath);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Load_MissingFile_ReturnsEmptyDictionary()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            var result = store.Load();
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SaveThenLoad_RoundTripsCredentials()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            var credentials = new Dictionary<string, DataEngineCredential>
            {
                ["MyMoneyUser"] = new DataEngineCredential { UserId = "MyMoneyUser", Password = "Sw0rdfish!23" }
            };

            store.Save(credentials);
            var loaded = store.Load();

            Assert.That(loaded.ContainsKey("MyMoneyUser"), Is.True);
            Assert.That(loaded["MyMoneyUser"].Password, Is.EqualTo("Sw0rdfish!23"));
        }

        [Test]
        public void Save_CreatesParentDirectoryIfMissing()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            store.Save(new Dictionary<string, DataEngineCredential>());
            Assert.That(File.Exists(this.tempPath), Is.True);
        }

        [Test]
        public void GetCredential_UnknownAccount_ThrowsWithGuidance()
        {
            var store = new DataEngineCredentialStore(this.tempPath);
            store.Save(new Dictionary<string, DataEngineCredential>());
            var ex = Assert.Throws<InvalidOperationException>(() => store.GetCredential("MyMoneyTest"));
            Assert.That(ex.Message, Does.Contain("MyMoneyTest"));
        }

        [Test]
        public void GetDefaultPath_EndsWithExpectedRelativeLayout()
        {
            string path = DataEngineCredentialStore.GetDefaultPath();
            Assert.That(path, Does.EndWith(Path.Combine(".secrets", "MyMoney", "dataengine.credentials.json")));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DataEngineCredentialStoreTests"`
Expected: FAIL (compile error)

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Walkabout.Data
{
    public class DataEngineCredential
    {
        public string UserId { get; set; }
        public string Password { get; set; }
    }

    public class DataEngineCredentialStore
    {
        private readonly string path;

        public DataEngineCredentialStore(string path)
        {
            this.path = path;
        }

        public static string GetDefaultPath()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, ".secrets", "MyMoney", "dataengine.credentials.json");
        }

        public Dictionary<string, DataEngineCredential> Load()
        {
            if (!File.Exists(this.path))
            {
                return new Dictionary<string, DataEngineCredential>();
            }

            string json = File.ReadAllText(this.path);
            var result = JsonConvert.DeserializeObject<Dictionary<string, DataEngineCredential>>(json);
            return result ?? new Dictionary<string, DataEngineCredential>();
        }

        public void Save(Dictionary<string, DataEngineCredential> credentials)
        {
            string dir = Path.GetDirectoryName(this.path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonConvert.SerializeObject(credentials, Formatting.Indented);
            File.WriteAllText(this.path, json);
        }

        public DataEngineCredential GetCredential(string accountName)
        {
            var all = this.Load();
            if (all.TryGetValue(accountName, out DataEngineCredential credential))
            {
                return credential;
            }

            throw new InvalidOperationException(
                $"No credential found for account '{accountName}' in {this.path}. Run MyMoneyAdmin to bootstrap the SQL Server database first.");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DataEngineCredentialStoreTests"`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney/Database/DataEngineCredentialStore.cs Source/WPF/UnitTests/DataEngineCredentialStoreTests.cs
git commit -m "Add DataEngineCredentialStore for the out-of-repo SQL Server credential file"
```

---

## Task 3: Password generator

**Files:**
- Create: `Source/WPF/MyMoney/Database/DataEnginePasswordGenerator.cs`
- Test: `Source/WPF/UnitTests/DataEnginePasswordGeneratorTests.cs`

**Interfaces:**
- Produces: `static class DataEnginePasswordGenerator { static string Generate(int length = 24); }` — relied on by Task 9 (MyMoneyAdmin bootstrap).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class DataEnginePasswordGeneratorTests
    {
        [Test]
        public void Generate_DefaultLength_Is24Characters()
        {
            string password = DataEnginePasswordGenerator.Generate();
            Assert.That(password.Length, Is.EqualTo(24));
        }

        [Test]
        public void Generate_ContainsAtLeastOneOfEachRequiredClass()
        {
            string password = DataEnginePasswordGenerator.Generate();
            Assert.That(password.Any(char.IsUpper), Is.True);
            Assert.That(password.Any(char.IsLower), Is.True);
            Assert.That(password.Any(char.IsDigit), Is.True);
            Assert.That(password.Any(c => "!@#$%^&*-_=+".Contains(c)), Is.True);
        }

        [Test]
        public void Generate_TwoCallsProduceDifferentPasswords()
        {
            string a = DataEnginePasswordGenerator.Generate();
            string b = DataEnginePasswordGenerator.Generate();
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Generate_LengthBelowMinimum_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => DataEnginePasswordGenerator.Generate(4));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DataEnginePasswordGeneratorTests"`
Expected: FAIL (compile error)

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Security.Cryptography;

namespace Walkabout.Data
{
    public static class DataEnginePasswordGenerator
    {
        private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Lowercase = "abcdefghijkmnpqrstuvwxyz";
        private const string Digits = "23456789";
        private const string Symbols = "!@#$%^&*-_=+";

        public static string Generate(int length = 24)
        {
            if (length < 8)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Password length must be at least 8 characters.");
            }

            string allChars = Uppercase + Lowercase + Digits + Symbols;
            char[] result = new char[length];

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                result[0] = PickRandomChar(rng, Uppercase);
                result[1] = PickRandomChar(rng, Lowercase);
                result[2] = PickRandomChar(rng, Digits);
                result[3] = PickRandomChar(rng, Symbols);

                for (int i = 4; i < length; i++)
                {
                    result[i] = PickRandomChar(rng, allChars);
                }

                Shuffle(rng, result);
            }

            return new string(result);
        }

        private static char PickRandomChar(RandomNumberGenerator rng, string charset)
        {
            byte[] buffer = new byte[4];
            rng.GetBytes(buffer);
            uint value = BitConverter.ToUInt32(buffer, 0);
            int index = (int)(value % (uint)charset.Length);
            return charset[index];
        }

        private static void Shuffle(RandomNumberGenerator rng, char[] chars)
        {
            for (int i = chars.Length - 1; i > 0; i--)
            {
                byte[] buffer = new byte[4];
                rng.GetBytes(buffer);
                uint value = BitConverter.ToUInt32(buffer, 0);
                int j = (int)(value % (uint)(i + 1));
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~DataEnginePasswordGeneratorTests"`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney/Database/DataEnginePasswordGenerator.cs Source/WPF/UnitTests/DataEnginePasswordGeneratorTests.cs
git commit -m "Add DataEnginePasswordGenerator for auto-generated SQL login passwords"
```

---

## Task 4: Retry/Cancel connection-attempt loop

**Files:**
- Create: `Source/WPF/MyMoneyAdmin/RetryLoop.cs`
- Test: `Source/WPF/UnitTests/RetryLoopTests.cs`

**Interfaces:**
- Produces: `static class RetryLoop { enum PromptResult { Retry, Cancel } static bool Run(Func<bool> tryAction, Func<PromptResult> promptOnFailure); }` — relied on by Task 8 (`SaBootstrapConnection`).

This is the control-flow logic behind "on any `sa` connection failure, ask Retry/Cancel" (per the corrected design — no attempt to classify *why* the connection failed). It's decoupled from real SQL/console I/O via injected delegates so it's fully unit-testable.

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class RetryLoopTests
    {
        [Test]
        public void FirstAttemptSucceeds_ReturnsTrueWithoutPrompting()
        {
            bool prompted = false;
            bool result = RetryLoop.Run(
                tryAction: () => true,
                promptOnFailure: () => { prompted = true; return RetryLoop.PromptResult.Cancel; });

            Assert.That(result, Is.True);
            Assert.That(prompted, Is.False);
        }

        [Test]
        public void FailsThenRetrySucceeds_ReturnsTrue()
        {
            int attempts = 0;
            bool result = RetryLoop.Run(
                tryAction: () => { attempts++; return attempts >= 2; },
                promptOnFailure: () => RetryLoop.PromptResult.Retry);

            Assert.That(result, Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void FailsThenCancel_ReturnsFalseAndStopsRetrying()
        {
            int attempts = 0;
            bool result = RetryLoop.Run(
                tryAction: () => { attempts++; return false; },
                promptOnFailure: () => RetryLoop.PromptResult.Cancel);

            Assert.That(result, Is.False);
            Assert.That(attempts, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~RetryLoopTests"`
Expected: FAIL (compile error — `RetryLoop` doesn't exist, and the `UnitTests` project doesn't yet reference `MyMoneyAdmin`)

- [ ] **Step 3: Create the MyMoneyAdmin project and add the UnitTests reference**

Create `Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Walkabout.Data</RootNamespace>
    <AssemblyName>MyMoneyAdmin</AssemblyName>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.2" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyMoney\MyMoney.csproj" />
  </ItemGroup>

</Project>
```

Add `MyMoneyAdmin` to the solution and reference it from `UnitTests.csproj`:

```bash
dotnet sln Source/WPF/MyMoney.sln add Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj
dotnet add Source/WPF/UnitTests/UnitTests.csproj reference Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj
```

- [ ] **Step 4: Write the implementation**

```csharp
using System;

namespace Walkabout.Data
{
    public static class RetryLoop
    {
        public enum PromptResult
        {
            Retry,
            Cancel
        }

        public static bool Run(Func<bool> tryAction, Func<PromptResult> promptOnFailure)
        {
            while (true)
            {
                if (tryAction())
                {
                    return true;
                }

                if (promptOnFailure() == PromptResult.Cancel)
                {
                    return false;
                }
            }
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~RetryLoopTests"`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj Source/WPF/MyMoneyAdmin/RetryLoop.cs \
  Source/WPF/UnitTests/RetryLoopTests.cs Source/WPF/UnitTests/UnitTests.csproj Source/WPF/MyMoney.sln
git commit -m "Add MyMoneyAdmin project and RetryLoop connection-retry control flow"
```

---

## Task 5: Bootstrap SQL script (master-db temp procedure)

**Files:**
- Create: `Source/WPF/MyMoney/Database/SqlScripts/Bootstrap/CreateDatabaseAndLogins.sql`

**Interfaces:**
- Produces: a `.sql` file with a single T-SQL batch, read as plain text and executed by Task 9's `BootstrapRunner`. No C# interface — this is data, not code.

This script is run by `sa` against `master`. It creates a temporary stored procedure, executes it (creating the `MyMoney` database and the three logins with caller-supplied passwords), then drops the procedure. There is no automated test for this file — verifying it requires a real SQL Server instance (see Manual Verification below), which matches the spec's premise that these scripts are meant to be run and inspected directly, e.g. in SSMS.

- [ ] **Step 1: Write the script**

```sql
-- CreateDatabaseAndLogins.sql
-- Run as 'sa' against the 'master' database.
-- Creates the MyMoney database and the three tiered SQL logins, then
-- removes itself. Passwords are substituted by MyMoneyAdmin before this
-- script is sent to the server (see BootstrapRunner.cs) -- the
-- placeholders below are never executed literally.

USE master;
GO

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.MyMoney_Bootstrap') AND type = N'P')
    DROP PROCEDURE dbo.MyMoney_Bootstrap;
GO

CREATE PROCEDURE dbo.MyMoney_Bootstrap
    @AdminPassword NVARCHAR(128),
    @UserPassword NVARCHAR(128),
    @TestPassword NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'MyMoney')
    BEGIN
        -- Note: some SQL Server versions restrict CREATE DATABASE to being
        -- the only statement in its batch. If this procedure fails on your
        -- target server with that error, run this CREATE DATABASE statement
        -- ad hoc first, then re-run the rest of this script with the
        -- database-creation block commented out.
        CREATE DATABASE MyMoney;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyAdmin')
        CREATE LOGIN MyMoneyAdmin WITH PASSWORD = @AdminPassword, CHECK_POLICY = ON;
    ELSE
        ALTER LOGIN MyMoneyAdmin WITH PASSWORD = @AdminPassword;

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyUser')
        CREATE LOGIN MyMoneyUser WITH PASSWORD = @UserPassword, CHECK_POLICY = ON;
    ELSE
        ALTER LOGIN MyMoneyUser WITH PASSWORD = @UserPassword;

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyTest')
        CREATE LOGIN MyMoneyTest WITH PASSWORD = @TestPassword, CHECK_POLICY = ON;
    ELSE
        ALTER LOGIN MyMoneyTest WITH PASSWORD = @TestPassword;
END
GO

EXEC dbo.MyMoney_Bootstrap
    @AdminPassword = N'{{AdminPassword}}',
    @UserPassword  = N'{{UserPassword}}',
    @TestPassword  = N'{{TestPassword}}';
GO

DROP PROCEDURE dbo.MyMoney_Bootstrap;
GO
```

- [ ] **Step 2: Manual verification (no automated test — requires a real SQL Server)**

Against a scratch/dev SQL Server instance where you've temporarily enabled `sa`:
1. Replace the three `{{...Password}}` placeholders with real strong password literals.
2. Run the script in SSMS connected as `sa`.
3. Confirm: the `MyMoney` database exists (`SELECT name FROM sys.databases WHERE name = 'MyMoney'`), the three logins exist (`SELECT name FROM sys.server_principals WHERE name IN ('MyMoneyAdmin','MyMoneyUser','MyMoneyTest')`), and `dbo.MyMoney_Bootstrap` no longer exists in `master` (`SELECT * FROM sys.objects WHERE name = 'MyMoney_Bootstrap'` returns nothing).
4. Re-run the whole script again to confirm it's idempotent (no errors on a second run).

- [ ] **Step 3: Commit**

```bash
git add Source/WPF/MyMoney/Database/SqlScripts/Bootstrap/CreateDatabaseAndLogins.sql
git commit -m "Add master-db bootstrap script (creates MyMoney database and three logins)"
```

---

## Task 6: Payees schema, access, and test SQL scripts

**Files:**
- Create: `Source/WPF/MyMoney/Database/SqlScripts/Schema/001_CreatePayeesTable.sql`
- Create: `Source/WPF/MyMoney/Database/SqlScripts/Access/Payees_AccessProcs.sql`
- Create: `Source/WPF/MyMoney/Database/SqlScripts/Test/Payees_TestProcs.sql`

**Interfaces:**
- Produces: three `.sql` files, run by Task 9's `BootstrapRunner` as the `MyMoneyAdmin` login against the `MyMoney` database. Column layout (`Id INT PRIMARY KEY`, `Name NVARCHAR(255) NOT NULL`) matches the existing `Payee` class's `[ColumnMapping]` attributes in `Database/Money.cs:5180-5188`, so this schema is identical to what SQLite already stores for this table.

- [ ] **Step 1: Write the schema script**

```sql
-- 001_CreatePayeesTable.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.

USE MyMoney;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Payees')
BEGIN
    CREATE TABLE dbo.Payees (
        [Id]   INT           NOT NULL PRIMARY KEY,
        [Name] NVARCHAR(255) NOT NULL
    );
END
GO
```

- [ ] **Step 2: Write the access procedures (granted to `MyMoneyUser`)**

```sql
-- Payees_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser is permitted to perform
-- on the Payees table -- it has no direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Payees_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name FROM dbo.Payees ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_Insert
    @Id INT,
    @Name NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Payees (Id, Name) VALUES (@Id, @Name);
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_Update
    @Id INT,
    @Name NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Payees SET Name = @Name WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Payees WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Payees_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_Delete TO MyMoneyUser;
GO
```

- [ ] **Step 3: Write the test-support procedure (granted to `MyMoneyTest` only)**

```sql
-- Payees_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all Payees rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Payees_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Payees;
END
GO

GRANT EXECUTE ON dbo.Payees_Test_Reset TO MyMoneyTest;
GO
```

- [ ] **Step 4: Manual verification (no automated test — requires a real SQL Server)**

Against the bootstrapped dev database, connected as `MyMoneyAdmin`:
1. Run all three scripts in order.
2. Connect as `MyMoneyUser` and confirm `EXEC dbo.Payees_Insert @Id=1, @Name=N'Test Payee'` succeeds, `EXEC dbo.Payees_SelectAll` returns it, and a direct `SELECT * FROM dbo.Payees` **fails** with a permissions error (proving no table-level grant leaked through).
3. Connect as `MyMoneyUser` and confirm `EXEC dbo.Payees_Test_Reset` **fails** with a permissions error (proving the test-only proc isn't reachable by the normal app account).
4. Connect as `MyMoneyTest` and confirm `EXEC dbo.Payees_Test_Reset` succeeds.

- [ ] **Step 5: Commit**

```bash
git add Source/WPF/MyMoney/Database/SqlScripts/Schema/001_CreatePayeesTable.sql \
  Source/WPF/MyMoney/Database/SqlScripts/Access/Payees_AccessProcs.sql \
  Source/WPF/MyMoney/Database/SqlScripts/Test/Payees_TestProcs.sql
git commit -m "Add Payees schema, access, and test stored procedures"
```

---

## Task 7: `SqlServerStoredProcDatabase`

**Files:**
- Modify: `Source/WPF/MyMoney/Database/SqlDatabase.cs:1343` (`ReadPayees`) and `:1451` (`UpdatePayees`) — add `virtual`
- Create: `Source/WPF/MyMoney/Database/SqlServerStoredProcDatabase.cs`
- Test: `Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs`

**Interfaces:**
- Consumes: `DataEngineCredentialStore.GetCredential` (Task 2) is not used here directly — this class takes a plain connection string, matching `SqlServerDatabase`'s existing constructor-less/property-based pattern.
- Produces: `class SqlServerStoredProcDatabase : SqlServerDatabase` overriding `ReadPayees(Payees, MyMoney)` and `UpdatePayees(Payees)`. Relied on by Task 10 (WPF integration).

`SqliteDatabase` also derives from `SqlServerDatabase` (`Database/SqliteDatabase.cs:14`) and does not override these two methods, so it is completely unaffected by making them `virtual` — a virtual method with no override behaves identically to a non-virtual one for existing callers.

- [ ] **Step 1: Mark the base methods `virtual`**

In `Source/WPF/MyMoney/Database/SqlDatabase.cs`, change:

```csharp
        public void ReadPayees(Payees payees, MyMoney money)
```
to:
```csharp
        public virtual void ReadPayees(Payees payees, MyMoney money)
```

and change:

```csharp
        public void UpdatePayees(Payees payees)
```
to:
```csharp
        public virtual void UpdatePayees(Payees payees)
```

- [ ] **Step 2: Write the failing test**

This is an integration test — it requires a real, already-bootstrapped (Tasks 5 & 6 run manually) dev SQL Server instance, since there is no mocking layer for `Microsoft.Data.SqlClient` in this codebase (introducing one is item #3's job, out of scope here). It's skipped automatically unless a connection string is supplied via environment variable, so it never breaks a normal `dotnet test` run.

```csharp
using System;
using NUnit.Framework;
using Walkabout.Data;

namespace Walkabout.Tests
{
    public class SqlServerStoredProcDatabaseTests
    {
        private const string EnvVarName = "MYMONEY_TEST_SQLSERVER_USER_CONNECTION";

        private string GetConnectionStringOrSkip()
        {
            string connectionString = Environment.GetEnvironmentVariable(EnvVarName);
            if (string.IsNullOrEmpty(connectionString))
            {
                Assert.Ignore($"Set {EnvVarName} to a MyMoneyUser connection string to run this test against a real SQL Server.");
            }
            return connectionString;
        }

        [Test]
        public void InsertUpdateDeletePayee_RoundTripsThroughStoredProcedures()
        {
            string connectionString = this.GetConnectionStringOrSkip();
            var db = new SqlServerStoredProcDatabase { ConnectionStringOverride = connectionString };

            var money = new MyMoney();
            var payee = money.Payees.AddPayee(999001);
            payee.Name = "SqlServerStoredProcDatabaseTests Payee";
            payee.OnInserted();

            db.UpdatePayees(money.Payees);

            var reloaded = new MyMoney();
            db.ReadPayees(reloaded.Payees, reloaded);
            var found = reloaded.Payees.FindPayee("SqlServerStoredProcDatabaseTests Payee", false);
            Assert.That(found, Is.Not.Null);
            Assert.That(found.Id, Is.EqualTo(999001));

            found.OnDelete();
            var toDelete = new Payees(reloaded);
            toDelete.Add(found);
            db.UpdatePayees(toDelete);

            var afterDelete = new MyMoney();
            db.ReadPayees(afterDelete.Payees, afterDelete);
            Assert.That(afterDelete.Payees.FindPayee("SqlServerStoredProcDatabaseTests Payee", false), Is.Null);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it's skipped (no env var set) or fails (compile error)**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerStoredProcDatabaseTests"`
Expected: FAIL (compile error — `SqlServerStoredProcDatabase` and `ConnectionStringOverride` don't exist yet)

- [ ] **Step 4: Write the implementation**

```csharp
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    /// <summary>
    /// A SqlServerDatabase variant that performs Payees CRUD exclusively
    /// through stored procedures, matching the grants given to the
    /// MyMoneyUser login (see Database/SqlScripts/Access/Payees_AccessProcs.sql).
    /// Other tables are not yet overridden -- see the "Known Limitation"
    /// section of the plan that introduced this class.
    /// </summary>
    public class SqlServerStoredProcDatabase : SqlServerDatabase
    {
        /// <summary>
        /// Sole reason this exists: tests need to point at an ad hoc dev
        /// connection string without going through DataEngineConfig/
        /// DataEngineCredentialStore. Production callers (Task 10) leave
        /// this null and rely on the inherited Server/DatabasePath/UserId/
        /// Password properties instead.
        /// </summary>
        public string ConnectionStringOverride { get; set; }

        protected override string GetConnectionString(bool includeDatabase)
        {
            if (!string.IsNullOrEmpty(this.ConnectionStringOverride))
            {
                return this.ConnectionStringOverride;
            }
            return base.GetConnectionString(includeDatabase);
        }

        public override void ReadPayees(Payees payees, MyMoney money)
        {
            payees.Clear();
            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                using (var command = new SqlCommand("dbo.Payees_SelectAll", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                using (var reader = command.ExecuteReader())
                {
                    payees.BeginUpdate(false);
                    while (reader.Read())
                    {
                        int id = reader.GetInt32(0);
                        Payee p = payees.AddPayee(id);
                        p.Name = reader.IsDBNull(1) ? null : reader.GetString(1);
                        p.OnUpdated();
                    }
                    payees.EndUpdate();
                }
            }
            payees.FireChangeEvent(payees, payees, null, ChangeType.Reloaded);
        }

        public override void UpdatePayees(Payees payees)
        {
            if (payees.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(this.GetConnectionString(true)))
            {
                connection.Open();
                foreach (Payee p in payees)
                {
                    if (p.IsChanged)
                    {
                        ExecutePayeeProc(connection, "dbo.Payees_Update", p);
                    }
                    else if (p.IsInserted)
                    {
                        ExecutePayeeProc(connection, "dbo.Payees_Insert", p);
                    }
                    else if (p.IsDeleted)
                    {
                        using (var command = new SqlCommand("dbo.Payees_Delete", connection) { CommandType = System.Data.CommandType.StoredProcedure })
                        {
                            command.Parameters.AddWithValue("@Id", p.Id);
                            command.ExecuteNonQuery();
                        }
                    }
                }
            }

            List<Payee> toRemove = new List<Payee>();
            foreach (Payee p in payees)
            {
                if (p.IsDeleted)
                {
                    toRemove.Add(p);
                }
                else
                {
                    p.OnUpdated();
                }
            }
            foreach (Payee p in toRemove)
            {
                p.Parent.RemoveChild(p);
            }
        }

        private static void ExecutePayeeProc(SqlConnection connection, string procName, Payee p)
        {
            using (var command = new SqlCommand(procName, connection) { CommandType = System.Data.CommandType.StoredProcedure })
            {
                command.Parameters.AddWithValue("@Id", p.Id);
                command.Parameters.AddWithValue("@Name", (object)p.Name ?? System.DBNull.Value);
                command.ExecuteNonQuery();
            }
        }
    }
}
```

- [ ] **Step 5: Run test**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj --filter "FullyQualifiedName~SqlServerStoredProcDatabaseTests"`
Expected: PASS if `MYMONEY_TEST_SQLSERVER_USER_CONNECTION` is set to a real `MyMoneyUser` connection string against a bootstrapped dev database (Tasks 5 & 6 completed manually first); otherwise SKIPPED, not FAILED.

- [ ] **Step 6: Run the full existing unit test suite to confirm no regression**

Run: `dotnet test Source/WPF/UnitTests/UnitTests.csproj`
Expected: All previously-passing tests (including `SqlMappingTests`, which exercises `SqliteDatabase`) still PASS — confirms the `virtual` change didn't alter `SqliteDatabase`'s behavior.

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney/Database/SqlDatabase.cs Source/WPF/MyMoney/Database/SqlServerStoredProcDatabase.cs \
  Source/WPF/UnitTests/SqlServerStoredProcDatabaseTests.cs
git commit -m "Add SqlServerStoredProcDatabase overriding Payees CRUD to call stored procedures"
```

---

## Task 8: `sa` bootstrap connection

**Revised 2026-09-14 (mid-implementation):** originally `sa`'s password was
never persisted. At the developer's explicit request (see the spec's `sa`
Handling section for the accepted tradeoff), it's now checked in the
credentials file first and saved there after a successful interactive
prompt, the same as the three app accounts.

**Files:**
- Create: `Source/WPF/MyMoneyAdmin/SaBootstrapConnection.cs`

**Interfaces:**
- Consumes: `RetryLoop.Run` (Task 4); `DataEngineCredentialStore`, `DataEngineCredential` (Task 2) — reads/writes an entry keyed `"sa"` alongside the three app accounts.
- Produces: `class SaBootstrapConnection { static bool TryConnect(string server, out string password); }` — relied on by Task 9's `BootstrapRunner`.

This wraps the actual `sa` connection attempt (real ADO.NET, so not unit-tested — covered by Task 4's `RetryLoop` unit tests plus manual verification here) with credential-file reuse, the console-based password prompt, and the Retry/Cancel loop.

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    public static class SaBootstrapConnection
    {
        private const string SaAccountName = "sa";

        /// <summary>
        /// Connects as 'sa'. Checks the credentials file for a previously
        /// saved 'sa' password first and uses it without prompting if it
        /// still works; otherwise prompts interactively (Retry/Cancel on
        /// failure per RetryLoop) and, on success, saves the password to
        /// the credentials file so later runs on this machine don't
        /// re-prompt. See the spec's "sa Handling" section for why this
        /// account is persisted here, unlike the rest of this codebase's
        /// general practice for admin credentials.
        /// </summary>
        public static bool TryConnect(string server, out string password)
        {
            var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            var stored = credentialStore.Load();

            if (stored.TryGetValue(SaAccountName, out DataEngineCredential saved) && TryOpenConnection(server, saved.Password))
            {
                password = saved.Password;
                return true;
            }

            string capturedPassword = null;
            bool result = RetryLoop.Run(
                tryAction: () =>
                {
                    Console.Write("Enter 'sa' password: ");
                    capturedPassword = ReadPasswordFromConsole();
                    return TryOpenConnection(server, capturedPassword);
                },
                promptOnFailure: () =>
                {
                    Console.Write("Retry connecting as 'sa'? [R]etry / [C]ancel: ");
                    string input = Console.ReadLine();
                    return string.Equals(input?.Trim(), "R", StringComparison.OrdinalIgnoreCase)
                        ? RetryLoop.PromptResult.Retry
                        : RetryLoop.PromptResult.Cancel;
                });

            if (result)
            {
                stored[SaAccountName] = new DataEngineCredential { UserId = SaAccountName, Password = capturedPassword };
                credentialStore.Save(stored);
            }

            password = result ? capturedPassword : null;
            return result;
        }

        private static bool TryOpenConnection(string server, string password)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = SaAccountName,
                Password = password,
                InitialCatalog = "master",
                ConnectTimeout = 10
            };

            try
            {
                using (var connection = new SqlConnection(builder.ConnectionString))
                {
                    connection.Open();
                }
                return true;
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"Could not connect as 'sa': {ex.Message}");
                Console.WriteLine("Verify the 'sa' account is enabled on the server and the password is correct.");
                return false;
            }
        }

        private static string ReadPasswordFromConsole()
        {
            var input = new System.Text.StringBuilder();
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && input.Length > 0)
                {
                    input.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    input.Append(key.KeyChar);
                }
            }
            Console.WriteLine();
            return input.ToString();
        }
    }
}
```

- [ ] **Step 2: Manual verification (no automated test — requires a real SQL Server and a real terminal)**

1. With `sa` disabled on a dev instance and no `"sa"` entry in the credentials file, run a small throwaway program calling `SaBootstrapConnection.TryConnect("<your-server>", out _)`, enter any password, confirm it prints the failure message and prompts Retry/Cancel, and that typing `C` returns `false` without looping further.
2. Enable `sa`, set a known password, repeat: confirm entering that password returns `true`, and that the credentials file now has a `"sa"` entry with that password.
3. Confirm the password you typed is masked (not echoed) in the console.
4. Run it again without changing anything: confirm it connects successfully with **no password prompt at all** (reused from the credentials file).
5. Change the actual `sa` password on the server (so the stored one is now stale) and run once more: confirm it falls through to the interactive prompt rather than looping forever on the stale stored password.
6. Disable `sa` again on the server (its normal resting state — see spec's `sa` Handling section) with the credentials file still holding a stored `sa` entry from a previous run: confirm the stored-password attempt fails (same as any other failure — disabled and wrong-password are indistinguishable to the client) and correctly falls through to the interactive Retry/Cancel prompt rather than getting stuck assuming the stored credential must still be valid.

- [ ] **Step 3: Commit**

```bash
git add Source/WPF/MyMoneyAdmin/SaBootstrapConnection.cs
git commit -m "Add SaBootstrapConnection: interactive sa connection with Retry/Cancel"
```

---

## Task 9: `BootstrapRunner` and `Program.cs`

**Files:**
- Create: `Source/WPF/MyMoneyAdmin/BootstrapRunner.cs`
- Create: `Source/WPF/MyMoneyAdmin/Program.cs`

**Interfaces:**
- Consumes: `SaBootstrapConnection.TryConnect` (Task 8), `DataEnginePasswordGenerator.Generate` (Task 3), `DataEngineCredentialStore` (Task 2), the three `.sql` files (Tasks 5 & 6).
- Produces: `class BootstrapRunner { bool Run(string server, string databaseName); }`, invoked by `Program.Main` and by Task 10's WPF integration (via `Process.Start`, not a direct C# call, so no cross-assembly dependency from `MyMoney.csproj` back to `MyMoneyAdmin.csproj`).

This orchestrates the full sequence from the spec: connect as `sa` → run the bootstrap script (substituting generated passwords) → write the credentials file → reconnect as `MyMoneyAdmin` → run the schema/access/test scripts.

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
    public class BootstrapRunner
    {
        private readonly string sqlScriptsRoot;

        public BootstrapRunner(string sqlScriptsRoot)
        {
            this.sqlScriptsRoot = sqlScriptsRoot;
        }

        public bool Run(string server, string databaseName)
        {
            if (!SaBootstrapConnection.TryConnect(server, out string saPassword))
            {
                Console.WriteLine("Bootstrap cancelled.");
                return false;
            }

            string adminPassword = DataEnginePasswordGenerator.Generate();
            string userPassword = DataEnginePasswordGenerator.Generate();
            string testPassword = DataEnginePasswordGenerator.Generate();

            string bootstrapScript = File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Bootstrap", "CreateDatabaseAndLogins.sql"))
                .Replace("{{AdminPassword}}", adminPassword.Replace("'", "''"))
                .Replace("{{UserPassword}}", userPassword.Replace("'", "''"))
                .Replace("{{TestPassword}}", testPassword.Replace("'", "''"));

            var saBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = "sa",
                Password = saPassword,
                InitialCatalog = "master",
                ConnectTimeout = 10
            };

            Console.WriteLine("Creating database and logins as 'sa'...");
            ExecuteBatchScript(saBuilder.ConnectionString, bootstrapScript);

            var credentials = new Dictionary<string, DataEngineCredential>
            {
                ["MyMoneyAdmin"] = new DataEngineCredential { UserId = "MyMoneyAdmin", Password = adminPassword },
                ["MyMoneyUser"] = new DataEngineCredential { UserId = "MyMoneyUser", Password = userPassword },
                ["MyMoneyTest"] = new DataEngineCredential { UserId = "MyMoneyTest", Password = testPassword }
            };
            var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            credentialStore.Save(credentials);
            Console.WriteLine($"Wrote generated credentials to {DataEngineCredentialStore.GetDefaultPath()}");

            var adminBuilder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                UserID = "MyMoneyAdmin",
                Password = adminPassword,
                InitialCatalog = databaseName,
                ConnectTimeout = 10
            };

            Console.WriteLine("Deploying schema and stored procedures as 'MyMoneyAdmin'...");
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Schema", "001_CreatePayeesTable.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Access", "Payees_AccessProcs.sql")));
            ExecuteBatchScript(adminBuilder.ConnectionString, File.ReadAllText(Path.Combine(this.sqlScriptsRoot, "Test", "Payees_TestProcs.sql")));

            Console.WriteLine("Bootstrap complete.");
            return true;
        }

        /// <summary>
        /// Splits a script on GO batch separators (a plain-text convention,
        /// not parsed T-SQL) and executes each batch in turn -- ADO.NET has
        /// no native concept of GO, it's an SSMS/sqlcmd-only directive.
        /// </summary>
        private static void ExecuteBatchScript(string connectionString, string script)
        {
            string[] batches = script.Split(new[] { "\nGO", "\r\nGO" }, StringSplitOptions.RemoveEmptyEntries);
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                foreach (string batch in batches)
                {
                    string trimmed = batch.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }
                    using (var command = new SqlCommand(trimmed, connection) { CommandTimeout = 60 })
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
```

```csharp
using System;
using System.IO;

namespace Walkabout.Data
{
    public class Program
    {
        private static int Main(string[] args)
        {
            string server = args.Length > 0 ? args[0] : "localhost";
            string databaseName = args.Length > 1 ? args[1] : "MyMoney";

            // SqlScripts ships alongside the MyMoney assembly it's committed
            // under (Source/WPF/MyMoney/Database/SqlScripts); locate it
            // relative to this executable's own build output.
            string sqlScriptsRoot = Path.Combine(AppContext.BaseDirectory, "SqlScripts");
            if (!Directory.Exists(sqlScriptsRoot))
            {
                Console.WriteLine($"Could not find SqlScripts folder at {sqlScriptsRoot}.");
                return 1;
            }

            var runner = new BootstrapRunner(sqlScriptsRoot);
            bool success = runner.Run(server, databaseName);
            return success ? 0 : 1;
        }
    }
}
```

Add a build step to `MyMoneyAdmin.csproj` so the `.sql` files end up next to the built executable at `AppContext.BaseDirectory` (append inside the existing `<Project>` element from Task 4):

```xml
  <ItemGroup>
    <None Include="..\MyMoney\Database\SqlScripts\**\*.sql">
      <Link>SqlScripts\%(RecursiveDir)%(Filename)%(Extension)</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 2: Build**

Run: `dotnet build Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj`
Expected: builds with 0 errors; `bin/Debug/net10.0/SqlScripts/{Bootstrap,Schema,Access,Test}/*.sql` present in the output.

- [ ] **Step 3: Manual end-to-end verification (requires a real SQL Server)**

1. Delete any existing `MyMoney` database and the three logins on your dev SQL Server (or use a fresh instance).
2. Delete `%USERPROFILE%\.secrets\MyMoney\dataengine.credentials.json` if present.
3. Enable `sa` on the dev server.
4. Run: `dotnet run --project Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj -- <your-server> MyMoney`
5. Enter the `sa` password when prompted.
6. Confirm it prints "Bootstrap complete.", the credentials file now exists and contains all three accounts, and (in SSMS) the `MyMoney` database has a `Payees` table and the four `Payees_*` procedures with the grants from Task 6.
7. Disable `sa` again on the server.

- [ ] **Step 4: Commit**

```bash
git add Source/WPF/MyMoneyAdmin/BootstrapRunner.cs Source/WPF/MyMoneyAdmin/Program.cs Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj
git commit -m "Add BootstrapRunner and Program entry point for MyMoneyAdmin"
```

---

## Task 10: WPF app startup integration

**Revision note (post-implementation, multiple rounds):** the code blocks
below are the task's original design and no longer match the shipped
files exactly — both diverged during implementation and two further
rounds of whole-branch review and live-server verification. Treat this
section as historical context, not a literal reference; the actual
source files are authoritative:
- `Source/WPF/MyMoney/Database/DataEngineStartup.cs`
- `Source/WPF/MyMoney/MainWindow.xaml.cs`
- `Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj`

Three concrete divergences worth calling out explicitly (see Step 1 and
Step 3 below for what actually happened and why):
1. The MSBuild wiring described in Step 1 (a `ProjectReference` from
   `MyMoney.csproj` to `MyMoneyAdmin.csproj`) was never implemented —
   it would have created a real circular project reference. The copy
   target lives in `MyMoneyAdmin.csproj` instead; `MyMoney.csproj` is
   never modified by this task.
2. The auto-load block in Step 3 does not sit right after
   `InitializeComponent()` — it moved to the end of the constructor,
   after `DataContextChanged` is subscribed, to fix a bug where the
   loaded model was being silently discarded.
3. `DataEngineStartup.TryAutoLoad` gained a synthetic `DatabasePath`
   assignment, config/credential-file error handling, and database-name
   validation beyond what Step 2's code block shows below, following
   two further review rounds and live-server testing.

**Files:**
- Create: `Source/WPF/MyMoney/Database/DataEngineStartup.cs`
- Modify: `Source/WPF/MyMoney/MainWindow.xaml.cs` (constructor — see revision note above for the actual insertion point)
- Modify: `Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj` (add a post-build copy target so `MyMoneyAdmin.exe` ends up next to `MyMoney.exe` — see revision note above for why this landed here instead of `MyMoney.csproj`)

**Interfaces:**
- Consumes: `DataEngineConfig.Load` (Task 1), `DataEngineCredentialStore` (Task 2), `SqlServerStoredProcDatabase` (Task 7).
- Produces: `class DataEngineStartup { static bool TryAutoLoad(string configPath, out IDatabase database, out MyMoney money); }`, called from `MainWindow`'s constructor.

`TryAutoLoad` returns `false` (with `database`/`money` both `null`) whenever the config says SQLite, is missing, or the file doesn't exist and can't be bootstrapped — in every such case `MainWindow` falls through to its existing, completely unmodified empty-launch / File-menu-driven behavior.

- [ ] **Step 1: Wire MyMoneyAdmin's build output into MyMoney's output directory**

`MyMoneyAdmin.csproj` (Task 4) builds to its own separate output folder
(`Source/WPF/MyMoneyAdmin/bin/...`) — nothing so far puts `MyMoneyAdmin.exe`
next to `MyMoney.exe`, which Step 2 below needs.

**As actually implemented:** `MyMoneyAdmin.csproj` already has a
`ProjectReference` to `MyMoney.csproj` (from Task 9, needed for
`DataEngineCredentialStore`/`DataEnginePasswordGenerator`). Adding the
reverse reference this step originally called for — from `MyMoney.csproj`
to `MyMoneyAdmin.csproj` — would create a real circular project reference
(`MyMoney → MyMoneyAdmin → MyMoney`) and fails at restore. Since
`MyMoneyAdmin` already depends on (and therefore always builds after)
`MyMoney`, no new reference is needed in either direction — the copy
target just needs to live in `MyMoneyAdmin.csproj` instead, copying its
own output into `MyMoney`'s output directory after it builds.
`MyMoney.csproj` is never modified by this task. Add this to
`Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj`, inside the existing
`<Project>` element:

```xml
  <Target Name="CopyMyMoneyAdminOutputToMyMoney" AfterTargets="Build" Condition="'$(Configuration)' == 'Debug'">
    <ItemGroup>
      <MyMoneyAdminOutput Include="$(OutDir)**\*.*" />
    </ItemGroup>
    <Copy SourceFiles="@(MyMoneyAdminOutput)"
          DestinationFolder="$(MSBuildThisFileDirectory)..\MyMoney\bin\$(Configuration)\net10.0-windows7.0\win-x64\%(RecursiveDir)"
          SkipUnchangedFiles="true"
          Condition="Exists('$(MSBuildThisFileDirectory)..\MyMoney\bin\$(Configuration)\net10.0-windows7.0\win-x64')" />
  </Target>
```

The `Condition="'$(Configuration)' == 'Debug'"` on the target itself is
required, not optional — without it, Release builds copy `MyMoneyAdmin.exe`
next to `MyMoney.exe` too, contradicting "Release builds completely
unaffected." (This condition was missing in an earlier round and caught
by review before merge.)

Run `dotnet build Source/WPF/MyMoney.sln` and confirm `MyMoneyAdmin.exe` (and
its dependency DLLs) now appear alongside `MyMoney.exe` in
`Source/WPF/MyMoney/bin/Debug/net10.0-windows7.0/win-x64/`. If they don't
show up, check that `$(Configuration)` matches what you built (Debug vs
Release) and that Task 4/9 already produced a `MyMoneyAdmin/bin/Debug/net10.0-windows7.0/`
folder to copy from. Also confirm with a `-c Release` build that they're
absent there.

- [ ] **Step 2: Write the DataEngineStartup implementation**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.SqlClient;

namespace Walkabout.Data
{
#if DEBUG
    public static class DataEngineStartup
    {
        public static bool TryAutoLoad(string configPath, out IDatabase database, out MyMoney money)
        {
            database = null;
            money = null;

            DataEngineConfig config = DataEngineConfig.Load(configPath);
            if (config.Engine != DataEngineType.SqlServer || string.IsNullOrEmpty(config.Server) || string.IsNullOrEmpty(config.Database))
            {
                return false;
            }

            if (!DatabaseExists(config.Server, config.Database))
            {
                if (!RunMyMoneyAdmin(config.Server, config.Database))
                {
                    return false;
                }
            }

            var credentialStore = new DataEngineCredentialStore(DataEngineCredentialStore.GetDefaultPath());
            DataEngineCredential userCredential;
            try
            {
                userCredential = credentialStore.GetCredential("MyMoneyUser");
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"DataEngineStartup: {ex.Message}");
                return false;
            }

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = config.Server,
                InitialCatalog = config.Database,
                UserID = userCredential.UserId,
                Password = userCredential.Password,
                // Required against real dev SQL Server instances with a
                // self-signed/untrusted cert -- omitting this was found,
                // via live-server testing, to block every connection
                // attempt in this feature (all four connection-string
                // builders across the codebase need it, not just this one).
                TrustServerCertificate = true
            };

            var sqlServerDatabase = new SqlServerStoredProcDatabase { ConnectionStringOverride = builder.ConnectionString };
            money = sqlServerDatabase.Load(null);
            database = sqlServerDatabase;
            return true;
        }

        private static bool DatabaseExists(string server, string databaseName)
        {
            // We don't have MyMoneyUser's password until bootstrap has run
            // once, so "does the database exist" is answered by "does the
            // credential file bootstrap wrote already exist" -- there's no
            // way to check the server itself without a login to check with.
            return File.Exists(DataEngineCredentialStore.GetDefaultPath());
        }

        private static bool RunMyMoneyAdmin(string server, string databaseName)
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "MyMoneyAdmin.exe");
            if (!File.Exists(exePath))
            {
                Debug.WriteLine($"DataEngineStartup: MyMoneyAdmin.exe not found at {exePath}. Build Source/WPF/MyMoneyAdmin first.");
                return false;
            }

            var startInfo = new ProcessStartInfo(exePath, $"\"{server}\" \"{databaseName}\"")
            {
                UseShellExecute = true
            };

            using (Process process = Process.Start(startInfo))
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }
    }
#endif
}
```

- [ ] **Step 3: Wire into `MainWindow`'s constructor**

**As actually implemented:** this does *not* sit right after
`InitializeComponent()`. The original placement set `this.DataContext`
before `DataContextChanged` was subscribed (that subscription happens
later in the constructor), so the auto-loaded model was silently
discarded — the handler that does the real wiring (`this.myMoney =
money`, and populating `accountsControl`/`categoriesControl`/etc.) never
ran. Fixed by moving this block to the end of the constructor, after
`DataContextChanged += ...` is subscribed and after the account/category/
payee/securities controls and `ofxController` are constructed — mirroring
the same sequence a normal file-based load already uses elsewhere in this
file (database, `DataContext`, `canSave`, `emptyWindow`), rather than a
minimal hand-picked subset of it. See
`Source/WPF/MyMoney/MainWindow.xaml.cs` for the current, authoritative
placement and body — it now also sets `this.emptyWindow = true` (so
`OnMainWindowLoaded` doesn't separately try to reopen the last-used file)
and threads a logger callback into `TryAutoLoad` for visible diagnostics.

- [ ] **Step 4: Build**

Run: `dotnet build Source/WPF/MyMoney.sln`
Expected: 0 errors.

- [ ] **Step 5: Manual verification — SQLite path unaffected**

1. Ensure `%LOCALAPPDATA%\MyMoney\dataengine.config.json` does not exist.
2. Launch `MyMoney.exe` (DEBUG build).
3. Confirm the app launches exactly as before — empty main window, no auto-loaded database, File menu drives everything as it always has.

- [ ] **Step 6: Manual verification — SQL Server auto-load**

1. Complete Task 9's bootstrap against a dev SQL Server.
2. Create `%LOCALAPPDATA%\MyMoney\dataengine.config.json`: `{ "engine": "SqlServer", "server": "<your-server>", "database": "MyMoney" }`.
3. Launch `MyMoney.exe` (DEBUG build).
4. Confirm the app auto-loads without any File-menu interaction, and that adding/editing/deleting a payee through the UI persists (verify with a direct `SELECT * FROM dbo.Payees` in SSMS as `MyMoneyAdmin`, or `EXEC dbo.Payees_SelectAll` as `MyMoneyUser`).

- [ ] **Step 7: Commit**

```bash
git add Source/WPF/MyMoney/Database/DataEngineStartup.cs Source/WPF/MyMoney/MainWindow.xaml.cs Source/WPF/MyMoneyAdmin/MyMoneyAdmin.csproj
git commit -m "Wire DEBUG-only SQL Server auto-load into MainWindow startup"
```

---

## Plan Self-Review

**Spec coverage:**
- Config-driven engine selection (`dataengine.config.json`) → Task 1, Task 10.
- Credential store outside repo (`%USERPROFILE%\.secrets\MyMoney\...`) → Task 2.
- Auto-generated passwords → Task 3.
- `sa` credentials persisted in the same credentials file as the three
  app accounts (revised 2026-09-14, mid-implementation, from the
  original "never persisted" design -- see the spec's `sa` Handling
  section for the accepted tradeoff), Retry/Cancel on any connection
  failure → Task 4, Task 8.
- Master-db self-dropping bootstrap procedure → Task 5.
- Three tiered SQL logins → Task 5.
- Hand-authored stored procedures for schema/access/test → Task 6.
- `MyMoneyUser` restricted to `EXECUTE`-only, no table grants → Task 6 (manual verification step explicitly proves this negative case).
- `MyMoneyTest` seam for future item #3 → Task 6's `Payees_Test_Reset`.
- Runtime CRUD via stored procedures, `SqliteDatabase` untouched → Task 7.
- DEBUG-only reachability → Task 10 (`#if DEBUG` at both the class and call-site level).
- Idempotent bootstrap → Task 5 script's `IF NOT EXISTS` guards, verified by its "re-run" manual step.

**Placeholder scan:** No TBD/TODO markers; every step has complete, real code or a fully-specified manual procedure. The `{{AdminPassword}}` etc. tokens in Task 5's `.sql` file are intentional runtime substitution markers (explained in-line and consumed by Task 9's `Replace()` calls), not unresolved plan placeholders.

**Type consistency:** `DataEngineCredential`/`DataEngineCredentialStore` (Task 2) are used identically in Task 9 (`BootstrapRunner`) and Task 10 (`DataEngineStartup`) — same `GetCredential`/`Save` signatures throughout. `SqlServerStoredProcDatabase.ConnectionStringOverride` (Task 7) is set the same way in its own test (Task 7) and in `DataEngineStartup` (Task 10). `RetryLoop.Run`'s delegate signatures (Task 4) match exactly how `SaBootstrapConnection` (Task 8) calls it.
