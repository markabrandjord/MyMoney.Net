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
    SELECT Id, AccountId, Date, Principal, Interest, Memo FROM dbo.LoanPayments ORDER BY Id;
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
