using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.CreateOrganization;

public record CreateOrganizationRequest(string Name);
public record CreateOrganizationResponse(Guid OrganizationId);

public class CreateOrganizationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole("Admin"));

    private static async Task<IResult> Handle(
        CreateOrganizationRequest request,
        ICommandHandler<CreateOrganizationCommand, Result<Guid>> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateOrganizationCommand(request.Name);
        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(organizationId => Results.Ok(new CreateOrganizationResponse(organizationId)));
    }
}
