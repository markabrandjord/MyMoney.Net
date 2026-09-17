-- RentBuildings_TestProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- Test-support operations too risky to grant MyMoneyUser: this proc
-- wipes all RentBuildings rows unconditionally for test setup/teardown.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_Test_Reset
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.RentBuildings;
END
GO

GRANT EXECUTE ON dbo.RentBuildings_Test_Reset TO MyMoneyTest;
GO
