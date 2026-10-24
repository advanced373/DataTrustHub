using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.ClearanceSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC2 and AC3 of #95 step 3b (ADR-0014, C5, C6, C8): an organization admin grants clearances on the policies of
// their organization to its members and guests, never to themselves, and lists only the clearances on those policies.
// Who may call (AC1) is covered in ClearanceAccessTests.
public class CreateAndListClearancesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TopSecretLevel = "TopSecret";
    private const int TopSecretPriority = 3;

    public static TheoryData<string> GrantableRoles => new(MemberRole, GuestRole);

    // --- grant: success ---

    [Fact]
    public async Task CreateClearance_Valid_IsListedWithItsLevel()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_create");

        var response = await PostClearanceAsync(
            grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId, TopSecretLevel, TopSecretPriority);
        var listed = await ListAsync(grant.OrgAdmin, grant.OrganizationId, $"?userId={grant.MemberId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var clearanceId = (await response.Content.ReadFromJsonAsync<CreateClearanceResponse>())!.ClearanceId;
        var clearance = Assert.Single(listed);
        Assert.Equal(
            new ClearanceResponse(clearanceId, $"{TopSecretLevel} clearance", grant.MemberId, grant.PolicyId, TopSecretLevel, TopSecretPriority),
            clearance);
    }

    // Invariant C5: a member or a guest of the organization may be granted a clearance
    [Theory]
    [MemberData(nameof(GrantableRoles))]
    public async Task CreateClearance_MemberOrGuest_Returns200AndStoresIt(string role)
    {
        var grant = await CreateGrantScenarioAsync(factory, $"clearance_role_{role}");
        var (_, userId) = await AddUserWithRoleAsync(factory, grant.Organization, $"clearance_role_{role}_user", role);

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, userId, grant.PolicyId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(await StoredClearancesAsync(factory), c => c.UserId == userId && c.PolicyId == grant.PolicyId);
    }

    // An organization admin may hold a clearance; another organization admin of the organization grants it
    [Fact]
    public async Task CreateClearance_ToAnotherOrgAdminByOrgAdmin_Returns200AndStoresIt()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_other_orgadmin");
        var (second, _) = await OrganizationCallers.AddSecondOrgAdminAsync(factory, grant.Organization, "clearance_other_orgadmin_second");

        var response = await PostClearanceAsync(second, grant.OrganizationId, grant.Organization.OrgAdminId, grant.PolicyId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            await StoredClearancesAsync(factory), c => c.UserId == grant.Organization.OrgAdminId && c.PolicyId == grant.PolicyId);
    }

    [Fact]
    public async Task CreateClearance_SameLevelTwice_ReusesTheClassificationLevel()
    {
        const string levelName = "Reused";
        const int levelPriority = 7;
        var grant = await CreateGrantScenarioAsync(factory, "clearance_reuse");
        var secondPolicyId = await CreatePolicyAsync(grant.OrgAdmin, "clearance_reuse second policy", grant.OrganizationId);

        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId, levelName, levelPriority);
        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, secondPolicyId, levelName, levelPriority);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        Assert.Equal(1, await db.ClassificationLevels.CountAsync(l => l.Name == levelName && l.Priority == levelPriority));
    }

    // --- grant: the refusals of AC2 ---

    // Invariant C3
    [Fact]
    public async Task CreateClearance_SameUserAndPolicyTwice_Returns409AndKeepsOne()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_duplicate");
        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.ClearanceAlreadyGranted, AdminErrorMessages.ClearanceAlreadyGranted);
        Assert.Single(await StoredClearancesOfUserAsync(grant.MemberId));
    }

    [Fact]
    public async Task CreateClearance_UnknownUser_Returns404UserNotFound()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_unknown_user");
        var unknownUserId = Guid.NewGuid();

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, unknownUserId, grant.PolicyId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.ClearanceUserNotFound, AdminErrorMessages.ClearanceUserNotFound);
        Assert.Empty(await StoredClearancesOfUserAsync(unknownUserId));
    }

    [Fact]
    public async Task CreateClearance_UnknownPolicy_Returns404PolicyNotFound()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_unknown_policy");

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, Guid.NewGuid());

        await AssertPolicyNotFoundAsync(response);
        Assert.Empty(await StoredClearancesOfUserAsync(grant.MemberId));
    }

    // Both unknown: since #95 step 3b the policy is reported first (AC2 order)
    [Fact]
    public async Task CreateClearance_UnknownUserAndPolicy_Returns404PolicyNotFound()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_unknown_both");

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, Guid.NewGuid(), Guid.NewGuid());

        await AssertPolicyNotFoundAsync(response);
    }

    // A policy of another organization through my route is not found, even when the user is a member of both
    // and the caller is an organization admin of both
    [Fact]
    public async Task CreateClearance_PolicyOfAnotherOrganization_Returns404PolicyNotFoundAndStoresNothing()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_foreign_policy");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "clearance_foreign_policy_other");
        var foreignPolicyId = await CreatePolicyAsync(other.OrgAdmin, "clearance_foreign_policy other policy", other.OrganizationId);
        await AddMemberAsync(other.OrgAdmin, other.OrganizationId, grant.MemberId, MemberRole);
        await SeedMembershipAsync(factory, Membership.AppointOrgAdmin(grant.Organization.OrgAdminId, other.OrganizationId));

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, foreignPolicyId);

        await AssertPolicyNotFoundAsync(response);
        Assert.Empty(await StoredClearancesOfUserAsync(grant.MemberId));
    }

    // Invariant C5
    [Fact]
    public async Task CreateClearance_UserWithoutMembership_Returns409UserNotMemberAndStoresNothing()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_not_member");
        var (_, outsiderId) = await CreateUserAsync(factory, "clearance_not_member_outsider");

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, outsiderId, grant.PolicyId);

        await AssertUserNotMemberAsync(response);
        Assert.Empty(await StoredClearancesOfUserAsync(outsiderId));
    }

    // Invariant C5: a membership in another organization does not count
    [Fact]
    public async Task CreateClearance_MemberOfAnotherOrganizationOnly_Returns409UserNotMemberAndStoresNothing()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_member_elsewhere");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "clearance_member_elsewhere_other");
        var (_, userId) = await AddUserWithRoleAsync(factory, other, "clearance_member_elsewhere_user", MemberRole);

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, userId, grant.PolicyId);

        await AssertUserNotMemberAsync(response);
        Assert.Empty(await StoredClearancesOfUserAsync(userId));
    }

    // Invariant C8: the organization admin is a member of the organization, so only C8 refuses
    [Fact]
    public async Task CreateClearance_OrgAdminToThemselves_Returns409CannotGrantSelfAndStoresNothing()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_self");
        var orgAdminId = grant.Organization.OrgAdminId;

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, orgAdminId, grant.PolicyId);

        await ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.ClearanceCannotGrantSelf, AdminErrorMessages.ClearanceCannotGrantSelf);
        Assert.Empty(await StoredClearancesOfUserAsync(orgAdminId));
    }

    [Fact]
    public async Task CreateClearance_NegativePriority_Returns400()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_invalid");

        var response = await PostClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId, DefaultLevelName, -1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await StoredClearancesOfUserAsync(grant.MemberId));
    }

    // --- list (AC3) ---

    // The member also holds a clearance in another organization; it is not listed here
    [Fact]
    public async Task ListClearances_OnlyClearancesOnThisOrganizationsPolicies_OrderedByName()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_list_scope");
        var (_, guestId) = await AddUserWithRoleAsync(factory, grant.Organization, "clearance_list_scope_guest", GuestRole);
        var memberClearance = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId, "B level", 1);
        var guestClearance = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, guestId, grant.PolicyId, "A level", 1);
        await GrantElsewhereAsync(grant.MemberId, "clearance_list_scope_other");

        var listed = await ListAsync(grant.OrgAdmin, grant.OrganizationId, string.Empty);

        Assert.Equal([guestClearance, memberClearance], listed.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task ListClearances_UserIdFilter_ReturnsOnlyThatUsersClearancesHere()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_list_user");
        var (_, guestId) = await AddUserWithRoleAsync(factory, grant.Organization, "clearance_list_user_guest", GuestRole);
        var memberClearance = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, guestId, grant.PolicyId);
        await GrantElsewhereAsync(grant.MemberId, "clearance_list_user_other");

        var listed = await ListAsync(grant.OrgAdmin, grant.OrganizationId, $"?userId={grant.MemberId}");

        Assert.Equal(memberClearance, Assert.Single(listed).Id);
    }

    [Fact]
    public async Task ListClearances_PolicyIdFilter_ReturnsOnlyThatPolicysClearances()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_list_policy");
        var secondPolicyId = await CreatePolicyAsync(grant.OrgAdmin, "clearance_list_policy second policy", grant.OrganizationId);
        var onPolicy = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, secondPolicyId);

        var listed = await ListAsync(grant.OrgAdmin, grant.OrganizationId, $"?policyId={grant.PolicyId}");

        Assert.Equal(onPolicy, Assert.Single(listed).Id);
    }

    // A policyId of another organization gives an empty list, never its clearances
    [Fact]
    public async Task ListClearances_PolicyIdOfAnotherOrganization_ReturnsEmpty()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_list_foreign");
        await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        var foreignPolicyId = await GrantElsewhereAsync(grant.MemberId, "clearance_list_foreign_other");

        var response = await grant.OrgAdmin.GetAsync($"{ClearancesEndpoint(grant.OrganizationId)}?policyId={foreignPolicyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<ClearanceResponse>>())!);
    }

    [Fact]
    public async Task ListClearances_NoClearancesHere_ReturnsEmpty()
    {
        var grant = await CreateGrantScenarioAsync(factory, "clearance_list_empty");
        await GrantElsewhereAsync(grant.MemberId, "clearance_list_empty_other");

        var listed = await ListAsync(grant.OrgAdmin, grant.OrganizationId, string.Empty);

        Assert.Empty(listed);
    }

    // --- helpers ---

    // The user joins another organization, whose own organization admin grants a clearance there; returns its policy
    private async Task<Guid> GrantElsewhereAsync(Guid userId, string prefix)
    {
        var other = await CreateOrganizationWithOrgAdminAsync(factory, prefix);
        var policyId = await CreatePolicyAsync(other.OrgAdmin, $"{prefix} policy", other.OrganizationId);
        await AddMemberAndGrantClearanceAsync(other.OrgAdmin, other.OrganizationId, userId, policyId, DefaultLevelName, DefaultLevelPriority);
        return policyId;
    }

    private async Task<List<Guid>> StoredClearancesOfUserAsync(Guid userId) =>
        (await StoredClearancesAsync(factory)).Where(c => c.UserId == userId).Select(c => c.Id).ToList();

    private static async Task<List<ClearanceResponse>> ListAsync(HttpClient caller, Guid organizationId, string queryString) =>
        (await caller.GetFromJsonAsync<List<ClearanceResponse>>($"{ClearancesEndpoint(organizationId)}{queryString}"))!;

    private static Task AssertPolicyNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.ClearancePolicyNotFound, AdminErrorMessages.ClearancePolicyNotFound);

    private static Task AssertUserNotMemberAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.Conflict, AdminErrorCodes.ClearanceUserNotMember, AdminErrorMessages.ClearanceUserNotMember);

    private record CreateClearanceResponse(Guid ClearanceId);

    private record ClearanceResponse(
        Guid Id, string Name, Guid UserId, Guid PolicyId, string ClassificationLevelName, int ClassificationLevelPriority);
}
