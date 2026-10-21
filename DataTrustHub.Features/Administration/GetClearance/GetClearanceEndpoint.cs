using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.GetClearance;

public record GetClearanceResponse(
    Guid Id,
    string Name,
    Guid UserId,
    Guid PolicyId,
    string ClassificationLevelName,
    int ClassificationLevelPriority);

public class GetClearanceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(Guid id, IQueryHandler<GetClearanceQuery, Result<GetClearanceResponse>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new GetClearanceQuery(id), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
