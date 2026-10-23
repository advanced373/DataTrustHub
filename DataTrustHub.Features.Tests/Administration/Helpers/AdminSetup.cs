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
    internal const string ClearancesEndpoint = "/admin/clearances";

    // The platform admin's policy routes before #95 step 3a, kept only to prove they no longer exist
    internal const string LegacyPoliciesEndpoint = "/admin/policies";

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

    internal static Task<HttpResponseMessage> PostClearanceAsync(
        HttpClient admin, Guid userId, Guid policyId, string level = "Secret", int priority = 2) =>
        admin.PostAsJsonAsync(ClearancesEndpoint, new
        {
            UserId = userId,
            PolicyId = policyId,
            Name = $"{level} clearance",
            ClassificationLevelName = level,
            ClassificationLevelPriority = priority
        });

    internal static async Task<Guid> CreateClearanceAsync(HttpClient admin, Guid userId, Guid policyId)
    {
        var response = await PostClearanceAsync(admin, userId, policyId);
        return (await response.Content.ReadFromJsonAsync<CreateClearanceResponse>())!.ClearanceId;
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

    internal record MembershipResponse(Guid MembershipId);

    private record CreateOrganizationResponse(Guid OrganizationId);
    private record CreatePolicyResponse(Guid PolicyId);
    private record CreateClearanceResponse(Guid ClearanceId);
}
