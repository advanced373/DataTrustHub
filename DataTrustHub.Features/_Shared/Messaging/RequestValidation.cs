using DataTrustHub.SharedKernel;
using FluentValidation;
using FluentValidation.Results;

namespace DataTrustHub.Features._Shared.Messaging;

// Runs the FluentValidation validators of a request and turns failures into the handler's
// result type, so an invalid request never reaches its handler
internal static class RequestValidation
{
    internal static async Task<ValidationFailure[]> ValidateAsync<TRequest>(
        IReadOnlyList<IValidator<TRequest>> validators, TRequest request, CancellationToken cancellationToken)
    {
        if (validators.Count == 0) return [];

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        return results
            .Where(r => !r.IsValid)
            .SelectMany(r => r.Errors)
            .ToArray();
    }

    internal static TResult BuildFailure<TResult>(ValidationFailure[] failures)
    {
        if (IsGenericResult(typeof(TResult)))
            return BuildGenericResultFailure<TResult>(failures);

        if (typeof(TResult) == typeof(Result))
            return (TResult)(object)Result.Failure(CreateValidationError(failures));

        throw new ValidationException(failures);
    }

    private static bool IsGenericResult(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>);

    private static TResult BuildGenericResultFailure<TResult>(ValidationFailure[] failures)
    {
        var valueType = typeof(TResult).GetGenericArguments()[0];
        var failureMethod = typeof(Result<>)
            .MakeGenericType(valueType)
            .GetMethod(nameof(Result<object>.ValidationFailure));

        if (failureMethod is not null)
            return (TResult)failureMethod.Invoke(null, [CreateValidationError(failures)])!;

        throw new InvalidOperationException("Could not invoke ValidationFailure method.");
    }

    private static ValidationError CreateValidationError(ValidationFailure[] failures) =>
        new(failures.Select(f => Error.Problem(f.ErrorCode, f.ErrorMessage)).ToArray());
}
