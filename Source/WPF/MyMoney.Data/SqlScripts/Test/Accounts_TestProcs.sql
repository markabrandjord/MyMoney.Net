-- Accounts_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all Accounts rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Accounts;
END
GO

GRANT EXECUTE ON dbo.Accounts_Test_Reset TO MyMoneyTest;
GO
