# SQL Server `MyMoneyUser` CRUD Coverage (issue #22) — Design

## Background

Item #2 (SQL Server dual-engine support) introduced a tiered-security model
for the SQL Server engine: `MyMoneyAdmin` (db_owner, full schema control),
`MyMoneyUser` (the login the shipped app actually connects as, via
`DataEngineStartup.TryAutoLoad` → `DataEngineCredentialStore.GetCredential("MyMoneyUser")`),
and `MyMoneyTest` (same tier as `MyMoneyUser`, plus test-support procs).
Neither `MyMoneyUser` nor `MyMoneyTest` has any direct table grants — all
access goes through stored procedures, following standard least-privilege
practice (ownership chaining lets a proc touch a table its caller can't
touch directly).

At the time, item #2 deliberately scoped stored-proc coverage to **Payees
only** — a vertical slice proving the pattern end-to-end (schema, tiered
logins, `SqlServerStoredProcDatabase` overriding just the Payees CRUD
methods), not a claim that every entity was covered. That was the right
call for proving the pattern, but it means every other `[TableMapping]`
entity — Accounts, Categories, Currencies, Securities, StockSplits, Aliases,
Transactions, Splits, Investment, plus OnlineAccounts/AccountAliases/
TransactionExtras/RentBuildings/RentUnits/LoanPayments — has no way to be
read or written under the `MyMoneyUser` tier the shipped app actually uses.
`SqlServerDatabase`'s generic CRUD path (`LazyCreateTables()` plus raw
`INSERT`/`UPDATE`/`DELETE`/`SELECT`) needs direct table access, which
`MyMoneyUser` doesn't have.

This surfaced while scoping issue #19 (a cross-engine SQLite-vs-SQL-Server
parity test), which needs a rich, multi-table dataset to round-trip through
SQL Server — not just Payees. Rather than route #19's tests through
`MyMoneyAdmin` (which would test a security posture the shipped app doesn't
actually use), this issue closes the real gap so `MyMoneyUser` — the tier
the app runs under — can actually perform full CRUD on the entities that
matter.

## Scope

Nine entities, matching what issue #19's dataset needs and the core of the
app's everyday usage: **Accounts, Categories, Currencies, Securities,
StockSplits, Aliases, Transactions, Splits, Investment.**

Explicitly out of scope, split to issue #23: OnlineAccounts, AccountAliases,
TransactionExtras, RentBuildings, RentUnits, LoanPayments. Nothing currently
depends on these being reachable under `MyMoneyUser`.

## Design

### 1. Stored procs, mirroring `Payees_AccessProcs.sql` exactly

For each of the 9 entities, add a `SqlScripts/Access/<Entity>_AccessProcs.sql`
script (or one combined script per logical group, e.g. `Transactions_AccessProcs.sql`
covering Transactions+Splits+Investment together, since they're already
handled as three separate methods/tables by the generic base class — see
below) with `_SelectAll`/`_Insert`/`_Update`/`_Delete` procs, `GRANT EXECUTE`
to both `MyMoneyUser` and `MyMoneyTest`, run as `MyMoneyAdmin` — same
structure, same header comment convention, same "run as MyMoneyAdmin"
instruction as the existing Payees script. `BootstrapRunner.cs` gets a new
`ExecuteBatchScript(...)` line per script, so re-running the bootstrap
against the existing SQL Server instance deploys them (idempotent, per the
existing `CREATE OR ALTER PROCEDURE` convention).

Transactions/Splits/Investment are **not** one atomic parent+children proc.
The existing generic base class (`SqlDatabase.UpdateTransactions`,
`UpdateSplits`, `UpdateInvestment`) already treats these as three separately
updated tables, called in sequence — the stored-proc design mirrors that
same shape exactly: three independent proc sets, three independent
`SqlServerStoredProcDatabase` override methods, no new atomicity behavior
introduced or removed relative to what the generic path already does.

### 2. C# calling side: a shared generic ADO.NET helper

