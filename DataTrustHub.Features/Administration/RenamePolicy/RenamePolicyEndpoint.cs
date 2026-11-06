using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.RenamePolicy;

public record RenamePolicyRequest(string Name);

// Any authenticated user may call; the handler checks that the caller is an organization admin there
public class RenamePolicyEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPut(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        Guid organizationId,
        Guid policyId,
        RenamePolicyRequest request,
        ICommandHandler<RenamePolicyCommand, Result<Guid>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = httpContext.User.GetUserId();
        if (callerId is null) return Results.Unauthorized();

        var command = new RenamePolicyCommand(organizationId, callerId.Value, policyId, request.Name);
        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(_ => Results.NoContent());
    }
}
