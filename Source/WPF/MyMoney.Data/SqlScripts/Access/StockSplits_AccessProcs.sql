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
    SELECT Id, Date, Security, Numerator, Denominator, Version FROM dbo.StockSplits ORDER BY Id;
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

IF TYPE_ID(N'dbo.StockSplitSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.StockSplitSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id BIGINT NOT NULL,
        Date DATETIME NULL,
        Security INT NULL,
        Numerator MONEY NULL,
        Denominator MONEY NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.StockSplits_SaveBatch
    @Rows dbo.StockSplitSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.StockSplits c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.StockSplits c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.StockSplits (Id, Date, Security, Numerator, Denominator, Version)
    SELECT Id, Date, Security, Numerator, Denominator, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Date = r.Date, Security = r.Security, Numerator = r.Numerator,
        Denominator = r.Denominator, Version = c.Version + 1
    FROM dbo.StockSplits c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.StockSplits c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.StockSplits WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.StockSplits_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.StockSplits_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.StockSplitSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.StockSplitSaveBatchRow TO MyMoneyTest;
GO
