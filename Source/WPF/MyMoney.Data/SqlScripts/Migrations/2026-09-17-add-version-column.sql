-- 2026-09-17-add-version-column.sql
-- Run once, by hand, as the MyMoneyAdmin login against the MyMoney database.
-- Adds the application-managed optimistic-concurrency column persistence-concurrency Phase 2c
-- needs. Named "Version", not "RowVersion": every one of these tables already has an unused
-- native RowVersion timestamp column (SQL Server's own auto-incrementing binary(8) type), which
-- can't be used for this purpose - it's a single counter shared by the whole database, not a
-- per-row counter, so it can never satisfy "a fresh row's version is 1" (see
-- docs/superpowers/specs/2026-09-17-persistence-concurrency-phase2c-design.md). This column is
-- purely additive: nothing existing reads or writes it.

USE MyMoney;
GO

IF COL_LENGTH('dbo.Categories', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.Categories ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.Currencies', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.Currencies ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.OnlineAccounts', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.OnlineAccounts ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.Accounts', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.Accounts ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.Payees', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.Payees ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.Aliases', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.Aliases ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.Securities', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.Securities ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.StockSplits', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.StockSplits ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.LoanPayments', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.LoanPayments ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.RentBuildings', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.RentBuildings ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO

IF COL_LENGTH('dbo.Transactions', 'Version') IS NULL
BEGIN
    ALTER TABLE dbo.Transactions ADD Version BIGINT NOT NULL DEFAULT 1;
END
GO
