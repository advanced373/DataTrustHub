using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.CreateGroup;

public record CreateGroupRequest(string Name);
public record CreateGroupResponse(Guid GroupId);

public class CreateGroupEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole("Admin"));

    private static async Task<IResult> Handle(
        CreateGroupRequest request,
        ICommandHandler<CreateGroupCommand, Result<Guid>> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateGroupCommand(request.Name);
        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(groupId => Results.Ok(new CreateGroupResponse(groupId)));
    }
}
