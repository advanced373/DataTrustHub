using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC1 and AC2 of #95 step 5a on a relational provider: the my-organizations query (join to organizations,
// order by name) and the find-by-email query must translate to SQL, which EF InMemory does not check
public class OrganizationPagesSqlTranslationTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    private const string EmailDomain = "@example.com";

    [Fact]
    public async Task ListMyOrganizations_OnSqlite_ReturnsMembershipsOrderedByOrganizationName()
    {
        var (user, userId) = await CreateUserAsync(factory, "sql_mine_user");
        var later = await CreateOrganizationWithOrgAdminAsync(factory, "sql_mine_z");
        var earlier = await CreateOrganizationWithOrgAdminAsync(factory, "sql_mine_a");
        await AddMemberAsync(later.OrgAdmin, later.OrganizationId, userId, MemberRole);
        await AddMemberAsync(earlier.OrgAdmin, earlier.OrganizationId, userId, GuestRole);

        var response = await user.GetAsync(MyOrganizationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organizations = (await response.Content.ReadFromJsonAsync<List<MyOrganization>>())!;
        Assert.Equal(
            [(earlier.OrganizationId, GuestRole), (later.OrganizationId, MemberRole)],
            organizations.Select(o => (o.OrganizationId, o.Role)));
    }

    [Fact]
    public async Task FindUserByEmail_OnSqlite_ReturnsMemberWithRole()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "sql_find");
        var (_, userId) = await CreateUserAsync(factory, "sql_find_target");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, GuestRole);

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, $"sql_find_target{EmailDomain}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new FoundUser(userId, $"sql_find_target{EmailDomain}", true, GuestRole),
            await response.Content.ReadFromJsonAsync<FoundUser>());
    }

    [Fact]
    public async Task FindUserByEmail_OnSqlite_WildcardIsNotAPattern_Returns404()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "sql_find_wild");
        await CreateUserAsync(factory, "sql_find_wild_target");

        var response = await PostFindUserByEmailAsync(scenario.OrgAdmin, scenario.OrganizationId, "sql_find_wild%@%");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record MyOrganization(Guid OrganizationId, string OrganizationName, Guid MembershipId, string Role);

    private record FoundUser(Guid UserId, string Email, bool IsMember, string? Role);
}
