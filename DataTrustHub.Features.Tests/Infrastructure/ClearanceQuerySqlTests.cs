using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.ClearanceSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC3 and AC4 of #95 step 3b on a relational provider: the organization scope of the clearance queries
// (a subquery on the policies) must translate to SQL, which EF InMemory does not check
public class ClearanceQuerySqlTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    [Fact]
    public async Task ListClearances_OnSqlite_ReturnsOnlyClearancesOnThisOrganizationsPolicies()
    {
        var grant = await CreateGrantScenarioAsync(factory, "sql_list_scope");
        var clearanceHere = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "sql_list_scope_other");
        var foreignPolicyId = await CreatePolicyAsync(other.OrgAdmin, "sql_list_scope other policy", other.OrganizationId);
        await AddMemberAndGrantClearanceAsync(
            other.OrgAdmin, other.OrganizationId, grant.MemberId, foreignPolicyId, DefaultLevelName, DefaultLevelPriority);

        var all = await ListAsync(grant.OrgAdmin, grant.OrganizationId, $"?userId={grant.MemberId}");
        var foreign = await ListAsync(grant.OrgAdmin, grant.OrganizationId, $"?policyId={foreignPolicyId}");

        Assert.Equal(clearanceHere, Assert.Single(all).Id);
        Assert.Empty(foreign);
    }

    [Fact]
    public async Task GetClearance_OnSqlite_ClearanceOfAnotherOrganization_Returns404()
    {
        var grant = await CreateGrantScenarioAsync(factory, "sql_get_scope");
        var clearanceHere = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "sql_get_scope_other");
        var foreignPolicyId = await CreatePolicyAsync(other.OrgAdmin, "sql_get_scope other policy", other.OrganizationId);
        var foreignClearance = await AddMemberAndGrantClearanceAsync(
            other.OrgAdmin, other.OrganizationId, grant.MemberId, foreignPolicyId, DefaultLevelName, DefaultLevelPriority);

        var here = await grant.OrgAdmin.GetAsync(ClearanceEndpoint(grant.OrganizationId, clearanceHere));
        var elsewhere = await grant.OrgAdmin.GetAsync(ClearanceEndpoint(grant.OrganizationId, foreignClearance));

        Assert.Equal(HttpStatusCode.OK, here.StatusCode);
        await ErrorAssert.HasErrorAsync(
            elsewhere, HttpStatusCode.NotFound, AdminErrorCodes.ClearanceNotFound, AdminErrorMessages.ClearanceNotFound);
    }

    private static async Task<List<ClearanceResponse>> ListAsync(HttpClient caller, Guid organizationId, string queryString) =>
        (await caller.GetFromJsonAsync<List<ClearanceResponse>>($"{ClearancesEndpoint(organizationId)}{queryString}"))!;

    private record ClearanceResponse(Guid Id, Guid UserId, Guid PolicyId);
}
