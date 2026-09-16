-- TransactionExtras_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the TransactionExtras table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT [Id], [Transaction], [TaxYear], [TaxDate] FROM dbo.TransactionExtras ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_Insert
    @Id INT, @Transaction BIGINT, @TaxYear INT, @TaxDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.TransactionExtras ([Id], [Transaction], [TaxYear], [TaxDate]) VALUES (@Id, @Transaction, @TaxYear, @TaxDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_Update
    @Id INT, @Transaction BIGINT, @TaxYear INT, @TaxDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.TransactionExtras SET [Transaction] = @Transaction, [TaxYear] = @TaxYear, [TaxDate] = @TaxDate WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.TransactionExtras_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.TransactionExtras WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.TransactionExtras_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.TransactionExtras_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.TransactionExtras_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.TransactionExtras_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.TransactionExtras_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.TransactionExtras_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.TransactionExtras_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.TransactionExtras_Delete TO MyMoneyTest;
GO
