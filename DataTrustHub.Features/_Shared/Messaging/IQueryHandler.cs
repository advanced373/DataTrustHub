namespace DataTrustHub.Features._Shared.Messaging;

// Handles a query: a use case that only reads. Same shape as ICommandHandler, kept separate
// so decorators can treat reads and writes differently (e.g. audit writes only).
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
