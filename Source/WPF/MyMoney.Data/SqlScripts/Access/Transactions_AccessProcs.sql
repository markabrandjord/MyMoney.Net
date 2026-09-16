-- Transactions_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Transactions table -- neither has any direct table grants.
-- Splits and Investments are separate tables with their own access-proc
-- scripts (Splits_AccessProcs.sql, Investments_AccessProcs.sql) -- see the
-- design spec for issue #22 for why these stay three independent proc sets.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Number, Date, Amount, Account, Status, Memo, Payee, Category, FITID, SalesTax, Flags,
           ReconciledDate, BudgetBalanceDate, MergeDate, OriginalPayee, Transfer, TransferSplit
    FROM dbo.Transactions ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_Insert
    @Id BIGINT, @Number NVARCHAR(20), @Account INT, @Date DATETIME, @Amount MONEY, @Status INT,
    @Memo NVARCHAR(255), @Payee INT, @Category INT, @Transfer BIGINT, @TransferSplit INT,
    @FITID NVARCHAR(50), @SalesTax MONEY, @Flags INT, @ReconciledDate DATETIME,
    @BudgetBalanceDate DATETIME, @MergeDate DATETIME, @OriginalPayee NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Transactions (Id, Number, Account, Date, Amount, Status, Memo, Payee, Category,
        Transfer, TransferSplit, FITID, SalesTax, Flags, ReconciledDate, BudgetBalanceDate, MergeDate, OriginalPayee)
    VALUES (@Id, @Number, @Account, @Date, @Amount, @Status, @Memo, @Payee, @Category, @Transfer,
        @TransferSplit, @FITID, @SalesTax, @Flags, @ReconciledDate, @BudgetBalanceDate, @MergeDate, @OriginalPayee);
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_Update
    @Id BIGINT, @Number NVARCHAR(20), @Account INT, @Date DATETIME, @Amount MONEY, @Status INT,
    @Memo NVARCHAR(255), @Payee INT, @Category INT, @Transfer BIGINT, @TransferSplit INT,
    @FITID NVARCHAR(50), @SalesTax MONEY, @Flags INT, @ReconciledDate DATETIME,
    @BudgetBalanceDate DATETIME, @MergeDate DATETIME, @OriginalPayee NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Transactions SET
        Number = @Number, Account = @Account, Date = @Date, Amount = @Amount, Status = @Status,
        Memo = @Memo, Payee = @Payee, Category = @Category, Transfer = @Transfer, TransferSplit = @TransferSplit,
        FITID = @FITID, SalesTax = @SalesTax, Flags = @Flags, ReconciledDate = @ReconciledDate,
        BudgetBalanceDate = @BudgetBalanceDate, MergeDate = @MergeDate, OriginalPayee = @OriginalPayee
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_Delete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Transactions WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Transactions_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Transactions_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Transactions_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Transactions_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Transactions_Delete TO MyMoneyTest;
GO
