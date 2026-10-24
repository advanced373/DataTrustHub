using Microsoft.AspNetCore.WebUtilities;

namespace DataTrustHub.Services.Api;

public interface IAdminApiClient
{
    Task<ApiResult<Guid>> CreateAccountAsync(string email, string password, CancellationToken cancellationToken);

    Task<ApiResult<IReadOnlyList<AccountResponse>>> ListAccountsAsync(CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> CreateOrganizationAsync(string name, CancellationToken cancellationToken);

    Task<ApiResult<IReadOnlyList<OrganizationResponse>>> ListOrganizationsAsync(CancellationToken cancellationToken);

    Task<ApiResult<OrganizationResponse>> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeleteOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    // The platform admin appoints an organization's first organization admin
    Task<ApiResult<Guid>> AppointOrganizationAdminAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    // Organization-scoped calls: any signed-in user may make them, the API checks the caller's membership

    Task<ApiResult<IReadOnlyList<MyOrganizationResponse>>> ListMyOrganizationsAsync(CancellationToken cancellationToken);

    Task<ApiResult<bool>> LeaveOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<ApiResult<IReadOnlyList<MemberResponse>>> ListMembersAsync(Guid organizationId, CancellationToken cancellationToken);

    // The email travels in the request body, never in the URL (ADR-0019)
    Task<ApiResult<UserByEmailResponse>> FindUserByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> AddMemberAsync(Guid organizationId, Guid userId, string role, CancellationToken cancellationToken);

    Task<ApiResult<bool>> ChangeMemberRoleAsync(Guid organizationId, Guid userId, string role, CancellationToken cancellationToken);

    Task<ApiResult<bool>> RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);

