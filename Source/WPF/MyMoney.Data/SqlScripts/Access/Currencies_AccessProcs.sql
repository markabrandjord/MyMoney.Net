-- Currencies_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Currencies table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Symbol, Name, Ratio, LastRatio, CultureCode FROM dbo.Currencies ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Insert
    @Id INT, @Symbol NVARCHAR(10), @Name NVARCHAR(80), @Ratio DECIMAL(18,6), @LastRatio DECIMAL(18,6), @CultureCode NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Currencies (Id, Symbol, Name, Ratio, LastRatio, CultureCode)
    VALUES (@Id, @Symbol, @Name, @Ratio, @LastRatio, @CultureCode);
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Update
    @Id INT, @Symbol NVARCHAR(10), @Name NVARCHAR(80), @Ratio DECIMAL(18,6), @LastRatio DECIMAL(18,6), @CultureCode NVARCHAR(10)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Currencies SET Symbol = @Symbol, Name = @Name, Ratio = @Ratio, LastRatio = @LastRatio, CultureCode = @CultureCode
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Currencies WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Currencies_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Currencies_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Currencies_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Currencies_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Currencies_Delete TO MyMoneyTest;
GO
