using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.ApproveRegistrationRequest;

public record ApproveRegistrationRequestResponse(Guid UserId);

public class ApproveRegistrationRequestEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(
        Guid id,
        ICommandHandler<ApproveRegistrationRequestCommand, Result<Guid>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new ApproveRegistrationRequestCommand(id), cancellationToken);
        return result.ToHttpResult(userId => Results.Ok(new ApproveRegistrationRequestResponse(userId)));
    }
}
