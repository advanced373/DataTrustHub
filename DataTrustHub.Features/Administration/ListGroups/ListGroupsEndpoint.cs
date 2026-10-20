using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListGroups;

public record GroupSummary(Guid Id, string Name);

public class ListGroupsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListGroupsQuery(), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
