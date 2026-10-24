using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC1 of #95 step 5a: any authenticated user lists their own memberships (my organizations), with the
// organization's name and the role, ordered by organization name, then organization id
public class ListMyOrganizationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string OrganizationSuffix = " organization";
    private const string SameOrganizationName = "mine_same_name organization";
    private const string SameOrganizationNameStable = "mine_same_name_stable organization";
    private const int SameNameOrganizationCount = 5;

    private static readonly HashSet<string> ContractFields = ["organizationId", "organizationName", "membershipId", "role"];

    [Fact]
    public async Task ListMyOrganizations_MembershipsInSeveralOrganizations_ReturnsEachWithNameAndRoleOrderedByName()
    {
        // Created in the order c, a, b, so the answer's order comes from the names and not from insertion
        var (user, userId) = await CreateUserAsync(factory, "mine_several_user");
        var orgAdminHere = await CreateOrganizationWithoutMembersAsync(factory, "mine_several_c");
        var orgAdminMembershipId = await AppointOrganizationAdminAsync(orgAdminHere.PlatformAdmin, orgAdminHere.OrganizationId, userId);
        var guestHere = await CreateOrganizationWithOrgAdminAsync(factory, "mine_several_a");
        var guestMembershipId = await AddMemberAsync(guestHere.OrgAdmin, guestHere.OrganizationId, userId, GuestRole);
        var memberHere = await CreateOrganizationWithOrgAdminAsync(factory, "mine_several_b");
        var memberMembershipId = await AddMemberAsync(memberHere.OrgAdmin, memberHere.OrganizationId, userId, MemberRole);

        var response = await user.GetAsync(MyOrganizationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                new MyOrganization(guestHere.OrganizationId, $"mine_several_a{OrganizationSuffix}", guestMembershipId, GuestRole),
                new MyOrganization(memberHere.OrganizationId, $"mine_several_b{OrganizationSuffix}", memberMembershipId, MemberRole),
                new MyOrganization(orgAdminHere.OrganizationId, $"mine_several_c{OrganizationSuffix}", orgAdminMembershipId, OrgAdminRole)
            ],
            await response.Content.ReadFromJsonAsync<List<MyOrganization>>());
    }

    [Fact]
    public async Task ListMyOrganizations_OtherUsersHaveMemberships_ReturnsOnlyTheCallersOwn()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "mine_scope");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "mine_scope_other");
        var (user, userId) = await CreateUserAsync(factory, "mine_scope_user");
        var (_, neighbourId) = await CreateUserAsync(factory, "mine_scope_neighbour");
        var ownMembershipId = await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, userId, MemberRole);
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, neighbourId, GuestRole);
        await AddMemberAsync(other.OrgAdmin, other.OrganizationId, neighbourId, MemberRole);

        var organizations = (await user.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Equal(ownMembershipId, Assert.Single(organizations).MembershipId);
    }

    [Fact]
    public async Task ListMyOrganizations_OrgAdmin_DoesNotListTheMembersOfTheirOrganization()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "mine_orgadmin");
        var (_, memberId) = await CreateUserAsync(factory, "mine_orgadmin_member");
        await AddMemberAsync(scenario.OrgAdmin, scenario.OrganizationId, memberId, MemberRole);

        var organizations = (await scenario.OrgAdmin.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        var own = Assert.Single(organizations);
        Assert.Equal((scenario.OrganizationId, OrgAdminRole), (own.OrganizationId, own.Role));
    }

    [Fact]
    public async Task ListMyOrganizations_NoMemberships_ReturnsEmptyList()
    {
        var (user, _) = await CreateUserAsync(factory, "mine_none");

        var response = await user.GetAsync(MyOrganizationsEndpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<MyOrganization>>())!);
    }

    // Organization names are unique through the use cases, so the two organizations are stored directly.
    // The expected order is the provider's Guid order, here EF InMemory's (Guid.CompareTo)
    [Fact]
    public async Task ListMyOrganizations_TwoOrganizationsWithTheSameName_AreOrderedByOrganizationId()
    {
        var (user, userId) = await CreateUserAsync(factory, "mine_same_name_user");
        var firstId = await SeedOrganizationRowAsync(SameOrganizationName);
        var secondId = await SeedOrganizationRowAsync(SameOrganizationName);
        await SeedMembershipAsync(factory, Membership.AddMember(userId, firstId));
        await SeedMembershipAsync(factory, Membership.AddGuest(userId, secondId));

        var organizations = (await user.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Equal(new[] { firstId, secondId }.Order(), organizations.Select(o => o.OrganizationId));
    }

    [Fact]
    public async Task ListMyOrganizations_TwoOrganizationsWithTheSameName_SameOrderOnEveryCall()
    {
        var (user, userId) = await CreateUserAsync(factory, "mine_same_name_stable_user");
        foreach (var _ in Enumerable.Range(0, SameNameOrganizationCount))
            await SeedMembershipAsync(factory, Membership.AddMember(userId, await SeedOrganizationRowAsync(SameOrganizationNameStable)));

        var first = (await user.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;
        var second = (await user.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Equal(SameNameOrganizationCount, first.Count);
        Assert.Equal(first.Select(o => o.OrganizationId), second.Select(o => o.OrganizationId));
    }

    [Fact]
    public async Task ListMyOrganizations_PlatformAdminWithoutMembership_ReturnsEmptyList()
    {
        // Creating an organization does not make the platform admin a member of it
        var (platformAdmin, _) = await CreateOrganizationWithoutMembersAsync(factory, "mine_platform_none");

        var organizations = (await platformAdmin.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Empty(organizations);
    }

    [Fact]
    public async Task ListMyOrganizations_PlatformAdminWithMigratedMembership_ListsItLikeAnyUsers()
    {
        // The migration stored the platform admin's OrgAdmin membership directly, without the use cases
        var (platformAdmin, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "mine_platform_migrated");
        var platformAdminId = await PlatformAdminIdAsync(factory, "mine_platform_migrated");
        var membershipId = await SeedMembershipAsync(factory, Membership.AppointOrgAdmin(platformAdminId, organizationId));

        var organizations = (await platformAdmin.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Equal(
            new MyOrganization(organizationId, $"mine_platform_migrated{OrganizationSuffix}", membershipId, OrgAdminRole),
            Assert.Single(organizations));
    }

    [Fact]
    public async Task ListMyOrganizations_PlatformAdminAppointedByAnotherPlatformAdmin_ListsTheMembership()
    {
        var (appointer, organizationId) = await CreateOrganizationWithoutMembersAsync(factory, "mine_platform_appointed");
        var appointee = await TestUsers.CreateClientAsync(factory, "mine_platform_appointee@example.com", UserRoles.Admin);
        var appointeeId = await TestUsers.GetIdAsync(factory, "mine_platform_appointee@example.com");
        var membershipId = await AppointOrganizationAdminAsync(appointer, organizationId, appointeeId);

        var organizations = (await appointee.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Equal(
            new MyOrganization(organizationId, $"mine_platform_appointed{OrganizationSuffix}", membershipId, OrgAdminRole),
            Assert.Single(organizations));
    }

    [Fact]
    public async Task ListMyOrganizations_OrganizationRowGone_DoesNotListTheMembership()
    {
        var kept = await CreateOrganizationWithOrgAdminAsync(factory, "mine_orphan_kept");
        var gone = await CreateOrganizationWithOrgAdminAsync(factory, "mine_orphan_gone");
        var (user, userId) = await CreateUserAsync(factory, "mine_orphan_user");
        var keptMembershipId = await AddMemberAsync(kept.OrgAdmin, kept.OrganizationId, userId, MemberRole);
        var orphanMembershipId = await AddMemberAsync(gone.OrgAdmin, gone.OrganizationId, userId, MemberRole);
        await RemoveOrganizationRowAsync(gone.OrganizationId);
        // Control: the membership row is still stored, so only the query can leave it out
        Assert.Contains(await StoredMembershipsOfUserAsync(factory, userId), m => m.Id == orphanMembershipId);

        var organizations = (await user.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Equal(keptMembershipId, Assert.Single(organizations).MembershipId);
    }

    [Fact]
    public async Task ListMyOrganizations_MembershipOfUnknownOrganization_IsNotListed()
    {
        var (user, userId) = await CreateUserAsync(factory, "mine_unknown_org");
        await SeedMembershipAsync(factory, Membership.AddMember(userId, Guid.NewGuid()));
        Assert.Single(await StoredMembershipsOfUserAsync(factory, userId));

        var organizations = (await user.GetFromJsonAsync<List<MyOrganization>>(MyOrganizationsEndpoint))!;

        Assert.Empty(organizations);
    }

    [Fact]
    public async Task ListMyOrganizations_Response_HasExactlyTheFourContractFields()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "mine_shape");

        using var document = JsonDocument.Parse(await scenario.OrgAdmin.GetStringAsync(MyOrganizationsEndpoint));

        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(ContractFields, item.EnumerateObject().Select(p => p.Name).ToHashSet());
    }

    [Fact]
    public async Task ListMyOrganizations_Anonymous_Returns401()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(MyOrganizationsEndpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // "mine" is not a guid, so the organization-scoped routes keep working next to it
    [Fact]
    public async Task ListMyOrganizations_RouteNextToOrganizationScopedRoutes_ListMembersStillAnswers()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "mine_routes");

        var response = await scenario.OrgAdmin.GetAsync(MembersEndpoint(scenario.OrganizationId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OrganizationScopedRoute_WithMineAsOrganizationId_Returns404()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "mine_as_id");

        var response = await scenario.OrgAdmin.GetAsync($"{MyOrganizationsEndpoint}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- helpers ---

    // Stores an organization row directly, without the use case and so without its unique-name rule
    private async Task<Guid> SeedOrganizationRowAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        var organization = new DbOrganization { Id = Guid.NewGuid(), Name = name };
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return organization.Id;
    }

    // Removes the organization row directly, leaving its memberships behind
    private async Task RemoveOrganizationRowAsync(Guid organizationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        db.Organizations.Remove(await db.Organizations.SingleAsync(o => o.Id == organizationId));
        await db.SaveChangesAsync();
    }

    private record MyOrganization(Guid OrganizationId, string OrganizationName, Guid MembershipId, string Role);
}
