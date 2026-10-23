using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.Administration.LeaveOrganization;

// Any authenticated user may call; the handler ends the caller's own membership, whatever its role
public class LeaveOrganizationEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapDelete(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        Guid organizationId,
        ICommandHandler<LeaveOrganizationCommand, Result<Guid>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = ExtractUserId(httpContext.User);
        if (callerId is null) return Results.Unauthorized();

        var command = new LeaveOrganizationCommand(organizationId, callerId.Value);
        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(_ => Results.NoContent());
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
