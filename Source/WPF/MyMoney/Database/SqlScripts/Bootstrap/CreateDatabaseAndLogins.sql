-- CreateDatabaseAndLogins.sql
-- Run as 'sa' against the 'master' database.
-- Creates the MyMoney database, the three tiered SQL logins, and the
-- corresponding database users inside MyMoney (MyMoneyAdmin as db_owner,
-- MyMoneyUser/MyMoneyTest as plain members), then removes itself.
-- Passwords are substituted by MyMoneyAdmin before this script is sent to
-- the server (see BootstrapRunner.cs) -- the {{...}} placeholders below are
-- never executed literally; by the time this script reaches SQL Server,
-- the EXEC call at the bottom passes real literal password values as the
-- procedure's arguments.
--
-- Why MyMoneyAdmin needs db_owner: schema/access procedures deployed later
-- (as the MyMoneyAdmin login, against the MyMoney database -- see
-- Schema/Access/Test scripts) end up owned by the dbo schema only because
-- MyMoneyAdmin is a db_owner member. That dbo ownership is what makes SQL
-- Server's ownership-chaining rules give MyMoneyUser/MyMoneyTest
-- EXECUTE-only access to the Payees table through those procedures without
-- ever needing a direct table grant. Without db_owner here, the whole
-- tiered-trust model does not work even once the database users exist.
--
-- Why CREATE LOGIN/ALTER LOGIN use dynamic SQL: passing @AdminPassword
-- etc. directly as `WITH PASSWORD = @AdminPassword` inside a procedure body
-- is ambiguous/unreliable across SQL Server versions for CREATE LOGIN's
-- non-query-expression PASSWORD clause. Building the statement as a string
-- and executing it via sp_executesql, with QUOTENAME(@Password, '''') to
-- safely embed the password as a quoted string literal (doubling any
-- embedded quote characters), is the standard, unambiguous pattern for
-- this and removes the ambiguity entirely rather than gambling on it.
--
-- Why CREATE USER needs dynamic SQL too: this procedure executes with
-- 'master' as its database context (it lives in master and is invoked by
-- 'sa' connected to master). CREATE USER must run against MyMoney's own
-- catalog, so the USE MyMoney statement has to be part of the same dynamic
-- batch as the CREATE USER/ALTER ROLE statements that follow it -- a USE
-- inside dynamic SQL only affects that dynamic batch's context, not the
-- calling session's (i.e. it would not "stick" for later statements in
-- this procedure if issued outside the dynamic string).

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

    DECLARE @sql NVARCHAR(MAX);

    ---------------------------------------------------------------------
    -- Server logins: dynamic SQL + QUOTENAME so the password is always
    -- embedded as an unambiguous quoted string literal, never passed as
    -- a bare variable to CREATE LOGIN/ALTER LOGIN's PASSWORD clause.
    ---------------------------------------------------------------------

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyAdmin')
        SET @sql = N'CREATE LOGIN [MyMoneyAdmin] WITH PASSWORD = ' + QUOTENAME(@AdminPassword, N'''') + N', CHECK_POLICY = ON;';
    ELSE
        SET @sql = N'ALTER LOGIN [MyMoneyAdmin] WITH PASSWORD = ' + QUOTENAME(@AdminPassword, N'''') + N';';
    EXEC sp_executesql @sql;

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyUser')
        SET @sql = N'CREATE LOGIN [MyMoneyUser] WITH PASSWORD = ' + QUOTENAME(@UserPassword, N'''') + N', CHECK_POLICY = ON;';
    ELSE
        SET @sql = N'ALTER LOGIN [MyMoneyUser] WITH PASSWORD = ' + QUOTENAME(@UserPassword, N'''') + N';';
    EXEC sp_executesql @sql;

    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'MyMoneyTest')
        SET @sql = N'CREATE LOGIN [MyMoneyTest] WITH PASSWORD = ' + QUOTENAME(@TestPassword, N'''') + N', CHECK_POLICY = ON;';
    ELSE
        SET @sql = N'ALTER LOGIN [MyMoneyTest] WITH PASSWORD = ' + QUOTENAME(@TestPassword, N'''') + N';';
    EXEC sp_executesql @sql;

    ---------------------------------------------------------------------
    -- Database users inside MyMoney, corresponding to the three logins
    -- just created/updated above. Without these, none of the three
    -- logins can open the MyMoney database at all (no database user =
    -- no access to that database, even with a valid server login), and
    -- the GRANT EXECUTE statements in the Access/Test scripts (which
    -- target MyMoneyUser/MyMoneyTest as database principals) would fail
    -- since those principals wouldn't exist yet.
    --
    -- This whole block is built as one dynamic string (USE MyMoney; plus
    -- the CREATE USER/ALTER ROLE statements) and executed as a single
    -- dynamic batch, because a USE statement only changes context for the
    -- dynamic batch it is part of.
    ---------------------------------------------------------------------

    SET @sql = N'
        USE MyMoney;

        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyAdmin'')
            CREATE USER [MyMoneyAdmin] FOR LOGIN [MyMoneyAdmin];

        IF NOT EXISTS (
            SELECT 1
            FROM sys.database_role_members drm
            JOIN sys.database_principals rp ON drm.role_principal_id = rp.principal_id
            JOIN sys.database_principals mp ON drm.member_principal_id = mp.principal_id
            WHERE rp.name = N''db_owner'' AND mp.name = N''MyMoneyAdmin'')
            ALTER ROLE db_owner ADD MEMBER [MyMoneyAdmin];

        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyUser'')
            CREATE USER [MyMoneyUser] FOR LOGIN [MyMoneyUser];

        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''MyMoneyTest'')
            CREATE USER [MyMoneyTest] FOR LOGIN [MyMoneyTest];
    ';
    EXEC (@sql);
END
GO

EXEC dbo.MyMoney_Bootstrap
    @AdminPassword = N'{{AdminPassword}}',
    @UserPassword  = N'{{UserPassword}}',
    @TestPassword  = N'{{TestPassword}}';
GO

DROP PROCEDURE dbo.MyMoney_Bootstrap;
GO
