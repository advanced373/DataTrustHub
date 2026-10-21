using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListClearances;

public record ClearanceSummary(
    Guid Id,
    string Name,
    Guid UserId,
    Guid PolicyId,
    string ClassificationLevelName,
    int ClassificationLevelPriority);

public class ListClearancesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    // userId and policyId are optional filters
    private static async Task<IResult> Handle(
        Guid? userId,
        Guid? policyId,
        IQueryHandler<ListClearancesQuery, Result<IReadOnlyList<ClearanceSummary>>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListClearancesQuery(userId, policyId), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
