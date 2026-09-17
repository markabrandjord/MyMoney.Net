-- Securities_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all Securities rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Securities_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Securities;
END
GO

GRANT EXECUTE ON dbo.Securities_Test_Reset TO MyMoneyTest;
GO
