-- Aliases_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Aliases table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Pattern, Payee, Flags, Version FROM dbo.Aliases ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_Insert
    @Id INT, @Pattern NVARCHAR(255), @Payee INT, @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Aliases (Id, Pattern, Payee, Flags) VALUES (@Id, @Pattern, @Payee, @Flags);
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_Update
    @Id INT, @Pattern NVARCHAR(255), @Payee INT, @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Aliases SET Pattern = @Pattern, Payee = @Payee, Flags = @Flags WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Aliases WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Aliases_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Aliases_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Aliases_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Aliases_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Aliases_Delete TO MyMoneyTest;
GO

IF TYPE_ID(N'dbo.AliasSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.AliasSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Pattern NVARCHAR(255) NULL,
        Payee INT NULL,
        Flags INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Aliases_SaveBatch
    @Rows dbo.AliasSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Aliases c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Aliases c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Aliases (Id, Pattern, Payee, Flags, Version)
    SELECT Id, Pattern, Payee, Flags, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET Pattern = r.Pattern, Payee = r.Payee, Flags = r.Flags, Version = c.Version + 1
    FROM dbo.Aliases c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Aliases c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Aliases WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Aliases_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Aliases_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.AliasSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.AliasSaveBatchRow TO MyMoneyTest;
GO
