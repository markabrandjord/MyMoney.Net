-- Splits_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Splits table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Splits_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, [Transaction], Amount, Category, Memo, Transfer, Payee, Flags, BudgetBalanceDate
    FROM dbo.Splits ORDER BY [Transaction], Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Splits_Insert
    @Id INT, @Transaction BIGINT, @Amount MONEY, @Category INT, @Memo NVARCHAR(255),
    @Transfer BIGINT, @Payee INT, @Flags INT, @BudgetBalanceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Splits (Id, [Transaction], Amount, Category, Memo, Transfer, Payee, Flags, BudgetBalanceDate)
    VALUES (@Id, @Transaction, @Amount, @Category, @Memo, @Transfer, @Payee, @Flags, @BudgetBalanceDate);
END
GO

CREATE OR ALTER PROCEDURE dbo.Splits_Update
    @Id INT, @Transaction BIGINT, @Amount MONEY, @Category INT, @Memo NVARCHAR(255),
    @Transfer BIGINT, @Payee INT, @Flags INT, @BudgetBalanceDate DATETIME
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Splits SET
        Amount = @Amount, Category = @Category, Memo = @Memo, Transfer = @Transfer, Payee = @Payee,
        Flags = @Flags, BudgetBalanceDate = @BudgetBalanceDate
    WHERE Id = @Id AND [Transaction] = @Transaction;
END
GO

CREATE OR ALTER PROCEDURE dbo.Splits_Delete
    @Id INT, @Transaction BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Splits WHERE Id = @Id AND [Transaction] = @Transaction;
END
GO

GRANT EXECUTE ON dbo.Splits_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Splits_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Splits_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Splits_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Splits_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Splits_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Splits_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Splits_Delete TO MyMoneyTest;
GO
