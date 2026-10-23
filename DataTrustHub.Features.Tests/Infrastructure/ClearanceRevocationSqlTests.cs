using System.Net;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Infrastructure;

// AC4 of #95 step 2b on a relational provider: ListIdsInOrganizationAsync and the C7 revocation must
// translate to SQL and commit together with the end of the membership, which EF InMemory does not check
public class ClearanceRevocationSqlTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    [Fact]
    public async Task ListIdsInOrganization_OnSqlite_ReturnsOnlyThatOrganizationsPolicyIds()
    {
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "sql_policy_ids");
        var first = await CreatePolicyAsync(platformAdmin, "sql_policy_ids first", organizationId);
        var second = await CreatePolicyAsync(platformAdmin, "sql_policy_ids second", organizationId);
        await CreatePolicyAsync(platformAdmin, "sql_policy_ids elsewhere");

        using var scope = factory.Services.CreateScope();
        var ids = await scope.ServiceProvider.GetRequiredService<IPolicyRepository>().ListIdsInOrganizationAsync(organizationId, default);

        Assert.Equal(new HashSet<Guid> { first, second }, ids.ToHashSet());
    }

    [Fact]
    public async Task RemoveMember_OnSqlite_RemovesMembershipAndRevokesOnlyClearancesHere()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "sql_remove_c7");
        var (_, userId) = await CreateUserAsync(factory, "sql_remove_c7_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        var clearances = await CreateClearancesAsync(factory, scenario, userId, "sql_remove_c7");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
        Assert.Equal([clearances.UserClearanceElsewhere], await StoredClearanceIdsOfUserAsync(factory, userId));
        Assert.Equal([clearances.OtherUserClearanceHere], await StoredClearanceIdsOfUserAsync(factory, clearances.OtherUserId));
    }

    [Fact]
    public async Task LeaveOrganization_OnSqlite_RemovesMembershipAndRevokesOnlyClearancesHere()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "sql_leave_c7");
        var (client, userId) = await CreateUserAsync(factory, "sql_leave_c7_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, GuestRole);
        var clearances = await CreateClearancesAsync(factory, scenario, userId, "sql_leave_c7");

        var response = await LeaveOrganizationAsync(client, scenario.OrganizationId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
        Assert.Equal([clearances.UserClearanceElsewhere], await StoredClearanceIdsOfUserAsync(factory, userId));
    }

    // AC5 on SQLite: memberships, clearances and the user go in one save, without a foreign key conflict
    [Fact]
    public async Task DeleteAccount_OnSqlite_RemovesUserMembershipsAndClearances()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "sql_delete_account");
        await CreateClearancesAsync(factory, scenario, scenario.OrgAdminId, "sql_delete_account");

        var response = await scenario.PlatformAdmin.DeleteAsync($"/admin/accounts/{scenario.OrgAdminId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await StoredMembershipsOfUserAsync(factory, scenario.OrgAdminId));
        Assert.Empty(await StoredClearanceIdsOfUserAsync(factory, scenario.OrgAdminId));
    }
}
