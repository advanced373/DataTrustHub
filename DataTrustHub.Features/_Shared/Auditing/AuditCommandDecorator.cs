using DataTrustHub.Domain.Auditing;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features._Shared.Auditing;

// The outermost decorator of every command handler (ADR-0016): it records each command in the audit trail,
// also when validation or a domain rule rejects it, or when it throws. The record is appended after the
// command, through IAuditLog's own save, so it survives a rolled-back unit of work
internal sealed class AuditCommandDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IAuditLog auditLog,
    AuditScope auditScope,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditCommandDecorator<TCommand, TResult>> logger)
    : ICommandHandler<TCommand, TResult>
    where TResult : Result
{
    private static readonly string Action = ToAction(typeof(TCommand).Name);

    public async Task<TResult> Handle(TCommand command, CancellationToken cancellationToken)
    {
        TResult result;
        try
        {
            result = await inner.Handle(command, cancellationToken);
        }
        catch (Exception exception)
        {
            await AppendSafelyAsync(command, AuditOutcome.Failed, exception.GetType().Name);
            throw;
        }

        var outcome = result.IsSuccess ? AuditOutcome.Succeeded : AuditOutcome.Rejected;
        await AppendSafelyAsync(command, outcome, result.IsSuccess ? null : result.Error.Code);
        return result;
    }

    // A failed audit write is logged and never changes the command's response or hides its exception.
    // It does not use the request's token: a cancelled request still leaves its record
    private async Task AppendSafelyAsync(TCommand command, AuditOutcome outcome, string? errorCode)
    {
        try
        {
            await auditLog.AppendAsync(BuildRecord(command, outcome, errorCode), CancellationToken.None);
        }
        catch (Exception exception)
        {
            // The exception itself is not logged: its message may quote the record's values
            logger.LogError(AuditConstants.AuditWriteFailedMessage, Action, outcome, exception.GetType().Name);
        }
    }

    // What the handler put in the audit scope wins over what the command declares
    private AuditRecord BuildRecord(TCommand command, AuditOutcome outcome, string? errorCode)
    {
        var target = auditScope.Target ?? (command as ITargetedCommand)?.Target;
        var organizationId = auditScope.OrganizationId ?? (command as IOrganizationScopedCommand)?.OrganizationId;

        return AuditRecord.Create(
            DateTime.UtcNow, FindActorUserId(), Action, target?.Type, target?.Id, organizationId, outcome, errorCode);
    }

    // The authenticated user's sub claim; null when anonymous (register, log in)
    private Guid? FindActorUserId()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    private static string ToAction(string commandTypeName) =>
        commandTypeName.EndsWith(AuditConstants.CommandSuffix, StringComparison.Ordinal)
            ? commandTypeName[..^AuditConstants.CommandSuffix.Length]
            : commandTypeName;
}
