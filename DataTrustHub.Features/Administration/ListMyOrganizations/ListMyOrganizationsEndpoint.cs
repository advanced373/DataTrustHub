using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListMyOrganizations;

public record MyOrganizationSummary(Guid OrganizationId, string OrganizationName, Guid MembershipId, string Role);

// Any authenticated user may call; the answer holds only the caller's own memberships
public class ListMyOrganizationsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        IQueryHandler<ListMyOrganizationsQuery, Result<IReadOnlyList<MyOrganizationSummary>>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = httpContext.User.GetUserId();
        if (callerId is null) return Results.Unauthorized();

        var result = await handler.Handle(new ListMyOrganizationsQuery(callerId.Value), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