`SqlServerStoredProcDatabase` currently has one bespoke set of Payees
methods (`ReadPayees`, `UpdatePayees`, `ExecutePayeeProc`). Rather than
writing 9 more fully independent bespoke method sets (high repetition) or
building a metadata-driven codegen system (bigger lift than this issue
needs — better suited to a future consolidation pass once more of the
schema follows this pattern), add one shared helper analogous to
`ExecutePayeeProc` but generic over the parameter list:

```csharp
private static void ExecuteProc(SqlConnection connection, string procName, params (string Name, object Value)[] parameters)
{
    using (var command = new SqlCommand(procName, connection) { CommandType = CommandType.StoredProcedure })
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        command.ExecuteNonQuery();
    }
}
```

Each entity's override (`UpdateAccounts`, `UpdateCategories`, etc.) keeps
the same per-entity `IsChanged`/`IsInserted`/`IsDeleted` dispatch loop
`UpdatePayees` already uses, but calls this shared helper instead of
hand-rolling its own `SqlCommand`/parameter-binding code. This keeps the
existing local-`SqlConnection`-per-call style `SqlServerStoredProcDatabase`
already uses (deliberately not switching to the base class's different
ambient-connection/`this.transaction` style used by `ExecuteNonQuery` —
keeping this subclass internally consistent with its own established
pattern rather than mixing two connection-management styles).

### 3. `Load()` extended to read all 9 entities

`SqlServerStoredProcDatabase.Load()` currently reads only Payees, leaving
every other collection empty by design (documented as the "Payees-only
vertical slice" limitation). This issue extends it to call the new
`ReadAccounts`/`ReadCategories`/etc. for all 9 newly-covered entities,
following the same `_SelectAll` proc + local-connection read pattern
`ReadPayees` already uses.

### 4. Verification

Add `SqlServerDatabaseContractTests : DatabaseContractTests` (in
`MyMoney.TestSupport`), using `SqlServerStoredProcDatabase` connected as
`MyMoneyUser` (new env var, `MYMONEY_TEST_SQLSERVER_ADMIN_CONNECTION` is
**not** needed here — `MyMoneyUser` now has everything the shared contract
suite touches: Accounts, Categories, Payees, Transactions). This closes the
"third contract-test flavor" item referenced in issue #1, as part of this
issue rather than #19.

Test isolation against the shared, persistent SQL Server database (unlike
`SqliteDatabaseContractTests`'s fresh-temp-file-per-test, there's no
"fresh database per test" here) is handled by a `SetUp`/`TearDown` helper
that deletes all rows from the 9 covered tables using the `MyMoneyAdmin`
connection (which has direct table access) — no new stored procs needed
for this, since `MyMoneyAdmin` isn't subject to the EXECUTE-only
restriction the tests themselves are proving out for `MyMoneyUser`.

Currencies, Securities, StockSplits, Aliases, and Investment aren't
exercised by the shared contract suite's `BuildSampleMoney()` — their
correctness under the `MyMoneyUser` tier gets proven by issue #19's later
rich-dataset parity test, which (once this issue lands) can run through
`SqlServerStoredProcDatabase` + `MyMoneyUser` — the same tier the real app
uses — instead of the `MyMoneyAdmin` workaround originally being considered.

## Out of scope

- OnlineAccounts, AccountAliases, TransactionExtras, RentBuildings,
  RentUnits, LoanPayments — split to issue #23.
- Any change to the tiered-security model itself, `MyMoneyAdmin`'s
  bootstrap process, or the SQLite/generic `SqlServerDatabase` CRUD paths.
- Parameterizing `SqlServerDatabase`'s generic (non-stored-proc) CRUD path
  — that's a separate, already-scoped-out decision from the SQLite
  parameterization work (`SupportsParameterizedUpdate` is deliberately
  `SqliteDatabase`-only).
- A metadata-driven codegen system for stored procs — noted as a possible
  future consolidation once more of the schema follows this pattern, not
  needed for this issue's scope.
