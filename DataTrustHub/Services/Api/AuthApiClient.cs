namespace DataTrustHub.Services.Api;

public interface IAuthApiClient
{
    Task<ApiResult<string>> LoginAsync(string email, string password, CancellationToken cancellationToken);

    Task<ApiResult<Guid>> RegisterAsync(string email, string password, CancellationToken cancellationToken);
}

internal sealed class AuthApiClient(HttpClient httpClient) : IAuthApiClient
{
    public async Task<ApiResult<string>> LoginAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var request = new CredentialsRequest(email, password);
        var result = await ApiRequest.SendAsync<LoginResponse>(
            token => httpClient.PostAsJsonAsync(ApiRoutes.Login, request, token),
            cancellationToken);

        // On the login endpoint 401 means wrong credentials, not an expired session
        return result.IsUnauthorized
            ? ApiResult<string>.Failure(Messages.InvalidCredentials)
            : result.Map(response => response.Token);
    }

    public async Task<ApiResult<Guid>> RegisterAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        var request = new CredentialsRequest(email, password);
        var result = await ApiRequest.SendAsync<UserCreatedResponse>(
            token => httpClient.PostAsJsonAsync(ApiRoutes.Register, request, token),
            cancellationToken);

        return result.Map(response => response.UserId);
    }
}
