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
           ReconciledDate, BudgetBalanceDate, MergeDate, OriginalPayee, Transfer, TransferSplit, Version
    FROM dbo.Transactions ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_Insert
    @Id BIGINT, @Number NVARCHAR(10), @Account INT, @Date DATETIME, @Amount MONEY, @Status INT,
    @Memo NVARCHAR(255), @Payee INT, @Category INT, @Transfer BIGINT, @TransferSplit INT,
    @FITID NVARCHAR(40), @SalesTax MONEY, @Flags INT, @ReconciledDate DATETIME,
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
    @Id BIGINT, @Number NVARCHAR(10), @Account INT, @Date DATETIME, @Amount MONEY, @Status INT,
    @Memo NVARCHAR(255), @Payee INT, @Category INT, @Transfer BIGINT, @TransferSplit INT,
    @FITID NVARCHAR(40), @SalesTax MONEY, @Flags INT, @ReconciledDate DATETIME,
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

IF TYPE_ID(N'dbo.TransactionSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.TransactionSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id BIGINT NOT NULL,
        Number NVARCHAR(10) NULL,
        Account INT NULL,
        Date DATETIME NULL,
        Amount MONEY NULL,
        Status INT NULL,
        Memo NVARCHAR(255) NULL,
        Payee INT NULL,
        Category INT NULL,
        FITID NVARCHAR(40) NULL,
        SalesTax MONEY NULL,
        Flags INT NULL,
        ReconciledDate DATETIME NULL,
        BudgetBalanceDate DATETIME NULL,
        MergeDate DATETIME NULL,
        OriginalPayee NVARCHAR(255) NULL,
        Transfer BIGINT NULL,
        TransferSplit INT NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

IF TYPE_ID(N'dbo.SplitRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.SplitRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        [Transaction] BIGINT NOT NULL,
        Amount MONEY NULL,
        Category INT NULL,
        Memo NVARCHAR(255) NULL,
        Transfer BIGINT NULL,
        Payee INT NULL,
        Flags INT NULL,
        BudgetBalanceDate DATETIME NULL
    )');
END
GO

IF TYPE_ID(N'dbo.InvestmentRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.InvestmentRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id BIGINT NOT NULL,
        Security INT NULL,
        UnitPrice MONEY NULL,
        Units MONEY NULL,
        Commission MONEY NULL,
        InvestmentType INT NULL,
        TradeType INT NULL,
        TaxExempt BIT NULL,
        Withholding MONEY NULL,
        MarkUpDown MONEY NULL,
        Taxes MONEY NULL,
        Fees MONEY NULL,
        [Load] MONEY NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.Transactions_SaveBatch
    @Transactions dbo.TransactionSaveBatchRow READONLY,
    @Splits dbo.SplitRow READONLY,
    @Investments dbo.InvestmentRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Transactions r
        LEFT JOIN dbo.Transactions t ON t.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (t.Id IS NULL OR t.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(t.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Transactions r LEFT JOIN dbo.Transactions t ON t.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (t.Id IS NULL OR t.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    -- Children before parent on delete.
    DELETE s FROM dbo.Splits s JOIN @Splits r ON s.Id = r.Id AND s.[Transaction] = r.[Transaction] WHERE r.[Action] = 'D';
    DELETE i FROM dbo.Investments i JOIN @Investments r ON i.Id = r.Id WHERE r.[Action] = 'D';
    DELETE t FROM dbo.Transactions t JOIN @Transactions r ON t.Id = r.Id WHERE r.[Action] = 'D';

    -- Parent before children on insert. Transfer/TransferSplit inserted NULL first - see this
    -- task's own note on why, above.
    INSERT INTO dbo.Transactions (Id, Number, Account, Date, Amount, Status, Memo, Payee, Category,
        Transfer, TransferSplit, FITID, SalesTax, Flags, ReconciledDate, BudgetBalanceDate, MergeDate,
        OriginalPayee, Version)
    SELECT Id, Number, Account, Date, Amount, Status, Memo, Payee, Category, NULL, NULL, FITID,
        SalesTax, Flags, ReconciledDate, BudgetBalanceDate, MergeDate, OriginalPayee, 1
    FROM @Transactions WHERE [Action] = 'I';

    UPDATE t SET Transfer = r.Transfer, TransferSplit = r.TransferSplit
    FROM dbo.Transactions t JOIN @Transactions r ON t.Id = r.Id
    WHERE r.[Action] = 'I' AND r.Transfer IS NOT NULL;

    UPDATE t SET
        Number = r.Number, Account = r.Account, Date = r.Date, Amount = r.Amount, Status = r.Status,
        Memo = r.Memo, Payee = r.Payee, Category = r.Category, Transfer = r.Transfer,
        TransferSplit = r.TransferSplit, FITID = r.FITID, SalesTax = r.SalesTax, Flags = r.Flags,
        ReconciledDate = r.ReconciledDate, BudgetBalanceDate = r.BudgetBalanceDate, MergeDate = r.MergeDate,
        OriginalPayee = r.OriginalPayee, Version = t.Version + 1
    FROM dbo.Transactions t JOIN @Transactions r ON t.Id = r.Id
    WHERE r.[Action] = 'U';

    INSERT INTO dbo.Splits (Id, [Transaction], Amount, Category, Memo, Transfer, Payee, Flags, BudgetBalanceDate)
    SELECT Id, [Transaction], Amount, Category, Memo, NULL, Payee, Flags, BudgetBalanceDate
    FROM @Splits WHERE [Action] = 'I';

    UPDATE s SET Transfer = r.Transfer
    FROM dbo.Splits s JOIN @Splits r ON s.Id = r.Id AND s.[Transaction] = r.[Transaction]
    WHERE r.[Action] = 'I' AND r.Transfer IS NOT NULL;

    UPDATE s SET Amount = r.Amount, Category = r.Category, Memo = r.Memo, Transfer = r.Transfer,
        Payee = r.Payee, Flags = r.Flags, BudgetBalanceDate = r.BudgetBalanceDate
    FROM dbo.Splits s JOIN @Splits r ON s.Id = r.Id AND s.[Transaction] = r.[Transaction]
    WHERE r.[Action] = 'U';

    INSERT INTO dbo.Investments (Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType,
        TaxExempt, Withholding, MarkUpDown, Taxes, Fees, [Load])
    SELECT Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType, TaxExempt, Withholding,
        MarkUpDown, Taxes, Fees, [Load]
    FROM @Investments WHERE [Action] = 'I';

    UPDATE i SET Security = r.Security, UnitPrice = r.UnitPrice, Units = r.Units, Commission = r.Commission,
        InvestmentType = r.InvestmentType, TradeType = r.TradeType, TaxExempt = r.TaxExempt,
        Withholding = r.Withholding, MarkUpDown = r.MarkUpDown, Taxes = r.Taxes, Fees = r.Fees, [Load] = r.[Load]
    FROM dbo.Investments i JOIN @Investments r ON i.Id = r.Id
    WHERE r.[Action] = 'U';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.Transactions WHERE Id IN (SELECT Id FROM @Transactions WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.Transactions_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.Transactions_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.TransactionSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.TransactionSaveBatchRow TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.SplitRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.SplitRow TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.InvestmentRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.InvestmentRow TO MyMoneyTest;
GO
