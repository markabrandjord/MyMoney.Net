-- Categories_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all Categories rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Categories;
END
GO

GRANT EXECUTE ON dbo.Categories_Test_Reset TO MyMoneyTest;
GO
