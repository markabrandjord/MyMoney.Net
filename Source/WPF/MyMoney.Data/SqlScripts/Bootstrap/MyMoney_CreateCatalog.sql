-- Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_CreateCatalog.sql
--
-- Permanent, idempotent, callable as 'MyMoneyAdmin' (granted dbcreator by
-- MyMoney_BootstrapServer). Creates one catalog by name -- production, a
-- test catalog, or any future catalog, no per-catalog script variant
-- needed -- and creates all three logins as database users inside it with
-- MyMoneyAdmin as db_owner. QUOTENAME on @CatalogName specifically to
-- avoid SQL injection through the parameter, per issue #32's design spec.
USE master;
GO

CREATE OR ALTER PROCEDURE dbo.MyMoney_CreateCatalog
    @CatalogName NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @CatalogName)
    BEGIN
        SET @sql = N'CREATE DATABASE ' + QUOTENAME(@CatalogName) + N';';
        EXEC (@sql);
    END

    SET @sql = N'
        USE ' + QUOTENAME(@CatalogName) + N';
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyAdmin'')
            CREATE USER [MyMoneyAdmin] FOR LOGIN [MyMoneyAdmin];
        IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
                        JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
                        JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                        WHERE r.name = N''db_owner'' AND m.name = N''MyMoneyAdmin'')
            ALTER ROLE db_owner ADD MEMBER [MyMoneyAdmin];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyUser'')
            CREATE USER [MyMoneyUser] FOR LOGIN [MyMoneyUser];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyTest'')
            CREATE USER [MyMoneyTest] FOR LOGIN [MyMoneyTest];
    ';
    EXEC (@sql);
END
GO
