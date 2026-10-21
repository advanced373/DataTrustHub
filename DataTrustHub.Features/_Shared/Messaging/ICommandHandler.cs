namespace DataTrustHub.Features._Shared.Messaging;

// Handles a command: a use case that changes state. Endpoints inject the handler they call directly;
// cross-cutting behaviour (validation, later auditing) wraps it as decorators (ADR-0012).
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}
