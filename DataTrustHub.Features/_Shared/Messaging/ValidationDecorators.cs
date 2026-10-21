using FluentValidation;

namespace DataTrustHub.Features._Shared.Messaging;

// Wraps every command handler: validates the command first, and calls the handler only when it is valid
internal sealed class CommandValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult>
{
    private readonly IReadOnlyList<IValidator<TCommand>> _validators = validators.ToList();

    public async Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var failures = await RequestValidation.ValidateAsync(_validators, command, cancellationToken);
        return failures.Length == 0
            ? await inner.Handle(command, cancellationToken)
            : RequestValidation.BuildFailure<TResult>(failures);
    }
}

// Same as CommandValidationDecorator, for query handlers
internal sealed class QueryValidationDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    IEnumerable<IValidator<TQuery>> validators)
    : IQueryHandler<TQuery, TResult>
{
    private readonly IReadOnlyList<IValidator<TQuery>> _validators = validators.ToList();

    public async Task<TResult> Handle(TQuery query, CancellationToken cancellationToken)
    {
        var failures = await RequestValidation.ValidateAsync(_validators, query, cancellationToken);
        return failures.Length == 0
            ? await inner.Handle(query, cancellationToken)
            : RequestValidation.BuildFailure<TResult>(failures);
    }
}
