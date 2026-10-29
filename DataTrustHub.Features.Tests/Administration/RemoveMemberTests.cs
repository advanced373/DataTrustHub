using System.Net;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC2 of #95 step 2b: an organization admin ends a membership (ADR-0014). M2 keeps the last organization admin;
// C7 revokes the user's clearances on the organization's policies in the same save
public class RemoveMemberTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static TheoryData<string> NotOrgAdminCallers => new(OrganizationCallers.NotOrgAdminKinds);

    [Theory]
    [InlineData(MemberRole)]
    [InlineData(GuestRole)]
    public async Task RemoveMember_MemberOrGuest_Returns204AndRemovesTheMembership(string role)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"remove_{role}");
        var (_, userId) = await CreateUserAsync(factory, $"remove_{role}_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, role);

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == userId);
    }

    // Invariant C7
    [Fact]
    public async Task RemoveMember_UserWithClearances_RevokesOnlyTheirClearancesOnThisOrganizationsPolicies()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_c7");
        var (_, userId) = await CreateUserAsync(factory, "remove_c7_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        var clearances = await CreateClearancesAsync(factory, scenario, userId, "remove_c7");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([clearances.UserClearanceElsewhere], await StoredClearanceIdsOfUserAsync(factory, userId));
        Assert.Equal([clearances.OtherUserClearanceHere], await StoredClearanceIdsOfUserAsync(factory, clearances.OtherUserId));
    }

    // The user's memberships in other organizations stay
    [Fact]
    public async Task RemoveMember_UserInTwoOrganizations_KeepsTheOtherMembership()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_two");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "remove_two_other");
        var (_, userId) = await CreateUserAsync(factory, "remove_two_user");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        var otherMembershipId = await AddMemberAsync(other.OrgAdmin, other.OrganizationId, userId, MemberRole);

        await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId);

        Assert.Equal(otherMembershipId, Assert.Single(await StoredMembershipsOfUserAsync(factory, userId)).Id);
    }

    [Fact]
    public async Task RemoveMember_OrgAdminWhileAnotherRemains_Returns204()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_admin");
        var (_, secondId) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "remove_admin_second");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, secondId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(scenario.OrgAdminId, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).UserId);
    }

    [Fact]
    public async Task RemoveMember_SelfWhileAnotherOrgAdminRemains_Returns204AndCallerLosesAccess()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_self_ok");
        await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "remove_self_ok_second");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, scenario.OrgAdminId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == scenario.OrgAdminId);
        await AssertOrganizationNotFoundAsync(await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId)));
    }

    // Invariant M2, also when the organization admin removes themselves as the last one; nothing changes
    [Fact]
    public async Task RemoveMember_SelfAsLastOrgAdmin_Returns409LastOrgAdminAndKeepsMembershipAndClearances()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_last_self");
        var clearances = await CreateClearancesAsync(factory, scenario, scenario.OrgAdminId, "remove_last_self");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, scenario.OrgAdminId);

        await AssertLastOrgAdminAsync(response);
        Assert.Equal(OrgAdminRole, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).Role);
        Assert.Equal(
            new HashSet<Guid> { clearances.UserClearanceHere, clearances.UserClearanceElsewhere },
            (await StoredClearanceIdsOfUserAsync(factory, scenario.OrgAdminId)).ToHashSet());
    }

    // M2 after a removal: the remaining organization admin is now the last one
    [Fact]
    public async Task RemoveMember_RemainingOrgAdminAfterRemovingTheOther_Returns409LastOrgAdmin()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_last_turn");
        var (second, secondId) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, scenario, "remove_last_turn_second");
        await DeleteMemberAsync(second, scenario.OrganizationId, scenario.OrgAdminId);

        var response = await DeleteMemberAsync(second, scenario.OrganizationId, secondId);

        await AssertLastOrgAdminAsync(response);
        Assert.Equal(secondId, Assert.Single(await StoredMembershipsAsync(factory, scenario.OrganizationId)).UserId);
    }

    [Fact]
    public async Task RemoveMember_TargetNotAMember_Returns404MembershipNotFoundAndKeepsClearances()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_notmember");
        var (_, userId) = await CreateUserAsync(factory, "remove_notmember_user");
        var clearances = await CreateClearancesAsync(factory, scenario, userId, "remove_notmember");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.MembershipNotFound, AdminErrorMessages.MembershipNotFound);
        Assert.Contains(clearances.UserClearanceHere, await StoredClearanceIdsOfUserAsync(factory, userId));
    }

    // The caller check comes first, so a caller outside the organization learns nothing about users
    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task RemoveMember_CallerNotOrgAdminAndUnknownTarget_Returns404OrganizationNotFound(string callerKind)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"remove_unknown_{callerKind}");
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, $"remove_unknown_{callerKind}");

        var response = await DeleteMemberAsync(caller, scenario.OrganizationId, Guid.NewGuid());

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task RemoveMember_CallerNotOrgAdmin_Returns404AndKeepsMembershipAndClearances(string callerKind)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"remove_caller_{callerKind}");
        var (_, memberId) = await CreateUserAsync(factory, $"remove_caller_{callerKind}_member");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);
        var clearances = await CreateClearancesAsync(factory, scenario, memberId, $"remove_caller_{callerKind}");
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, $"remove_caller_{callerKind}");

        var response = await DeleteMemberAsync(caller, scenario.OrganizationId, memberId);

        await AssertOrganizationNotFoundAsync(response);
        Assert.Contains(await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == memberId);
        Assert.Contains(clearances.UserClearanceHere, await StoredClearanceIdsOfUserAsync(factory, memberId));
    }

    [Fact]
    public async Task RemoveMember_UnknownOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_noorg");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, Guid.NewGuid(), scenario.OrgAdminId);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Fact]
    public async Task RemoveMember_EmptyUserId_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "remove_empty_user");

        var response = await DeleteMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, Guid.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveMember_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await DeleteMemberAsync(anonymous, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- helpers ---

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private static Task AssertLastOrgAdminAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.MembershipLastOrgAdmin, AdminErrorMessages.MembershipLastOrgAdmin);
}
