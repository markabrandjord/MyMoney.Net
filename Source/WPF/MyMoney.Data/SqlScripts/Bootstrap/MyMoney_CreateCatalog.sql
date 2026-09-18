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

    -- MyMoneyAdmin is checked by SID, not by name: the login that runs
    -- CREATE DATABASE automatically becomes that database's owner with an
    -- IMPLICIT 'dbo' user mapping -- confirmed live against Redmond,
    -- CREATE USER [MyMoneyAdmin] FOR LOGIN [MyMoneyAdmin] then fails with
    -- "The login already has an account with the user name 'dbo'." A
    -- name-based EXISTS check doesn't see that mapping (it's named 'dbo',
    -- not 'MyMoneyAdmin'), so it tries to create a second one for the same
    -- login, which SQL Server disallows. A SID-based check catches the
    -- implicit mapping regardless of what name it was created under, and
    -- CREATE USER + ALTER ROLE only run together when no principal is
    -- mapped to that SID under ANY name yet. If MyMoneyAdmin already has a
    -- mapping (as an explicit user from a prior run, or implicitly as
    -- 'dbo' -- see note above), it either already has db_owner rights or
    -- (as dbo) something strictly stronger, so no further action is
    -- needed or possible: ALTER ROLE db_owner ADD MEMBER [MyMoneyAdmin]
    -- would itself fail the same way CREATE USER did, since no principal
    -- is ever created under the literal name 'MyMoneyAdmin' in that case.
    SET @sql = N'
        USE ' + QUOTENAME(@CatalogName) + N';
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID(N''MyMoneyAdmin''))
        BEGIN
            CREATE USER [MyMoneyAdmin] FOR LOGIN [MyMoneyAdmin];
            ALTER ROLE db_owner ADD MEMBER [MyMoneyAdmin];
        END
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID(N''MyMoneyUser''))
            CREATE USER [MyMoneyUser] FOR LOGIN [MyMoneyUser];
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE sid = SUSER_SID(N''MyMoneyTest''))
            CREATE USER [MyMoneyTest] FOR LOGIN [MyMoneyTest];
    ';
    EXEC (@sql);
END
GO

-- GRANT ... TO [MyMoneyAdmin] below needs a database PRINCIPAL in master,
-- not just the server login MyMoney_BootstrapServer created -- confirmed
-- live against Redmond: without this, the GRANT itself fails with "Cannot
-- find the user 'MyMoneyAdmin', because it does not exist or you do not
-- have permission." A server login has no implicit database user anywhere
-- except its default database (which is not master here).
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'MyMoneyAdmin')
BEGIN
    CREATE USER [MyMoneyAdmin] FOR LOGIN [MyMoneyAdmin];
END
GO

-- Deployed as 'sa' (see SqlServerBootstrapper.BootstrapServerIfNeeded),
-- but called as 'MyMoneyAdmin' from SqlServerBootstrapper.CreateCatalog --
-- CREATE OR ALTER above doesn't imply EXECUTE rights for anyone but the
-- owner. Confirmed live against Redmond: without this grant, MyMoneyAdmin
-- (which only has the dbcreator server role, not sysadmin) gets
-- "The EXECUTE permission was denied on the object 'MyMoney_CreateCatalog'".
GRANT EXECUTE ON dbo.MyMoney_CreateCatalog TO [MyMoneyAdmin];
GO
