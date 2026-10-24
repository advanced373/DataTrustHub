using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.Administration.FindUserByEmail;

public record FindUserByEmailRequest(string? Email);

// Role is the user's membership role in this organization, or null when they are not in it
public record FindUserByEmailResponse(Guid UserId, string Email, bool IsMember, string? Role);

// Any authenticated user may call; the handler checks that the caller is an organization admin there
public class FindUserByEmailEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        // A POST so the email travels in the body, not in the URL that request logs store
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    // The email is optional here so that a missing one reaches the validator and gets its 400
    private static async Task<IResult> Handle(
        Guid organizationId,
        FindUserByEmailRequest request,
        IQueryHandler<FindUserByEmailQuery, Result<FindUserByEmailResponse>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = ExtractUserId(httpContext.User);
        if (callerId is null) return Results.Unauthorized();

        var query = new FindUserByEmailQuery(organizationId, callerId.Value, request.Email ?? string.Empty);
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
