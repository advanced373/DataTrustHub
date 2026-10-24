using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Administration.Helpers;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration;

// AC2 of #95 step 3a: an organization admin creates a policy of their organization; the organization comes
// from the route and the body has only the name (C6, P2)
public class CreatePolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int MaxNameLength = 256;

    [Fact]
    public async Task CreatePolicy_OrgAdminWithValidName_Returns200WithPolicyId()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_ok");

        var response = await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "Confidential Access");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreatePolicyResponse>();
        Assert.NotEqual(Guid.Empty, result!.PolicyId);
    }

    [Fact]
    public async Task CreatePolicy_OrgAdmin_StoresPolicyInTheRouteOrganization()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_stored");

        var response = await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "Stored policy");

        var policyId = (await response.Content.ReadFromJsonAsync<CreatePolicyResponse>())!.PolicyId;
        var stored = Assert.Single(await StoredPoliciesAsync(factory), p => p.Id == policyId);
        Assert.Equal(("Stored policy", scenario.OrganizationId), (stored.Name, stored.OrganizationId));
    }

    // The old body field is ignored: a caller cannot place a policy in another organization through the body
    [Fact]
    public async Task CreatePolicy_BodyWithAnotherOrganizationId_UsesTheRouteOrganization()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_body");
        var other = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_body_other");

        var response = await scenario.OrgAdmin.PostAsJsonAsync(
            PoliciesEndpoint(scenario.OrganizationId), new { Name = "Body policy", OrganizationId = other.OrganizationId });

        var policyId = (await response.Content.ReadFromJsonAsync<CreatePolicyResponse>())!.PolicyId;
        var stored = Assert.Single(await StoredPoliciesAsync(factory), p => p.Id == policyId);
        Assert.Equal(scenario.OrganizationId, stored.OrganizationId);
        Assert.DoesNotContain(await StoredPoliciesAsync(factory), p => p.OrganizationId == other.OrganizationId);
    }

    // Invariant P2
    [Fact]
    public async Task CreatePolicy_DuplicateNameInSameOrganization_Returns409WithCode()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_duplicate");
        await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "Duplicate Policy");

        var response = await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "Duplicate Policy");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.Conflict,
            AdminErrorCodes.PolicyNameAlreadyInUse, AdminErrorMessages.PolicyNameAlreadyInUse);
    }

    [Fact]
    public async Task CreatePolicy_DuplicateNameInSameOrganization_StoresNoSecondPolicy()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_duplicate_kept");
        await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "Kept Policy");

        await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "Kept Policy");

        Assert.Single(await StoredPoliciesAsync(factory), p => p.OrganizationId == scenario.OrganizationId);
    }

    [Fact]
    public async Task CreatePolicy_WithEmptyName_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_empty");

        var response = await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, "");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.BadRequest,
            AdminErrorCodes.ValidationGeneral, AdminErrorMessages.ValidationGeneral);
    }

    [Fact]
    public async Task CreatePolicy_NameAtMaximumLength_Returns200()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_max");

        var response = await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, new string('a', MaxNameLength));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreatePolicy_NameAboveMaximumLength_Returns400()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_long");

        var response = await PostPolicyAsync(scenario.OrgAdmin, scenario.OrganizationId, new string('a', MaxNameLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // P1 follows from the organization admin check: a missing organization has no organization admin
    [Fact]
    public async Task CreatePolicy_MissingOrganization_Returns404OrganizationNotFound()
    {
        var scenario = await CreateOrganizationWithOrgAdminAsync(factory, "policy_create_missing");

        var response = await PostPolicyAsync(scenario.OrgAdmin, Guid.NewGuid(), "Orphan policy");

        await ErrorAssert.HasErrorAsync(response, HttpStatusCode.NotFound,
            AdminErrorCodes.OrganizationNotFound, AdminErrorMessages.OrganizationNotFound);
    }

    private record CreatePolicyResponse(Guid PolicyId);
}
