using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.CreateClearance;

public record CreateClearanceRequest(
    Guid UserId,
    Guid PolicyId,
    string Name,
    string ClassificationLevelName,
    int ClassificationLevelPriority);

public record CreateClearanceResponse(Guid ClearanceId);

public class CreateClearanceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(
        CreateClearanceRequest request,
        ICommandHandler<CreateClearanceCommand, Result<Guid>> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateClearanceCommand(
            request.UserId,
            request.PolicyId,
            request.Name,
            request.ClassificationLevelName,
            request.ClassificationLevelPriority);

        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(clearanceId => Results.Ok(new CreateClearanceResponse(clearanceId)));
    }
}
