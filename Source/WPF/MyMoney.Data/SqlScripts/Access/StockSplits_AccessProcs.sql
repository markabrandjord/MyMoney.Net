-- StockSplits_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the StockSplits table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Date, Security, Numerator, Denominator FROM dbo.StockSplits ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_Insert
    @Id BIGINT, @Date DATETIME, @Security INT, @Numerator MONEY, @Denominator MONEY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.StockSplits (Id, Date, Security, Numerator, Denominator)
    VALUES (@Id, @Date, @Security, @Numerator, @Denominator);
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_Update
    @Id BIGINT, @Date DATETIME, @Security INT, @Numerator MONEY, @Denominator MONEY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.StockSplits SET Date = @Date, Security = @Security, Numerator = @Numerator, Denominator = @Denominator
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_Delete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.StockSplits WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.StockSplits_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.StockSplits_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.StockSplits_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.StockSplits_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.StockSplits_Delete TO MyMoneyTest;
GO
