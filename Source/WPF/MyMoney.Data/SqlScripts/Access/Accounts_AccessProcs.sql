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
           CategoryIdForPrincipal, CategoryIdForInterest
    FROM dbo.Accounts ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Accounts_Insert
    @Id INT, @AccountId NVARCHAR(20), @OfxAccountId NVARCHAR(50), @Name NVARCHAR(80), @Type INT,
    @Description NVARCHAR(255), @OnlineAccount INT, @OpeningBalance MONEY, @LastSync DATETIME,
    @LastBalance DATETIME, @SyncGuid UNIQUEIDENTIFIER, @Flags INT, @Currency NVARCHAR(10),
    @WebSite NVARCHAR(255), @ReconcileWarning INT, @CategoryIdForPrincipal INT, @CategoryIdForInterest INT
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
    @LastBalance DATETIME, @SyncGuid UNIQUEIDENTIFIER, @Flags INT, @Currency NVARCHAR(10),
    @WebSite NVARCHAR(255), @ReconcileWarning INT, @CategoryIdForPrincipal INT, @CategoryIdForInterest INT
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
