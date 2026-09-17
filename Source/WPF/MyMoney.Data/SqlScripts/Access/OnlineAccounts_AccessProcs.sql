-- OnlineAccounts_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the OnlineAccounts table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId, BrokerId,
           OfxVersion, LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken,
           AccessKey, UserKey, UserKeyExpireDate, Version
    FROM dbo.OnlineAccounts ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_Insert
    @Id INT, @Name NVARCHAR(80), @Institution NVARCHAR(80), @OFX NVARCHAR(255), @FID NVARCHAR(50),
    @UserId NVARCHAR(20), @Password NVARCHAR(50), @BankId NVARCHAR(50), @BranchId NVARCHAR(50),
    @BrokerId NVARCHAR(50), @OfxVersion NVARCHAR(10), @LogoUrl NVARCHAR(1000), @AppId NVARCHAR(10),
    @AppVersion NVARCHAR(10), @ClientUid NVARCHAR(36), @UserCred1 NVARCHAR(200), @UserCred2 NVARCHAR(200),
    @AuthToken NVARCHAR(200), @AccessKey NVARCHAR(36), @UserKey NVARCHAR(64), @UserKeyExpireDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.OnlineAccounts (Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId,
        BrokerId, OfxVersion, LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken,
        AccessKey, UserKey, UserKeyExpireDate)
    VALUES (@Id, @Name, @Institution, @OFX, @FID, @UserId, @Password, @BankId, @BranchId, @BrokerId,
        @OfxVersion, @LogoUrl, @AppId, @AppVersion, @ClientUid, @UserCred1, @UserCred2, @AuthToken,
        @AccessKey, @UserKey, @UserKeyExpireDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_Update
    @Id INT, @Name NVARCHAR(80), @Institution NVARCHAR(80), @OFX NVARCHAR(255), @FID NVARCHAR(50),
    @UserId NVARCHAR(20), @Password NVARCHAR(50), @BankId NVARCHAR(50), @BranchId NVARCHAR(50),
    @BrokerId NVARCHAR(50), @OfxVersion NVARCHAR(10), @LogoUrl NVARCHAR(1000), @AppId NVARCHAR(10),
    @AppVersion NVARCHAR(10), @ClientUid NVARCHAR(36), @UserCred1 NVARCHAR(200), @UserCred2 NVARCHAR(200),
    @AuthToken NVARCHAR(200), @AccessKey NVARCHAR(36), @UserKey NVARCHAR(64), @UserKeyExpireDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.OnlineAccounts SET
        Name = @Name, Institution = @Institution, OFX = @OFX, FID = @FID, UserId = @UserId,
        Password = @Password, BankId = @BankId, BranchId = @BranchId, BrokerId = @BrokerId,
        OfxVersion = @OfxVersion, LogoUrl = @LogoUrl, AppId = @AppId, AppVersion = @AppVersion,
        ClientUid = @ClientUid, UserCred1 = @UserCred1, UserCred2 = @UserCred2, AuthToken = @AuthToken,
        AccessKey = @AccessKey, UserKey = @UserKey, UserKeyExpireDate = @UserKeyExpireDate
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.OnlineAccounts WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.OnlineAccounts_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.OnlineAccounts_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.OnlineAccounts_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.OnlineAccounts_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.OnlineAccounts_Delete TO MyMoneyTest;
GO

IF TYPE_ID(N'dbo.OnlineAccountSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.OnlineAccountSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(80) NULL,
        Institution NVARCHAR(80) NULL,
        OFX NVARCHAR(255) NULL,
        FID NVARCHAR(50) NULL,
        UserId NCHAR(20) NULL,
        Password NVARCHAR(50) NULL,
        BankId NVARCHAR(50) NULL,
        BranchId NVARCHAR(50) NULL,
        BrokerId NVARCHAR(50) NULL,
        OfxVersion NCHAR(10) NULL,
        LogoUrl NVARCHAR(1000) NULL,
        AppId NCHAR(10) NULL,
        AppVersion NCHAR(10) NULL,
        ClientUid NCHAR(36) NULL,
        UserCred1 NVARCHAR(200) NULL,
        UserCred2 NVARCHAR(200) NULL,
        AuthToken NVARCHAR(200) NULL,
        AccessKey NCHAR(36) NULL,
        UserKey NVARCHAR(64) NULL,
        UserKeyExpireDate DATETIME NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.OnlineAccounts_SaveBatch
    @Rows dbo.OnlineAccountSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.OnlineAccounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.OnlineAccounts c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.OnlineAccounts (Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId,
        BrokerId, OfxVersion, LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken,
        AccessKey, UserKey, UserKeyExpireDate, Version)
    SELECT Id, Name, Institution, OFX, FID, UserId, Password, BankId, BranchId, BrokerId, OfxVersion,
        LogoUrl, AppId, AppVersion, ClientUid, UserCred1, UserCred2, AuthToken, AccessKey, UserKey,
        UserKeyExpireDate, 1
    FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET
        Name = r.Name, Institution = r.Institution, OFX = r.OFX, FID = r.FID, UserId = r.UserId,
        Password = r.Password, BankId = r.BankId, BranchId = r.BranchId, BrokerId = r.BrokerId,
        OfxVersion = r.OfxVersion, LogoUrl = r.LogoUrl, AppId = r.AppId, AppVersion = r.AppVersion,
        ClientUid = r.ClientUid, UserCred1 = r.UserCred1, UserCred2 = r.UserCred2, AuthToken = r.AuthToken,
        AccessKey = r.AccessKey, UserKey = r.UserKey, UserKeyExpireDate = r.UserKeyExpireDate,
        Version = c.Version + 1
    FROM dbo.OnlineAccounts c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.OnlineAccounts c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.OnlineAccounts WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.OnlineAccounts_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.OnlineAccounts_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.OnlineAccountSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.OnlineAccountSaveBatchRow TO MyMoneyTest;
GO
