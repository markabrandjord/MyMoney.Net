-- Source/WPF/MyMoney.Data/SqlScripts/Bootstrap/MyMoney_BootstrapServer.sql
--
-- Permanent, idempotent, callable as 'sa'. Creates/updates the three
-- server-level logins and grants MyMoneyAdmin the dbcreator server role so
-- it can run MyMoney_CreateCatalog itself afterward. Called once per
-- server, ever, by SqlServerBootstrapper.BootstrapServerIfNeeded -- never
-- dropped after use, unlike the temp-proc pattern it replaces.
USE master;
GO

CREATE OR ALTER PROCEDURE dbo.MyMoney_BootstrapServer
    @AdminPassword NVARCHAR(128),
    @UserPassword  NVARCHAR(128),
    @TestPassword  NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @sql NVARCHAR(MAX);

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyAdmin')
    BEGIN
        SET @sql = N'CREATE LOGIN [MyMoneyAdmin] WITH PASSWORD = ' + QUOTENAME(@AdminPassword, N'''') + N', CHECK_POLICY = OFF;';
        EXEC (@sql);
    END
    ELSE
    BEGIN
        SET @sql = N'ALTER LOGIN [MyMoneyAdmin] WITH PASSWORD = ' + QUOTENAME(@AdminPassword, N'''') + N';';
        EXEC (@sql);
    END
    IF NOT EXISTS (SELECT 1 FROM sys.server_role_members rm
                   JOIN sys.server_principals r ON rm.role_principal_id = r.principal_id
                   JOIN sys.server_principals m ON rm.member_principal_id = m.principal_id
                   WHERE r.name = N'dbcreator' AND m.name = N'MyMoneyAdmin')
    BEGIN
        ALTER SERVER ROLE dbcreator ADD MEMBER [MyMoneyAdmin];
    END

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyUser')
    BEGIN
        SET @sql = N'CREATE LOGIN [MyMoneyUser] WITH PASSWORD = ' + QUOTENAME(@UserPassword, N'''') + N', CHECK_POLICY = OFF;';
        EXEC (@sql);
    END
    ELSE
    BEGIN
        SET @sql = N'ALTER LOGIN [MyMoneyUser] WITH PASSWORD = ' + QUOTENAME(@UserPassword, N'''') + N';';
        EXEC (@sql);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyTest')
    BEGIN
        SET @sql = N'CREATE LOGIN [MyMoneyTest] WITH PASSWORD = ' + QUOTENAME(@TestPassword, N'''') + N', CHECK_POLICY = OFF;';
        EXEC (@sql);
    END
    ELSE
    BEGIN
        SET @sql = N'ALTER LOGIN [MyMoneyTest] WITH PASSWORD = ' + QUOTENAME(@TestPassword, N'''') + N';';
        EXEC (@sql);
    END
END
GO
