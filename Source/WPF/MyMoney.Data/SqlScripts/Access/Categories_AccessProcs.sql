-- Categories_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Categories table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Categories_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum
    FROM dbo.Categories ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Insert
    @Id INT, @Name NVARCHAR(255), @Description NVARCHAR(255), @Type INT, @ParentId INT,
    @Budget MONEY, @Frequency INT, @Balance MONEY, @Color NVARCHAR(20), @TaxRefNum INT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Categories (Id, Name, Description, Type, ParentId, Budget, Frequency, Balance, Color, TaxRefNum)
    VALUES (@Id, @Name, @Description, @Type, @ParentId, @Budget, @Frequency, @Balance, @Color, @TaxRefNum);
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Update
    @Id INT, @Name NVARCHAR(255), @Description NVARCHAR(255), @Type INT, @ParentId INT,
    @Budget MONEY, @Frequency INT, @Balance MONEY, @Color NVARCHAR(20), @TaxRefNum INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Categories SET
        Name = @Name, Description = @Description, Type = @Type, ParentId = @ParentId, Budget = @Budget,
        Frequency = @Frequency, Balance = @Balance, Color = @Color, TaxRefNum = @TaxRefNum
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Categories_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Categories WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Categories_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Categories_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Categories_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Categories_Delete TO MyMoneyTest;
GO
