using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Features._Shared.RateLimiting;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Administration.FindUserByEmail;

public record FindUserByEmailRequest(string? Email);

// Role is the user's membership role in this organization, or null when they are not in it
public record FindUserByEmailResponse(Guid UserId, string Email, bool IsMember, string? Role);

// Any authenticated user may call; the handler checks that the caller is an organization admin there.
// Rate limited per caller, because an answer can confirm that a platform account exists (ADR-0025)
public class FindUserByEmailEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        // A POST so the email travels in the body, not in the URL that request logs store
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization()
           .RequireRateLimiting(RateLimitPolicies.UserLookup);

    // The email is optional here so that a missing one reaches the validator and gets its 400
    private static async Task<IResult> Handle(
        Guid organizationId,
        FindUserByEmailRequest request,
        IQueryHandler<FindUserByEmailQuery, Result<FindUserByEmailResponse>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerId = httpContext.User.GetUserId();
        if (callerId is null) return Results.Unauthorized();

        var query = new FindUserByEmailQuery(organizationId, callerId.Value, request.Email ?? string.Empty);
        var result = await handler.Handle(query, cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }
}
