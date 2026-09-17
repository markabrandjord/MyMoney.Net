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
    SELECT Id, Symbol, Name, Ratio, LastRatio, CultureCode, Version FROM dbo.Currencies ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Insert
    @Id INT, @Symbol NVARCHAR(20), @Name NVARCHAR(80), @Ratio MONEY, @LastRatio MONEY, @CultureCode NVARCHAR(80)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Currencies (Id, Symbol, Name, Ratio, LastRatio, CultureCode)
    VALUES (@Id, @Symbol, @Name, @Ratio, @LastRatio, @CultureCode);
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_Update
    @Id INT, @Symbol NVARCHAR(20), @Name NVARCHAR(80), @Ratio MONEY, @LastRatio MONEY, @CultureCode NVARCHAR(80)
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

IF TYPE_ID(N'dbo.CurrencySaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.CurrencySaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Symbol NCHAR(20) NULL,
        Name NVARCHAR(80) NULL,
        Ratio MONEY NULL,
        LastRatio MONEY NULL,
        CultureCode NVARCHAR(80) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Currencies_SaveBatch
    @Rows dbo.CurrencySaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Currencies c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Currencies c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Currencies (Id, Symbol, Name, Ratio, LastRatio, CultureCode, Version)
    SELECT Id, Symbol, Name, Ratio, LastRatio, CultureCode, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Symbol = r.Symbol, Name = r.Name, Ratio = r.Ratio, LastRatio = r.LastRatio,
        CultureCode = r.CultureCode, Version = c.Version + 1
    FROM dbo.Currencies c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Currencies c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Currencies WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Currencies_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Currencies_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.CurrencySaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.CurrencySaveBatchRow TO MyMoneyTest;
GO
