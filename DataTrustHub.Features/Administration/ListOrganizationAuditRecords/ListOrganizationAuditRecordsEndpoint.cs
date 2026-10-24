using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.Administration.ListOrganizationAuditRecords;

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

// Any authenticated user may call; the handler checks that the caller is an organization admin there
public class ListOrganizationAuditRecordsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    // userId, action, from and to are optional filters; ids only in the query string, never an email (ADR-0019)
    private static async Task<IResult> Handle(
        Guid organizationId,
        Guid? userId,
        string? action,
        DateTime? from,
        DateTime? to,
        IQueryHandler<ListOrganizationAuditRecordsQuery, Result<IReadOnlyList<AuditRecordSummary>>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = ExtractUserId(httpContext.User);
        if (callerId is null) return Results.Unauthorized();

        var query = new ListOrganizationAuditRecordsQuery(organizationId, callerId.Value, userId, action, from, to);
        var result = await handler.Handle(query, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
