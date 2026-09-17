-- OnlineAccounts_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all OnlineAccounts rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.OnlineAccounts;
END
GO

GRANT EXECUTE ON dbo.OnlineAccounts_Test_Reset TO MyMoneyTest;
GO
