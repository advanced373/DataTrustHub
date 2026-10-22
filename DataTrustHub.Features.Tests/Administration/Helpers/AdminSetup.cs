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

    private record CreateOrganizationResponse(Guid OrganizationId);
    private record CreatePolicyResponse(Guid PolicyId);
    private record CreateClearanceResponse(Guid ClearanceId);
}
