using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ListPolicies;

public record PolicySummary(Guid Id, string Name, Guid OrganizationId);

public class ListPoliciesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    // organizationId is optional: without it every policy is listed
    private static async Task<IResult> Handle(
        Guid? organizationId,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListPoliciesQuery(organizationId), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
