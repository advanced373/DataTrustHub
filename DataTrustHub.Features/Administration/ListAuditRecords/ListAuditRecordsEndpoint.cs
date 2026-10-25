using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListAuditRecords;

// OccurredAt is in UTC; ActorEmail is null for an anonymous action or an account that no longer exists
public record AuditRecordSummary(
    Guid Id,
    DateTime OccurredAt,
    Guid? ActorUserId,
    string? ActorEmail,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string Outcome,
    string? ErrorCode);

public class ListAuditRecordsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    // userId, action, from and to are optional filters; ids only in the query string, never an email (ADR-0019)
    private static async Task<IResult> Handle(
        Guid? userId,
        string? action,
        DateTime? from,
        DateTime? to,
        IQueryHandler<ListAuditRecordsQuery, Result<IReadOnlyList<AuditRecordSummary>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListAuditRecordsQuery(userId, action, from, to), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
