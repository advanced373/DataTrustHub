namespace DataTrustHub.Services.Api;

public sealed record ApiResult<T>
{
    public T? Value { get; private init; }

    public string? ErrorMessage { get; private init; }

    // Individual validation messages returned by the API, if any
    public IReadOnlyList<string> Details { get; private init; } = [];

    public bool IsUnauthorized { get; private init; }

    public bool IsSuccess => ErrorMessage is null && !IsUnauthorized;

    public static ApiResult<T> Success(T value) => new() { Value = value };

    public static ApiResult<T> Failure(string message, IReadOnlyList<string>? details = null) =>
        new() { ErrorMessage = message, Details = details ?? [] };

    public static ApiResult<T> Unauthorized() =>
        new() { ErrorMessage = Messages.SessionExpired, IsUnauthorized = true };

    public ApiResult<TOut> Map<TOut>(Func<T, TOut> map)
    {
        if (IsSuccess) return ApiResult<TOut>.Success(map(Value!));

        return IsUnauthorized
            ? ApiResult<TOut>.Unauthorized()
            : ApiResult<TOut>.Failure(ErrorMessage!, Details);
    }
}
