namespace DataTrustHub.Services.Api;

public interface IAuthApiClient
{
    Task<ApiResult<string>> LoginAsync(string email, string password, CancellationToken cancellationToken);

    // Register creates a registration request, not an account; the value is the request's id (ADR-0026)
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
        var result = await ApiRequest.SendAsync<RegistrationRequestCreatedResponse>(
            token => httpClient.PostAsJsonAsync(ApiRoutes.Register, request, token),
            cancellationToken);

        // Register is limited per client address; 409 and 503 carry the API's own description
        return result.IsTooManyRequests
            ? ApiResult<Guid>.Failure(Messages.TooManyRegistrationAttempts)
            : result.Map(response => response.RegistrationRequestId);
    }
}
