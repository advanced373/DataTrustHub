using DataTrustHub.SharedKernel;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using System.Reflection;

namespace DataTrustHub.Features._Shared.Behaviors;

internal sealed class ValidationPipelineBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    private readonly IReadOnlyList<IValidator<TRequest>> _validators = validators.ToList();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var failures = await ValidateAsync(request, cancellationToken);
        if (failures.Length == 0)
            return await next(cancellationToken);

        return BuildFailureResponse(failures);
    }

    private TResponse BuildFailureResponse(ValidationFailure[] failures)
    {
        if (IsGenericResult(typeof(TResponse)))
            return BuildGenericResultFailure(failures);

        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(CreateValidationError(failures));

        throw new ValidationException(failures);
    }

    private static bool IsGenericResult(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>);

    private static TResponse BuildGenericResultFailure(ValidationFailure[] failures)
    {
        var resultType = typeof(TResponse).GetGenericArguments()[0];
        var failureMethod = typeof(Result<>)
            .MakeGenericType(resultType)
            .GetMethod(nameof(Result<object>.ValidationFailure));

        if (failureMethod is not null)
            return (TResponse)failureMethod.Invoke(null, [CreateValidationError(failures)])!;

        throw new InvalidOperationException("Could not invoke ValidationFailure method.");
    }

    private static ValidationError CreateValidationError(ValidationFailure[] failures) =>
        new(failures.Select(f => Error.Problem(f.ErrorCode, f.ErrorMessage)).ToArray());

    private async Task<ValidationFailure[]> ValidateAsync(
        TRequest request, CancellationToken cancellationToken)
    {
        if (!_validators.Any()) return [];

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        return results
            .Where(r => !r.IsValid)
            .SelectMany(r => r.Errors)
            .ToArray();
    }
}
