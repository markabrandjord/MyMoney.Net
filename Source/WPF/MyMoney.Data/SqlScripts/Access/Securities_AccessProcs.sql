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
    SELECT Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate, Version FROM dbo.Securities ORDER BY Id;
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

IF TYPE_ID(N'dbo.SecuritySaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.SecuritySaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(80) NULL,
        Symbol NVARCHAR(20) NULL,
        Price MONEY NULL,
        LastPrice MONEY NULL,
        CuspId NVARCHAR(20) NULL,
        SecurityType INT NULL,
        Taxable TINYINT NULL,
        PriceDate DATETIME NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Securities_SaveBatch
    @Rows dbo.SecuritySaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Securities c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Securities c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Securities (Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate, Version)
    SELECT Id, Name, Symbol, Price, LastPrice, CuspId, SecurityType, Taxable, PriceDate, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Name = r.Name, Symbol = r.Symbol, Price = r.Price, LastPrice = r.LastPrice,
        CuspId = r.CuspId, SecurityType = r.SecurityType, Taxable = r.Taxable, PriceDate = r.PriceDate,
        Version = c.Version + 1
    FROM dbo.Securities c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Securities c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Securities WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Securities_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Securities_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.SecuritySaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.SecuritySaveBatchRow TO MyMoneyTest;
GO
