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

ALTER TABLE dbo.Categories ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Currencies ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.OnlineAccounts ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Accounts ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Payees ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Aliases ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Securities ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.StockSplits ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.LoanPayments ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.RentBuildings ADD Version BIGINT NOT NULL DEFAULT 1;
ALTER TABLE dbo.Transactions ADD Version BIGINT NOT NULL DEFAULT 1;
GO
