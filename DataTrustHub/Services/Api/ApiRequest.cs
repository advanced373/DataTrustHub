using System.Net;
using System.Text.Json;

namespace DataTrustHub.Services.Api;

// Sends a request to the API and translates the HTTP response into an ApiResult
internal static class ApiRequest
{
    internal static async Task<ApiResult<T>> SendAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send(cancellationToken);
            return await ReadAsync<T>(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Failure(Messages.ApiUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiResult<T>.Failure(Messages.ApiUnavailable);
        }
    }

    private static async Task<ApiResult<T>> ReadAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return await ReadValueAsync<T>(response, cancellationToken);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ApiResult<T>.Unauthorized(),
            HttpStatusCode.Forbidden => ApiResult<T>.Failure(Messages.Forbidden),
            _ => await ReadFailureAsync<T>(response, cancellationToken)
        };
    }

    private static async Task<ApiResult<T>> ReadFailureAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var error = await TryReadJsonAsync<ApiError>(response, cancellationToken);
        var message = error?.Description ?? error?.Detail ?? Messages.RequestFailed;
        var details = error?.Errors?
            .Select(validationError => validationError.Description)
            .OfType<string>()
            .ToArray();

        return ApiResult<T>.Failure(message, details);
    }

    private static async Task<ApiResult<T>> ReadValueAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var value = await TryReadJsonAsync<T>(response, cancellationToken);
        return value is null
            ? ApiResult<T>.Failure(Messages.UnexpectedResponse)
            : ApiResult<T>.Success(value);
    }

    private static async Task<T?> TryReadJsonAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return default;
        }
    }
}
