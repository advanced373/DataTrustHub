using Carter;
using DataTrustHub.Features._Shared.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.DataManagement.ListClassificationOptions;

// A policy the requester may classify under, and the highest level they may use on it
public record ClassificationPolicyOption(Guid PolicyId, string PolicyName, string GroupName, int MaxPriority);

public record ClassificationOptionsResponse(
    IReadOnlyList<ClassificationLevel> Levels,
    IReadOnlyList<ClassificationPolicyOption> Policies);

public class ListClassificationOptionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = ExtractUserId(httpContext.User);
        if (userId is null) return Results.Unauthorized();

        var result = await sender.Send(new ListClassificationOptionsQuery(userId.Value), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
