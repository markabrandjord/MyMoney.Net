-- StockSplits_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all StockSplits rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.StockSplits;
END
GO

GRANT EXECUTE ON dbo.StockSplits_Test_Reset TO MyMoneyTest;
GO
