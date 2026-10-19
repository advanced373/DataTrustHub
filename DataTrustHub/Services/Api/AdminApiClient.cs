namespace DataTrustHub.Services.Api;

public interface IAdminApiClient
{
    Task<ApiResult<Guid>> CreateAccountAsync(string email, string password, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> CreatePolicyAsync(string name, Guid organizationId, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> CreateGroupAsync(string name, CancellationToken cancellationToken);
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

    private Task<ApiResult<TResponse>> PostAsync<TRequest, TResponse>(
        string route, TRequest request, CancellationToken cancellationToken) =>
        ApiRequest.SendAsync<TResponse>(
            token => httpClient.PostAsJsonAsync(route, request, token),
            cancellationToken);
}
