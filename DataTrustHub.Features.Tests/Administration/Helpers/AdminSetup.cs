using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

/// <summary>
/// Creates the organization, policy and clearance prerequisites through the API.
/// </summary>
internal static class AdminSetup
{
    internal const string OrganizationsEndpoint = "/admin/organizations";

    // The platform admin's policy routes before #95 step 3a, kept only to prove they no longer exist
    internal const string LegacyPoliciesEndpoint = "/admin/policies";

    // The platform admin's clearance routes before #95 step 3b, kept only to prove they no longer exist
    internal const string LegacyClearancesEndpoint = "/admin/clearances";

    private const string OrgAdminEmailPrefix = "policy_orgadmin_";
    private const string OrgAdminEmailDomain = "@example.com";

    internal static async Task<Guid> CreateOrganizationAsync(HttpClient admin, string name)
    {
        var organization = await admin.PostAsJsonAsync(OrganizationsEndpoint, new { Name = name });
        return (await organization.Content.ReadFromJsonAsync<CreateOrganizationResponse>())!.OrganizationId;
    }

    // --- policies (ADR-0014, C6): managed by an organization admin of the policy's organization ---

    internal static string PoliciesEndpoint(Guid organizationId) => $"/organizations/{organizationId}/policies";

    internal static string PolicyEndpoint(Guid organizationId, Guid policyId) => $"{PoliciesEndpoint(organizationId)}/{policyId}";

    internal static Task<HttpResponseMessage> PostPolicyAsync(HttpClient caller, Guid organizationId, string name) =>
        caller.PostAsJsonAsync(PoliciesEndpoint(organizationId), new { Name = name });

    internal static async Task<Guid> CreatePolicyAsync(HttpClient orgAdmin, string name, Guid organizationId)
    {
        var policy = await PostPolicyAsync(orgAdmin, organizationId, name);
        return (await policy.Content.ReadFromJsonAsync<CreatePolicyResponse>())!.PolicyId;
    }

    // A policy in a new organization "{name} organization", created by a new organization admin of it
    internal static async Task<Guid> CreatePolicyAsync(ApiFactory factory, HttpClient platformAdmin, string name)
    {
        var (organizationId, orgAdmin) = await CreateOrganizationWithOrgAdminAsync(factory, platformAdmin, $"{name} organization");
        return await CreatePolicyAsync(orgAdmin, name, organizationId);
    }

    // The platform admin creates the organization and appoints a new user as its first organization admin
    internal static async Task<(Guid OrganizationId, HttpClient OrgAdmin)> CreateOrganizationWithOrgAdminAsync(
        ApiFactory factory, HttpClient platformAdmin, string organizationName)
    {
        var organizationId = await CreateOrganizationAsync(platformAdmin, organizationName);
        var email = $"{OrgAdminEmailPrefix}{Guid.NewGuid():N}{OrgAdminEmailDomain}";
        var orgAdmin = await TestUsers.CreateClientAsync(factory, email);
        await AppointOrganizationAdminAsync(platformAdmin, organizationId, await TestUsers.GetIdAsync(factory, email));
        return (organizationId, orgAdmin);
    }

    // --- clearances (ADR-0014, C6): granted by an organization admin of the policy's organization ---

    internal const string DefaultLevelName = "Secret";
    internal const int DefaultLevelPriority = 2;

    internal static string ClearancesEndpoint(Guid organizationId) => $"/organizations/{organizationId}/clearances";

    internal static string ClearanceEndpoint(Guid organizationId, Guid clearanceId) =>
        $"{ClearancesEndpoint(organizationId)}/{clearanceId}";

    internal static Task<HttpResponseMessage> PostClearanceAsync(
        HttpClient caller, Guid organizationId, Guid userId, Guid policyId,
        string level = DefaultLevelName, int priority = DefaultLevelPriority) =>
        caller.PostAsJsonAsync(ClearancesEndpoint(organizationId), new
        {
            UserId = userId,
            PolicyId = policyId,
            Name = $"{level} clearance",
            ClassificationLevelName = level,
            ClassificationLevelPriority = priority
        });

