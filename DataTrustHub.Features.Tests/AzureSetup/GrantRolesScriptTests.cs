using System.Text.RegularExpressions;
using DataTrustHub.Features.Tests.Build.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.AzureSetup;

// #101 AC3: infra/sql/grant-roles.sql creates contained users from Microsoft Entra, guards every user and role
// membership with IF NOT EXISTS, names the API principal datatrusthubapi by default and keeps the deploy
// principal optional. Its T-SQL behaviour needs a SQL Server, so it is checked by hand, not here.
public class GrantRolesScriptTests
{
    private const string GrantRolesScriptFile = "infra/sql/grant-roles.sql";
    private const string ExternalProvider = "FROM EXTERNAL PROVIDER";
    private const string DdlAdminRole = "db_ddladmin";

    private static readonly Regex ApiPrincipalDeclaration = new(
        @"DECLARE\s+@ApiPrincipal\s+sysname\s*=\s*N'datatrusthubapi'\s*;", RegexOptions.IgnoreCase);

    private static readonly Regex DeployPrincipalDeclaration = new(
        @"DECLARE\s+@DeployPrincipal\s+sysname\s*=", RegexOptions.IgnoreCase);

    private static readonly Regex UserGuard = new(
        @"IF\s+NOT\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+sys\.database_principals\b", RegexOptions.IgnoreCase);

    private static readonly Regex RoleMembershipGuard = new(
        @"IF\s+NOT\s+EXISTS\s*\(\s*SELECT\s+1\s+FROM\s+sys\.database_role_members\b", RegexOptions.IgnoreCase);

    // A GO separator would end the batch and drop the principal variables declared at the top
    private static readonly Regex BatchSeparator = new(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // A login lives in master; the users must be contained in the application database
    private static readonly Regex CreateLogin = new(@"\bCREATE\s+LOGIN\b", RegexOptions.IgnoreCase);

    [Fact]
    public void GrantRolesScript_CreatesUsersFromExternalProvider()
    {
        Assert.Contains(ExternalProvider, Script(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GrantRolesScript_CreatesNoLogin()
    {
        Assert.DoesNotMatch(CreateLogin, Script());
    }

    [Fact]
    public void GrantRolesScript_GuardsUserCreation_WithIfNotExists()
    {
        Assert.Matches(UserGuard, Script());
    }

    [Fact]
    public void GrantRolesScript_GuardsRoleMembership_WithIfNotExists()
    {
        Assert.Matches(RoleMembershipGuard, Script());
    }

    [Fact]
    public void GrantRolesScript_ApiPrincipal_DefaultsToApiWebAppName()
    {
        Assert.Matches(ApiPrincipalDeclaration, Script());
    }

    [Fact]
    public void GrantRolesScript_DeclaresDeployPrincipalVariable()
    {
        Assert.Matches(DeployPrincipalDeclaration, Script());
    }

    [Fact]
    public void GrantRolesScript_GrantsDdlAdmin_ForTheDeployUser()
    {
        Assert.Contains(DdlAdminRole, Script(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GrantRolesScript_IsOneBatch_WithoutGo()
    {
        Assert.DoesNotMatch(BatchSeparator, Script());
    }

    private static string Script() => SolutionFiles.ReadText(GrantRolesScriptFile);
}
