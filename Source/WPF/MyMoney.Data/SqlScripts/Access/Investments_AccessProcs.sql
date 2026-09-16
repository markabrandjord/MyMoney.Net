-- Investments_AccessProcs.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.
-- These are the only operations MyMoneyUser/MyMoneyTest are permitted to
-- perform on the Investments table -- neither has any direct table grants.

USE MyMoney;
GO

CREATE OR ALTER PROCEDURE dbo.Investments_SelectAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType, TaxExempt,
           Withholding, MarkUpDown, Taxes, Fees, [Load]
    FROM dbo.Investments ORDER BY Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Investments_Insert
    @Id BIGINT, @Security INT, @UnitPrice MONEY, @Units MONEY, @Commission MONEY,
    @InvestmentType INT, @TradeType INT, @TaxExempt BIT, @Withholding MONEY, @MarkUpDown MONEY,
    @Taxes MONEY, @Fees MONEY, @Load MONEY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.Investments (Id, Security, UnitPrice, Units, Commission, InvestmentType, TradeType,
        TaxExempt, Withholding, MarkUpDown, Taxes, Fees, [Load])
    VALUES (@Id, @Security, @UnitPrice, @Units, @Commission, @InvestmentType, @TradeType, @TaxExempt,
        @Withholding, @MarkUpDown, @Taxes, @Fees, @Load);
END
GO

CREATE OR ALTER PROCEDURE dbo.Investments_Update
    @Id BIGINT, @Security INT, @UnitPrice MONEY, @Units MONEY, @Commission MONEY,
    @InvestmentType INT, @TradeType INT, @TaxExempt BIT, @Withholding MONEY, @MarkUpDown MONEY,
    @Taxes MONEY, @Fees MONEY, @Load MONEY
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Investments SET
        Security = @Security, UnitPrice = @UnitPrice, Units = @Units, Commission = @Commission,
        InvestmentType = @InvestmentType, TradeType = @TradeType, TaxExempt = @TaxExempt,
        Withholding = @Withholding, MarkUpDown = @MarkUpDown, Taxes = @Taxes, Fees = @Fees, [Load] = @Load
    WHERE Id = @Id;
END
GO

CREATE OR ALTER PROCEDURE dbo.Investments_Delete
    @Id BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM dbo.Investments WHERE Id = @Id;
END
GO

GRANT EXECUTE ON dbo.Investments_SelectAll TO MyMoneyUser;
GRANT EXECUTE ON dbo.Investments_Insert TO MyMoneyUser;
GRANT EXECUTE ON dbo.Investments_Update TO MyMoneyUser;
GRANT EXECUTE ON dbo.Investments_Delete TO MyMoneyUser;

GRANT EXECUTE ON dbo.Investments_SelectAll TO MyMoneyTest;
GRANT EXECUTE ON dbo.Investments_Insert TO MyMoneyTest;
GRANT EXECUTE ON dbo.Investments_Update TO MyMoneyTest;
GRANT EXECUTE ON dbo.Investments_Delete TO MyMoneyTest;
GO
