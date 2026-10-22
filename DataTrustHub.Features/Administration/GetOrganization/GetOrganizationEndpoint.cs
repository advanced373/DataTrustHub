using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.GetOrganization;

public record GetOrganizationResponse(Guid Id, string Name);

public class GetOrganizationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(Guid id, IQueryHandler<GetOrganizationQuery, Result<GetOrganizationResponse>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new GetOrganizationQuery(id), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
