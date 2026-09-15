-- 001_CreatePayeesTable.sql
-- Run as the MyMoneyAdmin login against the MyMoney database.

USE MyMoney;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Payees')
BEGIN
    CREATE TABLE dbo.Payees (
        [Id]   INT           NOT NULL PRIMARY KEY,
        [Name] NVARCHAR(255) NOT NULL
    );
END
GO
