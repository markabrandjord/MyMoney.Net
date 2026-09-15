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
