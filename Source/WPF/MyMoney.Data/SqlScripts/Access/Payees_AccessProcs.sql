-- Payees_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Payees table -- neither has any direct table grants.
-- Per the spec's account table, MyMoneyTest gets EXECUTE on these same
-- access procedures *plus* the additional test-support procedures in
-- Payees_TestProcs.sql (e.g. Payees_Test_Reset) that are too risky to
-- grant MyMoneyUser.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Payees_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Version FROM dbo.Payees ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_Insert
    @Id INT,
    @Name NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Payees (Id, Name) VALUES (@Id, @Name);
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_Update
    @Id INT,
    @Name NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Payees SET Name = @Name WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Payees WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Payees_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Payees_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Payees_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Payees_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Payees_Delete TO MyMoneyTest;
GO

IF TYPE_ID(N'dbo.PayeeSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.PayeeSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(255) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Payees_SaveBatch
    @Rows dbo.PayeeSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Payees c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Payees c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Payees (Id, Name, Version)
    SELECT Id, Name, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Name = r.Name, Version = c.Version + 1
    FROM dbo.Payees c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Payees c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Payees WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Payees_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Payees_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.PayeeSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.PayeeSaveBatchRow TO MyMoneyTest;
GO
