using System.Net;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC1 of #95 step 3a (ADR-0014, C6 for policies): every policy route requires an authenticated caller who is an
// organization admin of the route's organization, checked first; everyone else gets 404 Organization.NotFound,
// the platform admin included. AC2-AC4 validators: empty ids are refused with 400 before the handler runs.
public class PolicyAccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CreateRoute = "Create";
    private const string ListRoute = "List";
    private const string GetRoute = "Get";
    private const string DeleteRoute = "Delete";
    private const string PolicyName = "Access policy";
    private const string AttemptedPolicyName = "Attempted policy";

    private static readonly string[] AllRoutes = [CreateRoute, ListRoute, GetRoute, DeleteRoute];
    private static readonly string[] PolicyRoutes = [GetRoute, DeleteRoute];

    public static TheoryData<string> Routes => new(AllRoutes);

    public static TheoryData<string> RoutesWithPolicyId => new(PolicyRoutes);

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
    public async Task PolicyRoute_OrgAdmin_Succeeds(string route)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"access_ok_{route}");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, PolicyName, scenario.OrganizationId);

        var response = await SendAsync(scenario.OrgAdmin, route, scenario.OrganizationId, policyId);

        Assert.True(response.IsSuccessStatusCode, $"{route}: {response.StatusCode}");
    }

    // The policy exists in the organization, so the 404 can only come from the organization admin check
    [Theory]
    [MemberData(nameof(RoutesAndNotOrgAdminCallers))]
    public async Task PolicyRoute_CallerNotOrgAdmin_Returns404OrganizationNotFound(string route, string callerKind)
    {
        var prefix = $"access_{route}_{callerKind}";
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, prefix);
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, PolicyName, scenario.OrganizationId);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, prefix);

        var response = await SendAsync(caller, route, scenario.OrganizationId, policyId);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task CreatePolicy_CallerNotOrgAdmin_StoresNoPolicy(string callerKind)
    {
        var prefix = $"access_create_kept_{callerKind}";
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, prefix);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, prefix);

        await PostPolicyAsync(caller, scenario.OrganizationId, AttemptedPolicyName);

        Assert.DoesNotContain(await StoredPoliciesAsync(factory), p => p.OrganizationId == scenario.OrganizationId);
    }

    [Theory]
    [MemberData(nameof(NotOrgAdminCallers))]
    public async Task DeletePolicy_CallerNotOrgAdmin_KeepsPolicy(string callerKind)
    {
        var prefix = $"access_delete_kept_{callerKind}";
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, prefix);
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, PolicyName, scenario.OrganizationId);
        var caller = await OrganizationCallers.CreateNotOrgAdminAsync(factory, scenario, callerKind, prefix);

        await caller.DeleteAsync(PolicyEndpoint(scenario.OrganizationId, policyId));

        Assert.Contains(await StoredPoliciesAsync(factory), p => p.Id == policyId);
    }

    // A missing organization answers like a foreign one, also for an organization admin elsewhere
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task PolicyRoute_MissingOrganization_Returns404OrganizationNotFound(string route)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"access_missing_{route}");

        var response = await SendAsync(scenario.OrgAdmin, route, Guid.NewGuid(), Guid.NewGuid());

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task PolicyRoute_PlatformAdminAndMissingOrganization_Returns404OrganizationNotFound(string route)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"access_missing_platform_{route}");

        var response = await SendAsync(scenario.PlatformAdmin, route, Guid.NewGuid(), Guid.NewGuid());

        await AssertOrganizationNotFoundAsync(response);
    }

    // The check is made per request from the membership, not from the token: a former organization admin
    // whose token is still valid is refused
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task PolicyRoute_FormerOrgAdminWithValidToken_Returns404OrganizationNotFound(string route)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"access_former_{route}");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, PolicyName, scenario.OrganizationId);
        var membershipId = Assert.Single(
            await StoredMembershipsAsync(factory, scenario.OrganizationId), m => m.UserId == scenario.OrgAdminId).Id;
        await RemoveMembershipAsync(factory, membershipId);

        var response = await SendAsync(scenario.OrgAdmin, route, scenario.OrganizationId, policyId);

        await AssertOrganizationNotFoundAsync(response);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task PolicyRoute_Anonymous_Returns401(string route)
    {
        var anonymous = factory.CreateClient();

        var response = await SendAsync(anonymous, route, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- validators ---

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task PolicyRoute_EmptyOrganizationId_Returns400(string route)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"access_empty_org_{route}");
        var policyId = await CreatePolicyAsync(scenario.OrgAdmin, PolicyName, scenario.OrganizationId);

        var response = await SendAsync(scenario.OrgAdmin, route, Guid.Empty, policyId);

        await AssertValidationErrorAsync(response);
    }

    [Theory]
    [MemberData(nameof(RoutesWithPolicyId))]
    public async Task PolicyRoute_EmptyPolicyId_Returns400(string route)
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, $"access_empty_policy_{route}");

        var response = await SendAsync(scenario.OrgAdmin, route, scenario.OrganizationId, Guid.Empty);

        await AssertValidationErrorAsync(response);
    }

    // --- helpers ---

    private static Task<HttpResponseMessage> SendAsync(HttpClient caller, string route, Guid organizationId, Guid policyId) =>
        route switch
        {
            CreateRoute => PostPolicyAsync(caller, organizationId, AttemptedPolicyName),
            ListRoute => caller.GetAsync(PoliciesEndpoint(organizationId)),
            GetRoute => caller.GetAsync(PolicyEndpoint(organizationId, policyId)),
            DeleteRoute => caller.DeleteAsync(PolicyEndpoint(organizationId, policyId)),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null)
        };

    private static Task AssertOrganizationNotFoundAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.NotFound, AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);

    private static Task AssertValidationErrorAsync(HttpResponseMessage response) =>
        ErrorAssert.HasErrorAsync(
            response, HttpStatusCode.BadRequest, AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
}
