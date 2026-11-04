using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListRegistrationRequests;

// RequestedAt and ExpiresAt are in UTC; the password hash is never returned
public record RegistrationRequestSummary(Guid Id, string Email, DateTime RequestedAt, DateTime ExpiresAt);

// Unpaged, like ListAccounts: MaxPendingRequests bounds the list (ADR-0026)
public class ListRegistrationRequestsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(
        IQueryHandler<ListRegistrationRequestsQuery, Result<IReadOnlyList<RegistrationRequestSummary>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListRegistrationRequestsQuery(), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
