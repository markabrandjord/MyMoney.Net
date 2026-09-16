-- RentBuildings_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the RentBuildings table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue,
           CategoryForIncome, CategoryForTaxes, CategoryForInterest, CategoryForRepairs,
           CategoryForMaintenance, CategoryForManagement, OwnershipName1, OwnershipName2,
           OwnershipPercentage1, OwnershipPercentage2, Note
    FROM dbo.RentBuildings ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_Insert
    @Id INT, @Name NVARCHAR(255), @Address NVARCHAR(255), @PurchasedDate DATETIME, @PurchasedPrice MONEY,
    @LandValue MONEY, @EstimatedValue MONEY, @CategoryForIncome INT, @CategoryForTaxes INT,
    @CategoryForInterest INT, @CategoryForRepairs INT, @CategoryForMaintenance INT, @CategoryForManagement INT,
    @OwnershipName1 NVARCHAR(255), @OwnershipName2 NVARCHAR(255), @OwnershipPercentage1 MONEY,
    @OwnershipPercentage2 MONEY, @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.RentBuildings (Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue,
        CategoryForIncome, CategoryForTaxes, CategoryForInterest, CategoryForRepairs, CategoryForMaintenance,
        CategoryForManagement, OwnershipName1, OwnershipName2, OwnershipPercentage1, OwnershipPercentage2, Note)
    VALUES (@Id, @Name, @Address, @PurchasedDate, @PurchasedPrice, @LandValue, @EstimatedValue,
        @CategoryForIncome, @CategoryForTaxes, @CategoryForInterest, @CategoryForRepairs, @CategoryForMaintenance,
        @CategoryForManagement, @OwnershipName1, @OwnershipName2, @OwnershipPercentage1, @OwnershipPercentage2, @Note);
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_Update
    @Id INT, @Name NVARCHAR(255), @Address NVARCHAR(255), @PurchasedDate DATETIME, @PurchasedPrice MONEY,
    @LandValue MONEY, @EstimatedValue MONEY, @CategoryForIncome INT, @CategoryForTaxes INT,
    @CategoryForInterest INT, @CategoryForRepairs INT, @CategoryForMaintenance INT, @CategoryForManagement INT,
    @OwnershipName1 NVARCHAR(255), @OwnershipName2 NVARCHAR(255), @OwnershipPercentage1 MONEY,
    @OwnershipPercentage2 MONEY, @Note NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.RentBuildings SET
        Name = @Name, Address = @Address, PurchasedDate = @PurchasedDate, PurchasedPrice = @PurchasedPrice,
        LandValue = @LandValue, EstimatedValue = @EstimatedValue, CategoryForIncome = @CategoryForIncome,
        CategoryForTaxes = @CategoryForTaxes, CategoryForInterest = @CategoryForInterest,
        CategoryForRepairs = @CategoryForRepairs, CategoryForMaintenance = @CategoryForMaintenance,
        CategoryForManagement = @CategoryForManagement, OwnershipName1 = @OwnershipName1,
        OwnershipName2 = @OwnershipName2, OwnershipPercentage1 = @OwnershipPercentage1,
        OwnershipPercentage2 = @OwnershipPercentage2, Note = @Note
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.RentBuildings WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.RentBuildings_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.RentBuildings_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentBuildings_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentBuildings_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.RentBuildings_Delete TO MyMoneyTest;
GO
