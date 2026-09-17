-- LoanPayments_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the LoanPayments table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, AccountId, Date, Principal, Interest, Memo, Version FROM dbo.LoanPayments ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_Insert
    @Id INT, @AccountId INT, @Date DATETIME, @Principal MONEY, @Interest MONEY, @Memo NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.LoanPayments (Id, AccountId, Date, Principal, Interest, Memo) VALUES (@Id, @AccountId, @Date, @Principal, @Interest, @Memo);
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_Update
    @Id INT, @AccountId INT, @Date DATETIME, @Principal MONEY, @Interest MONEY, @Memo NVARCHAR(255)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.LoanPayments SET AccountId = @AccountId, Date = @Date, Principal = @Principal, Interest = @Interest, Memo = @Memo WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_Delete
    @Id INT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.LoanPayments WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.LoanPayments_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.LoanPayments_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.LoanPayments_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.LoanPayments_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.LoanPayments_Delete TO MyMoneyTest;
GO

IF TYPE_ID(N'dbo.LoanPaymentSaveBatchRow') IS NULL
BEGIN
    EXEC('CREATE TYPE dbo.LoanPaymentSaveBatchRow AS TABLE (
        [Action] CHAR(1) NOT NULL,
        Id INT NOT NULL,
        AccountId INT NULL,
        Date DATETIME NULL,
        Principal MONEY NULL,
        Interest MONEY NULL,
        Memo NVARCHAR(255) NULL,
        ExpectedVersion BIGINT NULL
    )');
END
GO

CREATE OR ALTER PROCEDURE dbo.LoanPayments_SaveBatch
    @Rows dbo.LoanPaymentSaveBatchRow READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;

    IF EXISTS (
        SELECT 1 FROM @Rows r
        LEFT JOIN dbo.LoanPayments c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion)
    )
    BEGIN
        SELECT r.Id, ISNULL(c.Version, -1) AS StoredVersion, r.ExpectedVersion AS CallerVersion, 'CONFLICT' AS Result
        FROM @Rows r LEFT JOIN dbo.LoanPayments c ON c.Id = r.Id
        WHERE r.[Action] IN ('U','D') AND (c.Id IS NULL OR c.Version <> r.ExpectedVersion);
        ROLLBACK TRANSACTION;
        RETURN;
    END

    INSERT INTO dbo.LoanPayments (Id, AccountId, Date, Principal, Interest, Memo, Version)
    SELECT Id, AccountId, Date, Principal, Interest, Memo, 1 FROM @Rows WHERE [Action] = 'I';

    UPDATE c SET AccountId = r.AccountId, Date = r.Date, Principal = r.Principal, Interest = r.Interest,
        Memo = r.Memo, Version = c.Version + 1
    FROM dbo.LoanPayments c JOIN @Rows r ON c.Id = r.Id
    WHERE r.[Action] = 'U';

    DELETE c FROM dbo.LoanPayments c JOIN @Rows r ON c.Id = r.Id WHERE r.[Action] = 'D';

    COMMIT TRANSACTION;

    SELECT Id, Version AS NewVersion, 'OK' AS Result
    FROM dbo.LoanPayments WHERE Id IN (SELECT Id FROM @Rows WHERE [Action] IN ('I','U'));
END
GO

GRANT EXECUTE ON dbo.LoanPayments_SaveBatch TO MyMoneyUser;
GRANT EXECUTE ON dbo.LoanPayments_SaveBatch TO MyMoneyTest;
GRANT EXECUTE ON TYPE::dbo.LoanPaymentSaveBatchRow TO MyMoneyUser;
GRANT EXECUTE ON TYPE::dbo.LoanPaymentSaveBatchRow TO MyMoneyTest;
GO
