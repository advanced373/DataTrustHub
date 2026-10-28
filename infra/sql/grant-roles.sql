/*
    DataTrustHub: database users and roles for production (ADR-0018 §4, ADR-0021).

    Run after infra/setup.ps1, connected to the application database (not master) as the
    Microsoft Entra admin of the SQL server, for example from the portal query editor.

    It creates contained users FROM EXTERNAL PROVIDER (Microsoft Entra principals):
      - the API's system-assigned managed identity: db_datareader, db_datawriter (no DDL);
      - the deploy identity (GitHub OIDC federated credential, #102): db_ddladmin, db_datareader,
        db_datawriter, to apply migrations.

    The deploy identity is created later, in #102, so it is optional: while @DeployPrincipal is
    empty or still the placeholder, the script grants the API user only and says the deploy user
    was skipped. Run it once now, and again after #102 with @DeployPrincipal filled in.

    Idempotent: every user and role membership is guarded, so a second run changes nothing.
    One batch, no GO, so the variables below stay in scope.
*/

-- ---------------------------------------------------------------------------------------------
-- Fill in before running
-- ---------------------------------------------------------------------------------------------
-- Display name of the API's system-assigned managed identity: the name of the API web app.
DECLARE @ApiPrincipal sysname = N'datatrusthubapi';
-- Display name of the deploy identity (the app registration or user-assigned managed identity
-- that holds the GitHub OIDC federated credential). Leave the placeholder until #102 creates it.
DECLARE @DeployPrincipal sysname = N'<deploy identity name>';

-- ---------------------------------------------------------------------------------------------
-- Checks
-- ---------------------------------------------------------------------------------------------
DECLARE @DeployPlaceholder sysname = N'<deploy identity name>';
DECLARE @MasterDatabase sysname = N'master';

IF DB_NAME() = @MasterDatabase
    THROW 50001, N'Connect to the application database, not master.', 1;

IF @ApiPrincipal IS NULL OR LEN(@ApiPrincipal) = 0
    THROW 50002, N'Fill in @ApiPrincipal with the display name of the API''s managed identity.', 1;

DECLARE @IncludeDeployPrincipal bit = CASE
    WHEN @DeployPrincipal IS NULL OR LEN(@DeployPrincipal) = 0 OR @DeployPrincipal = @DeployPlaceholder THEN 0
    ELSE 1
END;

-- ---------------------------------------------------------------------------------------------
-- Grants: one row per principal and role
-- ---------------------------------------------------------------------------------------------
DECLARE @Grants TABLE (GrantId int IDENTITY(1, 1) PRIMARY KEY, PrincipalName sysname, RoleName sysname);
INSERT INTO @Grants (PrincipalName, RoleName)
VALUES (@ApiPrincipal, N'db_datareader'),
       (@ApiPrincipal, N'db_datawriter');

IF @IncludeDeployPrincipal = 1
    INSERT INTO @Grants (PrincipalName, RoleName)
    VALUES (@DeployPrincipal, N'db_ddladmin'),
           (@DeployPrincipal, N'db_datareader'),
           (@DeployPrincipal, N'db_datawriter');
ELSE
    PRINT N'Skipped the deploy user: @DeployPrincipal is not filled in. Run this script again after #102.';

DECLARE @GrantId int = 1;
DECLARE @GrantCount int = (SELECT COUNT(*) FROM @Grants);
DECLARE @PrincipalName sysname;
DECLARE @RoleName sysname;
DECLARE @Statement nvarchar(max);

WHILE @GrantId <= @GrantCount
BEGIN
    SELECT @PrincipalName = PrincipalName, @RoleName = RoleName FROM @Grants WHERE GrantId = @GrantId;

    -- The contained user, created from the Microsoft Entra principal if missing
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @PrincipalName)
    BEGIN
        SET @Statement = N'CREATE USER ' + QUOTENAME(@PrincipalName) + N' FROM EXTERNAL PROVIDER;';
        EXEC sys.sp_executesql @Statement;
        PRINT N'Created user ' + @PrincipalName + N'.';
    END;

    -- The role membership, added if missing
    IF NOT EXISTS (
        SELECT 1
        FROM sys.database_role_members AS roleMembers
        JOIN sys.database_principals AS roles ON roles.principal_id = roleMembers.role_principal_id
        JOIN sys.database_principals AS members ON members.principal_id = roleMembers.member_principal_id
        WHERE roles.name = @RoleName AND members.name = @PrincipalName)
    BEGIN
        SET @Statement = N'ALTER ROLE ' + QUOTENAME(@RoleName) + N' ADD MEMBER ' + QUOTENAME(@PrincipalName) + N';';
        EXEC sys.sp_executesql @Statement;
        PRINT N'Added ' + @PrincipalName + N' to ' + @RoleName + N'.';
    END;

    SET @GrantId += 1;
END;

IF @IncludeDeployPrincipal = 1
    PRINT N'Done. The API user and the deploy user are in place.';
ELSE
    PRINT N'Done. The API user is in place; the deploy user was skipped.';
