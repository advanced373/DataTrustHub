using System.Net.Http.Json;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

/// <summary>
/// Creates the organization, policy and clearance prerequisites through the admin API.
/// </summary>
internal static class AdminSetup
{
    internal const string OrganizationsEndpoint = "/admin/organizations";
    internal const string PoliciesEndpoint = "/admin/policies";
    internal const string ClearancesEndpoint = "/admin/clearances";

    internal static async Task<Guid> CreatePolicyAsync(HttpClient admin, string name) =>
        await CreatePolicyAsync(admin, name, await CreateOrganizationAsync(admin, $"{name} organization"));

    internal static async Task<Guid> CreateOrganizationAsync(HttpClient admin, string name)
    {
        var organization = await admin.PostAsJsonAsync(OrganizationsEndpoint, new { Name = name });
        return (await organization.Content.ReadFromJsonAsync<CreateOrganizationResponse>())!.OrganizationId;
    }

    internal static async Task<Guid> CreatePolicyAsync(HttpClient admin, string name, Guid organizationId)
    {
        var policy = await admin.PostAsJsonAsync(PoliciesEndpoint, new { Name = name, OrganizationId = organizationId });
        return (await policy.Content.ReadFromJsonAsync<CreatePolicyResponse>())!.PolicyId;
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
