using Carter;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.DataManagement.GetDataItem;

public record GetDataItemResponse(
    Guid Id,
    string Name,
    long Size,
    string SecurityMarking,
    string? Content,
    int? ClassificationPriority,
    string? PolicyName,
    bool IsOwner);

public class GetDataItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        Guid id,
        IQueryHandler<GetDataItemQuery, Result<GetDataItemResponse>> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var requesterId = ExtractUserId(httpContext.User);
        if (requesterId is null) return Results.Unauthorized();

        var result = await handler.Handle(new GetDataItemQuery(id, requesterId.Value), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