    // Fails the test at once when the grant is refused, so a broken setup does not pass as Guid.Empty
    internal static async Task<Guid> CreateClearanceAsync(
        HttpClient orgAdmin, Guid organizationId, Guid userId, Guid policyId,
        string level = DefaultLevelName, int priority = DefaultLevelPriority)
    {
        var response = await PostClearanceAsync(orgAdmin, organizationId, userId, policyId, level, priority);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateClearanceResponse>())!.ClearanceId;
    }

    // C5: the organization admin first adds the user as a member, then grants the clearance
    internal static async Task<Guid> AddMemberAndGrantClearanceAsync(
        HttpClient orgAdmin, Guid organizationId, Guid userId, Guid policyId, string level, int priority)
    {
        (await PostMemberAsync(orgAdmin, organizationId, userId, MembershipSetup.MemberRole)).EnsureSuccessStatusCode();
        return await CreateClearanceAsync(orgAdmin, organizationId, userId, policyId, level, priority);
    }

    // A policy in a new organization "{name} organization", with the organization admin who may grant clearances on it
    internal static async Task<(Guid OrganizationId, HttpClient OrgAdmin, Guid PolicyId)> CreatePolicyWithOrgAdminAsync(
        ApiFactory factory, HttpClient platformAdmin, string name)
    {
        var (organizationId, orgAdmin) = await CreateOrganizationWithOrgAdminAsync(factory, platformAdmin, $"{name} organization");
        return (organizationId, orgAdmin, await CreatePolicyAsync(orgAdmin, name, organizationId));
    }

    // Every stored clearance, read directly, so a test can prove what a request did or did not change
    internal static async Task<List<DbClearance>> StoredClearancesAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Clearances.AsNoTracking().ToListAsync();
    }

    // Every stored policy, read directly, so a test can prove what a request did or did not change
    internal static async Task<List<DbPolicy>> StoredPoliciesAsync(ApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DContext>().Policies.AsNoTracking().ToListAsync();
    }

    // --- memberships (ADR-0014) ---

    internal static string AdminsEndpoint(Guid organizationId) => $"{OrganizationsEndpoint}/{organizationId}/admins";

    internal static string MembersEndpoint(Guid organizationId) => $"/organizations/{organizationId}/members";

    internal static Task<HttpResponseMessage> PostAppointOrganizationAdminAsync(
        HttpClient platformAdmin, Guid organizationId, Guid userId) =>
        platformAdmin.PostAsJsonAsync(AdminsEndpoint(organizationId), new { UserId = userId });

    internal static async Task<Guid> AppointOrganizationAdminAsync(HttpClient platformAdmin, Guid organizationId, Guid userId)
    {
        var response = await PostAppointOrganizationAdminAsync(platformAdmin, organizationId, userId);
        return (await response.Content.ReadFromJsonAsync<MembershipResponse>())!.MembershipId;
    }

    internal static Task<HttpResponseMessage> PostMemberAsync(HttpClient caller, Guid organizationId, Guid userId, string? role) =>
        caller.PostAsJsonAsync(MembersEndpoint(organizationId), new { UserId = userId, Role = role });

    internal static async Task<Guid> AddMemberAsync(HttpClient orgAdmin, Guid organizationId, Guid userId, string role)
    {
        var response = await PostMemberAsync(orgAdmin, organizationId, userId, role);
        return (await response.Content.ReadFromJsonAsync<MembershipResponse>())!.MembershipId;
    }

    internal static string MemberEndpoint(Guid organizationId, Guid userId) => $"{MembersEndpoint(organizationId)}/{userId}";

    internal static string MemberRoleEndpoint(Guid organizationId, Guid userId) => $"{MemberEndpoint(organizationId, userId)}/role";

    internal static string OwnMembershipEndpoint(Guid organizationId) => $"/organizations/{organizationId}/membership";

    internal static Task<HttpResponseMessage> PutMemberRoleAsync(HttpClient caller, Guid organizationId, Guid userId, string? role) =>
        caller.PutAsJsonAsync(MemberRoleEndpoint(organizationId, userId), new { Role = role });

    internal static Task<HttpResponseMessage> DeleteMemberAsync(HttpClient caller, Guid organizationId, Guid userId) =>
        caller.DeleteAsync(MemberEndpoint(organizationId, userId));

    internal static Task<HttpResponseMessage> LeaveOrganizationAsync(HttpClient caller, Guid organizationId) =>
        caller.DeleteAsync(OwnMembershipEndpoint(organizationId));

    // --- organization pages (#95 step 5a) ---

    internal const string MyOrganizationsEndpoint = "/organizations/mine";

    // ADR-0019: the email travels in the JSON body of a POST, never in the URL
    internal static string FindUserByEmailEndpoint(Guid organizationId) => $"/organizations/{organizationId}/users/find-by-email";

    internal static Task<HttpResponseMessage> PostFindUserByEmailAsync(HttpClient caller, Guid organizationId, string? email) =>
        caller.PostAsJsonAsync(FindUserByEmailEndpoint(organizationId), new { Email = email });

    // The GET route of the first round, with the email in the query string, kept only to prove it no longer answers
    internal static string LegacyFindUserByEmailEndpoint(Guid organizationId, string email) =>
        $"/organizations/{organizationId}/users?email={Uri.EscapeDataString(email)}";

    internal record MembershipResponse(Guid MembershipId);

    private record CreateOrganizationResponse(Guid OrganizationId);
    private record CreatePolicyResponse(Guid PolicyId);
    private record CreateClearanceResponse(Guid ClearanceId);
}
