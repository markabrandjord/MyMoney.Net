# Development

Software contributions to `MyMoney.Net` are welcome in the form
of github pull requests.  Simply fork the repo, create your own
branch and submit a pull request.

**MyMoney** is written entirely in C# for .NET Framework 10.0 and is easy for programmers who want access to their data and who want to quickly and easily add their own features. Your data will not be locked up in some proprietary format, it is yours to do with as you like.

Start by cloning the repo (or make your own fork if you plan to do pull requests):

```
git clone https://github.com/MoneyTools/MyMoney.Net
```

To build the WPF app load the following solution into
Visual Studio 2022.  You will need to first install [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```
devenv Source\WPF\MyMoney.sln
```

Then press F5 to run & debug the program.

## Testing

There are integrated unit tests which you can run in Visual Studio using the Test Explorer.

The integration tests in `ScenarioTests.cs` are interesting in that they are model based
tests running from a user model called `TestModel.dgml`.
To run this test you need to first install the
[DgmlTestMonitor 2022 plugin](https://marketplace.visualstudio.com/items?itemName=ChrisLovett.DgmlTestMonitor2022):

![](../Images/DgmlTestMonitorInstall.png)

This installs a new tool window, you can open View/Other Windows/DGML Test Monitor. Here's a [demo video](https://youtu.be/h5cIDTlnN8I) showing the testing running.

### Running the SQL Server contract tests

`SqlServerDatabaseContractTests` (in `MyMoney.TestSupport`) is tagged
`[Category("RequiresSqlServer")]` and is **not** run by
`dotnet test Source/WPF/UnitTests/UnitTests.csproj` or CI. It requires a real SQL Server instance
and three environment variables (`MYMONEY_TEST_SQLSERVER_USER_CONNECTION`,
`MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION`, `MYMONEY_TEST_ALLOW_DESTRUCTIVE_WIPE=1`) — it fails
fast, rather than skipping, if any are unset. Run it explicitly:

    dotnet test Source/WPF/MyMoney.TestSupport/MyMoney.TestSupport.csproj --filter "TestCategory=RequiresSqlServer"

This is a manual/local verification step, not part of CI — CI has no SQL Server instance to point
it at.

### Required flag when SQL Server env vars are set

`dotnet test Source/WPF/MyMoney.sln` runs each test project's host as a separate process. When
`MYMONEY_TEST_SQLSERVER_USER_CONNECTION`/`MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION` are set,
`MyMoney.TestSupport` (home of `SqlServerDatabaseContractTests`) and `UnitTests` (home of
`SqlServerStoredProcDatabaseTests`) can run concurrently and race against the same live database —
both start a fresh `MyMoney()` whose ID counters begin from the same deterministic value, so their
first-inserted rows can collide on primary key. Always pass `-m:1` to force serial project
execution when those env vars are set:

    dotnet test Source/WPF/MyMoney.sln -m:1

This is not needed when the SQL Server env vars are unset (those test fixtures skip/no-op) or when
running a single test project directly.

## Projects and Packages

The overall project dependencies looks like this where the main app MyMoney depends on `ModernWPF`, `Newtonsoft` and `System.Data.SQLite`.  Everything else shown here is test related.

![components](../Images/components.png)
