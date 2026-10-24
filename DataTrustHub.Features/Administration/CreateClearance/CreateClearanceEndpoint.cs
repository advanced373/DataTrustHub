using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.Administration.CreateClearance;

public record CreateClearanceRequest(
    Guid UserId,
    Guid PolicyId,
    string Name,
    string ClassificationLevelName,
    int ClassificationLevelPriority);

public record CreateClearanceResponse(Guid ClearanceId);

// Any authenticated user may call; the handler checks that the caller is an organization admin there
public class CreateClearanceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        Guid organizationId,
        CreateClearanceRequest request,
        ICommandHandler<CreateClearanceCommand, Result<Guid>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = ExtractUserId(httpContext.User);
        if (callerId is null) return Results.Unauthorized();

        var command = new CreateClearanceCommand(
            organizationId,
            callerId.Value,
            request.UserId,
            request.PolicyId,
            request.Name,
            request.ClassificationLevelName,
            request.ClassificationLevelPriority);

        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(clearanceId => Results.Ok(new CreateClearanceResponse(clearanceId)));
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
