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
    SELECT Id, Pattern, Payee, Flags FROM dbo.Aliases ORDER BY Id;
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
