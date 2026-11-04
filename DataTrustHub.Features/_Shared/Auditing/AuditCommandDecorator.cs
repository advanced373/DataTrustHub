using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features._Shared.Auditing;

// The outermost decorator of every command handler (ADR-0016): it records each command in the audit trail,
// also when validation or a domain rule rejects it, or when it throws (see AuditDecorator)
internal sealed class AuditCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IAuditLog auditLog,
    AuditScope auditScope,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditCommandDecorator<TCommand, TResult>> logger)
    : AuditDecorator<TCommand, TResult>(auditLog, auditScope, httpContextAccessor, logger, Action),
      ICommandHandler<TCommand, TResult>
    where TResult : Result
{
    private static readonly string Action = ToAction(typeof(TCommand).Name, AuditConstants.CommandSuffix);

    public Task<TResult> Handle(TCommand command, CancellationToken cancellationToken) =>
        HandleAuditedAsync(command, () => inner.Handle(command, cancellationToken));

    protected override AuditTarget? DeclaredTarget(TCommand command) => (command as ITargetedCommand)?.Target;

    protected override Guid? DeclaredOrganizationId(TCommand command) =>
        (command as IOrganizationScopedCommand)?.OrganizationId;
}
