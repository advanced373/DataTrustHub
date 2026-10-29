using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.Administration.AppointOrganizationAdmin;

public record AppointOrganizationAdminRequest(Guid UserId);

public record AppointOrganizationAdminResponse(Guid MembershipId);

public class AppointOrganizationAdminEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization(policy => policy.RequireRole(UserRoles.Admin));

    private static async Task<IResult> Handle(
        Guid organizationId,
        AppointOrganizationAdminRequest request,
        ICommandHandler<AppointOrganizationAdminCommand, Result<Guid>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = ExtractUserId(httpContext.User);
        if (callerId is null) return Results.Unauthorized();

        var command = new AppointOrganizationAdminCommand(organizationId, callerId.Value, request.UserId);
        var result = await handler.Handle(command, cancellationToken);
        return result.ToHttpResult(membershipId => Results.Ok(new AppointOrganizationAdminResponse(membershipId)));
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
