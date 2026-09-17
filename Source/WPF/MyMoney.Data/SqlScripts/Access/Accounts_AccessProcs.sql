-- Accounts_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Accounts table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount, OpeningBalance,
           LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
           CategoryIdForPrincipal, CategoryIdForInterest, Version
    FROM dbo.Accounts ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Insert
    @Id INT, @AccountId NVARCHAR(20), @OfxAccountId NVARCHAR(50), @Name NVARCHAR(80), @Type INT,
    @Description NVARCHAR(255), @OnlineAccount INT, @OpeningBalance MONEY, @LastSync DATETIME,
    @LastBalance DATETIME, @SyncGuid UNIQUEIDENTIFIER, @Flags INT, @Currency NVARCHAR(3),
    @WebSite NVARCHAR(512), @ReconcileWarning INT, @CategoryIdForPrincipal INT, @CategoryIdForInterest INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Accounts (Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount,
        OpeningBalance, LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
        CategoryIdForPrincipal, CategoryIdForInterest)
    VALUES (@Id, @AccountId, @OfxAccountId, @Name, @Type, @Description, @OnlineAccount, @OpeningBalance,
        @LastSync, @LastBalance, @SyncGuid, @Flags, @Currency, @WebSite, @ReconcileWarning,
        @CategoryIdForPrincipal, @CategoryIdForInterest);
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Update
    @Id INT, @AccountId NVARCHAR(20), @OfxAccountId NVARCHAR(50), @Name NVARCHAR(80), @Type INT,
    @Description NVARCHAR(255), @OnlineAccount INT, @OpeningBalance MONEY, @LastSync DATETIME,
    @LastBalance DATETIME, @SyncGuid UNIQUEIDENTIFIER, @Flags INT, @Currency NVARCHAR(3),
    @WebSite NVARCHAR(512), @ReconcileWarning INT, @CategoryIdForPrincipal INT, @CategoryIdForInterest INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Accounts SET
        AccountId = @AccountId, OfxAccountId = @OfxAccountId, Name = @Name, Type = @Type,
        Description = @Description, OnlineAccount = @OnlineAccount, OpeningBalance = @OpeningBalance,
        LastSync = @LastSync, LastBalance = @LastBalance, SyncGuid = @SyncGuid, Flags = @Flags,
        Currency = @Currency, WebSite = @WebSite, ReconcileWarning = @ReconcileWarning,
        CategoryIdForPrincipal = @CategoryIdForPrincipal, CategoryIdForInterest = @CategoryIdForInterest
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Accounts WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Accounts_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Accounts_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Accounts_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Accounts_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Accounts_Delete TO MyMoneyTest;
GO

IF TYPE_ID(N'dbo.AccountSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.AccountSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        AccountId NVARCHAR(20) NULL,
        OfxAccountId NVARCHAR(50) NULL,
        Name NVARCHAR(80) NULL,
        Type INT NULL,
        Description NVARCHAR(255) NULL,
        OnlineAccount INT NULL,
        OpeningBalance MONEY NULL,
        LastSync DATETIME NULL,
        LastBalance DATETIME NULL,
        SyncGuid UNIQUEIDENTIFIER NULL,
        Flags INT NULL,
        Currency NVARCHAR(3) NULL,
        WebSite NVARCHAR(512) NULL,
        ReconcileWarning INT NULL,
        CategoryIdForPrincipal INT NULL,
        CategoryIdForInterest INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_SaveBatch
    @Rows dbo.AccountSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.Accounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.Accounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.Accounts (Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount,
        OpeningBalance, LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
        CategoryIdForPrincipal, CategoryIdForInterest, Version)
    SELECT Id, AccountId, OfxAccountId, Name, Type, Description, OnlineAccount, OpeningBalance,
        LastSync, LastBalance, SyncGuid, Flags, Currency, WebSite, ReconcileWarning,
        CategoryIdForPrincipal, CategoryIdForInterest, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET
        AccountId = r.AccountId, OfxAccountId = r.OfxAccountId, Name = r.Name, Type = r.Type,
        Description = r.Description, OnlineAccount = r.OnlineAccount, OpeningBalance = r.OpeningBalance,
        LastSync = r.LastSync, LastBalance = r.LastBalance, SyncGuid = r.SyncGuid, Flags = r.Flags,
        Currency = r.Currency, WebSite = r.WebSite, ReconcileWarning = r.ReconcileWarning,
        CategoryIdForPrincipal = r.CategoryIdForPrincipal, CategoryIdForInterest = r.CategoryIdForInterest,
        Version = c.Version + 1
    FROM dbo.Accounts c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.Accounts c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Accounts WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Accounts_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Accounts_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.AccountSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.AccountSaveBatchRow TO MyMoneyTest;
GO
