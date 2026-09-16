-- Securities_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Securities table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Securities_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate FROM dbo.Securities ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_Insert
    @Id INT, @Name NVARCHAR(80), @Symbol NVARCHAR(20), @Price MONEY, @LastPrice MONEY,
    @CuspId NVARCHAR(20), @SecurityType INT, @Taxable TINYINT, @PriceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Securities (Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate)
    VALUES (@Id, @Name, @Symbol, @Price, @LastPrice, @CuspId, @SecurityType, @Taxable, @PriceDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_Update
    @Id INT, @Name NVARCHAR(80), @Symbol NVARCHAR(20), @Price MONEY, @LastPrice MONEY,
    @CuspId NVARCHAR(20), @SecurityType INT, @Taxable TINYINT, @PriceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Securities SET
        Name = @Name, Symbol = @Symbol, Price = @Price, LastPrice = @LastPrice, CuspId = @CuspId,
        SecurityType = @SecurityType, Taxable = @Taxable, PriceDate = @PriceDate
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Securities WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Securities_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Securities_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Securities_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Securities_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Securities_Delete TO MyMoneyTest;
GO
