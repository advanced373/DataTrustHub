using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataTrustHub.Features._Shared.Auditing;

// What the command and the query audit decorators share (ADR-0016, ADR-0025): run the handler, then append
// one audit record with its outcome, also when validation or a domain rule rejects the request, or when it
// throws. The record is appended through IAuditLog's own save, so it survives a rolled-back unit of work
internal abstract class AuditDecorator<TRequest, TResult>(
    IAuditLog auditLog,
    AuditScope auditScope,
    IHttpContextAccessor httpContextAccessor,
    ILogger logger,
    string action)
    where TResult : Result
{
    // What the request itself declares; what the handler puts in the audit scope wins over it
    protected abstract AuditTarget? DeclaredTarget(TRequest request);

    protected abstract Guid? DeclaredOrganizationId(TRequest request);

    protected async Task<TResult> HandleAuditedAsync(TRequest request, Func<Task<TResult>> handle)
    {
        TResult result;
        try
        {
            result = await handle();
        }
        catch (Exception exception)
        {
            await AppendSafelyAsync(request, AuditOutcome.Failed, exception.GetType().Name);
            throw;
        }

        var outcome = result.IsSuccess ? AuditOutcome.Succeeded : AuditOutcome.Rejected;
        await AppendSafelyAsync(request, outcome, result.IsSuccess ? null : result.Error.Code);
        return result;
    }

    // The request type name without its suffix: DeleteDataItemCommand -> DeleteDataItem
    protected static string ToAction(string requestTypeName, string suffix) =>
        requestTypeName.EndsWith(suffix, StringComparison.Ordinal)
            ? requestTypeName[..^suffix.Length]
            : requestTypeName;

    // A failed audit write is logged and never changes the response or hides the handler's exception.
    // It does not use the request's token: a cancelled request still leaves its record
    private async Task AppendSafelyAsync(TRequest request, AuditOutcome outcome, string? errorCode)
    {
        try
        {
            await auditLog.AppendAsync(BuildRecord(request, outcome, errorCode), CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The exception itself is not logged: its message may quote the record's values
            logger.LogError(AuditConstants.AuditWriteFailedMessage, action, outcome, exception.GetType().Name);
        }
    }

    private AuditRecord BuildRecord(TRequest request, AuditOutcome outcome, string? errorCode)
    {
        var target = auditScope.Target ?? DeclaredTarget(request);
        var organizationId = auditScope.OrganizationId ?? DeclaredOrganizationId(request);

        return AuditRecord.Create(
            DateTime.UtcNow, FindActorUserId(), action, target?.Type, target?.Id, organizationId, outcome, errorCode);
    }

    // The authenticated user's sub claim; null when anonymous (register, log in)
    private Guid? FindActorUserId()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        return user.GetUserId();
    }
}
