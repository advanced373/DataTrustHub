using System.Net.Http.Json;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

/// <summary>
/// Creates the group, policy and clearance prerequisites through the admin API.
/// </summary>
internal static class AdminSetup
{
    internal const string GroupsEndpoint = "/admin/groups";
    internal const string PoliciesEndpoint = "/admin/policies";
    internal const string ClearancesEndpoint = "/admin/clearances";

    internal static async Task<Guid> CreatePolicyAsync(HttpClient admin, string name)
    {
        var group = await admin.PostAsJsonAsync(GroupsEndpoint, new { Name = $"{name} group" });
        var groupId = (await group.Content.ReadFromJsonAsync<CreateGroupResponse>())!.GroupId;

        var policy = await admin.PostAsJsonAsync(PoliciesEndpoint, new { Name = name, OrganizationId = groupId });
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

    private record CreateGroupResponse(Guid GroupId);
    private record CreatePolicyResponse(Guid PolicyId);
    private record CreateClearanceResponse(Guid ClearanceId);
}
