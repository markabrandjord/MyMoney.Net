-- Categories_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Categories table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Categories_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum, Version
    FROM dbo.Categories ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Insert
    @Id INT, @Name NVARCHAR(80), @Description NVARCHAR(255), @Type INT, @ParentId INT,
    @Budget MONEY, @Frequency INT, @Balance MONEY, @Color NVARCHAR(10), @TaxRefNum INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Categories (Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum)
    VALUES (@Id, @Name, @Description, @Type, @ParentId, @Budget, @Frequency, @Balance, @Color, @TaxRefNum);
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Update
    @Id INT, @Name NVARCHAR(80), @Description NVARCHAR(255), @Type INT, @ParentId INT,
    @Budget MONEY, @Frequency INT, @Balance MONEY, @Color NVARCHAR(10), @TaxRefNum INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Categories SET
        Name = @Name, Description = @Description, Type = @Type, ParentId = @ParentId, Budget = @Budget,
        Frequency = @Frequency, Balance = @Balance, Color = @Color, TaxRefNum = @TaxRefNum
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Categories WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Categories_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Categories_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Delete TO MyMoneyTest;
GO

-- Phase 2c: SaveOne/SaveBatch support. Additive - the procs/grants above are untouched and still
-- serve the old whole-graph Save(MyMoney) path.

IF TYPE_ID(N'dbo.CategorySaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.CategorySaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(80) NULL,
        Description NVARCHAR(255) NULL,
        Type INT NULL,
        ParentId INT NULL,
        Budget MONEY NULL,
        Frequency INT NULL,
        Balance MONEY NULL,
        Color NCHAR(10) NULL,
        TaxRefNum INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_SaveBatch
    @Rows dbo.CategorySaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Categories c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Categories c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Categories (Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum, Version)
    SELECT Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET
        Name = r.Name, Description = r.Description, Type = r.Type, ParentId = r.ParentId, Budget = r.Budget,
        Frequency = r.Frequency, Balance = r.Balance, Color = r.Color, TaxRefNum = r.TaxRefNum, Version = c.Version + 1
    FROM dbo.Categories c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c
    FROM dbo.Categories c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Categories
    WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Categories_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.CategorySaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.CategorySaveBatchRow TO MyMoneyTest;
GO
