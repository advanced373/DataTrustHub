using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.ClearanceSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC1 of #95 step 3b (ADR-0014, C6 for clearances): every clearance route requires an authenticated caller who is an
// organization admin of the route's organization, checked first; everyone else gets 404 Organization.NotFound,
// the platform admin included. AC5: the platform admin's /admin/clearances routes are gone.
public class ClearanceAccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CreateRoute = "Create";
    private const string ListRoute = "List";
    private const string GetRoute = "Get";
    private const string DeleteRoute = "Delete";

    private static readonly string[] AllRoutes = [CreateRoute, ListRoute, GetRoute, DeleteRoute];
    private static readonly string[] ClearanceRoutes = [GetRoute, DeleteRoute];

    public static TheoryData<string> Routes => new(AllRoutes);

    public static TheoryData<string> RoutesWithClearanceId => new(ClearanceRoutes);

    public static TheoryData<string> NotOrgAdminCallers => new(OrganizationCallers.NotOrgAdminKinds);

    public static TheoryData<string, string> RoutesAndNotOrgAdminCallers
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var route in AllRoutes)
                foreach (var kind in OrganizationCallers.NotOrgAdminKinds)
                    data.Add(route, kind);
            return data;
        }
    }

    // Control: the organization admin of the organization reaches every route
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ClearanceRoute_OrgAdmin_Succeeds(string route)
    {
        var target = await ArrangeAsync($"clearance_access_ok_{route}");

        var response = await SendAsync(target.Grant.OrgAdmin, route, target.Grant.OrganizationId, target);

        Assert.True(response.IsSuccessStatusCode, $"{route}: {response.StatusCode}");
    }

    // The policy, the users and the clearance exist in the organization, so the 404 can only come from the caller check
    [Theory]
    [MemberData(nameof(RoutesAndNotOrgAdminCallers))]
    public async Task ClearanceRoute_CallerNotOrgAdmin_Returns404OrganizationNotFound(string route, string callerKind)
    {
        var prefix = $"clearance_access_{route}_{callerKind}";
        var target = await ArrangeAsync(prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, target.Grant.Organization, callerKind, prefix);

        var response = await SendAsync(caller, route, target.Grant.OrganizationId, target);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task CreateClearance_CallerNotOrgAdmin_StoresNoClearance(string callerKind)
    {
        var prefix = $"clearance_access_create_kept_{callerKind}";
        var target = await ArrangeAsync(prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, target.Grant.Organization, callerKind, prefix);

        await SendAsync(caller, CreateRoute, target.Grant.OrganizationId, target);

        Assert.DoesNotContain(await StoredClearancesAsync(factory), c => c.UserId == target.UngrantedUserId);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task DeleteClearance_CallerNotOrgAdmin_KeepsClearance(string callerKind)
    {
        var prefix = $"clearance_access_delete_kept_{callerKind}";
        var target = await ArrangeAsync(prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, target.Grant.Organization, callerKind, prefix);

        await SendAsync(caller, DeleteRoute, target.Grant.OrganizationId, target);

        Assert.Contains(await StoredClearancesAsync(factory), c => c.Id == target.ClearanceId);
    }

    // A missing organization answers like a foreign one, also for an organization admin elsewhere
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ClearanceRoute_MissingOrganization_Returns404OrganizationNotFound(string route)
    {
        var target = await ArrangeAsync($"clearance_access_missing_{route}");

        var response = await SendAsync(target.Grant.OrgAdmin, route, Guid.NewGuid(), target);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ClearanceRoute_PlatformAdminAndMissingOrganization_Returns404OrganizationNotFound(string route)
    {
        var target = await ArrangeAsync($"clearance_access_missing_platform_{route}");

        var response = await SendAsync(target.Grant.Organization.PlatformAdmin, route, Guid.NewGuid(), target);

        await AssertOrganizationNotFoundAsync(response);
    }

    // The check is made per request from the membership, not from the token: a former organization admin
    // whose token is still valid is refused
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ClearanceRoute_FormerOrgAdminWithValidToken_Returns404OrganizationNotFound(string route)
    {
        var target = await ArrangeAsync($"clearance_access_former_{route}");
        var organization = target.Grant.Organization;
        var membershipId = Assert.Single(
            await StoredMembershipsAsync(factory, organization.OrganizationId), m => m.UserId == organization.OrgAdminId).Id;
        await RemoveMembershipAsync(factory, membershipId);

        var response = await SendAsync(organization.OrgAdmin, route, organization.OrganizationId, target);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ClearanceRoute_Anonymous_Returns401(string route)
    {
        var target = await ArrangeAsync($"clearance_access_anonymous_{route}");
        var anonymous = factory.CreateClient();

        var response = await SendAsync(anonymous, route, target.Grant.OrganizationId, target);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- validators ---

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ClearanceRoute_EmptyOrganizationId_Returns400(string route)
    {
        var target = await ArrangeAsync($"clearance_access_empty_org_{route}");

        var response = await SendAsync(target.Grant.OrgAdmin, route, Guid.Empty, target);

        await AssertValidationErrorAsync(response);
    }

    [Theory]
    [MemberData(nameof(RoutesWithClearanceId))]
    public async Task ClearanceRoute_EmptyClearanceId_Returns400(string route)
    {
        var target = await ArrangeAsync($"clearance_access_empty_clearance_{route}");

        var response = await SendAsync(target.Grant.OrgAdmin, route, target.Grant.OrganizationId, target with { ClearanceId = Guid.Empty });

        await AssertValidationErrorAsync(response);
    }

    // --- AC5: the platform admin's /admin/clearances routes are gone ---

    [Fact]
    public async Task LegacyClearanceRoutes_PlatformAdmin_Return404FromRouting()
    {
        var target = await ArrangeAsync("clearance_legacy_routes");
        var admin = target.Grant.Organization.PlatformAdmin;
        var legacyClearanceRoute = $"{LegacyClearancesEndpoint}/{target.ClearanceId}";

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(LegacyClearancesEndpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostLegacyClearanceAsync(admin, target)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(legacyClearanceRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync(legacyClearanceRoute)).StatusCode);
    }

    [Fact]
    public async Task LegacyClearanceRoutes_PlatformAdmin_ChangeNothing()
    {
        var target = await ArrangeAsync("clearance_legacy_unchanged");
        var admin = target.Grant.Organization.PlatformAdmin;

        await PostLegacyClearanceAsync(admin, target);
        await admin.DeleteAsync($"{LegacyClearancesEndpoint}/{target.ClearanceId}");

        var stored = await StoredClearancesAsync(factory);
        Assert.Contains(stored, c => c.Id == target.ClearanceId);
        Assert.DoesNotContain(stored, c => c.UserId == target.UngrantedUserId);
    }

    // --- helpers ---

    // A grant scenario where the member holds a clearance, and a second member who holds none (the Create target)
    private sealed record AccessTarget(GrantScenario Grant, Guid ClearanceId, Guid UngrantedUserId);

    private async Task<AccessTarget> ArrangeAsync(string prefix)
    {
        var grant = await CreateGrantScenarioAsync(factory, prefix);
        var clearanceId = await CreateClearanceAsync(grant.OrgAdmin, grant.OrganizationId, grant.MemberId, grant.PolicyId);
        var (_, ungrantedUserId) = await AddUserWithRoleAsync(factory, grant.Organization, $"{prefix}_ungranted", MemberRole);
        return new AccessTarget(grant, clearanceId, ungrantedUserId);
    }

    // The request is valid for the organization admin, so only the caller check can refuse it
    private static Task<HttpResponseMessage> SendAsync(HttpClient caller, string route, Guid organizationId, AccessTarget target) =>
        route switch
        {
            CreateRoute => PostClearanceAsync(caller, organizationId, target.UngrantedUserId, target.Grant.PolicyId),
            ListRoute => caller.GetAsync(ClearancesEndpoint(organizationId)),
            GetRoute => caller.GetAsync(ClearanceEndpoint(organizationId, target.ClearanceId)),
            DeleteRoute => caller.DeleteAsync(ClearanceEndpoint(organizationId, target.ClearanceId)),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };

    private static Task<HttpResponseMessage> PostLegacyClearanceAsync(HttpClient admin, AccessTarget target) =>
        admin.PostAsJsonAsync(LegacyClearancesEndpoint, new
        {
            UserId = target.UngrantedUserId,
            target.Grant.PolicyId,
            Name = "Legacy clearance",
            ClassificationLevelName = DefaultLevelName,
            ClassificationLevelPriority = DefaultLevelPriority
        });

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private static Task AssertValidationErrorAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.BadRequest, AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
}
