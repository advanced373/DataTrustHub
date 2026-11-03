using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features._Shared.Auditing;

// The outermost decorator of every query handler (ADR-0025): a query that opts in with IAuditedQuery is
// recorded like a command (see AuditDecorator); any other query just runs and leaves no record
internal sealed class AuditQueryDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    IAuditLog auditLog,
    AuditScope auditScope,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditQueryDecorator<TQuery, TResult>> logger)
    : AuditDecorator<TQuery, TResult>(auditLog, auditScope, httpContextAccessor, logger, Action),
      IQueryHandler<TQuery, TResult>
    where TResult : Result
{
    private static readonly string Action = ToAction(typeof(TQuery).Name, AuditConstants.QuerySuffix);
    private static readonly bool IsAudited = typeof(IAuditedQuery).IsAssignableFrom(typeof(TQuery));

    public Task<TResult> Handle(TQuery query, CancellationToken cancellationToken) =>
        IsAudited
            ? HandleAuditedAsync(query, () => inner.Handle(query, cancellationToken))
            : inner.Handle(query, cancellationToken);

    // A query has no declared target: the handler sets it in the audit scope once it knows it
    protected override AuditTarget? DeclaredTarget(TQuery query) => null;

    protected override Guid? DeclaredOrganizationId(TQuery query) =>
        (query as IOrganizationScopedQuery)?.OrganizationId;
}