    Task<ApiResult<IReadOnlyList<PolicyResponse>>> ListPoliciesAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> CreatePolicyAsync(Guid organizationId, string name, CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeletePolicyAsync(Guid organizationId, Guid policyId, CancellationToken cancellationToken);

    // userId and policyId optionally narrow the list; only ids go in the query string (ADR-0019)
    Task<ApiResult<IReadOnlyList<ClearanceResponse>>> ListClearancesAsync(
        Guid organizationId, Guid? userId, Guid? policyId, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> GrantClearanceAsync(Guid organizationId, ClearanceGrant grant, CancellationToken cancellationToken);

    Task<ApiResult<bool>> RevokeClearanceAsync(Guid organizationId, Guid clearanceId, CancellationToken cancellationToken);
}

internal sealed class AdminApiClient(HttpClient httpClient) : IAdminApiClient
{
    public async Task<ApiResult<Guid>> CreateAccountAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var result = await PostAsync<CredentialsRequest, UserCreatedResponse>(
            ApiRoutes.AdminAccounts, new CredentialsRequest(email, password), cancellationToken);

        return result.Map(response => response.UserId);
    }

    public async Task<ApiResult<Guid>> CreateOrganizationAsync(string name, CancellationToken cancellationToken)
    {
        var result = await PostAsync<CreateOrganizationRequest, OrganizationCreatedResponse>(
            ApiRoutes.AdminOrganizations, new CreateOrganizationRequest(name), cancellationToken);

        return result.Map(response => response.OrganizationId);
    }

    public Task<ApiResult<IReadOnlyList<AccountResponse>>> ListAccountsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AccountResponse>>(ApiRoutes.AdminAccounts, cancellationToken);

    public Task<ApiResult<bool>> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.AdminAccountById, accountId), cancellationToken);

    public Task<ApiResult<IReadOnlyList<OrganizationResponse>>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<OrganizationResponse>>(ApiRoutes.AdminOrganizations, cancellationToken);

    public Task<ApiResult<OrganizationResponse>> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        GetAsync<OrganizationResponse>(string.Format(ApiRoutes.AdminOrganizationById, organizationId), cancellationToken);

    public Task<ApiResult<bool>> DeleteOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.AdminOrganizationById, organizationId), cancellationToken);

    public async Task<ApiResult<Guid>> AppointOrganizationAdminAsync(
        Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var result = await PostAsync<AppointOrganizationAdminRequest, MembershipCreatedResponse>(
            string.Format(ApiRoutes.AdminOrganizationAdmins, organizationId),
            new AppointOrganizationAdminRequest(userId),
            cancellationToken);

        return result.Map(response => response.MembershipId);
    }

    // Organization-scoped calls (ADR-0014): the API answers 404 Organization.NotFound to a caller without
    // the needed membership in the organization

    public Task<ApiResult<IReadOnlyList<MyOrganizationResponse>>> ListMyOrganizationsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<MyOrganizationResponse>>(ApiRoutes.MyOrganizations, cancellationToken);

    public Task<ApiResult<bool>> LeaveOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.MyMembership, organizationId), cancellationToken);

    public Task<ApiResult<IReadOnlyList<MemberResponse>>> ListMembersAsync(
        Guid organizationId, CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<MemberResponse>>(string.Format(ApiRoutes.OrganizationMembers, organizationId), cancellationToken);

    public Task<ApiResult<UserByEmailResponse>> FindUserByEmailAsync(
        Guid organizationId, string email, CancellationToken cancellationToken) =>
        PostAsync<FindUserByEmailRequest, UserByEmailResponse>(
            string.Format(ApiRoutes.OrganizationFindUserByEmail, organizationId),
            new FindUserByEmailRequest(email),
            cancellationToken);

    public async Task<ApiResult<Guid>> AddMemberAsync(
        Guid organizationId, Guid userId, string role, CancellationToken cancellationToken)
    {
        var result = await PostAsync<AddMemberRequest, MembershipCreatedResponse>(
            string.Format(ApiRoutes.OrganizationMembers, organizationId),
            new AddMemberRequest(userId, role),
            cancellationToken);

        return result.Map(response => response.MembershipId);
    }

    public Task<ApiResult<bool>> ChangeMemberRoleAsync(
        Guid organizationId, Guid userId, string role, CancellationToken cancellationToken) =>
        PutAsync(
            string.Format(ApiRoutes.OrganizationMemberRole, organizationId, userId),
            new ChangeMemberRoleRequest(role),
            cancellationToken);

    public Task<ApiResult<bool>> RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.OrganizationMember, organizationId, userId), cancellationToken);

    public Task<ApiResult<IReadOnlyList<PolicyResponse>>> ListPoliciesAsync(
        Guid organizationId, CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<PolicyResponse>>(string.Format(ApiRoutes.OrganizationPolicies, organizationId), cancellationToken);

    public async Task<ApiResult<Guid>> CreatePolicyAsync(
        Guid organizationId, string name, CancellationToken cancellationToken)
    {
        var result = await PostAsync<CreatePolicyRequest, PolicyCreatedResponse>(
            string.Format(ApiRoutes.OrganizationPolicies, organizationId),
            new CreatePolicyRequest(name),
            cancellationToken);

        return result.Map(response => response.PolicyId);
    }

    public Task<ApiResult<bool>> DeletePolicyAsync(Guid organizationId, Guid policyId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.OrganizationPolicy, organizationId, policyId), cancellationToken);

    public Task<ApiResult<IReadOnlyList<ClearanceResponse>>> ListClearancesAsync(
        Guid organizationId, Guid? userId, Guid? policyId, CancellationToken cancellationToken)
    {
        var filters = new Dictionary<string, string?>
        {
            [ApiRoutes.UserIdParameter] = userId?.ToString(),
            [ApiRoutes.PolicyIdParameter] = policyId?.ToString()
        };
        var route = QueryHelpers.AddQueryString(
            string.Format(ApiRoutes.OrganizationClearances, organizationId),
            filters.Where(filter => filter.Value is not null));

        return GetAsync<IReadOnlyList<ClearanceResponse>>(route, cancellationToken);
    }

    public async Task<ApiResult<Guid>> GrantClearanceAsync(
        Guid organizationId, ClearanceGrant grant, CancellationToken cancellationToken)
    {
        var request = new CreateClearanceRequest(
            grant.UserId, grant.PolicyId, grant.Name, grant.LevelName, grant.LevelPriority);
        var result = await PostAsync<CreateClearanceRequest, ClearanceCreatedResponse>(
            string.Format(ApiRoutes.OrganizationClearances, organizationId), request, cancellationToken);

        return result.Map(response => response.ClearanceId);
    }

    public Task<ApiResult<bool>> RevokeClearanceAsync(Guid organizationId, Guid clearanceId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.OrganizationClearance, organizationId, clearanceId), cancellationToken);

    private Task<ApiResult<TResponse>> GetAsync<TResponse>(string route, CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<TResponse>(token => httpClient.GetAsync(route, token), cancellationToken);

    private Task<ApiResult<bool>> DeleteAsync(string route, CancellationToken cancellationToken) =>
        ApiRequest.SendWithoutContentAsync(token => httpClient.DeleteAsync(route, token), cancellationToken);

    private Task<ApiResult<TResponse>> PostAsync<TRequest, TResponse>(
        string route, TRequest request, CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<TResponse>(
            token => httpClient.PostAsJsonAsync(route, request, token),
            cancellationToken);

    // For endpoints that answer 204 No Content, such as a role change
    private Task<ApiResult<bool>> PutAsync<TRequest>(
        string route, TRequest request, CancellationToken cancellationToken) =>
        ApiRequest.SendWithoutContentAsync(
            token => httpClient.PutAsJsonAsync(route, request, token),
            cancellationToken);
}
