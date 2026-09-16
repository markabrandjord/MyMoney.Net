-- AccountAliases_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the AccountAliases table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Pattern, AccountId, Flags FROM dbo.AccountAliases ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_Insert
    @Id INT, @Pattern NVARCHAR(255), @AccountId NVARCHAR(20), @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.AccountAliases (Id, Pattern, AccountId, Flags) VALUES (@Id, @Pattern, @AccountId, @Flags);
END
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_Update
    @Id INT, @Pattern NVARCHAR(255), @AccountId NVARCHAR(20), @Flags INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.AccountAliases SET Pattern = @Pattern, AccountId = @AccountId, Flags = @Flags WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.AccountAliases_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.AccountAliases WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.AccountAliases_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.AccountAliases_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.AccountAliases_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.AccountAliases_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.AccountAliases_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.AccountAliases_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.AccountAliases_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.AccountAliases_Delete TO MyMoneyTest;
GO
