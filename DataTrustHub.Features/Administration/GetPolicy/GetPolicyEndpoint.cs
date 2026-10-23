using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.Administration.GetPolicy;

public record GetPolicyResponse(Guid Id, string Name, Guid OrganizationId);

// Any authenticated user may call; the handler checks that the caller is an organization admin there
public class GetPolicyEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        Guid organizationId,
        Guid policyId,
        IQueryHandler<GetPolicyQuery, Result<GetPolicyResponse>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = ExtractUserId(httpContext.User);
        if (callerId is null) return Results.Unauthorized();

        var query = new GetPolicyQuery(organizationId, callerId.Value, policyId);
        var result = await handler.Handle(query, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
