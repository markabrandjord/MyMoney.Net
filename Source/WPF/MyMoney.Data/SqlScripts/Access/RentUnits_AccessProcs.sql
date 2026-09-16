-- RentUnits_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the RentUnits table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Building, Name, Renter, Note FROM dbo.RentUnits ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_Insert
    @Id INT, @Building INT, @Name NVARCHAR(255), @Renter NVARCHAR(255), @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.RentUnits (Id, Building, Name, Renter, Note) VALUES (@Id, @Building, @Name, @Renter, @Note);
END
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_Update
    @Id INT, @Building INT, @Name NVARCHAR(255), @Renter NVARCHAR(255), @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RentUnits SET Name = @Name, Renter = @Renter, Note = @Note WHERE Id = @Id AND Building = @Building;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentUnits_Delete
    @Id INT, @Building INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.RentUnits WHERE Id = @Id AND Building = @Building;
END
GO

GRANT EXECUTE ON dbo.RentUnits_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentUnits_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentUnits_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentUnits_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.RentUnits_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentUnits_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentUnits_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentUnits_Delete TO MyMoneyTest;
GO
