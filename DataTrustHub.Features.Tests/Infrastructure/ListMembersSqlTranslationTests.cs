using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC3 of #95 step 2a on a relational provider: the members query (join to users, order by email) must
// translate to SQL, which EF InMemory does not check
public class ListMembersSqlTranslationTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    [Fact]
    public async Task ListMembers_OnSqlite_ReturnsMembershipsOrderedByEmail()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "sql_list_m");
        var (_, zedId) = await CreateUserAsync(factory, "sql_list_zed");
        var (_, alphaId) = await CreateUserAsync(factory, "sql_list_alpha");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, zedId, MemberRole);
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, alphaId, GuestRole);

        var response = await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = (await response.Content.ReadFromJsonAsync<List<MemberResponse>>())!;
        Assert.Equal(
            [(alphaId, GuestRole), (scenario.OrgAdminId, OrgAdminRole), (zedId, MemberRole)],
            members.Select(m => (m.UserId, m.Role)));
    }

    private record MemberResponse(Guid MembershipId, Guid UserId, string Email, string Role);
}
