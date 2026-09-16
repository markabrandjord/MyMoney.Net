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
           AccessKey, UserKey, UserKeyExpireDate
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
