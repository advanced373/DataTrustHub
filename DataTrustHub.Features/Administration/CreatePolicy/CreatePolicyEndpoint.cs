using Carter;
using DataTrustHub.Features._Shared.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.CreatePolicy;

public record CreatePolicyRequest(string Name, Guid OrganizationId);
public record CreatePolicyResponse(Guid PolicyId);

public class CreatePolicyEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole("Admin"));

    private static async Task<IResult> Handle(
        CreatePolicyRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreatePolicyCommand(request.Name, request.OrganizationId);
        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(policyId => Results.Ok(new CreatePolicyResponse(policyId)));
    }
}
