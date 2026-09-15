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
    SELECT Id, Name FROM dbo.Payees ORDER BY Id;
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
