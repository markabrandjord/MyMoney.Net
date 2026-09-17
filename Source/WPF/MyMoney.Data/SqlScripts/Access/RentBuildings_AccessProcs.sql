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
           OwnershipPercentage1, OwnershipPercentage2, Note, Version
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

IF TYPE_ID(N'dbo.RentBuildingSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.RentBuildingSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Name NVARCHAR(255) NULL,
        Address NVARCHAR(255) NULL,
        PurchasedDate DATETIME NULL,
        PurchasedPrice MONEY NULL,
        LandValue MONEY NULL,
        EstimatedValue MONEY NULL,
        CategoryForIncome INT NULL,
        CategoryForTaxes INT NULL,
        CategoryForInterest INT NULL,
        CategoryForRepairs INT NULL,
        CategoryForMaintenance INT NULL,
        CategoryForManagement INT NULL,
        OwnershipName1 NVARCHAR(255) NULL,
        OwnershipName2 NVARCHAR(255) NULL,
        OwnershipPercentage1 MONEY NULL,
        OwnershipPercentage2 MONEY NULL,
        Note NVARCHAR(255) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

IF TYPE_ID(N'dbo.RentUnitRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.RentUnitRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        Building INT NOT NULL,
        Name NVARCHAR(255) NULL,
        Renter NVARCHAR(255) NULL,
        Note NVARCHAR(255) NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.RentBuildings_SaveBatch
    @Buildings dbo.RentBuildingSaveBatchRow READONLY,
    @Units dbo.RentUnitRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Buildings r
        LEFT JOIN dbo.RentBuildings c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Buildings r LEFT JOIN dbo.RentBuildings c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- Children before parent on delete, matching UpdateTransactions' documented FK-ordering rule.
    DELETE u FROM dbo.RentUnits u JOIN @Units r ON u.Id = r.Id AND u.Building = r.Building WHERE r.[Action] = 'D';

    DELETE c FROM dbo.RentBuildings c JOIN @Buildings r ON c.Id = r.Id WHERE r.[Action] = 'D';

    INSERT INTO dbo.RentBuildings (Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue,
        CategoryForIncome, CategoryForTaxes, CategoryForInterest, CategoryForRepairs, CategoryForMaintenance,
        CategoryForManagement, OwnershipName1, OwnershipName2, OwnershipPercentage1, OwnershipPercentage2, Note, Version)
    SELECT Id, Name, Address, PurchasedDate, PurchasedPrice, LandValue, EstimatedValue, CategoryForIncome,
        CategoryForTaxes, CategoryForInterest, CategoryForRepairs, CategoryForMaintenance, CategoryForManagement,
        OwnershipName1, OwnershipName2, OwnershipPercentage1, OwnershipPercentage2, Note, 1
    FROM @Buildings WHERE [Action] = 'I';

    UPDATE c SET Name = r.Name, Address = r.Address, PurchasedDate = r.PurchasedDate, PurchasedPrice = r.PurchasedPrice,
        LandValue = r.LandValue, EstimatedValue = r.EstimatedValue, CategoryForIncome = r.CategoryForIncome,
        CategoryForTaxes = r.CategoryForTaxes, CategoryForInterest = r.CategoryForInterest,
        CategoryForRepairs = r.CategoryForRepairs, CategoryForMaintenance = r.CategoryForMaintenance,
        CategoryForManagement = r.CategoryForManagement, OwnershipName1 = r.OwnershipName1,
        OwnershipName2 = r.OwnershipName2, OwnershipPercentage1 = r.OwnershipPercentage1,
        OwnershipPercentage2 = r.OwnershipPercentage2, Note = r.Note, Version = c.Version + 1
    FROM dbo.RentBuildings c JOIN @Buildings r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    INSERT INTO dbo.RentUnits (Id, Building, Name, Renter, Note)
    SELECT Id, Building, Name, Renter, Note FROM @Units WHERE [Action] = 'I';

    UPDATE u SET Name = r.Name, Renter = r.Renter, Note = r.Note
    FROM dbo.RentUnits u JOIN @Units r ON u.Id = r.Id AND u.Building = r.Building
    WHERE r.[Action] = 'U';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.RentBuildings WHERE Id IN (SELECT Id FROM @Buildings WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.RentBuildings_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.RentBuildings_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.RentBuildingSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.RentBuildingSaveBatchRow TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.RentUnitRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.RentUnitRow TO MyMoneyTest;
GO
