-- CreateDatabaseAndLogins.sql
-- Run as 'sa' against the 'master' database.
-- Creates the MyMoney database and the three tiered SQL logins, then
-- removes itself. Passwords are substituted by MyMoneyAdmin before this
-- script is sent to the server (see BootstrapRunner.cs) -- the
-- placeholders below are never executed literally.

USE master;
GO

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.MyMoney_Bootstrap') AND type = N'P')
    DROP PROCEDURE dbo.MyMoney_Bootstrap;
GO

CREATE PROCEDURE dbo.MyMoney_Bootstrap
    @AdminPassword NVARCHAR(128),
    @UserPassword NVARCHAR(128),
    @TestPassword NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'MyMoney')
    BEGIN
        -- Note: some SQL Server versions restrict CREATE DATABASE to being
        -- the only statement in its batch. If this procedure fails on your
        -- target server with that error, run this CREATE DATABASE statement
        -- ad hoc first, then re-run the rest of this script with the
        -- database-creation block commented out.
        CREATE DATABASE MyMoney;
    END

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyAdmin')
        CREATE LOGIN MyMoneyAdmin WITH PASSWORD = @AdminPassword, CHECK_POLICY = ON;
    ELSE
        ALTER LOGIN MyMoneyAdmin WITH PASSWORD = @AdminPassword;

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyUser')
        CREATE LOGIN MyMoneyUser WITH PASSWORD = @UserPassword, CHECK_POLICY = ON;
    ELSE
        ALTER LOGIN MyMoneyUser WITH PASSWORD = @UserPassword;

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyTest')
        CREATE LOGIN MyMoneyTest WITH PASSWORD = @TestPassword, CHECK_POLICY = ON;
    ELSE
        ALTER LOGIN MyMoneyTest WITH PASSWORD = @TestPassword;
END
GO

EXEC dbo.MyMoney_Bootstrap
    @AdminPassword = N'{{AdminPassword}}',
    @UserPassword  = N'{{UserPassword}}',
    @TestPassword  = N'{{TestPassword}}';
GO

DROP PROCEDURE dbo.MyMoney_Bootstrap;
GO
