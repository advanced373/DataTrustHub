namespace DataTrustHub.Services.Api;

public interface IAdminApiClient
{
    Task<ApiResult<Guid>> CreateAccountAsync(string email, string password, CancellationToken cancellationToken);

    Task<ApiResult<IReadOnlyList<AccountResponse>>> ListAccountsAsync(CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> CreatePolicyAsync(string name, Guid organizationId, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> CreateGroupAsync(string name, CancellationToken cancellationToken);

    Task<ApiResult<IReadOnlyList<GroupResponse>>> ListGroupsAsync(CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken);

    // Without a group id every policy is listed
    Task<ApiResult<IReadOnlyList<PolicyResponse>>> ListPoliciesAsync(Guid? groupId, CancellationToken cancellationToken);

    Task<ApiResult<bool>> DeletePolicyAsync(Guid policyId, CancellationToken cancellationToken);

    // Without a user id every clearance is listed
    Task<ApiResult<IReadOnlyList<ClearanceResponse>>> ListClearancesAsync(Guid? userId, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> GrantClearanceAsync(ClearanceGrant grant, CancellationToken cancellationToken);

    Task<ApiResult<bool>> RevokeClearanceAsync(Guid clearanceId, CancellationToken cancellationToken);
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

    public async Task<ApiResult<Guid>> CreatePolicyAsync(
        string name, Guid organizationId, CancellationToken cancellationToken)
    {
        var result = await PostAsync<CreatePolicyRequest, PolicyCreatedResponse>(
            ApiRoutes.AdminPolicies, new CreatePolicyRequest(name, organizationId), cancellationToken);

        return result.Map(response => response.PolicyId);
    }

    public async Task<ApiResult<Guid>> CreateGroupAsync(string name, CancellationToken cancellationToken)
    {
        var result = await PostAsync<CreateGroupRequest, GroupCreatedResponse>(
            ApiRoutes.AdminGroups, new CreateGroupRequest(name), cancellationToken);

        return result.Map(response => response.GroupId);
    }

    public Task<ApiResult<IReadOnlyList<AccountResponse>>> ListAccountsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<AccountResponse>>(ApiRoutes.AdminAccounts, cancellationToken);

    public Task<ApiResult<bool>> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.AdminAccountById, accountId), cancellationToken);

    public Task<ApiResult<IReadOnlyList<GroupResponse>>> ListGroupsAsync(CancellationToken cancellationToken) =>
        GetAsync<IReadOnlyList<GroupResponse>>(ApiRoutes.AdminGroups, cancellationToken);

    public Task<ApiResult<bool>> DeleteGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.AdminGroupById, groupId), cancellationToken);

    public Task<ApiResult<IReadOnlyList<PolicyResponse>>> ListPoliciesAsync(
        Guid? groupId, CancellationToken cancellationToken)
    {
        var route = groupId is null
            ? ApiRoutes.AdminPolicies
            : string.Format(ApiRoutes.AdminPoliciesByGroup, groupId);
        return GetAsync<IReadOnlyList<PolicyResponse>>(route, cancellationToken);
    }

    public Task<ApiResult<bool>> DeletePolicyAsync(Guid policyId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.AdminPolicyById, policyId), cancellationToken);

    public Task<ApiResult<IReadOnlyList<ClearanceResponse>>> ListClearancesAsync(
        Guid? userId, CancellationToken cancellationToken)
    {
        var route = userId is null
            ? ApiRoutes.AdminClearances
            : string.Format(ApiRoutes.AdminClearancesByUser, userId);
        return GetAsync<IReadOnlyList<ClearanceResponse>>(route, cancellationToken);
    }

    public async Task<ApiResult<Guid>> GrantClearanceAsync(ClearanceGrant grant, CancellationToken cancellationToken)
    {
        var request = new CreateClearanceRequest(
            grant.UserId, grant.PolicyId, grant.Name, grant.LevelName, grant.LevelPriority);
        var result = await PostAsync<CreateClearanceRequest, ClearanceCreatedResponse>(
            ApiRoutes.AdminClearances, request, cancellationToken);

        return result.Map(response => response.ClearanceId);
    }

    public Task<ApiResult<bool>> RevokeClearanceAsync(Guid clearanceId, CancellationToken cancellationToken) =>
        DeleteAsync(string.Format(ApiRoutes.AdminClearanceById, clearanceId), cancellationToken);

    private Task<ApiResult<TResponse>> GetAsync<TResponse>(string route, CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<TResponse>(token => httpClient.GetAsync(route, token), cancellationToken);

    private Task<ApiResult<bool>> DeleteAsync(string route, CancellationToken cancellationToken) =>
        ApiRequest.SendWithoutContentAsync(token => httpClient.DeleteAsync(route, token), cancellationToken);

    private Task<ApiResult<TResponse>> PostAsync<TRequest, TResponse>(
        string route, TRequest request, CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<TResponse>(
            token => httpClient.PostAsJsonAsync(route, request, token),
            cancellationToken);
}
