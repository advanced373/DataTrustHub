using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListOrganizations;

public record OrganizationSummary(Guid Id, string Name);

public class ListOrganizationsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(IQueryHandler<ListOrganizationsQuery, Result<IReadOnlyList<OrganizationSummary>>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ListOrganizationsQuery(), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
