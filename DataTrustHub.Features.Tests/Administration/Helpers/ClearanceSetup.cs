using DataTrustHub.Features.Tests.Helpers;
using static DataTrustHub.Features.Tests.Administration.Helpers.AdminSetup;
using static DataTrustHub.Features.Tests.Administration.Helpers.MembershipSetup;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// An organization with its organization admin, one policy of it and a member, who may be granted a clearance on it
internal sealed record GrantScenario(OrganizationScenario Organization, Guid PolicyId, HttpClient Member, Guid MemberId)
{
    internal Guid OrganizationId => Organization.OrganizationId;
    internal HttpClient OrgAdmin => Organization.OrgAdmin;
}

/// <summary>
/// Arranges the organizations, policies and memberships the clearance use cases need (#95 step 3b: C5, C6, C8).
/// </summary>
internal static class ClearanceSetup
{
    internal static async Task<GrantScenario> CreateGrantScenarioAsync(ApiFactory factory, string prefix)
    {
        var organization = await CreateOrganizationWithOrgAdminAsync(factory, prefix);
        var policyId = await CreatePolicyAsync(organization.OrgAdmin, $"{prefix} policy", organization.OrganizationId);
        var (member, memberId) = await AddUserWithRoleAsync(factory, organization, $"{prefix}_member", MemberRole);
        return new GrantScenario(organization, policyId, member, memberId);
    }

    // A new user whom the organization admin adds with the given role
    internal static async Task<(HttpClient Client, Guid UserId)> AddUserWithRoleAsync(
        ApiFactory factory, OrganizationScenario organization, string name, string role)
    {
        var (client, userId) = await CreateUserAsync(factory, name);
        (await PostMemberAsync(organization.OrgAdmin, organization.OrganizationId, userId, role)).EnsureSuccessStatusCode();
        return (client, userId);
    }
}
